using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Tests.Geometry;

public class GeometryTests
{
    [Fact]
    public void Resample_ProducesExactCount()
    {
        var pts = new List<StrokePoint> { new(0, 0, 0.5), new(10, 0, 0.5), new(20, 0, 0.5) };
        foreach (var n in new[] { 2, 5, 64, 100 })
            Assert.Equal(n, GeometryOps.Resample(pts, n).Count);
    }

    [Fact]
    public void Resample_PreservesEndpoints()
    {
        var pts = new List<StrokePoint> { new(0, 0, 0.1), new(5, 7, 0.5), new(20, 3, 0.9) };
        var r = GeometryOps.Resample(pts, 16);
        Assert.Equal(pts[0], r[0]);
        Assert.Equal(pts[^1].X, r[^1].X, 6);
        Assert.Equal(pts[^1].Y, r[^1].Y, 6);
    }

    [Fact]
    public void Resample_SpacingIsEvenByArcLength()
    {
        var pts = new List<StrokePoint> { new(0, 0, 0.5), new(100, 0, 0.5) };
        var r = GeometryOps.Resample(pts, 11);
        for (var i = 1; i < r.Count; i++)
        {
            var d = GeometryOps.Dist(r[i - 1], r[i]);
            Assert.InRange(d, 9.999, 10.001);
        }
    }

    [Fact]
    public void Resample_Degenerates()
    {
        Assert.Empty(GeometryOps.Resample([], 8));

        var single = GeometryOps.Resample([new StrokePoint(3, 4, 0.7)], 8);
        Assert.Equal(8, single.Count);
        Assert.All(single, p => Assert.Equal(new StrokePoint(3, 4, 0.7), p));

        // Zero-length path (all points identical).
        var zero = GeometryOps.Resample([new StrokePoint(1, 1, 0.5), new StrokePoint(1, 1, 0.5)], 4);
        Assert.Equal(4, zero.Count);
        Assert.All(zero, p => Assert.Equal(1, p.X));
    }

    [Fact]
    public void Resample_IsDeterministic()
    {
        var pts = Enumerable.Range(0, 30)
            .Select(i => new StrokePoint(Math.Sin(i * 0.4) * 50 + 60, i * 3.7, 0.5 + 0.4 * Math.Sin(i)))
            .ToList();
        var a = GeometryOps.Resample(pts, 64);
        var b = GeometryOps.Resample(pts, 64);
        Assert.Equal(a, b);
    }

    [Fact]
    public void PathLength_SumsSegments()
    {
        var pts = new List<StrokePoint> { new(0, 0, 0.5), new(3, 4, 0.5), new(3, 14, 0.5) };
        Assert.Equal(15, GeometryOps.PathLength(pts), 9);
    }

    [Fact]
    public void Centroid_AveragesPositions()
    {
        var c = GeometryOps.Centroid([new StrokePoint(0, 0, 1), new StrokePoint(10, 20, 0)]);
        Assert.Equal(5, c.X);
        Assert.Equal(10, c.Y);
    }

    [Fact]
    public void Smooth_PreservesEndpointsAndCount()
    {
        var pts = new List<StrokePoint> { new(0, 0, 0.5), new(10, 30, 0.5), new(20, 0, 0.5) };
        var s = GeometryOps.Smooth(pts, 2);
        Assert.Equal(pts.Count, s.Count);
        Assert.Equal(pts[0], s[0]);
        Assert.Equal(pts[^1], s[^1]);
        Assert.True(s[1].Y < pts[1].Y); // the spike is pulled down
    }
}

public class ColorTests
{
    [Theory]
    [InlineData("#ffffff", 255, 255, 255)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#ff8800", 255, 136, 0)]
    [InlineData("fff", 255, 255, 255)]
    [InlineData("#abc", 170, 187, 204)]
    public void HexToRgb_Parses(string hex, int r, int g, int b)
    {
        var (pr, pg, pb) = ColorOps.HexToRgb(hex);
        Assert.Equal((r, g, b), ((int)pr, (int)pg, (int)pb));
    }

    [Fact]
    public void HexToRgb_Invalid_Throws() =>
        Assert.Throws<FormatException>(() => ColorOps.HexToRgb("#12345"));

    [Fact]
    public void LerpColor_Endpoints()
    {
        Assert.Equal("#000000", ColorOps.LerpColor("#000000", "#ffffff", 0));
        Assert.Equal("#ffffff", ColorOps.LerpColor("#000000", "#ffffff", 1));
        Assert.Equal("#808080", ColorOps.LerpColor("#000000", "#ffffff", 0.5));
    }
}
