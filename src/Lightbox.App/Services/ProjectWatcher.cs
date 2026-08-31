using Avalonia.Threading;

namespace Lightbox.App.Services;

/// <summary>
/// Watches a project folder and asks the docker to re-read itself when what is
/// on disk stops matching what is shown.
/// </summary>
/// <remarks>
/// <para>
/// <b>B61.</b> The docker is built from the manifest once and then held, so a
/// change made outside Lightbox — another program, a file manager, a branch
/// switch — left it asserting files that were gone and silent about files that
/// had appeared. The reporter's own diagnosis is what shaped this: after
/// restarting, the files on disk were correct, so nothing is wrong with what is
/// <em>written</em>. It is a refresh problem.
/// </para>
/// <para>
/// The <c>Missing</c> flag and its tests landed first, and they drove
/// <c>ProjectViewModel.Refresh()</c> by hand. Nothing in the running application
/// ever called it, so the reported behaviour was unchanged — a fix that is real
/// in the tests and absent in the app. This is the half that closes it.
/// </para>
/// <para>
/// <b>Debounced, because a burst is the normal case rather than the edge.</b> A
/// checkout, an unzip or the project's own save fires one event per file and
/// arrives as hundreds within a few milliseconds. Re-reading per event is how a
/// docker becomes a stall, so every event only restarts a short timer and the
/// re-read happens once the disk goes quiet.
/// </para>
/// <para>
/// <b>The seam is <see cref="Notify"/> and <see cref="Flush"/>, deliberately.</b>
/// That is the shape <see cref="AutosaveService"/> already uses — mark, then a
/// timer or a caller flushes — and it is what makes the coalescing testable
/// without a clock or an OS event. Whether hundreds of notifications produce one
/// re-read is the property worth guarding, and it is exactly reproducible;
/// whether inotify delivered something is not.
/// </para>
/// <para>
/// <b>Self-inflicted events are not suppressed, and that is a choice that had to
/// be earned.</b> Saving a project writes files and so trips the watcher. The
/// argument for allowing that is that a re-read costs one <c>File.Exists</c> per
/// manifest row, the debounce collapses a save's whole burst into one of them, and
/// <c>MarkMissing</c> skips rows with unsaved changes — a millisecond, and correct.
/// A suspend/resume pair around every write would instead be a state machine that
/// stays suspended forever the first time an exception skips the resume, which is a
/// silent version of the bug this class exists to fix.
/// </para>
/// <para>
/// <b>That argument was false when it was first written, and the fix is what makes
/// it true.</b> A re-read rebuilt every row from scratch, so it was cheap and not
/// idempotent: it discarded the row objects the docker's own interactions were
/// holding — a rename in progress, an open context menu, a status flyout mid-click.
/// Arming this watcher turned that into something that happened on every save, and
/// an unrelated menu test is what noticed. <c>ProjectRow.Describes</c> and
/// <c>ProjectDockerTests.ARefreshKeepsTheRowsThatStillStandForTheSameThing</c> are
/// the repair; the cost was measured correctly and the side effect was not looked
/// for, which is the mistake worth remembering rather than the fix.
/// </para>
/// </remarks>
public sealed class ProjectWatcher : IDisposable
{
    /// <summary>
    /// Long enough that a checkout collapses into one re-read, short enough that
    /// a person who deleted a file in a file manager sees the docker notice
    /// while they are still looking at it.
    /// </summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly Action _refresh;
    private readonly DispatcherTimer _debounce;

    /// <summary>
    /// The UI thread's dispatcher, captured at construction — construction runs
    /// on the UI thread, and the callbacks below do not (B93).
    /// </summary>
    /// <remarks>
    /// Never the static <c>Dispatcher.UIThread</c> from a callback: between two
    /// headless tests Avalonia nulls its UI-thread binding, and the getter then
    /// CREATES a dispatcher owned by whichever thread asked first — so a stray
    /// callback reading it can steal UI-thread ownership and kill the next
    /// test's setup inside <c>EnsureIsolatedApplication</c>. That race is B93,
    /// reproduced on demand for the first time on 2026-08-31, and posting to a
    /// captured instance is the whole of the fix: a post to a torn-down
    /// dispatcher is swallowed, a read of the reset static is a poisoning.
    /// </remarks>
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;
    private FileSystemWatcher? _watcher;
    private bool _pending;

