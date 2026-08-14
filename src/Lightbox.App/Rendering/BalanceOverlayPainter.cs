using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>One frame's centre of mass, flattened for the render thread.</summary>
/// <param name="Current">The frame under the playhead — the dot being judged.</param>
/// <param name="Flagged">Its frame's volume drifted past the tolerance.</param>
public readonly record struct BalanceDot(float X, float Y, bool Current, bool Flagged);

/// <summary>
/// The volume checker's canvas half: a centre-of-mass dot per frame and the
/// arc they trace, onion-skin style.
/// </summary>
/// <remarks>
/// <para>
/// A walk reads wrong when this path jitters, and balance reads wrong when
/// the current dot is not over the support foot — both are judgements about
/// the <i>arc</i>, which no single frame can show. So every frame's dot is
/// drawn faint, the path connects them in time order, and the current frame's
/// dot is the emphatic one.
/// </para>
/// <para>
/// A separate testable class for <see cref="GuidePainter"/>'s reason: the
/// draw op only runs inside a compositor lease a headless run never grants,
/// so anything painted inline there is unreachable by test.
/// </para>
/// </remarks>
public static class BalanceOverlayPainter
{
    /// <summary>The arc and its dots: cool, like the rig's other furniture.</summary>
    private static readonly SKColor PathColor = new(0x4d, 0xc4, 0xff);

    /// <summary>A flagged frame's dot: warm, because it is a warning.</summary>
    private static readonly SKColor FlagColor = new(0xff, 0x6a, 0x3d);

    /// <summary>
    /// Paint the arc. Expects the canvas already in document space; empty or
    /// null draws nothing at all — the checker being off is absent, not
    /// merely invisible.
    /// </summary>
    public static void Paint(SKCanvas canvas, IReadOnlyList<BalanceDot>? dots, float scale)
    {
        if (dots is not { Count: > 0 }) return;
        var px = 1f / Math.Max(0.01f, scale);

        if (dots.Count > 1)
        {
            using var arc = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f * px,
                Color = PathColor.WithAlpha(110),
            };
            using var path = new SKPath();
            path.MoveTo(dots[0].X, dots[0].Y);
            for (var i = 1; i < dots.Count; i++) path.LineTo(dots[i].X, dots[i].Y);
            canvas.DrawPath(path, arc);
        }

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var ring = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * px,
        };
        foreach (var dot in dots)
        {
            var colour = dot.Flagged ? FlagColor : PathColor;
            if (dot.Current)
            {
                // The dot being judged: solid, ringed, unmistakably the one.
                fill.Color = colour;
                ring.Color = colour;
                canvas.DrawCircle(dot.X, dot.Y, 3.5f * px, fill);
                canvas.DrawCircle(dot.X, dot.Y, 6f * px, ring);
            }
            else
            {
                // Neighbours are context, so they read like onion skin: there,
                // and not competing.
                fill.Color = colour.WithAlpha(130);
                canvas.DrawCircle(dot.X, dot.Y, 2.5f * px, fill);
            }
        }
    }
}
