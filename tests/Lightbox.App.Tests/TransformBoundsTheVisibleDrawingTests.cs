using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B302: the transform gizmo boxes the drawing as it looks, not as the record
/// remembers it.
/// </summary>
/// <remarks>
/// <para>
/// Filed as a cosmetic bug — handles wrapping erased ink on a plain Ctrl+T —
/// and the entry's own note found the half that is not cosmetic underneath it:
/// <c>TransformOps.Bounds</c> walks stroke points and never looks at
/// <c>PngBase64</c>, so a frame that is <em>nothing but imported pixels</em>
/// measures as empty and the transform refuses it outright. An imported
/// drawing could not be transformed at all, and the failure wore the same
/// message as an honestly empty layer, which is why nobody had reported it.
/// </para>
/// <para>
/// Both wanted the same answer, so both are tested here rather than split: the
/// box is the visible drawing, baseline included, ink judged per point.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TransformBoundsTheVisibleDrawingTests(ITestOutputHelper output)
    : BrushStateIsolated
{
    private static MainViewModel Painted()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 16;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        return vm;
    }

    private static Frame ActiveFrame(MainViewModel vm) =>
        (Frame)vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!;

    /// <summary>The box the gizmo is raised with.</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY)? GizmoBox(MainViewModel vm)
    {
        (double, double, double, double)? box = null;
        void Seen(double a, double b, double c, double d) => box = (a, b, c, d);
        vm.TransformBegun += Seen;
        try
        {
            return vm.BeginTransform() ? box : null;
        }
        finally
        {
            vm.TransformBegun -= Seen;
        }
    }

    // ---- the half that was filed: erased ink inflates the box ---------------

    /// <summary>
    /// A line drawn far to the right and rubbed out does not stretch the box.
    /// </summary>
    /// <remarks>
    /// The reported symptom: on a reworked drawing the handles sit well outside
    /// anything visible, because the record still holds the rubbed-out version.
    /// Cosmetic — everything moves together, so no pixel lands wrong — and
    /// unnerving, because the box is the only thing on screen claiming to say
    /// what the gesture has hold of.
    /// </remarks>
    [AvaloniaFact]
    public void ARubbedOutLineDoesNotStretchTheGizmo()
    {
        var vm = Painted();
        vm.BeginStroke(100, 200, 1);
        vm.MoveStroke(200, 200, 1);
        vm.EndStroke();

        // A second line far to the right, then rubbed out along its length.
        vm.BeginStroke(600, 200, 1);
        vm.MoveStroke(700, 200, 1);
        vm.EndStroke();
        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 60;
        vm.BeginStroke(580, 200, 1);
        vm.MoveStroke(720, 200, 1);
        vm.EndStroke();
        vm.ActiveTool = ToolId.Brush;

        var box = GizmoBox(vm);
        Assert.NotNull(box);
        output.WriteLine($"gizmo {box}");
        // The visible line ends near x=200; the rubbed-out one reached 700.
        Assert.True(
            box!.Value.MaxX < 400,
            $"the box still wraps the rubbed-out line (right edge {box.Value.MaxX:0})");
        Assert.True(box.Value.MinX < 110, $"it lost the visible line (left edge {box.Value.MinX:0})");
    }

    /// <summary>
    /// A half-rubbed line keeps a box round the half you can still see.
    /// </summary>
    /// <remarks>
    /// The case that rules out the obvious fix. `StrokeRecordCleaner` drops a
    /// stroke once an eraser covers 85% of it, so it answers this one with
    /// "the whole line" — too big — and answers a fully-rubbed line with
    /// "nothing", which is how a baseline-plus-erased-strokes drawing would
    /// have stopped being transformable. Per point, the answer is neither.
    /// </remarks>
    [AvaloniaFact]
    public void AHalfRubbedLineIsBoxedRoundWhatSurvives()
    {
        var vm = Painted();
        vm.BeginStroke(100, 200, 1);
        vm.MoveStroke(300, 200, 1);
        vm.MoveStroke(500, 200, 1);
        vm.EndStroke();

        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 80;
        vm.BeginStroke(400, 200, 1);
        vm.MoveStroke(560, 200, 1);
        vm.EndStroke();
        vm.ActiveTool = ToolId.Brush;

        var box = GizmoBox(vm);
        Assert.NotNull(box);
        output.WriteLine($"gizmo {box}");
        Assert.True(
            box!.Value.MaxX < 480,
            $"the box kept the rubbed-out end (right edge {box.Value.MaxX:0})");
        Assert.True(
            box.Value.MaxX > 250,
            $"the box lost ink that is still on the canvas (right edge {box.Value.MaxX:0})");
    }

    // ---- the half that was silent: an imported drawing ----------------------

    /// <summary>
    /// A frame that is nothing but imported pixels can be transformed.
    /// </summary>
    /// <remarks>
    /// On `main` this returns false and says "Nothing to transform in this
    /// scope." — the same sentence an empty layer gets, which is exactly why a
    /// whole class of document being untransformable went unreported.
    /// </remarks>
    [AvaloniaFact]
    public void AnImportedDrawingCanBeTransformed()
    {
        var vm = Painted();
        var frame = ActiveFrame(vm);
        frame.Strokes.Clear();
        frame.PngBase64 = SolidPng(vm.Doc.Scene.Width, vm.Doc.Scene.Height);

        var box = GizmoBox(vm);

        output.WriteLine($"gizmo {(box?.ToString() ?? "refused: " + vm.AiStatus)}");
        Assert.NotNull(box);
        // Bounded by the paper: the record cannot say where a baseline's ink
        // is, only that it covers the page.
        Assert.Equal(0, box!.Value.MinX, 3);
        Assert.Equal(vm.Doc.Scene.Width, box.Value.MaxX, 3);
    }

    /// <summary>
    /// Baseline plus wholly-erased strokes — the combination the rejected fix
    /// would have broken.
    /// </summary>
    [AvaloniaFact]
    public void AnImportedDrawingWithOnlyErasedStrokesCanStillBeTransformed()
    {
        var vm = Painted();
        vm.BeginStroke(600, 200, 1);
        vm.MoveStroke(700, 200, 1);
        vm.EndStroke();
        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 60;
        vm.BeginStroke(580, 200, 1);
        vm.MoveStroke(720, 200, 1);
        vm.EndStroke();
        vm.ActiveTool = ToolId.Brush;

        ActiveFrame(vm).PngBase64 = SolidPng(vm.Doc.Scene.Width, vm.Doc.Scene.Height);

        var box = GizmoBox(vm);
        output.WriteLine($"gizmo {(box?.ToString() ?? "refused: " + vm.AiStatus)}");
        Assert.NotNull(box);
    }

    /// <summary>
    /// An honestly empty layer is still refused, in the same words.
    /// </summary>
    /// <remarks>
    /// The guard that keeps this fix from becoming "always say yes". Nothing
    /// drawn and no baseline is the one case where "Nothing to transform in
    /// this scope" is the truth.
    /// </remarks>
    [AvaloniaFact]
    public void AnEmptyLayerIsStillRefused()
    {
        var vm = Painted();
        ActiveFrame(vm).Strokes.Clear();

        Assert.Null(GizmoBox(vm));
        Assert.Contains("Nothing to transform", vm.AiStatus);
    }

    // ---- the region case, which was already correct ------------------------

    /// <summary>
    /// With a marquee up the baseline is left out, because it does not move.
    /// </summary>
    /// <remarks>
    /// A region-limited commit moves strokes and leaves baseline pixels where
    /// they are. A gizmo drawn round the page would box pixels the drag is not
    /// going to take — which is the same class of lie as boxing erased ink,
    /// pointed the other way.
    /// </remarks>
    [AvaloniaFact]
    public void UnderAMarqueeTheBaselineIsNotBoxed()
    {
        var vm = Painted();
        vm.BeginStroke(100, 200, 1);
        vm.MoveStroke(200, 200, 1);
        vm.EndStroke();
        ActiveFrame(vm).PngBase64 = SolidPng(vm.Doc.Scene.Width, vm.Doc.Scene.Height);

        vm.ApplySelectionShape(
            [new(80, 150, 1), new(220, 150, 1), new(220, 250, 1), new(80, 250, 1)],
            add: false, subtract: false);

        var box = GizmoBox(vm);
        Assert.NotNull(box);
        output.WriteLine($"gizmo {box}");
        Assert.True(
            box!.Value.MaxX < 400,
            $"the box wrapped the baseline the drag will not move (right edge {box.Value.MaxX:0})");
    }

    /// <summary>A fully opaque page, as a PNG the frame can carry.</summary>
    private static string SolidPng(int width, int height)
    {
        using var bmp = new SKBitmap(new SKImageInfo(
            width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(new SKColor(0x20, 0x40, 0x80, 0xFF));
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(data.ToArray());
    }
}
