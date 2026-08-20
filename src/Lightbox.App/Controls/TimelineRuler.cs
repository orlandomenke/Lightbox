using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Lightbox.App.Controls;

/// <summary>
/// The timeline's numbering row, Blender-style: frame numbers above the cel
/// strips, a playhead you can grab and scrub, and playback-range indication
/// (frames outside a set start/end render greyed out, the bounds get
/// green/red markers). Layout constants (<see cref="LeadingInset"/>,
/// <see cref="CellWidth"/>) line the numbers up with the cel buttons below.
/// </summary>
public sealed class TimelineRuler : Control
{
    public static readonly StyledProperty<int> ExtentProperty =
        AvaloniaProperty.Register<TimelineRuler, int>(nameof(Extent), 24);

    /// <summary>Last scrubbable frame index (the real timeline end).</summary>
    public static readonly StyledProperty<int> MaxFrameProperty =
        AvaloniaProperty.Register<TimelineRuler, int>(nameof(MaxFrame));

    public static readonly StyledProperty<double> CellWidthProperty =
        AvaloniaProperty.Register<TimelineRuler, double>(nameof(CellWidth), 42);

    public static readonly StyledProperty<double> LeadingInsetProperty =
        AvaloniaProperty.Register<TimelineRuler, double>(nameof(LeadingInset));

