using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Lightbox.App.Docking;

namespace Lightbox.App.Controls;

/// <summary>
/// Reusable panel block in the spirit of Krita's dockers: a title strip,
/// optional top and bottom option bars, and the docker's content in between.
/// Composition is fixed by the control template so every docker in the app
/// gets the same look; each instance only supplies its bars and content.
/// </summary>
public class Docker : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Docker, string?>(nameof(Title));

    public static readonly StyledProperty<object?> TopBarProperty =
        AvaloniaProperty.Register<Docker, object?>(nameof(TopBar));

    public static readonly StyledProperty<object?> BottomBarProperty =
        AvaloniaProperty.Register<Docker, object?>(nameof(BottomBar));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<Docker, System.Windows.Input.ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<object?> TitleBarExtraProperty =
        AvaloniaProperty.Register<Docker, object?>(nameof(TitleBarExtra));

    public static readonly StyledProperty<DockPanelId> PanelIdProperty =
        AvaloniaProperty.Register<Docker, DockPanelId>(nameof(PanelId));

    public static readonly StyledProperty<IEnumerable<DockPanelInfo>?> SwitchTargetsProperty =
        AvaloniaProperty.Register<Docker, IEnumerable<DockPanelInfo>?>(nameof(SwitchTargets));

    public static readonly StyledProperty<bool> ShowSwitcherProperty =
        AvaloniaProperty.Register<Docker, bool>(nameof(ShowSwitcher), defaultValue: true);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Which panel this is. The layout refers to panels by id and never holds
    /// the control, so this is the only link between the two.
    /// </summary>
    public DockPanelId PanelId
    {
        get => GetValue(PanelIdProperty);
        set => SetValue(PanelIdProperty, value);
    }

    /// <summary>
    /// What the header's switcher offers. Set by the host from the catalogue,
    /// minus this panel.
    /// </summary>
    public IEnumerable<DockPanelInfo>? SwitchTargets
    {
        get => GetValue(SwitchTargetsProperty);
        set => SetValue(SwitchTargetsProperty, value);
    }

    /// <summary>False for the timeline, which has nowhere else to be.</summary>
    public bool ShowSwitcher
    {
        get => GetValue(ShowSwitcherProperty);
        set => SetValue(ShowSwitcherProperty, value);
    }

    /// <summary>
    /// Shown as a ✕ button at the right of the title strip. What "close" means
    /// is the host's choice — a bottom docker collapses down, a side docker
    /// collapses to its side. Null hides the button.
    /// </summary>
    public System.Windows.Input.ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>Extra title-bar controls, placed just before the close button.</summary>
    public object? TitleBarExtra
    {
        get => GetValue(TitleBarExtraProperty);
        set => SetValue(TitleBarExtraProperty, value);
    }

    /// <summary>Option bar shown directly under the title (null = none).</summary>
    public object? TopBar
    {
        get => GetValue(TopBarProperty);
        set => SetValue(TopBarProperty, value);
    }

    /// <summary>Option bar pinned to the docker's bottom edge (null = none).</summary>
    public object? BottomBar
    {
        get => GetValue(BottomBarProperty);
        set => SetValue(BottomBarProperty, value);
    }

    /// <summary>
    /// The header was picked up and pulled far enough to mean it. The host runs
    /// the drag from here; a docker does not know where the other docks are.
    /// </summary>
    public event Action<Docker, PointerEventArgs>? PanelDragStarted;

    /// <summary>The header's switcher chose another panel: trade places with it.</summary>
    public event Action<Docker, DockPanelId>? SwitchRequested;

    private ComboBox? _switcher;

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (e.NameScope.Find<Border>("PART_Header") is { } header)
        {
            header.PointerPressed += (_, args) => HeaderPressed(args);
            header.PointerMoved += (_, args) => HeaderMoved(args);
            header.PointerReleased += (_, _) => HeaderReleased();
        }

        if (_switcher is not null) _switcher.SelectionChanged -= OnSwitcherChanged;
        _switcher = e.NameScope.Find<ComboBox>("PART_Switcher");
        if (_switcher is not null) _switcher.SelectionChanged += OnSwitcherChanged;
    }

    private void OnSwitcherChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_switcher?.SelectedItem is not DockPanelInfo target) return;
        // Back to the placeholder, which is the title: this dropdown is a verb,
        // not a setting, and leaving the chosen name in it would claim the
        // header now shows a panel that has in fact gone somewhere else.
        _switcher.SelectedItem = null;
        SwitchRequested?.Invoke(this, target.Id);
    }

    private Point? _pressed;

    /// <summary>
    /// How far the pointer has to travel before a press on the header counts
    /// as a drag. Without it, every click on a header — to focus the docker,
    /// to reach the close button — tears the panel out.
    /// </summary>
    private const double DragThreshold = 6;

    /// <summary>
    /// Called by the template's grip. Tunnelled from the host rather than
    /// handled here so a click that lands on a button in the header still
    /// reaches the button.
    /// </summary>
    internal void HeaderPressed(PointerPressedEventArgs e)
    {
        if (!DockPanels.Of(PanelId).Movable) return;
        // A press that landed on the switcher or the close button belongs to
        // them. Only bare header chrome is grip.
        if (e.Source is not (Border or TextBlock)) return;
        _pressed = e.GetPosition(this);
    }

    internal void HeaderMoved(PointerEventArgs e)
    {
        if (_pressed is not { } start) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < DragThreshold && Math.Abs(now.Y - start.Y) < DragThreshold) return;
        _pressed = null;
        PanelDragStarted?.Invoke(this, e);
    }

    internal void HeaderReleased() => _pressed = null;
}
