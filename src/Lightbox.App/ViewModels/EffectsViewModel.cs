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
        Value = use.At(spec.Key, frame, EffectRegistry.DefaultOf(spec, use));
        _syncing = false;
    }

    public string Label => _spec.Label;

    public double Min => _spec.Min;

    public double Max => _spec.Max;

    public double Increment => _spec.Increment;

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
/// One colour of the selected effect — a swatch and a hex field, the same
/// lightweight colour entry the fluid-effects window uses. Only a parseable
/// hex commits, so a half-typed value never reaches the record.
/// </summary>
public sealed partial class EffectColorRow : ObservableObject
{
    private readonly EffectsViewModel _owner;
    private readonly EffectUse _use;
    private readonly EffectColorSpec _spec;
    private bool _syncing;

    internal EffectColorRow(EffectsViewModel owner, EffectUse use, EffectColorSpec spec)
    {
        _owner = owner;
        _use = use;
        _spec = spec;
        _syncing = true;
        Value = use.ColorAt(spec.Key, spec.Default);
        _syncing = false;
    }

    public string Label => _spec.Label;

    [ObservableProperty]
    private string _value = "";

    /// <summary>One brush per value, not per read (the leak review's finding).</summary>
    [ObservableProperty]
    private Avalonia.Media.IBrush _swatch = Avalonia.Media.Brushes.Transparent;

    partial void OnValueChanged(string value)
    {
        Swatch = Avalonia.Media.Color.TryParse(value, out var color)
            ? new Avalonia.Media.SolidColorBrush(color)
            : Avalonia.Media.Brushes.Transparent;
        if (!_syncing) _owner.CommitColor(_use, _spec, value);
    }
}

/// <summary>A pickable effect kind for the docker's add buttons.</summary>
public sealed record EffectChoice(string Kind, string Name);

