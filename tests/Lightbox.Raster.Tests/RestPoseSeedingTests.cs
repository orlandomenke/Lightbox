using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The no-boil promise of <c>docs/DESIGN-bones.md</c>: a posed stroke stamps
/// at its posed path and seeds every dab dynamic from its bind-pose one, so
/// the transform moves the mark without re-rolling it. Half of what is under
/// test is the seeding; the other half is that a stroke that was never posed
/// renders byte-for-byte as it always did.
/// </summary>
public class RestPoseSeedingTests
{
    private const int W = 300, H = 300;

    /// <summary>A brush with every position-seeded dynamic turned up loud.</summary>
    private static BrushSettings NoisyBrush() => new()
    {
        Size = 18, Hardness = 0.9, Opacity = 1, Flow = 0.9, Spacing = 0.25, AntiAlias = false,
        Scatter = 0.5, SizeJitter = 0.6, FlowJitter = 0.5, RotationJitter = 0.7, RoundnessJitter = 0.5,
        Roundness = 0.6,
    };

    private static Stroke BoundStroke()
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i < 12; i++)
            pts.Add(new StrokePoint(60 + i * 6, 150 + Math.Sin(i * 0.5) * 20, 0.6 + 0.4 * Math.Sin(i * 0.3)));
        var stroke = new Stroke { Points = pts, Brush = NoisyBrush(), Color = "#a03020" };
        Skinning.AssignAll(stroke, "b");
        return stroke;
    }

    private static Armature OneBone() => new()
    {
        Bones = [new Bone { Id = "b", X = 150, Y = 150, Length = 80 }],
    };

    private static Dictionary<string, BonePose> Rotate(double deg) =>
        new() { ["b"] = new BonePose { RotationDeg = deg } };

    private static SKBitmap Render(Stroke stroke)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampStroke(canvas, stroke, info, bmp);
        canvas.Flush();
        return bmp;
    }

    private static bool Identical(SKBitmap a, SKBitmap b)
    {
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }

    [Fact]
    public void ADabsDynamicsSeedFromItsBindPoseSoAPoseCannotBoil()
    {
        var stroke = BoundStroke();
        var armature = OneBone();

        var a = BrushEngine.WalkDabs(Skinning.PoseStroke(stroke, armature, Rotate(10)));
        var b = BrushEngine.WalkDabs(Skinning.PoseStroke(stroke, armature, Rotate(35)));

        // Same dab set, generated in bind space: pose the character however,
        // the seeds — and so every scatter, jitter and grain roll — survive.
        Assert.Equal(a.Count, b.Count);
        var moved = 0;
        for (var i = 0; i < a.Count; i++)
        {
            Assert.NotNull(a[i].Seed);
            Assert.Equal(a[i].Seed, b[i].Seed);
            if (a[i].Pos != b[i].Pos) moved++;
        }
        // …while the stamped positions actually follow the pose.
        Assert.True(moved > a.Count / 2, $"only {moved} of {a.Count} dabs moved between poses");
    }

    [Fact]
    public void AnIdentityPoseRendersByteForByteAsTheUnposedStroke()
    {
        var stroke = BoundStroke();
        var posed = Skinning.PoseStroke(stroke, OneBone(), pose: null);

        using var plain = Render(stroke);
        using var atRest = Render(posed);

        // Byte-for-byte: the posed construction walks the same densified
        // path with the same arithmetic, so "posed at rest" and "never
        // posed" are the same picture — which is what lets the live path
        // route every bound stroke through PoseStroke unconditionally.
        Assert.True(Identical(plain, atRest), "identity pose changed the render");
    }

    [Fact]
    public void APosedRenderIsDeterministicAcrossWalks()
    {
        var stroke = BoundStroke();
        var posed = Skinning.PoseStroke(stroke, OneBone(), Rotate(25));

        using var first = Render(posed);
        using var second = Render(posed);
        Assert.True(Identical(first, second), "two renders of one pose disagree");

        // And across a reload, which is the shape replay actually takes.
        var doc = DocumentFactory.CreateDoc();
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = new Frame { Strokes = [posed] } }] });
        var back = Lightbox.Core.Serialization.DocJson.Deserialize(Lightbox.Core.Serialization.DocJson.Serialize(doc));
        using var reloaded = Render(back.Scene.Layers[^1].Cels[0].Frame!.Strokes[0]);
        Assert.True(Identical(first, reloaded), "a reload rendered a different pose");
    }

    [Fact]
    public void APoseActuallyMovesTheMark()
    {
        var stroke = BoundStroke();
        using var plain = Render(stroke);
        using var posed = Render(Skinning.PoseStroke(stroke, OneBone(), Rotate(40)));

        // The sanity check the saturation trap asks for: the feature does
        // something, as well as not doing the wrong thing.
        Assert.False(Identical(plain, posed), "a 40° pose left every pixel where it was");
    }

    [Fact]
    public void AMismatchedSeedPathFallsBackToOrdinarySeeding()
    {
        // A hand-edited document whose rest path lost a point must render,
        // not guess a correspondence.
        var stroke = BoundStroke();
        stroke.Weights = null;
        stroke.RestPoints = [new StrokePoint(0, 0, 1)];

        var dabs = BrushEngine.WalkDabs(stroke);
        Assert.True(dabs.Count > 0);
        Assert.All(dabs, d => Assert.Null(d.Seed));
    }
}
