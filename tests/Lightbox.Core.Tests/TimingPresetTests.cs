using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests;

/// <summary>
/// Timing presets — Q11's answer. A symbol carries drawings; this carries
/// their spacing, which is the half of frame-by-frame work no symbol can
/// express.
/// </summary>
public class TimingPresetTests
{
    /// <summary>A layer keyed on 1s, with distinguishable drawings.</summary>
    private static Layer OnOnes(int drawings)
    {
        var layer = new Layer { Name = "Anim", Kind = LayerKind.Painted };
        for (var i = 0; i < drawings; i++) layer.Cels.Add(new Cel { Frame = new PaintedFrame() });
        return layer;
    }

    /// <summary>Which cels hold a key, as a compact string: X keyed, . held.</summary>
    private static string Pattern(Layer layer) =>
        string.Concat(layer.Cels.Select(c => c.Frame is null ? '.' : 'X'));

    [Fact]
    public void ApplyingAPatternReExposesTheDrawingsThatAreThere()
    {
        var layer = OnOnes(8);
        var before = layer.Cels.Select(c => c.Frame!).ToList();

        var change = ExposureSheet.ApplyTiming(layer, 0, 8, new TimingPreset("On 2s", [2]));

        Assert.Equal("X.X.X.X.X.X.X.X.", Pattern(layer));
        Assert.Equal(8, change.Drawings);
        // The drawings are the same objects, in the same order — nothing was
        // created and nothing was replaced.
        Assert.Equal(before, layer.Cels.Where(c => c.Frame is not null).Select(c => c.Frame!));
    }

    [Fact]
    public void ThePatternDecidesTheLength_NotTheSelection()
    {
        // The correction that matters most. Holding the selection's length would
        // mean "on 2s" silently dropped half an artist's drawings, which is
        // never what that phrase means — thinning on purpose is ReduceToStep.
        var layer = OnOnes(12);

        var change = ExposureSheet.ApplyTiming(layer, 0, 12, new TimingPreset("On 2s", [2]));

        Assert.Equal(12, change.Drawings);
        Assert.Equal(24, change.Frames);
        Assert.Equal(12, change.Grew);
        Assert.Equal(24, layer.Cels.Count);
    }

    [Fact]
    public void GoingToOnesShrinksTheRange()
    {
        // The same rule in the other direction: six drawings on 2s put back on
        // 1s occupy six cels, so the row gets shorter.
        var layer = new Layer { Kind = LayerKind.Painted };
        for (var i = 0; i < 6; i++)
        {
            layer.Cels.Add(new Cel { Frame = new PaintedFrame() });
            layer.Cels.Add(new Cel());
        }

        var change = ExposureSheet.ApplyTiming(layer, 0, 12, new TimingPreset("On 1s", [1]));

        Assert.Equal("XXXXXX", Pattern(layer));
        Assert.Equal(-6, change.Grew);
        Assert.Equal(6, change.Drawings);
    }

    [Fact]
    public void APatternShorterThanTheRangeRepeats()
    {
        // What makes "on 3s" mean "on 3s for everything I selected" rather than
        // "on 3s for the first drawing and then whatever".
        var layer = OnOnes(4);
        ExposureSheet.ApplyTiming(layer, 0, 4, new TimingPreset("On 3s", [3]));
        Assert.Equal("X..X..X..X..", Pattern(layer));
    }

    [Fact]
    public void AnUnevenPatternIsLaidDownInOrder()
    {
        var layer = OnOnes(12);
        ExposureSheet.ApplyTiming(layer, 0, 12, new TimingPreset("Slow in", [1, 1, 2, 3, 4]));
        // 1+1+2+3+4 = 11 for the first five, then the pattern restarts. Twelve
        // drawings therefore occupy 24 cels and every one of them is still here.
        Assert.Equal("XXX.X..X...XXX.X..X...XX", Pattern(layer));
        Assert.Equal(12, Pattern(layer).Count(c => c == 'X'));
    }