/// <summary>
/// One shelf of the add row — the design's presentation lane, never a
/// capability: any effect can be keyed whatever shelf it sits on. Eleven
/// kinds in one wrap panel is a wall of buttons; grouped, an artist looking
/// for a glow reads one heading instead of eleven labels.
/// </summary>
public sealed record EffectShelf(string Name, IReadOnlyList<EffectChoice> Choices);

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
        AddChoices = [.. EffectRegistry.All.Where(d => !d.BackdropOnly)
            .Select(d => new EffectChoice(d.Kind, d.Name))];
        AddShelves = ShelvesOf(d => !d.BackdropOnly);
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

    public IReadOnlyList<EffectChoice> Catalogue { get; }

    /// <summary>What each shelf id is called in front of an artist.</summary>
    private static string ShelfName(string shelf) => shelf switch
    {
        "grade" => "Colour",
        "blur" => "Blur",
        "style" => "Layer styles",
        "anim" => "Animation",
        _ => shelf,
    };

    private static IReadOnlyList<EffectShelf> ShelvesOf(Func<EffectDefinition, bool> offered) =>
        [.. EffectRegistry.All.Where(offered)
            .GroupBy(d => d.Shelf)
            .Select(g => new EffectShelf(
                ShelfName(g.Key),
                [.. g.Select(d => new EffectChoice(d.Kind, d.Name))]))];

    /// <summary>
    /// The kinds the "add to this stack" row offers — the catalogue, minus
    /// backdrop-only kinds when the target is a plain layer's own stack,
    /// where they would render as identity. They stay one row down, as an
    /// adjustment layer, which clipped to the layer below is the per-layer
    /// use of the same pixels.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<EffectChoice> _addChoices = [];

    /// <summary>The same offer, grouped by shelf — what the panel shows.</summary>
    [ObservableProperty]
    private IReadOnlyList<EffectShelf> _addShelves = [];

    public ObservableCollection<EffectUseRow> Uses { get; } = [];

    public ObservableCollection<EffectParamRow> Params { get; } = [];

    public ObservableCollection<EffectColorRow> ColorRows { get; } = [];

    /// <summary>
    /// The adjustment-layer row's kinds: everything but the layer styles,
    /// which read a silhouette an adjustment layer does not have (Q153).
    /// </summary>
    public IReadOnlyList<EffectChoice> AdjustmentChoices { get; } =
        [.. EffectRegistry.All.Where(d => !d.SelfOnly)
            .Select(d => new EffectChoice(d.Kind, d.Name))];

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
        // Each scope offers only what does something there: a plain layer's
        // own stack takes no backdrop-only kind (identity on the self path),
        // and the backdrop scopes take no style (no silhouette to read).
        var selfStack = !EditingScene && ActiveLayer is { IsAdjustment: false };
        bool Offered(EffectDefinition d) => selfStack ? !d.BackdropOnly : !d.SelfOnly;
        AddChoices = [.. EffectRegistry.All.Where(Offered)
            .Select(d => new EffectChoice(d.Kind, d.Name))];
        AddShelves = ShelvesOf(Offered);
        StackExists = stack is not null;
        _syncingStack = true;
        StackEnabled = stack is not { Disabled: true };
        _syncingStack = false;
        Select(Uses.FirstOrDefault(r => r.Use.Id == selectedId) ?? Uses.FirstOrDefault());
        // The rows' fx chips mirror the stacks this panel edits.
        _owner.SyncMaskRows();
    }

    /// <summary>Whether the edited target currently has a stack at all — gates the master switch.</summary>
    [ObservableProperty]
    private bool _stackExists;

    private bool _syncingStack;

    /// <summary>
    /// The stack's master switch (Q158), as the panel header's checkbox:
    /// everything off in one click, every use's own switch untouched.
    /// </summary>
    [ObservableProperty]
    private bool _stackEnabled = true;

    partial void OnStackEnabledChanged(bool value)
    {
        if (_syncingStack || TargetStack(create: false) is not { } stack) return;
        if ((stack.Disabled == true) == !value) return;
        _owner.PanelEditor.Perform(_ => stack.Disabled = value ? null : true,
            label: value ? "Enable effects" : "Disable effects",
            frameContentUnchanged: true);
        Rebuild();
    }

    internal void Select(EffectUseRow? row)
    {
        foreach (var r in Uses) r.IsSelected = ReferenceEquals(r, row);
        _selected = row;
        Params.Clear();
        ColorRows.Clear();
        if (row is null || EffectRegistry.Resolve(row.Use.Kind) is not { } def) return;
        foreach (var spec in def.Params)
        {
            Params.Add(new EffectParamRow(this, row.Use, spec, _owner.CurrentFrameIndex));
        }
        foreach (var spec in def.ColorSpecs)
        {
            ColorRows.Add(new EffectColorRow(this, row.Use, spec));
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
        // The add row already hides these per scope; the guards keep a
        // programmatic add from writing a use that renders as identity.
        var selfStack = !EditingScene && ActiveLayer is { IsAdjustment: false };
        if (def.BackdropOnly && selfStack) return;
        if (def.SelfOnly && !selfStack) return;
        _owner.PanelEditor.Perform(_ =>
        {
            var stack = TargetStack(create: true)!;
            var use = new EffectUse { Kind = def.Kind };
            foreach (var spec in def.Params)
            {
                // A per-use default (a seed) is derived from the use's id, so
                // writing it here would freeze one value into every document
                // and lose the point of it being per use (Q159).
                if (spec.PerUse) continue;
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
                use.Params[spec.Key] = param = new EffectParam(
                    EffectRegistry.DefaultOf(spec, use));
            }
            // The constant. Keyed parameters are edited on the timeline once
            // keying UI lands; the record already carries them (Q122's
            // shared vocabulary), so this cannot orphan a curve.
            param.Value = clamped;
        }, label: "Effect setting", frameContentUnchanged: true);
    }

    internal void CommitColor(EffectUse use, EffectColorSpec spec, string value)
    {
        if (!Avalonia.Media.Color.TryParse(value, out _)) return; // half-typed hex
        _owner.PanelEditor.Perform(_ =>
        {
            // Authored on first edit, absent before it (Q153): the renderer
            // reads the spec's default until a colour actually exists.
            use.Colors ??= [];
            use.Colors[spec.Key] = value;
        }, label: "Effect colour", frameContentUnchanged: true);
    }

    /// <summary>
    /// A new adjustment layer above the active one, carrying one effect —
    /// Photoshop's gesture, on the ordinary layer machinery: mask it, clip
    /// it, fade it, hide it like any layer (Q151).
    /// </summary>
    [RelayCommand]
    private void AddAdjustmentLayer(EffectChoice choice)
    {
        if (EffectRegistry.Resolve(choice.Kind) is not { } def) return;
        if (def.SelfOnly) return; // a style has no backdrop silhouette to read
        var insertAt = Math.Clamp(_owner.ActiveLayerIndex + 1, 0, _owner.Doc.Scene.Layers.Count);
        _owner.PanelEditor.Perform(doc =>
        {
            var use = new EffectUse { Kind = def.Kind };
            foreach (var spec in def.Params)
            {
                if (spec.PerUse) continue;
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
