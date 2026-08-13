using Avalonia.Threading;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Services;

/// <summary>
/// Periodically writes the document to a per-user recovery path so a crash
/// never costs more than one interval of work. Recover by opening the autosave
/// file from the Open dialog.
/// </summary>
/// <remarks>
/// The interval comes from <see cref="AppSettings"/>, and zero turns it off —
/// which is a real answer for someone working on a network drive, not a
/// mistake to guard against.
///
/// It writes to the recovery copy, not to the document's own file, unless
/// <see cref="AppSettings.AutosaveInPlace"/> says otherwise. Silently
/// rewriting the file someone opened takes away the ability to close without
/// saving, and that is an editing move, not an accident.
/// </remarks>
public sealed class AutosaveService
{
    public static string AutosavePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lightbox",
            "autosave.lightbox.json");

    private readonly DispatcherTimer _timer;
    private readonly Func<Doc> _docProvider;
    private readonly Func<string?>? _inPlacePath;
    private readonly string _targetPath;
    private bool _dirty;
    private Task _write = Task.CompletedTask;

    /// <param name="inPlacePath">
    /// Where the document itself lives, or null when it has never been saved.
    /// Consulted only when the setting asks for in-place autosave.
    /// </param>
    /// <param name="targetPath">
    /// Where the recovery copy goes; defaults to <see cref="AutosavePath"/>.
    /// Overridable so tests do not write into the real user's recovery file.
    /// </param>
    public AutosaveService(
        Func<Doc> docProvider,
        TimeSpan? interval = null,
        Func<string?>? inPlacePath = null,
        string? targetPath = null)
    {
        _docProvider = docProvider;
        _inPlacePath = inPlacePath;
        _targetPath = targetPath ?? AutosavePath;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => Flush();
        if (interval != TimeSpan.Zero) _timer.Start();
    }

    /// <summary>
    /// The background write in flight, or a completed task when the disk is
    /// quiet. Anything that must observe the written file waits on this.
    /// </summary>
    public Task PendingWrite => _write;

    /// <summary>
    /// Block until any background write is done. Every UI-thread save of a
    /// document file calls this first.
    /// </summary>
    /// <remarks>
    /// With in-place autosave on, the background write and a manual save
    /// target the same path, and two <see cref="DocJson.Save"/> calls on one
    /// path collide on the temp file — worse, a stale snapshot finishing
    /// late would roll the newer save backwards, which is silent data loss.
    /// The fully synchronous code could not interleave writers; this wait
    /// restores that ordering. A no-op when the disk is quiet, bounded by
    /// one document write when it is not, and safe to call on the UI thread
    /// because the write needs nothing from it.
    /// </remarks>
    public void FinishPendingWrite()
    {
        try
        {
            _write.Wait();
        }
        catch (AggregateException)
        {
            // The write's own failure is its own business — swallowed there
            // for IO, surfaced there for bugs. The caller only needed the
            // disk quiet, and it is.
        }
    }

    /// <summary>
    /// Change the cadence without rebuilding the service — what the settings
    /// screen calls. Zero or null stops it.
    /// </summary>
    public void Reschedule(TimeSpan? interval)
    {
        _timer.Stop();
        if (interval is not { TotalSeconds: > 0 } every) return;
        _timer.Interval = every;
        _timer.Start();
    }

    /// <summary>Whether the recovery copy is also written over the real file.</summary>
    public bool InPlace { get; set; }

    /// <summary>Call whenever the document changes; the next tick persists it.</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>
    /// Persist now if there are unsaved changes: snapshot on the calling
    /// thread, write on a background one. Failures are swallowed — autosave
    /// must never crash the app.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B187: the write used to happen right here, on the UI thread.</b>
    /// Serializing a whole painting is hundreds of milliseconds at the sizes
    /// B30 measures, so every tick of the timer was a hitch that landed
    /// mid-stroke and grew with the document. <see cref="Doc.Clone"/> is
    /// milliseconds even at 5 000 strokes (B142's measurement), so the artist
    /// now pays for the snapshot and never for the serializer or the disk.
    /// </para>
    /// <para>
    /// The background task works on a private clone, so it races nothing the
    /// artist does next. If the previous write is still running when the timer
    /// fires again, the flush keeps the dirty flag and lets a later tick
    /// retry, rather than stacking two writers on one file.
    /// </para>
    /// </remarks>
    public void Flush()
    {
        if (!_dirty || !_write.IsCompleted) return;
        Doc snapshot;
        string? inPlacePath;
        try
        {
            snapshot = _docProvider().Clone();
            inPlacePath = InPlace ? _inPlacePath?.Invoke() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The old, synchronous Flush held the provider inside the same
            // filter; keeping that means the move off-thread cannot turn a
            // swallowed failure into a crash in the timer tick.
            return;
        }
        _dirty = false;
        _write = Task.Run(() =>
        {
            try
            {
                DocJson.Save(snapshot, _targetPath);
                if (inPlacePath is { Length: > 0 } path) DocJson.Save(snapshot, path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        });
    }
}
