using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using DocStop = Lightbox.Core.Documents.GradientStop;

namespace Lightbox.App.ViewModels;

/// <summary>
/// One stop in the gradient editor. Wraps the document's
/// <see cref="GradientStop"/> for the same reason <see cref="SwatchRow"/> does:
/// the object being edited and the object being rendered have to be one thing.
/// </summary>
public sealed partial class GradientStopRow : ObservableObject
{
    private readonly Action _edited;

    public GradientStopRow(DocStop model, Action edited)
    {
        Model = model;
        _edited = edited;
    }

    public DocStop Model { get; }

    /// <summary>0–100 rather than 0–1, so the slider and the spinner read naturally.</summary>
    public double Position
    {
        get => Model.Position * 100;
        set
        {
            var p = Math.Clamp(value, 0, 100) / 100.0;
            if (Math.Abs(p - Model.Position) < 1e-9) return;
            Model.Position = p;
            OnPropertyChanged();
            _edited();
        }
    }

    public string Color
    {
        get => Model.Color;
        set
        {
            var hex = HexColor.Normalize(value);
            if (hex is null || hex == Model.Color) return;
            Model.Color = hex;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Fill));
            _edited();
        }
    }

    /// <summary>0–100 opacity.</summary>
    public double Alpha
    {
        get => Model.Alpha * 100;
        set
        {
            var a = Math.Clamp(value, 0, 100) / 100.0;
            if (Math.Abs(a - Model.Alpha) < 1e-9) return;
            Model.Alpha = a;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Fill));
            _edited();
        }
    }

    public IBrush Fill
    {
        get
        {
            var (r, g, b) = GimpPalette.Rgb(Model.Color);
            return new SolidColorBrush(
                Avalonia.Media.Color.FromArgb((byte)Math.Round(Math.Clamp(Model.Alpha, 0, 1) * 255), r, g, b));
        }
    }
}

/// <summary>
/// The gradient docker: the document's gradients, the stops in the selected
/// one, and a preview of the ramp. Editing a stop is live — the same
/// arrangement as a palette swatch, and for the same reason: a gradient
/// already painted into the record resolves its definition by id, so changing
/// the definition changes the art.
/// </summary>
public sealed partial class GradientDockerViewModel : ObservableObject
{
    private readonly Action<string> _gradientEdited;
    private readonly Action<Action<Doc>> _edit;

    private Doc? _doc;
    private bool _loading;

    public GradientDockerViewModel(Action<string> gradientEdited, Action<Action<Doc>> edit)
    {
        _gradientEdited = gradientEdited;
        _edit = edit;
    }

    public ObservableCollection<Gradient> Gradients { get; } = [];

