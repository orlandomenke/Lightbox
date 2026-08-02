using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Services;

namespace Lightbox.App.ViewModels;

public enum ColorMode
{
    Wheel,
    Hsv,
    Hsl,
    Rgb,
    Cmyk,
}

/// <summary>
/// The color docker's state: one canonical color, viewable and editable
/// through several representations (Krita-style wheel, HSV, HSL, RGB, CMYK).
/// Every edit re-syncs all representations and commits a hex value to the
/// owner; hue is kept stable through grays/black so the wheel doesn't snap
/// to red when a channel hits zero.
/// </summary>
public sealed partial class ColorPickerViewModel : ObservableObject
{
    private bool _syncing;
    private double _r, _g, _b; // canonical color, 0..1

    /// <summary>Fired with "#rrggbb" whenever an edit changes the color.</summary>
    public event Action<string>? HexCommitted;

    public ColorPickerViewModel()
    {
        SetHex("#000000");
    }

    // ---- the document's palette, in every picker -----------------------------

    /// <summary>
    /// Where every picker gets the palette from.
    /// </summary>
    /// <remarks>
    /// Static because there is one active document and therefore one palette
    /// worth offering, and because the alternative — threading the palette
    /// through every flyout, template and control that hosts a picker — is a
    /// lot of plumbing to arrive at the same single answer.
    ///
    /// Set by <c>MainViewModel</c> whenever the document or its palettes
    /// change. Null until then, which is what makes a picker built in a test
    /// simply show no palette rather than reach for one.
    /// </remarks>
    public static Func<IReadOnlyList<Swatch>>? PaletteSource { get; set; }

    /// <summary>
    /// Raised with a swatch id when the artist picked from the palette rather
    /// than from the wheel.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="HexCommitted"/> because the two mean different
    /// things: a colour, versus a <i>reference</i> to a colour. Only the second
    /// can follow a later palette edit, and the owner has to know which it got
    /// or the live-palette link is silently dropped every time someone clicks
    /// a swatch in a flyout.
    /// </remarks>
    public event Action<string>? SwatchPicked;

    public IReadOnlyList<Swatch> PaletteSwatches => PaletteSource?.Invoke() ?? [];

    public bool HasPalette => PaletteSwatches.Count > 0;

    /// <summary>Re-read the palette — the document changed, or a swatch did.</summary>
    public void RefreshPalette()
    {
        OnPropertyChanged(nameof(PaletteSwatches));
        OnPropertyChanged(nameof(HasPalette));
    }

