using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Not compositing a frame that would come out identical — and, mostly, proving
/// it never skips one that would not.
/// </summary>
/// <remarks>
/// <para>
/// <b>The saving is easy and the risk is the whole job.</b> Showing frame 4 while
/// the playhead says 5 raises no exception, fails no test and looks exactly like
/// the artist's own drawing. So the tests that matter here are the ones that
/// require a composite to happen: a wrong skip is stale art, a wrong composite is
/// a few milliseconds.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class SkipUnchangedFramesTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A scene on 2s: a new drawing every other frame, holds between.</summary>
    private static MainViewModel OnTwos(int frames = 8)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ActiveLayerIndex = 1;
        for (var i = 0; i < frames; i += 2)
        {
            if (i > 0) vm.InsertFrameAt(new FrameCell(i) { LayerIndex = 1 }, FrameRole.Key);
            vm.CurrentFrameIndex = i;
            vm.BeginStroke(40 + i * 10, 40, 1);
            vm.MoveStroke(120 + i * 10, 120, 1);
            vm.EndStroke();
        }
        vm.PlaybackEndFrame = frames - 1;
        vm.CurrentFrameIndex = 0;
        return vm;
    }

    private static int PublishCount(MainViewModel vm, Action body)
    {
        var published = 0;
        void Count(RenderSnapshot s) => published++;
        vm.SnapshotChanged += Count;
        try { body(); }
        finally { vm.SnapshotChanged -= Count; }
        return published;
    }

    /// <summary>
    /// <b>The saving.</b> Stepping onto a hold exposes the drawings the previous
    /// frame did, so the composite would be identical and is not performed.
    /// </summary>
    [AvaloniaFact]
    public void SteppingOntoAHoldDoesNotCompose()
    {
        var vm = OnTwos();
        vm.IsPlaying = true;
        vm.PublishSnapshot();          // frame 0, composes and is remembered

        // Frame 1 is a hold of frame 0 on every layer.
        var published = PublishCount(vm, () =>
        {
            vm.CurrentFrameIndex = 1;
            vm.PublishSnapshot();
        });

        output.WriteLine($"publishes stepping onto a hold: {published}, reused {vm.FramesReused}");
        Assert.Equal(0, published);
        Assert.True(vm.FramesReused > 0);
        vm.IsPlaying = false;
    }

    /// <summary>
    /// And the other half: stepping onto a keyed drawing composes. A mechanism
    /// that skipped everything would pass the test above and show one frame for
    /// the whole scene.
    /// </summary>
    [AvaloniaFact]
    public void SteppingOntoANewDrawingComposes()
    {
        var vm = OnTwos();
        vm.IsPlaying = true;
        vm.PublishSnapshot();

        // Moving the playhead publishes on its own; an explicit call here would
        // add a same-index republish, which is composed rather than skipped and
        // would make this count two for a reason unrelated to the claim.
        var published = PublishCount(vm, () => vm.CurrentFrameIndex = 2);

        output.WriteLine($"publishes stepping onto a key: {published}");
        Assert.Equal(1, published);
        vm.IsPlaying = false;
    }

    /// <summary>
    /// <b>Not while drawing.</b> A publish is how a mark reaches the screen, and
    /// the dirty-region machinery already makes that cheap. Asking a second "did
    /// anything change" question there would be two answers to one question.
    /// </summary>
    [AvaloniaFact]
    public void PausedPublishesAreNeverSkipped()
    {
        var vm = OnTwos();
        vm.PublishSnapshot();

        var published = PublishCount(vm, () => vm.PublishSnapshot());

        Assert.Equal(1, published);
        Assert.Equal(0, vm.FramesReused);
    }

    /// <summary>
    /// <b>A pan changes every pixel and no drawing.</b> Reusing across one shows
    /// the previous view, which reads as the canvas being stuck rather than as a
    /// stale frame — and is the easiest of these to get wrong, because the
    /// document really is untouched.
    /// </summary>
    [AvaloniaFact]
    public void AChangedViewportComposes()
    {
        var vm = OnTwos();
        vm.SetViewport(SKRectI.Create(0, 0, 400, 300));
        vm.IsPlaying = true;
        vm.PublishSnapshot();

        var published = PublishCount(vm, () =>
        {
            vm.SetViewport(SKRectI.Create(120, 90, 400, 300));
            vm.CurrentFrameIndex = 1;   // a hold — only the view moved
            vm.PublishSnapshot();
        });

        output.WriteLine($"publishes after a pan onto a hold: {published}");
        Assert.Equal(1, published);
        vm.IsPlaying = false;
    }

    /// <summary>
    /// <b>The case B165's entry names: a change underneath must be followed.</b>
    /// Anything that reaches pixels marks the canvas dirty — that is the contract
    /// the guard rests on — and a dirty canvas is never reused.
    /// </summary>
    [AvaloniaFact]
    public void AnEditIsNeverSkippedEvenOnAHold()
    {
        var vm = OnTwos();
        vm.IsPlaying = true;
        vm.PublishSnapshot();

        var published = PublishCount(vm, () =>
        {
            vm.CurrentFrameIndex = 1;                 // a hold…
            vm.ActiveLayerIndex = 0;
            vm.Doc.Scene.Layers[0].Opacity = 0.5;     // …but the picture changed
            vm.MarkWholeCanvasDirtyForTests();
            vm.PublishSnapshot();
        });

        output.WriteLine($"publishes after an edit on a hold: {published}");
        Assert.Equal(1, published);
        vm.IsPlaying = false;
    }

    /// <summary>
    /// A held Multiply layer is as unchanged as a held SrcOver one. The fold
    /// rules stop at a blend that reads beneath it because associativity licenses
    /// folding; nothing about that applies to asking whether the picture moved,
    /// and conflating the two would exclude these documents from the saving.
    /// </summary>
    [AvaloniaFact]
    public void ABlendThatReadsBeneathItDoesNotBlockReuse()
    {
        var vm = OnTwos();
        vm.Doc.Scene.Layers[0].BlendMode = LayerBlendMode.Multiply;
        vm.IsPlaying = true;
        vm.PublishSnapshot();

        var published = PublishCount(vm, () =>
        {
            vm.CurrentFrameIndex = 1;
            vm.PublishSnapshot();
        });

        Assert.Equal(0, published);
        vm.IsPlaying = false;
    }

    /// <summary>
    /// A camera is refused outright rather than compared: a keyframed one moves
    /// every pixel while the drawings hold perfectly still, and telling that apart
    /// from a still camera is not something this guard should try.
    /// </summary>
    [AvaloniaFact]
    public void ADocumentWithACameraIsNeverReused()
    {
        var vm = OnTwos();
        vm.AddCameraCommand.Execute(null);
        vm.ViewThroughCamera = true;
        vm.IsPlaying = true;
        vm.PublishSnapshot();

        var published = PublishCount(vm, () =>
        {
            vm.CurrentFrameIndex = 1;
            vm.PublishSnapshot();
        });

        // Two, not one: moving the playhead publishes, and so does the explicit
        // call. Both composed, which is the property — counting publishes here
        // would be asserting the harness rather than the behaviour.
        output.WriteLine($"publishes with a camera, stepping onto a hold: {published}, reused {vm.FramesReused}");
        Assert.Equal(0, vm.FramesReused);
        Assert.True(published >= 1);
        vm.IsPlaying = false;
    }

    /// <summary>
    /// <b>A republish at the same frame is honoured, not refused.</b> It would
    /// compose identical pixels, so skipping it looks free — and nothing in
    /// playback ever asks for one, because the tick always advances the playhead.
    /// What the branch actually did was refuse an explicit "publish this now"
    /// from anywhere else, which is a reasonable thing to ask and cheap to give.
    /// Ten tests failed at once when it did not.
    /// </summary>
    [AvaloniaFact]
    public void ARepublishAtTheSameFrameIsStillComposed()
    {
        var vm = OnTwos();
        vm.IsPlaying = true;
        vm.PublishSnapshot();

        var published = PublishCount(vm, () => vm.PublishSnapshot());

        Assert.Equal(1, published);
        vm.IsPlaying = false;
    }
}
