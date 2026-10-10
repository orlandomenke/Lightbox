using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Views;

/// <summary>
/// What the performance lab asks a running instance, so a scenario can check
/// behaviour as well as time it (Q209): the state of the sheet and the layer
/// selection, and where on screen a cell, a layer row, a folder or a menu item
/// is — to click it the way a hand does.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why behaviour, in the lab.</b> Two fixes the owner was told had landed —
/// Delete and pull on an empty cell, Shift/Ctrl+click in the layer docker — were
/// tested by calling the view model directly and still failed in their hands.
/// The lab drives the real input path, so a check there fails where they do.
/// </para>
/// <para>
/// <b>Only on a lab instance</b> (<see cref="LabInstance"/>): one running on a
/// throwaway profile with the perf log on. It answers over a pipe named after its
/// own process, so the lab never talks to the artist's open Lightbox and the
/// artist's MCP bridge never reaches a lab instance. It reports and changes
/// nothing in the document; the one setup step, lab_show_panel, changes which
/// panels are on screen in the run's throwaway profile.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Whether this process is a performance-lab instance.</summary>
    /// <remarks>
    /// An explicit opt-in, <c>LIGHTBOX_LAB=1</c>, which only <c>perf/lab.py</c> sets —
    /// on top of the throwaway profile and the perf log. Inferring "lab" from those two
    /// alone (the sensitivity review) would turn an artist's own app into one if they
    /// ran a second profile and switched the perf log on for diagnostics, and their
    /// MCP bridge would quietly stop finding it.
    /// </remarks>
    internal static bool LabInstance =>
        Environment.GetEnvironmentVariable("LIGHTBOX_LAB") == "1"
        && Lightbox.Core.ProfileFolder.Overridden && PerfLog.On;

    /// <summary>The pipe a lab instance serves: its own, never the shared one.</summary>
    internal static string LabPipeName => $"lightbox-ipc-{Environment.ProcessId}";

    private ContextMenu? _labLastMenu;

    /// <summary>
    /// The MCP bridge's endpoint — or, on a lab instance, the lab's own pipe with
    /// the lab's questions on it, which the bridge never connects to.
    /// </summary>
    private IpcServer StartIpc()
    {
        TrackLabMenus();
        TrackResponses();
        return new IpcServer(
            new IpcDocumentApi(_vm) { Lab = LabInstance ? AnswerLab : null },
            LabInstance ? LabPipeName : null);
    }

    private void TrackLabMenus()
    {
        if (!LabInstance) return;
        // Every lab run records the input trace, the layer-selection trace with it,
        // so a check that fails also says what the app received (the modifiers on
        // a press, which handler took it) — written into the run's own profile.
        InputTrace.Arm();
        // The menu a right-click just opened lives in its own popup, outside this
        // window's visual tree; it is remembered here so a step can find its items.
        AddHandler(ContextRequestedEvent, (_, e) =>
        {
            if (e.Source is Control source && FindMenu(source) is { } menu) _labLastMenu = menu;
        }, handledEventsToo: true);
    }

    private int _labInputs;

    /// <summary>
    /// Every key and pointer press or release, marked as it reaches the window
    /// ("input"), and again at the first frame drawn once everything it set
    /// off has run ("shown") — the lab's "input to on screen" (Q209).
    /// </summary>
    /// <remarks>
    /// <b>Why ApplicationIdle, then a frame.</b> A handler that posts its real
    /// work (a deferred compose, a rebuild at Background) returns at once, so
    /// "the handler returned" would read every deferred stall as instant. Idle
    /// is when the queue is empty, and the frame after that is the first one
    /// that can show the result. Work moved off the UI thread — thumbnails —
    /// is not counted, which is the point of moving it.
    /// </remarks>
    private void TrackResponses()
    {
        if (!PerfLog.On) return;
        // On every TopLevel, not this window: a context menu is its own popup
        // root, and the first runs timed a menu click as "never answered".
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((_, e) => Respond("key " + e.Key),
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => Respond("press"),
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, _) => Respond("release"),
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void Respond(string what)
    {
        var id = (++_labInputs).ToString(System.Globalization.CultureInfo.InvariantCulture);
        PerfLog.Mark("input", id + " " + what);
        Dispatcher.Post(
            () => RequestAnimationFrame(_ => PerfLog.Mark("shown", id)),
            Avalonia.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static ContextMenu? FindMenu(Control from)
    {
        for (Visual? v = from; v is not null; v = v.GetVisualParent())
        {
            if (v is Control { ContextMenu: { } menu }) return menu;
        }
        return null;
    }

    /// <summary>The lab's questions; null for anything else.</summary>
    internal IpcProtocol.Response? AnswerLab(IpcProtocol.Request request) => request.Op switch
    {
        "lab_state" => IpcProtocol.Response.Success(LabState()),
        "lab_locate" => LabLocate(request.Payload),
        // Every icon on screen and where it landed in its button, at this
        // display's scaling — the placement question no headless run can ask.
        "lab_icons" => IpcProtocol.Response.Success(LabIcons()),
        // Setup, the one thing here that changes anything: put a panel on screen,
        // as the View menu would, so a scenario does not depend on which panels
        // a fresh profile's workspace happens to show (Q209: setup through the
        // app, the measured gesture through real input).
        "lab_show_panel" => LabShowPanel(request.Payload),
        _ => null,
    };

    private IpcProtocol.Response LabShowPanel(JsonElement? payload)
    {
        if (payload is not { } p || !p.TryGetProperty("panel", out var el)
            || !Enum.TryParse<Docking.DockPanelId>(el.GetString(), ignoreCase: true, out var id))
        {
            return IpcProtocol.Response.Fail("lab_show_panel needs a panel name, as in DockPanelId");
        }
        _vm.Workspace.SetVisible(id, true);
        _vm.Workspace.Activate(id);
        return IpcProtocol.Response.Success(new { shown = id.ToString() });
    }

    private object LabState()
    {
        var scene = _vm.Doc.Scene;
        var selected = _vm.SelectedLayerIds;
        return new
        {
            frameCount = scene.FrameCount,
            currentFrame = _vm.CurrentFrameIndex,
            active = _vm.ActiveLayerIndex >= 0 && _vm.ActiveLayerIndex < scene.Layers.Count
                ? scene.Layers[_vm.ActiveLayerIndex].Name : null,
            status = _vm.AiStatus,
            layers = scene.Layers.Select(l => new
            {
                id = l.Id,
                name = l.Name,
                folder = scene.LayerGroups.FirstOrDefault(g => g.Id == l.GroupId)?.Name,
                selected = selected.Contains(l.Id),
                // ". " for an empty cel, the drawing's id for a keyed one — what the
                // sheet shows, frame by frame, to the end of the scene.
                row = string.Join(" ", Enumerable.Range(0, scene.FrameCount).Select(i =>
                    ExposureSheet.FrameAtExactIndex(l, i)?.Id ?? ".")),
            }),
            folders = _vm.LayerPanelItems.OfType<GroupRow>().Select(g => new
            {
                name = g.Group.Name,
                selected = g.IsSelected,
            }),
        };
    }

    private object LabIcons()
    {
        var (realized, visible) = Rendering.IconCensus.CountPaths(this);
        return new
        {
            scale = RenderScaling,
            visuals = this.GetVisualDescendants().Count(),
            paths = realized,
            pathsVisible = visible,
            icons = Rendering.IconCensus.Take(this, RenderScaling),
        };
    }

    private IpcProtocol.Response LabLocate(JsonElement? payload)
    {
        if (payload is not { } p || !p.TryGetProperty("kind", out var kindEl))
        {
            return IpcProtocol.Response.Fail("lab_locate needs a kind");
        }
        // Malformed values answer "not found", never throw out of the handler.
        string? Str(string name) =>
            p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int Int(string name) =>
            p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : -1;

        // A beginning that two different tooltips share names neither button:
        // "Lock the layer" began both the layer's lock and the options bar's
        // transparency lock, and the first icon-buttons run timed the wrong one
        // under the right name. Rows that repeat one tooltip are still one answer.
        if (kindEl.GetString() == "tip-starts" && Str("text") is { Length: > 0 } begins)
        {
            var tips = this.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible)
                .Select(b => ToolTip.GetTip(b) as string)
                .Where(t => t is not null && t.StartsWith(begins, StringComparison.Ordinal))
                .Distinct().ToList();
            if (tips.Count > 1)
            {
                return IpcProtocol.Response.Fail(
                    $"\"{begins}\" begins {tips.Count} different tooltips: {string.Join(" | ", tips.Select(t => t!.Length > 48 ? t[..48] + "…" : t))}");
            }
        }

        Control? found = kindEl.GetString() switch
        {
            // A cel on the X-sheet: the button whose cell is at that frame, in the
            // row of the layer with that name.
            "xsheet-cel" => XsheetLayerList.GetVisualDescendants().OfType<Button>().FirstOrDefault(b =>
                b.DataContext is FrameCell cell && cell.Index == Int("frame")
                // The nearest presenter up is the cell's own (the row's inner list
                // of cells); the row is further up — the first ancestor carrying it.
                && b.GetVisualAncestors().OfType<Control>().FirstOrDefault(a => a.DataContext is LayerRow)
                    ?.DataContext is LayerRow row
                && row.Layer.Name == Str("layer")),
            // A row of the layer docker, by layer name — its item container, so the
            // centre is the row and not one of its small buttons.
            "layer-row" => LayerList.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(c =>
                c.DataContext is LayerRow row && row.Layer.Name == Str("layer")),
            "folder-row" => LayerList.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(c =>
                c.DataContext is GroupRow g && g.Group.Name == Str("folder")),
            // An item of the menu a right-click last opened, by its text — the
            // access-key underscore ("Delete and p_ull") is not part of it.
            "menu-item" => _labLastMenu?.GetLogicalDescendants().OfType<MenuItem>()
                .FirstOrDefault(m => m.IsVisible && (m.Header as string)?.Replace("_", "") == Str("text")),
            // A button by its tooltip, as an artist finds an icon: the timeline
            // bar's ＋ has no shortcut, so a scenario has to click it.
            "tip" => this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b =>
                b.IsEffectivelyVisible && ToolTip.GetTip(b) as string == Str("text")),
            // The same, by how the tooltip begins: a tool's tip is its name and
            // key followed by a paragraph, and a scenario should not have to
            // carry the paragraph to click the tool.
            "tip-starts" => Str("text") is { Length: > 0 } start
                ? this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b =>
                    b.IsEffectivelyVisible && ToolTip.GetTip(b) is string tip
                    && tip.StartsWith(start, StringComparison.Ordinal))
                : null,
            _ => null,
        };
        if (found is null || !found.IsEffectivelyVisible || found.Bounds.Width <= 0)
        {
            return IpcProtocol.Response.Fail($"nothing on screen for {p}");
        }
        // Under the pointer, or no answer: a row scrolled out of its list, or a
        // panel covered by another, is laid out and "visible" and still not where
        // a click would land — the first lab run clicked two hidden rows and read
        // the misses as the app ignoring them.
        var centre = new Point(found.Bounds.Width / 2, found.Bounds.Height / 2);
        if (TopLevel.GetTopLevel(found) is Visual root && found.TranslatePoint(centre, root) is { } inRoot
            && root is IInputElement input && input.InputHitTest(inRoot) is Visual hit
            && !ReferenceEquals(hit, found) && !found.IsVisualAncestorOf(hit))
        {
            var chain = string.Join(" < ", hit.GetVisualAncestors().Prepend(hit).Take(6).OfType<Control>()
                .Select(c => c.GetType().Name + (string.IsNullOrEmpty(c.Name) ? "" : "#" + c.Name)
                    + (c.Classes.Count > 0 ? "." + string.Join(".", c.Classes) : "")
                    + (c.DataContext is LayerRow lr ? $"[row {lr.Layer.Name}]" : c.DataContext is FrameCell fc ? $"[cell {fc.Index}]" : "")));
            return IpcProtocol.Response.Fail($"{p} is laid out but not clickable: on top of it is {chain}");
        }
        var topLeft = found.PointToScreen(new Point(0, 0));
        var bottomRight = found.PointToScreen(new Point(found.Bounds.Width, found.Bounds.Height));
        return IpcProtocol.Response.Success(new
        {
            x = topLeft.X,
            y = topLeft.Y,
            w = bottomRight.X - topLeft.X,
            h = bottomRight.Y - topLeft.Y,
        });
    }
}
