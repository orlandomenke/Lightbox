using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// How much of the layer stack holds still during playback — B165's sizing
/// measurement, and the rules the fix would rest on.
/// </summary>
/// <remarks>
/// <para>
/// <b>B165's own entry says to measure before building</b>, because the answer
/// decides whether the work is S or M: <i>"on a two-layer test scene the win may
/// be a rounding error, and on a ten-layer rig at 4K it is the difference between
/// playable and not."</i> These are that measurement, and they are pure exposure
/// sheet arithmetic — no pixels, no surface, no window.
/// </para>
/// </remarks>
public class UnchangedLayerRunTests(ITestOutputHelper output)
{
    /// <summary>A layer exposing a new drawing every <paramref name="onNs"/> frames.</summary>
    private static Layer Cycling(string name, int frames, int onNs)
    {
        var layer = new Layer { Name = name };
        Frame? current = null;
        for (var i = 0; i < frames; i++)
        {
            if (i % onNs == 0) current = new Frame();
            // A hold is an empty cel: ExposedFrame walks back to the last drawing.
            layer.Cels.Add(new Cel { Frame = i % onNs == 0 ? current : null });
        }
        return layer;
    }

    /// <summary>A layer that never changes — a BG plate, a colour hold, a locked prop.</summary>
    private static Layer Held(string name, int frames, bool background = false)
    {
        var layer = new Layer { Name = name, IsBackground = background };
        layer.Cels.Add(new Cel { Frame = new Frame() });
        for (var i = 1; i < frames; i++) layer.Cels.Add(new Cel());
        return layer;
    }

    private static Scene SceneOf(int frames, params Layer[] layers)
    {
        var scene = new Scene { Width = 1920, Height = 1080, FrameCount = frames };
        scene.Layers.AddRange(layers);
        return scene;
    }

    /// <summary>
    /// <b>The measurement B165 asked for, on three document shapes.</b> Printed
    /// rather than asserted tightly, because the number is the deliverable — but
    /// the ordering between them is asserted, since that is the claim: the deeper
    /// the stack, the more there is to save.
    /// </summary>
    [Fact]
    public void TheUnchangedShareGrowsWithTheStack()
    {
        const int frames = 24;

        // The shape this repository's own test scenes have, and the one that
        // would have reported "no win" if anybody had swept it.
        var twoLayer = SceneOf(frames, Held("Paper", frames, background: true), Cycling("Ink", frames, 2));

        // An ordinary character setup: paper, a held BG plate, a prop, the
        // character on 2s, and an effects layer on 1s.
        var sixLayer = SceneOf(frames,
            Held("Paper", frames, background: true),
            Held("BG plate", frames),
            Held("Prop", frames),
            Cycling("Character", frames, 2),
            Cycling("Mouth", frames, 2),
            Cycling("Effects", frames, 1));

        // A rig, which B125's own arithmetic calls "an ordinary character rig,
        // not a stress test".
        var tenLayer = SceneOf(frames,
            Held("Paper", frames, background: true),
            Held("BG plate", frames),
            Held("Mid prop", frames),
            Held("Shadow", frames),
            Held("Colour hold", frames),
            Cycling("Body", frames, 2),
            Cycling("Head", frames, 2),
            Cycling("Arm L", frames, 2),
            Cycling("Arm R", frames, 3),
            Cycling("FX", frames, 1));

        foreach (var (name, scene) in new[]
                 { ("2 layers", twoLayer), ("6 layers", sixLayer), ("10 layers", tenLayer) })
        {
            var (today, cached, saved) = UnchangedLayerRun.BlendsOverRange(scene, 0, frames - 1);
            output.WriteLine(
                $"{name,-10} blends {today,5} -> {cached,5}   saved {saved * 100,5:F1}%");
        }

        var two = UnchangedLayerRun.BlendsOverRange(twoLayer, 0, frames - 1).Saved;
        var six = UnchangedLayerRun.BlendsOverRange(sixLayer, 0, frames - 1).Saved;
        var ten = UnchangedLayerRun.BlendsOverRange(tenLayer, 0, frames - 1).Saved;

        Assert.True(six > two, "a six-layer rig saved no more than a two-layer scene");
        Assert.True(ten > six, "a ten-layer rig saved no more than a six-layer one");
    }

    /// <summary>
    /// <b>A depth of one saves nothing — replacing one blend with one blend —
    /// and the scene that shows it is not the one I expected.</b>
    /// </summary>
    /// <remarks>
    /// This test first asserted that an ordinary two-layer scene saves nothing,
    /// on the reasoning in B165's entry that a held background over one drawing
    /// layer has depth 1. **It measured 26% instead, and the entry's guess and
    /// mine were both wrong for the same reason**: on 2s, every second frame is a
    /// hold on the drawing layer too, so half the transitions have the *whole
    /// stack* unchanged rather than just the paper. The zero only appears when
    /// something really does change every single frame.
    /// </remarks>
    [Fact]
    public void ADepthOfOneSavesNothingAtAll()
    {
        const int frames = 12;
        // On 1s: a new drawing every frame, so only the paper ever holds.
        var scene = SceneOf(frames, Held("Paper", frames, background: true), Cycling("Ink", frames, 1));

        var (today, cached, saved) = UnchangedLayerRun.BlendsOverRange(scene, 0, frames - 1);

        output.WriteLine($"two layers on 1s: {today} -> {cached} blends, {saved * 100:F1}% saved");
        Assert.Equal(1, UnchangedLayerRun.Depth(scene, 1, 2));
        Assert.Equal(today, cached);
        Assert.Equal(0, saved, 6);
    }

