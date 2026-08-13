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

    /// <summary>A tab in this slot's header was picked: show it instead.</summary>
    public event Action<Docker, DockPanelId>? TabPicked;


    private ListBox? _tabs;
    private Border? _header;
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
        if (!LandedOnTheGrip(e.Source as Visual)) return;
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
    internal bool LandedOnTheGrip(Visual? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, this); node = node.GetVisualParent())
        {
            if (node is Button or ComboBox or ToggleButton or TextBox or Slider or CheckBox) return false;
            // The tab strip, and the plain title a docker wears when it has no
            // tabs at all. Both are "the name of this panel", which is the
            // thing you pick a panel up by.
            if (node is ListBox) return true;
            if (ReferenceEquals(node, _title)) return true;
        }
        return false;
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
        PanelDragStarted?.Invoke(this, e);
    }

    internal void HeaderReleased() => _pressed = null;
}
