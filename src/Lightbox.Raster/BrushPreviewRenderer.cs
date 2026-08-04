using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// A picture of the mark a brush makes, for a picker tile.
/// </summary>
/// <remarks>
/// <para>
/// <b>Drawn by the real engine, not approximated.</b> The preview goes through
/// <see cref="BrushEngine.StampStroke"/> like everything else, so a wet brush shows its
/// rim, a scattered one shows scatter, and a textured one shows its grain. A hand-drawn
/// swatch would be a second implementation of "what this brush looks like" and would start
/// lying the first time the engine changed.
/// </para>
/// <para>
/// <b>The one thing it does not reproduce is the brush's size, and that is deliberate.</b>
/// A 300 px brush cannot show its character in a 44 px tile: rendered at true size the tile
/// holds one flat patch of the middle of the mark, which is the least informative part of
/// it. So the size is clamped to fit and everything else — edge, flow, texture, scatter,
/// wetness, the taper from the pressure ramp — is the brush's own. That makes the tile a
/// portrait rather than a measurement, and it is worth being explicit that the two differ:
/// this is the one render in the app that is <em>not</em> the same mark the document would
/// get, which is why it lives here with a reason attached rather than looking like an
/// oversight.
/// </para>
/// <para>
/// Scaling the surface instead — render at document size, shrink the bitmap — would honour
/// invariant 7 and produce a worse picture for more money: a 300 px stroke downsampled to
/// 44 px is a grey smear whichever way it is resampled.
/// </para>
/// </remarks>
public static class BrushPreviewRenderer
{
    /// <summary>The thinnest and thickest mark a tile shows, in pixels.</summary>
    /// <remarks>
    /// A <em>range</em> rather than a ceiling, and the difference is the whole usefulness of
    /// the tile. Clamping to one maximum was the first attempt and it made a 40 px brush and
    /// a 300 px brush draw the same picture — size vanished from the picker entirely, which
    /// is worse than showing it inexactly. Mapping instead keeps the ordering: a heavier
    /// brush always reads heavier than a lighter one.
    /// </remarks>
    private const double ThinnestPreview = 2.5;
    private const double ThickestPreview = 24;

    /// <summary>The brush sizes the range is stretched across.</summary>
    /// <remarks>
    /// Logarithmic, because brush sizes are chosen that way: the step from 4 to 8 matters
    /// as much as the step from 100 to 200, and a linear map would put every ordinary
    /// inking brush in the bottom tenth of the range and spend the rest on sizes nobody
    /// uses for line work.
    /// </remarks>
    private const double SmallestBrush = 1;
    private const double LargestBrush = 300;

    /// <summary>Paper, so a light mark is visible and a dark one reads as ink.</summary>
    private static readonly SKColor Paper = new(0xf2, 0xf0, 0xec);

