using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// How far the live blur is from the blur that commits, measured rather than asserted.
/// </summary>
/// <remarks>
/// <para>
/// <b>B54's stated cause is wrong, and these numbers are what say so.</b> The entry blamed
/// <c>StampBlurDraft</c>'s cropped snapshot — a dab near the crop's edge sampling the boundary
/// instead of the real neighbourhood. Hand the whole stroke to <em>one</em> draft call and the
/// crop reproduces the commit <b>to the pixel</b>. The crop is innocent.
/// </para>
/// <para>
/// Two separate defects are left, and they want different fixes:
/// </para>
/// <para>
/// <b>1. Dab positions differ, which is where the coverage error comes from.</b> The application
/// sends the newest segment only, so <c>GeometryOps.Densify</c> sees a two-point list, returns it
/// unchanged, and the dab walk restarts its spacing from that segment's first point. The commit
/// densifies the whole path and walks it once, so the dabs land elsewhere: <b>1148 pixels the
/// preview blurs that the commit does not</b>, and 610 the commit blurs that the preview does not,
/// at a 147 px brush. This is BR1 — "the dab walk restarts every pointer event" — still true of
/// the effect path after it was fixed for paint. Backward context does not fix it: tried at 2, 3,
/// 4 and 5 points of overlap, the error wobbles (1579 / 1105 / 2518 / 1728 px) rather than
/// converging, because the walk's *phase* is what is wrong.
/// </para>
/// <para>
/// <b>2. Antialiased rims accumulate.</b> Each event redraws its dabs through an antialiased clip
/// with <c>SKBlendMode.Src</c>, which is not idempotent at partial coverage α:
/// <c>α·blurred + (1-α)·whatever is there</c>. Over eight events the rim converges somewhere one
/// pass never goes — 294 px at worst 6, and 292 px at worst 223 on the diagonal.
/// </para>
/// <para>
/// Restoring the region from the pre-stroke pixels before re-stamping fixes 2 completely (0 px
/// against the commit) <em>when the whole stroke is fed</em>, and is a worse bug than 2 when the
/// segment is fed: the padded rectangle reverts ground earlier segments blurred correctly and
/// nothing re-stamps it, leaving <b>20,710 pixels of gap trailing the brush</b>. Restoring only
/// the union of the tail's dab circles avoids that and fixes neither cleanly. So the fix is the
/// BR1 treatment for effects — walk the whole stroke, stamp what is new — which is a change to
/// the hot path and is <b>waiting on a decision, not on effort</b>.
/// </para>
/// <para>
/// The tolerances below therefore pin <em>today's</em> behaviour, generously, so this file is an
/// instrument that cannot rot rather than a promise nobody keeps. The lesson is the one this repo
/// keeps paying for: <em>the number was real and the attribution was not</em> — padding the crop
/// would have changed nothing measurable.
/// </para>
/// </remarks>
public class LiveBlurFidelityTests(ITestOutputHelper output)
{
    private const int W = 400, H = 200;

