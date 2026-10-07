using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// B394 / Q208: a crash costs at most one autosave interval of work, for every
/// open document, and the next launch can give it back.
/// </summary>
/// <remarks>
/// The loss this closes, 2026-10-07: autosave had the owner's work on disk in
/// one shared file, for one tab; the relaunch wrote over it twenty minutes
/// later, and nothing ever offered it back. A "crash" here is
/// <see cref="RecoverySession.Dispose"/> — the lock released, nothing deleted —
/// which is exactly what the operating system does to a process that dies.
/// </remarks>
public sealed class CrashRecoveryTests : BrushStateIsolated
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightbox-recovery-{Guid.NewGuid():N}");

    public override void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        base.Dispose();
    }

    private static Doc DocWithStrokes(int count)
    {
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        var frame = doc.Scene.Layers[0].Cels[0].Frame!;
        for (var i = 0; i < count; i++)
        {
            frame.Strokes.Add(new Stroke { Points = [new StrokePoint(i, i * 2, 0.5)] });
        }
        return doc;
    }

    private static int StrokesIn(Doc doc) => doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Count;

    private static RecoverySource Source(string key, Doc doc, long revision = 1, bool dirty = true, string? path = null) =>
        new(key, key, path, revision, dirty, () => doc);

    private static void Written(RecoverySession session) =>
        Assert.True(session.PendingWrite.Wait(WriteTimeout), "background write never finished");

    /// <summary>The failure of 2026-10-07, step for step.</summary>
    [Fact]
    public void ANewSessionNeverWritesOverWhatADeadOneLeft()
    {
        var crashed = RecoverySession.Start(_root);
        crashed.Sync([Source("protagonist", DocWithStrokes(7), path: @"C:\work\protagonist.lightbox.json")]);
        Written(crashed);
        crashed.Dispose(); // the crash

        // The relaunch, editing a document of its own and autosaving it.
        using var relaunch = RecoverySession.Start(_root);
        relaunch.Sync([Source("protagonist", DocWithStrokes(1))]);
        Written(relaunch);

        var left = RecoverySession.FindLeftBehind(_root, exceptDir: relaunch.Dir);
        var copy = Assert.Single(left);
        Assert.Equal("protagonist", copy.Title);
        Assert.Equal(@"C:\work\protagonist.lightbox.json", copy.OriginalPath);
        Assert.Equal(7, StrokesIn(RecoverySession.Load(copy)));
    }

    /// <summary>Every open document with unsaved work, not just the one being saved.</summary>
    [Fact]
    public void EveryDirtyDocumentGetsItsOwnCopy()
    {
        var session = RecoverySession.Start(_root);
        session.Sync([Source("a", DocWithStrokes(2)), Source("b", DocWithStrokes(5)), Source("c", DocWithStrokes(9), dirty: false)]);
        Written(session);
        session.Dispose();

        var left = RecoverySession.FindLeftBehind(_root);
        Assert.Equal(["a", "b"], left.Select(c => c.Title).Order());
        Assert.Equal(5, StrokesIn(RecoverySession.Load(left.Single(c => c.Title == "b"))));
    }

    /// <summary>Another instance that is still running is not a crash.</summary>
    [Fact]
    public void ALiveSessionsCopiesAreNeverOfferedToAnother()
    {
        using var running = RecoverySession.Start(_root);
        running.Sync([Source("a", DocWithStrokes(2))]);
        Written(running);

        Assert.Empty(RecoverySession.FindLeftBehind(_root));
    }

    /// <summary>
    /// A saved document's copy goes at once, and so does a closed one's — a copy
    /// surviving its save would be offered back over the newer file.
    /// </summary>
    [Fact]
    public void SavingOrClosingADocumentRetiresItsCopy()
    {
        var session = RecoverySession.Start(_root);
        var a = DocWithStrokes(2);
        var b = DocWithStrokes(3);
        session.Sync([Source("a", a), Source("b", b)]);
        Written(session);

        session.Sync([Source("a", a, dirty: false)]); // a saved, b closed
        session.Dispose();

        Assert.Empty(RecoverySession.FindLeftBehind(_root));
    }

    /// <summary>A clean exit leaves nothing for the next launch to ask about.</summary>
    [Fact]
    public void ACleanExitLeavesNothingBehind()
    {
        var session = RecoverySession.Start(_root);
        session.Sync([Source("a", DocWithStrokes(2))]);
        Written(session);
        session.End();

        Assert.False(Directory.Exists(session.Dir));
        Assert.Empty(RecoverySession.FindLeftBehind(_root));
    }

    /// <summary>A copy is rewritten when the document moves, and only then.</summary>
    [Fact]
    public void ACopyFollowsTheDocumentsRevision()
    {
        var session = RecoverySession.Start(_root);
        var doc = DocWithStrokes(2);
        session.Sync([Source("a", doc, revision: 1)]);
        Written(session);

        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.Add(new Stroke { Points = [new StrokePoint(1, 1, 1)] });
        session.Sync([Source("a", doc, revision: 1)]); // same revision: not rewritten
        Written(session);
        session.Sync([Source("a", doc, revision: 2)]);
        Written(session);
        session.Dispose();

        Assert.Equal(3, StrokesIn(RecoverySession.Load(Assert.Single(RecoverySession.FindLeftBehind(_root)))));
    }

    /// <summary>
    /// Restoring moves the copy into the running session, so a second crash
    /// before the artist saves offers it again rather than losing it.
    /// </summary>
    [Fact]
    public void ARestoredCopyBelongsToTheSessionThatRestoredIt()
    {
        var crashed = RecoverySession.Start(_root);
        crashed.Sync([Source("a", DocWithStrokes(4))]);
        Written(crashed);
        crashed.Dispose();

        var second = RecoverySession.Start(_root);
        var copy = Assert.Single(RecoverySession.FindLeftBehind(_root, exceptDir: second.Dir));
        second.Adopt(copy, "restored-key", revision: 0);
        Assert.False(Directory.Exists(copy.SessionDir), "the dead session's emptied folder was left behind");

        second.Dispose(); // and it crashes too
        var again = Assert.Single(RecoverySession.FindLeftBehind(_root));
        Assert.Equal(4, StrokesIn(RecoverySession.Load(again)));
    }

    /// <summary>Discard is the one permanent step, and it is permanent.</summary>
    [Fact]
    public void DiscardingACopyDeletesItAndItsFolder()
    {
        var crashed = RecoverySession.Start(_root);
        crashed.Sync([Source("a", DocWithStrokes(1))]);
        Written(crashed);
        crashed.Dispose();

        var copy = Assert.Single(RecoverySession.FindLeftBehind(_root));
        RecoverySession.Discard(copy);

        Assert.Empty(RecoverySession.FindLeftBehind(_root));
        Assert.False(Directory.Exists(copy.SessionDir));
    }

    /// <summary>
    /// With per-document copies running, the single shared file is not written:
    /// it is the slot a relaunch overwrote.
    /// </summary>
    [AvaloniaFact]
    public void AutosaveStopsWritingTheSharedCopyWhenRecoveryIsRunning()
    {
        var shared = Path.Combine(_root, "autosave.lightbox.json");
        Directory.CreateDirectory(_root);
        using var session = RecoverySession.Start(_root);
        var doc = DocWithStrokes(3);
        var service = new AutosaveService(() => doc, TimeSpan.Zero, targetPath: shared)
        {
            Recovery = session,
            RecoverySources = () => [Source("a", doc)],
        };

        service.MarkDirty();
        service.Flush();
        Written(session);
        Assert.True(service.PendingWrite.Wait(WriteTimeout));

        Assert.False(File.Exists(shared), "the shared single-slot copy was still written");
        Assert.True(File.Exists(Path.Combine(session.Dir, "a" + RecoverySession.DocSuffix)));
    }

    /// <summary>
    /// The restored tab: named as recovered, tied to no file, and counted as work
    /// to lose until it is saved — though its editor starts at revision zero.
    /// </summary>
    [AvaloniaFact]
    public void ARestoredDocumentIsUnsavedWorkUntilItIsSaved()
    {
        var crashed = RecoverySession.Start(_root);
        crashed.Sync([new RecoverySource("k", "protagonist", @"C:\work\protagonist.lightbox.json", 1, true, () => DocWithStrokes(6))]);
        Written(crashed);
        crashed.Dispose();

        var vm = new MainViewModel(artist: null);
        var opened = vm.RestoreRecovered(RecoverySession.FindLeftBehind(_root));

        Assert.Equal(1, opened);
        var tab = vm.ActiveTab!;
        Assert.Equal("protagonist (recovered)", tab.Title);
        Assert.Null(tab.FilePath);
        Assert.Equal(@"C:\work\protagonist.lightbox.json", tab.RecoveredFrom);
        Assert.Equal(6, StrokesIn(tab.Doc));
        Assert.True(tab.HasWorkToLose, "closing a restored document would not ask before throwing it away");

        tab.MarkSaved();
        Assert.False(tab.HasWorkToLose);
    }

    // ---- the view model's half: the wiring the first draft left untested ----------------

    private MainViewModel VmWithRecovery(RecoverySession session)
    {
        RecoverySession.Current = session;
        try
        {
            var vm = new MainViewModel(artist: null);
            vm.NewDocument(new NewDocumentSettings("Ink", 320, 180, 12, 72, "#ffffff", false));
            return vm;
        }
        finally
        {
            RecoverySession.Current = null;
        }
    }

    private static void Stroke(MainViewModel vm)
    {
        vm.BeginStroke(20, 20, 1);
        vm.MoveStroke(120, 90, 1);
        vm.EndStroke();
    }

    private static string[] CopiesIn(RecoverySession session) =>
        Directory.GetFiles(session.Dir, "*" + RecoverySession.DocSuffix);

    /// <summary>
    /// A drawn document gets a copy on the tick, and saving it takes the copy
    /// away at once — a copy outliving its save would be offered back over the
    /// newer file after the next crash.
    /// </summary>
    [AvaloniaFact]
    public void ADrawnDocumentGetsACopyAndSavingItRetiresTheCopy()
    {
        using var session = RecoverySession.Start(_root);
        var vm = VmWithRecovery(session);
        Stroke(vm);

        vm.FlushAutosaveForTests();
        Written(session);
        Assert.Single(CopiesIn(session));

        vm.NotifySaved(Path.Combine(_root, "saved.lightbox.json"));
        Assert.Empty(CopiesIn(session));
    }

    [AvaloniaFact]
    public void ClosingADocumentRetiresItsCopy()
    {
        using var session = RecoverySession.Start(_root);
        var vm = VmWithRecovery(session);
        Stroke(vm);
        vm.FlushAutosaveForTests();
        Written(session);
        Assert.Single(CopiesIn(session));

        vm.CloseTab(vm.ActiveTab!);
        Assert.Empty(CopiesIn(session));
    }

    /// <summary>
    /// The camera does not go through the undo stack. Before B394's second pass
    /// a camera-only change left the document clean: no badge, no prompt on
    /// close, and no recovery copy.
    /// </summary>
    [AvaloniaFact]
    public void ACameraEditIsWorkToLoseAndGetsACopy()
    {
        using var session = RecoverySession.Start(_root);
        var vm = VmWithRecovery(session);
        var tab = vm.ActiveTab!;
        Assert.False(tab.HasWorkToLose);

        vm.AddCameraCommand.Execute(null);

        Assert.True(tab.HasWorkToLose, "a camera-only change would close without asking");
        Assert.True(tab.IsDirty);
        vm.FlushAutosaveForTests();
        Written(session);
        Assert.Single(CopiesIn(session));
    }

    /// <summary>
    /// Restoring through the view model moves the copy into the running
    /// session, so a second crash offers it again.
    /// </summary>
    [AvaloniaFact]
    public void RestoringThroughTheAppMovesTheCopyIntoTheRunningSession()
    {
        var crashed = RecoverySession.Start(_root);
        crashed.Sync([Source("a", DocWithStrokes(4))]);
        Written(crashed);
        crashed.Dispose();

        using var session = RecoverySession.Start(_root);
        var vm = VmWithRecovery(session);
        var left = RecoverySession.FindLeftBehind(_root, exceptDir: session.Dir);
        Assert.Equal(1, vm.RestoreRecovered(left));

        Assert.Single(CopiesIn(session));
        Assert.False(Directory.Exists(left[0].SessionDir));
    }

    /// <summary>
    /// Exit deletes the copies only when the window closed through the
    /// unsaved-work prompt; any other way out leaves them for the next launch.
    /// </summary>
    [Fact]
    public void AnExitThatSkippedThePromptKeepsTheCopies()
    {
        var unclean = RecoverySession.Start(_root);
        unclean.Sync([Source("a", DocWithStrokes(2))]);
        Written(unclean);
        unclean.Exit();
        Assert.Single(RecoverySession.FindLeftBehind(_root));

        var clean = RecoverySession.Start(_root);
        clean.Sync([Source("b", DocWithStrokes(2))]);
        Written(clean);
        clean.CleanExit = true;
        clean.Exit();
        Assert.False(Directory.Exists(clean.Dir));
    }

    /// <summary>A write that failed is tried again, not believed.</summary>
    [Fact]
    public void AFailedCopyIsWrittenAgainOnTheNextTick()
    {
        using var session = RecoverySession.Start(_root);
        // A folder where the file should go: the write fails.
        var blocker = Path.Combine(session.Dir, "a" + RecoverySession.DocSuffix);
        Directory.CreateDirectory(blocker);
        var doc = DocWithStrokes(3);
        session.Sync([Source("a", doc)]);
        Written(session);

        Directory.Delete(blocker);
        session.Sync([Source("a", doc)]); // same revision
        Written(session);

        Assert.True(File.Exists(blocker), "the failed copy was recorded as written and never retried");
    }

    /// <summary>
    /// A run that died between writing a document and describing it still
    /// offers the document.
    /// </summary>
    [Fact]
    public void ACopyWithoutItsDescriptionIsStillOffered()
    {
        var crashed = RecoverySession.Start(_root);
        crashed.Sync([Source("a", DocWithStrokes(5))]);
        Written(crashed);
        File.Delete(Path.Combine(crashed.Dir, "a" + RecoverySession.MetaSuffix));
        crashed.Dispose();

        var copy = Assert.Single(RecoverySession.FindLeftBehind(_root));
        Assert.Equal("Unnamed document", copy.Title);
        Assert.Equal(5, StrokesIn(RecoverySession.Load(copy)));
    }

    /// <summary>
    /// The sensitivity review's blocker: a camera-only edit in a project document
    /// must reach the disk on a project save before its recovery copy is let go.
    /// </summary>
    /// <remarks>
    /// The first fix for camera edits made the tab dirty without putting the
    /// document in the project's dirty set. A project save then skipped it, said
    /// "Saved", cleared the badge — and the recovery copy was deleted on that
    /// claim. The reload is the half that catches it: in memory it passes either way.
    /// </remarks>
    [AvaloniaFact]
    public void ACameraEditInAProjectIsWrittenBeforeItsCopyIsLetGo()
    {
        var projectRoot = Path.Combine(_root, "project");
        using var session = RecoverySession.Start(Path.Combine(_root, "copies"));
        RecoverySession.Current = session;
        MainViewModel vm;
        try
        {
            vm = VmLayers.PaperVm();
        }
        finally
        {
            RecoverySession.Current = null;
        }
        vm.NewProject(projectRoot, "Production");
        vm.SaveProject(everything: true);
        var tab = vm.ActiveTab!;
        var source = Assert.IsType<Lightbox.Core.Projects.DocumentRef>(tab.Source);
        Assert.False(tab.HasWorkToLose);

        vm.AddCameraCommand.Execute(null);
        vm.FlushAutosaveForTests();
        Written(session);
        Assert.Single(CopiesIn(session));

        vm.Save();

        var reopened = Lightbox.Core.Projects.ProjectIo.Load(projectRoot);
        var onDisk = Lightbox.Core.Projects.ProjectIo.LoadDocument(
            reopened, reopened.Manifest.Documents.Single(d => d.Id == source.Id))!;
        Assert.NotNull(onDisk.Scene.Camera);
        Assert.False(tab.HasWorkToLose);
        Assert.Empty(CopiesIn(session));
    }

    /// <summary>
    /// Nothing the artist means to keep can be bound to a path inside the
    /// recovery folders, which Discard, Restore and a clean exit all empty.
    /// </summary>
    [AvaloniaFact]
    public void AFileOpenedFromTheRecoveryFolderOpensAsRecoveredNotBoundToIt()
    {
        var previous = RecoverySession.DefaultRoot;
        RecoverySession.DefaultRoot = _root;
        try
        {
            var inside = Path.Combine(_root, "20261007-120000-1-abcdef", "k.lightbox.json");
            Assert.True(RecoverySession.IsInside(inside));
            Assert.False(RecoverySession.IsInside(Path.Combine(Path.GetTempPath(), "elsewhere.lightbox.json")));

            var vm = new MainViewModel(artist: null);
            vm.OpenDocumentTab(DocWithStrokes(2), inside);

            var tab = vm.ActiveTab!;
            Assert.Null(tab.FilePath);
            Assert.True(tab.HasWorkToLose);
            Assert.EndsWith("(recovered)", tab.Title);
        }
        finally
        {
            RecoverySession.DefaultRoot = previous;
        }
    }

    /// <summary>A sweep never deletes what this class did not put in a folder.</summary>
    [Fact]
    public void ADeadFolderHoldingSomethingElseIsLeftAlone()
    {
        var crashed = RecoverySession.Start(_root);
        var keep = Path.Combine(crashed.Dir, "notes");
        Directory.CreateDirectory(keep);
        File.WriteAllText(Path.Combine(keep, "mine.txt"), "not a recovery copy");
        crashed.Dispose();

        Assert.Empty(RecoverySession.FindLeftBehind(_root));
        Assert.True(File.Exists(Path.Combine(keep, "mine.txt")));
    }

    /// <summary>
    /// The recovery folders are recognised by what is on disk, so a path that
    /// spells them differently (an 8.3 name, a junction) is still refused.
    /// </summary>
    [Fact]
    public void TheRecoveryFolderIsRecognisedByItsMarkerNotItsSpelling()
    {
        using var session = RecoverySession.Start(_root);
        var previous = RecoverySession.DefaultRoot;
        // The configured root spelled as somewhere else entirely: only the
        // marker and the lock can say where the copies are.
        RecoverySession.DefaultRoot = Path.Combine(Path.GetTempPath(), $"elsewhere-{Guid.NewGuid():N}");
        try
        {
            Assert.True(RecoverySession.IsInside(Path.Combine(session.Dir, "mine.lightbox.json")));
            Assert.True(RecoverySession.IsInside(Path.Combine(_root, "mine.lightbox.json")));
            Assert.False(RecoverySession.IsInside(Path.Combine(Path.GetTempPath(), "mine.lightbox.json")));
        }
        finally
        {
            RecoverySession.DefaultRoot = previous;
        }
    }

    /// <summary>
    /// A clean exit leaves nothing a later launch could offer back — not even
    /// the temp file of a write that failed half-way.
    /// </summary>
    [Fact]
    public void ACleanExitRemovesStrayTempFilesToo()
    {
        var session = RecoverySession.Start(_root);
        session.Sync([Source("a", DocWithStrokes(2))]);
        Written(session);
        File.WriteAllText(Path.Combine(session.Dir, "b" + RecoverySession.DocSuffix + ".tmp"), "half");
        File.WriteAllText(Path.Combine(session.Dir, "c" + RecoverySession.DocSuffix), "orphan");
        session.CleanExit = true;
        session.Exit();

        Assert.False(Directory.Exists(session.Dir));
        Assert.Empty(RecoverySession.FindLeftBehind(_root));
    }
}
