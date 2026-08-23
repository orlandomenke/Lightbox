using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Raster.Effects;

namespace Lightbox.App.ViewModels;

/// <summary>One slider of the selected effect, writing through as undoable edits.</summary>
public sealed partial class EffectParamRow : ObservableObject
{
    private readonly EffectsViewModel _owner;
    private readonly EffectUse _use;
    private readonly EffectParamSpec _spec;
    private bool _syncing;

    internal EffectParamRow(EffectsViewModel owner, EffectUse use, EffectParamSpec spec, int frame)
    {
        _owner = owner;
        _use = use;
        _spec = spec;
        _syncing = true;
        Value = use.At(spec.Key, frame, spec.Default);
        _syncing = false;
    }

    public string Label => _spec.Label;

    public double Min => _spec.Min;

    public double Max => _spec.Max;

    [ObservableProperty]
    private double _value;

    partial void OnValueChanged(double value)
    {
        if (!_syncing) _owner.CommitParam(_use, _spec, value);
    }
}

/// <summary>One effect in the edited stack, as the docker's list shows it.</summary>
public sealed partial class EffectUseRow : ObservableObject
{
    private readonly EffectsViewModel _owner;
    private bool _syncing;

    internal EffectUseRow(EffectsViewModel owner, EffectUse use)
    {
        _owner = owner;
        Use = use;
        var def = EffectRegistry.Resolve(use.Kind);
        Name = def?.Name ?? $"{use.Kind} (unknown)";
        IsKnown = def is not null;
        _syncing = true;
        Enabled = use.Applies;
        _syncing = false;
    }

    internal EffectUse Use { get; }

    public string Name { get; }

    /// <summary>False for a kind from a newer build — kept, shown, inert.</summary>
    public bool IsKnown { get; }

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnEnabledChanged(bool value)
    {
        if (!_syncing) _owner.SetUseDisabled(Use, !value);
    }
}

