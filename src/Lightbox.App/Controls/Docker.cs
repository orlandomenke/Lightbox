using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    /// <summary>
    /// The panels tabbed into this slot, this one included. One member means an
    /// ordinary docker, which is what most of them are.
    /// </summary>
    public static readonly StyledProperty<IEnumerable<DockPanelInfo>?> TabsProperty =
        AvaloniaProperty.Register<Docker, IEnumerable<DockPanelInfo>?>(nameof(Tabs));

    /// <summary>Which of <see cref="Tabs"/> is showing.</summary>
    public static readonly StyledProperty<DockPanelId> ActiveTabProperty =
        AvaloniaProperty.Register<Docker, DockPanelId>(nameof(ActiveTab));

    public static readonly StyledProperty<DockPanelId> PanelIdProperty =
        AvaloniaProperty.Register<Docker, DockPanelId>(nameof(PanelId));

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

    /// <summary>The panels tabbed into this slot, this one included.</summary>
    public IEnumerable<DockPanelInfo>? Tabs
    {
        get => GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }

    /// <summary>Which of <see cref="Tabs"/> is showing.</summary>
    public DockPanelId ActiveTab
    {
        get => GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
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

    /// <summary>A tab in this slot's header was picked: show it instead.</summary>
    public event Action<Docker, DockPanelId>? TabPicked;


    private ListBox? _tabs;
    private Border? _header;

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (e.NameScope.Find<Border>("PART_Header") is { } header)
        {
            _header = header;
            header.PointerPressed += (_, args) => HeaderPressed(args);
            header.PointerMoved += (_, args) => HeaderMoved(args);
            header.PointerReleased += (_, _) => HeaderReleased();
        }

        if (_tabs is not null) _tabs.SelectionChanged -= OnTabPicked;
        _tabs = e.NameScope.Find<ListBox>("PART_Tabs");
        if (_tabs is not null)
        {
            _tabs.SelectionChanged += OnTabPicked;
            // The template can apply after the host has already written the
            // tabs in, so the strip starts life with whatever is current.
            SyncStripSelection();
        }
    }

    /// <summary>
    /// Point the strip's highlight at <see cref="ActiveTab"/>, in code.
    /// </summary>
    /// <remarks>
    /// <b>B138: the template binding cannot do this job.</b> The template binds
    /// <c>SelectedValue</c> to <see cref="ActiveTab"/>, and it worked exactly
    /// once per docker: re-binding <see cref="Tabs"/> makes the ListBox clear
    /// its own selection, and that write is a <em>local</em> value — which
    /// outranks a template binding permanently in Avalonia. From then on the
    /// active docker was visible while its tab sat unlit. A local value is
    /// only beaten by another local value, so the sync lives here.
    /// </remarks>
    private void SyncStripSelection()
    {
        if (_tabs is null) return;
        var current = Tabs?.FirstOrDefault(t => t.Id == ActiveTab);
        if (!ReferenceEquals(_tabs.SelectedItem, current))
        {
            var was = _applying;
            _applying = true;
            try { _tabs.SelectedItem = current; }
            finally { _applying = was; }
        }
    }

    /// <summary>
    /// A tab was clicked. The host decides what that means; a docker does not
    /// know which of its siblings are showing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B135: posted, not raised.</b> Answering this event means the strip is
    /// rebuilt — the docker showing leaves it and another takes its place — and
    /// the docker showing is <em>this one</em>, whose ListBox is still
    /// dispatching the selection it just changed. Detaching a control from
    /// inside its own descendant's event is a native crash rather than an
    /// exception, so nothing catches it and no crash report names a cause.
    /// </para>
    /// <para>
    /// A turn of the dispatcher is all it needs: the selection finishes
    /// unwinding, the ListBox is done touching its containers, and the rebuild
    /// then reparents a control nobody is standing on. It costs one frame,
    /// which is not a thing an artist can see, and it is the fix at the place
    /// that knows about the hazard rather than at every handler that might
    /// later subscribe.
    /// </para>
    /// </remarks>
    private void OnTabPicked(object? sender, SelectionChangedEventArgs e)
    {
        // The host is writing the answer in, not the artist choosing one.
        if (_applying) return;

        if (_tabs?.SelectedItem is not DockPanelInfo picked) return;
        // Only when it is a change. Re-selecting the tab already showing would
        // otherwise mark the workspace dirty for a click that did nothing.
        if (picked.Id == ActiveTab) return;

        Dispatcher.UIThread.Post(() =>
        {
            // Re-checked inside the post: between the click and this turn the
            // group may have been taken apart by a drag, a workspace switch or
            // the panel being closed, and activating a tab that is no longer
            // in this slot would move a panel the artist never touched.
            if (Tabs?.Any(t => t.Id == picked.Id) is true && picked.Id != ActiveTab)
            {
                TabPicked?.Invoke(this, picked.Id);
            }
        });
    }

    private bool _applying;

    /// <summary>
    /// Set the slot's tabs and which one is showing, as the layout says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not two property assignments, and B135 is why.</b> Writing
    /// <see cref="Tabs"/> re-binds the strip and writing <see cref="ActiveTab"/>
    /// moves its selection; each raises <c>SelectionChanged</c>, and in between
    /// the two the control is in a state no artist ever produced — the new tab
    /// list against the old active id. The handler read that as a click on a
    /// tab, asked the host to activate it, and the host answered by applying
    /// the layout again. <b>An infinite loop, not a crash</b>, which presents as
    /// the application locking up rather than as anything with a stack trace.
    /// </para>
    /// <para>
    /// The obvious repair is to assign in the other order, and it works for
    /// exactly as long as nobody swaps two lines. This says what is meant
    /// instead: while the host is writing the answer in, a selection change is
    /// not a choice.
    /// </para>
    /// </remarks>
    public void ShowTabs(IReadOnlyList<DockPanelInfo>? tabs, DockPanelId active)
    {
        _applying = true;
        try
        {
            Tabs = tabs;
            ActiveTab = active;
            SyncStripSelection();
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>
    /// How deep this docker's header strip is, for the drop arithmetic.
    /// </summary>
    /// <remarks>
    /// Read from the realised header rather than declared as a constant. The
    /// header holds a tab strip now and its height follows the density scale,
    /// so a hard-coded band would be right until the first time somebody
    /// retuned the scale and then silently wrong — a drop target that no longer
    /// matches the thing it is drawn over.
    /// </remarks>
    public double HeaderHeight => _header?.Bounds.Height ?? 0;

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
        // A press that landed on a control in the header belongs to that
        // control. Only bare chrome is grip.
        //
        // Checking the source's type is not enough: a ComboBox's template is
        // made of Borders, so a press on the switcher looked exactly like a
        // press on the header, and the drag it started captured the pointer
        // and killed the popup before it could be clicked. Walk up instead,
        // and stop at anything interactive.
        if (LandedOnAControl(e.Source as Visual)) return;
        _pressed = e.GetPosition(this);
    }

    private bool LandedOnAControl(Visual? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, this); node = node.GetVisualParent())
        {
            // ListBox is the tab strip. Without it here, clicking a tab walks up
            // to the header, looks exactly like a press on the grip, and tears
            // the panel out instead of switching tabs — the same trap the
            // ComboBox above was added for.
            if (node is Button or ComboBox or ToggleButton or TextBox or Slider or CheckBox or ListBox)
            {
                return true;
            }
        }
        return false;
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