    [Fact]
    public void ItNeverCreatesOrDestroysADrawing()
    {
        // The property that makes this safe to reach for: the worst it can do
        // to an animator's art is re-time it.
        var layer = OnOnes(6);
        var drawings = layer.Cels.Select(c => c.Frame!).ToList();

        ExposureSheet.ApplyTiming(layer, 0, 6, new TimingPreset("On 4s", [4]));

        // Every drawing survives, and no drawing appeared that was not there.
        Assert.Equal(drawings, layer.Cels.Where(c => c.Frame is not null).Select(c => c.Frame!));
        Assert.Equal(6, layer.Cels.Count(c => c.Frame is not null));
    }

    [Fact]
    public void ARangeOfOnlyHoldsIsANoOp_AndNothingBlanks()
    {
        // A range with no drawing keyed inside it has nothing to re-time. The
        // front does not blank: the hold still resolves back to the drawing
        // keyed before the range, which was never touched.
        var layer = OnOnes(1);
        for (var i = 0; i < 5; i++) layer.Cels.Add(new Cel());
        layer.Cels.Add(new Cel { Frame = new PaintedFrame() });
        var was = Pattern(layer);

        var change = ExposureSheet.ApplyTiming(layer, 2, 4, new TimingPreset("On 2s", [2]));

        Assert.Equal(0, change.Drawings);
        Assert.Equal(was, Pattern(layer));
        Assert.NotNull(ExposureSheet.ExposedFrame(layer, 2));
    }

    [Fact]
    public void ARangeBeginningMidHoldDoesNotDragTheOutsideDrawingIn()
    {
        // The defect this rule exists to prevent: pulling the drawing keyed
        // before the range into it would put one Frame object in two cels, and
        // editing either would then edit both.
        var layer = new Layer { Kind = LayerKind.Painted };
        var outside = new PaintedFrame();
        layer.Cels.Add(new Cel { Frame = outside });      // 0: keyed
        layer.Cels.Add(new Cel());                        // 1: hold
        layer.Cels.Add(new Cel());                        // 2: hold
        var inside = new PaintedFrame();
        layer.Cels.Add(new Cel { Frame = inside });       // 3: keyed

        ExposureSheet.ApplyTiming(layer, 1, 3, new TimingPreset("On 2s", [2]));

        Assert.Equal(1, layer.Cels.Count(c => ReferenceEquals(c.Frame, outside)));
        Assert.Same(outside, layer.Cels[0].Frame);
        // The in-range drawing packs to the front of the selection.
        Assert.Same(inside, layer.Cels[1].Frame);
    }

