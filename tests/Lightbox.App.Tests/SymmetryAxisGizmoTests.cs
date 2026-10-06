using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The on-canvas symmetry gizmo's geometry, without a window.
/// </summary>
/// <remarks>
/// The thing that matters most here is the cross-check: the lines the gizmo
/// draws must be the lines the engine reflects across. Both are derived from
/// the same angles, but by different code — the gizmo in
/// <see cref="SymmetryAxisGizmo"/>, the engine in
/// <c>BrushEngine.SymmetryCopies</c> — and a gizmo that lies about where the
/// copy lands is worse than no gizmo.
/// </remarks>
public class SymmetryAxisGizmoTests
{
    private static readonly SKRect Page = new(0, 0, 400, 300);

    private static SymmetryAxis Axis(int order, bool mirror, double angle = 90, double cx = 200, double cy = 150) => new()
    {
        CenterX = cx, CenterY = cy, AngleDeg = angle, Order = order, Mirror = mirror,
    };

    [Fact]
    public void OrderOneWithAMirrorIsOneSolidLineThroughTheCentre()
    {
        var lines = SymmetryAxisGizmo.Lines(Axis(1, true), Page);

        var line = Assert.Single(lines);
        Assert.True(line.IsMirror);
        // Vertical, through the centre, and long enough to leave the page.
        Assert.Equal(200, line.From.X, 3);
        Assert.Equal(200, line.To.X, 3);
        Assert.True(line.From.Y < Page.Top && line.To.Y > Page.Bottom);
    }

    [Fact]
    public void OrderSixWithAMirrorIsSixMirrorLinesAndSixSpokes()
    {
        var lines = SymmetryAxisGizmo.Lines(Axis(6, true), Page);

        Assert.Equal(6, lines.Count(l => l.IsMirror));
        Assert.Equal(6, lines.Count(l => !l.IsMirror));
        // A spoke is a ray from the centre; a mirror line passes through it.
        Assert.All(lines.Where(l => !l.IsMirror), l => Assert.Equal(new SKPoint(200, 150), l.From));
    }

    [Fact]
    public void AnIdentityAxisStillShowsWhereItIs()
    {
        // Order one, mirror off: the engine does nothing, and the artist who
        // just unticked Mirror should see a dashed axis rather than nothing.
        var line = Assert.Single(SymmetryAxisGizmo.Lines(Axis(1, false), Page));
        Assert.False(line.IsMirror);
    }

    /// <summary>
    /// Every point on a drawn mirror line is fixed by one of the engine's
    /// reflection matrices — so the line is where the mark actually reflects.
    /// </summary>
    [Theory]
    [InlineData(1, 90)]
    [InlineData(1, 30)]
    [InlineData(3, 90)]
    [InlineData(4, 15)]
    public void TheMirrorLinesAreTheLinesTheEngineReflectsAcross(int order, double angle)
    {
        var axis = Axis(order, true, angle, 170, 120);
        var stroke = new Stroke { Symmetry = axis, Brush = new BrushSettings() };
        var copies = BrushEngine.SymmetryCopies(stroke);
        var reflections = copies.Where(m => m is { } mm && mm.ScaleX * mm.ScaleY - mm.SkewX * mm.SkewY < 0)
            .Select(m => m!.Value).ToList();
        Assert.Equal(order, reflections.Count);

        foreach (var line in SymmetryAxisGizmo.Lines(axis, Page).Where(l => l.IsMirror))
        {
            // A point partway along the line, away from the centre so the test
            // cannot pass on the centre alone.
            var p = new SKPoint(
                line.From.X + (line.To.X - line.From.X) * 0.8f,
                line.From.Y + (line.To.Y - line.From.Y) * 0.8f);
            var fixedBy = reflections.Count(m =>
            {
                var q = m.MapPoint(p);
                return Math.Abs(q.X - p.X) < 1e-2 && Math.Abs(q.Y - p.Y) < 1e-2;
            });
            Assert.True(fixedBy == 1, $"line at {angle}° order {order}: {fixedBy} reflections fix its points, expected exactly one");
        }
    }