    /// <summary>
    /// One stroke of <paramref name="settings"/>, on paper, at <paramref name="width"/> ×
    /// <paramref name="height"/>.
    /// </summary>
    /// <param name="colorHex">The mark's colour — the artist's, so the tile matches the ink.</param>
    public static SKBitmap Render(
        BrushSettings settings, string colorHex = "#1a1a1a", int width = 132, int height = 44)
    {
        var info = new SKImageInfo(
            Math.Max(8, width), Math.Max(8, height), SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = Ground(settings, info);
        using var canvas = new SKCanvas(bitmap);

        var fitted = Fitted(settings, info.Height);
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = colorHex,
            Brush = fitted,
            Points = Swash(info.Width, info.Height, fitted.Size),
        };

        try
        {
            BrushEngine.StampStroke(canvas, stroke, info, bitmap);
        }
        catch (Exception)
        {
            // A preset with settings the engine cannot honour shows an empty tile rather
            // than taking the picker down with it. The artist can still pick it and find
            // out; a picker that throws while being browsed is unusable.
        }
        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// The brush, resized to fit a tile and with anything view-dependent settled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tip, medium, texture and every dynamic are left exactly as the preset has them.
    /// Only the size changes.
    /// </para>
    /// <para>
    /// <b>An effect brush is shrunk only as far as it has to be</b>, rather than mapped onto
    /// the range. The reason the mapping exists is that a 300 px brush cannot show its
    /// character in a 44 px tile — but a smudge or blur's character <em>is</em> its
    /// absolute-pixel behaviour. The blur sigma is <c>Flow × Size / 4</c>, so mapping a 24 px
    /// blur down to 14 px took its sigma to about a third of a pixel and the tile came out
    /// byte-for-byte untouched: three of the twelve built-ins looked like a picker that had
    /// failed. Capping instead keeps the size honest wherever the brush already fits, which
    /// is where nearly every effect brush lives.
    /// </para>
    /// </remarks>
    private static BrushSettings Fitted(BrushSettings settings, int height)
    {
        var copy = settings.Clone();
        copy.Size = NeedsSomethingToWorkOn(settings)
            ? Math.Min(Math.Max(settings.Size, ThinnestPreview), Math.Max(8, height - 8))
            : PreviewSizeFor(copy.Size);
        return copy;
    }

    /// <summary>A brush size mapped onto the range a tile can draw.</summary>
    /// <remarks>
    /// Exposed so the tests can state the property that matters — bigger stays bigger,
    /// across the whole range — rather than inferring it from pixel counts alone.
    /// </remarks>
    public static double PreviewSizeFor(double brushSize)
    {
        var clamped = Math.Clamp(brushSize, SmallestBrush, LargestBrush);
        var t = Math.Log(clamped / SmallestBrush) / Math.Log(LargestBrush / SmallestBrush);
        return ThinnestPreview + (ThickestPreview - ThinnestPreview) * t;
    }

    /// <summary>
    /// What a tile looks like before the brush touches it.
    /// </summary>
    /// <remarks>
    /// Public because it is the only honest answer to "did this brush actually mark the
    /// tile", and that question has to be asked: three built-ins once drew nothing at all.
    /// The obvious way to ask it — render the brush with its flow and opacity at zero and
    /// compare — is wrong, and quietly so. <b>A simulated medium deposits pigment through a
    /// flow of zero</b>, so watercolour compared against itself and reported no difference on
    /// a tile that plainly had a mark on it. There is one ground and this is it.
    /// </remarks>
    public static SKBitmap Ground(BrushSettings settings, SKImageInfo info)
    {
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Paper);
        if (NeedsSomethingToWorkOn(settings)) LayBars(canvas, info);
        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Whether this brush has nothing to draw on blank paper.
    /// </summary>
    /// <remarks>
    /// Smudge, blur and the blender do not deposit colour — they move and soften what is
    /// already there. Previewed on clean paper they produce a <em>completely blank tile</em>,
    /// which is how the first contact sheet came out: three of the twelve built-ins were
    /// empty rectangles, indistinguishable from a picker that had failed to render. They
    /// get something to work on instead.
    /// </remarks>
    private static bool NeedsSomethingToWorkOn(BrushSettings settings) =>
        settings.Kind is BrushKind.Smudge or BrushKind.Blur;

    /// <summary>Bars across the tile, for a brush that disturbs rather than deposits.</summary>
    /// <remarks>
    /// <para>
    /// Hard vertical edges on purpose, because that is what makes the effects tell themselves
    /// apart at a glance: a smudge drags the edges sideways into streaks, a blur leaves them
    /// where they are and softens them. A soft gradient would show neither.
    /// </para>
    /// <para>
    /// <b>Two bar widths, because one width could only show one of the two effects</b> —
    /// found by rendering the three at 4× and looking. Wide bars showed the smudge dragging
    /// beautifully and left the blur tile <em>completely untouched</em>: the blur sigma is
    /// <c>Flow × Size / 4</c>, which on a tile-sized brush is about a pixel, and a pixel of
    /// softening is invisible against a 5 px bar. Narrow bars are what a one-pixel sigma
    /// destroys, so the fine pairs read the blur and the wide bar reads the smudge.
    /// </para>
    /// <para>
    /// Understating the blur is the one place the shrink-to-fit trade actually costs
    /// something, and it is worth naming: effects measured in absolute pixels shrink with the
    /// brush, so a tile shows less blur than the brush will lay down on a real canvas. The
    /// test pattern is the honest lever — it changes what the mark is judged against, not
    /// the mark.
    /// </para>
    /// </remarks>
    private static void LayBars(SKCanvas canvas, SKImageInfo info)
    {
        // A repeating group: two 2 px hairlines, then one 6 px bar. High contrast because the
        // crisp bars above and below the swash are the reference the disturbed band is judged
        // against — mid-grey bars were the first attempt and the disturbance did not read.
        using var bar = new SKPaint { Color = new SKColor(0x2a, 0x2e, 0x34) };
        for (var group = 3; group < info.Width; group += 18)
        {
            canvas.DrawRect(new SKRect(group, 0, group + 2, info.Height), bar);
            canvas.DrawRect(new SKRect(group + 4, 0, group + 6, info.Height), bar);
            canvas.DrawRect(new SKRect(group + 10, 0, group + 16, info.Height), bar);
        }
    }

    /// <summary>
    /// The path every preview uses: one comma-shaped swash, pressure ramping up and away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same path for every brush, so the tiles are comparable — a picker where each
    /// swatch is drawn differently tells you about the paths, not the brushes.
    /// </para>
    /// <para>
    /// Pressure runs light → full → light so a brush with any pressure dynamic shows its
    /// taper, which for an inking brush is most of what distinguishes it. The curve is a
    /// gentle S rather than a straight line for the same reason: a straight stroke hides
    /// what a direction-following tip does.
    /// </para>
    /// <para>
    /// <b>The swing narrows as the mark thickens</b>, and it has to: the range was widened
    /// to 24 px because a pencil, an ink pen and a soft round drew almost the same picture
    /// at the old ceiling of 17, and a 24 px mark on a fixed ±12 curve runs off the top of a
    /// 44 px tile. Deriving the swing from the fitted size keeps the widest brush inside
    /// the tile without flattening the thin ones, which would have cost the taper.
    /// </para>
    /// </remarks>
    private static List<StrokePoint> Swash(int width, int height, double fittedSize)
    {
        var points = new List<StrokePoint>();
        const int samples = 48;
        double left = 10, right = width - 10;
        var mid = height / 2.0;
        var swing = Math.Clamp(mid - fittedSize / 2 - 3, 2.5, 11);

        for (var i = 0; i < samples; i++)
        {
            var t = i / (double)(samples - 1);
            var x = left + (right - left) * t;
            var y = mid + Math.Sin(t * Math.PI * 1.35) * swing;
            // Light at both ends: a taper is the tell for pressure size and flow.
            var pressure = 0.18 + 0.82 * Math.Sin(t * Math.PI);
            points.Add(new StrokePoint(x, y, Math.Clamp(pressure, 0.05, 1)));
        }
        return points;
    }
}
