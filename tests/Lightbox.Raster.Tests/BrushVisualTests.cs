using Lightbox.Core.Documents;
using Lightbox.Testing;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The same properties the pixel tests assert, rendered as contact sheets a person can look at.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every test here also asserts.</b> A sheet nobody checks is worse than no sheet, because it
/// looks like coverage — so each of these fails on its own, and the picture is what makes a failure
/// diagnosable instead of merely red. Run with <c>LIGHTBOX_VISUALS</c> pointing at a directory to
/// get the images; without it these are ordinary tests that write nothing.
/// </para>
/// <para>
/// The set is chosen from what actually went wrong in this file's history rather than from what is
/// easy to draw: a live preview that did not match its commit (B54), a smudge that moved when the
/// output scale changed (B57), and a simulated medium that lost half its mark and was blamed on the
/// wrong parameter (B50). All three were invisible to the suite and obvious in a picture.
/// </para>
/// </remarks>
[Collection("Registries")]
[Trait("Category", "Visual")]
public class BrushVisualTests(ITestOutputHelper output)
{
    private const int W = 400, H = 200;

    /// <summary>Something with structure to work on: a bar, checkered so a blur has an effect.</summary>
    /// <remarks>
    /// Uniform colour is the trap here — blurring or smudging it is a no-op by construction, and
    /// `BUGS.md` records that costing B49 two wrong probes and B57 one. Structure is not decoration.
    /// </remarks>
    private static SKBitmap Bar()
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var body = new SKPaint { Color = new SKColor(0x20, 0xC0, 0x40, 255), IsAntialias = false };
        canvas.DrawRect(new SKRect(60, 70, 340, 130), body);
        using var stripe = new SKPaint { Color = new SKColor(0x10, 0x10, 0x10, 200) };
        for (var x = 60; x < 340; x += 20) canvas.DrawRect(new SKRect(x, 70, x + 10, 130), stripe);
        canvas.Flush();
        return bmp;
    }

    private static Stroke Effect(BrushKind kind, double size, bool diagonal) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = diagonal
            ? [.. Enumerable.Range(0, 9).Select(i => new StrokePoint(100 + i * 20, 40 + i * 10, 1))]
            : [.. Enumerable.Range(0, 9).Select(i => new StrokePoint(120 + i * 20, 100, 1))],
        Brush = new BrushSettings
        {
            Kind = kind, Size = size, Hardness = 1, Flow = kind == BrushKind.Blur ? 0.6 : 1,
            Spacing = 0.1, SmudgeLength = 0.9, SmudgeRadius = 0.7, ColorRate = 0,
        },
    };

    /// <summary>Worst per-pixel difference over white paper, and how many pixels differ.</summary>
    private static (int Worst, int Pixels) Compare(SKBitmap a, SKBitmap b)
    {
        int worst = 0, pixels = 0;
        for (var y = 0; y < Math.Min(a.Height, b.Height); y++)
        {
            for (var x = 0; x < Math.Min(a.Width, b.Width); x++)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                if (pa == pb) continue;
                pixels++;
                worst = Math.Max(worst, Math.Abs(pa.Red - pb.Red));
                worst = Math.Max(worst, Math.Abs(pa.Alpha - pb.Alpha));
            }
        }
        return (worst, pixels);
    }

    /// <summary>
    /// What a drag shows against what it commits — B54, with the rim visible.
    /// </summary>
    /// <remarks>
    /// The zoomed pair is the point of the sheet. Coverage being exact is a number
    /// (<c>ADraggedBlurMatchesTheCommittedOne</c> asserts it), but the residue B54 chose to accept
    /// lives on a rim one or two pixels wide, and 127 of 255 on a rim is either invisible or glaring
    /// depending on where it falls. At ×8 a person can say which.
    /// </remarks>
    [Theory]
    [InlineData(40, false)]
    [InlineData(147, true)]
    public void ADraggedBlurLooksLikeTheBlurThatCommits(double size, bool diagonal)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var stroke = Effect(BrushKind.Blur, size, diagonal);

        using var committed = Bar();
        using (var canvas = new SKCanvas(committed))
        {
            BrushEngine.StampStroke(canvas, stroke, info, committed);
            canvas.Flush();
        }

        // The application's own feed: whole stroke walked once per event, only the unsettled dabs
        // stamped. Wrong here and the sheet is a picture of the wrong thing.
        using var dragged = Bar();
        using var preStroke = Bar();
        var densify = new Lightbox.Core.Geometry.IncrementalDensify();
        List<BrushEngine.Dab>? previous = null;
        var settled = 0;
        for (var n = 2; n <= stroke.Points.Count; n++)
        {
            var soFar = new Stroke
            {
                Tool = stroke.Tool, Color = stroke.Color, Brush = stroke.Brush,
                Points = [.. stroke.Points.Take(n)],
            };
            var walk = BrushEngine.WalkDabs(soFar, densify);
            FrameRasterizer.AppendDraft(dragged, soFar, preStroke, walk, settled);
            settled = BrushEngine.StableCount(walk, previous);
            previous = walk;
        }

        var (worst, pixels) = Compare(committed, dragged);
        var name = $"blur-drag-vs-commit-{size:0}{(diagonal ? "-diagonal" : "")}";
        output.WriteLine($"{name}: {pixels} px differ, worst {worst} of 255");

        if (VisualSheet.Wanted)
        {
            using var flatDrag = VisualSheet.OverPaper(dragged);
            using var flatCommit = VisualSheet.OverPaper(committed);
            VisualSheet.WriteComparison(
                name,
                $"B54 — {size:0} px blur, {(diagonal ? "diagonal across the bar's edge" : "along the bar")}: "
                + $"{pixels} px differ, worst {worst}/255",
                "while dragging", flatDrag, "after release", flatCommit);

            // And the rim, where the accepted residue lives.
            var rim = diagonal ? new SKRectI(150, 60, 230, 120) : new SKRectI(150, 60, 230, 120);
            using var zoomDrag = VisualSheet.Zoom(flatDrag, rim, 8);
            using var zoomCommit = VisualSheet.Zoom(flatCommit, rim, 8);
            VisualSheet.WriteComparison(
                name + "-rim",
                $"B54 — the same {size:0} px blur at ×8, where the residue B54 accepted lives",
                "while dragging", zoomDrag, "after release", zoomCommit, amplify: 3);
        }

        // The assertion the sheet illustrates: coverage exact, values within the recorded residue.
        Assert.True(
            worst <= 130,
            $"the drag differs from the commit by {worst} of 255 over {pixels} px — beyond B54's residue");
    }

    /// <summary>
    /// An effect brush lands where it was dragged at every output scale — B57, drawn.
    /// </summary>
    /// <remarks>
    /// This is the defect a picture explains instantly and a number does not: at 2× the smudge was
    /// simply somewhere else on the canvas, because <c>LerpDab</c> works in device space and
    /// <c>StampSmudge</c> scaled the canvas as well. The 2× panel is resampled back down so the two
    /// are the same size, which is the only way "in the same place" is a thing you can see.
    /// </remarks>
    [Theory]
    [InlineData(BrushKind.Smudge)]
    [InlineData(BrushKind.Blur)]
    public void AnEffectBrushLandsInTheSamePlaceWhateverTheOutputScale(BrushKind kind)
    {
        SKBitmap Render(double scale)
        {
            var bar = new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#20c040",
                Points = [new StrokePoint(40, 70, 1), new StrokePoint(160, 70, 1)],
                Brush = new BrushSettings { Size = 34, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
            };
            // Starting **inside** the bar, not above it. A smudge that begins on bare canvas picks
            // up nothing and drags nothing across the mark, which is correct behaviour and reads as
            // an eraser — a demonstration that answers a question nobody asked. Beginning in the
            // colour makes the panel show the smear as well as where it landed.
            var effect = new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#000000",
                Points = [.. Enumerable.Range(0, 9).Select(i => new StrokePoint(60 + i * 12, 66 + i * 6, 1))],
                Brush = new BrushSettings
                {
                    Kind = kind, Size = 26, Hardness = 1, Flow = kind == BrushKind.Blur ? 0.6 : 1,
                    Spacing = 0.1, SmudgeLength = 0.9, SmudgeRadius = 0.7, ColorRate = 0,
                },
            };
            return FrameRasterizer.Rasterize([bar, effect], W, H, scale);
        }

        using var once = Render(1.0);
        using var twice = Render(2.0);

        // Down to 1× so the two can be laid side by side and subtracted.
        using var twiceSmall = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var image = SKImage.FromBitmap(twice))
        {
            image.ScalePixels(twiceSmall.PeekPixels(), new SKSamplingOptions(SKFilterMode.Linear));
        }

        var (worst, pixels) = Compare(once, twiceSmall);
        output.WriteLine($"{kind} 1x vs 2x-downsampled: {pixels} px differ, worst {worst} of 255");

        if (VisualSheet.Wanted)
        {
            using var flatOnce = VisualSheet.OverPaper(once);
            using var flatTwice = VisualSheet.OverPaper(twiceSmall);
            VisualSheet.WriteComparison(
                $"scale-{kind}".ToLowerInvariant(),
                $"B57 — {kind} rendered at 1× and at 2×, the 2× resampled back down. "
                + "A displacement here is the bug; softness is not.",
                "1× render", flatOnce, "2× render, downsampled", flatTwice, amplify: 2);
        }

        // Resampling never matches exactly, so this is deliberately loose — it is the *displacement*
        // that mattered, and a displaced smudge showed as thousands of pixels, not tens.
        Assert.True(
            pixels < W * H / 8,
            $"{kind} at 2× differs from 1× over {pixels} px — the mark has moved, not merely softened");
    }

    /// <summary>
    /// The simulated media side by side, at true size — B50's faintness, visible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The measured claim is that the watercolour lattice loses about half of what it is given: the
    /// same brush with the simulation off peaks at 137/255 and with it on at 51, while the area
    /// grows 1.7×. Numbers said that; this sheet is what makes it a decision — an artist can look
    /// at the strip and say whether the wash is *wrong* or merely *wet*, which is the half of B50
    /// that is expression rather than engineering.
    /// </para>
    /// <para>
    /// The last panel is the same watercolour brush with <c>Kind = None</c>. It is not a proposal —
    /// it has none of the wash's character — it is the reference for how much mark went missing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSimulatedMediaCanBeComparedAtTrueSize()
    {
        var strip = new List<VisualSheet.Panel>();
        var peaks = new List<string>();
        var sheets = new List<SKBitmap>();

        void Add(string label, BrushSettings brush)
        {
            var info = new SKImageInfo(300, 120, SKColorType.Rgba8888, SKAlphaType.Premul);
            var layer = new SKBitmap(info);
            using (var canvas = new SKCanvas(layer))
            {
                canvas.Clear(SKColors.Transparent);
                var stroke = new Stroke
                {
                    Tool = ToolKind.Brush,
                    Color = "#101828",
                    Points = [.. Enumerable.Range(0, 10).Select(i => new StrokePoint(40 + i * 24.0, 60, 0.9))],
                    Brush = brush,
                };
                BrushEngine.StampStroke(canvas, stroke, info, layer);
                canvas.Flush();
            }

            var peak = 0;
            for (var y = 0; y < layer.Height; y++)
            {
                for (var x = 0; x < layer.Width; x++)
                {
                    var p = layer.GetPixel(x, y);
                    if (p.Alpha == 0) continue;
                    peak = Math.Max(peak, 255 - (p.Red * p.Alpha + 255 * (255 - p.Alpha)) / 255);
                }
            }
            peaks.Add($"{label} {peak}");
            var flat = VisualSheet.OverPaper(layer);
            layer.Dispose();
            sheets.Add(flat);
            strip.Add(new VisualSheet.Panel($"{label} — peak {peak}/255", flat));
        }

        Add("Watercolor (simulated)", Presets.WatercolourWet());
        Add("Watercolor (flat)", Presets.WatercolourFlat());
        Add("Gouache (simulated)", Presets.GouacheBody());
        var unsimulated = Presets.WatercolourWet();
        unsimulated.Medium.Kind = MediumKind.None;
        Add("same brush, simulation off", unsimulated);

        output.WriteLine("peak darkening over white: " + string.Join(", ", peaks));
        VisualSheet.Write(
            "media-strip",
            "B50 — the simulated media at true size. The wash is the faint one, and the last panel "
            + "is the same brush with the lattice switched off: that gap is the missing mark.",
            [.. strip]);
        foreach (var s in sheets) s.Dispose();

        // The assertion, and it is the one B50 is actually about: the simulated watercolour is
        // markedly fainter than its own unsimulated self. It fails when B50 is fixed, and that is
        // the point — the sheet and the number stop disagreeing with the ledger together.
        var simulated = peaks[0].Split(' ')[^1];
        var off = peaks[^1].Split(' ')[^1];
        Assert.True(
            int.Parse(simulated) < int.Parse(off),
            $"the simulated watercolour ({simulated}/255) is no longer fainter than the same brush "
            + $"with the lattice off ({off}/255) — B50 has changed and its entry needs re-measuring");
    }
}