    public static readonly StyledProperty<int> CurrentFrameProperty =
        AvaloniaProperty.Register<TimelineRuler, int>(nameof(CurrentFrame), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Whether the playhead is being dragged right now.</summary>
    /// <remarks>
    /// Two-way and outward-only, like <see cref="CurrentFrame"/>: the ruler owns
    /// the gesture and the canvas needs to know it is happening, because a moving
    /// sequence is what licenses compositing through tiles. Dragging a loop handle
    /// deliberately does not set it — the playhead is not moving, so there is no
    /// motion for the tile route to be justified by.
    /// </remarks>
    public static readonly StyledProperty<bool> IsScrubbingProperty =
        AvaloniaProperty.Register<TimelineRuler, bool>(
            nameof(IsScrubbing), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Playback range start (-1 = unset).</summary>
    /// <remarks>
    /// Two-way: the handles on the ruler write it. Dragging the loop bounds
    /// where you can see them beats two menu commands that each ask you to put
    /// the playhead somewhere first.
    /// </remarks>
    public static readonly StyledProperty<int> RangeStartProperty =
        AvaloniaProperty.Register<TimelineRuler, int>(
            nameof(RangeStart), -1, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Playback range end (-1 = unset).</summary>
    public static readonly StyledProperty<int> RangeEndProperty =
        AvaloniaProperty.Register<TimelineRuler, int>(
            nameof(RangeEnd), -1, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Colored frame tags to draw on the ruler (rebound as a fresh list on change).</summary>
    public static readonly StyledProperty<IReadOnlyList<Lightbox.Core.Documents.FrameMarker>?> MarkersProperty =
        AvaloniaProperty.Register<TimelineRuler, IReadOnlyList<Lightbox.Core.Documents.FrameMarker>?>(nameof(Markers));

    static TimelineRuler()
    {
        AffectsMeasure<TimelineRuler>(ExtentProperty, CellWidthProperty, LeadingInsetProperty);
        AffectsRender<TimelineRuler>(
            MaxFrameProperty, CurrentFrameProperty, RangeStartProperty, RangeEndProperty, MarkersProperty,
            CameraKeyedProperty, PoseKeyedProperty);
    }

    public bool IsScrubbing
    {
        get => GetValue(IsScrubbingProperty);
        set => SetValue(IsScrubbingProperty, value);
    }

    public int Extent { get => GetValue(ExtentProperty); set => SetValue(ExtentProperty, value); }

    public int MaxFrame { get => GetValue(MaxFrameProperty); set => SetValue(MaxFrameProperty, value); }

    public double CellWidth { get => GetValue(CellWidthProperty); set => SetValue(CellWidthProperty, value); }

    public double LeadingInset { get => GetValue(LeadingInsetProperty); set => SetValue(LeadingInsetProperty, value); }

    public int CurrentFrame { get => GetValue(CurrentFrameProperty); set => SetValue(CurrentFrameProperty, value); }

    public int RangeStart { get => GetValue(RangeStartProperty); set => SetValue(RangeStartProperty, value); }

    public int RangeEnd { get => GetValue(RangeEndProperty); set => SetValue(RangeEndProperty, value); }

    public IReadOnlyList<Lightbox.Core.Documents.FrameMarker>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    /// <summary>
    /// Frames carrying a camera key, and frames carrying a pose key — the two
    /// things that are animated here and have no cel to show it with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Opt in, and empty rather than hidden when it is off.</b> The X-sheet
    /// is the exposure sheet: it shows ink, and camera moves and bone poses are
    /// not ink, which is why they live on the Timeline instead. What an artist
    /// can lose by that division is the knowledge that <em>something happens
    /// here at all</em> — four drawings on the sheet and a pose on frame 9 with
    /// nothing on the sheet to say so.
    /// </para>
    /// <para>
    /// Two sets rather than one, in the colours the Timeline already gives those
    /// tracks, so a mark says which without needing a legend. The control is
    /// told which frames to mark and nothing about why, the same division the
    /// rest of this file keeps.
    /// </para>
    /// </remarks>
    public static readonly StyledProperty<IReadOnlySet<int>?> CameraKeyedProperty =
        AvaloniaProperty.Register<TimelineRuler, IReadOnlySet<int>?>(nameof(CameraKeyed));

    public IReadOnlySet<int>? CameraKeyed
    {
        get => GetValue(CameraKeyedProperty);
        set => SetValue(CameraKeyedProperty, value);
    }

    public static readonly StyledProperty<IReadOnlySet<int>?> PoseKeyedProperty =
        AvaloniaProperty.Register<TimelineRuler, IReadOnlySet<int>?>(nameof(PoseKeyed));

    public IReadOnlySet<int>? PoseKeyed
    {
        get => GetValue(PoseKeyedProperty);
        set => SetValue(PoseKeyedProperty, value);
    }

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#181818"));
    private static readonly IBrush PlayheadBrush = new SolidColorBrush(Color.Parse("#4a6ea9"));
    private static readonly IBrush NumberBrush = new SolidColorBrush(Color.Parse("#c8c8c8"));
    private static readonly IBrush DimNumberBrush = new SolidColorBrush(Color.Parse("#5a5a5a"));
    private static readonly IBrush VirtualNumberBrush = new SolidColorBrush(Color.Parse("#454545"));
    private static readonly IBrush TickBrush = new SolidColorBrush(Color.Parse("#3a3a3a"));
    private static readonly IBrush RangeStartBrush = new SolidColorBrush(Color.Parse("#4caf50"));
    private static readonly IBrush RangeEndBrush = new SolidColorBrush(Color.Parse("#e05555"));

    // The Timeline's own track colours, so a mark here and the track it stands
    // for are recognisably the same thing.
    private static readonly IBrush CameraKeyBrush = new SolidColorBrush(TrackView.CameraColour);
    private static readonly IBrush PoseKeyBrush = new SolidColorBrush(Color.Parse("#7EC8E3"));

    /// <summary>How wide a mark is, and how far in from the cell's edge it sits.</summary>
    private const double MarkSize = 3;

    // ---- the loop handles ----------------------------------------------------------

    /// <summary>
    /// Which loop handle is being dragged, if either.
    /// </summary>
    /// <remarks>
    /// Toon Boom's gesture. The range already existed as two menu commands
    /// that each ask you to park the playhead somewhere first; dragging the
    /// bounds where you can see them is the same decision made in one motion.
    /// </remarks>
    private enum Handle { None, Start, End }

    private Handle _dragging;

    /// <summary>Where the pointer is, so the handles can appear on hover.</summary>
    private double? _hoverX;

    /// <summary>How close, in pixels, counts as being on a handle.</summary>
    private const double GrabPixels = 7;

    /// <summary>How tall the handle grips are — a fixed strip, not the whole ruler.</summary>
    private const double HandleHeight = 9;

    private static readonly IBrush HandleGhostBrush = new SolidColorBrush(Color.Parse("#6a6a6a"));
    private static readonly IBrush LoopBarBrush = new SolidColorBrush(Color.Parse("#3d6b3f"));

    /// <summary>The effective range, with unset bounds resolved to the timeline's own.</summary>
    private (int Start, int End) EffectiveRange() =>
        (Math.Max(0, RangeStart), RangeEnd < 0 ? MaxFrame : RangeEnd);

    /// <summary>Where a handle sits, in pixels. The end handle is on the far side of its frame.</summary>
    private double HandleX(Handle which)
    {
        var (start, end) = EffectiveRange();
        return which == Handle.Start
            ? LeadingInset + start * CellWidth
            : LeadingInset + (end + 1) * CellWidth;
    }

    private Handle HandleAt(double x)
    {
        // The start handle is tested first: with a one-frame range the two sit
        // a cell apart and the left one is the one you can still drag right.
        if (Math.Abs(x - HandleX(Handle.Start)) <= GrabPixels) return Handle.Start;
        if (Math.Abs(x - HandleX(Handle.End)) <= GrabPixels) return Handle.End;
        return Handle.None;
    }

    /// <summary>Whether the loop bounds are currently worth showing.</summary>
    private bool ShowHandles => RangeStart >= 0 || RangeEnd >= 0 || _hoverX is not null;

    /// <summary>Give up on the range entirely — Alt+click, or the context menu.</summary>
    public void ResetRange()
    {
        SetCurrentValue(RangeStartProperty, -1);
        SetCurrentValue(RangeEndProperty, -1);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(LeadingInset + Extent * CellWidth, 22);

    public override void Render(DrawingContext context)
    {
        var h = Bounds.Height;
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        var rangeSet = RangeStart >= 0 || RangeEnd >= 0;
        var start = Math.Max(0, RangeStart);
        var end = RangeEnd < 0 ? MaxFrame : RangeEnd;
        if (end < start) end = start;
        var typeface = new Typeface(FontFamily.Default);

        for (var i = 0; i < Extent; i++)
        {
            var x = LeadingInset + i * CellWidth;
            if (i == CurrentFrame)
            {
                context.FillRectangle(PlayheadBrush, new Rect(x, 0, CellWidth - 2, h));
            }
            var brush = i == CurrentFrame ? Brushes.White
                : i > MaxFrame ? VirtualNumberBrush
                : rangeSet && (i < start || i > end) ? DimNumberBrush
                : NumberBrush;
            var text = new FormattedText(
                (i + 1).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, brush);
            context.DrawText(text, new Point(x + (CellWidth - 2 - text.Width) / 2, (h - text.Height) / 2));
            context.FillRectangle(TickBrush, new Rect(x - 1, h - 4, 1, 4));

            // Along the bottom edge, where the tick already lives, so the
            // numbers keep the middle of the cell. Two marks sit side by side
            // when a frame carries both.
            var camera = CameraKeyed?.Contains(i) == true;
            var pose = PoseKeyed?.Contains(i) == true;
            if (camera || pose)
            {
                var width = (camera ? MarkSize : 0) + (pose ? MarkSize : 0) + (camera && pose ? 1 : 0);
                var markX = x + (CellWidth - 2 - width) / 2;
                if (camera)
                {
                    context.FillRectangle(CameraKeyBrush, new Rect(markX, h - MarkSize, MarkSize, MarkSize));
                    markX += MarkSize + 1;
                }
                if (pose)
                {
                    context.FillRectangle(PoseKeyBrush, new Rect(markX, h - MarkSize, MarkSize, MarkSize));
                }
            }
        }

        DrawLoopHandles(context, h);

        // Colored frame tags: a flag strip along the top edge plus the label
        // (drawn over the frame number — markers are rare and deliberate).
        if (Markers is { Count: > 0 } markers)
        {
            foreach (var marker in markers)
            {
                if (marker.Frame < 0 || marker.Frame >= Extent) continue;
                var x = LeadingInset + marker.Frame * CellWidth;
                IBrush brush;
                try { brush = new SolidColorBrush(Color.Parse(marker.Color)); }
                catch { brush = new SolidColorBrush(Color.Parse("#e0a030")); }
                context.FillRectangle(brush, new Rect(x, 0, CellWidth - 2, 3));
                context.FillRectangle(brush, new Rect(x, 0, 3, h)); // little flagpole
                if (!string.IsNullOrWhiteSpace(marker.Label))
                {
                    var label = new FormattedText(
                        marker.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        typeface, 9, brush);
                    context.DrawText(label, new Point(x + 5, h - label.Height - 1));
                }
            }
        }
    }

    /// <summary>
    /// The two loop bounds, and the bar between them.
    /// </summary>
    /// <remarks>
    /// Drawn only while there is a range or the pointer is over the ruler.
    /// Two grips permanently parked on a bar most documents never use would be
    /// two more things in the way of reading the frame numbers, which is what
    /// the ruler is for.
    /// </remarks>
    private void DrawLoopHandles(DrawingContext context, double h)
    {
        if (!ShowHandles) return;
        var set = RangeStart >= 0 || RangeEnd >= 0;
        var startX = HandleX(Handle.Start);
        var endX = HandleX(Handle.End);

        // The bar between them: what the range *is*, rather than two marks you
        // have to hold in your head as a pair.
        context.FillRectangle(
            set ? LoopBarBrush : HandleGhostBrush,
            new Rect(startX, h - 3, Math.Max(0, endX - startX), 3));

        Grip(startX, set ? RangeStartBrush : HandleGhostBrush, leading: true);
        Grip(endX, set ? RangeEndBrush : HandleGhostBrush, leading: false);

        void Grip(double x, IBrush brush, bool leading)
        {
            // A post the full height so it reads against the numbers, and a
            // flag on the inward side so the two are told apart at a glance.
            context.FillRectangle(brush, new Rect(x - 1, 0, 2, h));
            context.FillRectangle(
                brush, new Rect(leading ? x : x - 6, h - HandleHeight, 6, HandleHeight));
        }
    }

    // ---- scrubbing -----------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        var x = point.Position.X;

        // Right-click never scrubs — it asks what to do with the range.
        if (point.Properties.IsRightButtonPressed)
        {
            RangeMenuRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // Alt is "forget it": on a handle, or anywhere between the two, which
        // is the region the range is actually about.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (HandleAt(x) != Handle.None || WithinRange(x)) ResetRange();
            e.Handled = true;
            return;
        }

        if (HandleAt(x) is var grabbed && grabbed != Handle.None)
        {
            _dragging = grabbed;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // SetCurrentValue rather than the setter, exactly as ScrubTo does for the
        // frame: a plain assignment on a two-way bound property overwrites the
        // binding instead of pushing through it, and the symptom is a scrub that
        // works once.
        SetCurrentValue(IsScrubbingProperty, true);
        e.Pointer.Capture(this);
        ScrubTo(x);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;
        // Tracked whether or not anything is being dragged: the handles are
        // shown on hover, so the ruler has to know where the pointer is.
        _hoverX = x;
        InvalidateVisual();

        if (_dragging != Handle.None)
        {
            DragHandleTo(x);
            e.Handled = true;
            return;
        }
        if (!IsScrubbing) return;
        ScrubTo(x);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverX = null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        SetCurrentValue(IsScrubbingProperty, false);
        _dragging = Handle.None;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // The case that would otherwise strand the canvas on the tile route: a
        // drag ended by losing capture never sees a release.
        SetCurrentValue(IsScrubbingProperty, false);
        _dragging = Handle.None;
    }

    /// <summary>Right-click on the ruler: the window offers a reset.</summary>
    public event EventHandler? RangeMenuRequested;

    private bool WithinRange(double x)
    {
        if (RangeStart < 0 && RangeEnd < 0) return false;
        return x >= HandleX(Handle.Start) && x <= HandleX(Handle.End);
    }

    /// <summary>
    /// Move the grabbed bound to the frame under the pointer.
    /// </summary>
    /// <remarks>
    /// Settled onto whole frames as it goes rather than on release, so what
    /// you see while dragging is what you will get. The two bounds cannot
    /// cross: pushing one past the other pins it a frame away, which is what
    /// every range control does and the only alternative to an inverted range
    /// nothing downstream can read.
    /// </remarks>
    private void DragHandleTo(double x)
    {
        var (start, end) = EffectiveRange();
        if (_dragging == Handle.Start)
        {
            var frame = Math.Clamp(FrameAt(x), 0, Math.Max(0, MaxFrame));
            SetCurrentValue(RangeStartProperty, Math.Min(frame, end));
            if (RangeEnd < 0) SetCurrentValue(RangeEndProperty, end);
        }
        else
        {
            // The end handle sits on the far edge of its frame, so the frame
            // it names is the one before the boundary the pointer is nearest.
            var frame = Math.Clamp(FrameAt(x - CellWidth / 2), 0, Math.Max(0, MaxFrame));
            SetCurrentValue(RangeEndProperty, Math.Max(frame, start));
            if (RangeStart < 0) SetCurrentValue(RangeStartProperty, start);
        }
    }

    private int FrameAt(double x) => (int)Math.Floor((x - LeadingInset) / Math.Max(1, CellWidth));

    private void ScrubTo(double x)
    {
        var frame = (int)Math.Floor((x - LeadingInset) / Math.Max(1, CellWidth));
        SetCurrentValue(CurrentFrameProperty, Math.Clamp(frame, 0, Math.Max(0, MaxFrame)));
    }
}
