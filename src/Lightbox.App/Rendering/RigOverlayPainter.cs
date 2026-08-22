using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Draws the rig: anchors as crosses, collision shapes as rectangles, and grab
/// handles on whichever one is selected.
/// </summary>
/// <remarks>
/// <para>
/// <b>B58.</b> <c>RigOverlay</c> decided what a press hit and what a drag produced,
/// <c>MainViewModel.Rig.cs</c> landed the edit on the drawing, and roughly thirty
/// tests covered hit order, screen-sized targets, drag maths, holds and re-times.
/// <b>Nothing drew any of it.</b> `RigMarks`, `RigEditMode`, `SelectedRigMarkId`,
/// `AddAnchorAt`, `AddShapeAt` and `HasRig` had no consumer outside that file and
/// its own tests — no binding, nothing in <c>CanvasControl</c>, no menu item, no
/// shortcut. An artist could not switch the mode on, so they could not place,
/// select, drag or delete a socket or a hitbox at all.
/// </para>
/// <para>
/// <b>A separate class for the reason <see cref="GuidePainter"/> is one.</b> The
/// draw op only runs inside a Skia lease from the compositor, which a headless run
/// never grants — so anything written inside it is unreachable by test, and that is
/// how a whole feature came to be green and invisible. Everything here takes an
/// <see cref="SKCanvas"/> and returns nothing, so a test paints into a bitmap and
/// reads pixels.
/// </para>
/// <para>
/// <b>Screen-sized rather than document-sized</b>, and the scale is divided out for
/// the same reason <c>RigOverlay.HandleScreenRadius</c> exists: a handle in document
/// pixels is unclickable at 25% and covers the drawing at 800%. The overlay's
/// geometry has to match the hit-test's or the thing you can see is not the thing
/// you can grab.
/// </para>
/// </remarks>
public static class RigOverlayPainter
{
    /// <summary>Anchors: a socket is a point, and a point reads as a cross.</summary>
    private static readonly SKColor AnchorColor = new(0x4d, 0xc4, 0xff);

    /// <summary>Shapes: warm against the anchors' cold, so the two kinds never blur.</summary>
    private static readonly SKColor ShapeColor = new(0xff, 0xa5, 0x3d);

    /// <summary>Selection: white, because it must win over both.</summary>
    private static readonly SKColor SelectedColor = new(0xff, 0xff, 0xff);

    /// <summary>Half-length of an anchor's cross arms, in screen pixels.</summary>
    public const float AnchorArmScreen = 7;

    /// <summary>
    /// Paint every mark. Expects the canvas already in document space.
    /// </summary>
    /// <param name="scale">
    /// View scale, so screen-sized furniture can be divided back into document
    /// units — the same number <c>RigOverlay.Hit</c> is given.
    /// </param>
    /// <remarks>
    /// An empty or null list draws nothing at all rather than drawing an empty
    /// frame: the mode being off yields no marks, and "absent, not merely
    /// invisible" is the rule the camera set and this follows.
    /// </remarks>
    public static void Paint(SKCanvas canvas, IReadOnlyList<RigMark>? marks, float scale)
    {
        if (marks is not { Count: > 0 }) return;

        var px = 1f / Math.Max(0.01f, scale);
        var arm = AnchorArmScreen * px;
        var handle = (float)RigOverlay.HandleScreenRadius * px;

        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * px,
        };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        // Shapes first, anchors over them: an anchor is usually inside a shape, and
        // the hit-test already prefers the anchor — so drawing them the other way
        // round would hide the mark you are allowed to grab behind the one you are
        // not. RigOverlayTests.AnAnchorInsideAShapeIsStillReachable is the same
        // decision from the input side.
        foreach (var mark in marks)
        {
            if (mark.Kind != RigMarkKind.Shape) continue;
            stroke.Color = mark.Selected ? SelectedColor : ShapeColor;
            var rect = new SKRect((float)mark.X, (float)mark.Y, (float)mark.Right, (float)mark.Bottom);
            canvas.DrawRect(rect, stroke);

            // Handles only on the selected shape. Four filled squares on every box
            // would turn a rig of a dozen hitboxes into confetti, and the hit-test
            // agrees — an unselected shape has no corners to grab.
            if (!mark.Selected) continue;
            fill.Color = SelectedColor;
            foreach (var (hx, hy) in new (double, double)[]
            {
                (mark.X, mark.Y), (mark.Right, mark.Y),
                (mark.X, mark.Bottom), (mark.Right, mark.Bottom),
            })
            {
                canvas.DrawRect(
                    new SKRect((float)hx - handle, (float)hy - handle, (float)hx + handle, (float)hy + handle),
                    fill);
            }
        }