    public ObservableCollection<GradientStopRow> Stops { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGradient))]
    [NotifyPropertyChangedFor(nameof(Kind))]
    [NotifyPropertyChangedFor(nameof(Spread))]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private Gradient? _selectedGradient;

    public bool HasGradient => SelectedGradient is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStop))]
    private GradientStopRow? _selectedStop;

    public bool HasStop => SelectedStop is not null;

    public IReadOnlyList<GradientKind> KindChoices { get; } = [GradientKind.Linear, GradientKind.Radial];

    public IReadOnlyList<GradientSpread> SpreadChoices { get; } =
        [GradientSpread.Pad, GradientSpread.Repeat, GradientSpread.Mirror];

    public GradientKind Kind
    {
        get => SelectedGradient?.Kind ?? GradientKind.Linear;
        set
        {
            if (SelectedGradient is not { } g || g.Kind == value) return;
            g.Kind = value;
            OnPropertyChanged();
            NotifyEdited();
        }
    }

    public GradientSpread Spread
    {
        get => SelectedGradient?.Spread ?? GradientSpread.Pad;
        set
        {
            if (SelectedGradient is not { } g || g.Spread == value) return;
            g.Spread = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Preview));
            NotifyEdited();
        }
    }

    /// <summary>
    /// The ramp as an Avalonia brush for the preview strip. Built from
    /// <see cref="GradientOps.Sample"/> rather than from the stops directly,
    /// so the strip shows the linear-light interpolation the canvas will
    /// actually paint instead of Avalonia's own sRGB one.
    /// </summary>
    public IBrush Preview
    {
        get
        {
            var gradient = SelectedGradient;
            if (gradient is null) return Brushes.Transparent;
            const int Steps = 32;
            var stops = new GradientStops();
            for (var i = 0; i <= Steps; i++)
            {
                var t = i / (double)Steps;
                var (r, g, b, a) = GradientOps.Sample(gradient, t);
                stops.Add(new Avalonia.Media.GradientStop(Avalonia.Media.Color.FromArgb(a, r, g, b), t));
            }
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = stops,
            };
        }
    }

    partial void OnSelectedGradientChanged(Gradient? value) => RebuildStops();

    public void Load(Doc doc)
    {
        _doc = doc;
        var keepGradient = SelectedGradient?.Id;

        _loading = true;
        try
        {
            Gradients.Clear();
            foreach (var gradient in doc.Gradients.Values) Gradients.Add(gradient);
            SelectedGradient = Gradients.FirstOrDefault(g => g.Id == keepGradient) ?? Gradients.FirstOrDefault();
            RebuildStops();
        }
        finally
        {
            _loading = false;
        }
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Spread));
        OnPropertyChanged(nameof(Preview));
    }

    private void RebuildStops()
    {
        Stops.Clear();
        foreach (var stop in SelectedGradient?.Stops.OrderBy(s => s.Position).ToList() ?? [])
        {
            Stops.Add(new GradientStopRow(stop, NotifyEdited));
        }
        SelectedStop = Stops.FirstOrDefault();
    }

    private void NotifyEdited()
    {
        OnPropertyChanged(nameof(Preview));
        if (_loading || SelectedGradient is not { } gradient) return;
        _gradientEdited(gradient.Id);
    }

    // ---- commands -----------------------------------------------------------

    [RelayCommand]
    private void AddGradient()
    {
        if (_doc is null) return;
        var gradient = new Gradient { Name = $"Gradient {_doc.Gradients.Count + 1}" };
        _edit(d => d.Gradients[gradient.Id] = gradient);
        SelectedGradient = Gradients.FirstOrDefault(g => g.Id == gradient.Id);
    }

    [RelayCommand]
    private void RemoveGradient()
    {
        if (SelectedGradient is not { } gradient) return;
        var id = gradient.Id;
        _edit(d => d.Gradients.Remove(id));
    }

    /// <summary>Insert a stop halfway between the selected one and the next.</summary>
    [RelayCommand]
    private void AddStop()
    {
        if (SelectedGradient is not { } gradient) return;
        var id = gradient.Id;
        var ordered = GradientOps.Ordered(gradient);
        var at = SelectedStop is { } row ? row.Model.Position : 0.5;
        var next = ordered.FirstOrDefault(s => s.Position > at)?.Position ?? 1.0;
        var position = ordered.Count == 0 ? 0.5 : (at + next) / 2;
        var (r, g, b, a) = GradientOps.Sample(gradient, position);

        _edit(d =>
        {
            if (!d.Gradients.TryGetValue(id, out var target)) return;
            target.Stops.Add(new DocStop
            {
                Position = position,
                Color = $"#{r:x2}{g:x2}{b:x2}",
                Alpha = a / 255.0,
            });
        });
        SelectedStop = Stops.FirstOrDefault(s => Math.Abs(s.Model.Position - position) < 1e-9);
    }

    /// <summary>
    /// Remove the selected stop. A gradient needs two to be a ramp, so the
    /// last two are kept — deleting down to one would leave the tool painting
    /// a flat colour with no way back except undo.
    /// </summary>
    [RelayCommand]
    private void RemoveStop()
    {
        if (SelectedGradient is not { Stops.Count: > 2 } gradient || SelectedStop is not { } row) return;
        var id = gradient.Id;
        var stop = row.Model;
        _edit(d =>
        {
            if (d.Gradients.TryGetValue(id, out var target)) target.Stops.Remove(stop);
        });
    }

    /// <summary>Called by the owner when a stop's colour should come from the picker.</summary>
    public void SetSelectedStopColor(string hex)
    {
        if (SelectedStop is { } row) row.Color = hex;
    }
}

/// <summary>Shared hex parsing for the palette and gradient editors.</summary>
internal static class HexColor
{
    /// <summary>"#rgb", "rgb", "#rrggbb" or "rrggbb" to "#rrggbb"; anything else is rejected.</summary>
    public static string? Normalize(string? text)
    {
        if (text is null) return null;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        if (s.Length != 6) return null;
        foreach (var c in s)
        {
            if (!Uri.IsHexDigit(c)) return null;
        }
        return $"#{s.ToLowerInvariant()}";
    }
}
