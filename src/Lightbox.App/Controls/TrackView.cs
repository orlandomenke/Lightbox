using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lightbox.App.Controls;

/// <summary>
/// One row of the track timeline, as plain data: the view model projects the
/// exposure sheet (and the camera) into these, and <see cref="TrackView"/>
/// draws them. A fresh list arrives on every timeline change, which is what
/// tells the control to re-render.
/// </summary>
/// <param name="Name">The label in the gutter.</param>
/// <param name="Keys">Frames carrying a drawing (or a camera key).</param>
/// <param name="HoldEnds">
/// For each entry in <paramref name="Keys"/>, the last frame its drawing is
/// exposed — the bar the reference draws behind the dot.
/// </param>
/// <param name="Breakdowns">Which of <paramref name="Keys"/> are breakdowns (hollow dots).</param>
/// <param name="IsCamera">The camera track wears the fixed camera colour.</param>
public sealed record TrackRow(
    string Name,
    IReadOnlyList<int> Keys,
    IReadOnlyList<int> HoldEnds,
    IReadOnlyList<bool> Breakdowns,
    bool IsCamera);

/// <summary>
/// The reference's timeline: one coloured track per layer, drawings as dots,
/// holds as bars, the camera as its own track, a ruler and a playhead. Dots
/// drag to retime; the host answers <see cref="KeyDragged"/> because moving a
/// cel is the document's business, not a view's.
/// </summary>
public class TrackView : Control
{
    public static readonly StyledProperty<IReadOnlyList<TrackRow>?> TracksProperty =
        AvaloniaProperty.Register<TrackView, IReadOnlyList<TrackRow>?>(nameof(Tracks));

    public IReadOnlyList<TrackRow>? Tracks
    {
        get => GetValue(TracksProperty);
        set => SetValue(TracksProperty, value);
    }

    /// <summary>Pixels per frame; bound to the same slider the X-sheet uses.</summary>
    public static readonly StyledProperty<double> FrameWidthProperty =
        AvaloniaProperty.Register<TrackView, double>(nameof(FrameWidth), 14);

    public double FrameWidth
    {
        get => GetValue(FrameWidthProperty);
        set => SetValue(FrameWidthProperty, value);
    }

    public static readonly StyledProperty<int> CurrentFrameProperty =
        AvaloniaProperty.Register<TrackView, int>(
            nameof(CurrentFrame), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public int CurrentFrame
    {
        get => GetValue(CurrentFrameProperty);
        set => SetValue(CurrentFrameProperty, value);
    }

    public static readonly StyledProperty<int> FrameCountProperty =
        AvaloniaProperty.Register<TrackView, int>(nameof(FrameCount), 1);

    public int FrameCount
    {
        get => GetValue(FrameCountProperty);
        set => SetValue(FrameCountProperty, value);
    }

    /// <summary>
    /// A key dot was dragged from one frame to another on the given track
    /// (index into <see cref="Tracks"/>). The host retimes the document.
    /// </summary>
    public event Action<int, int, int>? KeyDragged;

    // The reference's geometry, measured off its timeline strip.
    internal const double Gutter = 118;   // track names
    internal const double RulerHeight = 20;
    internal const double RowPitch = 22;
    private const double DotRadius = 4.5;

    static TrackView()
    {
        AffectsMeasure<TrackView>(TracksProperty, FrameWidthProperty, FrameCountProperty);
        AffectsRender<TrackView>(TracksProperty, FrameWidthProperty, CurrentFrameProperty, FrameCountProperty);
    }

    // ---- geometry, static so the tests can hold it still ---------------------

    internal static double XAtFrame(int frame, double frameWidth) =>
        Gutter + (frame + 0.5) * frameWidth;

    internal static int FrameAtX(double x, double frameWidth, int frameCount) =>
        Math.Clamp((int)Math.Floor((x - Gutter) / frameWidth), 0, Math.Max(0, frameCount - 1));

    internal static double YAtRow(int row) => RulerHeight + (row + 0.5) * RowPitch;

    internal static int RowAtY(double y, int rows) =>
        Math.Clamp((int)Math.Floor((y - RulerHeight) / RowPitch), 0, Math.Max(0, rows - 1));

    // ---- colours --------------------------------------------------------------

    /// <summary>
    /// The per-track palette, cycling — the reference gives every track its
    /// own hue so a row is findable by colour before it is read by name. The
    /// camera always takes the orange, matching the reference.
    /// </summary>
    private static readonly Color[] TrackColours =
    [
        Color.Parse("#7B61FF"), // violet
        Color.Parse("#4FA3FF"), // blue
        Color.Parse("#E85FBE"), // magenta
        Color.Parse("#2FD1B9"), // teal
        Color.Parse("#7BD156"), // green
        Color.Parse("#E8C55F"), // gold
    ];

    private static readonly Color CameraColour = Color.Parse("#FF9F45");

    internal static Color ColourOf(int row, bool isCamera) =>
        isCamera ? CameraColour : TrackColours[row % TrackColours.Length];

    // ---- layout ---------------------------------------------------------------

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = Tracks?.Count ?? 0;
        return new Size(
            Gutter + FrameCount * FrameWidth + 24,
            RulerHeight + rows * RowPitch + 6);
    }

    // ---- painting -------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        var tracks = Tracks;
        if (tracks is null || tracks.Count == 0) return;

