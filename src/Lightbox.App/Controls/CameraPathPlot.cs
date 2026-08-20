using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lightbox.App.Controls;

/// <summary>
/// The camera's path across the canvas, small: the page as an outlined
/// rectangle, the eased path as a line through it, a dot per authored key.
/// The Scene panel's other half (Q84) — where a shot's move is read at a
/// glance rather than scrubbed for.
/// </summary>
/// <remarks>
/// Draws three things and decides nothing, the <see cref="NavigatorView"/>
/// split: the points arrive already interpolated (the view model samples
/// <c>CameraOps.At</c> per frame, so the easing shows), and nothing here is
/// proportional to the document — a path is as long as the timeline, not the
/// canvas.
/// </remarks>
public sealed class CameraPathPlot : Control
{
    /// <summary>The document's size, which the plot letterboxes.</summary>
    public static readonly StyledProperty<Size> DocumentSizeProperty =
        AvaloniaProperty.Register<CameraPathPlot, Size>(nameof(DocumentSize), new Size(1, 1));

    /// <summary>The camera's centre on every frame, in document coordinates.</summary>
    public static readonly StyledProperty<IReadOnlyList<Point>?> PathPointsProperty =
        AvaloniaProperty.Register<CameraPathPlot, IReadOnlyList<Point>?>(nameof(PathPoints));

    /// <summary>The authored keys, in document coordinates.</summary>
    public static readonly StyledProperty<IReadOnlyList<Point>?> KeyPointsProperty =
        AvaloniaProperty.Register<CameraPathPlot, IReadOnlyList<Point>?>(nameof(KeyPoints));

    static CameraPathPlot()
    {
        AffectsRender<CameraPathPlot>(DocumentSizeProperty, PathPointsProperty, KeyPointsProperty);
    }

    public Size DocumentSize
    {
        get => GetValue(DocumentSizeProperty);
        set => SetValue(DocumentSizeProperty, value);
    }

    public IReadOnlyList<Point>? PathPoints
    {
        get => GetValue(PathPointsProperty);
        set => SetValue(PathPointsProperty, value);
    }

    public IReadOnlyList<Point>? KeyPoints
    {
        get => GetValue(KeyPointsProperty);
        set => SetValue(KeyPointsProperty, value);
    }

    /// <summary>The page inside the control, letterboxed and centred.</summary>
    private Rect PageBounds()
    {
        var doc = DocumentSize;
        if (doc.Width <= 0 || doc.Height <= 0 || Bounds.Width <= 4 || Bounds.Height <= 4)
        {
            return default;
        }
        var scale = Math.Min((Bounds.Width - 4) / doc.Width, (Bounds.Height - 4) / doc.Height);
        var w = doc.Width * scale;
        var h = doc.Height * scale;
        return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
    }

    public override void Render(DrawingContext context)
    {
        var page = PageBounds();
        if (page.Width <= 0) return;

        // The page, faint — the same outline weight the navigator gives it.
        context.DrawRectangle(
            null, new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))), page);

        var doc = DocumentSize;
        Point Map(Point p) => new(
            page.X + p.X / doc.Width * page.Width,
            page.Y + p.Y / doc.Height * page.Height);

        // The path in the camera's own orange, the colour its timeline track
        // already wears — one hue for one concept, wherever it shows.
        var orange = Color.FromArgb(235, 255, 190, 60);
        if (PathPoints is { Count: > 1 } path)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(Map(path[0]), isFilled: false);
                for (var i = 1; i < path.Count; i++) g.LineTo(Map(path[i]));
                g.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(new SolidColorBrush(orange), 1.5), geometry);
        }

        if (KeyPoints is { Count: > 0 } keys)
        {
            var fill = new SolidColorBrush(orange);
            foreach (var key in keys)
            {
                context.DrawEllipse(fill, null, Map(key), 2.5, 2.5);
            }
        }
    }
}