    public ProjectWatcher(Action refresh, TimeSpan? debounce = null)
    {
        _refresh = refresh;
        _debounce = new DispatcherTimer { Interval = debounce ?? DefaultDebounce };
        _debounce.Tick += (_, _) => Flush();
    }

    /// <summary>The folder being watched, or null when nothing is.</summary>
    public string? Root { get; private set; }

    public bool IsWatching => _watcher is { EnableRaisingEvents: true };

    /// <summary>How many times the docker has actually been asked to re-read.</summary>
    /// <remarks>
    /// The number a coalescing test asserts on. A count rather than a flag,
    /// because "did it refresh" also passes when it refreshed two hundred times.
    /// </remarks>
    public int Refreshes { get; private set; }

    /// <summary>
    /// Point the watcher at a project root. Null, empty or a folder that is not
    /// there stops it.
    /// </summary>
    /// <remarks>
    /// Idempotent on the same root, so the funnel that adopts a project can call
    /// it freely without tearing down and rebuilding an OS handle each time.
    /// </remarks>
    public void Watch(string? root)
    {
        if (string.Equals(root, Root, StringComparison.Ordinal) && IsWatching) return;
        Stop();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        try
        {
            _watcher = new FileSystemWatcher(root)
            {
                // Everything, and every depth: a project is a folder of folders,
                // and a deleted character folder matters as much as a deleted
                // document inside one.
                Filter = "*",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnChanged;
            _watcher.Changed += OnChanged;
            // An overflow means events were dropped, so what is shown cannot be
            // trusted at all — which is the one case where a re-read is not
            // optional. Treated as a notification rather than as an error.
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;
            Root = root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // A watch is an improvement, not a guarantee: a network share or a
            // platform without inotify must leave the docker working exactly as
            // it did, with a manual refresh, rather than failing to open a
            // project. The whole application must not die for a nicety.
            Stop();
        }
    }

    private void OnChanged(object? sender, FileSystemEventArgs e) => Notify();

    private void OnError(object? sender, ErrorEventArgs e) => Notify();

    /// <summary>
    /// Something on disk moved. Coalesced — the re-read happens once the burst
    /// stops.
    /// </summary>
    /// <remarks>
    /// Marshalled to the UI thread because a <see cref="FileSystemWatcher"/>
    /// raises on a thread-pool thread and the refresh it leads to rewrites an
    /// <c>ObservableCollection</c> the docker is bound to. Restarting a
    /// <see cref="DispatcherTimer"/> off-thread is the same violation as touching
    /// the rows would be, only harder to see.
    /// </remarks>
    public void Notify()
    {
        if (_dispatcher.CheckAccess())
        {
            Mark();
            return;
        }
        _dispatcher.Post(Mark, DispatcherPriority.Background);
    }

    private void Mark()
    {
        _pending = true;
        // Restarting rather than starting is the debounce: the clock runs from
        // the last event, so a burst produces one re-read at its end instead of
        // one at its beginning and another for every straggler.
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Re-read now if anything is waiting. What the timer calls.</summary>
    /// <remarks>
    /// Public so a test can close the loop without a clock, and so a manual
    /// refresh and the timer go through one path rather than two that can
    /// disagree — the same reason <c>ProjectViewModel.Refresh</c> is public and
    /// parameterless.
    /// </remarks>
    public void Flush()
    {
        _debounce.Stop();
        if (!_pending) return;
        _pending = false;
        Refreshes++;
        // Never let a refresh take the application down. A watcher exists to
        // report the world; a world it cannot read is a status message.
        try
        {
            _refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Swallowed for the same reason autosave swallows: the next event
            // tries again, and there is nobody to tell at this depth.
        }
    }

    /// <summary>Whether a re-read is waiting for the burst to end.</summary>
    public bool Pending => _pending;

    public void Stop()
    {
        _debounce.Stop();
        _pending = false;
        if (_watcher is { } watcher)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnChanged;
            watcher.Deleted -= OnChanged;
            watcher.Renamed -= OnChanged;
            watcher.Changed -= OnChanged;
            watcher.Error -= OnError;
            watcher.Dispose();
        }
        _watcher = null;
        Root = null;
    }

    public void Dispose() => Stop();
}
