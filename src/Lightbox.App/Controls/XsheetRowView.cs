using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Controls;

/// <summary>
/// One row of the X-sheet — every cel of one layer — drawn by one control
/// (Q282).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why drawn.</b> The row used to be a button per cel, each with a picture
/// and a context menu of its own, in a list that built every cel of the scene
/// whether or not it was scrolled into view. This control paints the cels the
/// docker is showing and holds one menu, which it aims at the cel it opens on:
/// the menu's data context is that cel, so every item reads it exactly as it
/// read the cel's button before.
/// </para>
/// <para>
/// <b>What a cel shows</b> is <see cref="CellStyle"/>: the drawing's number, a
/// dot, or a picture. The ground — current, picked, breakdown, inbetween, past
/// the end of the scene, outside the playback range — is the same in all three
/// and uses the cel brushes the buttons used.
/// </para>
/// <para>
/// <b>Gestures stay where they were.</b> Shift and Ctrl picks, the double click,
/// the block drag and carrying a cel are the window's tunnel handlers, and they
/// find the cel under the pointer through <see cref="CellAt"/>. This control
/// does only what the button did on its own: a press and a release on the same
/// cel is a click, which runs <see cref="Command"/> with the cel.
/// </para>
/// </remarks>
public sealed class XsheetRowView : Control, Avalonia.Rendering.ICustomHitTest
{
    public static readonly StyledProperty<IReadOnlyList<FrameCell>?> CellsProperty =
        AvaloniaProperty.Register<XsheetRowView, IReadOnlyList<FrameCell>?>(nameof(Cells));

    public static readonly StyledProperty<double> FrameWidthProperty =
        AvaloniaProperty.Register<XsheetRowView, double>(nameof(FrameWidth), 26);

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<XsheetRowView, double>(nameof(Gap), 2);

    public static readonly StyledProperty<XsheetCellStyle> CellStyleProperty =
        AvaloniaProperty.Register<XsheetRowView, XsheetCellStyle>(nameof(CellStyle));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<XsheetRowView, ICommand?>(nameof(Command));

    static XsheetRowView()
    {
        AffectsMeasure<XsheetRowView>(CellsProperty, FrameWidthProperty, GapProperty);
        AffectsRender<XsheetRowView>(CellStyleProperty);
        FocusableProperty.OverrideDefaultValue<XsheetRowView>(true);
    }

    public XsheetRowView()
    {
        ClipToBounds = true;
        // Never ask to be brought into view as a whole. The row is the whole
        // scene wide, and the focus a press gives it asked the sheet to show all
        // of it — which slid the sheet sideways between the press and its
        // release, so the click landed on the next cel. A cel being clicked is on
        // screen already.
        AddHandler(RequestBringIntoViewEvent, (_, e) =>
        {
            if (ReferenceEquals(e.TargetObject, this)) e.Handled = true;
        });
        // Tunnel, so the menu is aimed before it opens on this control's own
        // bubble of the same event.
        AddHandler(ContextRequestedEvent, AimMenu, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        EffectiveViewportChanged += (_, e) =>
        {
            _viewport = e.EffectiveViewport;
            _hasViewport = true;
            InvalidateVisual();
        };
        ThemeColour.RepaintOnThemeChange(this, ForgetTheme);
    }

    /// <summary>The row's cels, in frame order.</summary>
    public IReadOnlyList<FrameCell>? Cells
    {
        get => GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    /// <summary>How wide a cel is.</summary>
    public double FrameWidth
    {
        get => GetValue(FrameWidthProperty);
        set => SetValue(FrameWidthProperty, value);
    }

    /// <summary>The space after a cel, kept so the ruler's pitch still lines up.</summary>
    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>What a cel shows of its drawing.</summary>
    public XsheetCellStyle CellStyle
    {
        get => GetValue(CellStyleProperty);
        set => SetValue(CellStyleProperty, value);
    }

    /// <summary>Run with the cel on a click — a press and a release on the same cel.</summary>
    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>The cel last clicked or aimed at here — what Ctrl+C takes while the row has focus.</summary>
    public FrameCell? FocusedCell { get; private set; }

    /// <summary>How many cels the last render painted; the cost guard reads it.</summary>
    internal int LastDrawnCells { get; private set; }

    private double Pitch => FrameWidth + Gap;

    /// <summary>Where a cel is, in this control's coordinates.</summary>
    public Rect CellBounds(int index) =>
        new(index * Pitch, 0, FrameWidth, Bounds.Height > 0 ? Bounds.Height : Height);

    /// <summary>
    /// The cel under a point, or null off the row. A point in the gap after a cel
    /// belongs to that cel, so a drag along the row never falls between two.
    /// </summary>
    public FrameCell? CellAt(Point p)
    {
        if (Cells is not { Count: > 0 } cells || p.X < 0 || p.Y < 0 || p.Y >= Bounds.Height) return null;
        var index = (int)(p.X / Pitch);
        return index < cells.Count ? cells[index] : null;
    }

    /// <summary>
    /// The whole row takes the pointer, gaps and unpainted cels included. Drawn
    /// content alone is not enough: a cel scrolled in before its first paint, or
    /// the gap between two, would let a press fall through to the row behind.
    /// </summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    // ---- keeping up with the cels ----------------------------------------------------

    private INotifyCollectionChanged? _watchedList;
    private readonly List<FrameCell> _watched = [];
    private CelGlyph[]? _plan;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CellsProperty && VisualRoot is not null) Watch();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unwatch();
    }

