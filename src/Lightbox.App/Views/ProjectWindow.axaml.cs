using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Views;

/// <summary>
/// The project window — Q29's second surface, in its own window by Q41.
/// </summary>
/// <remarks>
/// <para>
/// Almost nothing here. The window owns the multi-selection, because that is
/// the one thing a <c>ListBox</c> holds and a view model cannot be handed, and
/// everything else is <see cref="ProjectWindowViewModel"/> — which is what lets
/// the bulk edits, the filters and the counts be tested with no window at all.
/// </para>
/// <para>
/// Opened as a dialog on the main window, the way Configure and Export are.
/// Modal because it edits the same project the docker is showing, and two
/// surfaces writing one manifest with neither knowing about the other is the
/// class of bug B61 was.
/// </para>
/// </remarks>
public partial class ProjectWindow : Window
{
    private readonly ProjectWindowViewModel _vm;

    public ProjectWindow(Project project, Action? changed = null)
    {
        _vm = new ProjectWindowViewModel(project, changed);
        // B65's split, same as the docker: the window supplies the dialog and
        // the decision lives on the view model, so the cancel path is testable.
        _vm.AskName = (kind, suggested) =>
            TextPrompt.ShowAsync(this, $"New {kind}", "Name", suggested);
        // Opening a document has to take this window down with it: the dialog is
        // modal, so a tab opened behind it is a tab nobody can see. Supplied
        // here for the same reason AskName is — the decision is on the view
        // model, where a test can drive it, and the window is what can close.
        _vm.RequestClose = Close;
        DataContext = _vm;
        InitializeComponent();

        if (this.FindControl<ListBox>("StructureRows") is { } rows)
        {
            rows.SelectionChanged += OnStructureSelectionChanged;
        }
    }

    /// <summary>
    /// The window's state, so the owner can supply what creation needs — the
    /// blank-document factory, the dirty mark, the save.
    /// </summary>
    public ProjectWindowViewModel ViewModel => _vm;

