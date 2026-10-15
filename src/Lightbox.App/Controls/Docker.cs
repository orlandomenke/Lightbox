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
    /// True while the panel lives in its own window. The host writes it; the
    /// template reads it to swap the float button into a redock button.
    /// </summary>
    public static readonly StyledProperty<bool> IsFloatingProperty =
        AvaloniaProperty.Register<Docker, bool>(nameof(IsFloating));

    public bool IsFloating
    {
        get => GetValue(IsFloatingProperty);
        set => SetValue(IsFloatingProperty, value);
    }

    /// <summary>
    /// Whether the header offers the float button at all. The host sets it
    /// from the panel catalogue — the timeline is not movable, so it is not
    /// floatable either.
    /// </summary>
    public static readonly StyledProperty<bool> CanFloatProperty =
        AvaloniaProperty.Register<Docker, bool>(nameof(CanFloat), true);

    public bool CanFloat
    {
        get => GetValue(CanFloatProperty);
        set => SetValue(CanFloatProperty, value);
    }

    public static readonly StyledProperty<bool> IsCollapsedProperty =
        AvaloniaProperty.Register<Docker, bool>(nameof(IsCollapsed));

    /// <summary>
    /// Rolled up to its header (Q277). The host sets it from the layout; the
    /// template hides the bars and the content, and the strip gives the row
    /// only the height that is left.
    /// </summary>
    public bool IsCollapsed
    {
        get => GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    public static readonly StyledProperty<bool> CanCollapseProperty =
        AvaloniaProperty.Register<Docker, bool>(nameof(CanCollapse));

    /// <summary>Whether this docker is somewhere it can collapse: a side strip.</summary>
    public bool CanCollapse
    {
        get => GetValue(CanCollapseProperty);
        set => SetValue(CanCollapseProperty, value);
    }

    /// <summary>
    /// The chevron was pressed or a tab double-clicked, naming the state asked
    /// for rather than "toggle": a double-click on a tab behind the front one
    /// opens the slot with its first click, and a toggle on the second would
    /// shut it again.
    /// </summary>
    public event Action<Docker, bool>? CollapseRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCollapsedProperty) PseudoClasses.Set(":collapsed", IsCollapsed);
    }

    /// <summary>
    /// The float/redock button was clicked. What that means — float from
    /// where, dock back to where — is the host's knowledge, not the docker's.
    /// </summary>
    public event Action<Docker>? FloatToggleRequested;

    /// <summary>
    /// The header was picked up and pulled far enough to mean it. The host runs
    /// the drag from here; a docker does not know where the other docks are.
    /// </summary>
    public event Action<Docker, PointerEventArgs>? PanelDragStarted;

    /// <summary>
    /// The ⠿ grip or the bare header was picked up and pulled: the whole group
    /// moves (Q278). The tab is still the grip for one panel.
    /// </summary>
    public event Action<Docker, PointerEventArgs>? GroupDragStarted;

    public static readonly StyledProperty<bool> CanDragGroupProperty =
        AvaloniaProperty.Register<Docker, bool>(nameof(CanDragGroup), true);

    /// <summary>
    /// Whether every panel in this header can move. A group holding the
    /// timeline cannot be carried whole, so it shows no grip.
    /// </summary>
    public bool CanDragGroup
    {
        get => GetValue(CanDragGroupProperty);
        set => SetValue(CanDragGroupProperty, value);
    }

    /// <summary>What a press in the header picks up.</summary>
    internal enum Grip
    {
        None,
        /// <summary>One panel: its tab, or the title of a docker with no tabs.</summary>
        Tab,
        /// <summary>The whole group: the ⠿ grip or the bare header (Q278).</summary>
        Group,
    }

    /// <summary>A tab in this slot's header was picked: show it instead.</summary>
    public event Action<Docker, DockPanelId>? TabPicked;

    /// <summary>
    /// The header was right-clicked (or its context key pressed), naming the
    /// tab the menu is about: the one under the pointer, or the panel in front
    /// when the click landed beside the tabs. The host builds the menu (Q276),
    /// because only it knows the layout.
    /// </summary>
    public event Action<Docker, DockPanelId>? HeaderMenuRequested;


    private ListBox? _tabs;
    private Border? _header;
    private Control? _groupGrip;
    private TextBlock? _title;

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (e.NameScope.Find<Border>("PART_Header") is { } header)
        {
            _header = header;
            // handledEventsToo, because the grip IS the tab strip: the ListBox
            // marks a press on a tab as handled while selecting it, so an
            // ordinary subscription hears presses everywhere EXCEPT the one
            // place a panel is picked up by. That shipped as "dockers cannot
            // be dragged at all" (B183) — selection worked, the drag never
            // armed. LandedOnTheGrip still decides what counts; this only
            // makes sure the question is asked.
            header.AddHandler(PointerPressedEvent, (_, args) => HeaderPressed(args),
                Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            header.AddHandler(PointerMovedEvent, (_, args) => HeaderMoved(args),
                Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            header.AddHandler(PointerReleasedEvent, (_, _) => HeaderReleased(),
                Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            // A right press must not pick the tab it lands on. The list box
            // selects on any press, and selecting a tab that is not in front
            // swaps which docker the slot shows — so by the release the
            // pointer is over a different control, the context request comes
            // from the strip rather than from this header, and the menu never
            // opens. Tunnel, so the list box never sees it; the release still
            // reaches the tab and becomes the request.
            header.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (args.GetCurrentPoint(this).Properties.IsRightButtonPressed) args.Handled = true;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            // The whole header, tabs and the empty space beside them: that is
            // the target Photoshop and Krita both put this menu on.
            header.AddHandler(ContextRequestedEvent, (_, args) => HeaderContextRequested(args),
                Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        }

        _groupGrip = e.NameScope.Find<Control>("PART_GroupGrip");

        if (e.NameScope.Find<Button>("PART_Collapse") is { } chevron)
        {
            chevron.Click += (_, _) => CollapseRequested?.Invoke(this, !IsCollapsed);
        }

        if (e.NameScope.Find<Button>("PART_Float") is { } floater)
        {
            floater.Click += (_, _) => FloatToggleRequested?.Invoke(this);
        }

        // The title a docker shows when it has no tab strip. Held because it is
        // the grip in that case, and there is no type test that separates it
        // from any other TextBlock somebody puts in the title bar.
        _title = e.NameScope.Find<TextBlock>("PART_Title");

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

    /// <summary>
    /// Where each tab is drawn, left to right, in this docker's coordinates.
    /// </summary>
    /// <remarks>
    /// Measured from the realised strip for the reason <see cref="HeaderHeight"/>
    /// is: a tab is as wide as its title, so there is no arithmetic that gets
    /// this right from a count. Empty before the template has applied or while
    /// the containers are unrealised, which the drop arithmetic reads as "this
    /// header cannot say where in itself anything goes" and falls back to the
    /// plain join.
    /// </remarks>
    public IReadOnlyList<PanelTab> TabRects()
    {
        if (_tabs is null || Tabs is null) return [];
        var tabs = new List<PanelTab>();
        var items = Tabs.ToList();
        for (var i = 0; i < items.Count; i++)
        {
            if (_tabs.ContainerFromIndex(i) is not { } container) continue;
            Visual visual = container;
            if (visual.TranslatePoint(default, this) is not { } at) continue;
            tabs.Add(new PanelTab(
                items[i].Id,
                new DockRect(at.X, at.Y, container.Bounds.Width, container.Bounds.Height)));
        }
        return tabs;
    }

    private Point? _pressed;

    /// <summary>What the armed press will pick up when it is pulled.</summary>
    private Grip _pressedGrip;

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
        // Every press starts clean. One that lands on a button returns below
        // without arming, and a press armed earlier whose release went missing
        // would otherwise still be waiting — and the next pull anywhere in the
        // header would start a drag, now possibly of the whole group.
        _pressed = null;
        var grip = GripAt(e.Source as Visual);
        if (grip == Grip.Group)
        {
            if (!CanDragGroup) return;
            _pressedGrip = grip;
            _pressed = e.GetPosition(this);
            return;
        }
        if (grip != Grip.Tab) return;
        // A double-click on a tab rolls the slot up or opens it, Photoshop's
        // gesture. Only on a tab: the bare header is a grip, and two quick
        // presses on a grip are not a request to roll anything up.
        if (IsSecondPress(e, TabUnder(e.Source as Visual) ?? PanelId) is { } wasCollapsed && CanCollapse
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressed = null;
            // The other state from the one the artist saw at the first click.
            CollapseRequested?.Invoke(this, !wasCollapsed);
            return;
        }
        if (!DockPanels.Of(PanelId).Movable) return;
        _pressedGrip = grip;
        _pressed = e.GetPosition(this);
    }

    /// <summary>
    /// Whether a press belongs to the drag grip — which is the <b>tab</b>,
    /// and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This used to be the other way round, and the other way round was
    /// wrong.</b> The whole header was grip and the tab strip was the one
    /// thing excluded from it, which is backwards from every docking UI an
    /// artist has used: you move a panel by picking up its tab. It also made
    /// the header's empty space — the part that exists to give the title room
    /// to breathe — the largest interactive target in the docker, so a press
    /// meant to focus a panel tore it out instead.
    /// </para>
    /// <para>
    /// Inverting it also deletes a special case rather than adding one. The
    /// old rule needed "except a strip of ONE, where the tab IS the grip",
    /// because a lone docker is nearly all tab and excluding it left nothing
    /// to hold (B139, reported as a timeline that could not be dragged back).
    /// With the tab as the grip, one tab and five behave the same way and the
    /// exception has nowhere to live.
    /// </para>
    /// <para>
    /// <b>Dragging a tab that is not showing drags the right panel anyway</b>,
    /// without a line here: a <c>ListBoxItem</c> selects on pointer-press, so
    /// the tab is already active by the time the pointer has travelled far
    /// enough to count as a drag.
    /// </para>
    /// <para>
    /// Walking up rather than checking the source's type, which is the half of
    /// the old rule worth keeping: a <c>ComboBox</c>'s template is made of
    /// <c>Border</c>s, so a press on a switcher in the title bar looked exactly
    /// like a press on chrome, and the drag it started captured the pointer and
    /// killed the popup before it could be clicked. A control in the header
    /// still owns its own press — it is simply no longer the only thing that
    /// does.
    /// </para>
    /// </remarks>
    // Internal for the tests: pointer simulation is not reliable enough here to
    // guard this end to end, and the rule has now been reported broken twice.
    internal bool LandedOnTheGrip(Visual? source) => GripAt(source) == Grip.Tab;

    /// <summary>What a press on <paramref name="source"/> would pick up.</summary>
    internal Grip GripAt(Visual? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, this); node = node.GetVisualParent())
        {
            if (node is Button or ComboBox or ToggleButton or TextBox or Slider or CheckBox) return Grip.None;
            // The tab strip, and the plain title a docker wears when it has no
            // tabs at all. Both are "the name of this panel", which is the
            // thing you pick a panel up by.
            if (node is ListBox) return Grip.Tab;
            if (ReferenceEquals(node, _title)) return Grip.Tab;
            if (ReferenceEquals(node, _groupGrip)) return Grip.Group;
            // The bare header: what B155 made inert because it tore single
            // panels out, given back by Q278 as the grip for the group.
            if (ReferenceEquals(node, _header)) return Grip.Group;
        }
        return Grip.None;
    }

    internal void HeaderMoved(PointerEventArgs e)
    {
        if (_pressed is not { } start) return;
        // A drag can only start while the button is held. The release is not
        // guaranteed to arrive here: clicking a tab rebuilds the strip, the
        // pointer's capture goes down with the old ListBoxItem, and the
        // release routes past a header that armed on the press — so the next
        // innocent mouse move (button long since up) started a drag and the
        // tab chased the pointer with no release ever coming to end it.
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressed = null;
            return;
        }
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < DragThreshold && Math.Abs(now.Y - start.Y) < DragThreshold) return;
        _pressed = null;
        if (_pressedGrip == Grip.Group) GroupDragStarted?.Invoke(this, e);
        else PanelDragStarted?.Invoke(this, e);
    }

    internal void HeaderReleased() => _pressed = null;

    /// <summary>The last press on a tab, in whichever docker it landed.</summary>
    /// <remarks>
    /// Static, and that is the point: on a tab behind the front one the first
    /// click brings it forward and the slot swaps dockers, so the second press
    /// lands on a <em>different</em> docker — and the pointer's own click count
    /// starts again at one when the control under it changes. Measured: the
    /// pair arrived as two presses counted 1 and 1. Keyed to the window, so a
    /// click in one window never pairs with a click in another.
    /// </remarks>
    private static (WeakReference<TopLevel> Top, DockPanelId Tab, ulong At, Point Where, bool WasCollapsed)? s_lastTabPress;

    /// <summary>
    /// Stands in for the system's double-click time in a headless run, where a
    /// press is stamped with the wall clock when it is <em>processed</em> — so
    /// the re-layout between the two clicks counts against the pair, and under
    /// a loaded test run took 783 ms. On a real machine the stamp is when the
    /// button went down, and the layout's cost is not the artist's.
    /// </summary>
    internal static TimeSpan? DoubleClickTimeForTests { get; set; }

    /// <summary>
    /// When this press is the second of a double-click on the same tab, whether
    /// the slot was collapsed at the first; null when it is not a second press.
    /// </summary>
    private bool? IsSecondPress(PointerPressedEventArgs e, DockPanelId tab)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        var where = e.GetPosition(top);
        // The system's double-click time and distance, so the gesture agrees
        // with every other double-click on the machine.
        var time = DoubleClickTimeForTests
                   ?? Application.Current?.PlatformSettings?.GetDoubleTapTime(e.Pointer.Type)
                   ?? TimeSpan.FromMilliseconds(500);
        var size = Application.Current?.PlatformSettings?.GetDoubleTapSize(e.Pointer.Type) ?? new Size(4, 4);
        var second = s_lastTabPress is { } last
            && last.Top.TryGetTarget(out var was) && ReferenceEquals(was, top)
            && last.Tab == tab
            && e.Timestamp >= last.At && e.Timestamp - last.At <= (ulong)time.TotalMilliseconds
            && Math.Abs(where.X - last.Where.X) <= size.Width / 2
            && Math.Abs(where.Y - last.Where.Y) <= size.Height / 2;
        var wasCollapsed = s_lastTabPress?.WasCollapsed ?? false;
        // A recognised pair is spent, so a third click starts a new one rather
        // than toggling the slot straight back.
        s_lastTabPress = second ? null : (new WeakReference<TopLevel>(top), tab, e.Timestamp, where, IsCollapsed);
        return second ? wasCollapsed : null;
    }

    internal void HeaderContextRequested(ContextRequestedEventArgs e)
    {
        if (HeaderMenuRequested is not { } ask) return;
        // PanelId rather than ActiveTab for the empty header: one control is
        // shown per slot and it is the one in front, while ActiveTab reads as
        // the enum's first value on a docker that has never been given tabs.
        ask(this, TabUnder(e.Source as Visual) ?? PanelId);
        e.Handled = true;
    }

    /// <summary>The tab a visual belongs to, if it is part of one.</summary>
    /// <remarks>
    /// From the visual tree rather than from <see cref="TabRects"/> and a
    /// pointer position: a context request from the keyboard has no position,
    /// and the item the event came from already says which tab it is.
    /// </remarks>
    private DockPanelId? TabUnder(Visual? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, this); node = node.GetVisualParent())
        {
            if (node is ListBoxItem { DataContext: DockPanelInfo info }) return info.Id;
        }
        return null;
    }
}
