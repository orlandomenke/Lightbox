using Lightbox.App.Rendering;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Byte-identity of the composite: the harness B125 stage 2 is measured
/// against, and three properties it can already prove today.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stage 2 moves compositing off the UI thread</b> — the view model will
/// publish a pass list and the canvas will composite from it inside the draw
/// op. The only acceptable outcome is that the pixels do not change, and
/// "looks the same" is not a claim this project accepts after B156 through
/// B164 were diagnosed from field captures because nothing could assert on a
/// composite.
/// </para>
/// <para>
/// <b>So the harness comes before the plumbing.</b> <see cref="Bytes"/> is the
/// whole of it: compose a pass list and read the pixels out. When the draw op
/// grows a compose-from-passes path, the assertion is that its bytes equal
/// these.
/// </para>
/// <para>
/// <b>It is not speculative scaffolding — it has three customers now</b>, and
/// one of them is a hazard the code itself flags as untested. `PublishSnapshot`
/// invalidates the compose ring after a culled publish and says so in as many
/// words: <i>"honest note: this is unproven defence, not a tested fix … it is
/// kept because stale pixels are wrong quietly, which is the failure this
/// codebase is least able to notice."</i> A reused buffer that keeps a previous
/// frame's pixels is exactly that failure, and
/// <see cref="AFullComposeIntoADirtySurfaceMatchesAFreshOne"/> is the test that
/// was missing.
/// </para>
/// </remarks>
public class ComposeIdentityTests(ITestOutputHelper output)
{
    private static SKImageInfo Info(int w = 64, int h = 48) =>
        new(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);

    /// <summary>A layer bitmap with a recognisable, position-dependent pattern.</summary>
    private static SKBitmap Layer(int w, int h, byte seed)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(seed, (byte)(255 - seed), 128, 200) };
        // Off-centre and asymmetric, so a flip or an offset shows up as a
        // difference rather than cancelling out.
        canvas.DrawRect(seed % 16, seed % 8, w * 0.6f, h * 0.7f, paint);
        return bmp;
    }

    private static List<RenderPass> Passes(int w, int h) =>
    [
        new(Layer(w, h, 40), null, 1.0, SKBlendMode.SrcOver),
        new(Layer(w, h, 190), null, 0.65, SKBlendMode.Multiply),
    ];

    /// <summary>
    /// The harness. Compose a pass list into a surface and read the pixels —
    /// what stage 2's draw-op path will be compared against.
    /// </summary>
    private static byte[] Bytes(
        SKSurface surface, IReadOnlyList<RenderPass> passes, SKImageInfo info,
        SKRectI? clip = null, SKColor? background = null)
    {
        SceneRenderer.ComposeInto(surface, passes, background ?? SKColors.White, clip);
        var buffer = new byte[info.BytesSize];
        using var image = surface.Snapshot();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            Assert.True(
                image.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0),
                "could not read the composed pixels back");
        }
        finally
        {
            handle.Free();
        }
        return buffer;
    }

    private static byte[] Fresh(IReadOnlyList<RenderPass> passes, SKImageInfo info, SKRectI? clip = null)
    {
        using var surface = SKSurface.Create(info)!;
        return Bytes(surface, passes, info, clip);
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) return i;
        return a.Length == b.Length ? -1 : Math.Min(a.Length, b.Length);
    }

    /// <summary>
    /// The composite is deterministic. Invariant 2's sibling: without this, no
    /// comparison stage 2 makes means anything, because the reference would
    /// move under it.
    /// </summary>
    [Fact]
    public void TheSamePassesComposeToTheSameBytesTwice()
    {
        var info = Info();
        var passes = Passes(info.Width, info.Height);

        var first = Fresh(passes, info);
        var second = Fresh(passes, info);

        Assert.Equal(-1, FirstDifference(first, second));
    }

    /// <summary>
    /// <b>The hazard `PublishSnapshot` describes and admits it never tested.</b>
    /// Compositing everything into a surface that already holds a different
    /// frame must land exactly where compositing into a clean one does — if any
    /// of the previous contents survive, that is stale art shown silently.
    /// </summary>
    [Fact]
    public void AFullComposeIntoADirtySurfaceMatchesAFreshOne()
    {
        var info = Info();
        var passes = Passes(info.Width, info.Height);
        var reference = Fresh(passes, info);

        using var reused = SKSurface.Create(info)!;
        // Fill it with something unmistakably different first — a previous
        // frame, in effect.
        reused.Canvas.Clear(new SKColor(0xFF, 0x00, 0xFF, 0xFF));
        reused.Canvas.Flush();

        var afterReuse = Bytes(reused, passes, info);

        var diff = FirstDifference(reference, afterReuse);
        output.WriteLine(diff < 0
            ? "identical"
            : $"first difference at byte {diff}: fresh={reference[diff]} reused={afterReuse[diff]}");

        Assert.Equal(-1, diff);
    }

    /// <summary>
    /// A clipped composite must leave the clipped region identical to what an
    /// unclipped one produces. This is what the culled publish path relies on,
    /// and what stage 2 will rely on again when the draw op composites only the
    /// region it is asked for.
    /// </summary>
    [Fact]
    public void InsideTheClipAClippedComposeMatchesAnUnclippedOne()
    {
        var info = Info();
        var passes = Passes(info.Width, info.Height);
        var full = Fresh(passes, info);

        var clip = SKRectI.Create(8, 6, 24, 20);
        using var surface = SKSurface.Create(info)!;
        // Same starting ground as the unclipped compose, so only the clip
        // differs between the two runs.
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Flush();
        var clipped = Bytes(surface, passes, info, clip);

        // Compare only inside the clip: outside it, a clipped compose is
        // *supposed* to keep whatever was there.
        var bpp = info.BytesPerPixel;
        for (var y = clip.Top; y < clip.Bottom; y++)
        {
            for (var x = clip.Left; x < clip.Right; x++)
            {
                var i = (y * info.RowBytes) + (x * bpp);
                for (var b = 0; b < bpp; b++)
                {
                    Assert.True(full[i + b] == clipped[i + b],
                        $"clipped compose differs inside the clip at ({x},{y}) byte {b}: "
                        + $"{full[i + b]} vs {clipped[i + b]} — the culled publish path "
                        + "depends on this and so will the draw op");
                }
            }
        }
    }
}