        var text = new SolidColorBrush(Color.Parse("#A6ABB8"));
        var faint = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1);
        var typeface = new Typeface(Avalonia.Media.FontFamily.Default);

        // The light grid: a per-frame vertical when the frames are wide enough
        // to want one, and a hairline under each track row. Barely-there — it
        // exists so a dot's frame can be read without counting.
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)), 1);
        if (FrameWidth >= 8)
        {
            for (var f = 0; f < FrameCount; f++)
            {
                if (f % 12 == 0) continue;   // the ruler line below already covers it
                var gx = XAtFrame(f, FrameWidth);
                context.DrawLine(grid, new Point(gx, RulerHeight), new Point(gx, Bounds.Height));
            }
        }
        for (var r = 0; r <= tracks.Count; r++)
        {
            var gy = RulerHeight + r * RowPitch;
            context.DrawLine(grid,
                new Point(Gutter, gy), new Point(Gutter + FrameCount * FrameWidth, gy));
        }

        // Ruler: a number at 1 and every dozen frames after, the reference's
        // cadence; a faint vertical at each numbered frame.
        for (var f = 0; f < FrameCount; f += 12)
        {
            var x = XAtFrame(f, FrameWidth);
            context.DrawLine(faint, new Point(x, RulerHeight), new Point(x, Bounds.Height));
            var label = new FormattedText(
                (f + 1).ToString(), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, text);
            context.DrawText(label, new Point(x - label.Width / 2, 2));
        }

        for (var r = 0; r < tracks.Count; r++)
        {
            var track = tracks[r];
            var colour = ColourOf(r, track.IsCamera);
            var y = YAtRow(r);

            var name = new FormattedText(
                track.Name, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 11, text);
            context.DrawText(name, new Point(10, y - name.Height / 2));

            // The track line, faint, full width — the rail the dots ride.
            var rail = new Pen(new SolidColorBrush(Color.FromArgb(0x30, colour.R, colour.G, colour.B)), 2);
            context.DrawLine(rail,
                new Point(Gutter, y), new Point(Gutter + FrameCount * FrameWidth, y));

            for (var i = 0; i < track.Keys.Count; i++)
            {
                var key = track.Keys[i];
                // The hold bar first, under its dot.
                var holdEnd = i < track.HoldEnds.Count ? track.HoldEnds[i] : key;
                if (holdEnd > key)
                {
                    var bar = new SolidColorBrush(Color.FromArgb(0x66, colour.R, colour.G, colour.B));
                    context.DrawRectangle(bar, null, new RoundedRect(new Rect(
                        XAtFrame(key, FrameWidth), y - 2.5,
                        (holdEnd - key) * FrameWidth, 5), 2.5));
                }

                var at = new Point(XAtFrame(key, FrameWidth), y);
                var isBreakdown = i < track.Breakdowns.Count && track.Breakdowns[i];
                if (isBreakdown)
                {
                    context.DrawEllipse(null, new Pen(new SolidColorBrush(colour), 2), at, DotRadius - 1, DotRadius - 1);
                }
                else
                {
                    context.DrawEllipse(new SolidColorBrush(colour), null, at, DotRadius, DotRadius);
                }
            }
        }

        // A dragged dot's ghost, so the hand sees where the drawing will land.
        if (_drag is { } d)
        {
            var colour = ColourOf(d.Row, tracks[d.Row].IsCamera);
            context.DrawEllipse(
                new SolidColorBrush(Color.FromArgb(0x88, colour.R, colour.G, colour.B)), null,
                new Point(XAtFrame(d.ToFrame, FrameWidth), YAtRow(d.Row)), DotRadius, DotRadius);
        }

        // The playhead, over everything.
        var playX = XAtFrame(CurrentFrame, FrameWidth);
        var play = new Pen(new SolidColorBrush(Color.Parse("#B49CFF")), 1.5);
        context.DrawLine(play, new Point(playX, 0), new Point(playX, Bounds.Height));
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#7B61FF")), null,
            new RoundedRect(new Rect(playX - 9, 2, 18, 13), 3));
        var frameLabel = new FormattedText(
            (CurrentFrame + 1).ToString(), System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 9, Brushes.White);
        context.DrawText(frameLabel, new Point(playX - frameLabel.Width / 2, 4));
    }

    // ---- interaction -----------------------------------------------------------

    private (int Row, int FromFrame, int ToFrame)? _drag;
    private bool _scrubbing;

    /// <summary>The key index under the pointer, or null when it missed every dot.</summary>
    internal static int? KeyHit(TrackRow track, int frame, double x, double frameWidth)
    {
        for (var i = 0; i < track.Keys.Count; i++)
        {
            if (Math.Abs(XAtFrame(track.Keys[i], frameWidth) - x) <= 6) return track.Keys[i];
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var tracks = Tracks;
        if (tracks is null || tracks.Count == 0) return;
        var p = e.GetPosition(this);
        if (p.X < Gutter) return;

        if (p.Y > RulerHeight)
        {
            var row = RowAtY(p.Y, tracks.Count);
            if (Math.Abs(p.Y - YAtRow(row)) <= RowPitch / 2 &&
                KeyHit(tracks[row], 0, p.X, FrameWidth) is { } grabbed)
            {
                _drag = (row, grabbed, grabbed);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        // Anywhere else in the frame area scrubs, ruler included.
        _scrubbing = true;
        e.Pointer.Capture(this);
        CurrentFrame = FrameAtX(p.X, FrameWidth, FrameCount);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag is { } d)
        {
            var to = FrameAtX(p.X, FrameWidth, FrameCount);
            if (to != d.ToFrame)
            {
                _drag = (d.Row, d.FromFrame, to);
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }
        if (_scrubbing)
        {
            CurrentFrame = FrameAtX(p.X, FrameWidth, FrameCount);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is { } d)
        {
            _drag = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
            if (d.ToFrame != d.FromFrame) KeyDragged?.Invoke(d.Row, d.FromFrame, d.ToFrame);
            e.Handled = true;
            return;
        }
        if (_scrubbing)
        {
            _scrubbing = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
