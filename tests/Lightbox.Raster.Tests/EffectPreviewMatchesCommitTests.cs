using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What an artist sees while smudging is what commits when they let go.
/// </summary>
/// <remarks>
/// <para>
/// <b>B69 and B89</b>, which are one defect filed twice — B69 as the family statement
/// across all three effect brushes, B89 as the settling visible at pen lift. Both close
/// here because both are the same mechanism.
/// </para>
/// <para>
/// The live path used to hand the engine the newest <em>segment</em>. Four things
/// restarted on every pointer event, and all four changed at once when the pen lifted
/// and the whole stroke was re-rendered from the record in a single pass:
/// </para>
/// <list type="number">
/// <item><description>
/// The dab walk's spacing phase — this is BR1, fixed for paint in B45 and for blur in
/// B54, and left true for smudge. <c>LiveBlurFidelityTests</c> measured it on the blur at
/// 1148 px of over-coverage and recorded that lookbehind cannot repair a phase.
/// </description></item>
/// <item><description>
/// The carried colour, which is unique to smudge and the largest visible term: a segment
/// began with a fresh local sample while the commit dragged colour the length of the
/// stroke.
/// </description></item>
/// <item><description>
/// The heading, so a direction-following tip snapped at each event boundary.
/// </description></item>
/// <item><description>
/// The one-point overlap where segments join, re-smeared because a smudge is not
/// idempotent.
/// </description></item>
/// </list>
/// <para>
/// Feeding is modelled on <c>LiveBlurFidelityTests</c> deliberately, including the
/// structured ground: smudging flat colour is very nearly a no-op by construction, which
/// is how B49's first probes passed against broken code.
/// </para>
/// </remarks>
public class EffectPreviewMatchesCommitTests(ITestOutputHelper output)
{
    private const int W = 400, H = 200;

    /// <summary>A checkered bar — structure, so there is something to move.</summary>
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

