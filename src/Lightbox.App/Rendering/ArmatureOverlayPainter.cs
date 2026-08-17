using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>One control point's influence under the selected bone, for the heat view.</summary>
public readonly record struct HeatPoint(double X, double Y, double Weight);

/// <summary>
/// Draws the armature — bones as tapered shafts with handles — and the weight
/// heat view over the strokes. Chrome over the drawing, never into it.
/// </summary>
/// <remarks>
/// <para>
/// A separate class for <see cref="RigOverlayPainter"/>'s reason, which is
/// B58's lesson written down: a draw op only runs inside a compositor lease a
/// headless test never gets, so anything painted there is unreachable by
/// test and a whole feature can ship green and invisible. Everything here
/// takes an <see cref="SKCanvas"/>, so a test paints into a bitmap and reads
/// pixels.
/// </para>
/// <para>
/// Screen-sized furniture, scale divided out — the same numbers
/// <see cref="ArmatureOverlay.Hit"/> uses, or the thing you can see is not
/// the thing you can grab.
/// </para>
/// </remarks>
public static class ArmatureOverlayPainter
{
    /// <summary>Bones: green, because the rig chrome must not blur into the blue anchors or orange shapes.</summary>
    private static readonly SKColor BoneColor = new(0x6f, 0xd6, 0x6f);

    /// <summary>Selection: white, the same rule every overlay follows.</summary>
    private static readonly SKColor SelectedColor = new(0xff, 0xff, 0xff);

    /// <summary>
    /// The dark rim under every bone and handle — what makes the chrome read
    /// on paper. White-means-selected was calibrated on a dark canvas, where
    /// it measures 14:1; on the white paper most documents are it measured
    /// <b>1.00:1</b> — the selected bone literally disappeared, and the green
    /// and amber were little better (~1.8:1). Marching ants solve the same
    /// problem the same way: a light line over a dark one is legible on any
    /// ground. Translucent, so on dark canvases it fades into what is behind
    /// it instead of fattening the chrome.
    /// </summary>
    private static readonly SKColor RimColor = new(0x20, 0x20, 0x20, 0xc8);

    /// <summary>
    /// IK handles and poles: amber, so the thing an artist grabs to pose a
    /// limb is not the same green as the limb. A handle that looked like a
    /// bone would be findable only by knowing it was there, which is the
    /// failure this tool has already been reported for.
    /// </summary>
    private static readonly SKColor HandleColor = new(0xf0, 0xb0, 0x40);

    /// <summary>Cold end of the heat ramp — no influence.</summary>
    private static readonly SKColor HeatCold = new(0x28, 0x50, 0xc8);

    /// <summary>Hot end — the bone owns the point.</summary>
    private static readonly SKColor HeatHot = new(0xe0, 0x30, 0x28);

    /// <summary>Heat dot radius in screen pixels.</summary>
    public const float HeatDotScreen = 3;

    /// <summary>How wide a bone's base reads, in screen pixels.</summary>
    public const float BoneBaseScreen = 4.5f;

