using Avalonia.Controls;
using Avalonia.Input;
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
        DataContext = _vm;
        InitializeComponent();

        if (this.FindControl<ListBox>("StructureRows") is { } rows)
        {
            rows.SelectionChanged += OnStructureSelectionChanged;
        }
    }

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
    /// the whole window's type initialiser with it (B162). The name loses its
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
}
