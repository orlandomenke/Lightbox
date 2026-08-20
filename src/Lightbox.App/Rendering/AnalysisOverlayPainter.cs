using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Everything the motion trail's layer of the overlay sandwich shows: the
/// trail's own ticks, and the analysers riding it (Q133) — the spacing
/// assistant's ghost targets and the fitted jump arc. One snapshot, pushed
/// from the view model the way the trail always was, so the canvas gains
/// analyser chrome without gaining a second channel.
/// </summary>
public sealed record TrailOverlay(
    IReadOnlyList<TrailPoint>? Points,
    IReadOnlyList<SpacingTarget>? SpacingTargets,
    JumpArcFit? Jump);

/// <summary>
/// Draws the analysers over the trail: a dashed ghost tick where the intended
/// spacing wants a drawing, a tether from the drawing to it when it misses,
/// and the fitted gravity arc with the drawings off it ringed.
/// </summary>
/// <remarks>
/// <see cref="MotionTrailPainter"/>'s discipline throughout: document space,
/// screen-sized strokes via the view scale, everything through an
/// <see cref="SKCanvas"/> so a headless test paints a bitmap and reads pixels.
/// The ghost and the arc are intents rather than record, so they draw in their
/// own colour — one the onion tints do not use — and dashed, the convention
/// the canvas already has for "here, but not a mark".
/// </remarks>
public static class AnalysisOverlayPainter
{
    /// <summary>The intent colour: warm amber, distinct from the onion red/blue and the selection violet.</summary>
    private static readonly SKColor IntentColor = new(0xe8, 0xa0, 0x3f);

    /// <summary>The offender ring — the same amber, full strength, doubled width.</summary>
    private static readonly SKColor MissColor = new(0xff, 0xb0, 0x40);

    public static void Paint(SKCanvas canvas, TrailOverlay? overlay, float scale)
    {
        if (overlay is null) return;
        var px = 1f / Math.Max(0.01f, scale);

        if (overlay.Jump is { } jump) PaintJump(canvas, jump, px);
        if (overlay.SpacingTargets is { Count: > 0 } targets) PaintTargets(canvas, targets, px);
    }

    private static void PaintTargets(SKCanvas canvas, IReadOnlyList<SpacingTarget> targets, float px)
    {
        var tick = MotionTrailPainter.TickScreenRadius * px;
        using var ghost = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * px,
            Color = IntentColor.WithAlpha(220),
        };
        ghost.PathEffect = SKPathEffect.CreateDash([3f * px, 2f * px], 0);
        using var tether = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f * px,
            Color = IntentColor.WithAlpha(150),
        };

        foreach (var t in targets)
        {
            // A drawing already where it belongs needs no ghost — painting
            // one under every tick would read as the trail being doubled.
            if (!t.Misses) continue;
            canvas.DrawLine((float)t.X, (float)t.Y, (float)t.TargetX, (float)t.TargetY, tether);
            canvas.DrawCircle((float)t.TargetX, (float)t.TargetY, tick, ghost);
        }
    }

    private static void PaintJump(SKCanvas canvas, JumpArcFit jump, float px)
    {
        if (jump.Curve.Count < 2) return;

        using var arc = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * px,
            Color = IntentColor.WithAlpha(jump.Ballistic ? (byte)220 : (byte)110),
        };
        arc.PathEffect = SKPathEffect.CreateDash([5f * px, 3f * px], 0);

        using var path = new SKPath();
        path.MoveTo((float)jump.Curve[0].X, (float)jump.Curve[0].Y);
        for (var i = 1; i < jump.Curve.Count; i++)
            path.LineTo((float)jump.Curve[i].X, (float)jump.Curve[i].Y);
        canvas.DrawPath(path, arc);

        using var ring = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f * px,
            Color = MissColor,
        };
        var radius = MotionTrailPainter.TickScreenRadius * 2f * px;
        foreach (var d in jump.Deviations)
        {
            if (!d.OffArc) continue;
            canvas.DrawCircle((float)d.X, (float)d.Y, radius, ring);
        }
    }
}
