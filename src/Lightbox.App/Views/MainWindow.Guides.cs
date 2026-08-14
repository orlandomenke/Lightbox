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
    // ---- guides -------------------------------------------------------------------

    /// <summary>
    /// Hand the canvas the guides to draw, or null when there are none.
    /// </summary>
    /// <remarks>
    /// A vanishing point constrains a continuum of directions, so it is
    /// flattened here into an evenly spread fan. Drawing every direction would
    /// be a filled disc; twenty-four is enough to read as perspective and few
    /// enough to see the drawing through. The renderer stays dumb about what a
    /// guide means, which is what keeps it off the document objects.
    /// </remarks>
    private void RefreshGuides()
    {
        // The rulers carry a mark per guide, so they move with them.
        RefreshRulerMarks();

        if (!_vm.HasGuides || !_vm.Workspace.GuidesVisible)
        {
            Canvas.Guides = null;
            return;
        }
        var lines = new List<Rendering.CanvasControl.GuideLine>();
        foreach (var guide in _vm.Guides)
        {
            if (!guide.Visible) continue;
            var angles = guide.Kind == GuideKind.VanishingPoint
                ? Fan()
                : guide.Angles;
            lines.Add(new Rendering.CanvasControl.GuideLine(
                guide.Id, (int)guide.Kind, (float)guide.X, (float)guide.Y,
                (float)guide.Spacing, angles, guide.Name, guide.Divisions ?? 0));
        }
        Canvas.Guides = lines.Count > 0 ? lines : null;
    }

    /// <summary>
    /// Add an anchor in the middle of the canvas.
    /// </summary>
    /// <remarks>
    /// <b>B58.</b> The centre rather than the pointer, because a menu item has no
    /// pointer position to speak of — it is the place you can always find, and the
    /// mark is draggable the instant it exists. Placing by click is the canvas's
    /// job and arrives with <c>RigEmptyPressed</c>.
    /// </remarks>
    private void OnAddRigAnchor(object? sender, RoutedEventArgs e)
    {
        var (x, y) = (_vm.Doc.Scene.Width / 2.0, _vm.Doc.Scene.Height / 2.0);
        _vm.SelectedRigMarkId = _vm.AddAnchorAt($"anchor {(_vm.Doc.Scene.Anchors?.Count ?? 0) + 1}", x, y);
        RefreshRigOverlay();
    }

    private void OnAddRigShape(object? sender, RoutedEventArgs e)
    {
        // A quarter of the canvas, centred: big enough to see and grab, small
        // enough not to be mistaken for the frame.
        var w = _vm.Doc.Scene.Width / 4.0;
        var h = _vm.Doc.Scene.Height / 4.0;
        _vm.SelectedRigMarkId = _vm.AddShapeAt(
            $"shape {(_vm.Doc.Scene.Shapes?.Count ?? 0) + 1}",
            (_vm.Doc.Scene.Width - w) / 2, (_vm.Doc.Scene.Height - h) / 2, w, h);
        RefreshRigOverlay();
    }

    private void OnDeleteRigMark(object? sender, RoutedEventArgs e)
    {
        _vm.DeleteSelectedRigMark();
        RefreshRigOverlay();
    }

    /// <summary>
    /// Hand the canvas the marks to draw, or nothing at all.
    /// </summary>
    /// <remarks>
    /// <b>B58.</b> One line of plumbing, and its absence is what made a whole
    /// feature — thirty tests, a hit-tester, a drag solver and an editor path —
    /// unreachable. `RigMarks` already returns an empty list when the mode is off,
    /// so null and absent are the same thing here and neither needs a mode check.
    /// </remarks>
    private void RefreshRigOverlay()
    {
        Canvas.RigEditMode = _vm.RigEditMode;
        var marks = _vm.RigMarks;
        Canvas.RigMarks = marks.Count > 0 ? marks : null;
    }

    /// <inheritdoc cref="RefreshRigOverlay"/>
    private void RefreshArmatureOverlay()
    {
        var bones = _vm.BoneChromes;
        Canvas.BoneChromes = bones.Count > 0 ? bones : null;
        var heat = _vm.HeatPoints;
        Canvas.HeatPoints = heat.Count > 0 ? heat : null;
    }

    private static IReadOnlyList<double> Fan()
    {
        const int rays = 24;
        var angles = new double[rays];
        for (var i = 0; i < rays; i++) angles[i] = i * (180.0 / rays);
        return angles;
    }

    // ---- rulers ------------------------------------------------------------------

    /// <summary>
    /// Hook the two ruler strips up to the canvas.
    /// </summary>
    /// <remarks>
    /// Everything the rulers do runs through here: the mapping they draw
    /// against, the pointer they track, the guides they mark, and the drag
    /// that pulls a new guide out of one.
    /// </remarks>
    private void InitialiseRulers()
    {
        Canvas.ViewChanged += RefreshRulerMapping;
        CanvasHost.SizeChanged += (_, _) => RefreshRulerMapping();

        // Tracking the pointer is most of what a ruler is for — reading a
        // tick to work out where you are is slower than glancing at a line
        // that is already there. Handled events too: every tool marks its
        // moves handled, and the ruler has to follow all of them.
        Canvas.AddHandler(
            PointerMovedEvent,
            (_, e) => TrackPointer(e.GetPosition(Canvas)),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        Canvas.PointerExited += (_, _) => TrackPointer(null);

        foreach (var strip in (RulerStrip[])[TopRuler, LeftRuler])
        {
            strip.PullStarted += (s, p) => UpdateDraftGuide(s, p);
            strip.PullMoved += (s, p) => UpdateDraftGuide(s, p);
            strip.PullEnded += EndPull;
        }

        Canvas.ContentMoveStarted += (x, y, wholeLayer) => _vm.BeginMove(x, y, wholeLayer);
        Canvas.ContentMoveUpdated += (x, y, axisLock) => _vm.UpdateMove(x, y, axisLock);
        Canvas.ContentMoveEnded += _vm.EndMove;
        Canvas.ContentMoveCancelled += _vm.CancelMove;

        Canvas.GuidesMovedStarted += _vm.BeginGuidesMove;
        Canvas.GuidesMoved += (dx, dy) => _vm.UpdateGuidesMove(dx, dy);
        Canvas.GuidesMovedEnded += _vm.EndGuidesMove;

        Canvas.RefBoxesMoveStarted += _vm.BeginRefBoxesMove;
        Canvas.RefBoxesMoved += (dx, dy) => _vm.UpdateRefBoxesMove(dx, dy);
        Canvas.RefBoxesMovedEnded += _vm.EndRefBoxesMove;

        Canvas.AnchorsMoveStarted += _vm.BeginAnchorsMove;
        Canvas.AnchorsMoved += (dx, dy) => _vm.UpdateAnchorsMove(dx, dy);
        Canvas.AnchorsMovedEnded += _vm.EndAnchorsMove;

        Canvas.ShapesMoveStarted += _vm.BeginShapesMove;
        Canvas.ShapesMoved += (dx, dy) => _vm.UpdateShapesMove(dx, dy);
        Canvas.ShapesMovedEnded += _vm.EndShapesMove;

        Canvas.GuideMoved += (id, dx, dy) =>
        {
            if (GuideById(id) is not { } guide) return;
            // Remembered so the release can close the drag off: the canvas
            // only knows an id, and by then the guide it names may have been
            // replaced by an undo.
            _draggingGuide = guide;
            _vm.DragGuide(guide, dx, dy);
        };
        Canvas.GuideDragEnded += () =>
        {
            if (_draggingGuide is { } guide) _vm.EndGuideDrag(guide);
            _draggingGuide = null;
        };
        Canvas.GuideResized += (id, dy) =>
        {
            if (GuideById(id) is not { } guide) return;
            _draggingGuide = guide;
            _vm.DragHeightScaleTop(guide, dy);
        };
        Canvas.GuideResizeEnded += () =>
        {
            if (_draggingGuide is { } guide) _vm.EndHeightScaleResize(guide);
            _draggingGuide = null;
        };

        _vm.Workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(WorkspaceViewModel.RulersVisible)
                or nameof(WorkspaceViewModel.GuidesVisible)
                or nameof(WorkspaceViewModel.GuidesLocked))
            {
                ApplyRulers();
            }
        };
        ApplyRulers();
    }

    private Guide? _draggingGuide;

    private Guide? GuideById(string id) => _vm.Guides.FirstOrDefault(g => g.Id == id);

    /// <summary>
    /// Put the rulers on or take them away, and say whether guides can be
    /// picked up.
    /// </summary>
    /// <remarks>
    /// The canvas is inset by the strips rather than sharing a grid with them,
    /// so its own coordinates line up with each strip's along that strip's
    /// axis and the mapping needs no offset. Absent, not disabled: with the
    /// rulers off the canvas gets its whole cell back.
    /// </remarks>
    private void ApplyRulers()
    {
        var on = _vm.Workspace.RulersVisible;
        Canvas.Margin = on
            ? new Thickness(RulerStrip.Thickness, RulerStrip.Thickness, 0, 0)
            : default;
        RefreshGuideGrab();
        RefreshGuides();
        RefreshRulerMapping();
    }

    /// <summary>
    /// Whether a guide under the pointer can be picked up.
    /// </summary>
    /// <remarks>
    /// The Move tool's job, and only its job. The rulers used to carry it,
    /// which worked but put the switch a long way from the gesture: you had to
    /// know that showing a strip of numbers was also what made the rig
    /// draggable. A tool says it plainly, is visible in the palette, and is
    /// still the answer with the rulers down — which is most of the time.
    /// Locking or hiding the guides still overrides it, because both of those
    /// mean "leave the rig alone" whatever tool is in hand.
    /// </remarks>
    /// <summary>
    /// How much one press of <c>[</c> or <c>]</c> moves the brush.
    /// </summary>
    /// <remarks>
    /// Proportional, not a fixed step. One pixel at a time is right at size 3
    /// and useless at 300, and a flat ten is the other way round; a tenth of
    /// the current size takes about the same number of presses to double or
    /// halve wherever you start from.
    /// </remarks>
    private static double BrushSizeStep(double size) => Math.Max(1, Math.Round(size / 10));

    private void RefreshGuideGrab() =>
        Canvas.GuideDragEnabled =
            _vm.IsMoveTool && _vm.Workspace.GuidesVisible && !_vm.Workspace.GuidesLocked;

    private void RefreshRulerMapping()
    {
        if (!_vm.Workspace.RulersVisible) return;
        TopRuler.Mapping = Canvas.AxisMapping(horizontal: true);
        LeftRuler.Mapping = Canvas.AxisMapping(horizontal: false);
    }

    /// <summary>
    /// Mark each guide on the ruler it crosses.
    /// </summary>
    /// <remarks>
    /// Only the straight ones: a grid crosses the top ruler everywhere and a
    /// vanishing point is a place on the canvas rather than a position along
    /// an edge, so marking either would be noise where the marks have to stay
    /// readable.
    /// </remarks>
    private void RefreshRulerMarks()
    {
        if (!_vm.Workspace.RulersVisible) return;
        var horizontal = new List<double>();
        var vertical = new List<double>();
        if (_vm.Workspace.GuidesVisible)
        {
            foreach (var guide in _vm.Guides)
            {
                if (!guide.Visible || guide.Kind != GuideKind.Line) continue;
                var angle = ((guide.Angle % 180) + 180) % 180;
                if (angle < 1 || angle > 179) horizontal.Add(guide.Y);
                else if (Math.Abs(angle - 90) < 1) vertical.Add(guide.X);
            }
        }
        // A horizontal guide crosses the *left* ruler, at its height.
        TopRuler.Marks = vertical;
        LeftRuler.Marks = horizontal;
    }

    private void TrackPointer(Point? view)
    {
        if (!_vm.Workspace.RulersVisible) return;
        if (view is not { } p)
        {
            TopRuler.Tracking = null;
            LeftRuler.Tracking = null;
            return;
        }
        var (x, y) = Canvas.ViewToDoc(p);
        TopRuler.Tracking = x;
        LeftRuler.Tracking = y;
    }

    /// <summary>
    /// Follow a guide being pulled out of a ruler.
    /// </summary>
    /// <remarks>
    /// The axis that matters is the other one: dragging out of the top ruler
    /// places a horizontal guide, and where it lands is the pointer's height,
    /// not its position along the ruler. Which is why the strip hands over a
    /// raw point and this works in the canvas's coordinates.
    /// </remarks>
    private void UpdateDraftGuide(RulerStrip strip, Point inStrip)
    {
        var (x, y) = DocOf(strip, inStrip);
        var horizontal = strip.Orientation == Avalonia.Layout.Orientation.Horizontal;
        Canvas.DraftGuide = new Rendering.CanvasControl.GuideLine(
            "draft", (int)GuideKind.Line, (float)x, (float)y, 0,
            horizontal ? [0d] : [90d]);
    }

    private void EndPull(RulerStrip strip, Point inStrip, bool onStrip)
    {
        Canvas.DraftGuide = null;
        // Let go back over the ruler and it never existed. That is how
        // Photoshop throws a guide away, and it doubles as the way out of a
        // drag you did not mean to start.
        if (onStrip) return;
        var (x, y) = DocOf(strip, inStrip);
        _vm.AddGuide(GuideKind.Line, x, y,
            angle: strip.Orientation == Avalonia.Layout.Orientation.Horizontal ? 0 : 90);
    }

    private (double X, double Y) DocOf(RulerStrip strip, Point inStrip) =>
        Canvas.ViewToDoc(strip.TranslatePoint(inStrip, Canvas) ?? inStrip);


    /// <summary>
    /// New guides land in the middle of the canvas.
    /// </summary>
    /// <remarks>
    /// Somewhere visible, so the artist can see what appeared and drag it where
    /// they meant. A guide placed at the origin on a large canvas is a guide
    /// that looks like nothing happened.
    /// </remarks>
    private (double X, double Y) CanvasMiddle() => (_vm.Doc.Scene.Width / 2.0, _vm.Doc.Scene.Height / 2.0);

    private void OnAddHorizontalGuide(object? sender, RoutedEventArgs e)
    {
        var (x, y) = CanvasMiddle();
        _vm.AddGuide(GuideKind.Line, x, y);
    }

    private void OnAddVerticalGuide(object? sender, RoutedEventArgs e)
    {
        var (x, y) = CanvasMiddle();
        _vm.AddGuide(GuideKind.Line, x, y, angle: 90);
    }

    private void OnAddGridGuide(object? sender, RoutedEventArgs e) =>
        // From the origin, because a grid is a lattice over the whole canvas
        // and starting it in the middle would put its intersections in
        // half-cell offsets from every edge. The pitch comes from the
        // configuration — Edit ▸ Configure ▸ Guides and grid.
        _vm.AddGuide(GuideKind.Grid, 0, 0, spacing: _vm.GridSpacing);

    private void OnAddIsometricGuide(object? sender, RoutedEventArgs e)
    {
        var (x, y) = CanvasMiddle();
        _vm.AddGuide(GuideKind.Isometric, x, y);
    }

    private void OnAddVanishingPoint(object? sender, RoutedEventArgs e)
    {
        // On the horizon — a third of the way down is where one usually is —
        // and off to one side, since a VP directly ahead gives you nothing to
        // draw along.
        var scene = _vm.Doc.Scene;
        _vm.AddGuide(GuideKind.VanishingPoint, scene.Width * 0.15, scene.Height / 3.0);
    }

    /// <summary>
    /// A character height chart, standing where a character would: feet near
    /// the bottom of the canvas, six heads tall until the artist says
    /// otherwise.
    /// </summary>
    private void OnAddHeightScale(object? sender, RoutedEventArgs e)
    {
        var scene = _vm.Doc.Scene;
        const int divisions = 6;
        // Seven tenths of the canvas height, so the chart is unmistakably a
        // figure and still leaves headroom to pull the top up.
        var unit = scene.Height * 0.7 / divisions;
        _vm.AddGuide(
            GuideKind.HeightScale, scene.Width / 2.0, scene.Height * 0.85,
            spacing: unit, divisions: divisions);
    }

    /// <summary>
    /// The two named horizontals of a layout: plain lines, pre-named so the
    /// label says which is which on a rig somebody else set up.
    /// </summary>
    private void OnAddEyeLine(object? sender, RoutedEventArgs e)
    {
        var (x, y) = CanvasMiddle();
        _vm.AddGuide(GuideKind.Line, x, y, name: "Eye line");
    }

    private void OnAddHorizonLine(object? sender, RoutedEventArgs e) =>
        // A third of the way down, where the vanishing points already assume
        // it is.
        _vm.AddGuide(
            GuideKind.Line, _vm.Doc.Scene.Width / 2.0, _vm.Doc.Scene.Height / 3.0,
            name: "Horizon");

    /// <summary>
    /// Open the guide-set editor. It needs a project to keep the sets in, and
    /// says so rather than opening onto controls that cannot work.
    /// </summary>
    private void OnGuideSets(object? sender, RoutedEventArgs e)
    {
        if (_vm.ProjectDocker.Project is null)
        {
            _vm.AiStatus = "Guide sets live in a project — open or create one first.";
            return;
        }
        _vm.NotifyGuideSetOffers();
        new GuideSetEditor(_vm).ShowDialog(this);
    }
}
