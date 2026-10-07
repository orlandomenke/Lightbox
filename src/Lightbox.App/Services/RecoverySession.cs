using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Services;

/// <summary>One open document, as the recovery copy sees it.</summary>
/// <param name="Key">Stable for the life of the tab; names its copy on disk.</param>
/// <param name="Title">What the artist will be shown when it is offered back.</param>
/// <param name="OriginalPath">The file it came from, if any — shown, never written.</param>
/// <param name="Revision">Moves whenever the document does; a copy is rewritten only when it has.</param>
/// <param name="Dirty">False once it is saved, which is what retires its copy.</param>
/// <param name="Doc">Read on the UI thread, at the moment of the write.</param>
public sealed record RecoverySource(
    string Key, string Title, string? OriginalPath, long Revision, bool Dirty, Func<Doc> Doc);

/// <summary>A copy a session that is no longer running left behind.</summary>
public sealed record RecoverableDocument(
    string SessionDir, string Key, string Title, string? OriginalPath, DateTime WrittenAt)
{
    internal string DocPath => Path.Combine(SessionDir, Key + RecoverySession.DocSuffix);
    internal string MetaPath => Path.Combine(SessionDir, Key + RecoverySession.MetaSuffix);
}

/// <summary>
/// The recovery copies of this run: one per open document with unsaved work,
/// in a folder no other run writes to (B394, Q208).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a folder per run, held by a lock.</b> On 2026-10-07 a crash killed a
/// session with work unsaved. Autosave had that work on disk — in one shared
/// file, for one tab — and the relaunch twenty minutes later wrote over it
/// before anyone knew to look. Every rule here closes part of that:
/// </para>
/// <list type="bullet">
/// <item>Each run writes only into its own folder, so no later run, and no
/// second instance running alongside, can overwrite what a dead one left.</item>
/// <item>The run keeps a lock file in that folder open for its whole life. The
/// operating system releases it however the process ends — a native crash
/// included, which is the kind the app's own crash reporter never sees — so
/// "can I take this lock" is exactly "is that run still alive".</item>
/// <item>A copy is deleted only when its document is saved or closed, and the
/// folder only on a clean exit. Anything else is left for the next launch to
/// offer back.</item>
/// </list>
/// <para>
/// Owned and driven by the UI thread; only the serializing and the disk write
/// happen on a worker, on a private clone, as autosave already did (B187).
/// </para>
/// </remarks>
public sealed class RecoverySession : IDisposable
{
    internal const string DocSuffix = ".lightbox.json";
    internal const string MetaSuffix = ".recovery.json";
    private const string LockName = "session.lock";

    /// <summary>
    /// Marks the root of the recovery folders on disk, so <see cref="IsInside"/>
    /// recognises them by what is there rather than by how the path is spelled.
    /// </summary>
    private const string MarkerName = "lightbox-recovery.marker";

    /// <summary>Where every run's folder lives. Overridable so tests never touch the real one.</summary>
    /// <remarks>
    /// Local, not roaming (the owner's choice, B394): these are full copies of
    /// unsaved work, often under NDA, and a roaming profile would sync every one
    /// to the organisation's server at logoff. A recovery copy belongs to the
    /// machine that crashed.
    /// </remarks>
    public static string DefaultRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lightbox", "recovery");

