using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Services;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>One effect in the artist's library.</summary>
public sealed partial class EffectPresetRow(EffectPreset preset) : ObservableObject
{
    public EffectPreset Preset { get; } = preset;

    public string Id => Preset.Id;

    public string Label => Preset.Name;

    /// <summary>"3 elements · 34 frames", so the shelf says what a preset is without opening it.</summary>
    public string Detail
    {
        get
        {
            var n = Preset.Elements.Count;
            if (n == 0) return "empty";
            var frames = Preset.Elements.Max(e => e.FirstFrame + Math.Max(0, e.FrameCount));
            return $"{n} element{(n == 1 ? string.Empty : "s")} · {frames} frames";
        }
    }
}

/// <summary>
/// The library: an effect tuned once, used again.
/// </summary>
/// <remarks>
/// <b>A preset is parameters and a symbol is frames</b> (Q129), which is why
/// using one costs a simulation and gives something that can then be retuned —
/// forty frames instead of twenty-four, twice the size, blowing left. The
/// library is a source to choose from rather than a live dependency, so nothing
/// here is referenced once an effect has been made from it.
/// </remarks>
public sealed partial class FluidEffectsViewModel
{
    private EffectPresetStore.State _library = new();
    private bool _libraryLoaded;

    public ObservableCollection<EffectPresetRow> Presets { get; } = [];

    [ObservableProperty]
    private EffectPresetRow? _selectedPreset;

    /// <summary>Where the library is kept. A test seam; null is the artist's own file.</summary>
    internal string? LibraryPath { get; set; }

    internal void ReloadPresets()
    {
        _library = EffectPresetStore.Load(LibraryPath);
        Presets.Clear();
        foreach (var preset in _library.Presets.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Presets.Add(new EffectPresetRow(preset));
        }
        SelectedPreset = Presets.FirstOrDefault();
    }

    /// <summary>
    /// Keep the selected effect under a name.
    /// </summary>
    /// <remarks>
    /// A group rather than an element, because an explosion is a flash and a
    /// fireball and a roll of smoke — saving one of them would save a third of
    /// the effect and lose the timing between them.
    /// </remarks>
    public EffectPresetRow? SavePreset(string? name = null)
    {
        if (Group is not { } group) return null;

        var preset = EffectPresets.Capture(_vm.Doc, group, name ?? group.Name);
        if (preset.Elements.Count == 0)
        {
            Status = "There is nothing in that effect to keep.";
            return null;
        }

        _library.Presets.Add(preset);
        EffectPresetStore.Save(_library, LibraryPath);
        ReloadPresets();

        var row = Presets.FirstOrDefault(p => p.Id == preset.Id);
        SelectedPreset = row;
        Status = $"Kept “{preset.Name}”.";
        return row;
    }

    /// <summary>Make the selected preset again, in this document.</summary>
    /// <remarks>
    /// Placed where the current effect is if there is one, so using a preset
    /// beside an effect you are already working on does not drop it at the
    /// origin and make you go looking.
    /// </remarks>
    public SimGroupRow? UsePreset()
    {
        if (SelectedPreset?.Preset is not { } preset) return null;

        var (x, y, frame) = Group is { } current && Members().Count > 0
            ? (GroupOriginX, GroupOriginY, (int)GroupFirstFrame)
            : (0d, 0d, 0);

        var missing = EffectPresets.MissingLayers(_vm.Doc, preset);
        var made = _vm.MakeEffectFromPreset(preset, x, y, frame);
        if (made is null) return null;

        _cache.Clear();
        Reload();
        SelectedGroup = Groups.FirstOrDefault(g => g.Id == made.Id);

        Status = missing.Count == 0
            ? $"Made “{preset.Name}”."
            : $"Made “{preset.Name}” — no layer called {string.Join(" or ", missing.Select(m => $"“{m}”"))}, " +
              "so whatever used it is emitting from nothing.";
        return SelectedGroup;
    }

    /// <summary>Take a preset off the shelf. The effects already made from it are untouched.</summary>
    public void DeletePreset()
    {
        if (SelectedPreset is not { } row) return;

        _library.Presets.RemoveAll(p => p.Id == row.Id);
        EffectPresetStore.Save(_library, LibraryPath);
        ReloadPresets();
        Status = $"Removed “{row.Label}” from the library.";
    }

    /// <summary>Whether using the selected preset would leave something pointing at nothing.</summary>
    public IReadOnlyList<string> MissingLayersForSelectedPreset =>
        SelectedPreset?.Preset is { } preset ? EffectPresets.MissingLayers(_vm.Doc, preset) : [];

    partial void OnSelectedPresetChanged(EffectPresetRow? value) =>
        OnPropertyChanged(nameof(MissingLayersForSelectedPreset));
}
