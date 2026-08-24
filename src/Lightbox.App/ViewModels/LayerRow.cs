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

    /// <summary>
    /// Part of the docker's multi-layer selection (Ctrl+click / Shift+click).
    /// Always true of the active row, and true of more than one row only while
    /// a selection is being worked on.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True while the name is being edited (double-click to start).</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>Small preview of the layer's exposed drawing at the playhead (checkerboard = transparent).</summary>
    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _thumb;

    /// <summary>Staleness key: the exposed frame id the thumb was rendered from.</summary>
    internal string? ThumbFrameId;


    /// <summary>Whether the layer carries a painted mask (the docker's chip).</summary>
    public bool HasMask => Layer.Mask is not null;

    /// <summary>The mask exists but is switched off — the chip goes hollow.</summary>
    public bool MaskDisabled => Layer.Mask is { } mask && !mask.Applies;

    /// <summary>Painted coverage hides instead of shows.</summary>
    public bool MaskInverted => Layer.Mask is { } mask && mask.IsInverted;

    /// <summary>Strokes are landing on this layer's mask right now.</summary>
    public bool IsEditingMask => _owner.IsEditingMaskOf(Layer);

    /// <summary>Composites only where the layer below has content.</summary>
    public bool IsClipped => Layer.IsClipped;

    /// <summary>No mask yet — the menu offers the two ways to add one.</summary>
    public bool CanAddMask => !HasMask;

    /// <summary>A mask that currently applies — the menu offers to disable it.</summary>
    public bool CanDisableMask => HasMask && !MaskDisabled;

    /// <summary>Whether the layer carries an effect stack (the docker's fx chip).</summary>
    public bool HasEffects => Layer.Effects is not null;

    /// <summary>The stack exists but its master switch is off — the chip goes hollow (Q158).</summary>
    public bool EffectsDisabled => Layer.Effects is { Disabled: true };

    /// <summary>A stack that currently runs — the menu offers to switch it off.</summary>
    public bool CanDisableEffects => HasEffects && !EffectsDisabled;

    /// <summary>Re-read the mask, clip and effect properties after an edit.</summary>
    internal void SyncMaskFromModel()
    {
        OnPropertyChanged(nameof(HasMask));
        OnPropertyChanged(nameof(MaskDisabled));
        OnPropertyChanged(nameof(MaskInverted));
        OnPropertyChanged(nameof(IsEditingMask));
        OnPropertyChanged(nameof(IsClipped));
        OnPropertyChanged(nameof(CanAddMask));
        OnPropertyChanged(nameof(CanDisableMask));
        OnPropertyChanged(nameof(HasEffects));
        OnPropertyChanged(nameof(EffectsDisabled));
        OnPropertyChanged(nameof(CanDisableEffects));
    }

    /// <summary>Inside a layer folder (indented in the docker, eject button shown).</summary>
    public bool IsGrouped => Layer.GroupId is not null;

    /// <summary>In a link — the docker marks it, or the artist cannot tell it is one drawing.</summary>
    public bool IsLinked => Layer.LinkId is not null;

    /// <summary>The link's accent colour, for the docker's marker.</summary>
    public string LinkColor => _owner.LinkColorOf(Layer);

    /// <summary>The bracket's brush.</summary>
    /// <remarks>
    /// Immutable, because a docker row's brush is read on the render thread
    /// and a mutable <c>SolidColorBrush</c> is thread-affine — the failure
    /// <c>TrackView</c> already produced as cross-thread test flake.
    /// </remarks>
    public Avalonia.Media.IBrush LinkBrush =>
        new Avalonia.Media.Immutable.ImmutableSolidColorBrush(
            Avalonia.Media.Color.Parse(LinkColor));

    /// <summary>What this layer's strokes follow, said out loud on the row.</summary>
    /// <remarks>
    /// Empty on an unrigged layer, so the docker shows nothing rather than the
    /// word "none" on every row of every document that never rigged.
    /// </remarks>
    public string RigBadge => _owner.RigBadgeOf(Layer);

    /// <summary>Whether the row has anything to say about the rig.</summary>
    public bool HasRigBadge => RigBadge.Length > 0;

    /// <summary>Which piece of the link bracket this row draws.</summary>
    public LayerLinkMark LinkMark => _owner.LinkMarkOf(Layer);

    /// <summary>The bracket's corner, running down to the next member.</summary>
    public bool IsLinkTop => LinkMark == LayerLinkMark.Top;

    /// <summary>The bracket's line, with a tick into this row.</summary>
    public bool IsLinkMiddle => LinkMark == LayerLinkMark.Middle;

    /// <summary>The bracket coming down into its last corner.</summary>
    public bool IsLinkBottom => LinkMark == LayerLinkMark.Bottom;

    /// <summary>In a link whose other members are not adjacent — a tick, joining nothing.</summary>
    public bool IsLinkDetached => LinkMark == LayerLinkMark.Detached;

    /// <summary>Re-read the link-derived properties after a link edit.</summary>
    internal void SyncLinkFromModel()
    {
        OnPropertyChanged(nameof(IsLinked));
        OnPropertyChanged(nameof(LinkMark));
        OnPropertyChanged(nameof(IsLinkTop));
        OnPropertyChanged(nameof(IsLinkMiddle));
        OnPropertyChanged(nameof(IsLinkBottom));
        OnPropertyChanged(nameof(IsLinkDetached));
        OnPropertyChanged(nameof(LinkColor));
        OnPropertyChanged(nameof(LinkBrush));
        OnPropertyChanged(nameof(RigBadge));
        OnPropertyChanged(nameof(HasRigBadge));
        OnPropertyChanged(nameof(Visible));
    }

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
        OnPropertyChanged(nameof(IsGrouped));
        SyncLinkFromModel();
        SyncMaskFromModel();
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
