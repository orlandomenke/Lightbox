using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// One swatch, as the docker sees it. Wraps the document's <see cref="Swatch"/>
/// rather than copying it, because the whole point of the feature is that the
/// object the engine resolves and the object the artist edits are the same one.
/// </summary>
public sealed partial class SwatchRow : ObservableObject
{
    private readonly Action<SwatchRow, string> _edited;

    /// <param name="edited">Called with the row and the colour it held before the edit.</param>
    public SwatchRow(Swatch model, Action<SwatchRow, string> edited)
    {
        Model = model;
        _edited = edited;
    }

    public Swatch Model { get; }

    public string Id => Model.Id;

    public string Color
    {
        get => Model.Color;
        set
        {
            var hex = Normalize(value);
            if (hex is null || hex == Model.Color) return;
            var before = Model.Color;
            Model.Color = hex;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Fill));
            OnPropertyChanged(nameof(Label));
            _edited(this, before);
        }
    }

    public string? Name
    {
        get => Model.Name;
        set
        {
            var name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (name == Model.Name) return;
            Model.Name = name;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Label));
        }
    }

    /// <summary>What the tooltip and the list row show — the name if it has one.</summary>
    public string Label => Model.Name is { Length: > 0 } name ? name : Model.Color;

    public IBrush Fill
    {
        get
        {
            var (r, g, b) = GimpPalette.Rgb(Model.Color);
            return new SolidColorBrush(Avalonia.Media.Color.FromRgb(r, g, b));
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Push a colour set from elsewhere (the picker) without re-entering the setter's guard.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(Fill));
        OnPropertyChanged(nameof(Label));
    }

    /// <summary>"#rgb", "rgb", "#rrggbb" or "rrggbb" to "#rrggbb"; anything else is rejected.</summary>
    private static string? Normalize(string? text)
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

/// <summary>
/// The palette docker: the document's palettes, the swatches in the selected
/// one, and the two things an artist does with them — paint with a swatch, and
/// recolour a swatch and watch the art follow.
///
/// State lives here rather than in <see cref="MainViewModel"/> because the
/// docker needs no part of the paint pipeline; it reaches the document through
/// the three callbacks it is handed. Everything that touches pixels or undo
/// stays on the owner's side of that line.
/// </summary>
public sealed partial class PaletteDockerViewModel : ObservableObject
{
    private readonly Action<SwatchRow, string> _swatchRecoloured;
    private readonly Action<Action<Doc>> _edit;
    private readonly Action<string> _paintWith;
    private readonly Func<string> _currentColor;

    private Doc? _doc;
    private bool _loading;

    public PaletteDockerViewModel(
        Action<SwatchRow, string> swatchRecoloured,
        Action<Action<Doc>> edit,
        Action<string> paintWith,
        Func<string> currentColor)
    {
        _swatchRecoloured = swatchRecoloured;
        _edit = edit;
        _paintWith = paintWith;
        _currentColor = currentColor;
    }

    public ObservableCollection<Palette> Palettes { get; } = [];

    public ObservableCollection<SwatchRow> Swatches { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPalette))]
    private Palette? _selectedPalette;

    public bool HasPalette => SelectedPalette is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSwatch))]
    private SwatchRow? _selectedSwatch;

    public bool HasSwatch => SelectedSwatch is not null;

    /// <summary>
    /// When on, the colour picker edits the selected swatch instead of only
    /// setting the paint colour — Toon Boom's colour editor, and the mode in
    /// which dragging the wheel recolours the drawing live. Off by default:
    /// an artist reaching for the wheel means "paint in this colour" far more
    /// often than "repaint everything I have already drawn in it".
    /// </summary>
    [ObservableProperty]
    private bool _editSelectedSwatch;

    /// <summary>
    /// A run of colour edits to one swatch has finished. Dragging the wheel
    /// produces an edit per pointer event; the owner coalesces them into a
    /// single undo step and this is where it closes one off.
    /// </summary>
    public event Action? SwatchEditRunEnded;

    partial void OnEditSelectedSwatchChanged(bool value) => SwatchEditRunEnded?.Invoke();

    /// <summary>Status line for the docker's bottom bar — import/export results, mostly.</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    partial void OnSelectedPaletteChanged(Palette? value) => RebuildSwatches();

    partial void OnSelectedSwatchChanged(SwatchRow? oldValue, SwatchRow? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
            // Not while reloading: the rows are rebuilt from the same document
            // and the "change" is the same swatch under a new wrapper.
            if (!_loading) SwatchEditRunEnded?.Invoke();
        }
        if (newValue is null) return;
        newValue.IsSelected = true;
        // Selecting a swatch is how you paint with it. The owner records the
        // link, so the next stroke references the swatch rather than copying
        // the colour out of it.
        if (!_loading) _paintWith(newValue.Id);
    }

    /// <summary>Point the docker at a document. Idempotent; called on every document change.</summary>
    public void Load(Doc doc)
    {
        _doc = doc;
        var keepPalette = SelectedPalette?.Id;
        var keepSwatch = SelectedSwatch?.Id;

        _loading = true;
        try
        {
            Palettes.Clear();
            foreach (var palette in doc.Palettes) Palettes.Add(palette);
            SelectedPalette = Palettes.FirstOrDefault(p => p.Id == keepPalette) ?? Palettes.FirstOrDefault();
            RebuildSwatches();
            SelectedSwatch = Swatches.FirstOrDefault(s => s.Id == keepSwatch);
        }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildSwatches()
    {
        var keep = SelectedSwatch?.Id;
        Swatches.Clear();
        foreach (var swatch in SelectedPalette?.Swatches ?? []) Swatches.Add(new SwatchRow(swatch, OnSwatchEdited));
        SelectedSwatch = Swatches.FirstOrDefault(s => s.Id == keep);
    }

    private void OnSwatchEdited(SwatchRow row, string before) => _swatchRecoloured(row, before);

    /// <summary>
    /// Called by the owner when the colour picker moves and
    /// <see cref="EditSelectedSwatch"/> is on. Returns true if it consumed the
    /// colour, so the owner knows not to treat it as a plain paint-colour change.
    /// </summary>
    public bool ApplyPickerColor(string hex)
    {
        if (!EditSelectedSwatch || SelectedSwatch is not { } row) return false;
        row.Color = hex;
        return true;
    }

    // ---- commands -----------------------------------------------------------

    [RelayCommand]
    private void AddPalette()
    {
        if (_doc is null) return;
        var palette = new Palette { Name = $"Palette {_doc.Palettes.Count + 1}" };
        _edit(d => d.Palettes.Add(palette));
        SelectedPalette = Palettes.FirstOrDefault(p => p.Id == palette.Id);
    }

    [RelayCommand]
    private void RemovePalette()
    {
        if (SelectedPalette is not { } palette) return;
        var id = palette.Id;
        _edit(d => d.Palettes.RemoveAll(p => p.Id == id));
    }

    /// <summary>Add the colour currently in the picker as a new swatch.</summary>
    [RelayCommand]
    private void AddSwatch()
    {
        if (SelectedPalette is not { } palette) return;
        var id = palette.Id;
        var swatch = new Swatch { Color = _currentColor() };
        _edit(d =>
        {
            if (d.Palettes.FirstOrDefault(p => p.Id == id) is { } target) target.Swatches.Add(swatch);
        });
        SelectedSwatch = Swatches.FirstOrDefault(s => s.Id == swatch.Id);
    }

    /// <summary>
    /// Remove a swatch from the palette. Art that referenced it keeps the
    /// literal colour recorded on the stroke — the reference goes dead rather
    /// than the drawing going black.
    /// </summary>
    [RelayCommand]
    private void RemoveSwatch()
    {
        if (SelectedPalette is not { } palette || SelectedSwatch is not { } row) return;
        var paletteId = palette.Id;
        var swatchId = row.Id;
        _edit(d =>
        {
            if (d.Palettes.FirstOrDefault(p => p.Id == paletteId) is { } target)
            {
                target.Swatches.RemoveAll(s => s.Id == swatchId);
            }
        });
    }

    public void ImportGpl(string path)
    {
        if (_doc is null) return;
        try
        {
            var palette = GimpPalette.Read(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            _edit(d => d.Palettes.Add(palette));
            SelectedPalette = Palettes.FirstOrDefault(p => p.Id == palette.Id);
            Status = $"Imported {palette.Swatches.Count} swatches from {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not read {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    public void ExportGpl(string path)
    {
        if (SelectedPalette is not { } palette) return;
        try
        {
            File.WriteAllText(path, GimpPalette.Write(palette));
            Status = $"Wrote {palette.Swatches.Count} swatches to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not write {Path.GetFileName(path)}: {ex.Message}";
        }
    }
}