    /// <summary>
    /// And the spokes: each is where one of the engine's rotations puts the
    /// first, so a dashed spoke points at where a turned copy actually lands.
    /// </summary>
    [Theory]
    [InlineData(3, 90)]
    [InlineData(6, 20)]
    public void TheSpokesAreWhereTheEngineTurnsTheMarkTo(int order, double angle)
    {
        var axis = Axis(order, false, angle, 170, 120);
        var stroke = new Stroke { Symmetry = axis, Brush = new BrushSettings() };
        var rotations = BrushEngine.SymmetryCopies(stroke).Where(m => m is not null).Select(m => m!.Value).ToList();
        // Order − 1 non-identity turns; the identity is the null the engine
        // keeps off Skia's transform path.
        Assert.Equal(order - 1, rotations.Count);

        var spokes = SymmetryAxisGizmo.Lines(axis, Page).Where(l => !l.IsMirror).Select(l => l.To).ToList();
        Assert.Equal(order, spokes.Count);
        var first = spokes[0];
        foreach (var m in rotations)
        {
            var turned = m.MapPoint(first);
            Assert.Contains(spokes, p => Math.Abs(p.X - turned.X) < 1e-2 && Math.Abs(p.Y - turned.Y) < 1e-2);
        }
    }

    [Fact]
    public void AHostileOrderIsBoundedBeforeAnythingIsAllocatedForIt()
    {
        // A file is input. An order of two billion must not loop that many
        // times per repaint; it is clamped where it is used and the record
        // keeps the number it was given.
        var axis = Axis(int.MaxValue, true);
        var lines = SymmetryAxisGizmo.Lines(axis, Page);
        Assert.Equal(2 * SymmetryAxis.MaxOrder, lines.Count);
        Assert.Equal(int.MaxValue, axis.Order);
        Assert.Equal(2 * SymmetryAxis.MaxOrder, axis.CopyCount);
        Assert.Equal(2 * SymmetryAxis.MaxOrder, axis.Placements().Length);
    }

    [Fact]
    public void TheRotateHandleSitsAlongTheAxisAndIsHitBeforeTheCentre()
    {
        var axis = Axis(1, true);
        const float scale = 2f;
        var handle = SymmetryAxisGizmo.RotateHandle(axis, SymmetryAxisGizmo.RotateOffsetPx / scale);

        // Vertical axis: the handle is straight below the centre, 44 screen px
        // away, which is 22 document px at 2x.
        Assert.Equal(200, handle.X, 3);
        Assert.Equal(150 + 22, handle.Y, 3);

        Assert.Equal(SymmetryHandle.Rotate, SymmetryAxisGizmo.HandleAt(axis, handle.X, handle.Y + 2, scale));
        Assert.Equal(SymmetryHandle.Centre, SymmetryAxisGizmo.HandleAt(axis, 203, 148, scale));
        // Between them, and off to the side: nothing, so the canvas still paints.
        Assert.Equal(SymmetryHandle.None, SymmetryAxisGizmo.HandleAt(axis, 200, 162, scale));
        Assert.Equal(SymmetryHandle.None, SymmetryAxisGizmo.HandleAt(axis, 240, 150, scale));
    }

    [Fact]
    public void TheHitZonesAreScreenSizedWhateverTheZoom()
    {
        var axis = Axis(1, true);
        // 9 screen px: at 0.5x that is 18 document px, at 4x it is 2.25.
        Assert.Equal(SymmetryHandle.Centre, SymmetryAxisGizmo.HandleAt(axis, 216, 150, 0.5f));
        Assert.Equal(SymmetryHandle.None, SymmetryAxisGizmo.HandleAt(axis, 216, 150, 4f));
    }

    [Fact]
    public void AngleTowardsFollowsThePointerAndSnapsOnRequest()
    {
        var axis = Axis(1, true, 90, 100, 100);

        // Straight right of the centre is 0°, straight below is 90° (y down).
        Assert.Equal(0, SymmetryAxisGizmo.AngleTowards(axis, 150, 100), 6);
        Assert.Equal(90, SymmetryAxisGizmo.AngleTowards(axis, 100, 150), 6);
        // Above is 270 rather than −90: one turn, never negative.
        Assert.Equal(270, SymmetryAxisGizmo.AngleTowards(axis, 100, 50), 6);

        Assert.Equal(45, SymmetryAxisGizmo.AngleTowards(axis, 150, 148, snapDeg: 15), 6);
        Assert.Equal(44.23, SymmetryAxisGizmo.AngleTowards(axis, 150, 148.65), 1);
    }
}
