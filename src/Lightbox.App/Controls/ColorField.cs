using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Controls;

/// <summary>
/// A colour, anywhere that is not the Color panel: a swatch you click to get
/// the same ring-and-square wheel and the same readouts.
/// </summary>
/// <remarks>
/// Every other place a colour was chosen — a palette swatch, a gradient stop,
/// the brush's secondary colour — offered a hex box and nothing else. Typing
/// <c>#c04a2f</c> is not choosing a colour, it is transcribing one you already
/// found somewhere else, and the somewhere else was the Color panel. So the
/// wheel comes to the field instead.
///
/// Hex is still there, at the bottom of the flyout, under the wheel and the
/// slider — the same order the Color panel uses, so the two do not have to be
/// learned separately. It is the readout of a choice rather than the way to
/// make one, which is the right rank for it.
///
/// Each field owns a <see cref="ColorPickerViewModel"/> of its own. Sharing the
/// application's would mean opening a gradient stop moved the brush colour,
/// which is exactly the sort of action-at-a-distance a swatch must not have.
/// </remarks>
public class ColorField : TemplatedControl
{
    public static readonly StyledProperty<string?> HexProperty =
        AvaloniaProperty.Register<ColorField, string?>(
            nameof(Hex), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Shown next to the swatch. Empty for a bare swatch.</summary>
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<ColorField, string?>(nameof(Label));

    /// <summary>
    /// What an empty value means, when it is allowed to be empty — the brush's
    /// secondary colour is "none" until you set one.
    /// </summary>
    public static readonly StyledProperty<bool> AllowsNoneProperty =
        AvaloniaProperty.Register<ColorField, bool>(nameof(AllowsNone));

    public static readonly StyledProperty<IBrush?> SwatchProperty =
        AvaloniaProperty.Register<ColorField, IBrush?>(nameof(Swatch));

    /// <summary>
    /// Direct rather than styled, because the flyout binds to it from the
    /// template and a plain CLR property is not something a TemplateBinding
    /// can see.
    /// </summary>
    public static readonly DirectProperty<ColorField, ColorPickerViewModel> PickerProperty =
        AvaloniaProperty.RegisterDirect<ColorField, ColorPickerViewModel>(nameof(Picker), o => o.Picker);

    public string? Hex
    {
        get => GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool AllowsNone
    {
        get => GetValue(AllowsNoneProperty);
        set => SetValue(AllowsNoneProperty, value);
    }

    /// <summary>What the swatch paints. Read by the template.</summary>
    public IBrush? Swatch
    {
        get => GetValue(SwatchProperty);
        private set => SetValue(SwatchProperty, value);
    }

    /// <summary>The flyout's state. One per field — see the remarks.</summary>
    public ColorPickerViewModel Picker { get; }

    private bool _syncing;

    public ColorField()
    {
        Picker = new ColorPickerViewModel();
        // The flyout edits the picker; the picker writes back here. Guarded
        // because the write comes straight back round as a Hex change.
        Picker.HexCommitted += hex =>
        {
            if (_syncing) return;
            _syncing = true;
            Hex = hex;
            Swatch = BrushFor(hex);
            _syncing = false;
        };
        Swatch = BrushFor(null);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != HexProperty) return;
        var hex = change.GetNewValue<string?>();
        Swatch = BrushFor(hex);
        if (_syncing) return;
        _syncing = true;
        if (!string.IsNullOrWhiteSpace(hex)) Picker.SetHex(hex);
        _syncing = false;
    }

    /// <summary>
    /// A checkerboard for "no colour", so an unset field does not read as a
    /// black one — the difference matters for the secondary colour, where
    /// black is a real answer and none is a different real answer.
    /// </summary>
    private static IBrush BrushFor(string? hex)
    {
        if (Services.ColorSpace.HexToRgb(hex ?? "") is not { } rgb) return NoneBrush;
        return new SolidColorBrush(Color.FromRgb(
            (byte)Math.Round(Math.Clamp(rgb.R, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(rgb.G, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(rgb.B, 0, 1) * 255)));
    }

    private static readonly IBrush NoneBrush = BuildNoneBrush();

    private static IBrush BuildNoneBrush()
    {
        var checker = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            SourceRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing
                    {
                        Brush = new SolidColorBrush(Color.Parse("#3a3a3a")),
                        Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)),
                    },
                    new GeometryDrawing
                    {
                        Brush = new SolidColorBrush(Color.Parse("#2a2a2a")),
                        Geometry = new RectangleGeometry(new Rect(0, 0, 4, 4)),
                    },
                    new GeometryDrawing
                    {
                        Brush = new SolidColorBrush(Color.Parse("#2a2a2a")),
                        Geometry = new RectangleGeometry(new Rect(4, 4, 4, 4)),
                    },
                },
            },
        };
        return checker;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Clear") is { } clear) clear.Click += (_, _) => Clear();
    }

    /// <summary>Called by the template's Clear button when the field allows none.</summary>
    public void Clear() => Hex = null;
}
