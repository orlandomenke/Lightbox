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
    /// <remarks>
    /// One layer steps past its neighbour in its own folder, a whole folder at a
    /// time, and at the edge of its folder it steps out of it (Q204). A
    /// selection still shifts as a block on the flat list.
    /// </remarks>
    internal void MoveLayer(LayerRow row, int delta)
    {
        var id = row.Layer.Id;
        var targets = LayersForOp(row.Layer);
        if (targets.Count == 1)
        {
            if (StepTarget(row.Layer, delta) is { } step)
            {
                StackEdit(scene => FolderTree.Move(scene, [StackRef.Of(row.Layer)], step.Target, step.Where), "Move layer");
            }
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

    /// <summary>Where one ▲/▼ step takes a layer: past its neighbour in its container, or out of its folder.</summary>
    private (StackRef Target, StackDrop Where)? StepTarget(Layer layer, int delta)
    {
        var container = FolderTree.ContainerOf(Scene, layer);
        var siblings = FolderTree.Rows(Scene)
            .Select(r => r.Item)
            .Where(item => FolderTree.ContainerOf(Scene, item)?.Id == container?.Id)
            .ToList(); // topmost first
        var at = siblings.IndexOf(layer);
        var next = delta > 0 ? at - 1 : at + 1;
        if (next >= 0 && next < siblings.Count)
        {
            return (StackRef.Of(siblings[next]), delta > 0 ? StackDrop.Above : StackDrop.Below);
        }
        return container is null ? null : (StackRef.Of(container), delta > 0 ? StackDrop.Above : StackDrop.Below);
    }

    /// <summary>
    /// Run a stack edit as one undo step — after trying it on a skeleton of the
    /// stack, so a refusal says why and a move that changes nothing records
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <paramref name="edit"/> runs twice, on the skeleton and then on the
    /// document, so anything it creates is created inside it.
    /// </remarks>
    private bool StackEdit(Func<Scene, string?> edit, string label, bool frameContentUnchanged = true)
    {
        var trial = FolderTree.Skeleton(Scene);
        if (edit(trial) is { } why)
        {
            if (why.Length > 0) AiStatus = why;
            return false;
        }
        FolderTree.Settle(trial, Scene); // as Perform will, so the trial judges what the edit really does
        if (FolderTree.Signature(trial) == FolderTree.Signature(Scene)) return false;
        _editor.Perform(doc => edit(doc.Scene), label: label, frameContentUnchanged: frameContentUnchanged);
        return true;
    }

    [RelayCommand]
    private void MoveLayerUp(LayerRow row) => MoveLayer(row, +1);

    [RelayCommand]
    private void MoveLayerDown(LayerRow row) => MoveLayer(row, -1);

    /// <summary>The Layer menu's targets: the docker rows take a row, the menu takes whatever is active.</summary>
    private LayerRow? ActiveLayerRow() => LayerRows.FirstOrDefault(r => r.SceneIndex == ActiveLayerIndex);

    [RelayCommand]
    private void MoveActiveLayerUp()
    {
        if (ActiveLayerRow() is { } row) MoveLayer(row, +1);
    }

    [RelayCommand]
    private void MoveActiveLayerDown()
    {
        if (ActiveLayerRow() is { } row) MoveLayer(row, -1);
    }

    [RelayCommand]
    private void SelectActiveLayerContents()
    {
        if (ActiveLayerRow() is { } row) SelectLayerAlpha(row, add: false, subtract: false);
    }

    // ---- dragging in the docker ---------------------------------------------------

    /// <summary>
    /// What a drag carries: the selection when the dragged row is in it, the
    /// row alone otherwise.
    /// </summary>
    private List<StackRef> CarriedItems(object carried)
    {
        var inSelection = carried switch
        {
            LayerRow r => r.IsSelected && HasMultiLayerSelection,
            GroupRow g => g.IsSelected && _selectedLayerIds.Any(id =>
                Scene.Layers.FirstOrDefault(l => l.Id == id) is { } l && !FolderTree.IsWithin(Scene, l, g.Group)),
            _ => false,
        };
        if (inSelection) return SelectedStackItems();
        return carried switch
        {
            LayerRow r => [StackRef.Of(r.Layer)],
            GroupRow g => [StackRef.Of(g.Group)],
            _ => [],
        };
    }

    /// <summary>
    /// The selection as stack items, topmost first: the picked folder, and each
    /// selected layer that is not inside it.
    /// </summary>
    internal List<StackRef> SelectedStackItems()
    {
        var folder = SelectedGroup;
        var items = new List<StackRef>();
        foreach (var row in FolderTree.Rows(Scene))
        {
            switch (row.Item)
            {
                case LayerGroup g when g.Id == folder?.Id && !row.Continued:
                    items.Add(StackRef.Of(g));
                    break;
                case Layer l when _selectedLayerIds.Contains(l.Id)
                                  && (folder is null || !FolderTree.IsWithin(Scene, l, folder)):
                    items.Add(StackRef.Of(l));
                    break;
            }
        }
        return items;
    }

    /// <summary>The drop a hint on a row stands for, as a move.</summary>
    private (StackRef Target, StackDrop Where)? DropSpec(object target, LayerDropHint hint) => (target, hint) switch
    {
        (LayerRow r, LayerDropHint.BelowFolder) when FolderTree.Folder(Scene, r.Layer.GroupId) is { } home =>
            (StackRef.Of(home), StackDrop.Below),
        (LayerRow r, LayerDropHint.Above) => (StackRef.Of(r.Layer), StackDrop.Above),
        (LayerRow r, LayerDropHint.Below) => (StackRef.Of(r.Layer), StackDrop.Below),
        (GroupRow g, LayerDropHint.Into) => (StackRef.Of(g.Group), StackDrop.Into),
        (GroupRow g, LayerDropHint.Above) => (StackRef.Of(g.Group), StackDrop.Above),
        (GroupRow g, LayerDropHint.Below or LayerDropHint.BelowFolder) => (StackRef.Of(g.Group), StackDrop.Below),
        _ => null,
    };

    /// <summary>What kind of row a docker item is, as far as a drop cares.</summary>
    internal LayerDropTarget LayerDropTargetOf(object item)
    {
        switch (item)
        {
            case GroupRow header:
                // An empty folder has no members to aim below, so like a closed
                // one it takes a drop below itself on its own row.
                return header.Group.Collapsed || FolderTree.SubtreeLayers(Scene, header.Group).Count == 0
                       && !Scene.LayerGroups.Any(g => g.ParentId == header.Group.Id)
                    ? LayerDropTarget.CollapsedFolder
                    : LayerDropTarget.OpenFolder;
            case LayerRow { Depth: > 0 } row:
                var at = LayerPanelItems.IndexOf(row);
                var next = at >= 0 && at + 1 < LayerPanelItems.Count ? LayerPanelItems[at + 1] : null;
                var nextDepth = next switch { LayerRow r => r.Depth, GroupRow g => g.Depth, _ => -1 };
                if (nextDepth >= row.Depth) return LayerDropTarget.GroupedLayer;
                return next is null ? LayerDropTarget.BottomGroupedLayer : LayerDropTarget.LastGroupedLayer;
            default:
                return LayerDropTarget.LooseLayer;
        }
    }

    /// <summary>
    /// What dropping <paramref name="carried"/> on <paramref name="target"/>,
    /// <paramref name="fraction"/> of the way down it, would do — or None when
    /// it would do nothing or be refused, so no line is drawn that the drop
    /// then declines.
    /// </summary>
    internal LayerDropHint LayerDropHintFor(object carried, object target, double fraction)
    {
        if (ReferenceEquals(carried, target)) return LayerDropHint.None;
        var hint = LayerDropPlan.Resolve(fraction, LayerDropTargetOf(target));
        if (DropSpec(target, hint) is not { } drop) return LayerDropHint.None;
        var trial = FolderTree.Skeleton(Scene);
        if (FolderTree.Move(trial, CarriedItems(carried), drop.Target, drop.Where) is not null) return LayerDropHint.None;
        FolderTree.Settle(trial, Scene);
        return FolderTree.Signature(trial) == FolderTree.Signature(Scene) ? LayerDropHint.None : hint;
    }

    /// <summary>
    /// Carry out a drop in the layer docker. One undo step, or none when the
    /// drop changes nothing.
    /// </summary>
    internal void DropOnLayerPanel(object carried, object target, LayerDropHint hint)
    {
        if (DropSpec(target, hint) is not { } drop) return;
        var items = CarriedItems(carried);
        var label = items.Count > 1 ? "Move layers" : items[0].IsFolder ? "Move folder" : "Move layer";
        if (!StackEdit(scene => FolderTree.Move(scene, items, drop.Target, drop.Where), label)) return;
        if (items.FirstOrDefault(i => !i.IsFolder) is { } moved)
        {
            var index = Scene.Layers.FindIndex(l => l.Id == moved.Id);
            if (items.Count > 1) ActivateWithinSelection(index);
            else ActiveLayerIndex = index;
        }
    }

    /// <summary>Drop a dragged layer above or below another row. Kept for the gesture's callers and tests.</summary>
    internal void DropLayerOnRow(LayerRow draggedRow, LayerRow targetRow, bool above) =>
        DropOnLayerPanel(draggedRow, targetRow, above ? LayerDropHint.Above : LayerDropHint.Below);

    /// <summary>Drop a dragged folder above or below a row or another folder.</summary>
    internal void DropGroupBeside(LayerGroup dragged, object target, bool above)
    {
        if (!StackEdit(scene => FolderTree.Move(
                scene, [StackRef.Of(dragged)],
                target switch { GroupRow g => StackRef.Of(g.Group), LayerRow r => StackRef.Of(r.Layer), _ => StackRef.Of(dragged) },
                above ? StackDrop.Above : StackDrop.Below), "Move folder"))
        {
            return;
        }
        if (FolderTree.SubtreeLayers(Scene, dragged).FirstOrDefault() is { } first)
        {
            ActiveLayerIndex = Scene.Layers.IndexOf(first);
        }
    }

    /// <summary>Drop a dragged layer above or below a whole folder, outside it.</summary>
    internal void DropLayerBesideGroup(LayerRow draggedRow, GroupRow header, bool above) =>
        DropOnLayerPanel(draggedRow, header, above ? LayerDropHint.Above : LayerDropHint.Below);

    /// <summary>Clear every row's drop hint — called from every exit of a drag.</summary>
    internal void ClearLayerDropHints()
    {
        foreach (var item in LayerPanelItems)
        {
            switch (item)
            {
                case LayerRow row: row.DropHint = LayerDropHint.None; break;
                case GroupRow header: header.DropHint = LayerDropHint.None; break;
            }
        }
    }

    /// <summary>
    /// Show where the drop would land, on one row and nowhere else.
    /// </summary>
    internal void ShowLayerDropHint(object? target, LayerDropHint hint)
    {
        // Below an open folder lands under its last member, so that is where the
        // line goes. Drawn under the header it would sit between the header and
        // the first member — the place a drop INTO the folder lands — and the
        // line and the landing would disagree by the height of the folder.
        if (target is GroupRow { Group.Collapsed: false } openHeader && hint == LayerDropHint.BelowFolder
            && LastRowOf(openHeader) is { } last)
        {
            target = last;
        }
        foreach (var item in LayerPanelItems)
        {
            var mine = ReferenceEquals(item, target) ? hint : LayerDropHint.None;
            switch (item)
            {
                case LayerRow row: row.DropHint = mine; break;
                case GroupRow header: header.DropHint = mine; break;
            }
        }
    }

    /// <summary>The last docker row inside a folder's block, or null when it shows none.</summary>
    private object? LastRowOf(GroupRow header)
    {
        var at = LayerPanelItems.IndexOf(header);
        if (at < 0) return null;
        object? last = null;
        for (var i = at + 1; i < LayerPanelItems.Count; i++)
        {
            var depth = LayerPanelItems[i] switch { LayerRow r => r.Depth, GroupRow g => g.Depth, _ => -1 };
            if (depth <= header.Depth) break;
            last = LayerPanelItems[i];
        }
        return last;
    }

    // ---- layer folders ----------------------------------------------------------

    /// <summary>The docker's item list: folder headers and layer rows, topmost first, at their depths.</summary>
    public ObservableCollection<object> LayerPanelItems { get; } = [];

    /// <summary>
    /// Folder headers by folder id — and a second header of a folder split in
    /// two by id and a count — kept across rebuilds so their controls are too.
    /// </summary>
    private readonly Dictionary<string, GroupRow> _groupRows = [];

    /// <summary>
    /// Bring the docker's list in line with the stack, touching only what changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This used to clear the list and add everything back</b> on every
    /// edit — a reorder, an eye toggle, a rename. Each row is a heavy template
    /// (a context menu of thirty-odd items, three toggles, a thumbnail, chips),
    /// so every one of those tore down and re-inflated the whole docker, and a
    /// drag ended in a visible stall.
    /// </para>
    /// <para>
    /// <see cref="LayerRows"/> are already reused by position — a reorder
    /// re-points them rather than replacing them — so for a stack with no
    /// folders a reorder now changes nothing in this list at all. Headers are
    /// kept by folder id for the same reason.
    /// </para>
    /// <para>
    /// <b>The rows are <see cref="FolderTree.Rows"/>'s</b> (Q204): an empty
    /// folder is a row like any other, and folders nest to any depth.
    /// </para>
    /// </remarks>
    private void RebuildLayerPanel()
    {
        var byId = new Dictionary<string, LayerRow>();
        foreach (var row in LayerRows) byId.TryAdd(row.Layer.Id, row);
        var desired = new List<object>(LayerRows.Count + Scene.LayerGroups.Count);
        var used = new HashSet<string>();
        var repeats = new Dictionary<string, int>();
        foreach (var line in FolderTree.Rows(Scene))
        {
            switch (line.Item)
            {
                case Layer layer when byId.TryGetValue(layer.Id, out var row):
                    row.Depth = line.Depth;
                    if (!line.Hidden) desired.Add(row);
                    break;
                case LayerGroup group:
                    var key = group.Id;
                    if (line.Continued)
                    {
                        repeats[group.Id] = repeats.GetValueOrDefault(group.Id) + 1;
                        key = $"{group.Id}+{repeats[group.Id]}";
                    }
                    if (_groupRows.TryGetValue(key, out var header)) header.SyncFromModel(group);
                    else _groupRows[key] = header = new GroupRow(this, group);
                    header.Depth = line.Depth;
                    used.Add(key);
                    if (!line.Hidden) desired.Add(header);
                    break;
            }
        }
        foreach (var gone in _groupRows.Keys.Where(id => !used.Contains(id)).ToList())
        {
            _groupRows.Remove(gone);
        }

        PatchInPlace(LayerPanelItems, desired);
        RefreshGroupSelectionHighlights();
    }

    /// <summary>
    /// Turn <paramref name="items"/> into <paramref name="desired"/> by removing
    /// and inserting only what is not already in order.
    /// </summary>
    /// <remarks>
    /// The items kept are a longest common subsequence of the two lists, so a
    /// row dragged from the top to the bottom is one removal and one insertion
    /// rather than every row it passed shuffling up by one. The list is a
    /// docker's worth of rows; quadratic is nothing here.
    /// </remarks>
    internal static void PatchInPlace(IList<object> items, IReadOnlyList<object> desired)
    {
        var n = items.Count;
        var m = desired.Count;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = ReferenceEquals(items[i], desired[j])
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }
        var keep = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (ReferenceEquals(items[i], desired[j])) { keep.Add(items[i]); i++; j++; }
            else if (lcs[i + 1, j] >= lcs[i, j + 1]) i++;
            else j++;
        }
        for (var i = n - 1; i >= 0; i--)
        {
            if (!keep.Contains(items[i])) items.RemoveAt(i);
        }
        for (var j = 0; j < m; j++)
        {
            if (j >= items.Count || !ReferenceEquals(items[j], desired[j])) items.Insert(j, desired[j]);
        }
    }

    /// <summary>"Folder n" with the lowest n no folder in the document is using.</summary>
    private string NextFolderName()
    {
        var taken = Scene.LayerGroups.Select(g => g.Name).ToHashSet();
        var n = 1;
        while (taken.Contains($"Folder {n}")) n++;
        return $"Folder {n}";
    }

    /// <summary>The item a new folder goes directly above: the picked folder, else the active layer.</summary>
    private StackRef? NewFolderAnchor() =>
        SelectedGroup is { } group ? StackRef.Of(group)
        : ActiveLayerIndex >= 0 && ActiveLayerIndex < Scene.Layers.Count ? StackRef.Of(Scene.Layers[ActiveLayerIndex])
        : null;

    /// <summary>
    /// <b>New folder</b>: an empty folder directly above what is picked, in the
    /// same folder — or at the top of the stack (Q204).
    /// </summary>
    /// <remarks>
    /// Photoshop's <em>New Group</em> and Krita's <em>Add Group Layer</em>. It
    /// used to wrap the active layer, which pulled a layer that was already in
    /// a folder out of it and could empty — and so hide — the folder it left.
    /// Wrapping is <see cref="GroupLayersCommand"/>.
    /// </remarks>
    [RelayCommand]
    private void CreateLayerFolder()
    {
        var anchor = NewFolderAnchor();
        var name = NextFolderName();
        var id = Ids.NewId("group");
        if (!StackEdit(scene =>
            {
                FolderTree.AddFolder(scene, new LayerGroup { Id = id, Name = name }, anchor);
                return null;
            }, "New folder"))
        {
            return;
        }
        PickFolder(id);
    }

    /// <summary>
    /// <b>Group layers</b> (Ctrl+G): wrap the selection — layers and a picked
    /// folder — in a new folder where the topmost of them was (Q204).
    /// </summary>
    /// <remarks>
    /// Photoshop's <em>Group from Layers</em> and Krita's <em>Quick Group</em>.
    /// A selection with gaps in it is gathered into one run, as both do; undo
    /// puts the layers back.
    /// </remarks>
    [RelayCommand]
    private void GroupLayers()
    {
        var items = SelectedStackItems();
        if (items.Count == 0 && ActiveLayer is { } active) items = [StackRef.Of(active)];
        var name = NextFolderName();
        var id = Ids.NewId("group");
        if (!StackEdit(
                scene => FolderTree.Group(scene, new LayerGroup { Id = id, Name = name }, items),
                items.Count == 1 ? "Group layer" : "Group layers"))
        {
            return;
        }
        PickFolder(id);
    }

    /// <summary>Pick a folder on its own, as clicking its header does.</summary>
    private void PickFolder(string id)
    {
        if (LayerPanelItems.OfType<GroupRow>().FirstOrDefault(h => h.Group.Id == id) is { } header)
        {
            SelectGroup(header, toggle: false, range: false);
        }
    }

    /// <summary>Put the active layer into this folder, at its top.</summary>
    [RelayCommand]
    private void AddActiveLayerToGroup(GroupRow header) => MoveLayerIntoGroup(ActiveLayer, header.Group);

    /// <summary>
    /// Put a layer into a folder, at the top of it. Shared by the header's ＋
    /// button and dropping a dragged row on the header.
    /// </summary>
    /// <remarks>
    /// A layer already in the folder still moves to its top: dropping a member
    /// back on its own header is how an artist says "first in this folder", and
    /// a gesture that draws its hint and then does nothing reads as broken.
    /// </remarks>
    internal void MoveLayerIntoGroup(Layer layer, LayerGroup group)
    {
        if (!StackEdit(
                scene => FolderTree.Move(scene, [StackRef.Of(layer)], StackRef.Of(group), StackDrop.Into),
                "Move layer into folder"))
        {
            return;
        }
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == layer.Id);
    }

    /// <summary>Take a layer out of its folder, to sit just above the folder.</summary>
    [RelayCommand]
    private void RemoveLayerFromGroup(LayerRow row)
    {
        var layer = row.Layer;
        if (FolderTree.Folder(Scene, layer.GroupId) is not { } home) return;
        if (!StackEdit(
                scene => FolderTree.Move(scene, [StackRef.Of(layer)], StackRef.Of(home), StackDrop.Above),
                "Take layer out of folder"))
        {
            return;
        }
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == layer.Id);
    }

    /// <summary><b>Ungroup</b>: the folder goes and what was in it stays, in its place.</summary>
    [RelayCommand]
    private void DissolveGroup(GroupRow header)
    {
        var id = header.Group.Id;
        StackEdit(scene =>
        {
            if (FolderTree.Folder(scene, id) is { } folder) FolderTree.Ungroup(scene, folder);
            return null;
        }, "Ungroup");
    }

    /// <summary>
    /// <b>Delete folder</b>: the folder and everything inside it, as Krita does
    /// (Q204, the owner's choice). Undo brings it all back; Ungroup is the way
    /// to keep the contents.
    /// </summary>
    /// <remarks>
    /// Refused while anything inside is locked, for <see cref="DeleteLayer"/>'s
    /// reason pointed the other way: a delete that skipped the locked layers
    /// would leave them with no folder, which is not what either choice meant.
    /// </remarks>
    [RelayCommand]
    private void DeleteGroup(GroupRow header)
    {
        var folder = header.Group;
        // A lock above it protects it as surely as one inside it does: the
        // layers under a locked outer folder refuse a delete one at a time,
        // and picking the inner folder must not be the way round that.
        if (FolderTree.Ancestors(Scene, folder).FirstOrDefault(f => f.Locked) is { } lockedAbove)
        {
            AiStatus = $"\u201c{lockedAbove.Name}\u201d is locked \u2014 unlock it to delete a folder inside it.";
            return;
        }
        if (folder.Locked || FolderTree.SubtreeFolders(Scene, folder).Any(f => f.Locked))
        {
            AiStatus = $"“{folder.Name}” has a locked folder in it — unlock it to delete the folder.";
            return;
        }
        if (FolderTree.SubtreeLayers(Scene, folder).FirstOrDefault(l => l.Locked) is { } locked)
        {
            AiStatus = $"“{locked.Name}” is locked — unlock it to delete the folder it is in.";
            return;
        }
        var id = folder.Id;
        var name = folder.Name;
        var count = FolderTree.SubtreeLayers(Scene, folder).Count;
        var at = FolderTree.SubtreeLayers(Scene, folder).Select(l => Scene.Layers.IndexOf(l)).DefaultIfEmpty(-1).Min();
        if (!StackEdit(scene =>
            {
                if (FolderTree.Folder(scene, id) is { } f) FolderTree.DeleteWithContents(scene, f);
                RegrowAPaintableLayer(scene);
                return null;
            }, "Delete folder", frameContentUnchanged: false))
        {
            return;
        }
        _selectedGroupId = null;
        if (at >= 0)
        {
            var next = Math.Clamp(at, 0, Scene.Layers.Count - 1);
            ActiveLayerIndex = Scene.Layers[next].IsBackground ? FirstPaintableLayer(Doc) : next;
        }
        RefreshLayerSelectionHighlights();
        // Saying how much went is the receipt for a delete that takes contents.
        AiStatus = count switch
        {
            0 => $"Deleted the folder \u201c{name}\u201d.",
            1 => $"Deleted the folder \u201c{name}\u201d and the layer in it \u2014 Ctrl+Z brings it back.",
            _ => $"Deleted the folder \u201c{name}\u201d and the {count} layers in it \u2014 Ctrl+Z brings them back.",
        };
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
        using var perf = PerfLog.Begin("frame.add");
        _editor.AddFrameAfter(CurrentFrameIndex);
        CurrentFrameIndex++;
    }

    [RelayCommand]
    private void DuplicateFrame()
    {
        _editor.DuplicateFrame(CurrentFrameIndex);
        CurrentFrameIndex++;
    }

    /// <summary>
    /// The timeline bar's 🗑: take the playhead's frame out of the scene.
    /// </summary>
    /// <remarks>
    /// A column verb, like the ➕ and ⧉ beside it — those two add a frame to
    /// every layer, so this one removes it from every layer. It goes through
    /// the same path as an X-sheet <em>Delete and pull</em> on a column
    /// selection, so it refuses on a locked layer and keeps the paper, rather
    /// than being the one route that did neither.
    /// </remarks>
    [RelayCommand]
    private void DeleteFrame()
    {
        if (Scene.FrameCount <= 1) return;
        DeleteColumns([CurrentFrameIndex]);
    }

    /// <summary>Whether there is a step to take back — the Edit menu greys out on it.</summary>
    /// <remarks>
    /// Always available while a transform is open: the key then steps the
    /// session, and an empty session answers in the status line rather than by
    /// a greyed entry that would read as Undo being broken.
    /// </remarks>
    public bool CanUndo => TransformActive || _editor.CanUndo;

    /// <summary>Whether there is a step to put back.</summary>
    public bool CanRedo => TransformActive || _editor.CanRedo;

    /// <summary>
    /// The Edit menu's Undo entry, naming the step it would take back —
    /// <em>Undo Draw stroke</em> rather than <em>Undo</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Naming it is the difference between pressing the entry and guessing, and
    /// the name is already there: every step carries a label, which is what the
    /// History docker has been showing all along. This is the same record read
    /// one row at a time.
    /// </para>
    /// <para>
    /// The mnemonic is in the string because Avalonia takes it from the header
    /// whether that header is literal or bound, and a menu whose first item
    /// loses its access key when it gains a name would be a strange trade.
    /// </para>
    /// </remarks>
    public string UndoMenuHeader =>
        TransformActive ? "_Undo transform step"
        : _editor.UndoLabel is { } step ? $"_Undo {step}" : "_Undo";

    /// <summary>The Edit menu's Redo entry, naming the step it would put back.</summary>
    /// <inheritdoc cref="UndoMenuHeader" path="/remarks"/>
    public string RedoMenuHeader =>
        TransformActive ? "_Redo transform step"
        : _editor.RedoLabel is { } step ? $"_Redo {step}" : "_Redo";

    /// <summary>
    /// Re-read the four properties above. Called from the edit funnel rather
    /// than raised by each command, because undo state moves under commands
    /// that never touch it — an autosave-triggered edit, a jump in the History
    /// docker, a step trimmed by <c>MaxUndo</c>, a tab switch bringing a
    /// different stack.
    /// </summary>
    private void RefreshUndoRedo()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoMenuHeader));
        OnPropertyChanged(nameof(RedoMenuHeader));
    }

    [RelayCommand]
    private void Undo()
    {
        using var perf = PerfLog.Begin("undo");
        if (TransformActive)
        {
            StepTransformSession(undo: true);
            return;
        }
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
        using var perf = PerfLog.Begin("redo");
        if (TransformActive)
        {
            StepTransformSession(undo: false);
            return;
        }
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
        // A row is a document state, and the session's preview is composited
        // over the one it opened on — leave the session before the record moves.
        if (TransformActive) CancelTransform();
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
            // B327: the step's own account of what it could have moved. Undo of a
            // stroke then repaints that mark's footprint rather than re-stamping
            // every stroke on the drawing, which is what made Ctrl+Z cost 3 s on a
            // finished one. Null for a step that cannot say, and that is the old
            // behaviour unchanged.
            // Q167: the revision names which step moved, so the pixels saved
            // under that mark can be swapped back instead of the strokes
            // crossing it being replayed. Zero for a step that had none held, or
            // for a history jump that walked several — both take B327's replay.
            InvalidateFrameRender(frameId, scope.RepaintBounds, scope.Revision);
            _dirtyThumbIds.Add(frameId);
        }
        else if (scope.FrameIds is { } touched)
        {
            // A whole-document step that said which drawings it altered — a
            // transform commit. Those re-render; every other drawing's render
            // and thumbnail is still exactly right, as in the branch below.
            // Before this, undoing a transform on an 11-layer document threw
            // away all 64 drawings and every thumbnail: 10.5 s in the lab.
            foreach (var id in touched)
            {
                InvalidateFrameRender(id);
                _dirtyThumbIds.Add(id);
            }
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

    /// <summary>
    /// The editor, for the panel view models that own their domain's edits
    /// (the effects docker is the first). Follows the current document — the
    /// field is reassigned on a tab switch — which is why panels hold this
    /// accessor rather than the instance. Here rather than in
    /// MainViewModel.cs for the ratchet's reason: the main file may not grow.
    /// </summary>
    internal DocumentEditor PanelEditor => _editor;

    /// <summary>The effects docker's view model — registration only; every effect command lives on it.</summary>
    public EffectsViewModel EffectsPanel { get; }

    /// <summary>Registration only — every effect command lives on <see cref="EffectsPanel"/>.</summary>
    [RelayCommand]
    private void ToggleEffectsDocker() =>
        Workspace.EffectsDockerVisible = !Workspace.EffectsDockerVisible;

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
        NotifyLayerGating();
        // Hiding a folder's shape empties what it keeps inside (Q215); the
        // rows above it say so, and have to hear about it.
        SyncMaskRows();
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

    // ---- masks and clipping -------------------------------------------------

    /// <summary>
    /// The id of the layer whose mask is being painted, or null. Held by id
    /// rather than by reference because a snapshot-undo replaces the whole
    /// document tree, and an edit mode pointing at an orphaned instance would
    /// paint into a drawing nothing renders.
    /// </summary>
    private string? _maskEditLayerId;

    /// <summary>
    /// Whether strokes are landing on the active layer's mask. True only
    /// while the mask exists — deleting it, or undoing its creation, ends the
    /// mode by construction rather than by bookkeeping.
    /// </summary>
    public bool EditingLayerMask =>
        _maskEditLayerId is { } id && ActiveLayer is { } layer
        && layer.Id == id && layer.Mask is not null;

    /// <summary>The frame mask strokes land on, or null when not mask-editing.</summary>
    private Frame? MaskPaintTarget() =>
        EditingLayerMask ? ActiveLayer!.Mask!.Frame : null;

    /// <summary>Drives the row chip's outline, so the mode is never invisible.</summary>
    internal bool IsEditingMaskOf(Layer layer) =>
        layer.Id == _maskEditLayerId && layer.Mask is not null;

    internal void SetMaskEditing(Layer layer, bool editing)
    {
        _maskEditLayerId = editing && layer.Mask is not null ? layer.Id : null;
        OnPropertyChanged(nameof(EditingLayerMask));
        SyncMaskRows();
    }

    /// <summary>
    /// Add a painted mask and start editing it. <paramref name="paintHides"/>
    /// starts inverted: the layer stays fully visible and painting conceals —
    /// the vignette workflow. The other way starts fully hidden and painting
    /// reveals. Both are one flag apart forever after (Invert).
    /// </summary>
    internal void AddLayerMask(Layer layer, bool paintHides)
    {
        if (layer.Mask is not null)
        {
            SetMaskEditing(layer, true);
            return;
        }
        _editor.Perform(
            _ => layer.Mask = new LayerMask { Inverted = paintHides ? true : null },
            label: "Add layer mask", frameContentUnchanged: true);
        SetMaskEditing(layer, true);
    }

    internal void DeleteLayerMask(Layer layer)
    {
        if (layer.Mask is null) return;
        if (layer.Id == _maskEditLayerId) SetMaskEditing(layer, false);
        _editor.Perform(_ => layer.Mask = null,
            label: "Delete layer mask", frameContentUnchanged: true);
        SyncMaskRows();
    }

    internal void SetMaskDisabled(Layer layer, bool disabled)
    {
        if (layer.Mask is not { } mask || mask.Disabled == (disabled ? true : (bool?)null)) return;
        _editor.Perform(_ => mask.Disabled = disabled ? true : null,
            label: disabled ? "Disable layer mask" : "Enable layer mask",
            frameContentUnchanged: true);
        SyncMaskRows();
    }

    internal void ToggleMaskInverted(Layer layer)
    {
        if (layer.Mask is not { } mask) return;
        var inverted = !mask.IsInverted;
        _editor.Perform(_ => mask.Inverted = inverted ? true : null,
            label: "Invert layer mask", frameContentUnchanged: true);
        SyncMaskRows();
    }

    /// <summary>
    /// The stack's master switch (Q158): every effect and style off in one
    /// click without touching any use's own switch — the mask chip's
    /// disable, applied to the fx chip.
    /// </summary>
    internal void ToggleLayerEffects(Layer layer)
    {
        if (layer.Effects is not { } stack) return;
        var disable = stack.Disabled != true;
        _editor.Perform(_ => stack.Disabled = disable ? true : null,
            label: disable ? "Disable layer effects" : "Enable layer effects",
            frameContentUnchanged: true);
        SyncMaskRows();
        EffectsPanel.Rebuild();
    }

    /// <summary>
    /// Clip the layer to the one below (Photoshop's Ctrl+Alt+G). The base is
    /// positional — see <see cref="Layer.ClipToBelow"/> for why it is a flag.
    /// </summary>
    internal void SetLayerClipped(Layer layer, bool clipped, bool alone = false)
    {
        var targets = ToggleTargets(layer, l => l.IsClipped, clipped, alone);
        if (targets.Count == 0) return;
        _editor.Perform(_ =>
        {
            foreach (var target in targets) target.ClipToBelow = clipped ? true : null;
        }, label: clipped ? "Clip to layer below" : "Release clipping",
            frameContentUnchanged: true);
        SyncMaskRows();
    }

    [RelayCommand]
    private void ToggleActiveLayerClipped()
    {
        if (ActiveLayer is { } layer) SetLayerClipped(layer, !layer.IsClipped, alone: true);
    }

    [RelayCommand]
    private void ToggleActiveLayerMaskEditing()
    {
        if (ActiveLayer is { } layer && layer.Mask is not null)
        {
            SetMaskEditing(layer, !IsEditingMaskOf(layer));
        }
    }

    /// <summary>Re-read every row's mask and clip state after a mask edit.</summary>
    // Internal for the effects docker: an add or remove there changes what
    // the rows' fx chips show, the same way a mask edit changes their chips.
    internal void SyncMaskRows()
    {
        foreach (var row in LayerRows) row.SyncMaskFromModel();
    }

    private void NotifyLayerGating()
    {
        OnPropertyChanged(nameof(ActiveLayerBlocked));
        OnPropertyChanged(nameof(ActiveLayerAlphaLocked));
        OnPropertyChanged(nameof(ActiveLayerVisible));
        OnPropertyChanged(nameof(ActiveLayerLocked));
    }

    /// <summary>A row needs this to dim itself without reaching into the scene.</summary>
    internal bool IsLayerLockedByFolder(Layer layer) => FolderTree.LockedFolderOf(Scene, layer) is not null;

    /// <summary>Shown in the tool options so the restriction is never invisible.</summary>
    /// <remarks>
    /// The setter exists for the Layer menu's checkbox: a two-way binding wants
    /// a property, and routing it through <see cref="SetLayerAlphaLocked"/>
    /// keeps the menu, the docker padlock and the shortcut on one undo path.
    /// The same reasoning gives the two below their setters.
    /// </remarks>
    public bool ActiveLayerAlphaLocked
    {
        get => ActiveLayer is { AlphaLocked: true };
        set
        {
            if (ActiveLayer is { } layer && layer.AlphaLocked != value)
                SetLayerAlphaLocked(layer, value, alone: true);
        }
    }

    /// <summary>The Layer menu's eye: the active layer's visibility, undoable like the docker's.</summary>
    public bool ActiveLayerVisible
    {
        get => ActiveLayer is { Visible: true };
        set
        {
            if (ActiveLayer is { } layer && layer.Visible != value)
                SetLayerVisible(layer, value, alone: true);
        }
    }

    /// <summary>The Layer menu's padlock: the active layer's lock, undoable like the docker's.</summary>
    public bool ActiveLayerLocked
    {
        get => ActiveLayer is { Locked: true };
        set
        {
            if (ActiveLayer is { } layer && layer.Locked != value)
                SetLayerLocked(layer, value, alone: true);
        }
    }

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
    private void DeleteActiveLayer()
    {
        // A folder picked on its header is what Delete means (Q204), even an
        // empty one — the active layer is only where the brush would land.
        if (SelectedGroup is { } folder
            && LayerPanelItems.OfType<GroupRow>().FirstOrDefault(h => h.Group.Id == folder.Id) is { } header)
        {
            DeleteGroup(header);
            return;
        }
        DeleteLayer(ActiveLayer);
    }

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
            RegrowAPaintableLayer(scene);
        });
        var next = Math.Clamp(removedIndex, 0, Scene.Layers.Count - 1);
        // Never land on the paper: it is locked, so the next stroke would bounce.
        ActiveLayerIndex = Scene.Layers[next].IsBackground ? FirstPaintableLayer(Doc) : next;
    }

    /// <summary>
    /// Regrow one blank layer when nothing PAINTABLE is left, not merely when
    /// nothing is left: a document down to its locked paper has layers and
    /// still nowhere to draw.
    /// </summary>
    private static void RegrowAPaintableLayer(Scene scene)
    {
        if (scene.Layers.Any(l => !l.IsBackground)) return;
        var fresh = new Layer
        {
            Name = "Paint 1",
            Cels = [new Cel { Frame = new Frame() }],
        };
        while (fresh.Cels.Count < scene.FrameCount) fresh.Cels.Add(new Cel());
        scene.Layers.Add(fresh);
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
            && Lightbox.Raster.LayerMerge.WouldBakePixels(layer, below, Scene);
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
    /// <para>
    /// <paramref name="kind"/> is still written, because <c>Layer.Kind</c> is kept
    /// as provenance — it records where a layer's content came from, and every
    /// layer created in the app comes from the same place. It no longer picks a
    /// frame class, because there is one.
    /// </para>
    /// <para>
    /// <b>Where it lands</b> is where the artist is working, which is what every
    /// painting app has taught them: directly above the active layer, and in
    /// the active layer's folder; or, with a folder header picked, at the top of
    /// that folder. It used to go on top of the whole stack whatever was picked,
    /// so a new layer for a character in a folder arrived outside the folder,
    /// above everything, and had to be dragged home.
    /// </para>
    /// </remarks>
    private void AddLayer(LayerKind kind)
    {
        var place = NewLayerPlacement();
        string? addedId = null;
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
            if (place is { } p) FolderTree.Move(doc.Scene, [StackRef.Of(layer)], p.Target, p.Where);
            addedId = layer.Id;
            // A new layer filed into a collapsed folder would be active and
            // have no row to show it. Opening is a view preference, not an edit.
            foreach (var folder in FolderTree.FoldersOf(doc.Scene, layer)) folder.Collapsed = false;
        }, frameContentUnchanged: true);
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == addedId);
    }

    /// <summary>
    /// Where a new layer goes: at the top inside a picked folder — empty or
    /// not, at any depth (Q204) — or directly above the active layer, in its
    /// folder.
    /// </summary>
    /// <returns>Null for the top of the stack — an empty document, or no active layer to speak of.</returns>
    private (StackRef Target, StackDrop Where)? NewLayerPlacement()
    {
        if (SelectedGroup is { } group) return (StackRef.Of(group), StackDrop.Into);
        if (ActiveLayerIndex >= 0 && ActiveLayerIndex < Scene.Layers.Count)
        {
            return (StackRef.Of(Scene.Layers[ActiveLayerIndex]), StackDrop.Above);
        }
        return null;
    }

    [RelayCommand]
    private void ToggleActiveLayerVisible()
    {
        // Through SetLayerVisible rather than a direct Perform, so the Layer
        // menu's checkbox (ActiveLayerVisible) hears about it via the same
        // notification the docker's eye uses.
        if (ActiveLayer is { } layer) SetLayerVisible(layer, !layer.Visible, alone: true);
        RefreshPointerIntent();
    }

    /// <summary>Clicking a cel selects both the frame and the layer it belongs to.</summary>
    [RelayCommand]
    private void SelectFrame(FrameCell cell)
    {
        // A cel click picks a layer, so a folder picked on its header stops
        // being the pick — even when the layer it lands on was already active
        // and no change notification will say so.
        _selectedGroupId = null;
        RefreshGroupSelectionHighlights();
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
    /// The longest side of the bitmap a thumbnail shrinks from.
    /// </summary>
    /// <remarks>
    /// The largest consumer is the channels docker at 64x36; the layer docker
    /// takes 44x26 and a timeline cell 32x18. 256 gives the widest of those four
    /// linear headroom of four, which is more than a clean downscale needs and
    /// still two orders of magnitude off a document render.
    /// </remarks>
    private const int ThumbSourceLongestSide = 256;

    /// <summary>
    /// The output scale a thumbnail source is rendered at — small, and never
    /// larger than the document itself.
    /// </summary>
    /// <remarks>
    /// Both sides are constrained, not just the longest: a very wide, very short
    /// document scaled by its width alone would come back shorter than the 36 px
    /// the channels docker wants, and the thumbnail would be upscaled from too
    /// little. The short side is floored at 64 for that reason.
    /// </remarks>
    private static double ThumbSourceScale(int width, int height)
    {
        var longest = Math.Max(width, height);
        var shortest = Math.Max(1, Math.Min(width, height));
        var forLongSide = (double)ThumbSourceLongestSide / Math.Max(1, longest);
        var forShortSide = 64.0 / shortest;
        return Math.Min(1.0, Math.Max(forLongSide, forShortSide));
    }

    /// <summary>
    /// The bitmap a thumbnail shrinks from — rendered at thumbnail scale, not
    /// at document scale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B364.</b> This used to ask for <c>Scene.Width x Scene.Height</c>, on
    /// the reasoning that "the thumbnail rides a bitmap the canvas needs
    /// anyway". <b>That premise is false for every drawing except the one on
    /// screen.</b> The canvas publishes the current frame; the timeline and the
    /// layer docker ask about every drawing in the document. So each one
    /// commissioned a <em>full-document</em> render — synchronous on the UI
    /// thread, because <see cref="FrameBitmapCache.Get"/> answers a miss by
    /// rendering — and then kept it at full document size.
    /// </para>
    /// <para>
    /// At the owner's 3840x2160 that is 33 MB and around 700 ms <em>per
    /// drawing</em>, to produce a picture 64 px wide at most. It is what the
    /// artist feels as a gap on every pen lift (the commit ends in
    /// <c>RefreshThumbnails</c>) and it is where the capture's 415 MB of held
    /// frames went. A hundred-frame cycle would have asked for 3.3 GB and stayed
    /// inside its budget while doing it.
    /// </para>
    /// <para>
    /// <b>The one trade, stated because invariant 7 is about exactly this.</b>
    /// Output scale is a canvas transform rather than a multiplication of
    /// geometry, so a scaled render is a <em>different mark</em> —
    /// <c>Hash01</c> seeds every dab dynamic from position. A thumbnail is
    /// therefore not a pixel-exact miniature of the canvas, and at 32x18 that is
    /// invisible. Nothing else reads this: the two callers are the timeline cell
    /// and the layer row, and the artwork is untouched either way.
    /// </para>
    /// </remarks>
    private SKBitmap ThumbSource(Frame frame, int celIndex) =>
        _cache.Get(
            frame, Scene.Width, Scene.Height,
            outputScale: ThumbSourceScale(Scene.Width, Scene.Height),
            celIndex: celIndex);

    private ThumbnailWorker? _thumbWorker;

    /// <summary>
    /// The background thumbnail renderer, when the app has turned it on — null in
    /// the headless suite, which keeps the synchronous path.
    /// </summary>
    internal ThumbnailWorker? ThumbWorker =>
        ThumbnailWorker.Post is { } post ? _thumbWorker ??= StartThumbWorker(post) : null;

    private ThumbnailWorker StartThumbWorker(Action<Action> post)
    {
        var worker = new ThumbnailWorker(post, InstallThumbSource, () => QueueThumbRefresh(post));
        // Every path that marks a thumbnail out of date now also tells the
        // worker, so a render begun before that edit is refused (B397's review).
        _dirtyThumbIds.Added = worker.Invalidate;
        return worker;
    }

    private bool _thumbRefreshQueued;

    /// <summary>One thumbnail refresh after the current batch of arrivals, however many asked.</summary>
    private void QueueThumbRefresh(Action<Action> post)
    {
        if (_thumbRefreshQueued) return;
        _thumbRefreshQueued = true;
        post(() =>
        {
            _thumbRefreshQueued = false;
            RefreshThumbnails();
        });
    }

    /// <summary>
    /// Whether a drawing's thumbnail source has to be made, and has been sent to
    /// the worker instead — true means "not yet, it is on its way".
    /// </summary>
    /// <remarks>
    /// Opening the owner-shaped document replayed 64 drawings for their
    /// thumbnails on the UI thread, 5.9 s in one call (the performance lab).
    /// A source already in the frame cache is still used at once.
    /// </remarks>
    private bool DeferThumbSource(Frame frame, int celIndex)
    {
        if (ThumbWorker is not { } worker || _thumbs.Holds(frame.Id)) return false;
        // A posed drawing renders through the cache's pose resolver, which a
        // detached render has not got: done in the background it would come out
        // at the rest pose — and InsertWarm would put that on the canvas under
        // the posed key. The prewarmer refuses posed frames for the same reason.
        if (_cache.Rig.IsPosed(frame)) return false;
        // Everything is about to be re-rendered: start from a clean slate, so no
        // result from before the document-wide change can be installed after it.
        if (_allThumbsDirty) worker.Flush();
        var scale = ThumbSourceScale(Scene.Width, Scene.Height);
        if (_cache.Holds(frame, Scene.Width, Scene.Height, scale, celIndex)) return false;
        if (!FrameBitmapCache.CanCache(frame)) return false; // samples live: never cached, never deferred
        worker.Request(frame, Scene.Width, Scene.Height, scale, celIndex);
        return true;
    }

    /// <summary>
    /// A source the worker made, still current: hold its thumbnail, offer the
    /// bitmap to the frame cache, and let one coalesced refresh put it in place.
    /// </summary>
    /// <remarks>
    /// Stored, not applied: setting every cell that shows the drawing here meant a
    /// scan of the whole sheet per arrival — 64 arrivals on opening, each walking
    /// every cell (the leak-hunter's finding). The refresh after a batch of
    /// arrivals finds them all held and is a lookup per cell.
    /// </remarks>
    private bool InstallThumbSource(ThumbnailWorker.Made made)
    {
        var id = made.Frame.Id;
        // A document-wide change is pending and has not been refreshed yet: this
        // render predates it. Refused, and the refresh that follows asks again.
        if (_allThumbsDirty) return false;
        _thumbs.Put(id, ThumbnailRenderer.Render(made.Bitmap));
        var taken = _cache.InsertWarm(made.Frame, made.Width, made.Height, made.Scale, made.Cel, made.Bitmap);
        if (!taken)
        {
            // The cache had no room, so the layer rows could not find the source
            // later; give the few that show this drawing their picture now — a
            // walk of the rows, not of every cell.
            foreach (var row in LayerRows)
            {
                if (ExposureSheet.ExposedFrame(row.Layer, CurrentFrameIndex)?.Id != id) continue;
                row.Thumb = ThumbnailRenderer.RenderChecker(made.Bitmap, 44, 26);
                row.ThumbFrameId = id;
                LayerThumbRenders++;
            }
        }
        if (ThumbnailWorker.Post is { } post) QueueThumbRefresh(post);
        return taken;
    }

    /// <summary>How the thumbnail cache is doing — B202's guard reads this.</summary>
    internal (int Hits, int Renders, int Count) ThumbnailTraffic =>
        (_thumbs.Hits, _thumbs.Renders, _thumbs.Count);

    /// <summary>
    /// Update timeline thumbnails lazily: only cells whose keyed frame is new,
    /// changed, or explicitly marked dirty are re-rendered.
    /// </summary>
    private void RefreshThumbnails()
    {
        using var perf = PerfLog.Begin("thumbnails");
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
                // Off the UI thread when the app has turned that on: a cell whose
                // source is not ready keeps the picture it has, or shows none,
                // until the worker hands it back (InstallThumbSource).
                if (DeferThumbSource(frame, index))
                {
                    if (cell.ThumbFrameId != frame.Id) cell.Thumb = null;
                    continue;
                }
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
            if (DeferThumbSource(frame, CurrentFrameIndex))
            {
                if (row.ThumbFrameId != frame.Id) row.Thumb = null;
                continue;
            }

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
