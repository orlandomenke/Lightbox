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
/// drawn wider on the style's dark casing, so it is clear what the next click
/// joins; a match the artist made solid keeps the casing, so a shoe marked
/// as one object reads as one.
/// </para>
/// </remarks>
public static class LineMatchPainter
{
    /// <summary>
    /// Line width on screen, in pixels, for a line not yet matched: one, the
    /// width of a line that is merely present (DESIGN.md, On the canvas). These
    /// lines are the artwork shown in the onion colours, not chrome, so they
    /// are not cased.
    /// </summary>
    private const float Width = 1f;

    /// <summary>A matched line, and a line of the open match, in screen pixels.</summary>
    private const float MatchedWidth = 2f, OpenWidth = 3f;

    /// <summary>The colour of match <paramref name="index"/> (<see cref="OverlayStyle.Match"/>).</summary>
    public static SKColor MatchColour(int index) => OverlayStyle.Match(index);

    /// <summary>An overlay's lines in drawing order, each with its path built once.</summary>
    internal sealed class Built(List<(LineMatchLine Line, SKPath Path)> lines)
    {
        public List<(LineMatchLine Line, SKPath Path)> Lines { get; } = lines;
    }

    /// <summary>
    /// The paths of each overlay, built the first time it is painted and kept
    /// while the overlay lives. The overlay is replaced, never changed, when a
    /// match does, so a repaint while panning or zooming reuses them: rebuilt
    /// every frame, 300 lines cost 12-20 ms a repaint (perf-warden, 2026-10-14).
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LineMatchOverlay, Built> Paths = new();

    /// <summary>The overlay's lines in drawing order — unmatched under matched, the open match on top — with their paths.</summary>
    internal static Built PathsFor(LineMatchOverlay overlay) => Paths.GetValue(overlay, Build);

    private static Built Build(LineMatchOverlay overlay)
    {
        var ordered = new List<(LineMatchLine, SKPath)>(overlay.Lines.Count);
        foreach (var layer in new[] { 0, 1, 2 })
        {
            foreach (var line in overlay.Lines)
            {
                if ((line.Pending ? 2 : line.Match is null ? 0 : 1) != layer || line.Points.Count == 0) continue;
                var path = new SKPath();
                path.MoveTo((float)line.Points[0].X, (float)line.Points[0].Y);
                for (var i = 1; i < line.Points.Count; i++) path.LineTo((float)line.Points[i].X, (float)line.Points[i].Y);
                if (line.Points.Count == 1) path.LineTo((float)line.Points[0].X + 0.01f, (float)line.Points[0].Y);
                ordered.Add((line, path));
            }
        }
        return new Built(ordered);
    }

    /// <summary>Paint the overlay; expects the canvas in document space. Null draws nothing.</summary>
    public static void Paint(SKCanvas canvas, LineMatchOverlay? overlay, float scale)
    {
        if (overlay is null) return;
        var px = 1f / Math.Max(0.01f, scale);
        var tintA = SceneRenderer.ParseTint(overlay.TintA, SceneRenderer.OnionPrevTint);
        var tintB = SceneRenderer.ParseTint(overlay.TintB, SceneRenderer.OnionNextTint);
        using var dash = SKPathEffect.CreateDash([6 * px, 4 * px], 0);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };

        foreach (var (line, path) in PathsFor(overlay).Lines)
        {
            var width = (line.Pending ? OpenWidth : line.Match is null ? Width : MatchedWidth) * px;
            // The open match, and a match the artist made solid, sit on the
            // style's dark casing, unbroken under a dashed line (DESIGN.md).
            if (line.Pending || line.Solid == true)
            {
                paint.PathEffect = null;
                paint.Color = OverlayStyle.Casing;
                paint.StrokeWidth = width + OverlayStyle.CasingWidth * px;
                canvas.DrawPath(path, paint);
            }
            paint.PathEffect = line.Key == 1 ? dash : null;
            paint.Color = line.Match is { } m ? MatchColour(m) : line.Key == 0 ? tintA : tintB;
            paint.StrokeWidth = width;
            canvas.DrawPath(path, paint);
        }
    }
}