/// <summary>
/// The shipped presets these sheets are about, copied rather than referenced.
/// </summary>
/// <remarks>
/// <c>BrushPresets</c> lives in <c>Lightbox.App</c> and this is a Raster suite, so referencing it
/// would invert the dependency. Copied deliberately and narrowly: if the shipped preset changes and
/// this does not, the sheet stops describing the application — which
/// <c>BrushCatalogueTests</c> in the App suite is the place to catch, not this one.
/// </remarks>
internal static class Presets
{
    internal static BrushSettings WatercolourWet() => new()
    {
        Size = 42, Hardness = 0.25, Opacity = 0.55, Flow = 0.45, Spacing = 0.08,
        PressureFlowGamma = 0.8, SizeJitter = 0.15, RoundnessJitter = 0.2,
        Medium = new MediumSettings
        {
            Kind = MediumKind.Watercolour,
            Wetness = 0.85, Viscosity = 0.1, Drag = 0.25, FlowSteps = 16,
            Absorbency = 0.35, EdgePull = 0.06,
            PigmentDensity = 0.5, Granularity = 0.6, Hiding = 0.05,
            Paper = PaperKind.ColdPress, PaperScale = 14, PaperInfluence = 0.7,
            PressureWater = 0.8, Rewetting = 0.6,
        },
    };

    internal static BrushSettings WatercolourFlat() => new()
    {
        Size = 22, Hardness = 0.4, Opacity = 0.65, Flow = 0.5, Spacing = 0.1,
        WetEdge = 0.7, Granulation = 0.35, PressureFlowGamma = 0.8,
    };

    internal static BrushSettings GouacheBody() => new()
    {
        Size = 26, Hardness = 0.7, Opacity = 0.95, Flow = 0.85, Spacing = 0.1,
        AngleFollowsDirection = true, SizeJitter = 0.1,
        Medium = new MediumSettings
        {
            Kind = MediumKind.Gouache,
            Wetness = 0.35, Viscosity = 0.55, Drag = 0.6, FlowSteps = 6,
            Absorbency = 0.5, EdgePull = 0.05,
            PigmentDensity = 0.95, Granularity = 0.2, Hiding = 0.85,
            Paper = PaperKind.ColdPress, PaperScale = 10, PaperInfluence = 0.25,
            PressureWater = 0.2, Rewetting = 0.2,
        },
    };
}
