using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lightbox.Core.Documents;

namespace Lightbox.App.Controls;

/// <summary>Which track a marker belongs to.</summary>
public enum RampTrack
{
    /// <summary>Above the ramp: opacity.</summary>
    Alpha,

    /// <summary>Below the ramp: colour.</summary>
    Color,
}

/// <summary>A stop the ramp is pointing at.</summary>
public readonly record struct RampSelection(RampTrack Track, int Index);

/// <summary>
/// The gradient itself, editable: the ramp with opacity markers above it and
/// colour markers below.
/// </summary>
/// <remarks>
/// A list of rows with position spinners is a description of a gradient. This
/// is the gradient — you place a stop where you want the change, because
/// where you want the change is a spatial judgement and a number is a poor
/// way to make one.
///
/// Two rows because the model has two tracks, and the model has two tracks
/// because opacity changes in different places from colour. See
/// <see cref="GradientAlphaStop"/>.
///
/// The control draws and reports; it does not decide. Adding, moving and
/// selecting all go out as events, so the edits land in the undo history the
/// same way every other document change does rather than mutating behind the
/// view model's back.
/// </remarks>
public class GradientRamp : Control
{
    public static readonly StyledProperty<Gradient?> GradientProperty =
        AvaloniaProperty.Register<GradientRamp, Gradient?>(nameof(Gradient));

    public static readonly StyledProperty<RampSelection?> SelectionProperty =
        AvaloniaProperty.Register<GradientRamp, RampSelection?>(nameof(Selection));

    public Gradient? Gradient
    {
        get => GetValue(GradientProperty);
        set => SetValue(GradientProperty, value);
    }

    public RampSelection? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>A marker was picked up and moved to this position (0–1).</summary>
    public event Action<RampSelection, double>? StopMoved;

    /// <summary>An empty part of a track was clicked: add a stop there.</summary>
    public event Action<RampTrack, double>? StopAdded;

    /// <summary>A marker was chosen.</summary>
    public event Action<RampSelection?>? SelectionChanged;

    /// <summary>A marker was double-clicked or middle-clicked: remove it.</summary>
    public event Action<RampSelection>? StopRemoved;

    private const double MarkerSize = 11;
    private const double TrackHeight = 14;
    private const double Inset = MarkerSize / 2 + 1;

    static GradientRamp()
    {
        AffectsRender<GradientRamp>(GradientProperty, SelectionProperty);
        FocusableProperty.OverrideDefaultValue<GradientRamp>(true);
    }

    public GradientRamp()
    {
        Height = 64;
        MinWidth = 160;
    }

    // ---- geometry --------------------------------------------------------------

    private Rect RampRect => new(
        Inset, TrackHeight, Math.Max(1, Bounds.Width - Inset * 2), Math.Max(1, Bounds.Height - TrackHeight * 2));

    private double PositionAt(double x) =>
        Math.Clamp((x - RampRect.X) / Math.Max(1, RampRect.Width), 0, 1);

    private double XOf(double position) => RampRect.X + position * RampRect.Width;

    /// <summary>Which marker is under the pointer, if any.</summary>
    private RampSelection? MarkerAt(Point p)
    {
        if (Gradient is not { } gradient) return null;
        var alphaRow = p.Y < RampRect.Y;
        var colourRow = p.Y > RampRect.Bottom;
        if (!alphaRow && !colourRow) return null;

        if (alphaRow)
        {
            var track = gradient.AlphaStops;
            if (track is null) return null;
            for (var i = 0; i < track.Count; i++)
            {
                if (Math.Abs(XOf(track[i].Position) - p.X) <= MarkerSize) return new(RampTrack.Alpha, i);
            }
            return null;
        }

        for (var i = 0; i < gradient.Stops.Count; i++)
        {
            if (Math.Abs(XOf(gradient.Stops[i].Position) - p.X) <= MarkerSize) return new(RampTrack.Color, i);
        }
        return null;
    }

    // ---- input -------------------------------------------------------------------