    private static Stroke SmudgeStroke(SmudgeMode mode, double size, bool diagonal) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = diagonal
            ? [.. Enumerable.Range(0, 9).Select(i => new StrokePoint(100 + i * 20, 40 + i * 10, 1))]
            : [.. Enumerable.Range(0, 9).Select(i => new StrokePoint(120 + i * 20, 100, 1))],
        Brush = new BrushSettings
        {
            Kind = BrushKind.Smudge, Size = size, Hardness = 0.5, Flow = 0.12, Spacing = 0.1,
            SmudgeMode = mode, SmudgeLength = 0.6, SmudgeRadius = 0.5,
            ColorRate = mode == SmudgeMode.Dulling ? 0 : 0.05,
        },
    };

    private readonly record struct Diff(int Pixels, int Worst, int DraftOnly, int CommitOnly);

    private static Diff Compare(SKBitmap committed, SKBitmap draft, SKBitmap untouched)
    {
        int pixels = 0, worst = 0, draftOnly = 0, commitOnly = 0;
        for (var y = 0; y < H; y++)
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
        return new Diff(pixels, worst, draftOnly, commitOnly);
    }

    /// <summary>The commit: one pass over the whole stroke, exactly as the record renders.</summary>
    private static SKBitmap Committed(Stroke stroke)
    {
        var bmp = Ground();
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        BrushEngine.StampStroke(canvas, stroke, info, bmp);
        canvas.Flush();
        return bmp;
    }

    /// <summary>
    /// The drag, event by event, exactly as <c>MainViewModel.StampLiveSmudge</c> drives it:
    /// whole-stroke walk, settled prefix stamped permanently, provisional tail lent and
    /// taken back, carry checkpointed at the boundary.
    /// </summary>
    private static SKBitmap Dragged(Stroke stroke)
    {
        var draft = Ground();
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var densify = new Lightbox.Core.Geometry.IncrementalDensify();

        List<BrushEngine.Dab>? previous = null;
        var settledSoFar = 0;
        var carry = default(BrushEngine.SmudgeCarry);
        SKBitmap? backup = null;
        SKRectI? lent = null;

        for (var n = 2; n <= stroke.Points.Count; n++)
        {
            var soFar = new Stroke
            {
                Tool = stroke.Tool,
                Color = stroke.Color,
                Brush = stroke.Brush,
                Points = [.. stroke.Points.Take(n)],
            };
            var walk = BrushEngine.WalkDabs(soFar, densify);
            var settled = BrushEngine.StableCount(walk, previous);

            using var canvas = new SKCanvas(draft);

            // 1. take the old tail back
            if (lent is { } region && backup is not null)
            {
                using var restore = SKImage.FromBitmap(backup);
                using var src = new SKPaint { BlendMode = SKBlendMode.Src };
                canvas.DrawImage(
                    restore,
                    new SKRect(0, 0, region.Width, region.Height),
                    new SKRect(region.Left, region.Top, region.Right, region.Bottom),
                    src);
                canvas.Flush();
                lent = null;
            }

            // 2. extend the settled prefix, checkpointing the carry
            if (settled > settledSoFar)
            {
                carry = BrushEngine.StampSmudgeRange(
                    canvas, draft, soFar, walk, settledSoFar, settled, carry);
            }

            // 3. lend the new tail
            if (BrushEngine.RangeBounds(walk, settled, soFar.Brush, info) is { } tail)
            {
                canvas.Flush();
                if (backup is null || backup.Width < tail.Width || backup.Height < tail.Height)
                {
                    backup?.Dispose();
                    backup = new SKBitmap(new SKImageInfo(
                        Math.Max(tail.Width, 64), Math.Max(tail.Height, 64),
                        SKColorType.Rgba8888, SKAlphaType.Premul));
                }
                using (var sub = new SKBitmap())
                {
                    if (draft.ExtractSubset(sub, tail))
                    {
                        using var px = sub.PeekPixels();
                        using var view = px is null ? null : SKImage.FromPixels(px);
                        if (view is not null)
                        {
                            using var into = new SKCanvas(backup);
                            using var src = new SKPaint { BlendMode = SKBlendMode.Src };
                            into.DrawImage(view, 0, 0, src);
                            into.Flush();
                            lent = tail;
                        }
                    }
                }
                BrushEngine.StampSmudgeRange(canvas, draft, soFar, walk, settled, walk.Count, carry);
                canvas.Flush();
            }

            settledSoFar = settled;
            previous = walk;
        }

        backup?.Dispose();
        return draft;
    }

    private Diff Run(SmudgeMode mode, double size, bool diagonal, string label)
    {
        var stroke = SmudgeStroke(mode, size, diagonal);
        using var committed = Committed(stroke);
        using var draft = Dragged(stroke);
        using var untouched = Ground();

        var diff = Compare(committed, draft, untouched);
        var changed = Compare(committed, untouched, untouched).Pixels;

        output.WriteLine(
            $"{label}: {diff.Pixels} px differ, worst {diff.Worst}; "
            + $"draft-only {diff.DraftOnly}, commit-only {diff.CommitOnly}");
        output.WriteLine($"    the committed smudge changed {changed} px against the untouched ground");

        // Not vacuous: smudging flat colour is a no-op by construction, and a broken brush
        // that draws nothing would otherwise "match" perfectly.
        Assert.True(changed > 500, $"the committed smudge only changed {changed} px — nothing is being measured");
        return diff;
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(30, true)]
    [InlineData(80, false)]
    [InlineData(80, true)]
    public void ASmudgePreviewMatchesItsCommit(double size, bool diagonal)
    {
        var diff = Run(SmudgeMode.Smearing, size, diagonal, $"smudge {size}{(diagonal ? " diagonal" : "")}");
        Assert.Equal(0, diff.Pixels);
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(80, true)]
    public void ABlenderPreviewMatchesItsCommit(double size, bool diagonal)
    {
        var diff = Run(SmudgeMode.Dulling, size, diagonal, $"blender {size}{(diagonal ? " diagonal" : "")}");
        Assert.Equal(0, diff.Pixels);
    }

    /// <summary>
    /// B69's own words: "click and release changes the effected area."
    /// </summary>
    [Theory]
    [InlineData(SmudgeMode.Smearing)]
    [InlineData(SmudgeMode.Dulling)]
    public void TheAffectedAreaDoesNotChangeOnRelease(SmudgeMode mode)
    {
        var diff = Run(mode, 80, diagonal: true, $"area {mode}");
        Assert.Equal(0, diff.DraftOnly);
        Assert.Equal(0, diff.CommitOnly);
    }

    /// <summary>B89: nothing settles at pen lift, because there is nothing left to settle.</summary>
    [Theory]
    [InlineData(SmudgeMode.Smearing)]
    [InlineData(SmudgeMode.Dulling)]
    public void NoSettlingVisibleBetweenPreviewAndCommit(SmudgeMode mode)
    {
        var diff = Run(mode, 30, diagonal: false, $"settling {mode}");
        Assert.Equal(0, diff.Worst);
    }

    /// <summary>The two the ledger names separately for B89, kept honest as their own cases.</summary>
    [Fact]
    public void ASmudgePreviewStaysSameOnRelease()
    {
        Assert.Equal(0, Run(SmudgeMode.Smearing, 50, diagonal: true, "smudge release").Pixels);
    }

    [Fact]
    public void ABlenderPreviewStaysSameOnRelease()
    {
        Assert.Equal(0, Run(SmudgeMode.Dulling, 50, diagonal: true, "blender release").Pixels);
    }
}
