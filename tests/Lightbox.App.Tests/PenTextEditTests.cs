using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// A pen double-click on a name starts a rename that stays started.
/// </summary>
/// <remarks>
/// <para>
/// <b>Driven through the platform's own input entry, not raised at a control.</b>
/// The fault lives in what Avalonia's devices do with a pen: which element a
/// press captures, that a pen focuses on release while a mouse focuses on
/// press, and how two devices' click counts interleave. A test that raises
/// <c>PointerPressedEventArgs</c> at the label skips every one of those — which
/// is how a headless mouse double-click stayed green for a bug that ended every
/// pen rename. These feed <c>RawPointerEventArgs</c> into the window the way the
/// Windows backend does, with a real <c>PenDevice</c> and a separate
/// <c>MouseDevice</c> standing in for the tablet's phantom one (B255).
/// </para>
/// <para>
/// <b>Reflection, because Avalonia 12's reference assemblies hide the entry.</b>
/// The constructors and <c>ITopLevelImpl.Input</c> are public at run time and
/// absent from what the compiler sees. If an upgrade renames any of them these
/// tests fail loudly in <see cref="Send"/>, which is the right failure: it says
/// the harness broke, not the rename.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class PenTextEditTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class Devices
    {
        public readonly PenDevice Pen = (PenDevice)Activator.CreateInstance(typeof(PenDevice), All, null, [true], null)!;

        public readonly MouseDevice Mouse = (MouseDevice)Activator.CreateInstance(typeof(MouseDevice), All, null,
            [new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true)], null)!;
    }

    private static void Send(Window w, IInputDevice device, RawPointerEventType type, Point at, ulong time)
    {
        var impl = w.PlatformImpl!;
        var root = (IInputRoot)impl.GetType().GetProperty("InputRoot", All)!.GetValue(impl)!;
        var input = (Action<RawInputEventArgs>)impl.GetType().GetProperty("Input", All)!.GetValue(impl)!;
        var ctor = typeof(RawPointerEventArgs).GetConstructors(All).First(c =>
            c.GetParameters() is { Length: 6 } ps && ps[4].ParameterType == typeof(Point));
        var mods = type == RawPointerEventType.LeftButtonDown ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None;
        var args = (RawPointerEventArgs)ctor.Invoke([device, time, root, type, at, mods]);
        if (device is PenDevice)
        {
            var id = typeof(RawPointerEventArgs).GetProperty("RawPointerId", All)!;
            id.SetValue(args, Convert.ChangeType(7, id.PropertyType));
        }
        Render(w);
        input(args);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Lay out and render now.
    /// </summary>
    /// <remarks>
    /// <b>Hit testing reads the last rendered frame, not the current layout.</b>
    /// Headless rendering runs on a real-time timer, so at full speed a press
    /// could be hit-tested against a frame from before the docker finished
    /// laying out — and land on the panel behind the name rather than the name.
    /// That was this class failing about one run in two while passing every
    /// time with logging slowing it down.
    /// </remarks>
    private static void Render(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(w)?.Dispose();
    }

    /// <summary>
    /// Where the next sequence's clock starts. Always forward, and far past the
    /// last one: Avalonia keeps some double-tap state statically, so a test
    /// whose clock restarted at the previous test's start would be read as a
    /// press arriving before the last one — and the click count with it.
    /// </summary>
    private static ulong _clock = 1_000_000;

    /// <summary>What each press landed on — printed with any failure, because a
    /// double-click whose second press hit something else is not a double-click.</summary>
    private static readonly List<string> Trace = [];

    private static void Play(Window w, Devices d, Point at, string sequence)
    {
        var start = _clock += 100_000;
        foreach (var step in sequence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            IInputDevice device = step[0] == 'P' ? d.Pen : d.Mouse;
            var type = step[1] switch
            {
                'v' => RawPointerEventType.LeftButtonDown,
                '^' => RawPointerEventType.LeftButtonUp,
                _ => RawPointerEventType.Move,
            };
            if (type == RawPointerEventType.LeftButtonDown)
            {
                var hit = w.InputHitTest(at) as StyledElement;
                Trace.Add($"{step}->{hit?.GetType().Name}.{string.Join('.', hit?.Classes.Where(c => !c.StartsWith(':')) ?? [])}");
            }
            Send(w, device, type, at, start + ulong.Parse(step[2..]));
        }
        Dispatcher.UIThread.RunJobs();
    }

    private MainWindow? _window;

    /// <summary>
    /// Close the window: an open one keeps its pointers, captures and gesture
    /// state alive into the next test, and the double-tap is exactly the kind
    /// of state that leaks.
    /// </summary>
    public override void Dispose()
    {
        _window?.Close();
        Dispatcher.UIThread.RunJobs();
        base.Dispose();
    }

    private (MainWindow Window, MainViewModel Vm) Open()
    {
        Trace.Clear();
        var window = _window = new MainWindow { Width = 1400, Height = 1600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Pen", 400, 300, 12, 72, "#ffffff", false));
        vm.AddPaintedLayerCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static Border RowControl(Window window, object item) =>
        window.GetVisualDescendants().OfType<Border>()
            .First(b => (b.Classes.Contains("layerRow") || b.Classes.Contains("groupRow"))
                        && ReferenceEquals(b.DataContext, item));

    /// <summary>Where to press on a row's name, with the frame brought up to date first.</summary>
    private static Point OnLabel(Window window, Border row, string text)
    {
        Render(window);
        var label = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text);
        var at = label.TranslatePoint(new Point(6, label.Bounds.Height / 2), window)!.Value;
        Assert.Same(label, window.InputHitTest(at)); // the harness, not the fix, if this fails
        return at;
    }

    public static TheoryData<string, string> Sequences => new()
    {
        { "pen alone", "P~0 Pv10 P^80 Pv200 P^270 P~300" },
        { "phantom mouse first", "P~0 Mv5 Pv10 M^75 P^80 Mv195 Pv200 M^265 P^270 P~300" },
        { "pen first, echo behind", "P~0 Pv10 Mv15 P^80 M^85 Pv200 Mv205 P^270 M^275 P~300" },
        { "echo after each release", "P~0 Pv10 P^80 Mv85 M^90 Pv200 P^270 Mv275 M^280 P~300" },
        { "mouse alone", "M~0 Mv10 M^80 Mv200 M^270" },
    };

    /// <summary>
    /// The owner's report, on the layer name: a pen double-click opened the
    /// rename and closed it again before a key could be typed.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Sequences))]
    public void APenDoubleClickOnALayerNameStaysInTheRename(string name, string sequence)
    {
        var (window, vm) = Open();
        var row = vm.LayerRows[0];
        var control = RowControl(window, row);
        var box = control.GetVisualDescendants().OfType<TextBox>().First();

        Play(window, new Devices(), OnLabel(window, control, row.Name), sequence);

        var focusedNow = window.FocusManager?.GetFocusedElement() as StyledElement;
        output.WriteLine($"{name}: renaming {row.IsRenaming}, box focused {box.IsFocused}, " +
            $"same row control {ReferenceEquals(control, RowControl(window, row))}, " +
            $"focus on {focusedNow?.GetType().Name}.{string.Join('.', focusedNow?.Classes ?? [])}");
        Assert.True(row.IsRenaming, $"{name}: the rename ended as soon as it began; presses landed on: {string.Join(", ", Trace)}");
        Assert.True(box.IsFocused, $"{name}: the rename is open but nothing can be typed into it");
    }

    /// <summary>Folders are renamed the same way, and were broken the same way.</summary>
    [AvaloniaFact]
    public void APenDoubleClickOnAFolderNameStaysInTheRename()
    {
        var (window, vm) = Open();
        vm.GroupLayersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var header = vm.LayerPanelItems.OfType<GroupRow>().Single();
        var control = RowControl(window, header);

        Play(window, new Devices(), OnLabel(window, control, header.Name), "P~0 Pv10 P^80 Pv200 P^270 P~300");

        Assert.True(header.IsRenaming, "presses landed on: " + string.Join(", ", Trace));
    }

    /// <summary>
    /// The guard keeps an edit against events that are inside it, not against
    /// the artist: a pen tap on another row still ends the rename.
    /// </summary>
    [AvaloniaFact]
    public void APenTapSomewhereElseStillEndsTheRename()
    {
        var (window, vm) = Open();
        var row = vm.LayerRows[0];
        var other = vm.LayerRows[1];
        var devices = new Devices();
        Play(window, devices, OnLabel(window, RowControl(window, row), row.Name), "P~0 Pv10 P^80 Pv200 P^270");
        Assert.True(row.IsRenaming);

        Play(window, devices, OnLabel(window, RowControl(window, other), other.Name), "P~1000 Pv1010 P^1080");

        Assert.False(row.IsRenaming);
    }

    /// <summary>
    /// A click routed to the text box is the text box's own, and the guard
    /// leaves it alone — it only ever acts on a route that has gone stale.
    /// </summary>
    [AvaloniaFact]
    public void AClickInsideTheBoxIsNotTouched()
    {
        var (window, vm) = Open();
        var row = vm.LayerRows[0];
        var control = RowControl(window, row);
        var devices = new Devices();
        var resets = 0;
        vm.LayerPanelItems.CollectionChanged += (_, e) => resets++;
        Play(window, devices, OnLabel(window, control, row.Name), "M~0 Mv10 M^80 Mv200 M^270");
        Assert.True(row.IsRenaming, $"the mouse double-click did not start the rename; panel changes during it: {resets}, same control {ReferenceEquals(control, RowControl(window, row))}; presses landed on: {string.Join(", ", Trace)}");

        var box = control.GetVisualDescendants().OfType<TextBox>().First();
        var inside = box.TranslatePoint(new Point(box.Bounds.Width - 4, box.Bounds.Height / 2), window)!.Value;
        // Read at the window's own tunnel step, which runs after the guard's
        // class handler: a kept event would already be marked handled here.
        // Not TextEditFocusGuard.Kept — that counter is process-wide, and other
        // classes running in parallel move it.
        var seen = new List<string>();
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => seen.Add($"press handled={e.Handled}"),
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, (_, e) => seen.Add($"release handled={e.Handled}"),
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        Play(window, devices, inside, "M~1000 Mv1010 M^1080");

        var focusedNow = window.FocusManager?.GetFocusedElement() as StyledElement;
        output.WriteLine(string.Join(", ", seen) +
            $" | same row control {ReferenceEquals(control, RowControl(window, row))}, " +
            $"focus on {focusedNow?.GetType().Name}.{string.Join('.', focusedNow?.Classes ?? [])}, renaming {row.IsRenaming}");
        Assert.True(row.IsRenaming);
        Assert.Equal(["press handled=False", "release handled=False"], seen);
    }

    /// <summary>
    /// A press that began outside the box keeps its release, even when the pen
    /// lets go inside the box — otherwise whatever it pressed stays pressed.
    /// </summary>
    /// <remarks>The adversarial review's finding on the first version.</remarks>
    [AvaloniaFact]
    public void APressFromOutsideTheBoxKeepsItsRelease()
    {
        var (window, vm) = Open();
        var row = vm.LayerRows[0];
        var control = RowControl(window, row);
        var devices = new Devices();
        Play(window, devices, OnLabel(window, control, row.Name), "P~0 Pv10 P^80 Pv200 P^270");
        Assert.True(row.IsRenaming);
        var box = control.GetVisualDescendants().OfType<TextBox>().First();
        var outside = control.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First();
        var from = outside.TranslatePoint(new Point(outside.Bounds.Width / 2, outside.Bounds.Height / 2), window)!.Value;
        var into = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!.Value;

        var releases = new List<bool>();
        window.AddHandler(InputElement.PointerReleasedEvent, (_, e) => releases.Add(e.Handled),
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        Play(window, devices, from, "P~1000 Pv1010");
        Play(window, devices, into, "P~1050 P^1080");

        Assert.Equal([false], releases);
    }

    /// <summary>
    /// Something drawn over the box — a menu, a dropdown, the box's own
    /// Cut/Copy/Paste — is the artist's to click, even though it sits inside
    /// the box's rectangle and is not part of the box.
    /// </summary>
    /// <remarks>
    /// The app opens popups in the window's overlay layer (B255), so this is
    /// the one ordinary case that looks exactly like a stale route: a press
    /// inside the focused box, routed to something else. Without the overlay
    /// exemption the guard swallowed it.
    /// </remarks>
    [AvaloniaFact]
    public void AnythingDrawnOverTheBoxStillTakesItsClicks()
    {
        var (window, vm) = Open();
        var row = vm.LayerRows[0];
        var control = RowControl(window, row);
        var devices = new Devices();
        Play(window, devices, OnLabel(window, control, row.Name), "M~0 Mv10 M^80 Mv200 M^270");
        Assert.True(row.IsRenaming);
        var box = control.GetVisualDescendants().OfType<TextBox>().First();
        Assert.True(box.IsFocused);

        // A button in the overlay layer, laid exactly over the box.
        var clicks = 0;
        var item = new Button { Content = "Paste", Width = box.Bounds.Width, Height = box.Bounds.Height };
        item.Click += (_, _) => clicks++;
        var overlay = Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(window)!;
        var origin = box.TranslatePoint(default, overlay)!.Value;
        Canvas.SetLeft(item, origin.X);
        Canvas.SetTop(item, origin.Y);
        overlay.Children.Add(item);
        Render(window);
        var centre = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        Assert.True(item.IsVisualAncestorOf(window.InputHitTest(centre) as Visual) || window.InputHitTest(centre) == item);

        Play(window, devices, centre, "M~1000 Mv1010 M^1080");

        overlay.Children.Remove(item);
        Assert.Equal(1, clicks);
    }
}