    private RampSelection? _dragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Gradient is null) return;
        var p = e.GetPosition(this);
        var point = e.GetCurrentPoint(this);

        if (MarkerAt(p) is { } hit)
        {
            // Middle-click removes, matching the "no dialog for a small
            // destructive act you can immediately undo" convention the
            // timeline already uses.
            if (point.Properties.IsMiddleButtonPressed)
            {
                StopRemoved?.Invoke(hit);
                e.Handled = true;
                return;
            }
            Selection = hit;
            SelectionChanged?.Invoke(hit);
            _dragging = hit;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Clicking a track where there is no marker adds one. Clicking the
        // ramp itself does nothing: it is a preview, and a stray click on it
        // should not silently change the gradient.
        if (!point.Properties.IsLeftButtonPressed) return;
        if (p.Y < RampRect.Y) StopAdded?.Invoke(RampTrack.Alpha, PositionAt(p.X));
        else if (p.Y > RampRect.Bottom) StopAdded?.Invoke(RampTrack.Color, PositionAt(p.X));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging is not { } dragging) return;
        StopMoved?.Invoke(dragging, PositionAt(e.GetPosition(this).X));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging is null) return;
        _dragging = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = null;
    }

    // ---- drawing -------------------------------------------------------------------

    private static readonly IBrush Checker = BuildChecker();

    private static IBrush BuildChecker() => new DrawingBrush
    {
        TileMode = TileMode.Tile,
        SourceRect = new RelativeRect(0, 0, 10, 10, RelativeUnit.Absolute),
        DestinationRect = new RelativeRect(0, 0, 10, 10, RelativeUnit.Absolute),
        Drawing = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Color.Parse("#4a4a4a")),
                    Geometry = new RectangleGeometry(new Rect(0, 0, 10, 10)),
                },
                new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Color.Parse("#383838")),
                    Geometry = new RectangleGeometry(new Rect(0, 0, 5, 5)),
                },
                new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Color.Parse("#383838")),
                    Geometry = new RectangleGeometry(new Rect(5, 5, 5, 5)),
                },
            },
        },
    };

    public override void Render(DrawingContext context)
    {
        var ramp = RampRect;
        // Checkerboard under the ramp, so a transparent stretch reads as
        // transparent rather than as whatever the panel behind it happens to be.
        context.FillRectangle(Checker, ramp);

        if (Gradient is not { } gradient)
        {
            context.DrawRectangle(null, new Pen(Brushes.Gray, 1), ramp);
            return;
        }

        // Sampled through GradientOps, so this is the ramp the canvas will
        // actually paint — including its alpha track.
        var stops = new GradientStops();
        for (var i = 0; i <= 32; i++)
        {
            var t = i / 32.0;
            var (r, g, b, a) = GradientOps.Sample(gradient, t);
            stops.Add(new Avalonia.Media.GradientStop(Color.FromArgb(a, r, g, b), t));
        }
        context.FillRectangle(
            new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = stops,
            },
            ramp);
        context.DrawRectangle(null, new Pen(Brushes.Gray, 1), ramp);

        if (gradient.AlphaStops is { } alphas)
        {
            for (var i = 0; i < alphas.Count; i++)
            {
                var shade = (byte)Math.Round(Math.Clamp(alphas[i].Alpha, 0, 1) * 255);
                DrawMarker(
                    context, XOf(alphas[i].Position), ramp.Y, up: false,
                    new SolidColorBrush(Color.FromRgb(shade, shade, shade)),
                    Selection == new RampSelection(RampTrack.Alpha, i));
            }
        }

        for (var i = 0; i < gradient.Stops.Count; i++)
        {
            var (r, g, b) = GimpPalette.Rgb(gradient.Stops[i].Color);
            DrawMarker(
                context, XOf(gradient.Stops[i].Position), ramp.Bottom, up: true,
                new SolidColorBrush(Color.FromRgb(r, g, b)),
                Selection == new RampSelection(RampTrack.Color, i));
        }
    }

    /// <summary>
    /// A house-shaped marker pointing at the ramp: the point marks the exact
    /// position, the body carries the colour or the shade.
    /// </summary>
    private static void DrawMarker(
        DrawingContext context, double x, double y, bool up, IBrush fill, bool selected)
    {
        var h = MarkerSize;
        var w = MarkerSize / 2;
        var tip = up ? y : y;
        var baseY = up ? y + h : y - h;

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new Point(x, tip), isFilled: true);
            sink.LineTo(new Point(x - w, up ? tip + w : tip - w));
            sink.LineTo(new Point(x - w, baseY));
            sink.LineTo(new Point(x + w, baseY));
            sink.LineTo(new Point(x + w, up ? tip + w : tip - w));
            sink.EndFigure(true);
        }

        var outline = selected
            ? new Pen(Brushes.White, 2)
            : new Pen(new SolidColorBrush(Color.Parse("#909090")), 1);
        context.DrawGeometry(fill, outline, geometry);
    }
}
