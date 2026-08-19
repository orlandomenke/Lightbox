using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Several elements that are one effect, and the handful of things you do to
/// them together.
/// </summary>
/// <remarks>
/// <para>
/// <b>A group has no position of its own</b>, so the numbers below are not
/// stored anywhere: <see cref="GroupOriginX"/> reads the leftmost member and
/// writes the difference to every member, and <see cref="GroupFirstFrame"/>
/// does the same along the timeline. Typing 34 into "starts on" moves the whole
/// explosion and keeps the smoke four frames behind the flash, because the
/// spacing between members <em>is</em> the effect's timing.
/// </para>
/// <para>
/// <b>No multi-select, deliberately.</b> The obvious way to make a group is to
/// select three elements and press Group, which means a multi-select list and a
/// second selection concept beside <see cref="Selected"/> — two ways to say
/// "the current element", which is how a panel starts disagreeing with itself.
/// Adding the selected element to the selected group is one press more and
/// needs neither.
/// </para>
/// </remarks>
public sealed partial class FluidEffectsViewModel
{
    public ObservableCollection<SimGroupRow> Groups { get; } = [];

    [ObservableProperty]
    private SimGroupRow? _selectedGroup;

    /// <summary>The selected group, or null.</summary>
    public SimGroup? Group => SelectedGroup?.Group;

    private SimGroup? FindGroup(string id) =>
        _vm.Doc.SimGroups is { } groups && groups.TryGetValue(id, out var group) ? group : null;

    private string Describe(SimGroup group)
    {
        var members = SimGroupOps.Members(_vm.Doc, group);
        if (members.Count == 0) return "empty";
        var first = SimGroupOps.FirstFrame(_vm.Doc, group);
        var end = SimGroupOps.EndFrame(_vm.Doc, group);
        return $"{members.Count} element{(members.Count == 1 ? string.Empty : "s")} · {first}–{end - 1}";
    }

    /// <summary>The group an element is in, for the badge on its row.</summary>
    private string? GroupNameOf(string elementId) =>
        _vm.Doc.SimGroups?.Values.FirstOrDefault(g => g.ElementIds.Contains(elementId))?.Name;

    internal void ReloadGroups()
    {
        var wasSelected = SelectedGroup?.Id;

        Groups.Clear();
        if (_vm.Doc.SimGroups is { } groups)
        {
            foreach (var group in groups.Values.OrderBy(g => SimGroupOps.FirstFrame(_vm.Doc, g))
                         .ThenBy(g => g.Id, StringComparer.Ordinal))
            {
                Groups.Add(new SimGroupRow(group.Id, FindGroup, Describe));
            }
        }

        SelectedGroup = Groups.FirstOrDefault(r => r.Id == wasSelected) ?? Groups.FirstOrDefault();
    }

    // ---- making and unmaking -------------------------------------------------------

    /// <summary>
    /// Start an effect from the selected element. The others are added to it.
    /// </summary>
    public SimGroupRow? NewGroup(string name = "Effect")
    {
        if (Element is not { } element) return null;

        var group = SimGroupOps.Create(_vm.Doc, name, [element]);
        var row = new SimGroupRow(group.Id, FindGroup, Describe);
        Groups.Add(row);
        SelectedGroup = row;
        Selected?.Relabel();
        _vm.NoteEffectEdited();
        return row;
    }

    /// <summary>Put the selected element into the selected group.</summary>
    public void AddToGroup()
    {
        if (Element is not { } element || Group is not { } group) return;
        if (group.ElementIds.Contains(element.Id)) return;

        // One group at a time, the same rule Create keeps: two groups that each
        // think they can retime an element would move it twice.
        foreach (var other in _vm.Doc.SimGroups!.Values) other.ElementIds.Remove(element.Id);
        group.ElementIds.Add(element.Id);

        Refresh();
    }

    /// <summary>Take the selected element out of whatever group it is in.</summary>
    public void RemoveFromGroup()
    {
        if (Element is not { } element || _vm.Doc.SimGroups is not { } groups) return;

        foreach (var group in groups.Values) group.ElementIds.Remove(element.Id);
        Refresh();
    }

    /// <summary>Take the group apart, leaving its elements exactly where they are.</summary>
    public void Ungroup()
    {
        if (Group is not { } group) return;
        SimGroupOps.Ungroup(_vm.Doc, group);
        Refresh();
    }

    /// <summary>Copy the whole effect — its elements too, so the copy can be retuned alone.</summary>
    public SimGroupRow? DuplicateGroup()
    {
        if (Group is not { } group) return null;

        var copy = SimGroupOps.Duplicate(_vm.Doc, group);
        Reload();
        SelectedGroup = Groups.FirstOrDefault(r => r.Id == copy.Id);
        _vm.NoteEffectEdited();
        return SelectedGroup;
    }

