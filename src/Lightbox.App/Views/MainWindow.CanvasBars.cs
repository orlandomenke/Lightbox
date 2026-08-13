using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using static Lightbox.App.Views.PlacementChoiceDialog;

namespace Lightbox.App.Views;

/// <summary>Part of the MainWindow code-behind — see MainWindow.axaml.cs.</summary>
/// <remarks>
/// Split out of <c>MainWindow.axaml.cs</c> under Q76, which was 5,706 lines across 37
/// sections with 79% of its fields touched by exactly one of them. Every field this
/// file uses is either declared here or in the shared block at the top of
/// <c>MainWindow.axaml.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainWindow
{
    // ---- the bars on the canvas -----------------------------------------------

    private IEnumerable<(OverlayId Id, CanvasOverlayBar Bar)> OverlayBars() =>
    [
        (OverlayId.View, ViewBar),
        (OverlayId.Shortcuts, ShortcutBar),
    ];

    private void InitialiseOverlays()
    {
        foreach (var (_, bar) in OverlayBars())
        {
            bar.DragHost = CanvasHost;
            // Live, not just on release. A bar that only jumps when you let go
            // makes you drop it to find out where it would land, which is not
            // a drag — it is a guess followed by an undo.
            bar.Dragging += (b, at) => PositionOverlay(b, EdgeAt(at), AlongAt(at));
            bar.Dropped += (b, at) =>
                _vm.Workspace.PlaceOverlay(b.OverlayId, EdgeAt(at), AlongAt(at));
            bar.CloseRequested += b => _vm.Workspace.SetOverlayVisible(b.OverlayId, false);
            bar.PropertyChanged += (_, e) =>
            {
                if (e.Property == CanvasOverlayBar.CollapsedProperty)
                {
                    _vm.Workspace.SetOverlayCollapsed(bar.OverlayId, bar.Collapsed);
                }
            };
        }
    }

    private CanvasEdge EdgeAt(Point at) =>
        CanvasOverlayLayout.NearestEdge(at.X, at.Y, CanvasHost.Bounds.Width, CanvasHost.Bounds.Height);

    private double AlongAt(Point at) =>
        CanvasOverlayLayout.AlongFor(
            EdgeAt(at), at.X, at.Y, CanvasHost.Bounds.Width, CanvasHost.Bounds.Height);

    /// <summary>Read the workspace and put every bar where it says.</summary>
    private void ApplyOverlayLayout()
    {
        var overlays = _vm.Workspace.Layout.Overlays;
        foreach (var (id, bar) in OverlayBars())
        {
            var placement = overlays.Place(id);
            bar.IsVisible = placement.Visible;
            if (!placement.Visible) continue;
            bar.Collapsed = placement.Collapsed;
            PositionOverlay(bar, placement.Edge, placement.Along);
        }
    }

    /// <summary>
    /// Put one bar on an edge without touching the workspace — the live half
    /// of a drag, and the mechanism the committed placement uses too.
    /// </summary>
    /// <remarks>
    /// Alignment pins the bar to its edge. The other axis is a lopsided
    /// margin: a fraction of the room left over once the bar's own size is
    /// taken out, which is what turns "0.25 along the top" into a position
    /// without the bar ever hanging off the end.
    /// </remarks>
    private void PositionOverlay(CanvasOverlayBar bar, CanvasEdge edge, double along)
    {
        bar.Edge = edge;
        var vertical = CanvasOverlayLayout.IsVertical(edge);
        const double Gap = 8;

        bar.HorizontalAlignment = edge switch
        {
            CanvasEdge.Right => Avalonia.Layout.HorizontalAlignment.Right,
            _ => Avalonia.Layout.HorizontalAlignment.Left,
        };
        bar.VerticalAlignment = edge switch
        {
            CanvasEdge.Bottom => Avalonia.Layout.VerticalAlignment.Bottom,
            _ => Avalonia.Layout.VerticalAlignment.Top,
        };

        var slack = vertical
            ? Math.Max(0, CanvasHost.Bounds.Height - 2 * Gap - bar.Bounds.Height)
            : Math.Max(0, CanvasHost.Bounds.Width - 2 * Gap - bar.Bounds.Width);
        var offset = Gap + Math.Clamp(along, 0, 1) * slack;
        bar.Margin = vertical
            ? new Thickness(Gap, offset, Gap, Gap)
            : new Thickness(offset, Gap, Gap, Gap);
    }

    /// <summary>
    /// Whether a panel makes sense right now, regardless of where the layout
    /// puts it: the project tree needs a project, and the timeline means
    /// nothing on a reference tab.
    /// </summary>
    private bool IsPanelUsable(DockPanelId id) => id switch
    {
        DockPanelId.Project => _vm.HasProject,
        // Symbols are *not* gated. A project symbol needs a project, but the
        // artist's own library does not — it is theirs, and it should be there
        // when they open the app to draw one picture. Placing one into a loose
        // document copies it into Doc.Symbols, so the file still stands alone.
        // The project tree above stays gated, because without a project it has
        // literally nothing to show.
        DockPanelId.Timeline => _vm.ShowTimeline,
        _ => true,
    };

    private static void Detach(Control child)
    {
        switch (child.Parent)
        {
            case Panel panel: panel.Children.Remove(child); break;
            case ContentControl host when ReferenceEquals(host.Content, child): host.Content = null; break;
            case Decorator d when ReferenceEquals(d.Child, child): d.Child = null; break;
        }
    }

    private void Park(Docker panel)
    {
        Detach(panel);
        if (!PanelPool.Children.Contains(panel)) PanelPool.Children.Add(panel);
    }

    /// <summary>
    /// Open or collapse an edge. "Optional means absent, not disabled": an area
    /// with nothing in it takes no width, shows no splitter and costs no
    /// layout, so a workspace that never uses the left edge looks exactly like
    /// one that could not have.
    /// </summary>
    private void SizeArea(DockSide side, DockLayout layout, double? cap, bool occupied)
    {
        var extent = layout.AreaExtents.TryGetValue(side, out var saved) && saved > 40
            ? saved
            : side is DockSide.Left or DockSide.Right ? 300 : 280;

        switch (side)
        {
            case DockSide.Left:
                Collapse(LeftHost, LeftSplitter, occupied);
                SizeColumn(WorkArea.ColumnDefinitions[2], occupied, extent, cap);
                break;
            case DockSide.Right:
                Collapse(RightHost, RightSplitter, occupied);
                SizeColumn(WorkArea.ColumnDefinitions[6], occupied, extent, cap);
                break;
            case DockSide.Top:
                Collapse(TopHost, TopSplitter, occupied);
                SizeRow(RootGrid.RowDefinitions[0], occupied, extent, cap);
                break;
            default:
                Collapse(BottomHost, BottomSplitter, occupied);
                // The bottom strip lives inside the centre column now, so the
                // sidebars keep their full height beside it.
                SizeRow(CentreColumn.RowDefinitions[2], occupied, extent, cap);
                break;
        }
    }

    private static void Collapse(Control host, Control splitter, bool occupied)
    {
        host.IsVisible = occupied;
        splitter.IsVisible = occupied;
    }

    private static void SizeColumn(ColumnDefinition col, bool occupied, double extent, double? cap)
    {
        if (!occupied)
        {
            col.MinWidth = 0;
            col.MaxWidth = double.PositiveInfinity;
            col.Width = new GridLength(0, GridUnitType.Pixel);
            return;
        }
        col.MinWidth = 180;
        // A capped strip holds only fixed-size controls, so widening it just
        // adds whitespace. Uncapped panels — the layer stack, the project tree
        // — genuinely use the room, and remove the ceiling for the whole strip.
        col.MaxWidth = cap ?? double.PositiveInfinity;
        col.Width = new GridLength(cap is { } c ? Math.Min(extent, c) : extent, GridUnitType.Pixel);
    }

    private static void SizeRow(RowDefinition row, bool occupied, double extent, double? cap)
    {
        if (!occupied)
        {
            row.MinHeight = 0;
            row.MaxHeight = double.PositiveInfinity;
            row.Height = new GridLength(0, GridUnitType.Pixel);
            return;
        }
        row.MinHeight = 120;
        row.MaxHeight = cap ?? double.PositiveInfinity;
        row.Height = new GridLength(cap is { } c ? Math.Min(extent, c) : extent, GridUnitType.Pixel);
    }

    /// <summary>
    /// A one-field modal. Returns null when the user cancels — which is not
    /// the same as an empty string, and the callers rely on the difference.
    /// </summary>
    /// <remarks>
    /// The dialog itself is <see cref="TextPrompt"/>, shared with the project
    /// window since it started creating documents and folders too.
    /// </remarks>
    private Task<string?> PromptForText(string title, string label, string initial) =>
        TextPrompt.ShowAsync(this, title, label, initial);

    private void OnAutosaveOff(object? sender, RoutedEventArgs e) => _vm.AutosaveMinutes = 0;

    private void OnAutosaveHalfMinute(object? sender, RoutedEventArgs e) => _vm.AutosaveMinutes = 0.5;

    private void OnAutosaveMinute(object? sender, RoutedEventArgs e) => _vm.AutosaveMinutes = 1;

    private void OnAutosaveFiveMinutes(object? sender, RoutedEventArgs e) => _vm.AutosaveMinutes = 5;

    private void OnAutosaveFifteenMinutes(object? sender, RoutedEventArgs e) => _vm.AutosaveMinutes = 15;

    // ---- the gradient ramp editor ------------------------------------------------

    /// <summary>
    /// The ramp draws and reports; every edit it reports lands in the view
    /// model, and therefore in the undo history. A control that mutated the
    /// document directly would make dragging a stop the one change Ctrl+Z
    /// could not reach.
    /// </summary>
    /// <remarks>
    /// Found by name at first use rather than wired at construction: the ramp
    /// lives inside a Flyout, and a flyout's content is not built until it is
    /// first opened.
    /// </remarks>

    private void WireGradientRamp()
    {
        // The panel's copy exists from the start; the toolbar's lives in a
        // flyout, whose content is not built until it is first opened.
        Bind(PanelRampEditor);
        if (GradientPreviewButton.Flyout is Flyout flyout)
        {
            flyout.Opened += (_, _) =>
            {
                if (flyout.Content is Control content
                    && content.FindControl<GradientRamp>("RampEditor") is { } ramp)
                {
                    Bind(ramp);
                }
            };
        }
    }

    private readonly HashSet<GradientRamp> _wiredRamps = [];

    private void Bind(GradientRamp? ramp)
    {
        if (ramp is null || !_wiredRamps.Add(ramp)) return;
        var gradients = _vm.GradientDocker;
        ramp.StopAdded += (track, at) => gradients.AddStopAt(track == RampTrack.Alpha, at);
        ramp.StopMoved += (stop, at) =>
            gradients.MoveStop(stop.Track == RampTrack.Alpha, stop.Index, at);
        ramp.SelectionChanged += stop =>
        {
            if (stop is { } s) gradients.Select(s.Track == RampTrack.Alpha, s.Index);
        };
        ramp.StopRemoved += stop =>
        {
            gradients.RemoveStopAt(stop.Track == RampTrack.Alpha, stop.Index);
            ramp.Selection = null;
        };
    }
}
