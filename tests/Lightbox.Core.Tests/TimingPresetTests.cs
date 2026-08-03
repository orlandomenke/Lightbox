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
        var before = layer.Cels.Select(c => c.Frame).Where(f => f is not null).ToList();

        var exposed = ExposureSheet.ApplyTiming(layer, 0, 8, new TimingPreset("On 2s", [2]));

        Assert.Equal("X.X.X.X.", Pattern(layer));
        Assert.Equal(4, exposed);
        // The drawings are the same objects, in the same order — nothing was
        // created and nothing was replaced.
        var after = layer.Cels.Select(c => c.Frame).Where(f => f is not null).ToList();
        Assert.Equal(before.Take(4), after);
    }

    [Fact]
    public void APatternShorterThanTheRangeRepeats()
    {
        // What makes "on 2s" mean "on 2s for as far as I dragged" rather than
        // "on 2s for the first drawing and then whatever".
        var layer = OnOnes(12);
        ExposureSheet.ApplyTiming(layer, 0, 12, new TimingPreset("On 3s", [3]));
        Assert.Equal("X..X..X..X..", Pattern(layer));
    }

    [Fact]
    public void AnUnevenPatternIsLaidDownInOrder()
    {
        var layer = OnOnes(12);
        ExposureSheet.ApplyTiming(layer, 0, 12, new TimingPreset("Slow in", [1, 1, 2, 3, 4]));
        // 1 + 1 + 2 + 3 + 4 = 11, so the fifth drawing lands at index 7 and the
        // pattern restarts at 11.
        Assert.Equal("XXX.X..X...X", Pattern(layer));
    }

    [Fact]
    public void ItNeverCreatesOrDestroysADrawing()
    {
        // The property that makes this safe to reach for: the worst it can do
        // to an animator's art is re-time it.
        var layer = OnOnes(6);
        var drawings = layer.Cels.Select(c => c.Frame!).ToHashSet();

        ExposureSheet.ApplyTiming(layer, 0, 6, new TimingPreset("On 4s", [4]));

        Assert.Equal(6, layer.Cels.Count);
        // Every frame still exposed is one that was there before — no new
        // objects, and the ones that no longer fit were dropped from the
        // exposure, not deleted from anything.
        foreach (var cel in layer.Cels.Where(c => c.Frame is not null))
        {
            Assert.Contains(cel.Frame!, drawings);
        }
    }

    [Fact]
    public void ARangeThatBeginsMidHoldKeepsShowingItsDrawing()
    {
        // Starting from nothing would blank the front of the range — the cel at
        // `start` is showing a drawing and has to go on showing one.
        var layer = OnOnes(1);
        for (var i = 0; i < 5; i++) layer.Cels.Add(new Cel());   // four holds
        layer.Cels.Add(new Cel { Frame = new PaintedFrame() });

        ExposureSheet.ApplyTiming(layer, 2, 4, new TimingPreset("On 2s", [2]));

        Assert.NotNull(ExposureSheet.ExposedFrame(layer, 2));
        Assert.NotNull(layer.Cels[2].Frame);
    }

    [Fact]
    public void ARangeOfOneCelIsAKeyAndNothingElseMoves()
    {
        var layer = OnOnes(4);
        var third = layer.Cels[2].Frame;

        ExposureSheet.ApplyTiming(layer, 1, 1, new TimingPreset("On 2s", [2]));

        Assert.Equal("XX", Pattern(layer)[..2]);
        Assert.Same(third, layer.Cels[2].Frame);   // outside the range, untouched
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ANonRangeIsANoOp(int count)
    {
        var layer = OnOnes(4);
        Assert.Equal(0, ExposureSheet.ApplyTiming(layer, 0, count, new TimingPreset("On 2s", [2])));
        Assert.Equal("XXXX", Pattern(layer));
    }

    [Fact]
    public void ADegeneratePatternBehavesRatherThanThrowing()
    {
        // A preset with a zero or a negative in it should not reach the artist
        // as an exception, and must not loop for ever laying keys on one cel.
        var layer = OnOnes(4);
        var exposed = ExposureSheet.ApplyTiming(layer, 0, 4, new TimingPreset("Broken", [0, -2]));

        Assert.Equal("XXXX", Pattern(layer));
        Assert.Equal(4, exposed);
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
}
