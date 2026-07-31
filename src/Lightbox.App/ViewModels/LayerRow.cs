using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

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

    internal void SyncFromModel(Layer layer, int sceneIndex)
    {
        Layer = layer;
        SceneIndex = sceneIndex;
        _syncing = true;
        Name = layer.Name;
        Visible = layer.Visible;
        OnionEnabled = layer.OnionEnabled;
        _syncing = false;
        OnPropertyChanged(nameof(KindLabel));
    }

    partial void OnNameChanged(string value)
    {
        if (!_syncing) _owner.CommitLayerRename(this, value);
    }

    partial void OnVisibleChanged(bool value)
    {
        if (!_syncing) _owner.SetLayerVisible(Layer, value);
    }

    partial void OnOnionEnabledChanged(bool value)
    {
        if (!_syncing) _owner.SetLayerOnionEnabled(Layer, value);
    }
}
