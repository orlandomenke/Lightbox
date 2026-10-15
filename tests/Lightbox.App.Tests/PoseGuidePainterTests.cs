using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The pose guide as painted (Q274): a bone's volume is a faint tube as wide
/// as the part measured in the keys, the bone itself a thin line down its
/// middle, both in the reference colour — faint enough that the artist's own
/// line, drawn over it, is always the darker mark.
/// </summary>
public sealed class PoseGuidePainterTests(ITestOutputHelper output)
{
    /// <summary>One horizontal bone across the middle, 40 wide, with its two joints.</summary>
    private static PoseGuideOverlay Shin(bool editable = false) => new(
        [new PoseGuideBone(new StrokePoint(20, 50.5, 0), new StrokePoint(80, 50.5, 0), 40, 0)],
        [
            new PoseGuideJoint("shin", "knee", new StrokePoint(20, 50.5, 0), 0, 0),
            new PoseGuideJoint("shin", "ankle", new StrokePoint(80, 50.5, 0), 0, 1),
        ],
        editable);

    private static SKBitmap Paint(PoseGuideOverlay? guide)
    {
        var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        PoseGuidePainter.Paint(canvas, guide, 1f);
        return bitmap;
    }

    [Fact]
    public void TheVolumeIsAFaintTubeAsWideAsThePart()
    {
        using var bitmap = Paint(Shin());

        var inside = bitmap.GetPixel(50, 62);    // 11.5 below the bone: inside a 40-wide tube
        var outside = bitmap.GetPixel(50, 75);   // 24.5 below: outside it
        var bone = bitmap.GetPixel(50, 50);
        output.WriteLine($"inside {inside}, outside {outside}, bone {bone}");

        Assert.Equal(SKColors.White, outside);
        Assert.NotEqual(SKColors.White, inside);
        // Faint: still mostly paper, so a line drawn over it reads.
        Assert.True(inside.Blue > 200, $"the volume is too heavy to draw over: {inside}");
        // The bone is the reference colour at full strength: warm, the blue gone.
        Assert.True(bone.Red > 200 && bone.Blue < 120, $"the bone drew {bone}");
    }

    [Fact]
    public void AnEditableGuideHasLargerJoints()
    {
        using var plain = Paint(Shin());
        using var editable = Paint(Shin(editable: true));

        // Six pixels out from the knee (pixel centres are at +0.5): past a plain
        // ring of radius 4, on the casing of an editable joint's grip.
        var p = plain.GetPixel(20, 44);
        var e = editable.GetPixel(20, 44);
        output.WriteLine($"plain {p}, editable {e}");

        Assert.True(e.Blue < p.Blue - 40, $"the editable ring is not larger: plain {p}, editable {e}");
    }

    /// <summary>
    /// Where two bones meet, the volume is no darker than along either (Q274
    /// review): faint tubes laid one over the other doubled at every joint, and
    /// a leg read as a string of sausages instead of one form.
    /// </summary>
    [Fact]
    public void TheVolumeIsNoDarkerWhereTwoBonesMeet()
    {
        var leg = new PoseGuideOverlay(
            [
                new PoseGuideBone(new StrokePoint(10, 50.5, 0), new StrokePoint(50, 50.5, 0), 30, 0),
                new PoseGuideBone(new StrokePoint(50, 50.5, 0), new StrokePoint(90, 50.5, 0), 30, 0),
            ],
            [], false);
        using var bitmap = Paint(leg);

        var knee = bitmap.GetPixel(50, 60);   // beside the joint, inside both tubes' caps
        var shin = bitmap.GetPixel(70, 60);
        output.WriteLine($"knee {knee}, shin {shin}");

        Assert.Equal(shin, knee);
    }

    [Fact]
    public void AtRestTheBoneIsThinnerAndFainterThanWhenEditable()
    {
        var shin = Shin() with { Joints = [] };
        using var rest = Paint(shin);
        using var editable = Paint(shin with { Editable = true });

        // A pixel off the bone's line: a 1 px line misses it, an edited one does not.
        var r = rest.GetPixel(50, 51);
        var e = editable.GetPixel(50, 51);
        output.WriteLine($"rest {r}, editable {e}");

        Assert.True(e.Blue < r.Blue - 30, $"the edited bone is not the stronger: rest {r}, editable {e}");
    }

    [Fact]
    public void NoGuideDrawsNothing()
    {
        using var bitmap = Paint(null);

        Assert.Equal(SKColors.White, bitmap.GetPixel(50, 50));
    }
}
