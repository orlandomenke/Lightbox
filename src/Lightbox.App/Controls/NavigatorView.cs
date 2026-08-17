using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Lightbox.App.Controls;

/// <summary>
/// The whole drawing at a glance, with the part you are looking at drawn on it —
/// and a click anywhere to go there (Q110).
/// </summary>
/// <remarks>
/// <para>
/// <b>It draws two things and decides nothing.</b> The picture is handed to it
/// already composited and already small; the rectangle is the viewport the
/// canvas reports. Where a click should take the view is document coordinates
/// raised as an event, because moving the view is the canvas's business — the
/// same split every other panel here follows.
/// </para>
/// <para>
/// <b>Nothing here is proportional to the document.</b> A 4K page and a
/// thumbnail cost the same: one image draw and one rectangle, whatever the
/// artist is panning across.
/// </para>
/// </remarks>
public sealed class NavigatorView : Control
{
    public static readonly StyledProperty<Bitmap?> PictureProperty =
        AvaloniaProperty.Register<NavigatorView, Bitmap?>(nameof(Picture));

    /// <summary>The document rectangle on screen, or null when all of it is.</summary>
    public static readonly StyledProperty<SKRectI?> ViewportProperty =
        AvaloniaProperty.Register<NavigatorView, SKRectI?>(nameof(Viewport));

    /// <summary>The document's own size, which the picture is a scaling of.</summary>
    public static readonly StyledProperty<Size> DocumentSizeProperty =
        AvaloniaProperty.Register<NavigatorView, Size>(nameof(DocumentSize), new Size(1, 1));

    static NavigatorView()
    {
        AffectsRender<NavigatorView>(PictureProperty, ViewportProperty, DocumentSizeProperty);
    }

    public Bitmap? Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    public SKRectI? Viewport
    {
        get => GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    public Size DocumentSize
    {
        get => GetValue(DocumentSizeProperty);
        set => SetValue(DocumentSizeProperty, value);
    }

    /// <summary>Take the view to this point, in document coordinates.</summary>
    public event Action<double, double>? CentreRequested;

    private bool _dragging;

    /// <summary>Where the picture sits inside the control, letterboxed and centred.</summary>
    /// <remarks>
    /// Aspect is preserved for the reason every other reference surface here
    /// preserves it: a stretched picture of the drawing would put the viewport
    /// rectangle somewhere the drawing is not.
    /// </remarks>
    public Rect PictureBounds()
    {
        var doc = DocumentSize;
        if (doc.Width <= 0 || doc.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return default;
        }
        var scale = Math.Min(Bounds.Width / doc.Width, Bounds.Height / doc.Height);
        var w = doc.Width * scale;
        var h = doc.Height * scale;
        return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
    }

    /// <summary>A point on the control as a point in the document.</summary>
    public (double X, double Y) ToDocument(Point at)
    {
        var picture = PictureBounds();
        if (picture.Width <= 0 || picture.Height <= 0) return (0, 0);
        var doc = DocumentSize;
        return (
            (at.X - picture.X) / picture.Width * doc.Width,
            (at.Y - picture.Y) / picture.Height * doc.Height);
    }

    public override void Render(DrawingContext context)
    {
        var picture = PictureBounds();
        if (picture.Width <= 0) return;

        if (Picture is { } bitmap)
        {
            context.DrawImage(bitmap, new Rect(bitmap.Size), picture);
        }
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))), picture);

        if (Viewport is not { } viewport) return;
        var doc = DocumentSize;
        var box = new Rect(
            picture.X + (viewport.Left / doc.Width * picture.Width),
            picture.Y + (viewport.Top / doc.Height * picture.Height),
            Math.Max(2, viewport.Width / doc.Width * picture.Width),
            Math.Max(2, viewport.Height / doc.Height * picture.Height));

        // Outline only, and bright: the navigator's whole job is to say where
        // you are without hiding what is there.
        context.DrawRectangle(
            null, new Pen(new SolidColorBrush(Color.FromArgb(235, 255, 190, 60)), 1.5), box.Intersect(picture));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        e.Pointer.Capture(this);
        Centre(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        // A drag scrubs the view around, which is what makes this a navigator
        // rather than a picture with a click target on it.
        if (_dragging) Centre(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void Centre(Point at)
    {
        var (x, y) = ToDocument(at);
        CentreRequested?.Invoke(x, y);
    }
}
