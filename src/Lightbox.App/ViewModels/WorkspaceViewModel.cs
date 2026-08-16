using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.App.Docking;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The workspace as the app talks to it: one <see cref="DockLayout"/>, plus
/// the commands the View menu and the panel headers drive it with.
/// </summary>
/// <remarks>
/// Every mutation goes through here and ends in one <see cref="Changed"/>
/// event, which the window answers by rebuilding its strips from scratch. A
/// rebuild is cheap — reparenting seven controls — and it is the reason the
/// window never has to reason about what changed, only about what the layout
/// now says. Docking bugs that survive are then bugs in the model, which is
/// tested without a window at all.
/// </remarks>
public sealed partial class WorkspaceViewModel : ObservableObject
{
    private readonly WorkspaceStore _store;

    private DockLayout _layout;

    public WorkspaceViewModel() : this(WorkspaceStore.Load())
    {
    }

    /// <summary>Test seam: a store that is not the user's own file.</summary>
    public WorkspaceViewModel(WorkspaceStore store)
    {
        _store = store;
        _layout = (store.Find(store.Current) ?? store.Workspaces[0]).Layout.Clone();
        SelectedName = store.Current;
        foreach (var option in QuickBarCatalog.All)
        {
            QuickBarChoices.Add(new QuickBarChoice(this, option));
        }
        RefreshChoices();
    }

    public DockLayout Layout => _layout;

    public WorkspaceStore Store => _store;

    /// <summary>The layout changed and the view should re-lay-out.</summary>
    public event Action? Changed;

    /// <summary>
    /// True once the user has moved, resized, opened or closed anything. What
    /// "save the current workspace" has to offer, and what a reset undoes.
    /// </summary>
    public bool IsDirty { get; private set; }

    public void Replace(DockLayout layout, bool dirty = false)
    {
        _layout = layout;
        IsDirty = dirty;
        Raise();
    }

    /// <summary>Persist the arrangement on screen into the selected workspace's slot.</summary>
    public void Persist() => _store.Save();

