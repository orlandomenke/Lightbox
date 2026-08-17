using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.App.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q78, which was 13,628 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- channels -----------------------------------------------------------

    /// <summary>
    /// Which channel the canvas is soloing, if any. View-only (invariant 5):
    /// the canvas control reads it, the record never hears about it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRedSolo))]
    [NotifyPropertyChangedFor(nameof(IsGreenSolo))]
    [NotifyPropertyChangedFor(nameof(IsBlueSolo))]
    [NotifyPropertyChangedFor(nameof(IsAlphaSolo))]
    private ChannelSolo _channelSolo = ChannelSolo.None;

    public bool IsRedSolo => ChannelSolo == ChannelSolo.Red;

    public bool IsGreenSolo => ChannelSolo == ChannelSolo.Green;

    public bool IsBlueSolo => ChannelSolo == ChannelSolo.Blue;

    public bool IsAlphaSolo => ChannelSolo == ChannelSolo.Alpha;

    /// <summary>Solo this channel, or un-solo it if it already is — one click in, one click out.</summary>
    [RelayCommand]
    private void ToggleChannelSolo(ChannelSolo channel) =>
        ChannelSolo = ChannelSolo == channel ? ChannelSolo.None : channel;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _channelThumbRed;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _channelThumbGreen;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _channelThumbBlue;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _channelThumbAlpha;

    /// <summary>
    /// True when the Channels panel is the tab actually showing — not merely
    /// resting behind Color in the colour family's strip.
    /// </summary>
    private bool ChannelsTabShowing
    {
        get
        {
            var layout = Workspace.Layout;
            return layout.IsVisible(Docking.DockPanelId.Channels)
                && layout.ActiveOf(layout.SlotOf(Docking.DockPanelId.Channels)) == Docking.DockPanelId.Channels;
        }
    }

    /// <summary>
    /// Redraw the Channels panel's four thumbnails from what the canvas
    /// currently shows. Gated on the tab actually showing, because the
    /// composite below materialises a document-sized bitmap — a price the
    /// default layout must not pay for a tab nobody has brought forward
    /// (the unbounded-canvas budget is the test that caught this).
    /// </summary>
    private void RefreshChannelThumbs()
    {
        if (!ChannelsTabShowing) return;
        using var composite = CompositeVisibleLayers();
        ChannelThumbRed = ThumbnailRenderer.RenderChannel(composite, ChannelSolo.Red);
        ChannelThumbGreen = ThumbnailRenderer.RenderChannel(composite, ChannelSolo.Green);
        ChannelThumbBlue = ThumbnailRenderer.RenderChannel(composite, ChannelSolo.Blue);
        ChannelThumbAlpha = ThumbnailRenderer.RenderChannel(composite, ChannelSolo.Alpha);
    }

    // ---- layer reordering -------------------------------------------------------

    /// <summary>
    /// Move a layer toward the viewer (+1) or away (−1), keeping it active —
    /// or the whole selection when this layer is inside it, as one step.
    /// </summary>
    internal void MoveLayer(LayerRow row, int delta)
    {
        var id = row.Layer.Id;
        var targets = LayersForOp(row.Layer);
        if (targets.Count == 1)
        {
            _editor.MoveLayer(id, delta);
        }
        else
        {
            var ids = targets.Select(l => l.Id).ToHashSet();
            _editor.Perform(
                doc => ShiftLayers(doc.Scene.Layers, ids, delta), label: "Move layers",
                frameContentUnchanged: true);
        }
        // Keeps the selection: moving a stack of layers is something an artist
        // does twice, and a reorder that dissolved the selection would make the
        // second press move one layer out of the group it just moved with.
        ActivateWithinSelection(Scene.Layers.FindIndex(l => l.Id == id));
    }

    /// <summary>
    /// Whether a drop may go ahead, given the paper.
    /// </summary>
    /// <remarks>
    /// The paper is the desk the drawings lie on, not one of them: it is the
    /// bottom of the stack and locked, which is what makes everything paint
    /// over it (<c>BackgroundLayerTests</c>). The ▲/▼ buttons never threatened
    /// that — they step one place and the paper is already at the end — but a
    /// drag can drop anything anywhere, so the rule has to be said out loud
    /// here. Refusing silently rather than clamping: a drop that quietly did
    /// something other than what the pointer said would be worse than one that
    /// does nothing, and the row does not move under the cursor to suggest it.
    /// </remarks>
    private bool CanReorderPastPaper(Layer dragged, Layer target, bool above)
    {
        if (dragged.IsBackground)
        {
            AiStatus = $"“{dragged.Name}” is the paper — it stays at the bottom of the stack.";
            return false;
        }
        // Landing under the paper is the same displacement seen from the other
        // side, and it is the one an artist reaches by dragging *down*.
        if (target.IsBackground && !above)
        {
            AiStatus = $"“{target.Name}” is the paper — nothing goes under it.";
            return false;
        }
        return true;
    }

    [RelayCommand]
    private void MoveLayerUp(LayerRow row) => MoveLayer(row, +1);

    [RelayCommand]
    private void MoveLayerDown(LayerRow row) => MoveLayer(row, -1);

    /// <summary>
    /// Drop a dragged layer beside another row — visually above it (toward the
    /// viewer) or below. The layer adopts the target's folder, so dragging into
    /// a folder's block joins the folder and dragging out to a loose row leaves
    /// it. One undo step.
    /// </summary>
    /// <remarks>
    /// The panel lists topmost first while <c>Scene.Layers</c> stores bottom
    /// first, so "above the target on screen" is "after the target in the
    /// list" — the same inversion the ▲/▼ buttons already encode as +1 up.
    /// </remarks>
    internal void DropLayerOnRow(LayerRow draggedRow, LayerRow targetRow, bool above)
    {
        var dragged = draggedRow.Layer;
        var target = targetRow.Layer;
        if (dragged.Id == target.Id) return;
        if (!CanReorderPastPaper(dragged, target, above)) return;
        _editor.Perform(doc =>
        {
            var layers = doc.Scene.Layers;
            var from = layers.FindIndex(l => l.Id == dragged.Id);
            if (from < 0) return;
            var layer = layers[from];
            layers.RemoveAt(from);
            var at = layers.FindIndex(l => l.Id == target.Id);
            if (at < 0)
            {
                layers.Insert(from, layer); // target vanished mid-drag: put it back
                return;
            }
            layers.Insert(above ? at + 1 : at, layer);
            layer.GroupId = target.GroupId;
        }, label: "Move layer", frameContentUnchanged: true);
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == dragged.Id);
    }

    // ---- layer folders ----------------------------------------------------------

    /// <summary>The docker's item list: folder headers followed by their (uncollapsed) member rows.</summary>
    public ObservableCollection<object> LayerPanelItems { get; } = [];

    private void RebuildLayerPanel()
    {
        LayerPanelItems.Clear();
        var emitted = new HashSet<string>();
        foreach (var row in LayerRows) // topmost first
        {
            var group = Scene.GroupOf(row.Layer);
            if (group is null)
            {
                LayerPanelItems.Add(row);
                continue;
            }
            if (emitted.Add(group.Id)) LayerPanelItems.Add(new GroupRow(this, group));
            if (!group.Collapsed) LayerPanelItems.Add(row);
        }
    }

    /// <summary>New folder containing the active layer — or every selected layer.</summary>
    [RelayCommand]
    private void CreateLayerFolder()
    {
        var ids = LayersForOp(ActiveLayer).Select(l => l.Id).ToHashSet();
        _editor.Perform(doc =>
        {
            var group = new LayerGroup { Name = $"Folder {doc.Scene.LayerGroups.Count + 1}" };
            doc.Scene.LayerGroups.Add(group);
            foreach (var layer in doc.Scene.Layers.Where(l => ids.Contains(l.Id))) layer.GroupId = group.Id;
        }, label: ids.Count == 1 ? "Create layer folder" : "Create folder from layers");
    }

    /// <summary>Put the active layer into this folder (moved adjacent so the folder stays one block).</summary>
    [RelayCommand]
    private void AddActiveLayerToGroup(GroupRow header) => MoveLayerIntoGroup(ActiveLayer, header.Group);

    /// <summary>
    /// Put a layer into a folder, moved to the top of the folder's block so the
    /// folder stays contiguous. Shared by the header's ＋ button and dropping a
    /// dragged row on the header.
    /// </summary>
    internal void MoveLayerIntoGroup(Layer layer, LayerGroup group)
    {
        if (layer.GroupId == group.Id) return;
        if (layer.IsBackground)
        {
            // Filing the paper into a folder moves it up the stack to join that
            // folder's block, which is the same displacement CanReorderPastPaper
            // refuses by another route.
            AiStatus = $"“{layer.Name}” is the paper — it stays at the bottom of the stack.";
            return;
        }
        _editor.Perform(doc =>
        {
            var layers = doc.Scene.Layers;
            layers.Remove(layer);
            var top = -1;
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].GroupId == group.Id) top = i;
            }
            if (top < 0)
            {
                layers.Add(layer); // empty folder: just append
            }
            else
            {
                layers.Insert(top + 1, layer); // top of the folder's block
            }
            layer.GroupId = group.Id;
        });
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == layer.Id);
    }

    /// <summary>Take a layer out of its folder (placed just above the folder's block).</summary>
    [RelayCommand]
    private void RemoveLayerFromGroup(LayerRow row)
    {
        var layer = row.Layer;
        if (layer.GroupId is null) return;
        var groupId = layer.GroupId;
        _editor.Perform(doc =>
        {
            var layers = doc.Scene.Layers;
            layers.Remove(layer);
            var top = -1;
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].GroupId == groupId) top = i;
            }
            layers.Insert(top < 0 ? layers.Count : top + 1, layer);
            layer.GroupId = null;
        });
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == layer.Id);
    }

    /// <summary>Dissolve the folder: its layers stay, ungrouped.</summary>
    [RelayCommand]
    private void DissolveGroup(GroupRow header)
    {
        _editor.Perform(doc =>
        {
            foreach (var layer in doc.Scene.Layers)
            {
                if (layer.GroupId == header.Group.Id) layer.GroupId = null;
            }
            doc.Scene.LayerGroups.RemoveAll(g => g.Id == header.Group.Id);
        });
    }

    internal void CommitGroupRename(LayerGroup group, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || group.Name == trimmed) return;
        _editor.Perform(_ => group.Name = trimmed, frameContentUnchanged: true);
    }

    internal void SetGroupVisible(LayerGroup group, bool visible)
    {
        if (group.Visible == visible) return;
        _editor.Perform(_ => group.Visible = visible, frameContentUnchanged: true);
    }

    internal void SetGroupColor(LayerGroup group, string color)
    {
        if (group.Color == color) return;
        _editor.Perform(_ => group.Color = color, frameContentUnchanged: true);
    }

    /// <summary>Collapse is a view preference: persisted, but not an undo step.</summary>
    internal void SetGroupCollapsed(LayerGroup group, bool collapsed)
    {
        if (group.Collapsed == collapsed) return;
        group.Collapsed = collapsed;
        MarkDocumentEdited();
        RebuildLayerPanel();
    }

    private void RefreshRangeHighlights()
    {
        var rangeSet = PlaybackStartFrame >= 0 || PlaybackEndFrame >= 0;
        var start = EffectiveStartFrame;
        var end = EffectiveEndFrame;
        foreach (var row in LayerRows)
        {
            foreach (var cell in row.Cells)
            {
                cell.OutOfRange = rangeSet && !cell.IsVirtual && (cell.Index < start || cell.Index > end);
            }
        }
    }

    [RelayCommand]
    private void AddFrame()
    {
        _editor.AddFrameAfter(CurrentFrameIndex);
        CurrentFrameIndex++;
    }

    [RelayCommand]
    private void DuplicateFrame()
    {
        _editor.DuplicateFrame(CurrentFrameIndex);
        CurrentFrameIndex++;
    }

    [RelayCommand]
    private void DeleteFrame()
    {
        if (Scene.FrameCount <= 1) return;
        _editor.DeleteFrame(CurrentFrameIndex);
        CurrentFrameIndex = Math.Min(CurrentFrameIndex, Scene.FrameCount - 1);
    }

    [RelayCommand]
    private void Undo()
    {
        // The stroke the next Shift+click would have joined to may be the one
        // going away. Joining to a mark that is no longer there draws a line
        // out of nowhere, which is worse than not joining at all.
        _lastStrokeEnd = null;
        // An uncommitted palette edit is not on the stack yet; undoing before
        // it lands would step over it and then have it reappear.
        CommitSwatchEdit();
        ApplyEditScope(WhileApplyingScope(_editor.UndoScoped));
    }

    [RelayCommand]
    private void Redo()
    {
        CommitSwatchEdit();
        ApplyEditScope(WhileApplyingScope(_editor.RedoScoped));
    }

    /// <summary>The History docker, built on first use and following the active tab.</summary>
    public UndoHistoryViewModel UndoHistory => _undoHistory ??= new(JumpToHistory);

    private UndoHistoryViewModel? _undoHistory;

    /// <summary>
    /// Stand the document at a history row's state — a multi-step undo or
    /// redo, through the same guards a single step takes, invalidating what
    /// the walked steps touched between them.
    /// </summary>
    private void JumpToHistory(long revision)
    {
        // Same two preconditions as Undo, for the same reasons.
        _lastStrokeEnd = null;
        CommitSwatchEdit();
        ApplyEditScope(WhileApplyingScope(() => _editor.JumpTo(revision)));
    }

    private DocumentEditor.EditScope WhileApplyingScope(Func<DocumentEditor.EditScope> step)
    {
        _applyingEditScope = true;
        try
        {
            return step();
        }
        finally
        {
            _applyingEditScope = false;
        }
    }


    private void ApplyEditScope(DocumentEditor.EditScope scope)
    {
        if (!scope.Any) return;
        if (scope.FrameId is { } frameId)
        {
            InvalidateFrameRender(frameId);
            _dirtyThumbIds.Add(frameId);
        }
        else if (scope.FrameContentUnchanged)
        {
            // Nothing to invalidate: the step moved the layer structure and no
            // drawing changed, so every cached frame bitmap and thumbnail is
            // still exactly right (B202). The canvas is still republished below,
            // because which layers composite and in what order *did* change.
        }
        else
        {
            ClearFrameRenders();
            _allThumbsDirty = true;
        }
        ClampCurrentFrame(publishIfUnchanged: false);
        PublishSnapshot();
        RefreshThumbnails();
    }

    /// <summary>
    /// Adds a drawing layer. There is one kind of layer to add.
    /// </summary>
    /// <remarks>
    /// This was three commands and a dropdown — <c>AddPaintedLayer</c>,
    /// <c>AddVectorLayer</c> and an <c>AddLayerOfSelectedKind</c> reading a
    /// Raster/Vector picker. The choice never meant anything an artist could act
    /// on: both kinds drew the same strokes through the same engine, and picking
    /// Vector only subtracted the ability to hold imported pixels or a symbol
    /// placement. Asking the question up front made the artist guess at a
    /// limitation instead of choosing a capability. Q52.
    /// </remarks>
    [RelayCommand]
    private void AddPaintedLayer() => AddLayer(LayerKind.Painted);

    [RelayCommand]
    private void ToggleSidebar() => SidebarVisible = !SidebarVisible;

    [RelayCommand]
    private void SwitchSidebarSide() => SidebarOnRight = !SidebarOnRight;

    [RelayCommand]
    private void ToggleTimeline() => TimelineVisible = !TimelineVisible;

    [RelayCommand]
    private void ActivateLayer(LayerRow row) => ActiveLayerIndex = row.SceneIndex;

    /// <summary>Rename as one undoable step (called by the row on commit).</summary>
    internal void CommitLayerRename(LayerRow row, string name)
    {
        var layer = row.Layer;
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            // Snap the row back to the document name instead of storing a blank.
            row.SyncFromModel(layer, row.SceneIndex);
            return;
        }
        if (layer.Name == trimmed) return;
        _editor.Perform(_ => layer.Name = trimmed, frameContentUnchanged: true);
    }

    /// <summary>
    /// The layers this toggle covers and does not already agree with. Empty
    /// means nothing to do, which is what keeps a no-op off the undo stack.
    /// </summary>
    /// <param name="alone">
    /// Cover only <paramref name="layer"/>, whatever is selected. For the
    /// entry points that name the active layer — the shortcut bar, the
    /// keyboard, the menu — because a control that says "the active layer"
    /// has to mean it. The docker's own row toggles pass false and follow
    /// the selection, which is what the selection is for.
    /// </param>
    private List<Layer> ToggleTargets(Layer layer, Func<Layer, bool> current, bool value, bool alone = false) =>
        (alone ? [layer] : LayersForOp(layer)).Where(l => current(l) != value).ToList();

    internal void SetLayerVisible(Layer layer, bool visible, bool alone = false)
    {
        var targets = ToggleTargets(layer, l => l.Visible, visible, alone);
        if (targets.Count == 0) return;
        _editor.Perform(_ =>
        {
            foreach (var target in targets) target.Visible = visible;
        }, label: targets.Count == 1 ? "Set layer visible" : "Set layers visible",
            frameContentUnchanged: true);
    }

    /// <summary>
    /// Undoable, like visibility — locking is a document decision an artist
    /// can change their mind about, not a view preference.
    /// </summary>
    internal void SetLayerLocked(Layer layer, bool locked, bool alone = false)
    {
        var targets = ToggleTargets(layer, l => l.Locked, locked, alone);
        if (targets.Count == 0) return;
        _editor.Perform(_ =>
        {
            foreach (var target in targets) target.Locked = locked;
        }, label: targets.Count == 1 ? "Set layer locked" : "Set layers locked",
            frameContentUnchanged: true);
        NotifyLayerGating();
    }

    internal void SetLayerAlphaLocked(Layer layer, bool locked, bool alone = false)
    {
        var targets = ToggleTargets(layer, l => l.AlphaLocked, locked, alone);
        if (targets.Count == 0) return;
        _editor.Perform(_ =>
        {
            foreach (var target in targets) target.AlphaLocked = locked;
        }, label: targets.Count == 1 ? "Set layer alpha locked" : "Set layers alpha locked",
            frameContentUnchanged: true);
        NotifyLayerGating();
    }

    /// <summary>Locking a folder locks every layer inside it.</summary>
    internal void SetGroupLocked(LayerGroup group, bool locked)
    {
        if (group.Locked == locked) return;
        _editor.Perform(_ => group.Locked = locked, frameContentUnchanged: true);
        SyncLayerRows();
        NotifyLayerGating();
    }

    /// <summary>Lock or unlock the active layer (keyboard and menu path).</summary>
    [RelayCommand]
    private void ToggleActiveLayerLocked()
    {
        if (ActiveLayer is { } layer) SetLayerLocked(layer, !layer.Locked, alone: true);
    }

    [RelayCommand]
    private void ToggleActiveLayerAlphaLocked()
    {
        if (ActiveLayer is { } layer) SetLayerAlphaLocked(layer, !layer.AlphaLocked, alone: true);
    }

    private void NotifyLayerGating()
    {
        OnPropertyChanged(nameof(ActiveLayerBlocked));
        OnPropertyChanged(nameof(ActiveLayerAlphaLocked));
    }

    /// <summary>A row needs this to dim itself without reaching into the scene.</summary>
    internal bool IsLayerLockedByFolder(Layer layer) => Scene.GroupOf(layer) is { Locked: true };

    /// <summary>Shown in the tool options so the restriction is never invisible.</summary>
    public bool ActiveLayerAlphaLocked => ActiveLayer is { AlphaLocked: true };

    /// <summary>
    /// Per-layer onion-skin participation. A display preference, so it is
    /// persisted (autosave) but deliberately not an undo step.
    /// </summary>
    /// <remarks>
    /// <b>One layer, even with several selected</b>, unlike the eye and the two
    /// locks beside it. Those describe the drawing — hidden, locked, editable —
    /// and an artist who picked five layers meant all five. This describes what
    /// they are <em>looking through</em> while working on one of them, and the
    /// arrangement of ghosts is something you tune per layer as you go: the
    /// background stays off, the layer under the hand stays on. Sweeping it
    /// across a selection would clear an arrangement that took a while to set up
    /// and gives nothing back, because there is no bulk onion job to do.
    /// </remarks>
    internal void SetLayerOnionEnabled(Layer layer, bool enabled)
    {
        if (layer.OnionEnabled == enabled) return;
        layer.OnionEnabled = enabled;
        // The same switch exists in two places — the timeline's layer column ◉
        // and the shortcut bar's — and they have to agree. The row does not read
        // the layer except when it is rebuilt, so pushing the value across is
        // what stops one of them showing yesterday's answer.
        if (LayerRows.FirstOrDefault(r => r.Layer.Id == layer.Id) is { } row)
        {
            row.OnionEnabled = enabled;
        }
        if (layer.Id == ActiveLayer.Id) OnPropertyChanged(nameof(ActiveLayerOnion));
        MarkDocumentEdited();
        PublishSnapshot();
    }

    // ---- active layer compositing (opacity + blend mode) ----------------------

    public IReadOnlyList<LayerBlendMode> BlendModeChoices { get; } = Enum.GetValues<LayerBlendMode>();

    /// <summary>
    /// Active layer's opacity, 0–100 for the docker slider. Applied live while
    /// dragging, so deliberately not an undo step (an undo snapshot per slider
    /// tick would flood the history).
    /// </summary>
    public double ActiveLayerOpacity
    {
        get => Math.Round(ActiveLayer.Opacity * 100);
        set
        {
            var clamped = Math.Clamp(value / 100.0, 0, 1);
            if (Math.Abs(ActiveLayer.Opacity - clamped) < 0.0005) return;
            ActiveLayer.Opacity = clamped;
            MarkDocumentEdited();
            OnPropertyChanged();
            PublishSnapshot();
        }
    }

    /// <summary>Active layer's blend mode — a deliberate compositing choice, one undo step.</summary>
    public LayerBlendMode ActiveLayerBlendMode
    {
        get => ActiveLayer.BlendMode;
        set
        {
            if (ActiveLayer.BlendMode == value) return;
            var layer = ActiveLayer;
            _editor.Perform(_ => layer.BlendMode = value, frameContentUnchanged: true);
            OnPropertyChanged();
        }
    }

    private void NotifyActiveLayerCompositing()
    {
        OnPropertyChanged(nameof(ActiveLayerOpacity));
        OnPropertyChanged(nameof(ActiveLayerBlendMode));
    }

    /// <summary>Delete the active layer; an empty document always regrows one blank layer.</summary>
    [RelayCommand]
    private void DeleteActiveLayer() => DeleteLayer(ActiveLayer);

    /// <summary>
    /// Delete a layer — or every selected layer when this one is inside the
    /// selection — as one undoable step.
    /// </summary>
    /// <remarks>
    /// A locked layer in the selection is skipped rather than taken as a refusal
    /// of the whole delete: <see cref="CanEdit"/> says which one and why, and the
    /// rest still go. Refusing all five because one is locked would make the
    /// artist unlock a layer only to delete it.
    /// </remarks>
    public void DeleteLayer(Layer layer)
    {
        var ids = LayersForOp(layer).Where(l => CanEdit(l, "delete it")).Select(l => l.Id).ToHashSet();
        if (ids.Count == 0) return;
        var removedIndex = Scene.Layers.FindIndex(l => ids.Contains(l.Id));
        if (removedIndex < 0) return;
        _editor.Perform(doc =>
        {
            var scene = doc.Scene;
            var wasPaper = scene.Layers.Any(l => ids.Contains(l.Id) && l.IsBackground);
            scene.Layers.RemoveAll(l => ids.Contains(l.Id));
            // Deleting the paper means there is no paper. Without this the
            // composite falls back to clearing to the scene's colour, so the
            // canvas goes opaque white and the deletion looks like it did
            // nothing — the one thing it must not look like.
            if (wasPaper && !scene.Layers.Exists(l => l.IsBackground))
            {
                scene.TransparentBackground = true;
            }
            // Regrow when nothing PAINTABLE is left, not merely when nothing is
            // left: a document down to its locked paper has layers and still
            // nowhere to draw.
            if (!scene.Layers.Any(l => !l.IsBackground))
            {
                var fresh = new Layer
                {
                    Name = "Paint 1",
                    Cels = [new Cel { Frame = new Frame() }],
                };
                while (fresh.Cels.Count < scene.FrameCount) fresh.Cels.Add(new Cel());
                scene.Layers.Add(fresh);
            }
        });
        var next = Math.Clamp(removedIndex, 0, Scene.Layers.Count - 1);
        // Never land on the paper: it is locked, so the next stroke would bounce.
        ActiveLayerIndex = Scene.Layers[next].IsBackground ? FirstPaintableLayer(Doc) : next;
    }

    /// <summary>Blank the active layer: every drawing on it loses its content, the timing stays.</summary>
    [RelayCommand]
    private void ClearActiveLayer() => ClearLayerContent(ActiveLayer);

    /// <summary>Merge the active layer into the one below it (Ctrl+E).</summary>
    [RelayCommand]
    private void MergeActiveLayerDown() => MergeLayerDown(ActiveLayer);

    /// <summary>
    /// The layer a merge-down would land on, or null when nothing is below.
    /// A null <paramref name="layer"/> means the active one, here and on the
    /// two methods below, so the shortcut can ask without the window holding
    /// a layer reference.
    /// </summary>
    public Layer? MergeTargetOf(Layer? layer)
    {
        layer ??= ActiveLayer;
        var index = Scene.Layers.FindIndex(l => l.Id == layer.Id);
        return index > 0 ? Scene.Layers[index - 1] : null;
    }

    /// <summary>
    /// Would merging this layer down turn any drawing into pixels? Feeds the
    /// Q52 warning, which the window shows before calling
    /// <see cref="MergeLayerDown"/> — and only when AI is enabled, because
    /// "the inbetweener cannot read pixels" is noise to an artist without one.
    /// </summary>
    public bool MergeWouldBake(Layer? layer)
    {
        layer ??= ActiveLayer;
        return MergeTargetOf(layer) is { } below
            && Lightbox.Raster.LayerMerge.WouldBakePixels(layer, below);
    }

    /// <summary>
    /// Merge a layer into the one below it, drawing by drawing along the
    /// exposure sheet. One undo step; the merged layer keeps the lower
    /// layer's name, opacity, blend mode and folder.
    /// </summary>
    public void MergeLayerDown(Layer? layer)
    {
        layer ??= ActiveLayer;
        if (MergeTargetOf(layer) is not { } below)
        {
            AiStatus = $"Nothing below “{layer.Name}” to merge into.";
            return;
        }
        if (!CanEdit(layer, "merge it down") || !CanEdit(below, "merge into it")) return;
        var targetIndex = Scene.Layers.FindIndex(l => l.Id == below.Id);
        _editor.Perform(doc =>
        {
            var scene = doc.Scene;
            var upperIndex = scene.Layers.FindIndex(l => l.Id == layer.Id);
            if (upperIndex <= 0) return;
            Lightbox.Raster.LayerMerge.MergeDown(
                scene, scene.Layers[upperIndex], scene.Layers[upperIndex - 1]);
            scene.Layers.RemoveAt(upperIndex);
        });
        ActiveLayerIndex = targetIndex;
        AiStatus = $"Merged “{layer.Name}” into “{below.Name}”.";
    }

    /// <summary>
    /// Set a project document's status, and export it if that is what the artist asked
    /// the app to do on that status.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shortcut through the whole export pillar: finish an asset, mark it Ready, and
    /// the sheet lands where the engine is already looking. Nobody has to remember to
    /// export — and the export nobody remembered is the one that makes a designer think
    /// the artist has not started.
    /// </para>
    /// <para>
    /// <b>The order is load-bearing. The status is written and saved first; the export is
    /// a consequence.</b> If the destination is missing, locked by the engine, or on a
    /// drive that is not mounted, the artist gets a message and keeps their status.
    /// Refusing the status change because a file could not be written would make a
    /// production field hostage to a network share.
    /// </para>
    /// <para>
    /// The document is read from the open tab when there is one, so an unsaved edit
    /// exports as the artist sees it rather than as the file last had it. Otherwise it
    /// comes off disk, which is the point of statuses living on the manifest: marking
    /// something Ready never needed it open.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Where a project row's document lives, and whether the file is behind the edits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The facts <see cref="Services.SaveRequirement"/> needs, for <em>this row</em> rather
    /// than for whatever tab happens to be in front. Marking a row Ready is a statement
    /// about that row's drawing, and gating it on the active tab would ask about the wrong
    /// file — which is exactly the kind of near-miss that reads as the app being random.
    /// </para>
    /// <para>
    /// A row whose document is open answers from the tab, because that is where the unsaved
    /// edits are. Otherwise the answer is the project's own path for it: a freshly added
    /// animation has a <c>DocumentRef</c> before it has a file, so this returns a path that
    /// does not exist yet and the gate reports it honestly.
    /// </para>
    /// </remarks>
    public (string? FilePath, bool HasUnsavedEdits) SaveFactsFor(ProjectRow row)
    {
        if (row.Animation is not { } reference) return (null, false);

        if (Tabs.FirstOrDefault(t => t.Source?.Id == reference.Id) is { } open)
        {
            return (open.FilePath ?? Resolve(reference), open.IsDirty);
        }
        // Not open, so nothing can be unsaved about it beyond whether it was ever written.
        return (Resolve(reference), false);

        string? Resolve(DocumentRef r) =>
            ProjectDocker.RootPath is { Length: > 0 } root && r.Path is { Length: > 0 }
                ? System.IO.Path.Combine(root, r.Path.Replace('/', System.IO.Path.DirectorySeparatorChar))
                : null;
    }

    public void SetProjectStatus(ProjectRow row, AssetStatus? status)
    {
        if (row.Animation is not { } reference) return;

        var before = ProjectDocker.SetStatus(row, status);
        if (status is not { } now) return;

        var settings = Settings.AutoExport;
        var root = ProjectDocker.RootPath;

        // Decided before anything is loaded, so the ordinary case — auto-export off —
        // costs a comparison rather than reading a document off disk.
        var (folder, outcome) = AutoExport.Decide(before, now, settings, root);
        if (folder is null)
        {
            if (AutoExport.Explain(outcome, settings) is { Length: > 0 } why) AiStatus = why;
            return;
        }

        var doc = Tabs.FirstOrDefault(t => t.Source?.Id == reference.Id)?.Editor.Doc
                  ?? (ProjectDocker.Project is { } project
                      ? ProjectIo.LoadDocument(project, reference)
                      : null);
        if (doc is null)
        {
            AiStatus = $"Status saved, but {reference.Name} could not be read to export it.";
            return;
        }

        var report = AutoExport.Run(
            doc, reference.Name, before, now, settings, ProjectDocker.RootPath, ExportPresets());
        if (report.Message is { Length: > 0 }) AiStatus = report.Message;
    }

    /// <summary>Built-in export presets plus the artist's own.</summary>
    private static List<ExportPreset> ExportPresets() =>
        ExportPreset.BuiltIns.Concat(ExportPresetStore.Load()).ToList();

    /// <summary>
    /// One status line for a finished export, including what it left out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The omissions are in the sentence rather than behind a dialog, and that is the
    /// point of reporting them at all: "a layer I wanted is missing" is invisible in
    /// the sheet and surfaces in the engine, on a build, days later. Naming the layers
    /// is what turns that into something an artist notices now.
    /// </para>
    /// <para>
    /// Names, not a count. "2 layers left out" is exactly as unhelpful as silence when
    /// the question is <em>which</em>.
    /// </para>
    /// </remarks>
    public string DescribeExport(Lightbox.App.Services.ExportRun run)
    {
        var parts = new List<string> { run.Summary };

        if (run.Omitted.Count > 0)
        {
            var named = run.Omitted.Select(o => $"{o.Name} ({Reason(o.Signal)})");
            parts.Add("left out: " + string.Join(", ", named));
        }
        if (run.Suspected.Count > 0)
        {
            parts.Add("kept but looks like a background: "
                      + string.Join(", ", run.Suspected.Select(s => s.Name)));
        }
        return string.Join(" — ", parts);

        static string Reason(Lightbox.Core.Export.BackgroundSignal signal) => signal switch
        {
            Lightbox.Core.Export.BackgroundSignal.Pinned => "never export",
            Lightbox.Core.Export.BackgroundSignal.Paper => "paper",
            Lightbox.Core.Export.BackgroundSignal.Hidden => "hidden",
            _ => "fills the canvas",
        };
    }

    /// <summary>
    /// Pin a layer out of exports, into them, or back to letting the export decide.
    /// </summary>
    /// <param name="pin">
    /// <c>true</c> never export, <c>false</c> always export, <c>null</c> let
    /// <see cref="Lightbox.Core.Export.BackgroundHandling"/> decide.
    /// </param>
    /// <remarks>
    /// Through the editor, so it is one undo step and marks the document dirty — this
    /// changes what leaves the app, which makes it document state rather than a
    /// preference. No cache invalidation and no thumbnail refresh: it reaches the
    /// export and never a pixel on the canvas.
    /// </remarks>
    public void SetLayerExportPin(Layer layer, bool? pin)
    {
        var ids = LayersForOp(layer).Where(l => l.OmitFromExport != pin).Select(l => l.Id).ToHashSet();
        if (ids.Count == 0) return;
        _editor.Perform(doc =>
        {
            foreach (var target in doc.Scene.Layers.Where(l => ids.Contains(l.Id)))
                target.OmitFromExport = pin;
        }, label: ids.Count == 1 ? "Set layer export pin" : "Set layer export pins",
            frameContentUnchanged: true);
        SyncLayerRows();
    }

    public void ClearLayerContent(Layer layer)
    {
        var ids = LayersForOp(layer).Select(l => l.Id).ToHashSet();
        // Mark before the edit so the thumbnail refresh inside Changed sees them.
        foreach (var target in Scene.Layers.Where(l => ids.Contains(l.Id)))
        {
            foreach (var cel in target.Cels)
            {
                if (cel.Frame is { } frame)
                {
                    InvalidateFrameRender(frame.Id);
                    _dirtyThumbIds.Add(frame.Id);
                }
            }
        }
        _editor.Perform(doc =>
        {
            foreach (var target in doc.Scene.Layers.Where(l => ids.Contains(l.Id)))
            {
                foreach (var cel in target.Cels)
                {
                    if (cel.Frame is not { } frame) continue;
                    frame.Strokes.Clear();
                    // Null rather than "": clearing a layer should leave frames that
                    // write no baseline key, the same as ones that never had one.
                    frame.PngBase64 = null;
                }
            }
        }, label: ids.Count == 1 ? "Clear layer content" : "Clear layers content");
    }

    /// <remarks>
    /// <paramref name="kind"/> is still written, because <c>Layer.Kind</c> is kept
    /// as provenance — it records where a layer's content came from, and every
    /// layer created in the app comes from the same place. It no longer picks a
    /// frame class, because there is one.
    /// </remarks>
    private void AddLayer(LayerKind kind)
    {
        _editor.Perform(doc =>
        {
            var layer = new Layer
            {
                Name = $"Paint {doc.Scene.Layers.Count + 1}",
                Kind = kind,
                Cels = [new Cel { Frame = new Frame() }],
            };
            while (layer.Cels.Count < doc.Scene.FrameCount) layer.Cels.Add(new Cel());
            doc.Scene.Layers.Add(layer);
        }, frameContentUnchanged: true);
        ActiveLayerIndex = Scene.Layers.Count - 1;
    }

    [RelayCommand]
    private void ToggleActiveLayerVisible()
    {
        _editor.Perform(_ => ActiveLayer.Visible = !ActiveLayer.Visible, frameContentUnchanged: true);
        RefreshPointerIntent();
    }

    /// <summary>Clicking a cel selects both the frame and the layer it belongs to.</summary>
    [RelayCommand]
    private void SelectFrame(FrameCell cell)
    {
        if (cell.LayerIndex >= 0 && cell.LayerIndex < Scene.Layers.Count)
            ActiveLayerIndex = cell.LayerIndex;
        CurrentFrameIndex = cell.Index;
        ClearCelRange();

        // Q103. A hatched cell can be stood on and cannot be *selected as a cel*,
        // and the anchor is where that line is drawn: there is no cel out there
        // to copy, cut, delete or range from, so leaving the anchor unset keeps
        // a following Shift+click from building a range over frames that do not
        // exist. Standing on it still authors nothing — the scene grows on the
        // first edit, not on arriving.
        if (cell.IsVirtual) return;
        _celAnchor = (cell.LayerIndex, cell.Index);
    }

    // ---- thumbnails ----------------------------------------------------------


    /// <summary>
    /// The bitmap a thumbnail shrinks from: the full-size cached render, so the
    /// thumbnail rides a bitmap the canvas needs anyway.
    /// </summary>
    private SKBitmap ThumbSource(Frame frame, int celIndex) =>
        _cache.Get(frame, Scene.Width, Scene.Height, celIndex: celIndex);

    /// <summary>How the thumbnail cache is doing — B202's guard reads this.</summary>
    internal (int Hits, int Renders, int Count) ThumbnailTraffic =>
        (_thumbs.Hits, _thumbs.Renders, _thumbs.Count);

    /// <summary>
    /// Update timeline thumbnails lazily: only cells whose keyed frame is new,
    /// changed, or explicitly marked dirty are re-rendered.
    /// </summary>
    private void RefreshThumbnails()
    {
        foreach (var row in LayerRows)
        {
            foreach (var cell in row.Cells)
            {
                var frame = ExposureSheet.FrameAtExactIndex(row.Layer, cell.Index);
                if (frame is null)
                {
                    cell.Thumb = null;
                    cell.ThumbFrameId = null;
                    continue;
                }
                // The cell's remembered id says only whether it is already
                // showing the right drawing; whether that drawing's thumbnail
                // has to be *made* is the cache's question now (B202). The two
                // used to be the same test, which is why re-pointing a row at
                // another layer re-rendered the whole timeline.
                // _allThumbsDirty still forces every cell through the cache: a
                // genuine document-wide change cleared it, so the lookup misses
                // and re-renders. What it no longer does is decide the render on
                // its own.
                if (!_allThumbsDirty && cell.ThumbFrameId == frame.Id && cell.Thumb is not null
                    && !_dirtyThumbIds.Contains(frame.Id)) continue;

                var index = cell.Index;
                cell.Thumb = _thumbs.Get(
                    frame.Id, () => ThumbnailRenderer.Render(ThumbSource(frame, index)));
                cell.ThumbFrameId = frame.Id;
            }
        }
        RefreshLayerThumbs();
        RefreshChannelThumbs();
        // The navigator's picture is the same fact at another size, so it is
        // refreshed on the same funnel and never on its own timer (Q110).
        RefreshNavigatorThumb();
        _dirtyThumbIds.Clear();
        _allThumbsDirty = false;
    }

    /// <summary>
    /// Layer-docker thumbnails show the exposed drawing at the playhead
    /// (holds resolve to the drawing they hold) over a checkerboard.
    /// Also called on playhead moves, where only rows whose exposed frame
    /// actually changed re-render.
    /// </summary>
    /// <summary>
    /// How many layer thumbnails have actually been rasterized, for the budget
    /// test (B152).
    /// </summary>
    /// <remarks>
    /// A count rather than a stopwatch, for B151's reason: the property is not
    /// "this got faster" but "this does not happen per tick", which is exact and
    /// cannot go flaky. Each one is a full-resolution frame render behind it, so
    /// the count is a fair proxy for the cost as well as for the rule.
    /// </remarks>
    internal int LayerThumbRenders { get; private set; }

    private void RefreshLayerThumbs()
    {
        foreach (var row in LayerRows)
        {
            var frame = ExposureSheet.ExposedFrame(row.Layer, CurrentFrameIndex);
            if (frame is null)
            {
                row.Thumb = null;
                row.ThumbFrameId = null;
                continue;
            }
            var stale = _allThumbsDirty
                        || row.ThumbFrameId != frame.Id
                        || _dirtyThumbIds.Contains(frame.Id);
            if (!stale && row.Thumb is not null) continue;

            var bmp = ThumbSource(frame, CurrentFrameIndex);
            row.Thumb = ThumbnailRenderer.RenderChecker(bmp, 44, 26);
            row.ThumbFrameId = frame.Id;
            LayerThumbRenders++;
        }
    }

    private void RefreshCellHighlights()
    {
        foreach (var row in LayerRows)
        {
            foreach (var cell in row.Cells) cell.IsCurrent = cell.Index == CurrentFrameIndex;
        }
    }

    // ---- onion skin -------------------------------------------------------------

    /// <summary>Onion skin as the artist has set it up. Global, not per document.</summary>
    public Services.OnionSettings Onion => Settings.Onion;
}
