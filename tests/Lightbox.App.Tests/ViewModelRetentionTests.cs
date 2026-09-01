using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// B281. The App suite's execution child grew to 14.4 GB over one run — ~9.5 GB
/// of it committed managed heap that survived gen2 collections — and was being
/// OOM-killed on 16 GB machines, locally with exit 137 and on CI as the B269
/// wedge (the runner VM itself going down). The growth was not bitmaps leaking:
/// it was <b>every MainViewModel ever constructed staying reachable</b>, about
/// 2 MB each across 4,200 tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>The chain, proven by A/B before it was fixed:</b> a running
/// <c>DispatcherTimer</c> is rooted by the dispatcher; <c>AutosaveService</c>
/// started one at construction whose Tick closure captured the service; the
/// service's document provider is a closure over the view model. 15 of 15
/// dropped view models survived a full GC; with just that one timer stopped,
/// 1 of 15. <c>PlaybackClock</c> and <c>ProjectWatcher</c> carried the same
/// shape, armed on play and on watcher events respectively.
/// </para>
/// <para>
/// <b>The fix is that a timer's tick holds its owner weakly</b> — the closure
/// captures a <c>WeakReference</c> and the timer, never <c>this</c>, and stops
/// the timer when the owner is gone. The dispatcher then roots a dead timer
/// object and nothing else. This test guards the whole class, not the three
/// call sites: any future constructor that roots the view model in something
/// process-lived — a timer, a static event, a registry of delegates — turns it
/// red again.
/// </para>
/// <para>
/// <b>One survivor is allowed, deliberately.</b> A couple of statics hold the
/// most recent view model by design (<c>ColorPickerViewModel.PaletteSource</c>,
/// <c>AttachmentOverlay.Resolver</c>) — bounded at one, that is a stand-in, not
/// a leak. The failure this guards is growth <em>per construction</em>.
/// </para>
/// </remarks>
public class ViewModelRetentionTests(ITestOutputHelper output)
{
    /// <summary>
    /// Built the way a typical test builds one, in a helper so no JIT-rooted
    /// local can keep the reference alive past the return.
    /// </summary>
    private static WeakReference OneDroppedVm()
    {
        var vm = VmLayers.PaperVm();
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(200, 150, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return new WeakReference(vm);
    }

    /// <summary>
    /// The window arm of the same guard, and the one that held the other
    /// ~8 GB: the suite dump's single gcroot for the retained heap ran through
    /// the static <c>CanvasControl.BackendDetected</c> event's invocation list
    /// straight into whole MainWindow visual trees and their server
    /// compositors. The handler was detached in OnClosed — which holds only
    /// for windows somebody closes, and the suite constructs thousands it
    /// never closes.
    /// </summary>
    /// <remarks>
    /// Asserted on the invocation list rather than on collectability, because
    /// a live window cannot be collected in-app anyway: its own application
    /// roots it through theme and resource events until the harness tears the
    /// application down, and that root is correct. What must never happen is a
    /// subscription that survives the teardown — which is exactly what a
    /// handler whose <c>Target</c> is the window itself is, and what a weak
    /// wrapper is not.
    /// </remarks>
    [AvaloniaFact]
    public void TheBackendReportNeverHoldsAWindow()
    {
        var window = new Views.MainWindow();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var field = typeof(Rendering.CanvasControl).GetField(
            "BackendDetected",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var handlers = ((Delegate?)field!.GetValue(null))?.GetInvocationList() ?? [];

        // Two ways to hold the window strongly, both seen in the wild: the
        // handler's Target IS the window (an instance-method subscription),
        // and the Target is a compiler closure whose captured fields include
        // it — which is what reading any instance field inside the "weak"
        // lambda produces, because the field access captures `this`. The
        // first version of the fix had exactly that bug, this assertion's
        // first version could not see it, and the heap dump found all 153
        // test windows hanging off the captured `this`.
        static bool HoldsAWindow(Delegate h) =>
            h.Target is Views.MainWindow
            || (h.Target is { } target && target
                .GetType()
                .GetFields(System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic)
                .Any(f => f.GetValue(target) is Views.MainWindow));

        var strong = handlers.Where(HoldsAWindow).ToList();
        output.WriteLine($"BackendDetected handlers: {handlers.Length}, window-holding: {strong.Count}");
        Assert.True(
            strong.Count == 0,
            $"{strong.Count} BackendDetected handler(s) hold a MainWindow strongly — directly or "
            + "through a captured `this` — and a static subscription pins every never-closed test "
            + "window's whole visual tree for the life of the process (B281); capture only locals "
            + "and a WeakReference in the wrapper");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// The third pinner from the same dump: the IPC server's accept loop parks
    /// a native pipe read whose overlapped I/O is a strong GC handle — it
    /// outlives the application it was born in, so anything the server holds
    /// strongly is held for the life of the process. The api therefore holds
    /// the view model weakly, and a request arriving after the document is
    /// gone gets a failure response rather than a resurrection.
    /// </summary>
    [AvaloniaFact]
    public void TheIpcApiDoesNotOutliveItsDocument()
    {
        static (Services.IpcDocumentApi Api, WeakReference Doc) Build()
        {
            var vm = VmLayers.PaperVm();
            return (new Services.IpcDocumentApi(vm), new WeakReference(vm));
        }

        var (api, doc) = Build();
        // A second view model, so the "most recent" stand-ins the process
        // statics hold by design (the palette source, the attachment resolver)
        // point at somebody else — this test is about the api's reference, and
        // the bounded last-VM ones are the other test's allowance.
        _ = VmLayers.PaperVm();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(
            doc.IsAlive,
            "the IPC api kept its view model alive — the accept loop's pending native I/O "
            + "roots the api for the life of the process, so a strong reference here pins "
            + "the document, the window and its whole visual tree (B281)");
        var response = api.Handle(new Services.IpcProtocol.Request { Op = "get_scene" });
        Assert.False(response.Ok, "a request after the document is gone should fail, not throw or resurrect");
    }

    [AvaloniaFact]
    public void DroppedViewModelsAreCollectable()
    {
        var before = GC.GetTotalMemory(forceFullCollection: true) / (1024 * 1024);
        var refs = new List<WeakReference>();
        for (var i = 0; i < 6; i++) refs.Add(OneDroppedVm());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        var alive = refs.Count(r => r.IsAlive);
        var after = GC.GetTotalMemory(forceFullCollection: true) / (1024 * 1024);
        output.WriteLine($"alive after full GC: {alive}/6 — heap {before} MB -> {after} MB");
        Assert.True(
            alive <= 1,
            $"{alive} of 6 dropped view models survived a full GC — something "
            + "constructed with the view model roots it in process-lived state "
            + "(a running DispatcherTimer whose tick captures its owner is the "
            + "recorded way, B281), and the suite is back on the road to the "
            + "14 GB heap that was OOM-killing 16 GB runners");
    }
}