    /// <summary>Note a change made directly on <see cref="Layout"/> — a splitter drag.</summary>
    public void Touch()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(CurrentLabel));
        Changed?.Invoke();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(Layout));
        foreach (var info in DockPanels.All) OnPropertyChanged(VisibilityNameOf(info.Id));
        foreach (var name in QuickNames.Values) OnPropertyChanged(name);
        foreach (var choice in QuickBarChoices) choice.Sync();
        RefreshChoices();
        Changed?.Invoke();
    }

    private void Mutate(Action<DockLayout> change)
    {
        change(_layout);
        IsDirty = true;
        Raise();
    }

    // ---- what the View menu binds to ----------------------------------------

    // One two-way property per panel rather than a parameterised command,
    // because a menu checkbox binds to a bool and nothing else. The names are
    // the ones the menu already used, so the menu is unchanged by all of this.

    public bool ProjectPanelVisible
    {
        get => _layout.IsVisible(DockPanelId.Project);
        set => SetVisible(DockPanelId.Project, value);
    }

    public bool SymbolsPanelVisible
    {
        get => _layout.IsVisible(DockPanelId.Symbols);
        set => SetVisible(DockPanelId.Symbols, value);
    }

    public bool HistoryPanelVisible
    {
        get => _layout.IsVisible(DockPanelId.History);
        set => SetVisible(DockPanelId.History, value);
    }

    public bool ToolOptionsDockerVisible
    {
        get => _layout.IsVisible(DockPanelId.ToolOptions);
        set => SetVisible(DockPanelId.ToolOptions, value);
    }

    public bool XsheetDockerVisible
    {
        get => _layout.IsVisible(DockPanelId.Xsheet);
        set => SetVisible(DockPanelId.Xsheet, value);
    }

    public bool GraphEditorDockerVisible
    {
        get => _layout.IsVisible(DockPanelId.GraphEditor);
        set => SetVisible(DockPanelId.GraphEditor, value);
    }

    public bool LayersPanelVisible
    {
        get => _layout.IsVisible(DockPanelId.Layers);
        set => SetVisible(DockPanelId.Layers, value);
    }

    public bool ColorDockerVisible
    {
        get => _layout.IsVisible(DockPanelId.Color);
        set => SetVisible(DockPanelId.Color, value);
    }

    public bool SheetsDockerVisible
    {
        get => _layout.IsVisible(DockPanelId.Sheets);
        set => SetVisible(DockPanelId.Sheets, value);
    }

    public bool PaletteDockerVisible
    {
        get => _layout.IsVisible(DockPanelId.Palette);
        set => SetVisible(DockPanelId.Palette, value);
    }

    public bool GradientDockerVisible
    {
        get => _layout.IsVisible(DockPanelId.Gradient);
        set => SetVisible(DockPanelId.Gradient, value);
    }

    public bool ChannelsDockerVisible
    {
        get => _layout.IsVisible(DockPanelId.Channels);
        set => SetVisible(DockPanelId.Channels, value);
    }

    public bool ReferenceDockerVisible
    {
        get => _layout.IsVisible(DockPanelId.Reference);
        set => SetVisible(DockPanelId.Reference, value);
    }

    // ---- the bars on the canvas ---------------------------------------------
    //
    // Listed separately from the panels in the View menu, because they are a
    // different kind of thing: a panel takes room away from the drawing, an
    // overlay bar sits on top of it. Somebody who wants no panels at all may
    // still want the zoom readout.

    public CanvasOverlayLayout Overlays => _layout.Overlays;

    public bool ViewBarVisible
    {
        get => _layout.Overlays.IsVisible(OverlayId.View);
        set => SetOverlayVisible(OverlayId.View, value);
    }

    public bool ShortcutBarVisible
    {
        get => _layout.Overlays.IsVisible(OverlayId.Shortcuts);
        set => SetOverlayVisible(OverlayId.Shortcuts, value);
    }

    /// <summary>
    /// Rulers along the canvas. Part of the workspace, like the overlay bars:
    /// an arrangement of the screen, belonging to the person rather than the
    /// artwork, so it saves, resets and switches with everything else.
    /// </summary>
    public bool RulersVisible
    {
        get => _layout.Rulers;
        set
        {
            if (_layout.Rulers == value) return;
            Mutate(l => l.Rulers = value);
            OnPropertyChanged();
        }
    }

    public bool GuidesVisible
    {
        get => _layout.GuidesVisible;
        set
        {
            if (_layout.GuidesVisible == value) return;
            Mutate(l => l.GuidesVisible = value);
            OnPropertyChanged();
        }
    }

    public bool GuidesLocked
    {
        get => _layout.GuidesLocked;
        set
        {
            if (_layout.GuidesLocked == value) return;
            Mutate(l => l.GuidesLocked = value);
            OnPropertyChanged();
        }
    }

    public void SetOverlayVisible(OverlayId id, bool visible)
    {
        if (_layout.Overlays.IsVisible(id) == visible) return;
        Mutate(l => l.Overlays.Place(id).Visible = visible);
        OnPropertyChanged(id == OverlayId.View ? nameof(ViewBarVisible) : nameof(ShortcutBarVisible));
    }

    /// <summary>Move a bar to an edge, or roll it up. One workspace edit either way.</summary>
    public void PlaceOverlay(OverlayId id, CanvasEdge edge, double along)
    {
        Mutate(l =>
        {
            var placement = l.Overlays.Place(id);
            placement.Edge = edge;
            placement.Along = Math.Clamp(along, 0, 1);
        });
    }

    public void SetOverlayCollapsed(OverlayId id, bool collapsed)
    {
        if (_layout.Overlays.Place(id).Collapsed == collapsed) return;
        Mutate(l => l.Overlays.Place(id).Collapsed = collapsed);
    }

    [RelayCommand]
    private void ToggleViewBar() => ViewBarVisible = !ViewBarVisible;

    [RelayCommand]
    private void ToggleShortcutBar() => ShortcutBarVisible = !ShortcutBarVisible;

    public bool TimelineVisible
    {
        get => _layout.IsVisible(DockPanelId.Timeline);
        set => SetVisible(DockPanelId.Timeline, value);
    }

    private static string VisibilityNameOf(DockPanelId id) => id switch
    {
        DockPanelId.Project => nameof(ProjectPanelVisible),
        DockPanelId.Layers => nameof(LayersPanelVisible),
        DockPanelId.Color => nameof(ColorDockerVisible),
        DockPanelId.Sheets => nameof(SheetsDockerVisible),
        DockPanelId.Palette => nameof(PaletteDockerVisible),
        DockPanelId.Gradient => nameof(GradientDockerVisible),
        DockPanelId.Channels => nameof(ChannelsDockerVisible),
        DockPanelId.Reference => nameof(ReferenceDockerVisible),
        DockPanelId.Symbols => nameof(SymbolsPanelVisible),
        DockPanelId.History => nameof(HistoryPanelVisible),
        DockPanelId.ToolOptions => nameof(ToolOptionsDockerVisible),
        DockPanelId.Xsheet => nameof(XsheetDockerVisible),
        DockPanelId.GraphEditor => nameof(GraphEditorDockerVisible),
        _ => nameof(TimelineVisible),
    };

    public void SetVisible(DockPanelId id, bool visible)
    {
        if (_layout.IsVisible(id) == visible) return;
        Mutate(l =>
        {
            if (visible) l.Show(id);
            else l.Hide(id);
        });
    }

    [RelayCommand]
    public void ClosePanel(DockPanelId id) => SetVisible(id, false);

    [RelayCommand]
    private void ToggleColorDocker() => SetVisible(DockPanelId.Color, !ColorDockerVisible);

    [RelayCommand]
    private void ToggleSheetsDocker() => SetVisible(DockPanelId.Sheets, !SheetsDockerVisible);

    [RelayCommand]
    private void TogglePaletteDocker() => SetVisible(DockPanelId.Palette, !PaletteDockerVisible);

    [RelayCommand]
    private void ToggleGradientDocker() => SetVisible(DockPanelId.Gradient, !GradientDockerVisible);

    [RelayCommand]
    private void ToggleReferenceDocker() => SetVisible(DockPanelId.Reference, !ReferenceDockerVisible);

    [RelayCommand]
    private void ToggleTimeline() => SetVisible(DockPanelId.Timeline, !TimelineVisible);

    // ---- the Quick options bar ------------------------------------------------
    //
    // One bool per catalogue entry, the same shape as the panel visibilities
    // above and for the same reason: the bar's sections bind to a bool and
    // nothing else. The workspace decides what the bar offers; the active tool
    // still decides which of those offers is relevant right now, so the
    // tool-bound sections AND these together in the XAML.

    public bool QuickBrushOptions => QuickHas(QuickBarCatalog.BrushOptions);

    public bool QuickEraserOptions => QuickHas(QuickBarCatalog.EraserOptions);

    public bool QuickShapeOptions => QuickHas(QuickBarCatalog.ShapeOptions);

    public bool QuickFillOptions => QuickHas(QuickBarCatalog.FillOptions);

    public bool QuickSelectOptions => QuickHas(QuickBarCatalog.SelectOptions);

    public bool QuickGradientOptions => QuickHas(QuickBarCatalog.GradientOptions);

    public bool QuickArrowOptions => QuickHas(QuickBarCatalog.ArrowOptions);

    public bool QuickGuideOptions => QuickHas(QuickBarCatalog.GuideOptions);

    public bool QuickLineOptions => QuickHas(QuickBarCatalog.LineOptions);

    public bool QuickTransport => QuickHas(QuickBarCatalog.Transport);

    public bool QuickAddFrame => QuickHas(QuickBarCatalog.AddFrame);

    /// <summary>Catalogue id → the property the XAML gates that section with.</summary>
    /// <remarks>
    /// Public and enumerable so a test can hold the XAML to it: every entry in
    /// the catalogue must have a gate in the bar, or the customize flyout
    /// offers a checkbox that changes nothing.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> QuickNames =
        new Dictionary<string, string>
        {
            [QuickBarCatalog.BrushOptions] = nameof(QuickBrushOptions),
            [QuickBarCatalog.EraserOptions] = nameof(QuickEraserOptions),
            [QuickBarCatalog.ShapeOptions] = nameof(QuickShapeOptions),
            [QuickBarCatalog.FillOptions] = nameof(QuickFillOptions),
            [QuickBarCatalog.SelectOptions] = nameof(QuickSelectOptions),
            [QuickBarCatalog.GradientOptions] = nameof(QuickGradientOptions),
            [QuickBarCatalog.ArrowOptions] = nameof(QuickArrowOptions),
            [QuickBarCatalog.GuideOptions] = nameof(QuickGuideOptions),
            [QuickBarCatalog.LineOptions] = nameof(QuickLineOptions),
            [QuickBarCatalog.Transport] = nameof(QuickTransport),
            [QuickBarCatalog.AddFrame] = nameof(QuickAddFrame),
        };

    public bool QuickHas(string id) => _layout.QuickBarContents.Contains(id);

    /// <summary>The customize flyout's rows, one per catalogue entry, fixed.</summary>
    public ObservableCollection<QuickBarChoice> QuickBarChoices { get; } = [];

    /// <summary>
    /// Include or drop one option — a workspace edit like any other: dirty
    /// until saved, undone by reset, and stored per workspace.
    /// </summary>
    public void SetQuickOption(string id, bool on)
    {
        if (QuickHas(id) == on) return;
        Mutate(l =>
        {
            // Materialised on first choice; order is the catalogue's, because
            // the XAML declares the sections in that order anyway.
            var chosen = l.QuickBarContents.ToHashSet();
            if (on) chosen.Add(id);
            else chosen.Remove(id);
            l.QuickBar = QuickBarCatalog.All.Select(o => o.Id).Where(chosen.Contains).ToList();
        });
    }

    // ---- what the window drives ---------------------------------------------

    public void Dock(DockPanelId id, DockSide side, int index) => Mutate(l => l.Dock(id, side, index));

    public void Float(DockPanelId id, double x, double y, double w, double h) =>
        Mutate(l => l.Float(id, x, y, w, h));

    public void Redock(DockPanelId id) => Mutate(l => l.Redock(id));

    /// <summary>Tab a panel together with another.</summary>
    public void JoinGroup(DockPanelId id, DockPanelId target) => Mutate(l => l.JoinGroup(id, target));

    /// <summary>Show a tab, and stop showing its siblings.</summary>
    /// <remarks>
    /// Through <see cref="Mutate"/> like every other layout change, which is
    /// what makes it mark the workspace dirty and stay session-only until it is
    /// saved. Writing to the layout directly would skip both, and the symptom —
    /// an arrangement that quietly survives a restart, or one that quietly does
    /// not — is the kind nobody traces back to a missing wrapper.
    /// </remarks>
    public void Activate(DockPanelId id) => Mutate(l => l.Activate(id));

    // ---- named workspaces ----------------------------------------------------

    /// <summary>
    /// The picker's rows. Rebuilt rather than mutated, because a workspace's
    /// name is its identity and a rename is a different row.
    /// </summary>
    public ObservableCollection<WorkspaceRow> WorkspaceChoices { get; } = [];

    /// <summary>The name of the workspace showing now, or a saved one's name.</summary>
    public string SelectedName { get; private set; } = "";

    /// <summary>
    /// The picker's label. A workspace the user has since rearranged is marked,
    /// because "Animation" on screen next to a layout that is not Animation's
    /// is the one thing a picker must not claim.
    /// </summary>
    public string CurrentLabel => IsDirty ? SelectedName + " *" : SelectedName;

    private void RefreshChoices()
    {
        WorkspaceChoices.Clear();
        foreach (var workspace in _store.Workspaces)
        {
            WorkspaceChoices.Add(new WorkspaceRow(
                workspace.Name,
                workspace.BuiltIn,
                string.Equals(workspace.Name, SelectedName, StringComparison.OrdinalIgnoreCase)));
        }
        OnPropertyChanged(nameof(WorkspaceChoices));
        OnPropertyChanged(nameof(CurrentLabel));
    }

    /// <summary>Apply a saved workspace by name.</summary>
    [RelayCommand]
    public void Apply(string name)
    {
        if (_store.Find(name) is not { } workspace) return;
        _store.Current = workspace.Name;
        SelectedName = workspace.Name;
        Replace(workspace.Layout.Clone());
        _store.Save();
    }

    /// <summary>
    /// Store the arrangement on screen as a <em>new</em> workspace. A name
    /// left unchanged still gets a new workspace — "Name (edited)" — because
    /// this is "save as new"; overwriting what is selected is
    /// <see cref="SaveCurrent"/>'s job, and doing it from here would make the
    /// two menu items the same command.
    /// </summary>
    [RelayCommand]
    public void SaveAs(string name)
    {
        name = name.Trim();
        // The unchanged name forks rather than overwrites; the store already
        // does the same for any built-in's name, edited or not.
        if (string.Equals(name, SelectedName, StringComparison.OrdinalIgnoreCase)
            && _store.Find(name) is { BuiltIn: false })
        {
            name += " (edited)";
        }
        var saved = _store.Save(name, _layout);
        SelectedName = saved.Name;
        IsDirty = false;
        _store.Save();
        RefreshChoices();
    }

    /// <summary>
    /// Update the workspace showing now, in place — built-ins included. No
    /// fork, no "(edited)": pressing save on the workspace you are in means
    /// "keep it like this", and reset still knows how a built-in shipped.
    /// </summary>
    [RelayCommand]
    public void SaveCurrent()
    {
        if (_store.Update(SelectedName, _layout) is null) return;
        IsDirty = false;
        _store.Save();
        RefreshChoices();
    }

    /// <summary>
    /// Throw away the changes and go back to what the selected workspace
    /// originally said. A built-in resets to how it shipped — even one that
    /// has since been saved over, because "reset" on a workspace the app
    /// ships is a promise about the app, not about the file.
    /// </summary>
    [RelayCommand]
    public void Reset()
    {
        if (_store.Find(SelectedName) is { BuiltIn: true } builtIn
            && WorkspaceStore.ShippedLayout(builtIn.Name) is { } shipped)
        {
            builtIn.Layout = shipped;
        }
        Apply(SelectedName);
    }

    /// <summary>Delete a saved workspace. Built-ins and the last one refuse.</summary>
    [RelayCommand]
    public void Delete(string name)
    {
        if (!_store.Delete(name)) return;
        if (string.Equals(SelectedName, name, StringComparison.OrdinalIgnoreCase))
        {
            Apply(_store.Current);
        }
        _store.Save();
        RefreshChoices();
    }

    /// <summary>
    /// Take the built-in workspace for a project type — the offer made when a
    /// project of that type is created.
    /// </summary>
    public void UseDefaultFor(Lightbox.Core.Projects.ProjectType? type)
    {
        var workspace = _store.DefaultFor(type);
        SelectedName = workspace.Name;
        _store.Current = workspace.Name;
        Replace(workspace.Layout.Clone());
    }

}