        foreach (var mark in marks)
        {
            if (mark.Kind != RigMarkKind.Anchor) continue;
            stroke.Color = mark.Selected ? SelectedColor : AnchorColor;
            var x = (float)mark.X;
            var y = (float)mark.Y;
            canvas.DrawLine(x - arm, y, x + arm, y, stroke);
            canvas.DrawLine(x, y - arm, x, y + arm, stroke);
            // A ring on the selected one, so which socket is being dragged is
            // readable without reading the label.
            if (mark.Selected) canvas.DrawCircle(x, y, arm * 0.6f, stroke);

            // Q144: the direction stalk. An authored angle draws on every
            // anchor — a direction nobody can see without selecting is not a
            // direction — and the selected anchor with none gets a dim ghost
            // stub along +X, because grabbing the stub IS how an angle is
            // given. The tip handle appears with the selection, matching what
            // the hit-test will actually grab.
            if (!mark.HasAngle && !mark.Selected) continue;
            var (tx, ty) = RigOverlay.StalkTipOf(mark);
            // Past the continue above, no angle implies selected — the ghost
            // stub only exists on the anchor being worked on.
            stroke.Color = mark.HasAngle
                ? (mark.Selected ? SelectedColor : AnchorColor)
                : SelectedColor.WithAlpha(90);
            canvas.DrawLine(x, y, (float)tx, (float)ty, stroke);
            if (mark.Selected)
            {
                fill.Color = mark.HasAngle ? SelectedColor : SelectedColor.WithAlpha(140);
                canvas.DrawCircle((float)tx, (float)ty, handle, fill);
            }
        }
    }

    /// <summary>
    /// Names beside the marks, drawn separately so the caller can leave them out.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Paint"/> because text is the part that stops being
    /// useful when there is a lot of it: forty labels at 25% zoom are a grey smear
    /// over the drawing. Text is also the one thing here that needs a typeface, and
    /// keeping it out of <see cref="Paint"/> means the geometry can be pixel-tested
    /// without a font being involved at all.
    /// </remarks>
    public static void PaintLabels(SKCanvas canvas, IReadOnlyList<RigMark>? marks, float scale)
    {
        if (marks is not { Count: > 0 }) return;
        var px = 1f / Math.Max(0.01f, scale);

        using var font = new SKFont(SKTypeface.Default, 11 * px);
        using var text = new SKPaint { IsAntialias = true, Color = SelectedColor };
        using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 190) };

        foreach (var mark in marks)
        {
            if (string.IsNullOrEmpty(mark.Label)) continue;
            var x = (float)mark.X + AnchorArmScreen * px + 2 * px;
            var y = (float)mark.Y - 3 * px;
            // Drawn twice, offset by a pixel: a rig sits over artwork of unknown
            // colour, and a label that vanishes on a light drawing is a label
            // nobody can rely on.
            canvas.DrawText(mark.Label, x + px, y + px, SKTextAlign.Left, font, shadow);
            canvas.DrawText(mark.Label, x, y, SKTextAlign.Left, font, text);
        }
    }
}
