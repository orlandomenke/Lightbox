using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Ai.Tests;

/// <summary>
/// The measurement the pixel-context question is gated on: <b>is a drawing
/// cheaper, and no worse, to send as a picture than as stroke JSON?</b>
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/DESIGN-ai-payload.md</c> settled that images are ~87% of a request's
/// bytes and ~5% of its tokens while strokes are the reverse, and named item 3 —
/// <i>send the strokes that need judgement, not the frame</i> — the 6× win worth
/// building. What it could not say is what the context around those strokes
/// should be made of. This is that arm.
/// </para>
/// <para>
/// <b>Three ways to send the same drawing</b>, which is the whole design:
/// </para>
/// <list type="table">
///   <item><term>json</term><description>every stroke of both keys as JSON. Production today.</description></item>
///   <item><term>split</term><description>only the strokes that move as JSON, plus a render of each key. Item 3.</description></item>
///   <item><term>pixels</term><description>the keys as pictures and nothing else. What a pure pixel path could offer.</description></item>
/// </list>
/// <para>
/// <b>The cost half needs no provider and runs everywhere.</b> Bytes and tokens
/// are arithmetic, and all three arms are built from one fixture so the drawing
/// is held constant while only its encoding moves. The quality half needs
/// <c>LIGHTBOX_MEASURE_KEY</c> and is skipped without it — the same gate, the
/// same key and the same session as <see cref="TaxonomyMeasurementTests"/>,
/// which is why the two were built together.
/// </para>
/// <para>
/// <b>The split arm's prompt is a probe and deliberately does not live in
/// <c>Prompts</c>.</b> Nothing in the application sends a request in this shape,
/// and adding a production method nothing calls is how <c>DrawAsync</c> survived
/// eleven milestones. If this arm wins, the wording goes to <c>ai-engineer</c>
/// and <c>art-director</c> under G12 before it becomes a code path — a
/// measurement says which encoding is cheaper, never whether the prompt is right.
/// </para>
/// </remarks>
public class PixelContextMeasurementTests(ITestOutputHelper output)
{
    private const int W = 512;
    private const int H = 512;

    /// <summary>The strokes that actually move between the two keys.</summary>
    private static readonly string[] Moving = ["near-arm", "far-arm"];

    private static string? Key => Environment.GetEnvironmentVariable("LIGHTBOX_MEASURE_KEY");

    private static string Model =>
        Environment.GetEnvironmentVariable("LIGHTBOX_MEASURE_MODEL") ?? AnthropicArtist.Model;

    // ---- the fixture --------------------------------------------------------

    /// <summary>
    /// A figure whose arms swing and whose everything else holds — the shape
    /// almost every inbetween actually has, and the one the payload document had
    /// in mind when it said "in most inbetweens the great majority of those
    /// strokes barely move".
    /// </summary>
    /// <param name="detail">
    /// How many strokes of scenery sit around the ones that move. Swept rather
    /// than fixed, because the question is how the two encodings scale against
    /// each other and a single density would answer it for one drawing.
    /// </param>
    /// <param name="swing">Where the arms are: -1 back, +1 forward.</param>
    private static List<Stroke> Figure(int detail, double swing)
    {
        var strokes = new List<Stroke>
        {
            Line("torso", (256, 150), (256, 340)),
            Line("head", (256, 148), (256, 96)),
            Line("near-leg", (256, 340), (214, 470)),
            Line("far-leg", (256, 340), (300, 470)),
            Line("near-arm", (252, 186), (256 - 104 * swing, 262)),
            Line("far-arm", (260, 186), (256 + 96 * swing, 258)),
        };

        // Scenery: hatching, folds, hair — the strokes an artist draws and an
        // inbetweener mostly carries across untouched.
        for (var i = 0; i < detail; i++)
        {
            double y = 160 + i * 7 % 300;
            double x = 210 + i * 13 % 90;
            strokes.Add(Line($"detail-{i}", (x, y), (x + 26, y + 18)));
        }

        return strokes;
    }

