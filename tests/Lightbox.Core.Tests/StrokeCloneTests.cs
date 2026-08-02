using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests;

/// <summary>
/// What survives being copied.
/// </summary>
/// <remarks>
/// <see cref="Stroke.Clone"/> is what duplicating a cel and generating an
/// inbetween both go through, so a field it forgets is a field that silently
/// stops working on every copied drawing. It forgot two.
/// </remarks>
public class StrokeCloneTests
{
    private static Stroke Painted() => new()
    {
        Tool = ToolKind.Brush,
        Color = "#112233",
        SwatchId = "sw_skin",
        GradientId = "gr_sky",
        ClipId = "clip_head",
        AlphaLocked = true,
        Label = "left-arm",
        Points = [new StrokePoint(1, 2, 0.5), new StrokePoint(3, 4, 1)],
        Brush = new BrushSettings { Size = 17 },
    };

    [Fact]
    public void ACloneKeepsItsLinkToThePalette()
    {
        // B22. A copied drawing kept its colour and lost the swatch behind it,
        // so recolouring the palette afterwards changed the original and left
        // the copy where it was.
        var clone = Painted().Clone();

        Assert.Equal("sw_skin", clone.SwatchId);
        Assert.Equal("gr_sky", clone.GradientId);
    }

    [Fact]
    public void ACloneKeepsEverythingElseToo()
    {
        var source = Painted();

        var clone = source.Clone();

        Assert.Equal(source.Tool, clone.Tool);
        Assert.Equal(source.Color, clone.Color);
        Assert.Equal(source.ClipId, clone.ClipId);
        Assert.Equal(source.AlphaLocked, clone.AlphaLocked);
        Assert.Equal(source.Label, clone.Label);
        Assert.Equal(source.Brush.Size, clone.Brush.Size);
        Assert.Equal(source.Points, clone.Points);
    }

    [Fact]
    public void ACloneIsANewStrokeWithItsOwnPoints()
    {
        var source = Painted();

        var clone = source.Clone();
        clone.Points.Add(new StrokePoint(9, 9, 1));

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal(2, source.Points.Count);
    }

    [Fact]
    public void ADuplicatedCelStillPaintsFromTheSameSwatch()
    {
        // The path an artist actually takes: copy a drawing along the
        // timeline, then recolour. Both have to change.
        var frame = new PaintedFrame { Strokes = [Painted()] };

        var copy = (PaintedFrame)DocumentEditor.CloneFrame(frame)!;

        Assert.Equal("sw_skin", copy.Strokes[0].SwatchId);
    }
}
