using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// B373: how a multi-frame symbol lands, and who decides.
/// </summary>
/// <remarks>
/// <para>
/// The bug was one line — <c>task.Wait(5000)</c> inside the view model, on a
/// dialog that can only complete on the thread being blocked. It froze the app
/// for the full timeout, placed a default nobody chose, and left the dialog open
/// with its answer discarded. It shipped 2026-08-06 under "All tests passing
/// (2551 tests)", which was true and meant nothing: not one of them went near
/// this path.
/// </para>
/// <para>
/// So these are written against the two things that would have caught it — that
/// placing does not block, and that the answer is the caller's to give.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class SymbolPlacementChoiceTests : BrushStateIsolated
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-placechoice-{Guid.NewGuid():N}.lbproj");

    /// <summary>The artist's own library, redirected so this does not write theirs.</summary>
    private readonly string _store = Path.Combine(
        Path.GetTempPath(), $"lightbox-placechoice-lib-{Guid.NewGuid():N}.json");

    public SymbolPlacementChoiceTests() =>
        Lightbox.App.Services.SymbolLibrary.PathOverride = _store;

    /// <remarks>
    /// <see cref="BrushStateIsolated"/> because the preference is persisted and
    /// process-wide: without it, a test that stores "always import" hands that
    /// answer to every view model constructed afterwards, including ones running
    /// beside it.
    /// </remarks>
    public override void Dispose()
    {
        Lightbox.App.Services.SymbolLibrary.PathOverride = null;
        if (File.Exists(_store)) File.Delete(_store);
        SymbolRegistry.Clear();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private static Stroke Bar(double x, double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 40, y, 1)],
        Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    private static Symbol Prop(string name, int frames) => new()
    {
        Name = name,
        PivotX = 0,
        PivotY = 0,
        Layers = Symbol.Flat(
            name,
            Enumerable.Range(0, frames).Select(i => new Frame { Strokes = [Bar(i, 0)] })),
    };

    private MainViewModel Loaded(out Symbol walk)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings(
            "probe", 640, 360, 12, 72, Scene.DefaultBackgroundColor, false));
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;

        var project = ProjectIo.Create("Knight", _root);
        walk = Prop("walk", frames: 8);
        project.Symbols[walk.Id] = walk;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        vm.PlacementPreference = null;
        return vm;
    }

    /// <summary>
    /// Placing must not block. This is the regression: the old path waited five
    /// seconds on a dialog it had made unanswerable.
    /// </summary>
    [AvaloniaFact]
    public void PlacingAMultiFrameSymbolDoesNotBlockTheThread()
    {
        var vm = Loaded(out var walk);

        var sw = Stopwatch.StartNew();
        var placed = vm.PlaceSymbol(walk.Id, 100, 100);
        sw.Stop();

        Assert.NotNull(placed);
        Assert.True(sw.Elapsed.TotalMilliseconds < 1000,
            $"placing took {sw.Elapsed.TotalMilliseconds:0} ms. The view model must never wait on a "
            + "dialog: it runs on the thread that would answer one, so waiting is a deadlock that "
            + "resolves only by timing out (B373).");
    }

    /// <summary>
    /// Nobody asked, so nothing is imported — the scene keeps its own length.
    /// </summary>
    /// <remarks>
    /// The old code defaulted the other way, and that default was an accident of
    /// a timeout rather than a decision. A caller that never asked — the MCP
    /// surface, a test, an agent — must not silently lengthen the scene.
    /// </remarks>
    [AvaloniaFact]
    public void AnUnansweredPlacementReferencesRatherThanLengtheningTheScene()
    {
        var vm = Loaded(out var walk);
        var before = vm.Doc.Scene.FrameCount;

        var placed = vm.PlaceSymbol(walk.Id, 100, 100);

        Assert.NotNull(placed);
        Assert.Equal(before, vm.Doc.Scene.FrameCount);
    }

    /// <summary>Asked for frames, and frames is what lands.</summary>
    [AvaloniaFact]
    public void ImportFramesLengthensTheSceneToHoldTheSymbol()
    {
        var vm = Loaded(out var walk);

        vm.PlaceSymbol(walk.Id, 100, 100, FrameImportChoice.ImportFrames);

        Assert.True(vm.Doc.Scene.FrameCount >= walk.FrameCount,
            $"asked to import {walk.FrameCount} frames but the scene holds {vm.Doc.Scene.FrameCount}");
    }

    /// <summary>
    /// A single-frame symbol is never a question, so it is never asked about.
    /// </summary>
    [AvaloniaFact]
    public void OneFrameIsNotAQuestion()
    {
        var vm = Loaded(out _);
        var sword = Prop("sword", frames: 1);
        vm.ProjectDocker.Project!.Symbols[sword.Id] = sword;
        SymbolRegistry.Register(sword);

        Assert.False(vm.PlacementNeedsAChoice(sword.Id));
    }

    /// <summary>A multi-frame symbol with nothing stored is a question.</summary>
    [AvaloniaFact]
    public void AMultiFrameSymbolIsAQuestionUntilItIsAnswered()
    {
        var vm = Loaded(out var walk);

        Assert.True(vm.PlacementNeedsAChoice(walk.Id));

        vm.PlacementPreference = FrameImportChoice.Reference;

        Assert.False(vm.PlacementNeedsAChoice(walk.Id));
    }

    /// <summary>
    /// "Don't ask again" has to survive a restart, and it has to be able to fire
    /// at all — it never once did.
    /// </summary>
    /// <remarks>
    /// The old field was assigned only inside <c>if (task.Wait(5000))</c>, the
    /// branch the deadlock made unreachable, so the checkbox on the dialog was
    /// dead code in every shipped build.
    /// </remarks>
    [AvaloniaFact]
    public void ThePreferenceIsRememberedAcrossAReload()
    {
        var vm = Loaded(out _);

        vm.PlacementPreference = FrameImportChoice.ImportFrames;

        var reloaded = Lightbox.App.Services.AppSettings.Load();
        Assert.Equal("ImportFrames", reloaded.SymbolPlacementChoice);
        Assert.Equal(FrameImportChoice.ImportFrames, vm.PlacementPreference);
    }

    /// <summary>
    /// Untouched, the preference writes no key at all — absent, not defaulted.
    /// </summary>
    /// <remarks>
    /// Absent is what means "ask me". A settings file that stored a default here
    /// would answer a question the artist was never shown, which is the whole of
    /// what B373 did.
    /// </remarks>
    [AvaloniaFact]
    public void AnUnsetPreferenceIsAbsentFromTheSettingsFile()
    {
        var settings = new Lightbox.App.Services.AppSettings();

        var json = settings.Serialize();

        Assert.DoesNotContain("\"SymbolPlacementChoice\"", json);
    }

    /// <summary>
    /// A stored value nobody recognises falls back to asking rather than
    /// refusing to load.
    /// </summary>
    [AvaloniaFact]
    public void AnUnreadablePreferenceMeansAsk()
    {
        var vm = Loaded(out var walk);
        vm.Settings.SymbolPlacementChoice = "NotAChoice";

        Assert.Null(vm.PlacementPreference);
        Assert.True(vm.PlacementNeedsAChoice(walk.Id));
    }

    /// <summary>
    /// The dialog the view shows can be answered, and answering it closes it.
    /// </summary>
    /// <remarks>
    /// The old path left it open: it abandoned the Task on timeout without ever
    /// calling <c>Close()</c>, so the artist was left with a dialog on screen
    /// whose buttons did nothing to a symbol that had already been placed.
    /// </remarks>
    [AvaloniaFact]
    public void TheDialogClosesWhenItIsAnswered()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var walk = Prop("walk", frames: 8);
        var asking = window.ShowPlacementChoiceDialogAsync(walk);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Single(window.OwnedWindows);

        window.OwnedWindows[0].Close(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(asking.IsCompleted,
            "the dialog was closed and the task it was driving did not complete — the old path "
            + "abandoned this task and left the window open (B373)");
        Assert.Empty(window.OwnedWindows);
    }

    /// <summary>
    /// A multi-frame symbol still only in the artist's library is a question
    /// too.
    /// </summary>
    /// <remarks>
    /// It is not in the <see cref="SymbolRegistry"/> until placing it adopts it,
    /// and the adoption happens <em>inside</em> <c>PlaceSymbol</c> — so asking
    /// the registry alone answers "no question" and the symbol goes in as a
    /// silent Reference with the artist never asked. The drag route carries only
    /// an id, which is what makes this indistinguishable from the project case
    /// at the panel.
    /// </remarks>
    [AvaloniaFact]
    public void ASymbolStillOnlyInTheLibraryIsAskedAboutToo()
    {
        var vm = Loaded(out _);
        var global = Prop("library walk", frames: 6);
        Lightbox.App.Services.SymbolLibrary.Save(
            new Dictionary<string, Symbol> { [global.Id] = global });
        vm.ReloadSymbolLibraryForTests();

        Assert.Null(SymbolRegistry.Resolve(global.Id));
        // Equal rather than same: the library round-trips through JSON, so this
        // is the artist's symbol rebuilt, not the object handed to Save.
        var resolved = vm.SymbolToPlace(global.Id);
        Assert.NotNull(resolved);
        Assert.Equal(global.Id, resolved!.Id);
        Assert.Equal(6, resolved.FrameCount);
        Assert.True(
            vm.PlacementNeedsAChoice(global.Id),
            "a six-frame symbol from the library was placed without the artist being asked, "
            + "because it is not in the registry until placing it adopts it");
    }
}
