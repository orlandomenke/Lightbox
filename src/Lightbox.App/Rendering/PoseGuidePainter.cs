using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Draws the pose guide (Q274): each part's volume as a faint tube around its
/// bones, the bones as thin lines, the joints as small rings — the construction
/// an animator roughs in before drawing, for the artist to draw the inbetween
/// over. Never in a render or an export.
/// </summary>
/// <remarks>
/// In the reference colour, because it is exactly what that colour is for: a
/// mark to draw against rather than the thing being drawn. The volume is a
/// faint fill rather than an outline so the artist's own line, drawn over it,
/// is always the darkest thing on the paper. On a key, where joints can be
/// dragged, the rings are larger.
/// </remarks>
public static class PoseGuidePainter
{
    /// <summary>The volume's fill: faint enough to draw over.</summary>
    private const byte VolumeAlpha = 38;

    /// <summary>Bone line and joint ring widths, and ring radius, in screen pixels.</summary>
    private const float BoneWidth = 1.5f, RingWidth = 1.5f, Ring = 4f, EditableRing = 6f;

    /// <summary>Paint the guide; expects the canvas in document space. Null draws nothing.</summary>
    public static void Paint(SKCanvas canvas, PoseGuideOverlay? guide, float scale)
    {
        if (guide is null) return;
        var px = 1f / Math.Max(0.01f, scale);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };

        paint.Color = OverlayStyle.Reference.WithAlpha(VolumeAlpha);
        foreach (var bone in guide.Bones)
        {
            if (bone.Width <= 0) continue;
            paint.StrokeWidth = (float)bone.Width;
            canvas.DrawLine((float)bone.From.X, (float)bone.From.Y, (float)bone.To.X, (float)bone.To.Y, paint);
        }

        paint.Color = OverlayStyle.Reference;
        paint.StrokeWidth = BoneWidth * px;
        foreach (var bone in guide.Bones)
        {
            canvas.DrawLine((float)bone.From.X, (float)bone.From.Y, (float)bone.To.X, (float)bone.To.Y, paint);
        }

        paint.StrokeWidth = RingWidth * px;
        var radius = (guide.Editable ? EditableRing : Ring) * px;
        foreach (var joint in guide.Joints)
        {
            canvas.DrawCircle((float)joint.At.X, (float)joint.At.Y, radius, paint);
        }
    }
}