/// <summary>
/// The effects docker's view model (DESIGN-effects.md's decoupling bar): the
/// stack on the active layer or the scene, its parameters as sliders, and
/// the way adjustment layers are made. Owns every effect command so
/// <see cref="MainViewModel"/> gains only the registration property.
/// </summary>
public sealed partial class EffectsViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    internal EffectsViewModel(MainViewModel owner)
    {
        _owner = owner;
        Catalogue = [.. EffectRegistry.All.Select(d => new EffectChoice(d.Kind, d.Name))];
        _owner.PropertyChanged += (_, e) =>
        {
            // The panel mirrors the selection context: a new active layer, a
            // moved playhead (keyed params show their value there) or a
            // document switch all re-aim it.
            if (e.PropertyName is nameof(MainViewModel.ActiveLayerIndex)
                or nameof(MainViewModel.CurrentFrameIndex)
                or nameof(MainViewModel.Doc))
            {
                Rebuild();
            }
        };
    }

    /// <summary>A pickable effect kind for the add buttons.</summary>
    public sealed record EffectChoice(string Kind, string Name);

    public IReadOnlyList<EffectChoice> Catalogue { get; }

    public ObservableCollection<EffectUseRow> Uses { get; } = [];

    public ObservableCollection<EffectParamRow> Params { get; } = [];

    /// <summary>True: the panel edits the scene grade instead of the active layer's stack.</summary>
    [ObservableProperty]
    private bool _editingScene;

    partial void OnEditingSceneChanged(bool value) => Rebuild();

    [ObservableProperty]
    private string _targetLabel = "";

    /// <summary>The adjustment badge — says what the active layer's stack reaches.</summary>
    [ObservableProperty]
    private string _scopeNote = "";

    private EffectUseRow? _selected;

    /// <summary>The stack the panel is currently pointed at, created on demand by edits.</summary>
    private EffectStack? TargetStack(bool create)
    {
        if (EditingScene)
        {
            var scene = _owner.Doc.Scene;
            if (scene.Effects is null && create) scene.Effects = new EffectStack();
            return scene.Effects;
        }
        var layer = ActiveLayer;
        if (layer is null) return null;
        if (layer.Effects is null && create) layer.Effects = new EffectStack();
        return layer.Effects;
    }

    private Layer? ActiveLayer =>
        _owner.ActiveLayerIndex >= 0 && _owner.ActiveLayerIndex < _owner.Doc.Scene.Layers.Count
            ? _owner.Doc.Scene.Layers[_owner.ActiveLayerIndex]
            : null;

    /// <summary>Re-read everything from the document — after any edit, undo included.</summary>
    internal void Rebuild()
    {
        var selectedId = _selected?.Use.Id;
        Uses.Clear();
        var stack = TargetStack(create: false);
        if (stack is not null)
        {
            foreach (var use in stack.Uses) Uses.Add(new EffectUseRow(this, use));
        }
        TargetLabel = EditingScene
            ? "Scene — grades everything the camera sees"
            : ActiveLayer is { } layer
                ? layer.IsAdjustment
                    ? $"{layer.Name} — adjusts the layers beneath it"
                    : layer.Name
                : "No layer";
        ScopeNote = !EditingScene && ActiveLayer is { IsAdjustment: false, HasLiveEffects: true }
            ? "Applied to this layer's own drawing, before blend."
            : "";
        Select(Uses.FirstOrDefault(r => r.Use.Id == selectedId) ?? Uses.FirstOrDefault());
    }

    internal void Select(EffectUseRow? row)
    {
        foreach (var r in Uses) r.IsSelected = ReferenceEquals(r, row);
        _selected = row;
        Params.Clear();
        if (row is null || EffectRegistry.Resolve(row.Use.Kind) is not { } def) return;
        foreach (var spec in def.Params)
        {
            Params.Add(new EffectParamRow(this, row.Use, spec, _owner.CurrentFrameIndex));
        }
    }

    [RelayCommand]
    private void SelectUse(EffectUseRow row) => Select(row);

    /// <summary>Add one effect to the edited stack, seeded with its defaults.</summary>
    [RelayCommand]
    private void AddUse(EffectChoice choice)
    {
        if (EffectRegistry.Resolve(choice.Kind) is not { } def) return;
        if (!EditingScene && ActiveLayer is null) return;
        _owner.PanelEditor.Perform(_ =>
        {
            var stack = TargetStack(create: true)!;
            var use = new EffectUse { Kind = def.Kind };
            foreach (var spec in def.Params)
            {
                use.Params[spec.Key] = new EffectParam(spec.Default);
            }
            stack.Uses.Add(use);
        }, label: $"Add {def.Name}", frameContentUnchanged: true);
        Rebuild();
        Select(Uses.LastOrDefault());
    }

    [RelayCommand]
    private void RemoveUse(EffectUseRow row)
    {
        _owner.PanelEditor.Perform(_ =>
        {
            var stack = TargetStack(create: false);
            stack?.Uses.RemoveAll(u => u.Id == row.Use.Id);
            // The last effect leaving takes the stack with it, so the
            // document returns to writing no key at all — absent, not empty.
            if (stack is { Uses.Count: 0 }) ClearTargetStack();
        }, label: "Remove effect", frameContentUnchanged: true);
        Rebuild();
    }

    private void ClearTargetStack()
    {
        if (EditingScene) _owner.Doc.Scene.Effects = null;
        else if (ActiveLayer is { } layer) layer.Effects = null;
    }

    internal void SetUseDisabled(EffectUse use, bool disabled)
    {
        if (use.Disabled == (disabled ? true : (bool?)null)) return;
        _owner.PanelEditor.Perform(
            _ => use.Disabled = disabled ? true : null,
            label: disabled ? "Disable effect" : "Enable effect",
            frameContentUnchanged: true);
    }

    internal void CommitParam(EffectUse use, EffectParamSpec spec, double value)
    {
        var clamped = Math.Clamp(value, spec.Min, spec.Max);
        _owner.PanelEditor.Perform(_ =>
        {
            if (!use.Params.TryGetValue(spec.Key, out var param))
            {
                use.Params[spec.Key] = param = new EffectParam(spec.Default);
            }
            // The constant. Keyed parameters are edited on the timeline once
            // keying UI lands; the record already carries them (Q122's
            // shared vocabulary), so this cannot orphan a curve.
            param.Value = clamped;
        }, label: "Effect setting", frameContentUnchanged: true);
    }

    /// <summary>
    /// A new adjustment layer above the active one, carrying one effect —
    /// Photoshop's gesture, on the ordinary layer machinery: mask it, clip
    /// it, fade it, hide it like any layer (Q146).
    /// </summary>
    [RelayCommand]
    private void AddAdjustmentLayer(EffectChoice choice)
    {
        if (EffectRegistry.Resolve(choice.Kind) is not { } def) return;
        var insertAt = Math.Clamp(_owner.ActiveLayerIndex + 1, 0, _owner.Doc.Scene.Layers.Count);
        _owner.PanelEditor.Perform(doc =>
        {
            var use = new EffectUse { Kind = def.Kind };
            foreach (var spec in def.Params)
            {
                use.Params[spec.Key] = new EffectParam(spec.Default);
            }
            var layer = new Layer
            {
                Name = def.Name,
                Adjusts = true,
                Effects = new EffectStack { Uses = [use] },
            };
            while (layer.Cels.Count < doc.Scene.FrameCount) layer.Cels.Add(new Cel());
            doc.Scene.Layers.Insert(Math.Min(insertAt, doc.Scene.Layers.Count), layer);
        }, label: $"Add {def.Name} adjustment layer", frameContentUnchanged: true);
        _owner.ActiveLayerIndex = insertAt;
        EditingScene = false;
        Rebuild();
    }
}
