using System.Security.Cryptography;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The exact pixels each brush shape lays down, as a hash.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written for B177's optimisation, and kept because the guard was missing.</b>
/// The wet-edge pass got four repeated composites and three repeated
/// <c>DstIn</c> draws replaced by two table filters — arithmetically the same
/// curve, and "arithmetically the same" is exactly the claim that needs a test
/// rather than an argument. The existing wet-edge tests assert that the rim is
/// darker than the centre, which stays true under a change that shifts every
/// pixel by one.
/// </para>
/// <para>
/// <b>A hash rather than a stored image.</b> What matters is that the render did
/// not move, not what it looks like — the visual tests own that question. A hash
/// makes an accidental change loud and costs nothing to store.
/// </para>
/// <para>
/// <b>When one of these fails, it is not automatically wrong.</b> A deliberate
/// change to a brush changes its pixels by design; the failure means *say so*.
/// Re-record the value in the same commit that changes the engine, and the diff
/// then carries both halves — which is the whole point, because a silent pixel
/// change on the stroke record is the thing invariants 2 and 4 exist to stop.
/// </para>
/// </remarks>
public class BrushPresetRenderFingerprintTests(ITestOutputHelper output)
{
    private const int W = 320;
    private const int H = 200;

    /// <summary>
    /// The shapes that matter for the texture passes: one with granulation, one
    /// with a wet edge, one with both, one with neither. Settings written here
    /// rather than read from the app's presets — this is Raster, and a test that
    /// followed the shipped presets would change its answer when an artist's
    /// default did.
    /// </summary>
    public static TheoryData<string, double, double> Shapes => new()
    {
        { "plain", 0, 0 },
        { "granulation", 0.35, 0 },
        { "wet edge", 0, 0.7 },
        { "both", 0.35, 0.7 },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AShapeRendersTheSamePixelsEveryTime(string name, double granulation, double wetEdge)
    {
        var first = Render(granulation, wetEdge);
        var second = Render(granulation, wetEdge);

        output.WriteLine($"{name,-12} {first}");

        // Determinism first: invariant 2 says a re-render reproduces, and a
        // fingerprint that is not stable within one process cannot guard
        // anything across a change.
        Assert.Equal(first, second);
    }

    /// <summary>
    /// <b>The one that caught a change nobody would have seen.</b>
    /// </summary>
    /// <remarks>
    /// B177 tried replacing the wet edge's four repeated composites and three
    /// repeated <c>DstIn</c> draws with two table filters — arithmetically the
    /// same curves, <c>1-(1-a)^4</c> and <c>a^4</c>, and the tables were built by
    /// replaying the eight-bit arithmetic rather than the formula so the rounding
    /// would match. <b>It still moved the pixels</b>, on exactly the two shapes
    /// that run the pass and neither of the two that do not:
    /// <c>9F85DAE8… → A54E82E2…</c> and <c>5FED81B4… → C4724B2A…</c>.
    /// <see cref="SKColorFilter.CreateTable"/> works in unpremultiplied colour,
    /// so the round trip rounds differently from repeated premultiplied
    /// compositing however carefully the table is built.
    /// </remarks>
    /// <remarks>
    /// <b>492 raster tests passed through that change.</b> The existing wet-edge
    /// tests assert the rim is darker than the centre, which stays true when
    /// every pixel shifts — so the optimisation would have shipped, and every
    /// document already drawn with a wet edge would have re-rendered slightly
    /// differently on load. That is what invariants 2 and 4 are for, and this is
    /// the guard that was missing.
    /// </remarks>
    [Theory]
    // Re-recorded 2026-08-24: every one of these shapes is hardness 0.35, so all
    // four are now held down to the brush's own footprint (Q157,
    // BrushEngine.NeedsFootprintCap). The soft edge each lays down went from
    // about half the width the brush describes to exactly it, which changes
    // every pixel in the falloff. Said here rather than discovered later, which
    // is what this file asks for.
    // Re-recorded again 2026-08-24, and all four moved together, which is the
    // right answer rather than a worrying one: the shape here is spacing 0.1
    // against a 0.09 target, so the walk now lays two dabs per interval where it
    // laid one (B307, BrushEngine.SubdividesForFidelity). That reaches the
    // coverage every shape is built on, so a change confined to the texture
    // passes — moving "granulation" and "both" but not "plain" — would have been
    // the surprising outcome here, not this one.
    // Re-recorded 2026-09-02, all four together again, when the ceiling stopped
    // being the best any single dab does at a pixel and became the brush's
    // falloff applied to how far inside the whole mark's reach the pixel sits
    // (B349, docs/DESIGN-swept-ceiling.md). Every shape here is soft enough to
    // be capped, and a capped stroke's falloff is now flat along its length
    // instead of rippling at the dab pitch, so the change reaches the same
    // coverage the 2026-08-24 entry did — and, by the note's argument, never
    // lowers a pixel: SweptCeilingTests counts the pixels that went down and
    // finds none.
    // Re-recorded 2026-10-10, all four together: a document-scale render now
    // stamps its dabs in document coordinates through a pooled plane, so that
    // replaying a stroke is the same arithmetic as drawing it live
    // (docs/DESIGN-paint-path.md). Skia's gradient arithmetic is not
    // translation-invariant, so every shape moved by a level on a scattering
    // of edge pixels and nowhere else — the dabs under all four are the same
    // dabs, which is why they move together once more.
    // Re-recorded 2026-10-10, the two with a wet edge: the wet edge is no
    // longer a blur of the whole mark subtracted from itself after the dabs,
    // it is the rim of the ceiling, read off the distance to the edge of the
    // mark's support and laid under the pen (docs/DESIGN-paint-path.md, phase
    // 2). A new formulation of the same look, so these two move and the two
    // without a wet edge do not - which is the evidence that grain, now also
    // laid by the band finish, is the same grain to the byte.
    [InlineData("plain", 0, 0, "9BCFD3D92A8A3F92")]
    [InlineData("granulation", 0.35, 0, "7BC9E543177AD6E5")]
    [InlineData("wet edge", 0, 0.7, "B198EB0DF8F929A9")]
    [InlineData("both", 0.35, 0.7, "CC0C7F32911B08E3")]
    public void AShapeStillRendersWhatItDidBefore(
        string name, double granulation, double wetEdge, string expected)
    {
        var actual = Render(granulation, wetEdge);
        output.WriteLine($"{name,-12} expected {expected}  actual {actual}");
         
        Assert.Equal(expected, actual);
    }

    private static string Render(double granulation, double wetEdge)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var layer = new SKBitmap(info);
        using var canvas = new SKCanvas(layer);

        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#204060",
            Points = Enumerable.Range(0, 12)
                .Select(i => new StrokePoint(40 + i * 20, 100 + Math.Sin(i * 0.5) * 30, 0.5 + i % 3 * 0.2))
                .ToList(),
            Brush = new BrushSettings
            {
                Size = 18,
                Hardness = 0.6,
                Opacity = 1,
                Flow = 0.8,
                Spacing = 0.1,
                Granulation = granulation,
                WetEdge = wetEdge,
            },
        };

        BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();

        var bytes = layer.GetPixelSpan().ToArray();
        return Convert.ToHexString(SHA256.HashData(bytes))[..16];
    }
}