    /// <summary>
    /// Repaint when a cel changes. Cels are long-lived and change in place
    /// (current, picked, keyed), so the row listens to each — and lets go of them
    /// when it leaves the tree, or a closed sheet would stay reachable from its
    /// cels.
    /// </summary>
    private void Watch()
    {
        Unwatch();
        if (Cells is INotifyCollectionChanged list)
        {
            _watchedList = list;
            list.CollectionChanged += OnCellsChanged;
        }
        foreach (var cell in Cells ?? [])
        {
            cell.PropertyChanged += OnCellChanged;
            _watched.Add(cell);
        }
        // A cel no longer in the row is not this row's to act on: rows are
        // reused across layers, and a stale one would let Ctrl+C or a menu from
        // the keyboard reach a cel the artist cannot see as focused.
        if (FocusedCell is { } focused && Cells?.Contains(focused) != true) FocusedCell = null;
        _plan = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void Unwatch()
    {
        if (_watchedList is { } list) list.CollectionChanged -= OnCellsChanged;
        _watchedList = null;
        foreach (var cell in _watched) cell.PropertyChanged -= OnCellChanged;
        _watched.Clear();
    }

    /// <summary>
    /// Follow the list one cel at a time: the sheet grows and shrinks by adding
    /// and removing cels, and re-listening to every cel on each one made
    /// extending a long scene cost the square of its length.
    /// </summary>
    private void OnCellsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Move)
        {
            Watch();
            return;
        }
        foreach (FrameCell gone in e.OldItems ?? Array.Empty<FrameCell>())
        {
            gone.PropertyChanged -= OnCellChanged;
            _watched.Remove(gone);
            if (ReferenceEquals(gone, FocusedCell)) FocusedCell = null;
        }
        foreach (FrameCell added in e.NewItems ?? Array.Empty<FrameCell>())
        {
            added.PropertyChanged += OnCellChanged;
            _watched.Add(added);
        }
        _plan = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnCellChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The plan reads only what is keyed, blank and inside the scene; the rest
        // is ground, read straight off the cel when it is painted.
        if (e.PropertyName is nameof(FrameCell.IsKeyed) or nameof(FrameCell.IsBlank) or nameof(FrameCell.IsVirtual))
            _plan = null;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Cells?.Count ?? 0;
        var width = count == 0 ? 0 : count * FrameWidth + (count - 1) * Gap;
        return new Size(width, double.IsNaN(Height) ? 26 : Height);
    }

    // ---- input -------------------------------------------------------------------------

    private FrameCell? _pressed;
    private int _hover = -1;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Shift and Ctrl picks and the double click are taken by the window's
        // tunnel and arrive handled; a press they took is not a click.
        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (CellAt(e.GetPosition(this)) is not { } cell) return;
        _pressed = cell;
        FocusedCell = cell;
        Focus();
        // Held, as a button holds its press: the moves keep arriving here, which
        // is what lets the window turn this press into a drag.
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed is not { } pressed || e.InitialPressMouseButton != MouseButton.Left) return;
        _pressed = null;
        if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
        if (e.Handled || !ReferenceEquals(CellAt(e.GetPosition(this)), pressed)) return;
        if (Command is { } command && command.CanExecute(pressed)) command.Execute(pressed);
        e.Handled = true;
    }

    /// <summary>
    /// A press whose capture was taken away — a block drag letting go so the row
    /// under the pointer can hear it — is no longer a click.
    /// </summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pressed = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var over = CellAt(e.GetPosition(this))?.Index ?? -1;
        if (over == _hover) return;
        _hover = over;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover < 0) return;
        _hover = -1;
        InvalidateVisual();
    }

    /// <summary>
    /// Reached from the keyboard with nothing clicked yet, the row holds the
    /// playhead's cel, so Enter, the menu key and Ctrl+C have a cel to act on —
    /// what a tab onto a cel's button gave them before.
    /// </summary>
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        FocusedCell ??= Cells?.FirstOrDefault(c => c.IsCurrent);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Enter or Key.Space)) return;
        if (FocusedCell is { } cell && Command is { } command && command.CanExecute(cell))
        {
            command.Execute(cell);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Aim the row's one menu at the cel it is opening on: the one under the
    /// pointer, or the focused one when asked for from the keyboard. Off every
    /// cel there is nothing for it to act on, so it does not open.
    /// </summary>
    private void AimMenu(object? sender, ContextRequestedEventArgs e)
    {
        var cell = e.TryGetPosition(this, out var at) ? CellAt(at) : FocusedCell;
        if (cell is null)
        {
            e.Handled = true;
            return;
        }
        FocusedCell = cell;
        if (ContextMenu is { } menu) menu.DataContext = cell;
    }

    // ---- painting ----------------------------------------------------------------------

    /// <summary>The theme's brushes, looked up once per theme rather than per cel.</summary>
    private sealed record Palette(
        IBrush? Cel, IBrush? Breakdown, IBrush? Tween, IBrush? Current, IBrush? Selected, IBrush? Hatch,
        IBrush Ink, IBrush Faint, Pen Hold, IBrush Hover)
    {
        public Pen Cross { get; } = new(Faint, 1.25);
        public Pen Ring { get; } = new(Ink, 1);
        public Pen DotRing { get; } = new(Ink, 1.75);
    }

    private Rect _viewport;
    private bool _hasViewport;
    private Palette? _palette;
    private readonly Dictionary<(int Number, bool Strong, bool Faint), FormattedText> _numbers = [];

    private void ForgetTheme()
    {
        _palette = null;
        _numbers.Clear();
    }

    private IBrush? Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) ? found as IBrush : null;

    private Palette Colours() => _palette ??= new Palette(
        Resource("CelBrush"),
        Resource("CelBreakdownBrush"),
        Resource("CelTweenBrush"),
        Resource("CelCurrentBrush"),
        Resource("CelSelectedBrush"),
        Resource("CelVirtualHatchBrush"),
        Resource("TextPrimaryBrush") ?? new SolidColorBrush(ThemeColour.Hairline(this, 0xFF)),
        Resource("TextSecondaryBrush") ?? new SolidColorBrush(ThemeColour.Hairline(this, 0xA0)),
        new Pen(new SolidColorBrush(ThemeColour.Hairline(this, 0x70)), 1.5),
        // The app's hover: lighter on every theme. The text colour at a low
        // alpha darkened a cel on Studio grey.
        Resource("HoverTintBrush") ?? new SolidColorBrush(ThemeColour.Hairline(this, 0x18)));

    /// <summary>A drawing's number, laid out once and kept: a long sheet reuses the same few hundred.</summary>
    private FormattedText Number(int number, bool strong, bool faint, Palette colours)
    {
        if (_numbers.Count > 4096) _numbers.Clear();
        if (!_numbers.TryGetValue((number, strong, faint), out var text))
        {
            var size = this.TryFindResource("TypeDense", out var s) && s is double d ? d : 11;
            text = new FormattedText(
                number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(GetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty), FontStyle.Normal, strong ? FontWeight.Bold : FontWeight.Normal),
                size, faint ? colours.Faint : colours.Ink);
            _numbers[(number, strong, faint)] = text;
        }
        return text;
    }

    public override void Render(DrawingContext context)
    {
        LastDrawnCells = 0;
        if (Cells is not { Count: > 0 } cells) return;
        var plan = _plan ??= XsheetCellPlan.Of(cells);
        var colours = Colours();

        // Only what is showing: nothing at all for a row scrolled out of the
        // sheet, which still repaints when its cels change. Before the first
        // viewport arrives, everything — a row never laid out in a scroller has
        // nothing to cull by.
        var first = 0;
        var last = cells.Count - 1;
        if (_hasViewport)
        {
            // The viewport is the scroller's, in this row's coordinates — not cut
            // to the row — so a row scrolled away has one lying wholly above or
            // below it rather than an empty one.
            var seen = _viewport.Intersect(new Rect(Bounds.Size));
            if (!(seen.Width > 0 && seen.Height > 0)) return;
            first = Math.Max(0, (int)Math.Floor(_viewport.Left / Pitch));
            last = Math.Min(cells.Count - 1, (int)Math.Ceiling(_viewport.Right / Pitch));
        }

        for (var i = first; i <= last; i++)
        {
            var cell = cells[i];
            var box = CellBounds(i);
            // The opacities the button styles gave a cel past the end and one
            // outside the playback range; pushed only when there is one.
            // .dim came after .virtualCell, so out-of-range won when both held.
            var opacity = cell.OutOfRange ? 0.45 : cell.IsVirtual ? 0.35 : 1;
            if (opacity < 1)
            {
                using (context.PushOpacity(opacity)) DrawCel(context, cell, plan[i], box, colours);
            }
            else
            {
                DrawCel(context, cell, plan[i], box, colours);
            }
            if (i == _hover) context.FillRectangle(colours.Hover, box);
            if (ReferenceEquals(cell, FocusedCell) && Classes.Contains(":focus-visible"))
                context.DrawRectangle(null, colours.Ring, box.Deflate(0.5));
            LastDrawnCells++;
        }
    }

    private void DrawCel(DrawingContext context, FrameCell cell, CelGlyph glyph, Rect box, Palette colours)
    {
        if (Ground(cell, colours) is { } ground) context.FillRectangle(ground, box);
        switch (CellStyle)
        {
            case XsheetCellStyle.Numbers: DrawNumber(context, cell, glyph, box, colours); break;
            case XsheetCellStyle.Dots: DrawDot(context, cell, glyph, box, colours); break;
            default: DrawPicture(context, cell, box); break;
        }
    }

    /// <summary>The ground a cel is painted on, for the tests that read it.</summary>
    internal IBrush? GroundFor(FrameCell cell) => Ground(cell, Colours());

    /// <summary>
    /// The cel's ground, in the order the button styles applied theirs: the last
    /// one wins. The breakdown and inbetween tints only under pictures: numbers
    /// and dots already say the kind of drawing with the glyph, and saying it
    /// twice was noise across a long row.
    /// </summary>
    private IBrush? Ground(FrameCell cell, Palette colours) =>
        cell.IsSelected ? colours.Selected
        : cell.IsCurrent ? colours.Current
        : cell.IsVirtual ? colours.Hatch
        : CellStyle != XsheetCellStyle.Pictures ? colours.Cel
        : cell.IsBreakdown ? colours.Breakdown
        : cell.IsInbetween ? colours.Tween
        : colours.Cel;

    /// <summary>
    /// The paper sheet: the number where the drawing lands — bold on a key,
    /// ringed on a breakdown, quiet on an inbetween — × on a blank drawing, and a
    /// line along the cels it is held across.
    /// </summary>
    private void DrawNumber(DrawingContext context, FrameCell cell, CelGlyph glyph, Rect box, Palette colours)
    {
        if (glyph.Number == 0) return;
        var mid = box.Center.Y;
        var endsHere = glyph.HoldEnd == cell.Index;
        if (!glyph.Starts)
        {
            // Through the gap before the cel too, so a hold reads as one line.
            context.DrawLine(colours.Hold, new Point(box.Left - Gap, mid), new Point(box.Right - (endsHere ? 3 : 0), mid));
            return;
        }
        if (glyph.Blank)
        {
            var r = Math.Min(box.Width, box.Height) * 0.16;
            context.DrawLine(colours.Cross, new Point(box.Center.X - r, mid - r), new Point(box.Center.X + r, mid + r));
            context.DrawLine(colours.Cross, new Point(box.Center.X - r, mid + r), new Point(box.Center.X + r, mid - r));
        }
        else
        {
            var text = Number(glyph.Number, strong: !cell.IsBreakdown && !cell.IsInbetween, faint: cell.IsInbetween, colours);
            context.DrawText(text, new Point(box.Center.X - text.Width / 2, mid - text.Height / 2));
            if (cell.IsBreakdown)
            {
                var r = Math.Max(text.Width, text.Height) / 2 + 1;
                context.DrawEllipse(null, colours.Ring, box.Center, r, r);
            }
        }
        if (!endsHere) context.DrawLine(colours.Hold, new Point(box.Right - 2, mid), new Point(box.Right, mid));
    }

    /// <summary>The Timeline's language: a mark where the drawing lands, on a bar along its hold.</summary>
    private void DrawDot(DrawingContext context, FrameCell cell, CelGlyph glyph, Rect box, Palette colours)
    {
        if (glyph.Number == 0) return;
        var mid = box.Center.Y;
        var from = glyph.Starts ? box.Center.X : box.Left - Gap;
        var to = glyph.HoldEnd == cell.Index ? box.Right - 3 : box.Right;
        if (to > from && colours.Hold.Brush is { } bar) context.FillRectangle(bar, new Rect(from, mid - 2, to - from, 4));
        if (!glyph.Starts) return;

        var rad = Math.Max(3, Math.Min(box.Width, box.Height) * 0.2);
        if (glyph.Blank)
            context.DrawRectangle(colours.Cel, colours.Ring, new Rect(box.Center.X - rad, mid - rad, rad * 2, rad * 2), 1.5, 1.5);
        else if (cell.IsBreakdown)
            context.DrawEllipse(null, colours.DotRing, box.Center, rad - 0.5, rad - 0.5);
        else if (cell.IsInbetween)
            context.DrawEllipse(colours.Ink, null, box.Center, rad * 0.55, rad * 0.55);
        else
            context.DrawEllipse(colours.Ink, null, box.Center, rad, rad);
    }

    /// <summary>The cel's picture, where it has one: the sheet as it was before Q282.</summary>
    private static void DrawPicture(DrawingContext context, FrameCell cell, Rect box)
    {
        if (!cell.IsKeyed || cell.Thumb is not { } thumb) return;
        var room = box.Deflate(new Thickness(4, Math.Max(0, (box.Height - 16) / 2)));
        var size = thumb.Size;
        if (size.Width <= 0 || size.Height <= 0 || room.Width <= 0 || room.Height <= 0) return;
        var scale = Math.Min(room.Width / size.Width, room.Height / size.Height);
        var w = size.Width * scale;
        var h = size.Height * scale;
        context.DrawImage(thumb, new Rect(room.Center.X - w / 2, room.Center.Y - h / 2, w, h));
    }
}