    [Fact]
    public void ARangeOfOneCelLeavesEverythingAfterItIntact()
    {
        var layer = OnOnes(4);
        var third = layer.Cels[2].Frame;

        ExposureSheet.ApplyTiming(layer, 1, 1, new TimingPreset("On 2s", [2]));

        // One drawing held for two: the single cel became two, so the row grew
        // by one and the drawings after it shifted down rather than vanishing.
        Assert.Equal("XX.XX", Pattern(layer));
        Assert.Same(third, layer.Cels[3].Frame);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ANonRangeIsANoOp(int count)
    {
        var layer = OnOnes(4);
        Assert.Equal(0, ExposureSheet.ApplyTiming(layer, 0, count, new TimingPreset("On 2s", [2])).Drawings);
        Assert.Equal("XXXX", Pattern(layer));
    }

    [Fact]
    public void ADegeneratePatternBehavesRatherThanThrowing()
    {
        // A preset with a zero or a negative in it should not reach the artist
        // as an exception, and must not loop for ever laying keys on one cel.
        var layer = OnOnes(4);
        var change = ExposureSheet.ApplyTiming(layer, 0, 4, new TimingPreset("Broken", [0, -2]));

        Assert.Equal("XXXX", Pattern(layer));
        Assert.Equal(4, change.Drawings);
        Assert.Equal(0, change.Grew);
        Assert.Equal(1, new TimingPreset("Empty", []).HoldFor(0));
    }

    [Fact]
    public void TheBuiltInsAreTheOnesAnAnimatorAsksForByName()
    {
        var names = TimingPreset.BuiltIns.Select(p => p.Name).ToList();
        Assert.Contains("On 1s", names);
        Assert.Contains("On 2s", names);
        Assert.Contains("Slow in", names);
        Assert.All(TimingPreset.BuiltIns, p => Assert.NotEmpty(p.Holds));
        Assert.Equal(11, TimingPreset.BuiltIns.First(p => p.Name == "Slow in").CycleLength);
    }

    // ---- typing a pattern in ---------------------------------------------------

    [Theory]
    [InlineData("2", new[] { 2 })]
    [InlineData("1,1,2,3,4", new[] { 1, 1, 2, 3, 4 })]
    [InlineData("1 1 2 3 4", new[] { 1, 1, 2, 3, 4 })]
    [InlineData(" 1, 1 ,2 ", new[] { 1, 1, 2 })]
    public void APatternCanBeTypedWithCommasOrSpaces(string text, int[] expected)
    {
        Assert.True(TimingPreset.TryParse("Mine", text, out var preset));
        Assert.Equal(expected, preset.Holds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1,1,x,3")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("1,,2,oops")]
    public void ATypoFailsTheWholeParseRatherThanBeingSkipped(string text)
    {
        // Dropping the bad entry would make a preset that quietly is not the one
        // the artist typed, which is worse than refusing it.
        Assert.False(TimingPreset.TryParse("Mine", text, out _));
    }

    [Fact]
    public void APatternRoundTripsThroughItsText()
    {
        var preset = new TimingPreset("Slow in", [1, 1, 2, 3, 4]);
        Assert.Equal("1, 1, 2, 3, 4", preset.Pattern);
        Assert.True(TimingPreset.TryParse(preset.Name, preset.Pattern, out var back));
        Assert.Equal(preset.Holds, back.Holds);
        Assert.Equal(preset.Name, back.Name);
    }

    // ---- through the editor, which is where undo lives -------------------------

    private static Doc DocOnOnes(int drawings)
    {
        var doc = new Doc();
        doc.Scene.Layers.Clear();
        doc.Scene.Layers.Add(OnOnes(drawings));
        doc.Scene.FrameCount = drawings;
        return doc;
    }

    [Fact]
    public void RetimingIsOneUndoStep()
    {
        // The whole operation has to collapse to one Ctrl+Z. Re-timing a range
        // and getting eighteen undos would make the feature unusable.
        var doc = DocOnOnes(6);
        var editor = new DocumentEditor(doc);
        var layerId = doc.Scene.Layers[0].Id;

        var change = editor.ApplyTiming(layerId, 0, 5, new TimingPreset("On 2s", [2]));
        Assert.Equal(6, change.Drawings);
        Assert.Equal(12, editor.Doc.Scene.Layers[0].Cels.Count);

        editor.Undo();

        Assert.Equal(6, editor.Doc.Scene.Layers[0].Cels.Count);
        Assert.Equal("XXXXXX", Pattern(editor.Doc.Scene.Layers[0]));
    }

    [Fact]
    public void TheSceneGrowsToFitButNeverShrinks()
    {
        // Growing matches StretchExposure. Not shrinking is deliberate: other
        // layers still occupy those frames, and truncating the scene because one
        // row got shorter would cut them off.
        var doc = DocOnOnes(6);
        doc.Scene.Layers.Add(OnOnes(6));
        var editor = new DocumentEditor(doc);

        editor.ApplyTiming(doc.Scene.Layers[0].Id, 0, 5, new TimingPreset("On 3s", [3]));
        Assert.Equal(18, editor.Doc.Scene.FrameCount);

        editor.ApplyTiming(editor.Doc.Scene.Layers[0].Id, 0, 17, new TimingPreset("On 1s", [1]));
        Assert.Equal(6, editor.Doc.Scene.Layers[0].Cels.Count);
        Assert.Equal(18, editor.Doc.Scene.FrameCount);
    }

    [Fact]
    public void AnUnknownLayerIsANoOpRatherThanAThrow()
    {
        var editor = new DocumentEditor(DocOnOnes(4));
        Assert.Equal(0, editor.ApplyTiming("layer-does-not-exist", 0, 3, new TimingPreset("On 2s", [2])).Drawings);
    }
}
