using Lightbox.App.ViewModels;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Draws the Match lines overlay (Q271): both keys' pieces over the canvas,
/// in the onion skin's colours until they are matched, and in a colour of
/// their own, shared with their partners, once they are.
/// </summary>
/// <remarks>
/// <para>
/// The onion colours because the mode is read the way onion skin is: red is
/// the drawing before, blue the drawing after, whatever the artist set them
/// to. A matched pair leaves both: it is one thing now, and one colour says
/// so. Key B's lines are dashed throughout, so the two keys stay apart where
/// a pair's colours meet.
/// </para>
/// <para>
/// The open match — lines of A clicked, waiting for their lines of B — is
/// drawn wider on a pale casing, so it is clear what the next click joins.
/// </para>
/// </remarks>
public static class LineMatchPainter
{
    /// <summary>Line width on screen, in pixels, for a line not yet matched.</summary>
    private const float Width = 2f;

    /// <summary>How much wider a matched line is, and an open one.</summary>
    private const float MatchedGain = 1.5f, OpenGain = 2.5f;

    /// <summary>
    /// The colour of match <paramref name="index"/>: hues a golden angle apart,
    /// so any two neighbouring matches are far apart on the wheel.
    /// </summary>
    public static SKColor MatchColour(int index) => SKColor.FromHsv((float)(index * 137.508 % 360), 80, 90);

    /// <summary>Paint the overlay; expects the canvas in document space. Null draws nothing.</summary>
    public static void Paint(SKCanvas canvas, LineMatchOverlay? overlay, float scale)
    {
        if (overlay is null) return;
        var px = 1f / Math.Max(0.01f, scale);
        var tintA = SceneRenderer.ParseTint(overlay.TintA, SceneRenderer.OnionPrevTint);
        var tintB = SceneRenderer.ParseTint(overlay.TintB, SceneRenderer.OnionNextTint);
        using var dash = SKPathEffect.CreateDash([6 * px, 4 * px], 0);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };

        // Unmatched under matched, and the open match on top of everything.
        foreach (var line in overlay.Lines.OrderBy(l => l.Pending ? 2 : l.Match is null ? 0 : 1))
        {
            if (line.Points.Count == 0) continue;
            using var path = new SKPath();
            path.MoveTo((float)line.Points[0].X, (float)line.Points[0].Y);
            foreach (var p in line.Points.Skip(1)) path.LineTo((float)p.X, (float)p.Y);
            if (line.Points.Count == 1) path.LineTo((float)line.Points[0].X + 0.01f, (float)line.Points[0].Y);

            paint.PathEffect = line.Key == 1 ? dash : null;
            if (line.Pending)
            {
                paint.Color = OverlayStyle.Open.WithAlpha(200);
                paint.StrokeWidth = Width * OpenGain * px + OverlayStyle.CasingWidth * px;
                paint.PathEffect = null;
                canvas.DrawPath(path, paint);
                paint.PathEffect = line.Key == 1 ? dash : null;
            }
            paint.Color = line.Match is { } m ? MatchColour(m) : line.Key == 0 ? tintA : tintB;
            paint.StrokeWidth = Width * px * (line.Pending ? OpenGain : line.Match is null ? 1 : MatchedGain);
            canvas.DrawPath(path, paint);
        }
    }
}