    /// <summary>Parameterless for the designer and the XAML compiler only.</summary>
    public ProjectWindow()
    {
        _vm = new ProjectWindowViewModel(new Project(new ProjectManifest(), ""));
        DataContext = _vm;
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Mirror the list's selection onto the view model.
    /// </summary>
    /// <remarks>
    /// Rather than binding <c>SelectedItems</c>, which Avalonia exposes as a
    /// non-generic <c>IList</c> that a view model would then have to guard on
    /// every read. Copying it here keeps the typed collection typed, and keeps
    /// the bulk commands drivable by a test that never made a ListBox.
    /// </remarks>
    private void OnStructureSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list) return;
        _vm.SetSelection(list.SelectedItems?.OfType<BoardRow>() ?? []);
    }

    // ---- dragging a card between status columns --------------------------------

    /// <summary>What is being dragged, so the drop knows which row to move.</summary>
    /// <remarks>
    /// A field rather than a payload on the <c>DataObject</c>, matching what the
    /// project docker already does for row drags — one pattern for both, and the
    /// object is never leaving this window.
    /// </remarks>
    private BoardRow? _draggedCard;

    /// <summary>The token the drag carries, so <c>DoDragDropAsync</c> has a payload.</summary>
    /// <remarks>
    /// <para>
    /// In-process, like every other drag in the application, because the drop
    /// handler reads <see cref="_draggedCard"/> and never unpacks this — the
    /// card is not going anywhere another application could receive it.
    /// </para>
    /// <para>
    /// It was <c>CreateStringApplicationFormat</c>, which is the cross-application
    /// one, and Avalonia validates those identifiers: a <c>/</c> is rejected, so
    /// <c>"lightbox/status-card"</c> threw out of this field initialiser and took
    /// the whole window's type initialiser with it (B163). The name loses its
    /// slash as well as its constructor, so a later move back to an application
    /// format cannot resurrect the crash.
    /// </para>
    /// </remarks>
    private static readonly DataFormat<string> CardFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-status-card");

    private async void OnStatusCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: BoardRow row }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _draggedCard = row;
        try
        {
            // The same call the project docker's row drag uses, and the payload
            // is the document id for the same reason: an id survives the rebuild
            // a drop causes, where an object reference would not.
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(CardFormat, row.Document?.Id ?? ""));
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("status-card-drag", ex);
        }
        finally
        {
            _draggedCard = null;
        }
    }

    private void OnStatusDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = _draggedCard is null ? DragDropEffects.None : DragDropEffects.Move;

    private void OnStatusDrop(object? sender, DragEventArgs e)
    {
        if (_draggedCard is not { } row) return;
        if (sender is not Control { DataContext: StatusColumn column }) return;
        e.Handled = true;
        _vm.MoveToStatus((row, column.Status));
    }

    // ---- creating structure from the window --------------------------------------

    // Click rather than Command, the docker's rule for the same control: a
    // flyout's items live in a popup, so a binding there is a coin flip that
    // lands wrong. The handlers are one line each; the sequence they start is
    // on the view model where a test can drive it.
    private async void OnNewFolder(object? sender, RoutedEventArgs e) =>
        await _vm.CreateFolderAsync();

    private async void OnNewDocument(object? sender, RoutedEventArgs e) =>
        await _vm.CreateDocumentAsync();

    // ---- dragging an asset from the library onto a scope --------------------------

    /// <summary>What is being dragged, so the drop knows which asset lands.</summary>
    private AssetEntry? _draggedAsset;

    /// <summary>In-process, like the status card's — the drop reads the field.</summary>
    private static readonly DataFormat<string> AssetFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-asset");

    private async void OnAssetPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: AssetEntry asset }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _draggedAsset = asset;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(AssetFormat, asset.Id));
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("asset-drag", ex);
        }
        finally
        {
            _draggedAsset = null;
        }
    }

    // ---- dragging the hierarchy itself (both trees) -------------------------------
    //
    // Press-then-move rather than drag-on-press: Structure is multi-select,
    // and starting a drag on every press would eat shift-clicks and
    // double-clicks alike. The indicator lives on the row (DropAbove /
    // DropInto), set from drag-over and cleared when the drag ends.

    private object? _mayDrag;
    private Point _pressedAt;

    /// <summary>The press that may become a drag — DoDragDropAsync needs it.</summary>
    private PointerPressedEventArgs? _pressArgs;
    private BoardRow? _draggedTreeRow;
    private AssetScope? _draggedScope;
    private object? _indicated;

    private static readonly DataFormat<string> TreeFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-tree-row");

    private void OnTreeRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: BoardRow row }) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _mayDrag = row;
        _pressedAt = e.GetPosition(this);
        _pressArgs = e;
    }

    private void OnScopeRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: AssetScope scope }) return;
        // Any button: the create menu and the share bar act on the row under
        // the pointer, the docker's B108 rule applied here.
        _vm.SelectedScope = scope;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _mayDrag = scope;
        _pressedAt = e.GetPosition(this);
        _pressArgs = e;
    }

    private async void OnTreeRowMoved(object? sender, PointerEventArgs e) =>
        await BeginTreeDragAsync(e);

    private async void OnScopeRowMoved(object? sender, PointerEventArgs e) =>
        await BeginTreeDragAsync(e);

    private async Task BeginTreeDragAsync(PointerEventArgs e)
    {
        if (_mayDrag is null || _pressArgs is not { } press) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _mayDrag = null;
            _pressArgs = null;
            return;
        }
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - _pressedAt.X) < 4 && Math.Abs(at.Y - _pressedAt.Y) < 4) return;

        var dragged = _mayDrag;
        _mayDrag = null;
        _pressArgs = null;
        _draggedTreeRow = dragged as BoardRow;
        _draggedScope = dragged as AssetScope;
        if (_draggedScope is { IsProject: true })
        {
            // The project is a place, not a thing in the project.
            _draggedScope = null;
            return;
        }
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(TreeFormat, ""));
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("tree-row-drag", ex);
        }
        finally
        {
            _draggedTreeRow = null;
            _draggedScope = null;
            ClearIndicator();
        }
    }

    /// <summary>Whether the pointer means "before this row" rather than "into it".</summary>
    /// <remarks>
    /// Only a folder (or the project) can be dropped <em>into</em>, so on any
    /// other row the whole height means "before". On a folder the top quarter
    /// means before and the rest means inside — a line and a tint, and the
    /// artist watches which one lights.
    /// </remarks>
    private static bool WantsAbove(Control control, DragEventArgs e, bool canHold) =>
        !canHold || e.GetPosition(control).Y < control.Bounds.Height * 0.25;

    private void Indicate(object row, bool above)
    {
        if (!ReferenceEquals(_indicated, row)) ClearIndicator();
        _indicated = row;
        switch (row)
        {
            case BoardRow b:
                b.DropAbove = above;
                b.DropInto = !above;
                break;
            case AssetScope s:
                s.DropAbove = above;
                s.DropInto = !above;
                break;
        }
    }

    private void ClearIndicator()
    {
        switch (_indicated)
        {
            case BoardRow b:
                b.DropAbove = false;
                b.DropInto = false;
                break;
            case AssetScope s:
                s.DropAbove = false;
                s.DropInto = false;
                break;
        }
        _indicated = null;
    }

    private void OnTreeRowDragOver(object? sender, DragEventArgs e)
    {
        if (_draggedTreeRow is null) return;
        if (sender is not Control { DataContext: BoardRow row } control) return;
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
        Indicate(row, WantsAbove(control, e, row.IsFolder));
    }

    private void OnTreeRowDrop(object? sender, DragEventArgs e)
    {
        if (_draggedTreeRow is not { } dragged) return;
        if (sender is not Control { DataContext: BoardRow target } control) return;
        e.Handled = true;
        var above = WantsAbove(control, e, target.IsFolder);
        ClearIndicator();
        if (above) _vm.MoveBefore(dragged, target);
        else _vm.MoveTo(dragged, target.Folder);
    }

    private void OnScopeDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: AssetScope scope } control) return;
        if (_draggedAsset is not null)
        {
            // A library asset lands *on* a scope, never between two.
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
            Indicate(scope, above: false);
            return;
        }
        if (_draggedScope is null) return;
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
        var canHold = scope.IsProject || (scope.Folder is not null && scope.Document is null);
        Indicate(scope, !scope.IsProject && WantsAbove(control, e, canHold));
    }

    private void OnScopeDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: AssetScope scope } control) return;
        e.Handled = true;
        ClearIndicator();
        if (_draggedAsset is { } asset)
        {
            _vm.DropOnScope(asset, scope);
            return;
        }
        if (_draggedScope is not { } dragged) return;
        if (scope.IsProject)
        {
            _vm.MoveTo(dragged, null);
            return;
        }
        var canHold = scope.Folder is not null && scope.Document is null;
        if (WantsAbove(control, e, canHold)) _vm.MoveBefore(dragged, scope);
        else _vm.MoveTo(dragged, scope.Folder);
    }

    /// <summary>Empty space below the rows: the project root.</summary>
    private void OnTreeListDragOver(object? sender, DragEventArgs e)
    {
        if (_draggedTreeRow is null && _draggedScope is null) return;
        e.DragEffects = DragDropEffects.Move;
        ClearIndicator();
    }

    private void OnTreeListDrop(object? sender, DragEventArgs e)
    {
        ClearIndicator();
        if (_draggedTreeRow is { } row) _vm.MoveTo(row, null);
        else if (_draggedScope is { } scope) _vm.MoveTo(scope, null);
    }

    // ---- renaming in place (B64's gesture, the layer panel's idiom) ----------------

    private void OnTreeNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: BoardRow row }) return;
        // A sheet renames from the Reference sheets panel, where its views live.
        if (row.Sheet is not null) return;
        row.IsRenaming = true;
        e.Handled = true;
    }

    private void OnTreeNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: BoardRow row } box) return;
        if (!row.IsRenaming) return;
        _vm.Rename(row, box.Text);
    }

    private void OnTreeNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: BoardRow row } box) return;
        if (e.Key == Key.Enter)
        {
            _vm.Rename(row, box.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.IsRenaming = false;
            _vm.Rebuild();   // put the shown name back
            e.Handled = true;
        }
    }

    // ---- removing and deleting (the docker's two operations, here too) -------------

    private void OnRemoveRow(object? sender, RoutedEventArgs e)
    {
        foreach (var row in SelectedRows())
        {
            _vm.RemoveFromProject(row);
        }
    }

    private async void OnDeleteRow(object? sender, RoutedEventArgs e)
    {
        // One at a time even under multi-select: each deletion is its own
        // decision, and one dialog covering five folders is a decision about
        // an average.
        foreach (var row in SelectedRows())
        {
            if (_vm.DeleteNeedsConfirmation(row)
                && !await ConfirmAsync("Delete permanently", _vm.DeleteWarning(row), "Delete"))
            {
                continue;
            }
            _vm.DeleteFromDisk(row);
        }
    }

    private System.Collections.Generic.List<BoardRow> SelectedRows() =>
        [.. StructureRows.SelectedItems?.OfType<BoardRow>() ?? []];

    // ---- opening a row: in Lightbox, or on disk -----------------------------------
    //
    // Click rather than Command for the flyout's items, the rule the rest of
    // this window's menus already follow: a flyout's items live in a popup,
    // where a window binding resolves to nothing.
    //
    // Not on double-click, and that is the one worth writing down. Double-click
    // on a row's name already starts a rename here (B64's gesture), so an open
    // on double-click would be told apart from a rename by a few pixels of
    // horizontal position — and getting it wrong takes a modal window down and
    // opens a tab. The docker keeps double-click-to-open because nothing there
    // renames that way.

    private void OnOpenRowInLightbox(object? sender, RoutedEventArgs e) =>
        _vm.OpenSelectedInLightboxCommand.Execute(null);

    private void OnRevealRow(object? sender, RoutedEventArgs e) =>
        _vm.RevealSelectedCommand.Execute(null);

    private void OnOpenScopeInLightbox(object? sender, RoutedEventArgs e) =>
        _vm.OpenScopeInLightbox(_vm.SelectedScope);

    private void OnRevealScope(object? sender, RoutedEventArgs e) =>
        _vm.RevealScope(_vm.SelectedScope);

    private void OnRemoveScope(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedScope is not { } scope || _vm.AsRow(scope) is not { } row)
        {
            _vm.Status = "Select a folder or a document row first — the project itself stays.";
            return;
        }
        _vm.RemoveFromProject(row);
    }

    private async void OnDeleteScope(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedScope is not { } scope || _vm.AsRow(scope) is not { } row)
        {
            _vm.Status = "Select a folder or a document row first — the project itself stays.";
            return;
        }
        if (_vm.DeleteNeedsConfirmation(row)
            && !await ConfirmAsync("Delete permanently", _vm.DeleteWarning(row), "Delete"))
        {
            return;
        }
        _vm.DeleteFromDisk(row);
    }

    private async void OnDeleteAsset(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AssetEntry asset }) return;
        if (!await ConfirmAsync("Delete asset", _vm.DeleteAssetWarning(asset), "Delete")) return;
        _vm.DeleteAsset(asset);
    }

    /// <summary>A yes/no the artist has to mean — the docker's dialog, here.</summary>
    private async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        var yes = false;
        var confirm = new Button { Content = confirmLabel, IsDefault = false };
        var cancel = new Button { Content = "Cancel", IsDefault = true, IsCancel = true };
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, MaxWidth = 360, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Children = { cancel, confirm },
                    },
                },
            },
        };
        confirm.Click += (_, _) => { yes = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return yes;
    }

    // ---- running the export plan (the Export tab's ▶) -----------------------------

    private async void OnRunExport(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Export to folder",
                AllowMultiple = false,
            });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } destination) return;
        await _vm.RunExportToAsync(destination);
    }

    // ---- creating assets (the Assets tab's right-click) ---------------------------

    private async void OnNewSheet(object? sender, RoutedEventArgs e) =>
        await _vm.CreateSheetAsync();

    private async void OnNewPalette(object? sender, RoutedEventArgs e) =>
        await _vm.CreatePaletteAsync();

    private async void OnNewGradient(object? sender, RoutedEventArgs e) =>
        await _vm.CreateGradientAsync();

    private async void OnNewTemplate(object? sender, RoutedEventArgs e) =>
        await _vm.CreateTemplateAsync();
}
