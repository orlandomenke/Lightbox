using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai;

/// <summary>
/// Puts the artist's brush — and the alpha lock — back on strokes a model
/// drew (B468).
/// </summary>
/// <remarks>
/// <para>
/// <b>The wire carries three numbers of a brush — size, hardness, opacity —
/// and a model answers in the same three</b>, so what comes back is a plain
/// round brush. The tip, the texture, the pressure response, the flow and
/// everything else that makes a pencil line a pencil line were never sent and
/// cannot come back. The alpha lock is lost the same way, and that is the one
/// measured on real line art (2026-10-13): a black line coloured yellow by
/// alpha-locked strokes came back as a yellow blob several times the line's
/// width, because the colouring strokes were redrawn as ordinary paint. With
/// the lock and the brush restored, the same answer is a clean inbetween of
/// the line — the model had drawn it right.
/// </para>
/// <para>
/// Sending the whole brush would not fix it and would cost on every stroke of
/// every request — a model has no use for a texture id. The brush is the
/// artist's and the motion is the model's, so each drawn stroke is paired with
/// the key stroke it came from by the same matcher the free engine and the
/// verifier use, and wears a copy of that stroke's brush. Only the size the
/// model chose is kept: it is the one brush number it was asked for, and
/// blending it between the keys is part of the motion.
/// </para>
/// <para>
/// A stroke with nothing to pair — ink the keys do not explain, a revealed
/// line — keeps the plain brush rather than borrowing a stranger's. The
/// matcher assigns whatever is left however far apart, so a pairing is only
/// trusted when the two strokes sit near each other (a quarter of the scene's
/// diagonal) and the key stroke is a brush stroke: a fill is described to the
/// model as an outline, and its settings are not a line's. A wrong donor is
/// worse than none — its alpha lock would hide the stroke entirely.
/// </para>
/// <para>
/// Before halfway the brush is key A's and after it key B's, so a stroke that
/// changes from pencil to ink changes in the middle rather than popping on the
/// last frame; a stroke only one key has takes that key's. Where the keys
/// disagree about opacity the model's value is kept, since blending it is a
/// fade, which is motion.
/// </para>
/// </remarks>
public static class AiBrushes
{
    public static List<Stroke> Restore(
        IReadOnlyList<Stroke> drawn, IReadOnlyList<Stroke> keyA, IReadOnlyList<Stroke> keyB,
        SceneInfo scene, double t)
    {
        var near = 0.25 * Math.Sqrt((double)scene.Width * scene.Width + (double)scene.Height * scene.Height);
        var fromA = Sources(keyA, drawn, near);
        var fromB = Sources(keyB, drawn, near);
        var restored = new List<Stroke>(drawn.Count);
        foreach (var stroke in drawn)
        {
            var a = fromA.GetValueOrDefault(stroke);
            var b = fromB.GetValueOrDefault(stroke);
            var source = t < 0.5 ? a ?? b : b ?? a;
            if (source is null || stroke.Tool != ToolKind.Brush)
            {
                restored.Add(stroke);
                continue;
            }
            var brush = source.Brush.Clone();
            brush.Size = stroke.Brush.Size;
            if (a is not null && b is not null && Math.Abs(a.Brush.Opacity - b.Brush.Opacity) > 1e-6)
                brush.Opacity = stroke.Brush.Opacity;
            stroke.Brush = brush;
            // Alpha lock is how a stroke behaves, not where it goes: a colouring
            // stroke over ink. Sent as "brush" and returned as one, it paints
            // in the open — the blob measured on real line art.
            stroke.AlphaLocked = source.AlphaLocked;
            restored.Add(stroke);
        }
        return restored;
    }

    /// <summary>For each drawn stroke the matcher paired with a key stroke, that key stroke.</summary>
    private static Dictionary<Stroke, Stroke> Sources(IReadOnlyList<Stroke> key, IReadOnlyList<Stroke> drawn, double near)
    {
        var map = new Dictionary<Stroke, Stroke>(ReferenceEqualityComparer.Instance);
        foreach (var pair in StrokeMatcher.Match(key.Where(s => s.Tool == ToolKind.Brush).ToList(), drawn))
        {
            if (pair is { A: { } k, B: { } d } && Distance(Centroid(k), Centroid(d)) <= near) map[d] = k;
        }
        return map;
    }

    private static (double X, double Y) Centroid(Stroke s) =>
        s.Points.Count == 0 ? (0, 0) : (s.Points.Average(p => p.X), s.Points.Average(p => p.Y));

    private static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
