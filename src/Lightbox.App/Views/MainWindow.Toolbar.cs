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
    // ---- toolbar ---------------------------------------------------------------

    /// <summary>Map the VM's tool + selection variant onto the canvas input mode.</summary>
    private void SyncCanvasToolMode()
    {
        Canvas.ToolMode = _vm.ActiveTool switch
        {
            ToolId.Fill => Rendering.CanvasControl.CanvasToolMode.Fill,
            ToolId.Picker => Rendering.CanvasControl.CanvasToolMode.Pick,
            ToolId.Gradient => Rendering.CanvasControl.CanvasToolMode.Gradient,
            ToolId.Shape => Rendering.CanvasControl.CanvasToolMode.Shape,
            ToolId.Move => Rendering.CanvasControl.CanvasToolMode.Move,
            ToolId.Bone => Rendering.CanvasControl.CanvasToolMode.Bone,
            // Reviving the object-selection mode rather than adding a parallel
            // one. It has been in the enum with its whole hit-test chain since
            // the selection manager landed, and nothing ever assigned it — so
            // picking a placement, guide or anchor has been unreachable. The
            // black arrow is what that code was always for.
            ToolId.Arrow => Rendering.CanvasControl.CanvasToolMode.Select,
            ToolId.DirectSelect => Rendering.CanvasControl.CanvasToolMode.PathEdit,
            ToolId.Pen => Rendering.CanvasControl.CanvasToolMode.Pen,
            ToolId.Width => Rendering.CanvasControl.CanvasToolMode.Width,
            ToolId.Select => _vm.ActiveSelectVariant switch
            {
                SelectVariant.Polygon => Rendering.CanvasControl.CanvasToolMode.SelectPolygon,
                SelectVariant.Box => Rendering.CanvasControl.CanvasToolMode.SelectRect,
                SelectVariant.Ellipse => Rendering.CanvasControl.CanvasToolMode.SelectEllipse,
                SelectVariant.Wand => Rendering.CanvasControl.CanvasToolMode.SelectWand,
                _ => Rendering.CanvasControl.CanvasToolMode.SelectFreehand,
            },
            _ => Rendering.CanvasControl.CanvasToolMode.Paint,
        };
        // The Bone tool IS the rig mode (Q81): its chrome exists while the
        // tool is active and not a moment longer.
        _vm.ArmatureEditMode = _vm.ActiveTool == ToolId.Bone;
    }

    /// <summary>
    /// Toolbar width decides its shape: two icon columns when narrow, one
    /// full-width column when widened, icon + tool name when wider still.
    /// </summary>
    private void OnToolbarSizeChanged(object? sender, SizeChangedEventArgs e) =>
        ReflowToolRail(e.NewSize.Width, e.NewSize.Height);

    /// <summary>
    /// The rail's column count follows the window (Q56): one column when the
    /// window is tall enough to hold every tool in it, two as the ordinary
    /// case, three when the window is short and the rail wide enough — and
    /// the columns sit centred. Dragged past 150 px the rail becomes the
    /// labelled single-column list it always was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two modes, told apart by whether the column is still <c>Auto</c>.</b>
    /// Until the rail is dragged it sizes itself: the column count comes from
    /// the height alone and the column then measures to exactly that many
    /// tiles. Deriving the count from the width as well would be circular now
    /// that the width follows the count — and the fixed 88px that used to break
    /// the circle is what left a one-column rail sitting in the middle of 27px
    /// of nothing down each side.
    /// </para>
    /// <para>
    /// A drag writes pixels into the column, and from then on the artist's
    /// width is the input and the count follows it, exactly as before. That is
    /// also the only way to reach the three-column and labelled layouts, which
    /// is what the manual has always said: three columns need the rail
    /// <i>dragged</i> wide enough.
    /// </para>
    /// </remarks>
    private void ReflowToolRail(double width, double height)
    {
        if (height <= 0) return;

        var chosen = !WorkArea.ColumnDefinitions[0].Width.IsAuto;
        if (chosen && width <= 0) return;

        if (chosen && width >= 150)
        {
            Toolbar.Classes.Set("labels", true);
            ToolButtons.ItemWidth = Math.Max(40, width - 14);
            ToolButtons.Width = double.NaN;
            return;
        }
        Toolbar.Classes.Set("labels", false);

        const double tile = 34;
        var visible = ToolButtons.Children.Count(c => c.IsVisible);
        if (visible == 0) return;
        var first = ToolButtons.Children.First(c => c.IsVisible);
        var tileH = first.Bounds.Height > 1 ? first.Bounds.Height + 4 : 32;

        // Self-sizing stops at two. A third column is worth the canvas it costs
        // only when somebody has asked for it, and asking is the drag.
        var most = chosen ? Math.Clamp((int)((width - 8) / tile), 1, 3) : 2;
        var cols = 1;
        while (cols < most && Math.Ceiling(visible / (double)cols) * tileH > height - 8) cols++;

        ToolButtons.ItemWidth = tile;
        ToolButtons.Width = cols * tile;
    }

    // Press-and-hold a tool button to list its variants (like Photoshop/Krita).
    private Avalonia.Threading.DispatcherTimer? _holdTimer;
    private bool _variantFlyoutOpened;

    /// <summary>
    /// Hold a tool button to get its list of variants.
    /// </summary>
    /// <remarks>
    /// One implementation for every tool that has variants, because the
    /// gesture has to be identical: a hold that works on Select and not on
    /// Shape is worse than no hold at all — the artist stops trusting it and
    /// goes to the options bar every time.
    /// </remarks>
    private void HoldToOpen(Control button, PointerPressedEventArgs e)
    {
        _variantFlyoutOpened = false;
        _holdButton = null;
        if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
        _holdTimer?.Stop();
        _holdTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer?.Stop();
            _variantFlyoutOpened = true;
            _holdButton = button;
            button.ContextFlyout?.ShowAt(button);
        };
        _holdTimer.Start();
    }

    private Control? _holdButton;

    private void OnSelectToolPressed(object? sender, PointerPressedEventArgs e) =>
        HoldToOpen(SelectToolButton, e);

    private void OnShapeToolPressed(object? sender, PointerPressedEventArgs e) =>
        HoldToOpen(ShapeToolButton, e);

    private void OnSelectToolReleased(object? sender, PointerReleasedEventArgs e)
    {
        _holdTimer?.Stop();
        // The hold already opened the variant list — don't also register a click.
        if (_variantFlyoutOpened) e.Handled = true;
    }

    /// <summary>
    /// Close the hold-list after a variant has been picked.
    /// </summary>
    /// <remarks>
    /// Posted, not called. <c>Button.OnClick</c> raises Click and only then
    /// runs the command, and closing the flyout from inside the Click handler
    /// tears the button out of the tree first — its <c>{Binding …Command}</c>
    /// loses its DataContext, resolves to null, and the command never runs.
    /// The list closed, nothing changed, and the tool options bar still showed
    /// the old variant, which is precisely what "the dropdown does nothing"
    /// looks like. Letting the click finish first fixes it.
    /// </remarks>
    private void OnVariantChosen(object? sender, RoutedEventArgs e)
    {
        var button = _holdButton ?? SelectToolButton;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => button.ContextFlyout?.Hide());
    }

    /// <summary>Brush-parameter flyout: categories on the left, one page visible at a time.</summary>
    private void OnBrushCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BrushPageGeneral is null) return; // template not built yet
        var index = BrushCategoryList.SelectedIndex;
        BrushPageGeneral.IsVisible = index == 0;
        BrushPageEffects.IsVisible = index == 1;
        BrushPageMedium.IsVisible = index == 2;
        BrushPagePressure.IsVisible = index == 3;
        BrushPagePresets.IsVisible = index == 4;

        if (index == 0) RefreshTipButton();
        if (index == 3) BuildPressureCurves();
        if (index == 4) RefreshPresetPage();
    }
}