    [RelayCommand]
    private void PickSwatch(Swatch? swatch)
    {
        if (swatch is null) return;
        SetHex(swatch.Color);
        Commit();
        SwatchPicked?.Invoke(swatch.Id);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWheelMode))]
    [NotifyPropertyChangedFor(nameof(IsHsvMode))]
    [NotifyPropertyChangedFor(nameof(IsHslMode))]
    [NotifyPropertyChangedFor(nameof(IsRgbMode))]
    [NotifyPropertyChangedFor(nameof(IsCmykMode))]
    private ColorMode _mode = ColorMode.Wheel;

    public IReadOnlyList<ColorMode> ModeChoices { get; } =
        [ColorMode.Wheel, ColorMode.Hsv, ColorMode.Hsl, ColorMode.Rgb, ColorMode.Cmyk];

    public bool IsWheelMode => Mode == ColorMode.Wheel;
    public bool IsHsvMode => Mode == ColorMode.Hsv;
    public bool IsHslMode => Mode == ColorMode.Hsl;
    public bool IsRgbMode => Mode == ColorMode.Rgb;
    public bool IsCmykMode => Mode == ColorMode.Cmyk;

    // ---- channel properties (UI units) --------------------------------------

    /// <summary>Shared by HSV and HSL, degrees 0..360.</summary>
    [ObservableProperty]
    private double _hue;

    [ObservableProperty]
    private double _hsvSaturation; // 0..100

    [ObservableProperty]
    private double _hsvValue; // 0..100

    [ObservableProperty]
    private double _hslSaturation; // 0..100

    [ObservableProperty]
    private double _hslLightness; // 0..100

    [ObservableProperty]
    private double _red; // 0..255

    [ObservableProperty]
    private double _green;

    [ObservableProperty]
    private double _blue;

    [ObservableProperty]
    private double _cyan; // 0..100

    [ObservableProperty]
    private double _magenta;

    [ObservableProperty]
    private double _yellow;

    [ObservableProperty]
    private double _black;

    /// <summary>Wheel binding target — HsvColor keeps hue through black/gray.</summary>
    [ObservableProperty]
    private HsvColor _wheelHsv = new(1, 0, 0, 0.1);

    /// <summary>Preview swatch for the docker.</summary>
    public IBrush SwatchBrush =>
        new SolidColorBrush(Color.FromRgb((byte)Math.Round(_r * 255), (byte)Math.Round(_g * 255), (byte)Math.Round(_b * 255)));

    /// <summary>
    /// The colour as <c>#rrggbb</c>. Settable, so the flyout's hex box is an
    /// input as well as a readout — nonsense is ignored rather than rejected,
    /// because a half-typed <c>#c0</c> is a person mid-thought, not an error.
    /// </summary>
    public string Hex
    {
        get => ColorSpace.RgbToHex(_r, _g, _b);
        set
        {
            if (ColorSpace.HexToRgb(value ?? "") is null) return;
            SetHex(value!);
            Commit();
        }
    }

    /// <summary>Announce the current colour to whoever owns this picker.</summary>
    public void Commit() => HexCommitted?.Invoke(Hex);

    // ---- edits: recompute the canonical color from the edited model ---------

    partial void OnHueChanged(double value) => SyncFrom(Mode == ColorMode.Hsl ? ColorMode.Hsl : ColorMode.Hsv);

    partial void OnHsvSaturationChanged(double value) => SyncFrom(ColorMode.Hsv);

    partial void OnHsvValueChanged(double value) => SyncFrom(ColorMode.Hsv);

    partial void OnHslSaturationChanged(double value) => SyncFrom(ColorMode.Hsl);

    partial void OnHslLightnessChanged(double value) => SyncFrom(ColorMode.Hsl);

    partial void OnRedChanged(double value) => SyncFrom(ColorMode.Rgb);

    partial void OnGreenChanged(double value) => SyncFrom(ColorMode.Rgb);

    partial void OnBlueChanged(double value) => SyncFrom(ColorMode.Rgb);

    partial void OnCyanChanged(double value) => SyncFrom(ColorMode.Cmyk);

    partial void OnMagentaChanged(double value) => SyncFrom(ColorMode.Cmyk);

    partial void OnYellowChanged(double value) => SyncFrom(ColorMode.Cmyk);

    partial void OnBlackChanged(double value) => SyncFrom(ColorMode.Cmyk);

    partial void OnWheelHsvChanged(HsvColor value) => SyncFrom(ColorMode.Wheel);

    /// <summary>External color change (toolbar hex box, document load).</summary>
    public void SetHex(string hex)
    {
        if (_syncing || ColorSpace.HexToRgb(hex) is not { } rgb) return;
        if (ColorSpace.RgbToHex(rgb.R, rgb.G, rgb.B) == Hex && HasSynced) return;
        (_r, _g, _b) = rgb;
        HasSynced = true;
        PushToAllModels(source: null, commit: false);
    }

    private bool HasSynced { get; set; }

    private void SyncFrom(ColorMode source)
    {
        if (_syncing) return;
        (_r, _g, _b) = source switch
        {
            ColorMode.Wheel => ColorSpace.HsvToRgb(WheelHsv.H, WheelHsv.S, WheelHsv.V),
            ColorMode.Hsv => ColorSpace.HsvToRgb(Hue, HsvSaturation / 100, HsvValue / 100),
            ColorMode.Hsl => ColorSpace.HslToRgb(Hue, HslSaturation / 100, HslLightness / 100),
            ColorMode.Rgb => (Math.Clamp(Red, 0, 255) / 255, Math.Clamp(Green, 0, 255) / 255, Math.Clamp(Blue, 0, 255) / 255),
            _ => ColorSpace.CmykToRgb(Cyan / 100, Magenta / 100, Yellow / 100, Black / 100),
        };
        PushToAllModels(source, commit: true);
    }

    /// <summary>
    /// Update every representation from the canonical color — EXCEPT the one
    /// the user is editing. Writing recomputed values back into the source
    /// control mid-drag makes its thumb fight the pointer (the wheel-snapping
    /// bug), so the source keeps exactly what the user set. Hue-carrying
    /// models keep the previous hue when the color is achromatic.
    /// </summary>
    private void PushToAllModels(ColorMode? source, bool commit)
    {
        _syncing = true;

        var (hv, sv, v) = ColorSpace.RgbToHsv(_r, _g, _b);
        var (hl, sl, l) = ColorSpace.RgbToHsl(_r, _g, _b);
        var (c, m, y, k) = ColorSpace.RgbToCmyk(_r, _g, _b);
        var achromatic = sv <= 0.0001;
        var hue = source == ColorMode.Wheel
            ? WheelHsv.H // the wheel is hue-authoritative while dragging it
            : achromatic ? Hue : hv;

        Hue = hue;
        if (source != ColorMode.Hsv)
        {
            HsvSaturation = sv * 100;
            HsvValue = v * 100;
        }
        if (source != ColorMode.Hsl)
        {
            HslSaturation = sl * 100;
            HslLightness = l * 100;
        }
        if (source != ColorMode.Rgb)
        {
            Red = _r * 255;
            Green = _g * 255;
            Blue = _b * 255;
        }
        if (source != ColorMode.Cmyk)
        {
            Cyan = c * 100;
            Magenta = m * 100;
            Yellow = y * 100;
            Black = k * 100;
        }
        if (source != ColorMode.Wheel)
        {
            WheelHsv = new HsvColor(1, hue, sv, v);
        }
        OnPropertyChanged(nameof(SwatchBrush));
        OnPropertyChanged(nameof(Hex));

        _syncing = false;
        if (commit) HexCommitted?.Invoke(Hex);
    }
}
