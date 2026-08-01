using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// A layer folder's header row in the docker: visibility gates every member,
/// collapse hides them from the panel, rename/dissolve like a layer.
/// </summary>
public sealed partial class GroupRow : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _syncing;

    internal GroupRow(MainViewModel owner, LayerGroup group)
    {
        _owner = owner;
        Group = group;
        _syncing = true;
        Name = group.Name;
        Visible = group.Visible;
        Locked = group.Locked;
        Collapsed = group.Collapsed;
        Color = group.Color;
        _syncing = false;
    }

    public LayerGroup Group { get; }

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _visible = true;

    /// <summary>Locking a folder locks every layer inside it.</summary>
    [ObservableProperty]
    private bool _locked;

    [ObservableProperty]
    private bool _collapsed;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _color = "#4a6ea9";

    public string CollapseGlyph => Collapsed ? "▸" : "▾";

    /// <summary>The folder's accent color for the header bar.</summary>
    public Avalonia.Media.IBrush ColorBrush =>
        Avalonia.Media.Brush.Parse(Color);

    partial void OnNameChanged(string value)
    {
        if (!_syncing) _owner.CommitGroupRename(Group, value);
    }

    partial void OnColorChanged(string value)
    {
        OnPropertyChanged(nameof(ColorBrush));
        if (!_syncing) _owner.SetGroupColor(Group, value);
    }

    partial void OnLockedChanged(bool value)
    {
        if (!_syncing) _owner.SetGroupLocked(Group, value);
    }

    partial void OnVisibleChanged(bool value)
    {
        if (!_syncing) _owner.SetGroupVisible(Group, value);
    }

    partial void OnCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(CollapseGlyph));
        if (!_syncing) _owner.SetGroupCollapsed(Group, value);
    }
}

/// <summary>
/// One layer as shown in the layer docker and the timeline: renamable name,
/// visibility, per-layer onion-skin toggle, and the layer's timeline cells.
/// Edits write through to the document via the owning view model (rename and
/// visibility as undoable steps); model changes sync back in.
/// </summary>
public sealed partial class LayerRow : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _syncing;

    internal LayerRow(MainViewModel owner)
    {
        _owner = owner;
        Layer = new Layer();
    }

    /// <summary>The document layer this row currently mirrors.</summary>
    public Layer Layer { get; private set; }

    /// <summary>Index into Scene.Layers (0 = bottom).</summary>
    public int SceneIndex { get; private set; }

    public ObservableCollection<FrameCell> Cells { get; } = [];

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _visible = true;

    /// <summary>Refuses pixel and geometry edits; still renders and exports.</summary>
    [ObservableProperty]
    private bool _locked;

    /// <summary>Paint only where the layer already has content.</summary>
    [ObservableProperty]
    private bool _alphaLocked;

    private bool _lockedByFolder;

    [ObservableProperty]
    private bool _onionEnabled = true;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>True while the name is being edited (double-click to start).</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>Small preview of the layer's exposed drawing at the playhead (checkerboard = transparent).</summary>
    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _thumb;

    /// <summary>Staleness key: the exposed frame id the thumb was rendered from.</summary>
    internal string? ThumbFrameId;

    public string KindLabel => Layer.Kind == LayerKind.Vector ? "V" : "R";

    /// <summary>Inside a layer folder (indented in the docker, eject button shown).</summary>
    public bool IsGrouped => Layer.GroupId is not null;

    internal void SyncFromModel(Layer layer, int sceneIndex)
    {
        Layer = layer;
        SceneIndex = sceneIndex;
        _syncing = true;
        Name = layer.Name;
        Visible = layer.Visible;
        Locked = layer.Locked;
        AlphaLocked = layer.AlphaLocked;
        OnionEnabled = layer.OnionEnabled;
        _lockedByFolder = _owner.IsLayerLockedByFolder(layer);
        _syncing = false;
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(IsGrouped));
    }

    partial void OnNameChanged(string value)
    {
        if (!_syncing) _owner.CommitLayerRename(this, value);
    }

    partial void OnVisibleChanged(bool value)
    {
        if (!_syncing) _owner.SetLayerVisible(Layer, value);
    }

    partial void OnLockedChanged(bool value)
    {
        if (!_syncing) _owner.SetLayerLocked(Layer, value);
        OnPropertyChanged(nameof(EditsBlocked));
    }

    partial void OnAlphaLockedChanged(bool value)
    {
        if (!_syncing) _owner.SetLayerAlphaLocked(Layer, value);
    }

    /// <summary>
    /// Whether this row refuses edits, folder included. Drives the dimmed row
    /// so a layer locked by its folder does not look editable.
    /// </summary>
    public bool EditsBlocked => Locked || _lockedByFolder;

    partial void OnOnionEnabledChanged(bool value)
    {
        if (!_syncing) _owner.SetLayerOnionEnabled(Layer, value);
    }
}
