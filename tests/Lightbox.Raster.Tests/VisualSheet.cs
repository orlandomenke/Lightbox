using SkiaSharp;

namespace Lightbox.Testing;

/// <summary>
/// Writes labelled PNG contact sheets so a person can check with their eyes what the assertions
/// checked with numbers.
/// </summary>
/// <remarks>
/// <para>
/// <b>This does not replace a pixel test and is not allowed to.</b> `CLAUDE.md` prefers a headless
/// pixel assertion over a screenshot for a good reason — synthetic input through Xvfb drops events,
/// and a dropped click looks exactly like a bug. Everything here sits <em>beside</em> a test that
/// already fails on its own; the sheet exists because several defects in this repository were
/// obvious to a person and invisible to a passing assertion, and because the reverse also happened:
/// a probe reported 100.0% coverage on a blank image, and one look would have said so.
/// </para>
/// <para>
/// <b>Off unless asked for.</b> Set <c>LIGHTBOX_VISUALS</c> to a directory and every visual test in
/// the solution writes there; leave it unset and an ordinary <c>dotnet test</c> writes nothing and
/// costs nothing. One switch for the whole solution rather than one per suite: the earlier
/// <c>LIGHTBOX_BRUSH_IMAGES</c> covered a single file, and <c>LiveMatchesCommittedTests</c> was
/// moved onto this rather than left with a second name, because two switches mean nobody can ask
/// for "all the sheets" without first knowing which files have one.
/// </para>
/// <para>
/// The difference panels are amplified, and that is deliberate: the errors worth catching here are
/// a handful of 255ths spread over a rim, which read as black at 1× and as a shape at 6×. The
/// amplification factor is printed on the panel so nobody mistakes the picture for the measurement.
/// </para>
/// </remarks>
public static class VisualSheet
{
    /// <summary>Where sheets go, or null when nobody asked for them.</summary>
    public static string? Directory =>
        Environment.GetEnvironmentVariable("LIGHTBOX_VISUALS") is { } dir && !string.IsNullOrWhiteSpace(dir)
            ? dir
            : null;

    /// <summary>Whether to bother rendering at all — check before doing extra work for a sheet.</summary>
    public static bool Wanted => Directory is not null;

    private const int Gap = 10;
    private const int LabelHeight = 22;
    private const int TitleHeight = 30;
    private static readonly SKColor Ink = new(0xE8, 0xE8, 0xEC);
    private static readonly SKColor Backdrop = new(0x1E, 0x1E, 0x22);

    /// <summary>"These pixels agree" — light, so a faint difference is visibly *not* this.</summary>
    private static readonly SKColor Neutral = new(0x9A, 0x9A, 0x9E);

    /// <summary>One labelled image in a sheet.</summary>
    /// <param name="Label">Printed under the panel. Say what it is, not that it is a panel.</param>
    /// <param name="Image">Not disposed here — the caller owns it.</param>
    public readonly record struct Panel(string Label, SKBitmap Image);

