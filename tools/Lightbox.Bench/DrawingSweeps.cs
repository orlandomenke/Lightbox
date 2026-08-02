using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Bench;

/// <summary>
/// The drawing and painting half: what happens as the canvas, the brush and
/// the mark get bigger.
/// </summary>
/// <remarks>
/// <para>
/// This area already has budgets, so the sweeps are not here to re-confirm
/// that a stroke is fast — they are here for the two things a budget cannot
/// say. Where does it stop being usable, and does the cost track the stroke or
/// the canvas. The second is charter O3 stated as a curve rather than as a
/// hope: an exponent near zero against canvas area is the invariant holding,
/// and one near one is it broken.
/// </para>
/// <para>
/// Everything is measured against a <b>live preview</b> or a <b>commit</b>,
/// because those are the two things an artist waits for. Nothing here
/// composites a frame — that is the animation half, and mixing them is how the
/// onion curve ended up belonging to the frame cache.
/// </para>
/// </remarks>
public static class DrawingSweeps
{
    private static Stroke Mark(double size, int dabs, int span, MediumSettings? medium = null) =>
        new()
        {
            Tool = ToolKind.Brush,
            Color = "#204060",
            Points = Enumerable.Range(0, dabs)
                // A bend rather than a straight line: charter O8, and a curve
                // is the only shape that exercises the arc walk in Densify.
                .Select(i => new StrokePoint(
                    80 + i * span,
                    200 + Math.Sin(i * 0.4) * 60,
                    0.4 + i % 3 * 0.3))
                .ToList(),
            Brush = new BrushSettings
            {
                Size = size, Hardness = 0.6, Opacity = 1, Flow = 0.9, Spacing = 0.08,
                PressureFlowGamma = 1,
                Medium = medium ?? new MediumSettings(),
            },
        };

    /// <summary>A canvas held for the life of one swept value, as the app holds one.</summary>
    private sealed class Sheet(int w, int h) : IDisposable
    {
        public SKImageInfo Info { get; } = new(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);

        public SKBitmap Layer { get; private set; } = new(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));

        public void Fresh()
        {
            Layer.Dispose();
            Layer = new SKBitmap(Info);
        }

        public void Dispose() => Layer.Dispose();
    }

    private static void Commit(Sheet sheet, Stroke stroke)
    {
        using var canvas = new SKCanvas(sheet.Layer);
        BrushEngine.StampStroke(canvas, stroke, sheet.Info, sheet.Layer);
        canvas.Flush();
    }

    public static IEnumerable<Scenario> All()
    {
        yield return CanvasArea();
        yield return BrushDiameter();
        yield return StrokeLength();
        yield return MediumFlowSteps();
    }

    // ---- the invariant, as a curve -------------------------------------------

    private static Scenario CanvasArea()
    {
        Sheet? sheet = null;
        var stroke = Mark(40, 20, 18);

        return new Scenario(
            "Commit a stroke, by canvas size",
            "canvas megapixels ×10",
            // 960×540, 1280×720, 1920×1080, 2560×1440, 3840×2160 — recorded as
            // tenths of a megapixel so the x axis is the thing that actually
            // grows rather than a width that hides a squaring.
            [5, 9, 21, 37, 83],
            Cadence.PerAction,
            Setup: n =>
            {
                var (w, h) = n switch
                {
                    5 => (960, 540),
                    9 => (1280, 720),
                    21 => (1920, 1080),
                    37 => (2560, 1440),
                    _ => (3840, 2160),
                };
                sheet = new Sheet(w, h);
                return sheet;
            },
            Work: _ =>
            {
                sheet!.Fresh();
                Commit(sheet, stroke);
            },
            Note: "**The exponent is the assertion here, not the time.** Charter O3 says a stroke costs "
                + "what the stroke touched and not what the canvas is; the same mark on a canvas sixteen "
                + "times the area should therefore be flat. Near 0 is the invariant holding. Near 1 means "
                + "something went full-canvas and every drawing on a big document just got slower.")
        {
            Iterations = 10,
        };
    }

    // ---- the two things an artist turns up -----------------------------------

    private static Scenario BrushDiameter()
    {
        Sheet? sheet = null;

        return new Scenario(
            "Commit a stroke, by brush size",
            "brush diameter px",
            [10, 25, 50, 100, 200, 300],
            Cadence.PerAction,
            Setup: _ =>
            {
                sheet = new Sheet(1920, 1080);
                return sheet;
            },
            Work: n =>
            {
                sheet!.Fresh();
                Commit(sheet, Mark(n, 20, 18));
            },
            Note: "Area, so n^2 is the honest answer and anything above it is a defect. 300 px is the "
                + "maximum the size field allows, so the last row is the worst an artist can ask for.")
        {
            Iterations = 10,
        };
    }

    private static Scenario StrokeLength()
    {
        Sheet? sheet = null;

        return new Scenario(
            "Commit a stroke, by how long it is",
            "dabs",
            [10, 25, 50, 100, 200, 400],
            Cadence.PerAction,
            Setup: _ =>
            {
                sheet = new Sheet(1920, 1080);
                return sheet;
            },
            Work: n =>
            {
                sheet!.Fresh();
                Commit(sheet, Mark(40, n, 6));
            },
            Note: "One long sweep across the page. Linear is right; superlinear would mean the dab walk "
                + "is re-doing work per dab, which is the shape the wet-media pass had before "
                + "PostProcessDabs and the one most likely to come back.")
        {
            Iterations = 10,
        };
    }

    // ---- the expensive option, swept ------------------------------------------

    private static Scenario MediumFlowSteps()
    {
        Sheet? sheet = null;

        return new Scenario(
            "Commit a watercolour stroke, by flow steps",
            "flow steps",
            [0, 2, 4, 8, 16, 32],
            Cadence.PerAction,
            Setup: _ =>
            {
                sheet = new Sheet(1920, 1080);
                return sheet;
            },
            Work: n =>
            {
                sheet!.Fresh();
                Commit(sheet, Mark(40, 20, 18, new MediumSettings
                {
                    Kind = MediumKind.Watercolour,
                    Wetness = 0.85, Viscosity = 0.1, Drag = 0.25, FlowSteps = n,
                    Absorbency = 0.35, EdgePull = 0.7,
                    PigmentDensity = 0.5, Granularity = 0.6, Hiding = 0.05,
                    Paper = PaperKind.ColdPress, PaperScale = 14, PaperInfluence = 0.7,
                    PressureWater = 0.8, Rewetting = 0.6,
                }));
            },
            Note: "32 is the clamp in MediumSimulator, so this sweeps the control to its stop. Linear in "
                + "steps is the design — each step is one pass over the lattice — and the row worth "
                + "reading is 0, which is what an artist pays for choosing a simulated medium at all "
                + "before asking it to flow anywhere.")
        {
            Iterations = 8,
        };
    }
}
