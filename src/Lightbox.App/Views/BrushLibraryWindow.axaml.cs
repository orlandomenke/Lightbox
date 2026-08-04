using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>
/// The brush library: everything you have, and what you may do to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> Importing a collection of fifty-six brushes worked and then there was
/// no way to get rid of them. Deleting one at a time from the brush options is a fifty-six
/// click job through a flyout that closes when you pick something, and there was no way to
/// rename an import at all — you were stuck with whatever the person who made the pack called
/// it.
/// </para>
/// <para>
/// <b>Called a library rather than "Edit imports", and the name is a decision.</b> The window
/// manages your own brushes as well as imported ones, and a name that says "imports" would
/// make people go looking somewhere else for the brush they made — the same trap "Edit assets"
/// sets by not saying which assets. *Library* is also what this app already calls a collection
/// of reusable things (the character library, the tip library), so it costs nothing to learn.
/// </para>
/// <para>
/// A window rather than a docker, for the reason the tip workshop is one: weeding a collection
/// is not something you do while drawing, and panel space taken here is taken from every
/// session that never opens it.
/// </para>
/// </remarks>
public partial class BrushLibraryWindow : Window
{
    private static readonly HashSet<string> NoTags = new(StringComparer.OrdinalIgnoreCase);

    private readonly MainViewModel _vm;
    private CancellationTokenSource? _importing;

    public BrushLibraryWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
    }

    public BrushLibraryWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        Refresh();
    }

    /// <summary>The brushes on offer, filtered, as tiles.</summary>
    /// <remarks>
    /// <see cref="BrushChoice"/> again rather than a second row type. Its previews are cached
    /// on the preset's id and settings, so the library and the picker share them — opening
    /// this window after using the picker costs nothing, and the mark shown here is the mark
    /// shown there.
    /// </remarks>
    private void Refresh()
    {
        if (BrushList is null) return;

        // No tag filter here: the library is where you go to find the brush you want rid of,
        // and a second filter to remember is a place for it to hide.
        var matches = BrushFilter.Apply(_vm.BrushPresetChoices, SearchBox.Text, NoTags);
        BrushList.ItemsSource = matches.Select(BrushChoice.For).ToList();
        UpdateCount();
        UpdateSelectionState();
    }

    private void UpdateCount()
    {
        var total = _vm.BrushPresetChoices.Count;
        var shown = BrushList.ItemsSource?.Cast<object>().Count() ?? 0;
        CountLabel.Text = shown == total
            ? $"{total} brush{(total == 1 ? "" : "es")}"
            : $"{shown} of {total}";
    }

    private List<BrushChoice> Selected =>
        BrushList.SelectedItems?.Cast<BrushChoice>().ToList() ?? [];

    /// <summary>
    /// What the buttons and the rename row should look like for the current selection.
    /// </summary>
    /// <remarks>
    /// The shipped-brush note appears when the selection contains one, rather than after the
    /// click. A refusal you could have been told about beforehand is a worse refusal — B2's
    /// lesson, and the same reason the roadmap wants a forbidden cursor.
    /// </remarks>
    private void UpdateSelectionState()
    {
        var chosen = Selected;
        var removable = chosen.Count(c => !c.Preset.IsBuiltIn);

        RemoveButton.IsEnabled = removable > 0;
        RemoveButton.Content = removable > 1 ? $"Remove {removable}" : "Remove";
        ShippedNote.IsVisible = chosen.Any(c => c.Preset.IsBuiltIn);

        // Exactly one, and yours: renaming a dozen at once means nothing, and renaming a
        // shipped brush would change a name that documents already refer to.
        var one = chosen.Count == 1 && !chosen[0].Preset.IsBuiltIn ? chosen[0] : null;
        RenameRow.IsVisible = one is not null;
        if (one is not null && !RenameBox.IsFocused) RenameBox.Text = one.Name;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateSelectionState();

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => Refresh();

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        var going = Selected.Select(c => c.Preset).Where(p => !p.IsBuiltIn).ToList();
        if (going.Count == 0) return;

        // One call, one save. Looping over DeletePreset would rewrite the whole store per
        // brush — see DeletePresets for why that matters at collection size.
        var removed = _vm.DeletePresets(going);
        _vm.AiStatus = removed == 1
            ? $"Removed {going[0].Name}."
            : $"Removed {removed} brushes.";
        Refresh();
    }

    private void OnRename(object? sender, RoutedEventArgs e) => ApplyRename();

    private void OnRenameKey(object? sender, KeyEventArgs e)
    {
        // Enter commits, because typing a name and reaching for the mouse is the slower half
        // of renaming forty brushes.
        if (e.Key == Key.Enter) ApplyRename();
    }

    private void ApplyRename()
    {
        if (Selected is not [{ } only] || only.Preset.IsBuiltIn) return;
        var wanted = RenameBox.Text?.Trim();
        if (string.IsNullOrEmpty(wanted) || wanted == only.Name) return;

        if (_vm.RenamePreset(only.Preset, wanted)) Refresh();
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import brushes",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Brush files") { Patterns = BrushImportJob.Patterns },
            ],
        });
        if (files.Count == 0) return;

        // Reading the files off disk is IO and already awaited; it is the parsing that used to
        // block, and that is what ImportBrushFilesAsync moves off this thread.
        var payloads = new List<(string, byte[])>();
        foreach (var file in files)
        {
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            payloads.Add((file.Name, memory.ToArray()));
        }

        await RunImport(payloads);
    }

    /// <summary>Import with the bar showing, and a way to stop.</summary>
    internal async Task RunImport(IReadOnlyList<(string Name, byte[] Bytes)> payloads)
    {
        _importing?.Cancel();
        _importing = new CancellationTokenSource();

        ProgressRow.IsVisible = true;
        ImportButton.IsEnabled = false;
        ImportProgress.Value = 0;
        ProgressLabel.Text = $"Reading {payloads.Count} file{(payloads.Count == 1 ? "" : "s")}…";

        var progress = new Progress<BrushImportProgress>(p =>
        {
            ImportProgress.Value = p.Fraction;
            ProgressLabel.Text = $"{p.File} — {p.Done} of {p.Total}, {p.Found} brush{(p.Found == 1 ? "" : "es")} so far";
        });

        try
        {
            await _vm.ImportBrushFilesAsync(payloads, progress, _importing.Token);
        }
        finally
        {
            // Whatever happened — finished, stopped, or threw — the bar goes and the button
            // comes back. A window left with a dead progress bar on it is indistinguishable
            // from the stall this whole change exists to remove.
            ProgressRow.IsVisible = false;
            ImportButton.IsEnabled = true;
            _importing.Dispose();
            _importing = null;
            Refresh();
        }
    }

    private void OnCancelImport(object? sender, RoutedEventArgs e) => _importing?.Cancel();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        // Closing the window mid-import stops the reading rather than leaving a worker
        // filling a list nobody is watching.
        _importing?.Cancel();
        base.OnClosed(e);
    }
}
