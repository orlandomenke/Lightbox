using Lightbox.App.ViewModels;
using Lightbox.Core.Cleanup;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Draws a line cleanup's preview (Q292): each line it would change, in its
/// new place, over the drawing as it stands — so the difference is read on the
/// page — and each mark it would remove, struck through where it is now.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chrome, not paint.</b> The proposal is drawn as a thin path rather than
/// stamped with the line's brush: stamping would be a second render of the
/// drawing per preview, and the question the preview answers is <em>where</em>
/// each line goes, which a path answers exactly. The artist's own ink under it
/// is the "before".
/// </para>
/// <para>
/// The accent for a line that moves, on the style's dark casing so it reads on
/// light paper and dark ink alike; the reference amber, dashed, for a mark
/// that goes — the same pair the canvas already uses for "this is the tool's
/// suggestion" and "this is about to change".
/// </para>
/// </remarks>
public static class CleanupPainter
{
    /// <summary>Line widths on screen, in pixels.</summary>
    private const float Width = 1.5f, RemovedWidth = 2f;

    /// <summary>Paths built once per overlay; the overlay is replaced, never changed.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CleanupOverlay, List<(CleanupLine Line, SKPath Path)>> Paths = new();

    internal static List<(CleanupLine Line, SKPath Path)> PathsFor(CleanupOverlay overlay) => Paths.GetValue(overlay, Build);

    private static List<(CleanupLine, SKPath)> Build(CleanupOverlay overlay)
    {
        var built = new List<(CleanupLine, SKPath)>(overlay.Lines.Count);
        foreach (var line in overlay.Lines)
        {
            if (line.Points.Count == 0) continue;
            var path = new SKPath();
            path.MoveTo((float)line.Points[0].X, (float)line.Points[0].Y);
            for (var i = 1; i < line.Points.Count; i++) path.LineTo((float)line.Points[i].X, (float)line.Points[i].Y);
            if (line.Points.Count == 1) path.LineTo((float)line.Points[0].X + 0.01f, (float)line.Points[0].Y);
            built.Add((line, path));
        }
        return built;
    }

    /// <summary>Paint the preview; expects the canvas in document space. Null draws nothing.</summary>
    public static void Paint(SKCanvas canvas, CleanupOverlay? overlay, float scale)
    {
        if (overlay is null) return;
        var px = 1f / Math.Max(0.01f, scale);
        using var dash = SKPathEffect.CreateDash([4 * px, 3 * px], 0);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        foreach (var (line, path) in PathsFor(overlay))
        {
            var removed = line.Kind == CleanupKind.Removed;
            var width = (removed ? RemovedWidth : Width) * px;
            paint.PathEffect = null;
            paint.Color = OverlayStyle.Casing;
            paint.StrokeWidth = width + OverlayStyle.CasingWidth * px;
            canvas.DrawPath(path, paint);
            paint.PathEffect = removed ? dash : null;
            paint.Color = removed ? OverlayStyle.Reference : OverlayStyle.Accent;
            paint.StrokeWidth = width;
            canvas.DrawPath(path, paint);
        }
    }
}