/// <summary>
/// One row of the workspace picker.
/// </summary>
/// <param name="CanDelete">
/// Only a saved workspace offers a bin. A built-in is what reset falls back
/// to, so deleting one would take the fallback with it.
/// </param>
public sealed record WorkspaceRow(string Name, bool BuiltIn, bool IsCurrent)
{
    public bool CanDelete => !BuiltIn;

    public override string ToString() => Name;
}

/// <summary>
/// One row of the quick bar's customize flyout: a catalogue entry and whether
/// this workspace carries it.
/// </summary>
/// <remarks>
/// A live object rather than a rebuilt record like <see cref="WorkspaceRow"/>,
/// because these rows are toggled from an open flyout — replacing the
/// collection under a checkbox mid-click is how a flyout closes itself.
/// <see cref="Sync"/> is called by the owner whenever the layout changes, so
/// a workspace switch re-checks the boxes without rebuilding them.
/// </remarks>
public sealed class QuickBarChoice(WorkspaceViewModel owner, QuickBarOption option)
    : ObservableObject
{
    public string Label => option.Label;

    public string Hint => option.Hint;

    public bool IsOn
    {
        get => owner.QuickHas(option.Id);
        set
        {
            if (owner.QuickHas(option.Id) == value) return;
            owner.SetQuickOption(option.Id, value);
            OnPropertyChanged();
        }
    }

    /// <summary>Re-announce <see cref="IsOn"/> after the layout changed underneath.</summary>
    public void Sync() => OnPropertyChanged(nameof(IsOn));
}
