using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The durable presentation frame (B122): patch only what changed, and still be
/// pixel-identical to the composite it is standing in for.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these tests can and cannot prove.</b> The saving being chased is a
/// texture upload, and this suite runs with no GPU context — so
/// <see cref="PresentedFrame"/> falls back to a CPU surface here and no upload
/// happens either way. What is verifiable, and what these tests assert, is the
/// property the saving follows from: <b>the number of pixels copied per frame,
/// and that copying only those pixels still produces the right image</b>. Bytes
/// uploaded are proportional to pixels patched by construction — the patch image's
/// dimensions are the patched region — so pinning the pixel count pins the upload
/// without pretending to have measured it.
/// </para>
/// <para>
/// The correctness half matters more than the saving. A durable frame that misses
/// a change shows stale pixels, so the dropped-frame and resize cases are tested
/// as carefully as the happy path.
/// </para>
/// </remarks>
public class PresentedFrameTests(ITestOutputHelper output)
{
    private const int W = 400;
    private const int H = 300;

    /// <summary>A composite with a filled rect, so a patch has something to carry.</summary>
    private static SKImage Composite(SKColor background, params (SKRectI Rect, SKColor Colour)[] marks)
    {
        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(background);
        foreach (var (rect, colour) in marks)
        {
            using var paint = new SKPaint { Color = colour };
            canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), paint);
        }
        canvas.Flush();
        return surface.Snapshot();
    }

    private static long Differing(SKImage a, SKImage b)
    {
        using var ba = SKBitmap.FromImage(a);
        using var bb = SKBitmap.FromImage(b);
        long n = 0;
        for (var y = 0; y < Math.Min(ba!.Height, bb!.Height); y++)
        for (var x = 0; x < Math.Min(ba.Width, bb.Width); x++)
        {
            if (ba.GetPixel(x, y) != bb.GetPixel(x, y)) n++;
        }
        return n;
    }

    /// <summary>
    /// The saving itself: a small change costs a small patch, not a whole frame.
    /// </summary>
    [Fact]
    public void ASmallChangeCopiesASmallPatch()
    {
        using var frame = new PresentedFrame();

        // First present is legitimately full — there is nothing to patch onto.
        using var first = Composite(SKColors.White);
        frame.Present(null, first, null);
        Assert.True(frame.LastWasFull);
        var fullPixels = frame.LastPatchedPixels;

        // Then a dab-sized change.
        var dab = new SKRectI(180, 140, 224, 168);   // 44x28, the measured dab from B121
        using var second = Composite(SKColors.White, (dab, SKColors.Black));
        frame.Present(null, second, dab);

        var ratio = fullPixels / (double)frame.LastPatchedPixels;
        output.WriteLine($"full present {fullPixels} px, dab present {frame.LastPatchedPixels} px — {ratio:F0}x less");

        Assert.False(frame.LastWasFull);
        Assert.Equal(44L * 28, frame.LastPatchedPixels);
        Assert.True(ratio > 50,
            $"a dab-sized change copied {frame.LastPatchedPixels} of {fullPixels} pixels — "
            + "the patch is not proportional to the change");
    }

    /// <summary>
    /// And the half that matters more: patching must land the same image a full
    /// repaint would have. A cheap frame that is wrong is not a saving.
    /// </summary>
    [Fact]
    public void PatchingLandsTheSameImageAFullRepaintWould()
    {
        using var frame = new PresentedFrame();
        using var start = Composite(SKColors.White);
        frame.Present(null, start, null);

        // A run of changes, each presented with only its own region, the way a
        // stroke arrives one pointer event at a time.
        var marks = new List<(SKRectI, SKColor)>();
        for (var i = 0; i < 6; i++)
        {
            var r = new SKRectI(40 + i * 50, 60 + i * 30, 40 + i * 50 + 30, 60 + i * 30 + 20);
            marks.Add((r, SKColors.Black));
            using var step = Composite(SKColors.White, marks.ToArray());
            frame.Present(null, step, r);
        }

        using var expected = Composite(SKColors.White, marks.ToArray());
        var presented = frame.Present(null, expected, new SKRectI(0, 0, 1, 1)); // a no-op patch
        var differing = Differing(presented, expected);

        output.WriteLine($"after 6 incremental patches, {differing} pixels differ from a full composite");
        Assert.Equal(0, differing);
    }

    /// <summary>
    /// A dropped frame must not lose its change. The canvas drops frames under
    /// back-pressure, and a durable frame that is not told would keep the pixels
    /// of a frame nobody ever presented.
    /// </summary>
    [Fact]
    public void ADroppedFramesChangeIsStillOwedAndArrivesWithTheNextPatch()
    {
        using var frame = new PresentedFrame();
        using var start = Composite(SKColors.White);
        frame.Present(null, start, null);

        var dropped = new SKRectI(50, 50, 90, 80);
        var kept = new SKRectI(300, 200, 340, 240);

        // The frame carrying `dropped` never reaches Present — it is dropped.
        frame.Skipped(dropped);

        using var latest = Composite(SKColors.White, (dropped, SKColors.Black), (kept, SKColors.Black));
        var presented = frame.Present(null, latest, kept);

        var differing = Differing(presented, latest);
        output.WriteLine($"patched {frame.LastPatchedPixels} px; {differing} pixels differ from the composite");

        // Both regions must be present, so the patch had to cover the union.
        Assert.Equal(0, differing);
        Assert.True(frame.LastPatchedPixels >= (long)kept.Width * kept.Height,
            "the owed region from the dropped frame was not included in the patch");
    }

    /// <summary>
    /// Without <see cref="PresentedFrame.Skipped"/> the dropped change would be
    /// lost — this is that claim, checked, so the call above is known to be
    /// load-bearing rather than assumed to be.
    /// </summary>
    [Fact]
    public void NotBeingToldAboutADroppedFrameIsWhatWouldLoseIt()
    {
        using var frame = new PresentedFrame();
        using var start = Composite(SKColors.White);
        frame.Present(null, start, null);

        var dropped = new SKRectI(50, 50, 90, 80);
        var kept = new SKRectI(300, 200, 340, 240);

        // Deliberately DO NOT call Skipped.
        using var latest = Composite(SKColors.White, (dropped, SKColors.Black), (kept, SKColors.Black));
        var presented = frame.Present(null, latest, kept);

        var differing = Differing(presented, latest);
        output.WriteLine($"without Skipped(): {differing} pixels differ — the dropped region is missing");
        Assert.True(differing > 0,
            "a dropped change went unnoticed even without Skipped(), so that call proves nothing");
    }

    /// <summary>
    /// A repaint that carries no new publish must cost nothing. The canvas
    /// repaints on every pointer move to move the brush ring, which is far more
    /// often than the compositor publishes.
    /// </summary>
    [Fact]
    public void ARepaintWithNoNewPublishCostsNothing()
    {
        using var frame = new PresentedFrame();
        using var start = Composite(SKColors.White);
        frame.Present(null, start, null, seq: 1);

        var dab = new SKRectI(180, 140, 224, 168);
        using var next = Composite(SKColors.White, (dab, SKColors.Black));
        frame.Present(null, next, dab, seq: 2);
        var afterPublish = frame.LastPatchedPixels;

        // Three more repaints at the same publish — a hover, three times.
        SKImage? handed = null;
        for (var i = 0; i < 3; i++) handed = frame.Present(null, next, dab, seq: 2);

        output.WriteLine($"publish patched {afterPublish} px; each hover repaint patched {frame.LastPatchedPixels} px");
        Assert.Equal(0, frame.LastPatchedPixels);

        // And it must still be showing the right thing, not just doing nothing.
        Assert.NotNull(handed);
        Assert.Equal(0, Differing(handed!, next));
    }

    /// <summary>
    /// The seq shortcut may not swallow a change that is owed — a dropped frame
    /// followed by a repaint at the same publish still has pixels to deliver.
    /// </summary>
    [Fact]
    public void TheSeqShortcutStillHonoursAnOwedRegion()
    {
        using var frame = new PresentedFrame();
        using var start = Composite(SKColors.White);
        frame.Present(null, start, null, seq: 1);

        var dropped = new SKRectI(50, 50, 90, 80);
        var kept = new SKRectI(300, 200, 340, 240);
        using var latest = Composite(SKColors.White, (dropped, SKColors.Black), (kept, SKColors.Black));

        frame.Present(null, latest, kept, seq: 2);
        // A frame is dropped, then a repaint arrives still labelled seq 2.
        frame.Skipped(dropped);
        var presented = frame.Present(null, latest, kept, seq: 2);

        output.WriteLine($"patched {frame.LastPatchedPixels} px after an owed region at the same seq");
        Assert.True(frame.LastPatchedPixels > 0, "the owed region was swallowed by the seq shortcut");
        Assert.Equal(0, Differing(presented, latest));
    }

    /// <summary>
    /// B130: an image handed to the compositor must not be freed underneath it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The crash this guards is native — an access violation inside
    /// <c>sk_canvas_draw_image_rect</c> — so none of the crash reporter's three
    /// managed channels sees it and no report is written. It presented as
    /// "Lightbox dies after the splash screen as soon as I touch anything, and
    /// there is no crash report", which is the least diagnosable shape a bug has.
    /// </para>
    /// <para>
    /// <c>PresentCore</c> used to <c>Dispose()</c> the previous snapshot on every
    /// present. <c>CanvasControl</c> documents the identical failure for
    /// <c>RenderSnapshot</c> and keeps a retirement queue for it; this class did
    /// not. So the assertion is the same one <c>HeldImagesAlive</c> makes there:
    /// the handle of an image we handed out is still valid after later presents.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnImageHandedOutSurvivesLaterPresents()
    {
        using var frame = new PresentedFrame();
        using var start = Composite(SKColors.White);
        var first = frame.Present(null, start, null, seq: 1);

        // Whoever received `first` may still be drawing it. Present again — which
        // is what a pointer move does — and it must not be freed.
        var dab = new SKRectI(180, 140, 224, 168);
        using var next = Composite(SKColors.White, (dab, SKColors.Black));
        var second = frame.Present(null, next, dab, seq: 2);

        output.WriteLine($"first handle {(first.Handle == IntPtr.Zero ? "FREED" : "alive")}, "
                         + $"second {(second.Handle == IntPtr.Zero ? "FREED" : "alive")}, "
                         + $"retired {frame.RetiredCount}");

        Assert.NotEqual(IntPtr.Zero, first.Handle);
        Assert.NotEqual(IntPtr.Zero, second.Handle);
        Assert.True(frame.HeldImagesAlive);

        // And still readable, not merely non-null — a freed SKImage can keep a
        // stale handle, so the check that matters is that the pixels answer.
        using var bmp = SKBitmap.FromImage(first);
        Assert.NotNull(bmp);
        Assert.Equal(W, bmp!.Width);
    }

    /// <summary>
    /// Retirement has to be bounded, or the crash is traded for the copy-on-write
    /// cost this class exists to avoid: a snapshot of a surface still being drawn
    /// into forces Skia to duplicate it.
    /// </summary>
    [Fact]
    public void RetirementIsBoundedRatherThanUnlimited()
    {
        using var frame = new PresentedFrame();
        using var img = Composite(SKColors.White);

        for (var i = 0; i < 40; i++)
        {
            frame.Present(null, img, new SKRectI(0, 0, 20, 20), seq: i + 1);
        }

        output.WriteLine($"after 40 presents, {frame.RetiredCount} images retired");
        Assert.True(frame.RetiredCount <= 3,
            $"{frame.RetiredCount} superseded images are being held — retirement is unbounded");
        Assert.True(frame.HeldImagesAlive);
    }

    /// <summary>A resize cannot be patched onto — it must repaint in full.</summary>
    [Fact]
    public void AResizeRepaintsInFull()
    {
        using var frame = new PresentedFrame();
        using var small = Composite(SKColors.White);
        frame.Present(null, small, null);

        using var bigSurface = SKSurface.Create(new SKImageInfo(W * 2, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        bigSurface.Canvas.Clear(SKColors.Red);
        bigSurface.Canvas.Flush();
        using var big = bigSurface.Snapshot();

        // A tiny region offered, but the size changed, so it must be ignored.
        var presented = frame.Present(null, big, new SKRectI(0, 0, 4, 4));
        output.WriteLine($"after resize: full={frame.LastWasFull}, patched={frame.LastPatchedPixels} px, "
                         + $"presented {presented.Width}x{presented.Height}");

        Assert.True(frame.LastWasFull, "a resize was patched instead of repainted, which cannot be correct");
        Assert.Equal(W * 2, presented.Width);
        Assert.Equal(0, Differing(presented, big));
    }

    /// <summary>
    /// <see cref="PresentedFrame.ForceFull"/> is the escape hatch for anything
    /// that invalidates the frame without changing its size, and it must actually
    /// force a full repaint rather than be advisory.
    /// </summary>
    [Fact]
    public void ForceFullRepaintsEverythingEvenWhenASmallRegionIsOffered()
    {
        using var frame = new PresentedFrame();
        using var start = Composite(SKColors.White);
        frame.Present(null, start, null);

        using var different = Composite(SKColors.Blue);
        frame.ForceFull();
        var presented = frame.Present(null, different, new SKRectI(0, 0, 4, 4));

        output.WriteLine($"full={frame.LastWasFull}, patched={frame.LastPatchedPixels} px");
        Assert.True(frame.LastWasFull);
        Assert.Equal(0, Differing(presented, different));
    }
}
