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
/// <para>
/// In the reference colour, because it is exactly what that colour is for: a
/// mark to draw against rather than the thing being drawn. At rest it is one
/// faint pass — the tube a wash, the bones and rings one uncased pixel in the
/// amber turned down — so the artist's own line, drawn over it, is always the
/// darkest thing on the paper.
/// </para>
/// <para>
/// The tubes are laid together and washed once: washed one by one they
/// doubled wherever two met, and a leg read as a string of sausages instead
/// of one form. While the guide is being edited, on a key, the bones are cased
/// and the joints are grips — squares, because a joint moves freely.
/// </para>
/// </remarks>
public static class PoseGuidePainter
{
    /// <summary>The volume's wash: faint enough to draw over.</summary>
    private const byte VolumeAlpha = 38;

    /// <summary>The bones and rings at rest: the amber turned down.</summary>
    private const byte RestAlpha = 170;

    /// <summary>A joint's ring at rest, in screen pixels.</summary>
    private const float Ring = 4f;

    /// <summary>Paint the guide; expects the canvas in document space. Null draws nothing.</summary>
    public static void Paint(SKCanvas canvas, PoseGuideOverlay? guide, float scale)
    {
        if (guide is null) return;
        var px = 1f / Math.Max(0.01f, scale);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };

        // The volume: every tube opaque in a layer, the layer washed on once.
        if (guide.Bones.Any(b => b.Width > 0))
        {
            // A layer paint lends only its alpha: the colour is the tubes'.
            using var wash = new SKPaint { Color = OverlayStyle.Reference.WithAlpha(VolumeAlpha) };
            canvas.SaveLayer(wash);
            paint.Color = OverlayStyle.Reference;
            foreach (var bone in guide.Bones)
            {
                if (bone.Width <= 0) continue;
                paint.StrokeWidth = (float)bone.Width;
                canvas.DrawLine((float)bone.From.X, (float)bone.From.Y, (float)bone.To.X, (float)bone.To.Y, paint);
            }
            canvas.Restore();
        }

        if (guide.Editable)
        {
            paint.Color = OverlayStyle.Casing;
            paint.StrokeWidth = OverlayStyle.CasingWidth * px;
            Bones(canvas, guide, paint);
            paint.Color = OverlayStyle.Reference;
            paint.StrokeWidth = OverlayStyle.LineWidth * px;
            Bones(canvas, guide, paint);
            Grips(canvas, guide, px);
            return;
        }

        paint.Color = OverlayStyle.Reference.WithAlpha(RestAlpha);
        paint.StrokeWidth = OverlayStyle.ThinWidth * px;
        Bones(canvas, guide, paint);
        foreach (var joint in guide.Joints)
        {
            canvas.DrawCircle((float)joint.At.X, (float)joint.At.Y, Ring * px, paint);
        }
    }

    private static void Bones(SKCanvas canvas, PoseGuideOverlay guide, SKPaint paint)
    {
        foreach (var bone in guide.Bones)
        {
            canvas.DrawLine((float)bone.From.X, (float)bone.From.Y, (float)bone.To.X, (float)bone.To.Y, paint);
        }
    }

    /// <summary>Each joint as a grip: cased, open inside, outlined in the amber.</summary>
    private static void Grips(SKCanvas canvas, PoseGuideOverlay guide, float px)
    {
        using var casing = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = OverlayStyle.Casing, StrokeWidth = OverlayStyle.CasingWidth * px };
        using var open = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = OverlayStyle.Open };
        using var outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = OverlayStyle.Reference, StrokeWidth = OverlayStyle.GripOutline * px };
        var half = OverlayStyle.GripHalf * px;
        foreach (var joint in guide.Joints)
        {
            var box = new SKRect((float)joint.At.X - half, (float)joint.At.Y - half, (float)joint.At.X + half, (float)joint.At.Y + half);
            canvas.DrawRect(box, casing);
            canvas.DrawRect(box, open);
            canvas.DrawRect(box, outline);
        }
    }
}
