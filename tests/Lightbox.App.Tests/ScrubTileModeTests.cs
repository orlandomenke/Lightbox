using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Controls;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Dragging the playhead composites through tiles, and stops the moment the
/// drag does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a whole file rather than two more cases in
/// <c>ScenePassBuilderTests</c>.</b> That file asserts the pass list, which is
/// where the gate itself lives and is tested. The properties here are the ones
/// that make the gate <em>safe</em> rather than merely correct, and none of them
/// are visible in a pass list: that the flag falls back to false however the
/// gesture ends, and that the still picture at the end of a drag is republished
/// through the bounded route.
/// </para>
/// <para>
/// <b>The second one is the load-bearing property.</b> Tiles are licensed while
/// the sequence is moving, because the pyramid's resample difference below 100%
/// zoom lasts exactly as long as the motion. Releasing the pointer moves no
/// playhead, so nothing else in the application republishes — without the
/// explicit one the artist is left looking at a tiled still, which is the thing
/// the whole justification promises never to leave on screen.
/// </para>
/// </remarks>
public class ScrubTileModeTests(ITestOutputHelper output)
{
    private static MainViewModel WithScene()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Scene", 120, 80, 12, 72, "#ffffff", false));
        return vm;
    }

    /// <summary>
    /// Ending the drag republishes, so the still picture is composed by the
    /// bounded route rather than left as the last tiled frame of the scrub.
    /// </summary>
    [AvaloniaFact]
    public void LettingGoOfThePlayheadRepublishesTheStill()
    {
        var vm = WithScene();
        vm.IsScrubbing = true;

        var publishes = 0;
        vm.SnapshotChanged += _ => publishes++;

        vm.IsScrubbing = false;

        output.WriteLine($"{publishes} publish(es) on releasing the playhead");
        Assert.Equal(1, publishes);
    }

    /// <summary>
    /// Beginning a drag does not republish on its own — the scrub's own frame
    /// change does that, and a second publish here would be one wasted composite
    /// per drag on top of the one that matters.
    /// </summary>
    [AvaloniaFact]
    public void TakingHoldOfThePlayheadDoesNotRepublishByItself()
    {
        var vm = WithScene();

        var publishes = 0;
        vm.SnapshotChanged += _ => publishes++;

        vm.IsScrubbing = true;

        Assert.Equal(0, publishes);
    }

    /// <summary>
    /// <b>The drag that never sees a release.</b> A pointer lost to another
    /// window, a dialog stealing focus, or the app losing capture ends the
    /// gesture without <c>OnPointerReleased</c> ever running — and a flag left
    /// true there would strand the canvas compositing through tiles for the rest
    /// of the session, showing a resampled still nobody could get rid of.
    /// </summary>
    [AvaloniaFact]
    public void LosingCaptureEndsTheScrubJustAsReleasingDoes()
    {
        var ruler = new TimelineRuler { MaxFrame = 10, CellWidth = 10, Extent = 10 };
        ruler.SetValue(TimelineRuler.IsScrubbingProperty, true);

        ruler.RaiseEvent(new PointerCaptureLostEventArgs(
            ruler, new Pointer(0, PointerType.Mouse, true)));

        Assert.False(ruler.IsScrubbing);
    }

    /// <summary>
    /// A tile pass built for a still document would be handed to the bounded
    /// compositor, which skips it rather than dereferencing nothing — so it would
    /// vanish silently rather than fail. Asserted at the view-model level as well
    /// as in the builder, because this is the composition of the two.
    /// </summary>
    [AvaloniaFact]
    public void AStillDocumentPublishesWithoutTiles()
    {
        var vm = WithScene();

        RenderSnapshot? last = null;
        vm.SnapshotChanged += s => last = s;
        vm.PublishSnapshot();

        Assert.NotNull(last);
        Assert.NotNull(last!.Passes);
        Assert.DoesNotContain(last.Passes!, p => p.SourceFrame is not null);
    }
}