    /// <summary>
    /// <b>Working on 2s is where most of the saving comes from, and that is the
    /// finding.</b> Every second frame holds on every drawing layer, so the whole
    /// composite is reusable — not merely a bottom run. The same scene on 1s
    /// saves nothing at all, which is the cleanest statement of what the fix is
    /// actually exploiting.
    /// </summary>
    [Fact]
    public void TheSavingComesFromHoldsRatherThanFromBackgroundLayers()
    {
        const int frames = 24;
        var onOnes = SceneOf(frames, Held("Paper", frames, background: true), Cycling("Ink", frames, 1));
        var onTwos = SceneOf(frames, Held("Paper", frames, background: true), Cycling("Ink", frames, 2));

        var ones = UnchangedLayerRun.BlendsOverRange(onOnes, 0, frames - 1).Saved;
        var twos = UnchangedLayerRun.BlendsOverRange(onTwos, 0, frames - 1).Saved;

        output.WriteLine($"same two layers — on 1s {ones * 100:F1}% saved, on 2s {twos * 100:F1}% saved");
        Assert.Equal(0, ones, 6);
        Assert.True(twos > 0.2, $"animating on 2s only saved {twos * 100:F1}%");
    }

    /// <summary>
    /// A hold means the drawing did not change, so the whole stack is the run.
    /// This is the frame an artist spends most of a sequence on when working on
    /// 2s — every second frame is a hold on every layer.
    /// </summary>
    [Fact]
    public void AFrameWhereNothingMovedIsEntirelyUnchanged()
    {
        var scene = SceneOf(8,
            Held("Paper", 8, background: true),
            Cycling("A", 8, 4),
            Cycling("B", 8, 4));

        // Frames 1, 2 and 3 are holds of frame 0 on both cycling layers.
        Assert.Equal(3, UnchangedLayerRun.Depth(scene, 1, 2));
        // Frame 4 keys both of them, so the run ends at the paper.
        Assert.Equal(1, UnchangedLayerRun.Depth(scene, 3, 4));
    }

    /// <summary>
    /// <b>The run is contiguous from the bottom, and a static layer above a
    /// moving one is worth nothing.</b> Compositing is ordered: what sits above a
    /// changed layer has changed pixels beneath it, so it cannot be part of a
    /// pre-folded bitmap however still it is itself. This is the rule a naive
    /// "which layers changed" implementation gets wrong.
    /// </summary>
    [Fact]
    public void AStaticLayerAboveAMovingOneIsNotPartOfTheRun()
    {
        var scene = SceneOf(8,
            Held("Paper", 8, background: true),
            Cycling("Character", 8, 1),   // changes every frame
            Held("Foreground", 8));       // never changes, but sits above

        var depth = UnchangedLayerRun.Depth(scene, 1, 2);

        output.WriteLine($"depth {depth} of {UnchangedLayerRun.VisibleLayers(scene)} visible layers");
        Assert.Equal(1, depth);
    }

    /// <summary>
    /// <b>A non-SrcOver blend ends the run whether or not it moved.</b> Folding
    /// rests on premultiplied source-over being associative; a Multiply layer
    /// reads what is beneath it, so pre-folding through one changes the picture.
    /// The rule is <see cref="LayerStackBake"/>'s and is shared rather than
    /// restated, so the two cannot drift apart.
    /// </summary>
    [Fact]
    public void ABlendThatReadsBeneathItEndsTheRun()
    {
        var scene = SceneOf(8,
            Held("Paper", 8, background: true),
            Held("Multiply shadow", 8),
            Held("Above", 8));
        scene.Layers[1].BlendMode = LayerBlendMode.Multiply;

        // Everything is held, so without the blend rule the run would be all
        // three. It stops at the Multiply layer instead.
        Assert.Equal(1, UnchangedLayerRun.Depth(scene, 1, 2));
    }

    /// <summary>
    /// A hidden layer is neither a saving nor a barrier — it is never composited,
    /// so it does not appear in the count on either side.
    /// </summary>
    [Fact]
    public void AHiddenLayerIsSkippedRatherThanEndingTheRun()
    {
        var scene = SceneOf(8,
            Held("Paper", 8, background: true),
            Cycling("Hidden mover", 8, 1),
            Held("Prop", 8));
        scene.Layers[1].Visible = false;

        // With the mover hidden, paper and prop are adjacent and both held.
        Assert.Equal(2, UnchangedLayerRun.Depth(scene, 1, 2));
        Assert.Equal(2, UnchangedLayerRun.VisibleLayers(scene));
    }
}
