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

    /// <summary>Playback range start (-1 = unset).</summary>
    public static readonly StyledProperty<int> RangeStartProperty =
        AvaloniaProperty.Register<TimelineRuler, int>(nameof(RangeStart), -1);

    /// <summary>Playback range end (-1 = unset).</summary>
    public static readonly StyledProperty<int> RangeEndProperty =
        AvaloniaProperty.Register<TimelineRuler, int>(nameof(RangeEnd), -1);

    static TimelineRuler()
    {
        AffectsMeasure<TimelineRuler>(ExtentProperty, CellWidthProperty, LeadingInsetProperty);
        AffectsRender<TimelineRuler>(MaxFrameProperty, CurrentFrameProperty, RangeStartProperty, RangeEndProperty);
    }

    public int Extent { get => GetValue(ExtentProperty); set => SetValue(ExtentProperty, value); }

    public int MaxFrame { get => GetValue(MaxFrameProperty); set => SetValue(MaxFrameProperty, value); }

    public double CellWidth { get => GetValue(CellWidthProperty); set => SetValue(CellWidthProperty, value); }

    public double LeadingInset { get => GetValue(LeadingInsetProperty); set => SetValue(LeadingInsetProperty, value); }

    public int CurrentFrame { get => GetValue(CurrentFrameProperty); set => SetValue(CurrentFrameProperty, value); }

    public int RangeStart { get => GetValue(RangeStartProperty); set => SetValue(RangeStartProperty, value); }

    public int RangeEnd { get => GetValue(RangeEndProperty); set => SetValue(RangeEndProperty, value); }

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#181818"));
    private static readonly IBrush PlayheadBrush = new SolidColorBrush(Color.Parse("#4a6ea9"));
    private static readonly IBrush NumberBrush = new SolidColorBrush(Color.Parse("#c8c8c8"));
    private static readonly IBrush DimNumberBrush = new SolidColorBrush(Color.Parse("#5a5a5a"));
    private static readonly IBrush VirtualNumberBrush = new SolidColorBrush(Color.Parse("#454545"));
    private static readonly IBrush TickBrush = new SolidColorBrush(Color.Parse("#3a3a3a"));
    private static readonly IBrush RangeStartBrush = new SolidColorBrush(Color.Parse("#4caf50"));
    private static readonly IBrush RangeEndBrush = new SolidColorBrush(Color.Parse("#e05555"));

    private bool _scrubbing;

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
        }

        if (RangeStart >= 0)
            context.FillRectangle(RangeStartBrush, new Rect(LeadingInset + RangeStart * CellWidth - 1, 0, 2, h));
        if (RangeEnd >= 0)
            context.FillRectangle(RangeEndBrush, new Rect(LeadingInset + (RangeEnd + 1) * CellWidth - 3, 0, 2, h));
    }

    // ---- scrubbing -----------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _scrubbing = true;
        e.Pointer.Capture(this);
        ScrubTo(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_scrubbing) return;
        ScrubTo(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _scrubbing = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _scrubbing = false;
    }

    private void ScrubTo(double x)
    {
        var frame = (int)Math.Floor((x - LeadingInset) / Math.Max(1, CellWidth));
        SetCurrentValue(CurrentFrameProperty, Math.Clamp(frame, 0, Math.Max(0, MaxFrame)));
    }
}
