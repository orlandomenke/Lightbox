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

    /// <summary>
    /// One-entry caches, keyed by what actually changes: the dash effects by
    /// the zoom (their intervals are screen-sized, so document-space lengths
    /// move with it) and the arc path by the fit it traces (a new fit only
    /// arrives with a trail refresh). Allocating these per paint was a leak —
    /// paint runs every frame the overlay is visible (leak-hunter,
    /// 2026-08-20). Safe as statics because the draw op runs on one thread.
    /// </summary>
    private static float _dashPx;

    private static SKPathEffect? _ghostDash;

    private static SKPathEffect? _arcDash;

    private static JumpArcFit? _pathFor;

    private static SKPath? _arcPath;

    private static void RefreshDashes(float px)
    {
        if (px == _dashPx && _ghostDash is not null) return;
        _ghostDash?.Dispose();
        _arcDash?.Dispose();
        _ghostDash = SKPathEffect.CreateDash([3f * px, 2f * px], 0);
        _arcDash = SKPathEffect.CreateDash([5f * px, 3f * px], 0);
        _dashPx = px;
    }

    private static SKPath ArcPathFor(JumpArcFit jump)
    {
        if (ReferenceEquals(_pathFor, jump) && _arcPath is not null) return _arcPath;
        _arcPath?.Dispose();
        var path = new SKPath();
        path.MoveTo((float)jump.Curve[0].X, (float)jump.Curve[0].Y);
        for (var i = 1; i < jump.Curve.Count; i++)
            path.LineTo((float)jump.Curve[i].X, (float)jump.Curve[i].Y);
        _arcPath = path;
        _pathFor = jump;
        return path;
    }

    public static void Paint(SKCanvas canvas, TrailOverlay? overlay, float scale)
    {
        if (overlay is null) return;
        var px = 1f / Math.Max(0.01f, scale);
        RefreshDashes(px);

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
        ghost.PathEffect = _ghostDash;
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
        arc.PathEffect = _arcDash;

        canvas.DrawPath(ArcPathFor(jump), arc);

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