    /// <summary>
    /// A stroke of 40 points, which is past <see cref="StrokeWire.MaxWirePoints"/>
    /// on purpose: the resample is part of what the JSON arm costs, and a fixture
    /// that stayed under the cap would price a wire shape production never sends.
    /// </summary>
    private static Stroke Line(string label, (double X, double Y) a, (double X, double Y) b)
    {
        const int n = 40;
        var points = new List<StrokePoint>(n);
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)(n - 1);
            // A slight bow, so the points are not collinear and the resample has
            // something to preserve.
            var bow = Math.Sin(t * Math.PI) * 6;
            points.Add(new StrokePoint(
                a.X + (b.X - a.X) * t + bow,
                a.Y + (b.Y - a.Y) * t,
                0.4 + 0.4 * Math.Sin(t * Math.PI)));
        }

        return new Stroke
        {
            Label = label,
            Color = "#101010",
            Brush = new BrushSettings { Size = 6, Hardness = 0.8 },
            Points = points,
        };
    }

    // ---- rendering ----------------------------------------------------------

    /// <summary>
    /// Render a key the way a reference view is rendered: composed onto white,
    /// PNG, bare base64. Line art on transparency is not what the model is shown
    /// anywhere else in this application, and would price a different picture.
    /// </summary>
    private static string RenderKey(IReadOnlyList<Stroke> strokes)
    {
        using var art = FrameRasterizer.Rasterize(strokes, W, H);
        using var sheet = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(art, 0, 0);
            canvas.Flush();
        }

        return PngCodec.Encode(sheet);
    }

    /// <summary>How many pixels of a rendered sheet are not the paper.</summary>
    private static int InkedPixels(string pngBase64)
    {
        using var bitmap = PngCodec.Decode(pngBase64);
        var inked = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red < 200 || p.Green < 200 || p.Blue < 200) inked++;
            }
        }

        return inked;
    }

    // ---- the three arms -----------------------------------------------------

    private sealed record Arm(string Name, string UserText, IReadOnlyList<string> Images);

    private static InbetweenRequest Request(
        IReadOnlyList<Stroke> a, IReadOnlyList<Stroke> b, IReadOnlyList<string>? images = null) =>
        new(new SceneInfo(W, H, 12), a, b, [0.5], Easing.EaseInOut, ReferenceImages: images);

    private static Arm JsonArm(int detail)
    {
        var a = Figure(detail, -1);
        var b = Figure(detail, +1);
        return new Arm("json", Prompts.InbetweenUser(Request(a, b)), []);
    }

    private static Arm SplitArm(int detail)
    {
        var a = Figure(detail, -1);
        var b = Figure(detail, +1);

        // The pictures show the whole key, not the leftovers. A render of "the
        // strokes you are not being sent" would be a drawing with holes in it,
        // and the model is being asked to place a limb against the body it
        // crosses — which means it has to see the body.
        var images = new[] { RenderKey(a), RenderKey(b) };

        var text =
            "Two attached images are keyframe A and keyframe B of a hand-drawn " +
            "animation, complete as the artist drew them. The JSON below carries " +
            "only the strokes that move between them.\n" +
            "Redraw ONLY those strokes at each requested t. Do not return the " +
            "strokes that appear solely in the images — they are unchanged, and " +
            "are there so you can see what the moving strokes have to read " +
            "against.\n" +
            Prompts.InbetweenUser(Request(Movers(a), Movers(b)));

        return new Arm("split", text, images);
    }

    private static Arm PixelsArm(int detail)
    {
        var a = Figure(detail, -1);
        var b = Figure(detail, +1);
        var images = new[] { RenderKey(a), RenderKey(b) };

        var text =
            "Two attached images are keyframe A and keyframe B of a hand-drawn " +
            "animation. There is no stroke data for them.\n" +
            "Produce the inbetween at each requested t as strokes, in the given " +
            "schema, reading the drawing from the pictures.\n" +
            Prompts.InbetweenUser(Request([], []));

        return new Arm("pixels", text, images);
    }

    private static List<Stroke> Movers(IEnumerable<Stroke> strokes) =>
        strokes.Where(s => Moving.Contains(s.Label)).ToList();

    // ---- cost, which needs no provider --------------------------------------

    /// <summary>Characters ÷ 4 — the payload document's floor, not an estimate.</summary>
    private static int TextTokens(string s) => s.Length / 4;

    /// <summary>
    /// An image is priced by area: width × height ÷ 750. Checked against the
    /// four figures recorded in <c>docs/DESIGN-ai-payload.md</c> by
    /// <see cref="TheImageTokenFormulaReproducesTheRecordedFigures"/>, so this
    /// measurement does not quietly invent its own arithmetic.
    /// </summary>
    private static int ImageTokens(int width, int height) => width * height / 750;

    [Fact]
    public void TheImageTokenFormulaReproducesTheRecordedFigures()
    {
        Assert.Equal(2764, ImageTokens(1920, 1080));
        Assert.Equal(691, ImageTokens(960, 540));
        Assert.Equal(442, ImageTokens(768, 432));
        Assert.Equal(196, ImageTokens(512, 288));
    }

    /// <summary>
    /// The half the live run rests on: the three arms really do carry the same
    /// drawing, and differ only in how it is encoded.
    /// </summary>
    /// <remarks>
    /// Worth asserting rather than assuming, for the reason
    /// <c>TaxonomyMeasurementTests</c> gives about its own arms — a comparison
    /// whose cheap arm turned out to be cheap because it was <em>empty</em>
    /// produces two plausible answers and a confident wrong conclusion. The
    /// failure mode is not a red test, it is a decision. So the render is checked
    /// against a blank sheet of the same size and both numbers are printed.
    /// </remarks>
    [Fact]
    public void EveryArmCarriesTheSameDrawingAndDiffersOnlyInHowItIsSent()
    {
        const int detail = 40;
        var a = Figure(detail, -1);
        var b = Figure(detail, +1);

        // 1. The picture is a drawing, not a blank sheet.
        var rendered = RenderKey(a);
        var blank = RenderKey([]);
        var inked = InkedPixels(rendered);
        var paper = InkedPixels(blank);
        output.WriteLine($"render: {inked} inked px, against {paper} on an empty sheet of the same size");
        Assert.Equal(0, paper);
        Assert.True(
            inked > 1000,
            $"the rendered key is nearly blank ({inked} px) — the split arm would be cheap for the wrong reason");

        // 2. Both keys are drawn, and they are not the same drawing.
        Assert.NotEqual(rendered, RenderKey(b));

        // 3. The JSON arm names every stroke; the split arm names only the movers.
        var json = JsonArm(detail);
        var split = SplitArm(detail);
        Assert.Contains("\"detail-0\"", json.UserText);
        Assert.DoesNotContain("\"detail-0\"", split.UserText);
        foreach (var label in Moving)
        {
            Assert.Contains($"\"{label}\"", json.UserText);
            Assert.Contains($"\"{label}\"", split.UserText);
        }

        // 4. The pixels arm carries no stroke geometry at all.
        var pixels = PixelsArm(detail);
        Assert.DoesNotContain("\"near-arm\"", pixels.UserText);
        Assert.Equal(2, pixels.Images.Count);

        // 5. And the two arms that use pictures send the same ones.
        Assert.Equal(split.Images, pixels.Images);
    }

    /// <summary>
    /// What each arm costs as the drawing gets denser. The table the decision is
    /// made from.
    /// </summary>
    /// <remarks>
    /// The shape to look for is not which row is smallest but which column
    /// <em>grows</em>: stroke cost is linear in what the artist drew and image
    /// cost is flat, so the two encodings cross somewhere, and the useful output
    /// of this test is where.
    /// <para>
    /// <b>What the ratio at the bottom is not.</b> The fixture holds the number
    /// of moving strokes at two while the scenery grows, so the split arm's text
    /// is flat by construction and the ratio climbs without limit. That measures
    /// <i>what context costs</i>, which is the question asked — it does not
    /// measure how many strokes a real inbetween needs judgement on, and reading
    /// it as "37× cheaper" would be quoting an upper bound as a result. The
    /// honest claim is the shape: context sent as strokes is linear in the
    /// drawing, context sent as a picture is flat, and only the first one has a
    /// bill that grows with how much the artist has drawn.
    /// </para>
    /// <para>
    /// Image <i>bytes</i> do creep up with density (more ink, larger PNG) while
    /// image <i>tokens</i> do not move at all, because an image is priced by
    /// area. That is the payload document's two halves visible in one table.
    /// </para>
    /// </remarks>
    [Fact]
    public void WhatEachArmCostsAcrossDrawingDensity()
    {
        output.WriteLine($"scene {W}x{H}, two keys, one inbetween at t=0.5");
        output.WriteLine(
            $"an image is {ImageTokens(W, H)} tokens by area; text tokens are chars/4, which is a floor");
        output.WriteLine("");
        output.WriteLine("  strokes  arm       text B    img KB    tokens");

        foreach (var detail in new[] { 4, 12, 40, 120 })
        {
            var total = detail + 6;
            foreach (var arm in new[] { JsonArm(detail), SplitArm(detail), PixelsArm(detail) })
            {
                var imageBytes = arm.Images.Sum(i => (long)i.Length);
                var tokens = TextTokens(arm.UserText) + arm.Images.Count * ImageTokens(W, H);
                output.WriteLine(
                    $"{total,9}  {arm.Name,-8}{arm.UserText.Length,8}{imageBytes / 1024.0,10:0.0}{tokens,10}");
            }

            output.WriteLine("");
        }

        // The mechanical claim, and the only one this test asserts: past a
        // realistic drawing density, splitting costs fewer tokens than sending
        // every stroke. If this ever fails, item 3 of the payload document has
        // stopped being true and the design note needs revisiting before anybody
        // builds on it.
        const int dense = 120;
        var jsonTokens = TextTokens(JsonArm(dense).UserText);
        var splitArm = SplitArm(dense);
        var splitTokens = TextTokens(splitArm.UserText) + splitArm.Images.Count * ImageTokens(W, H);
        output.WriteLine(
            $"at {dense + 6} strokes: json {jsonTokens} tokens, split {splitTokens} — "
            + $"{jsonTokens / (double)splitTokens:0.0}x");
        Assert.True(
            splitTokens < jsonTokens,
            $"split ({splitTokens}) did not beat json ({jsonTokens}) at {dense + 6} strokes");
    }

    // ---- quality, which needs a provider ------------------------------------

    [Fact]
    public async Task DoesPixelContextBeatStrokeJson()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            // Not silently green, for the reason TaxonomyMeasurementTests gives:
            // the judgement half cannot be faked locally, and a test that
            // pretended otherwise would be worse than an absent one.
            output.WriteLine("SKIPPED — no provider. The live measurement did not run.");
            output.WriteLine("Set LIGHTBOX_MEASURE_KEY (and optionally LIGHTBOX_MEASURE_MODEL)");
            output.WriteLine("and re-run: dotnet test tests/Lightbox.Ai.Tests "
                           + "--filter DoesPixelContextBeatStrokeJson");
            output.WriteLine("");
            output.WriteLine("The same key answers DoesTheTaxonomyAloneImproveAnInbetween in the");
            output.WriteLine("same session — run both and judge them together.");
            return;
        }

        const int detail = 40;
        var a = Figure(detail, -1);
        var b = Figure(detail, +1);
        var images = new[] { RenderKey(a), RenderKey(b) };

        var artist = new AnthropicArtist(Key!, Model);

        output.WriteLine($"model: {Model}, {detail + 6} strokes, {Moving.Length} of them moving");

        // json: every stroke, no pictures. Production today.
        var json = await artist.GenerateInbetweensAsync(Request(a, b), CancellationToken.None);

        // split: the movers as JSON, the keys as pictures.
        var split = await artist.GenerateInbetweensAsync(
            Request(Movers(a), Movers(b), images), CancellationToken.None);

        // pixels: pictures only.
        var pixels = await artist.GenerateInbetweensAsync(
            Request([], [], images), CancellationToken.None);

        Report("json — every stroke as JSON", json, expected: detail + 6);
        Report("split — movers as JSON, keys as pictures", split, expected: Moving.Length);
        Report("pixels — pictures only, no stroke data", pixels, expected: Moving.Length);

        // The mechanical half is assertable. Whether an inbetween reads is
        // art-director's call, and is not a threshold somebody can tune.
        Assert.Equal(AiOutcome.Success, json.Outcome);
        Assert.Equal(AiOutcome.Success, split.Outcome);

        output.WriteLine("");
        output.WriteLine("Judge: did `split` place the arms as well as `json`, at a fraction of the");
        output.WriteLine("tokens? If yes, payload item 3 is unblocked and the prompt goes to G12's");
        output.WriteLine("pair before it becomes a code path. If `pixels` also held up, the pixel");
        output.WriteLine("path is viable for context and the answer is bigger than item 3.");
    }

    private void Report(string label, AiResult<List<InbetweenFrameResult>> result, int expected)
    {
        output.WriteLine("");
        output.WriteLine($"--- {label} ---");
        if (result.Outcome != AiOutcome.Success)
        {
            output.WriteLine($"{result.Outcome}: {result.Message}");
            return;
        }

        foreach (var frame in result.Value!)
        {
            output.WriteLine($"t={frame.T:0.##}, {frame.Strokes.Count} strokes (asked for {expected})");
            foreach (var stroke in frame.Strokes.Where(s => s.Points.Count > 0))
            {
                var xs = stroke.Points.Select(p => p.X).ToList();
                var ys = stroke.Points.Select(p => p.Y).ToList();
                output.WriteLine(
                    $"  {stroke.Label ?? "(unlabelled)",-14} {stroke.Points.Count,2} pts  "
                    + $"x {xs.Min():0}-{xs.Max():0}  y {ys.Min():0}-{ys.Max():0}");
            }

            var labels = frame.Strokes.Select(s => s.Label).ToList();
            foreach (var wanted in Moving)
            {
                if (!labels.Contains(wanted)) output.WriteLine($"  MISSING: {wanted}");
            }

            if (labels.Any(string.IsNullOrEmpty)) output.WriteLine("  LOST A LABEL");
            if (frame.Strokes.Count > expected)
            {
                output.WriteLine($"  REDREW MORE THAN ASKED: {frame.Strokes.Count} for {expected}");
            }
        }
    }
}