    /// <summary>Delete the group and every element in it, drawings included.</summary>
    public void DeleteGroup()
    {
        if (Group is not { } group) return;

        _vm.DeleteSimGroup(group);
        _cache.Clear();
        Reload();
    }

    // ---- where and when it is ------------------------------------------------------

    public string GroupName
    {
        get => Group?.Name ?? string.Empty;
        set
        {
            if (Group is not { } group) return;
            group.Name = string.IsNullOrWhiteSpace(value) ? "Effect" : value.Trim();
            OnPropertyChanged();
            SelectedGroup?.Relabel();
            foreach (var row in Elements) row.Relabel();
            _vm.NoteEffectEdited();
        }
    }

    /// <summary>
    /// The group's left edge, which is its leftmost member. Setting it moves
    /// every member by the difference.
    /// </summary>
    public double GroupOriginX
    {
        get => Members().Count == 0 ? 0 : Members().Min(e => e.OriginX);
        set
        {
            if (Group is not { } group || Members().Count == 0) return;
            SimGroupOps.Move(_vm.Doc, group, value - GroupOriginX, 0);
            AfterMoving();
        }
    }

    public double GroupOriginY
    {
        get => Members().Count == 0 ? 0 : Members().Min(e => e.OriginY);
        set
        {
            if (Group is not { } group || Members().Count == 0) return;
            SimGroupOps.Move(_vm.Doc, group, 0, value - GroupOriginY);
            AfterMoving();
        }
    }

    /// <summary>
    /// The frame the effect starts on. Setting it shifts every member by the
    /// difference, so the smoke stays however many frames behind the flash.
    /// </summary>
    public double GroupFirstFrame
    {
        get => Group is { } group ? SimGroupOps.FirstFrame(_vm.Doc, group) : 0;
        set
        {
            if (Group is not { } group) return;
            SimGroupOps.Retime(_vm.Doc, group, (int)Math.Round(value) - (int)GroupFirstFrame);
            AfterMoving();
        }
    }

    private List<SimElement> Members() =>
        Group is { } group ? SimGroupOps.Members(_vm.Doc, group) : [];

    private void AfterMoving()
    {
        // Placement is what the solver reads, so every member's cached solve is
        // stale — and there is no cheaper answer, because an element that moved
        // may now overlap a different obstacle or mask.
        foreach (var element in Members()) _cache.Remove(element.Id);

        OnPropertyChanged(nameof(GroupOriginX));
        OnPropertyChanged(nameof(GroupOriginY));
        OnPropertyChanged(nameof(GroupFirstFrame));
        SelectedGroup?.Relabel();
        foreach (var row in Elements) row.Relabel();
        BuildFields();
        _vm.NoteEffectEdited();
    }

    // ---- baking the lot ------------------------------------------------------------

    /// <summary>
    /// Bake every member and put their layers in one folder.
    /// </summary>
    /// <remarks>
    /// Member by member rather than as one operation, because each is an
    /// independent simulation on its own grid — which is what lets a small hot
    /// fireball run at four document pixels per cell beside a slow smoke at ten,
    /// each paying for its own resolution. Folding comes after, so the bake path
    /// stays ignorant of groups.
    /// </remarks>
    public void BakeGroup(IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (Group is not { } group) return;

        var members = Members();
        if (members.Count == 0) return;

        var was = Selected;
        var done = 0;
        foreach (var element in members)
        {
            cancel.ThrowIfCancellationRequested();
            Selected = Elements.FirstOrDefault(r => r.Id == element.Id);
            Bake(null, cancel);
            done++;
            progress?.Report(done / (double)members.Count);
        }
        Selected = was;

        _vm.FoldSimGroup(group);
        SelectedGroup?.Relabel();
        Status = $"Baked {members.Count} element{(members.Count == 1 ? string.Empty : "s")} into “{group.Name}”.";
    }

    /// <summary>Rebuild both lists and everything that reads them.</summary>
    private void Refresh()
    {
        ReloadGroups();
        foreach (var row in Elements) row.Relabel();
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(GroupOriginX));
        OnPropertyChanged(nameof(GroupOriginY));
        OnPropertyChanged(nameof(GroupFirstFrame));
        _vm.NoteEffectEdited();
    }

    partial void OnSelectedGroupChanged(SimGroupRow? value)
    {
        foreach (var name in new[]
                 {
                     nameof(Group), nameof(GroupName), nameof(GroupOriginX),
                     nameof(GroupOriginY), nameof(GroupFirstFrame),
                 })
        {
            OnPropertyChanged(name);
        }
    }
}
