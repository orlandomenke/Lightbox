using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Controls;

/// <summary>
/// The resize dialog's preview: the current paper and the new one as two
/// outlines, so the direction the paper grows is visible before the button.
/// </summary>
/// <remarks>
/// <para>
/// <b>Outlines only, no artwork</b> — the owner's choice over a live thumbnail
/// and over arrows in the anchor grid. The question an artist has at this
/// moment is "which side gets the paper", and two rectangles answer it at any
/// document size for nothing; a thumbnail would cost a render of the scene and
/// say the same thing smaller.
/// </para>
/// <para>
/// Draws and decides nothing, the <see cref="CameraPathPlot"/> split: the
/// rectangles arrive placed and normalised from
/// <see cref="ResizeDialogViewModel.Outline"/>, whose placement is
/// <c>CanvasResize.OriginShift</c> — so this cannot disagree with the edit.
/// The new paper is a solid outline over a faint fill, the current paper a
/// dashed one, and where the current paper is cropped the part cut away keeps
/// only a dim fill.
/// </para>
/// </remarks>
public sealed class ResizeOutlinePreview : Control
{
    public static readonly StyledProperty<ResizeOutline?> OutlineProperty =
        AvaloniaProperty.Register<ResizeOutlinePreview, ResizeOutline?>(nameof(Outline));

    // Today's paper is dashed because it is the reference shown beside the
    // thing being decided (DESIGN.md: a dashed line is a copy, not the thing).
    private static readonly DashStyle Dashed = new([3, 2], 0);

    // Fills are the theme's ink at low alpha and stack: the cut-away part has
    // only the first, the new paper adds the second, the kept part the third,
    // so the drawing that survives reads strongest and a crop's loss dimmest.
    private const byte OldFill = 18, NewFill = 26, KeptFill = 34;

    // The new paper is the answer, so its edge is near full ink; today's is
    // a reference and steps back.
    private const byte OldEdge = 120, NewEdge = 230;

    static ResizeOutlinePreview()
    {
        AffectsRender<ResizeOutlinePreview>(OutlineProperty);
    }

    public ResizeOutlinePreview()
    {
        ThemeColour.RepaintOnThemeChange(this);
    }

    /// <summary>The two papers, in the view model's unit space.</summary>
    public ResizeOutline? Outline
    {
        get => GetValue(OutlineProperty);
        set => SetValue(OutlineProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Outline is not { } o || o.Extent.Width <= 0 || o.Extent.Height <= 0) return;

        // Fit the union in the box with room for the pens: flush left, so the
        // drawing starts under its "Preview" header as the grid does under its
        // own, and centred vertically against the grid beside it.
        const double inset = 2;
        var room = new Size(Bounds.Width - 2 * inset, Bounds.Height - 2 * inset);
        if (room.Width <= 0 || room.Height <= 0) return;
        var fit = Math.Min(room.Width / o.Extent.Width, room.Height / o.Extent.Height);
        var origin = new Point(
            inset,
            inset + (room.Height - o.Extent.Height * fit) / 2);

        // Whole pixels plus a half, so a one-pixel pen lands on one pixel row.
        Rect Place(Rect r)
        {
            var left = Math.Round(origin.X + r.X * fit) + 0.5;
            var top = Math.Round(origin.Y + r.Y * fit) + 0.5;
            var right = Math.Round(origin.X + r.Right * fit) - 0.5;
            var bottom = Math.Round(origin.Y + r.Bottom * fit) - 0.5;
            return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        var ink = ThemeColour.Of(this, "TextPrimaryBrush", Colors.White);
        IBrush Tint(byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B));

        var oldRect = Place(o.Old);
        var newRect = Place(o.New);

        context.DrawRectangle(Tint(OldFill), null, oldRect);
        context.DrawRectangle(Tint(NewFill), null, newRect);
        context.DrawRectangle(Tint(KeptFill), null, Place(o.Kept));

        context.DrawRectangle(null, new Pen(Tint(OldEdge), 1, Dashed), oldRect);
        context.DrawRectangle(null, new Pen(Tint(NewEdge), 1), newRect);
    }
}
