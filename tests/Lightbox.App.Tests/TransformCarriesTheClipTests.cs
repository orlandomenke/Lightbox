using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B340 — a transform carries the stencil a stroke is cut by, not just the
/// stroke.
/// </summary>
/// <remarks>
/// <para>
/// A stroke painted while a marquee was up keeps a <c>ClipId</c> for ever
/// after; that is invariant 3, and it is what makes the mark re-render
/// identically on reload. The transform moved the points and left the clip
/// where it was, so the ink an artist saw during the drag and the ink they
/// had after the commit were <em>different parts of the same stroke</em>:
/// the moved geometry cut against a stencil standing still. On a rotation
/// it reads as one line jumping somewhere else while everything around it
/// turns correctly — reported that way, and the reason it looked like a
/// single-stroke fault is that only the strokes drawn under a selection
/// carry a clip at all.
/// </para>
/// <para>
/// The pixel assertion here is <b>preview against commit</b> rather than
/// against a stored image. The preview composites the drawing's own pixels
/// through the gizmo's matrix, so it is the transform's own statement of
/// what the result should be; a commit that disagrees with it is the bug
/// whatever the absolute numbers are. Rotating by exactly 90° keeps the
/// comparison honest — a resample at that angle is a permutation of pixels
/// rather than an interpolation, so any difference at all is geometry.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TransformCarriesTheClipTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>Two hard lines, the second painted under a marquee that cuts it.</summary>
    private static MainViewModel Drawn()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 20;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushScatter = 0;
        vm.BrushSizeJitter = 0;
        vm.BrushFlowJitter = 0;
        vm.BrushRotationJitter = 0;
        vm.BrushRoundnessJitter = 0;
        vm.BrushColorJitter = 0;
        vm.BrushGranulation = 0;
        vm.BrushWetEdge = 0;
        vm.AntiAliasing = false;
        Line(vm, 100, 150, 800, 150);
        vm.ApplySelectionShape(
            [new(300, 100, 1), new(600, 100, 1), new(600, 400, 1), new(300, 400, 1)], false, false);
        Line(vm, 100, 300, 800, 300);
        vm.DeselectCommand.Execute(null);
        return vm;
    }

    private static void Line(MainViewModel vm, double x0, double y0, double x1, double y1)
    {
        vm.BeginStroke(x0, y0, 1);
        vm.MoveStroke((x0 + x1) / 2, (y0 + y1) / 2, 1);
        vm.MoveStroke(x1, y1, 1);
        vm.EndStroke();
    }

    private static Stroke Clipped(MainViewModel vm) =>
        vm.PaintStrokes().Single(s => s.ClipId is not null);

    private static SKBitmap Snap(MainViewModel vm)
    {
        RenderSnapshot? latest = null;
        void Capture(RenderSnapshot s) => latest = s;
        vm.SnapshotChanged += Capture;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Capture;
        return SKBitmap.FromImage(latest!.Image);
    }

    /// <summary>Inked pixels, and where their weight sits.</summary>
    private static (long Count, double X, double Y) Ink(SKBitmap bmp)
    {
        long n = 0;
        double sx = 0, sy = 0;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y).Alpha <= 8) continue;
                n++;
                sx += x;
                sy += y;
            }
        }
        return (n, sx / Math.Max(1, n), sy / Math.Max(1, n));
    }

    /// <summary>The setup is only worth anything if one stroke really is clipped.</summary>
    [AvaloniaFact]
    public void PaintingUnderAMarqueeLeavesAClipBehind()
    {
        var vm = Drawn();
        Assert.False(vm.HasSelection);
        Assert.Equal(2, vm.PaintStrokes().Count);
        Assert.NotNull(Clipped(vm).ClipId);
    }

    /// <summary>
    /// A whole-layer rotation: the commit must show what the drag showed.
    /// </summary>
    [AvaloniaFact]
    public void AWholeLayerRotationEndsWhereTheDragSaidItWould()
    {
        var vm = Drawn();
        Assert.True(vm.BeginTransform(), vm.AiStatus);

        double px = vm.Doc.Scene.Width / 2.0, py = vm.Doc.Scene.Height / 2.0;
        var quarter = Math.PI / 2;
        var m = SKMatrix.CreateTranslation((float)-px, (float)-py);
        m = m.PostConcat(SKMatrix.CreateRotation((float)quarter));
        m = m.PostConcat(SKMatrix.CreateTranslation((float)px, (float)py));
        vm.PreviewTransform(m);
        using var preview = Snap(vm);
        vm.CommitTransformAffine(px, py, 1, 1, quarter, 0, 0);
        using var committed = Snap(vm);

        var (was, wasX, wasY) = Ink(preview);
        var (now, nowX, nowY) = Ink(committed);
        output.WriteLine($"preview ink={was} at ({wasX:F1},{wasY:F1}); commit ink={now} at ({nowX:F1},{nowY:F1})");

        // The clip stayed put before this fix: the moved line was carved
        // against the boundary it used to sit behind, so ink went missing and
        // what survived sat somewhere else.
        Assert.InRange(now, was * 0.99, was * 1.01);
        Assert.InRange(nowX, wasX - 1, wasX + 1);
        Assert.InRange(nowY, wasY - 1, wasY + 1);
    }

    /// <summary>
    /// The record's own statement of the same thing: the stencil moved by
    /// exactly what the ink moved by.
    /// </summary>
    [AvaloniaFact]
    public void TheStencilMovesByWhatTheInkMovedBy()
    {
        var vm = Drawn();
        var before = ClipRegionRegistry.Resolve(Clipped(vm).ClipId!)!;
        var wasLeft = before.Contours.SelectMany(c => c).Min(p => p.X);

        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 210, 0);

        var after = ClipRegionRegistry.Resolve(Clipped(vm).ClipId!)!;
        Assert.Equal(wasLeft + 210, after.Contours.SelectMany(c => c).Min(p => p.X), 3);
    }

    /// <summary>
    /// Invariant 3: the region the strokes now name has to be in the document,
    /// or a reload renders the mark unclipped — the whole line back.
    /// </summary>
    [AvaloniaFact]
    public void TheCarriedStencilIsPartOfTheRecord()
    {
        var vm = Drawn();
        var before = Clipped(vm).ClipId!;
        Assert.True(vm.BeginTransform(), vm.AiStatus);
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 210, 0);

        // A region that has moved is a different region, so it has a different
        // content hash — and the document has to be holding it by then.
        var after = Clipped(vm).ClipId!;
        Assert.NotEqual(before, after);
        Assert.Contains(after, vm.Doc.ClipRegions.Keys);
    }

    /// <summary>
    /// The Move tool commits down the same path, so it had the same fault.
    /// </summary>
    [AvaloniaFact]
    public void TheMoveToolCarriesItToo()
    {
        var vm = Drawn();
        var before = ClipRegionRegistry.Resolve(Clipped(vm).ClipId!)!;
        var wasTop = before.Contours.SelectMany(c => c).Min(p => p.Y);

        Assert.True(vm.BeginMove(400, 300, wholeLayer: true));
        vm.UpdateMove(400, 380, axisLock: false);
        vm.EndMove();

        var after = ClipRegionRegistry.Resolve(Clipped(vm).ClipId!)!;
        Assert.Equal(wasTop + 80, after.Contours.SelectMany(c => c).Min(p => p.Y), 3);
    }
}