    /// <summary>
    /// Panels in a row under a title, written as <c>{name}.png</c>. No-op when unasked.
    /// </summary>
    public static void Write(string name, string title, params Panel[] panels)
    {
        if (Directory is not { } dir || panels.Length == 0) return;
        System.IO.Directory.CreateDirectory(dir);

        var height = panels.Max(p => p.Image.Height);
        // Wide enough for the panels *and* for the title, which is not decoration: it carries the
        // bug number and the measurement, and a sheet whose caption is cut off at "the missin" is
        // a sheet somebody has to come back and ask about. The first version did exactly that.
        // Every caption has to fit, not just the title: a panel's label starts at that panel's left
        // edge, so the last one is the one that runs off. The first version measured only the title
        // and clipped "2x render, downsam".
        var needed = panels.Sum(p => p.Image.Width) + Gap * (panels.Length + 1);
        needed = Math.Max(needed, (int)Math.Ceiling(TextWidth(title, 14, bold: true)) + Gap * 2);
        var at = Gap;
        foreach (var panel in panels)
        {
            needed = Math.Max(needed, at + (int)Math.Ceiling(TextWidth(panel.Label, 12)) + Gap);
            at += panel.Image.Width + Gap;
        }
        var width = needed;
        using var sheet = new SKBitmap(width, TitleHeight + height + LabelHeight + Gap);
        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(Backdrop);
            DrawText(canvas, title, Gap, TitleHeight - 10, 14, bold: true);

            var x = Gap;
            foreach (var panel in panels)
            {
                // A checker behind each panel, because half of what these sheets show is where a
                // mark is *absent*, and transparent-on-dark reads as ink.
                Checker(canvas, x, TitleHeight, panel.Image.Width, panel.Image.Height);
                canvas.DrawBitmap(panel.Image, x, TitleHeight);
                DrawText(canvas, panel.Label, x, TitleHeight + height + 14, 12);
                x += panel.Image.Width + Gap;
            }
        }
        Save(sheet, System.IO.Path.Combine(dir, name + ".png"));
    }

    /// <summary>
    /// <paramref name="a"/> | <paramref name="b"/> | their amplified difference.
    /// </summary>
    /// <remarks>
    /// The third panel is signed: <b>red where the first is heavier, blue where the second is</b>.
    /// A magnitude-only difference cannot tell "the preview painted more" from "the preview painted
    /// less", and those are different bugs with different fixes — B54 had both at once.
    /// </remarks>
    public static void WriteComparison(
        string name, string title, string labelA, SKBitmap a, string labelB, SKBitmap b, int amplify = 6)
    {
        if (!Wanted) return;
        using var diff = Difference(a, b, amplify);
        Write(
            name, title,
            new Panel(labelA, a),
            new Panel(labelB, b),
            new Panel($"difference, ×{amplify} — red: {labelA} heavier, blue: {labelB} heavier", diff));
    }

    /// <summary>A signed, amplified difference image. Flat mid-grey means identical.</summary>
    /// <remarks>
    /// The neutral is deliberately <em>light</em>. The first version used near-black, which is what
    /// a small amplified difference also looks like — so "identical" and "out by two" were the same
    /// picture, in a helper built to tell them apart.
    /// </remarks>
    public static SKBitmap Difference(SKBitmap a, SKBitmap b, int amplify = 6)
    {
        var w = Math.Min(a.Width, b.Width);
        var h = Math.Min(a.Height, b.Height);
        var diff = new SKBitmap(a.Width, a.Height);
        using var canvas = new SKCanvas(diff);
        canvas.Clear(Neutral);
        canvas.Dispose();

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                // Composited over white, so a transparent pixel and a white one are the same thing
                // — which is what they are to the artist looking at paper.
                var inkA = 255 - (pa.Red * pa.Alpha + 255 * (255 - pa.Alpha)) / 255;
                var inkB = 255 - (pb.Red * pb.Alpha + 255 * (255 - pb.Alpha)) / 255;
                var d = Math.Abs(inkA - inkB);
                if (d == 0)
                {
                    diff.SetPixel(x, y, Neutral);
                    continue;
                }
                // Away from the neutral in one direction or the other, so both signs read against
                // it: more red and less blue-green for A heavier, and the reverse for B.
                var v = (byte)Math.Min(255, d * amplify);
                var fade = (byte)(Neutral.Red - Math.Min(Neutral.Red, v / 2));
                diff.SetPixel(
                    x, y,
                    inkA > inkB
                        ? new SKColor((byte)Math.Min(255, Neutral.Red + v), fade, fade)
                        : new SKColor(fade, fade, (byte)Math.Min(255, Neutral.Blue + v)));
            }
        }
        return diff;
    }

    /// <summary>An image composited over white paper, which is how an artist sees a layer.</summary>
    public static SKBitmap OverPaper(SKBitmap layer)
    {
        var flat = new SKBitmap(layer.Width, layer.Height);
        using var canvas = new SKCanvas(flat);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(layer, 0, 0);
        canvas.Flush();
        return flat;
    }

    /// <summary>A crop blown up with no smoothing, for looking at a rim a pixel or two wide.</summary>
    public static SKBitmap Zoom(SKBitmap source, SKRectI area, int factor)
    {
        var clipped = SKRectI.Intersect(area, new SKRectI(0, 0, source.Width, source.Height));
        var zoomed = new SKBitmap(clipped.Width * factor, clipped.Height * factor);
        using var canvas = new SKCanvas(zoomed);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(
            image,
            new SKRect(clipped.Left, clipped.Top, clipped.Right, clipped.Bottom),
            new SKRect(0, 0, zoomed.Width, zoomed.Height),
            // Nearest neighbour: a smoothed zoom invents the very gradients being inspected.
            new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.Flush();
        return zoomed;
    }

    private static void Checker(SKCanvas canvas, int left, int top, int w, int h)
    {
        const int cell = 8;
        using var light = new SKPaint { Color = new SKColor(0x3A, 0x3A, 0x3E) };
        using var dark = new SKPaint { Color = new SKColor(0x32, 0x32, 0x36) };
        for (var y = 0; y < h; y += cell)
        {
            for (var x = 0; x < w; x += cell)
            {
                var paint = ((x / cell + y / cell) % 2 == 0) ? light : dark;
                canvas.DrawRect(
                    new SKRect(left + x, top + y, left + Math.Min(x + cell, w), top + Math.Min(y + cell, h)),
                    paint);
            }
        }
    }

    /// <summary>
    /// Labels, when the platform has a typeface to draw them with.
    /// </summary>
    /// <remarks>
    /// A headless container may have no fonts at all, and a sheet with no captions is still worth
    /// looking at — so this degrades rather than throwing. The filename carries the same
    /// information for exactly that case, which is why <c>Write</c> takes a name as well as a title.
    /// </remarks>
    private static void DrawText(SKCanvas canvas, string text, float x, float y, float size, bool bold = false)
    {
        using var typeface = SKTypeface.FromFamilyName(
            null, bold ? SKFontStyleWeight.SemiBold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        if (typeface is null) return;
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint { Color = Ink, IsAntialias = true };
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
    }

    /// <summary>How wide a caption will be, or 0 when there is no typeface to ask.</summary>
    /// <remarks>
    /// <paramref name="bold"/> has to match what <see cref="DrawText"/> will use. Measuring the
    /// regular face and drawing the semi-bold one under-measures by a few per cent, which is exactly
    /// enough to clip the last word of a title — and did.
    /// </remarks>
    private static float TextWidth(string text, float size, bool bold = false)
    {
        using var typeface = SKTypeface.FromFamilyName(
            null, bold ? SKFontStyleWeight.SemiBold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        if (typeface is null) return 0;
        using var font = new SKFont(typeface, size);
        return font.MeasureText(text);
    }

    private static void Save(SKBitmap sheet, string path)
    {
        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
    }
}
