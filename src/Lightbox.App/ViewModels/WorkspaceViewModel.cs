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
    private DockLayout _layout = DockLayout.Default();

    public DockLayout Layout => _layout;

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

    /// <summary>Note a change made directly on <see cref="Layout"/> — a splitter drag.</summary>
    public void Touch()
    {
        IsDirty = true;
        Changed?.Invoke();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(Layout));
        foreach (var info in DockPanels.All) OnPropertyChanged(VisibilityNameOf(info.Id));
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
    private void ToggleTimeline() => SetVisible(DockPanelId.Timeline, !TimelineVisible);

    // ---- what the window drives ---------------------------------------------

    public void Dock(DockPanelId id, DockSide side, int index) => Mutate(l => l.Dock(id, side, index));

    public void Float(DockPanelId id, double x, double y, double w, double h) =>
        Mutate(l => l.Float(id, x, y, w, h));

    /// <summary>The header switcher: two panels trade places.</summary>
    public void Swap(DockPanelId a, DockPanelId b) => Mutate(l => l.Swap(a, b));

    /// <summary>What a panel's header offers: everything except itself.</summary>
    public static IReadOnlyList<DockPanelInfo> SwitchTargetsFor(DockPanelId id) =>
        DockPanels.All.Where(p => p.Id != id && p.Movable).ToList();

    // One property per header so the XAML can bind without a converter. They
    // never change — the catalogue is fixed — so they are computed once.
    public IReadOnlyList<DockPanelInfo> ProjectSwitchTargets { get; } = SwitchTargetsFor(DockPanelId.Project);

    public IReadOnlyList<DockPanelInfo> LayersSwitchTargets { get; } = SwitchTargetsFor(DockPanelId.Layers);

    public IReadOnlyList<DockPanelInfo> ColorSwitchTargets { get; } = SwitchTargetsFor(DockPanelId.Color);

    public IReadOnlyList<DockPanelInfo> SheetsSwitchTargets { get; } = SwitchTargetsFor(DockPanelId.Sheets);

    public IReadOnlyList<DockPanelInfo> PaletteSwitchTargets { get; } = SwitchTargetsFor(DockPanelId.Palette);

    public IReadOnlyList<DockPanelInfo> GradientSwitchTargets { get; } = SwitchTargetsFor(DockPanelId.Gradient);
}
