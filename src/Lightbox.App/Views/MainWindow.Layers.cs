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
    // ---- layer rename (double-click, both dockers) ---------------------------

    private void OnLayerNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LayerRow row) return;
        row.IsRenaming = true;
        if ((sender as Control)?.Parent is Panel panel)
        {
            var box = panel.Children.OfType<TextBox>().FirstOrDefault();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                box?.Focus();
                box?.SelectAll();
            });
        }
        e.Handled = true;
    }

    private void OnLayerNameLostFocus(object? sender, RoutedEventArgs e)
    {
        // The LostFocus binding has already committed the text by now.
        if ((sender as Control)?.DataContext is LayerRow row) row.IsRenaming = false;
    }

    // ---- layer folder rename / collapse ---------------------------------------

    private void OnGroupCollapseClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is GroupRow row) row.Collapsed = !row.Collapsed;
    }

    private void OnGroupNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not GroupRow row) return;
        row.IsRenaming = true;
        if ((sender as Control)?.Parent is Panel panel)
        {
            var box = panel.Children.OfType<TextBox>().FirstOrDefault();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                box?.Focus();
                box?.SelectAll();
            });
        }
        e.Handled = true;
    }

    private void OnGroupNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is GroupRow row) row.IsRenaming = false;
    }

    private void OnGroupNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not GroupRow row) return;
        switch (e.Key)
        {
            case Key.Enter:
                row.Name = box.Text ?? "";
                row.IsRenaming = false;
                e.Handled = true;
                break;
            case Key.Escape:
                box.Text = row.Name;
                row.IsRenaming = false;
                e.Handled = true;
                break;
        }
    }

    // ---- layer docker context menus (menu items inherit the row's DataContext) ----

    private static LayerRow? LayerRowOf(object? sender) =>
        (sender as Control)?.DataContext as LayerRow;

    private static GroupRow? GroupRowOf(object? sender) =>
        (sender as Control)?.DataContext as GroupRow;

    private void OnLayerMenuRename(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) row.IsRenaming = true;
    }

    private void OnLayerMenuMoveUp(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.MoveLayerUpCommand.Execute(row);
    }

    private void OnLayerMenuMoveDown(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.MoveLayerDownCommand.Execute(row);
    }

    private void OnLayerMenuNewFolder(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is not { } row) return;
        _vm.ActivateLayerCommand.Execute(row);
        _vm.CreateLayerFolderCommand.Execute(null);
    }

    private void OnLayerMenuRemoveFromFolder(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.RemoveLayerFromGroupCommand.Execute(row);
    }

    private void OnLayerMenuSelectAlpha(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.SelectLayerAlpha(row, add: false, subtract: false);
    }

    private void OnLayerMenuBlank(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.ClearLayerContent(row.Layer);
    }

    // ---- linking (Q90) --------------------------------------------------------------
    //
    // Each of these activates the row first. The menu belongs to a row and the
    // link operations are written in terms of the ACTIVE layer, so a menu
    // opened on a row that is not active would otherwise link the wrong one —
    // which is worse than doing nothing, because it does something.

    private void OnLayerMenuLinkAbove(object? sender, RoutedEventArgs e) =>
        OnActiveRow(sender, () => _vm.LinkLayerAboveCommand.Execute(null));

    private void OnLayerMenuLinkBelow(object? sender, RoutedEventArgs e) =>
        OnActiveRow(sender, () => _vm.LinkLayerBelowCommand.Execute(null));

    private void OnLayerMenuUnlink(object? sender, RoutedEventArgs e) =>
        OnActiveRow(sender, () => _vm.UnlinkLayerCommand.Execute(null));

    private void OnLayerMenuShareRig(object? sender, RoutedEventArgs e) =>
        OnActiveRow(sender, () => _vm.LinkCarriesBones = !_vm.LinkCarriesBones);

    private void OnLayerMenuShareAlpha(object? sender, RoutedEventArgs e) =>
        OnActiveRow(sender, () => _vm.LinkCarriesAlpha = !_vm.LinkCarriesAlpha);

    private void OnLayerMenuShareVisibility(object? sender, RoutedEventArgs e) =>
        OnActiveRow(sender, () => _vm.LinkCarriesVisibility = !_vm.LinkCarriesVisibility);

    private void OnLayerMenuRigToBone(object? sender, RoutedEventArgs e) =>
        OnActiveRow(sender, () => _vm.RigLayerToSelectedBoneCommand.Execute(null));

    private void OnLayerMenuRigToSkeleton(object? sender, RoutedEventArgs e) =>
        OnActiveRow(sender, () => _vm.RigLayerToSkeletonCommand.Execute(null));

    private void OnLayerMenuUnrig(object? sender, RoutedEventArgs e) =>
        OnActiveRow(sender, () => _vm.UnrigLayerCommand.Execute(null));

    private void OnActiveRow(object? sender, Action act)
    {
        if (LayerRowOf(sender) is not { } row) return;
        _vm.SelectLayer(row, toggle: false, range: false);
        act();
    }

    private void OnLayerMenuDelete(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.DeleteLayer(row.Layer);
    }

    private void OnLayerMenuMergeDown(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) RequestMergeDown(row.Layer);
    }

    /// <summary>
    /// Merge a layer into the one below, asking first when the merge would
    /// turn drawings into pixels — the Q52 warning, shown only when AI is
    /// enabled because "the inbetweener cannot read pixels" is noise without
    /// one. A merge that keeps every stroke record just happens.
    /// </summary>
    private async void RequestMergeDown(Lightbox.Core.Documents.Layer? layer)
    {
        if (_vm.AiEnabled && _vm.MergeWouldBake(layer))
        {
            var below = _vm.MergeTargetOf(layer);
            var go = await AskImportChoice(
                "Merge layer down?",
                $"Blend modes, opacity or erasers here mean the merged drawings become pixels. "
                + $"The AI inbetweener reads strokes and cannot read pixels, so it will skip "
                + $"what lands on “{below?.Name}”.",
                ("Merge", "The drawings that need it are flattened to pixels; the rest keep their strokes.", true));
            if (go != true) return;
        }
        _vm.MergeLayerDown(layer);
    }

    // Three states rather than a checkbox, because "leave it to the export" and
    // "keep this in whatever the export decides" are genuinely different answers and
    // a two-state control cannot say both.
    private void OnLayerMenuExportAuto(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.SetLayerExportPin(row.Layer, null);
    }

    private void OnLayerMenuExportNever(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.SetLayerExportPin(row.Layer, true);
    }

    private void OnLayerMenuExportAlways(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.SetLayerExportPin(row.Layer, false);
    }

    private void OnGroupMenuRename(object? sender, RoutedEventArgs e)
    {
        if (GroupRowOf(sender) is { } row) row.IsRenaming = true;
    }

    private void OnGroupColorClicked(object? sender, RoutedEventArgs e)
    {
        if (GroupRowOf(sender) is { } row && (sender as Control)?.Tag is string hex)
            row.Color = hex;
    }

    private void OnGroupMenuCollapse(object? sender, RoutedEventArgs e)
    {
        if (GroupRowOf(sender) is { } row) row.Collapsed = !row.Collapsed;
    }

    private void OnGroupMenuAddActive(object? sender, RoutedEventArgs e)
    {
        if (GroupRowOf(sender) is { } row) _vm.AddActiveLayerToGroupCommand.Execute(row);
    }

    private void OnGroupMenuDissolve(object? sender, RoutedEventArgs e)
    {
        if (GroupRowOf(sender) is { } row) _vm.DissolveGroupCommand.Execute(row);
    }

    private void OnLayerNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not LayerRow row) return;
        switch (e.Key)
        {
            case Key.Enter:
                row.Name = box.Text ?? ""; // commit through the row's write-through
                row.IsRenaming = false;
                e.Handled = true;
                break;
            case Key.Escape:
                box.Text = row.Name; // revert, so the LostFocus commit is a no-op
                row.IsRenaming = false;
                e.Handled = true;
                break;
        }
    }
}