    /// <summary>
    /// A checkered bar. Structure matters: blurring a uniform colour is a no-op by construction,
    /// which is how B49's first two probes passed against broken code.
    /// </summary>
    private static SKBitmap Ground()
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var bar = new SKPaint { Color = new SKColor(0x20, 0xc0, 0x40, 255), IsAntialias = false };
        canvas.DrawRect(new SKRect(60, 70, 340, 130), bar);
        using var dark = new SKPaint { Color = new SKColor(0x10, 0x10, 0x10, 200) };
        for (var x = 60; x < 340; x += 20) canvas.DrawRect(new SKRect(x, 70, x + 10, 130), dark);
        canvas.Flush();
        return bmp;
    }

    private static Stroke BlurStroke(double size, bool diagonal) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        // Diagonal crosses the bar's edge and runs out into bare canvas; horizontal stays inside
        // the structure. The two fail differently, so both are here.
        Points = diagonal
            ? [.. Enumerable.Range(0, 9).Select(i => new StrokePoint(100 + i * 20, 40 + i * 10, 1))]
            : [.. Enumerable.Range(0, 9).Select(i => new StrokePoint(120 + i * 20, 100, 1))],
        Brush = new BrushSettings
        {
            Kind = BrushKind.Blur, Size = size, Hardness = 1, Flow = 0.6, Spacing = 0.1,
        },
    };

    private readonly record struct Diff(int Pixels, int Worst, int DraftOnly, int CommitOnly);

    private static Diff Compare(SKBitmap committed, SKBitmap draft, SKBitmap untouched)
    {
        int pixels = 0, worst = 0, draftOnly = 0, commitOnly = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var c = committed.GetPixel(x, y);
                var d = draft.GetPixel(x, y);
                var o = untouched.GetPixel(x, y);
                if (c != d)
                {
                    pixels++;
                    worst = Math.Max(worst, Math.Abs(c.Red - d.Red));
                    worst = Math.Max(worst, Math.Abs(c.Alpha - d.Alpha));
                }
                if (d != o && c == o) draftOnly++;
                if (c != o && d == o) commitOnly++;
            }
        }
        return new Diff(pixels, worst, draftOnly, commitOnly);
    }

    /// <summary>
    /// How the App feeds the draft path, and the distinction matters.
    /// </summary>
    /// <remarks>
    /// <c>MainViewModel</c> sends <b>the newest segment only</b> — <c>points.Skip(stamped - 1)</c>,
    /// overlapping one point so the pieces join. Testing whole-stroke tails alone would have hidden
    /// the risk the fix carries: a restore that reverts more than the tail re-stamps leaves gaps
    /// behind the brush. Both feeds are here for exactly that reason.
    /// </remarks>
    public enum Feed
    {
        /// <summary>Newest segment only — what the application actually does.</summary>
        Segment,

        /// <summary>Everything so far, which is how a commit sees the stroke.</summary>
        WholeStroke,
    }

    [Theory]
    [InlineData(40, false, Feed.Segment)]
    [InlineData(40, true, Feed.Segment)]
    [InlineData(147, false, Feed.Segment)]   // the size from the original report
    [InlineData(147, true, Feed.Segment)]
    [InlineData(40, false, Feed.WholeStroke)]
    [InlineData(147, true, Feed.WholeStroke)]
    public void ADraggedBlurMatchesTheCommittedOne(double size, bool diagonal, Feed feed)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var stroke = BlurStroke(size, diagonal);

        using var committed = Ground();
        using (var canvas = new SKCanvas(committed))
        {
            BrushEngine.StampStroke(canvas, stroke, info, committed);
            canvas.Flush();
        }

        // One AppendDraft per pointer event, all reading the same pre-stroke base.
        using var draft = Ground();
        using var preStroke = Ground();
        var densify = new Lightbox.Core.Geometry.IncrementalDensify();
        List<BrushEngine.Dab>? previous = null;
        var settled = 0;
        for (var n = 2; n <= stroke.Points.Count; n++)
        {
            var soFar = new Stroke
            {
                Tool = stroke.Tool,
                Color = stroke.Color,
                Brush = stroke.Brush,
                Points = [.. stroke.Points.Take(n)],
            };
            if (feed == Feed.Segment)
            {
                // What MainViewModel does: the whole stroke walked once, only the unsettled dabs
                // stamped. The walk uses the shared densify cache, as the live path does.
                var walk = BrushEngine.WalkDabs(soFar, densify);
                FrameRasterizer.AppendDraft(draft, soFar, preStroke, walk, settled);
                settled = BrushEngine.StableCount(walk, previous);
                previous = walk;
            }
            else
            {
                // Every dab re-stamped every event — the shape the path had before B54, kept as
                // the control. It must still not be worse than what it was.
                FrameRasterizer.AppendDraft(draft, soFar, preStroke);
            }
        }

        using var untouched = Ground();
        var diff = Compare(committed, draft, untouched);
        output.WriteLine(
            $"size {size}, {(diagonal ? "diagonal" : "horizontal")}, {feed}: {diff.Pixels} px differ, "
            + $"worst {diff.Worst}; draft-only {diff.DraftOnly}, commit-only {diff.CommitOnly}");

        // Not vacuous: the blur has to have done something for "identical" to mean anything.
        var changed = Compare(committed, untouched, untouched).Pixels;
        output.WriteLine($"the committed blur changed {changed} px against the untouched ground");
        Assert.True(changed > 500, $"the committed blur only changed {changed} px — nothing is being measured");

        if (feed == Feed.Segment)
        {
            // **The fix, and exactly what it is worth.** Coverage is now exact — the drag blurs the
            // pixels the commit blurs and no others, where before it was 1148 px out one way and 610
            // the other. Values are not exact, and cannot be affordably: replaying every dab each
            // event gets 0 px and costs 194 ms an event by the 300th point against 20.8 ms for this
            // range. The residue is the mark's rim, bounded here so it cannot grow unnoticed.
            Assert.Equal(0, diff.DraftOnly);
            Assert.Equal(0, diff.CommitOnly);
            Assert.True(
                diff.Pixels <= 1200 && diff.Worst <= 130,
                $"the live blur differs from the committed one over {diff.Pixels} px at worst "
                + $"{diff.Worst} of 255 — beyond the rim residue B54 recorded");
        }
        else
        {
            // Replay-every-dab-every-event: the shape the path had before B54, kept as a control.
            // It improved without being aimed at — 294 px to 29, and 292 to 171 on the diagonal —
            // because the restore stops the rims creeping whatever range is drawn. It is not exact,
            // and making it so needs the restore to cover the padded rectangle instead of the dabs,
            // which is safe only when every dab is replayed and is measured at 194 ms an event.
            Assert.True(diff.Pixels <= 200, $"the replay-everything control drifted to {diff.Pixels} px");
        }
    }

    /// <summary>
    /// One draft call is the reference the multi-event case has to meet, and pinning it separately
    /// says which half broke when this file goes red.
    /// </summary>
    [Fact]
    public void OneDraftCallOfAWholeStrokeIsAlreadyExact()
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var stroke = BlurStroke(147, diagonal: true);

        using var committed = Ground();
        using (var canvas = new SKCanvas(committed))
        {
            BrushEngine.StampStroke(canvas, stroke, info, committed);
            canvas.Flush();
        }

        using var draft = Ground();
        using var preStroke = Ground();
        FrameRasterizer.AppendDraft(draft, stroke, preStroke);

        using var untouched = Ground();
        var diff = Compare(committed, draft, untouched);
        output.WriteLine($"single draft call: {diff.Pixels} px differ, worst {diff.Worst}");
        Assert.Equal(0, diff.Pixels);
    }
}