    /// <summary>Paint the bones. Expects the canvas already in document space; null or empty draws nothing.</summary>
    /// <remarks>
    /// Ghosts first, then the live skeleton, whatever order the list arrived
    /// in — a neighbouring pose is context and must never sit over the thing
    /// being aimed at. Live bones draw over a dark rim (<see cref="RimColor"/>);
    /// ghosts deliberately have none, and are outlines without handles: they
    /// should recede, and a handle on a pose nobody can grab is a lie.
    /// </remarks>
    public static void Paint(SKCanvas canvas, IReadOnlyList<BoneChrome>? bones, float scale)
    {
        if (bones is not { Count: > 0 }) return;

        var px = 1f / Math.Max(0.01f, scale);
        var half = BoneBaseScreen * px;
        var handle = (float)ArmatureOverlay.HandleScreenRadius * px;

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * px,
        };
        using var rim = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3.5f * px,
            Color = RimColor,
        };

        // The parent tethers, under everything: a dashed line from the
        // parent's tip to a joint that stands apart from it, so hierarchy
        // stays legible when bones separate. Dashes in screen pixels, like
        // the rest of the furniture; rim first for the same reason the bones
        // have one. Ghosts carry no links by construction.
        using (var linkDash = SKPathEffect.CreateDash([4f * px, 4f * px], 0))
        {
            using var linkRim = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f * px,
                Color = RimColor,
                PathEffect = linkDash,
            };
            using var link = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f * px,
                PathEffect = linkDash,
            };
            foreach (var bone in bones)
            {
                if (bone is not { LinkX: { } lx, LinkY: { } ly, Ghost: BoneGhost.None }) continue;
                link.Color = (bone.Selected ? SelectedColor : bone.IsHandle ? HandleColor : BoneColor)
                    .WithAlpha(0xb4);
                canvas.DrawLine((float)lx, (float)ly, (float)bone.X0, (float)bone.Y0, linkRim);
                canvas.DrawLine((float)lx, (float)ly, (float)bone.X0, (float)bone.Y0, link);
            }
        }

        foreach (var ghostPass in new[] { true, false })
        foreach (var bone in bones)
        {
            var isGhost = bone.Ghost is not BoneGhost.None;
            if (isGhost != ghostPass) continue;

            var colour = isGhost
                ? (bone.Ghost is BoneGhost.Before ? SceneRenderer.OnionPrevTint : SceneRenderer.OnionNextTint)
                : bone.Selected ? SelectedColor : bone.IsHandle ? HandleColor : BoneColor;

            // The classic bone silhouette: a kite from a wide base at the
            // origin to a point at the tip, so direction reads at a glance.
            var dx = bone.X1 - bone.X0;
            var dy = bone.Y1 - bone.Y0;
            var len = Math.Sqrt(dx * dx + dy * dy);
            var (nx, ny) = len < 1e-9 ? (0.0, 0.0) : (-dy / len * half, dx / len * half);

            using (var path = new SKPath())
            {
                path.MoveTo((float)bone.X0, (float)bone.Y0);
                path.LineTo((float)(bone.X0 + dx * 0.18 + nx), (float)(bone.Y0 + dy * 0.18 + ny));
                path.LineTo((float)bone.X1, (float)bone.Y1);
                path.LineTo((float)(bone.X0 + dx * 0.18 - nx), (float)(bone.Y0 + dy * 0.18 - ny));
                path.Close();
                if (isGhost)
                {
                    stroke.Color = colour.WithAlpha(0x96);
                    canvas.DrawPath(path, stroke);
                    continue;
                }
                canvas.DrawPath(path, rim);
                fill.Color = colour.WithAlpha(0x50);
                canvas.DrawPath(path, fill);
                stroke.Color = colour;
                canvas.DrawPath(path, stroke);
            }

            // Handles on both ends — filled at the origin (a joint), open at
            // the tip (a direction), matching what each grab does.
            canvas.DrawCircle((float)bone.X0, (float)bone.Y0, handle * 0.8f, rim);
            fill.Color = colour;
            canvas.DrawCircle((float)bone.X0, (float)bone.Y0, handle * 0.8f, fill);
            canvas.DrawCircle((float)bone.X1, (float)bone.Y1, handle * 0.8f, rim);
            stroke.Color = colour;
            canvas.DrawCircle((float)bone.X1, (float)bone.Y1, handle * 0.8f, stroke);
        }
    }

    /// <summary>
    /// Paint the heat view: one dot per control point, blue through red by the
    /// selected bone's influence. Null or empty draws nothing — no bone
    /// selected means no heat, not a cold canvas.
    /// </summary>
    public static void PaintHeat(SKCanvas canvas, IReadOnlyList<HeatPoint>? points, float scale)
    {
        if (points is not { Count: > 0 }) return;

        var px = 1f / Math.Max(0.01f, scale);
        var r = HeatDotScreen * px;
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        foreach (var p in points)
        {
            var t = (float)Math.Clamp(p.Weight, 0, 1);
            fill.Color = new SKColor(
                (byte)(HeatCold.Red + (HeatHot.Red - HeatCold.Red) * t),
                (byte)(HeatCold.Green + (HeatHot.Green - HeatCold.Green) * t),
                (byte)(HeatCold.Blue + (HeatHot.Blue - HeatCold.Blue) * t),
                0xd0);
            canvas.DrawCircle((float)p.X, (float)p.Y, r, fill);
        }
    }
}