    /// <summary>
    /// Whether a path is inside the recovery folders — somewhere no document
    /// should be opened as itself or saved to, because Discard, Restore and a
    /// clean exit all delete or move what is there.
    /// </summary>
    /// <remarks>
    /// Decided by what is on disk as well as by the spelling: an 8.3 short name
    /// (this owner's own profile has one), a junction or a <c>subst</c> drive all
    /// spell the folder differently, and every one of them still finds the
    /// marker at the root, or a run's lock file under it, on the way up.
    /// </remarks>
    public static bool IsInside(string path)
    {
        try
        {
            var root = Path.GetFullPath(DefaultRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
                 dir is not null; dir = dir.Parent)
            {
                if (!dir.Exists) continue;
                if (File.Exists(Path.Combine(dir.FullName, MarkerName))) return true;
                if (File.Exists(Path.Combine(dir.FullName, LockName))
                    && dir.Parent is { } parent && File.Exists(Path.Combine(parent.FullName, MarkerName)))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// The running application's session, or null. Set by the app at startup
    /// and nowhere else, so the thousands of view models the test suite builds
    /// never leave folders in a real profile for the next launch to offer.
    /// </summary>
    public static RecoverySession? Current { get; set; }

    private readonly FileStream _lock;
    private readonly Dictionary<string, long> _written = [];
    private Task<List<(string Key, long Revision)>> _write = Task.FromResult(new List<(string, long)>());
    private bool _ended;

    /// <summary>This run's folder.</summary>
    public string Dir { get; }

    private RecoverySession(string dir, FileStream held)
    {
        Dir = dir;
        _lock = held;
    }

    /// <summary>Open a fresh folder for this run and hold its lock.</summary>
    public static RecoverySession Start(string? root = null)
    {
        root ??= DefaultRoot;
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, MarkerName);
        if (!File.Exists(marker))
        {
            File.WriteAllText(marker, "Lightbox crash-recovery copies. Emptied automatically - do not save work here.");
        }
        var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..6]}";
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        var held = new FileStream(
            Path.Combine(dir, LockName), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        return new RecoverySession(dir, held);
    }

    /// <summary>The background write in flight, for anything that must see the disk quiet.</summary>
    public Task PendingWrite => _write;

    /// <summary>
    /// Bring the folder in line with the open documents: write a copy of each
    /// dirty one that changed since its last copy, and delete the copy of any
    /// that is now saved or no longer open.
    /// </summary>
    /// <remarks>
    /// Deletions happen now, on the calling thread, because they are what a save
    /// or a close is waiting on — a copy surviving its document's save would be
    /// offered back over the newer file after the next crash. Writes are
    /// snapshotted now and written on a worker; if the previous write is still
    /// going, the changed documents are simply left for the next call, which is
    /// the autosave rule that keeps two writers off one file.
    /// </remarks>
    public void Sync(IReadOnlyList<RecoverySource> sources)
    {
        if (_ended) return;
        if (_write.IsCompleted) TakeFinishedWrites();
        foreach (var key in _written.Keys.ToList())
        {
            var source = sources.FirstOrDefault(s => s.Key == key);
            if (source is null || !source.Dirty) Forget(key);
        }

        if (!_write.IsCompleted) return;
        TakeFinishedWrites();
        var batch = new List<(string Key, Doc Snapshot, Meta Meta, long Revision)>();
        foreach (var s in sources)
        {
            if (!s.Dirty) continue;
            if (_written.TryGetValue(s.Key, out var r) && r == s.Revision) continue;
            Doc snapshot;
            try
            {
                snapshot = s.Doc().Clone();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            batch.Add((s.Key, snapshot, new Meta(s.Title, s.OriginalPath, DateTime.Now), s.Revision));
        }
        if (batch.Count == 0) return;

        // Recorded as written only once the write has landed (TakeFinishedWrites):
        // a write that failed — a full disk, a scanner holding the file — is
        // tried again on the next call rather than believed (B394's adversarial
        // pass found the first draft marking it done before it had happened).
        var dir = Dir;
        _write = Task.Run(() =>
        {
            var done = new List<(string Key, long Revision)>(batch.Count);
            foreach (var b in batch)
            {
                try
                {
                    // Document first, description second: a description is the
                    // claim that a copy exists, so it must never point at one
                    // that was not finished.
                    DocJson.Save(b.Snapshot, Path.Combine(dir, b.Key + DocSuffix));
                    DocJson.WriteAtomic(
                        Path.Combine(dir, b.Key + MetaSuffix), JsonSerializer.Serialize(b.Meta));
                    done.Add((b.Key, b.Revision));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
            return done;
        });
    }

    /// <summary>Record the copies the last background write actually landed.</summary>
    private void TakeFinishedWrites()
    {
        if (!_write.IsCompletedSuccessfully) return;
        foreach (var (key, revision) in _write.Result) _written[key] = revision;
        _write = Task.FromResult(new List<(string, long)>());
    }

    /// <summary>Delete one document's copy — it was saved, closed or discarded.</summary>
    public void Forget(string key)
    {
        FinishPendingWrite();
        TakeFinishedWrites();
        _written.Remove(key);
        TryDelete(Path.Combine(Dir, key + MetaSuffix));
        TryDelete(Path.Combine(Dir, key + DocSuffix));
    }

    /// <summary>
    /// Take a dead run's copy into this one, under <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// A move rather than a load-and-forget, so there is no moment at which the
    /// work exists only in memory: until the restored document is saved, its
    /// copy is this run's, and a second crash offers it again.
    /// </remarks>
    public void Adopt(RecoverableDocument copy, string key, long revision)
    {
        FinishPendingWrite();
        File.Move(copy.DocPath, Path.Combine(Dir, key + DocSuffix), overwrite: true);
        if (File.Exists(copy.MetaPath))
        {
            File.Move(copy.MetaPath, Path.Combine(Dir, key + MetaSuffix), overwrite: true);
        }
        _written[key] = revision;
        RemoveIfEmpty(copy.SessionDir);
    }

    /// <summary>Block until the background write is done.</summary>
    public void FinishPendingWrite()
    {
        try
        {
            _write.Wait();
        }
        catch (AggregateException)
        {
        }
    }

    /// <summary>
    /// The window closed through the unsaved-work prompt, or with nothing
    /// unsaved: every document was saved or deliberately discarded.
    /// </summary>
    /// <remarks>
    /// The only thing that licenses <see cref="End"/> to delete. An exit that
    /// did not come through that prompt — an operating-system logoff, an
    /// exception unwinding the application — leaves the copies for the next
    /// launch, which is the whole point of having them (B394).
    /// </remarks>
    public bool CleanExit { get; set; }

    /// <summary>
    /// The application is exiting: delete this run's copies if the exit was
    /// clean, and otherwise only let go of the lock, as a crash would.
    /// </summary>
    public void Exit()
    {
        if (CleanExit) End();
        else Dispose();
    }

    /// <summary>
    /// A clean exit: every document was saved or deliberately discarded, so
    /// nothing in the folder is owed to anyone.
    /// </summary>
    public void End()
    {
        if (_ended) return;
        _ended = true;
        FinishPendingWrite();
        // Everything this run could have written — copies, descriptions, and the
        // temp files a write that failed half-way leaves — so nothing survives to
        // be offered back as "Unnamed document" after a clean exit. Nothing else
        // belongs in here: Save As and Export refuse this folder (IsInside).
        foreach (var pattern in new[] { "*" + MetaSuffix, "*" + DocSuffix, "*.tmp" })
        {
            foreach (var file in Directory.EnumerateFiles(Dir, pattern).ToList()) TryDelete(file);
        }
        // The lock last, so a launch starting this instant sees a live run until
        // its copies are gone, not a dead one whose copies are being deleted.
        _lock.Dispose();
        RemoveIfEmpty(Dir);
    }

    /// <summary>
    /// Release the lock without deleting anything — what a crash does. For tests;
    /// the application ends with <see cref="End"/>.
    /// </summary>
    public void Dispose()
    {
        if (_ended) return;
        _ended = true;
        FinishPendingWrite();
        _lock.Dispose();
    }

    // ---- what dead runs left ----------------------------------------------------------

    /// <summary>
    /// Every copy left by a run that is no longer running, newest first.
    /// </summary>
    /// <remarks>
    /// A folder whose lock this process can take belongs to a dead run. One it
    /// cannot take belongs to a live one — another instance — and is left
    /// alone. A dead run's folder with no copies in it (it crashed clean, or
    /// before anything was dirty) is deleted on the way past.
    /// </remarks>
    public static List<RecoverableDocument> FindLeftBehind(string? root = null, string? exceptDir = null)
    {
        root ??= DefaultRoot;
        var found = new List<RecoverableDocument>();
        if (!Directory.Exists(root)) return found;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                CollectFrom(dir, exceptDir, found);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Another instance restored or discarded it while this was
                // looking. Whatever is left will be seen next time.
            }
        }
        found.Sort((a, b) => b.WrittenAt.CompareTo(a.WrittenAt));
        return found;
    }

    private static void CollectFrom(string dir, string? exceptDir, List<RecoverableDocument> found)
    {
        {
            if (exceptDir is not null && SamePath(dir, exceptDir)) return;
            if (!IsDead(dir)) return;
            var any = false;
            foreach (var metaPath in Directory.EnumerateFiles(dir, "*" + MetaSuffix))
            {
                var key = Path.GetFileName(metaPath)[..^MetaSuffix.Length];
                if (!File.Exists(Path.Combine(dir, key + DocSuffix))) continue;
                try
                {
                    var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(metaPath));
                    found.Add(new RecoverableDocument(
                        dir, key, meta?.Title ?? "Unnamed document", meta?.OriginalPath,
                        meta?.WrittenAt ?? File.GetLastWriteTime(Path.Combine(dir, key + DocSuffix))));
                    any = true;
                }
                catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
                {
                    // A description that cannot be read is still a document on
                    // disk; leave the folder for a person rather than delete it.
                    any = true;
                }
            }
            // A document whose description was never written — the run died
            // between the two writes — is still somebody's work. Offered under
            // a plain name rather than skipped, which would also have left the
            // folder on disk for ever.
            foreach (var docPath in Directory.EnumerateFiles(dir, "*" + DocSuffix))
            {
                var key = Path.GetFileName(docPath)[..^DocSuffix.Length];
                if (File.Exists(Path.Combine(dir, key + MetaSuffix))) continue;
                found.Add(new RecoverableDocument(
                    dir, key, "Unnamed document", null, File.GetLastWriteTime(docPath)));
                any = true;
            }
            if (!any) RemoveIfEmpty(dir);
        }
    }

    /// <summary>Read a left-behind copy.</summary>
    public static Doc Load(RecoverableDocument copy) => DocJson.Load(copy.DocPath);

    /// <summary>Delete a left-behind copy for good — the artist said discard.</summary>
    public static void Discard(RecoverableDocument copy)
    {
        TryDelete(copy.MetaPath);
        TryDelete(copy.DocPath);
        RemoveIfEmpty(copy.SessionDir);
    }

    private static bool IsDead(string dir)
    {
        var lockPath = Path.Combine(dir, LockName);
        // A folder with no lock yet may be a run that is starting this instant —
        // Start makes the folder, then the lock — and sweeping it would leave
        // that run with nowhere to keep its copies. Old enough, it is not.
        if (!File.Exists(lockPath))
        {
            return DateTime.Now - Directory.GetCreationTime(dir) > TimeSpan.FromMinutes(2);
        }
        try
        {
            using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Delete a dead run's folder once nothing but its lock is left in it.</summary>
    /// <remarks>
    /// Never recursive: anything in the folder that this class did not put
    /// there — a subfolder, a file saved by hand — keeps the folder alive.
    /// </remarks>
    private static void RemoveIfEmpty(string dir)
    {
        try
        {
            if (Directory.EnumerateFileSystemEntries(dir)
                .Any(f => !Path.GetFileName(f).Equals(LockName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            TryDelete(Path.Combine(dir, LockName));
            Directory.Delete(dir, recursive: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    private sealed record Meta(string Title, string? OriginalPath, DateTime WrittenAt);
}
