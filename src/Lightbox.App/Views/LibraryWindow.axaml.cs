using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>
/// The character library's browsing home (Q138): the shelf of everything the
/// library folders offer, and the folders themselves. A bigger view over the
/// same <see cref="LibraryViewModel"/> the project browser's picker reads —
/// never a second scan or a second import path.
/// </summary>
public partial class LibraryWindow : Window
{
    private readonly LibraryViewModel _vm;

    public LibraryWindow(LibraryViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        vm.ConfirmReplaceEdited = names => ConfirmReplaceEditedAsync(this, names);
        // Scan when the surface opens, never at startup — LibrarySettings' rule.
        Opened += (_, _) => vm.Rescan();
    }

    private async void OnAddRoot(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a library folder",
            AllowMultiple = false,
        });
        if (picked.Count == 1 && picked[0].TryGetLocalPath() is { } path)
        {
            _vm.AddRoot(path);
            Status.Text = _vm.ScannedEmpty
                ? "No characters found — a library is a project whose type is Asset library."
                : "";
        }
    }

    private void OnRemoveRoot(object? sender, RoutedEventArgs e)
    {
        if (RootList.SelectedItem is string root) _vm.RemoveRoot(root);
    }

    private void OnRescan(object? sender, RoutedEventArgs e) => _vm.Rescan();

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not LibraryEntryRow row)
        {
            Status.Text = "Pick a character to import.";
            return;
        }
        if (await _vm.Import(row) is not { } result)
        {
            Status.Text = "Open a project first — an import needs somewhere to land.";
            return;
        }
        Status.Text = Summarise(result);
    }

    private static string Summarise(Core.Projects.ImportResult result)
    {
        var parts = new List<string>();
        if (result.Added.Count > 0) parts.Add($"added {string.Join(", ", result.Added)}");
        if (result.Replaced.Count > 0) parts.Add($"updated {string.Join(", ", result.Replaced)}");
        if (result.KeptEdited.Count > 0) parts.Add($"kept {string.Join(", ", result.KeptEdited)} (edited here)");
        return parts.Count > 0
            ? $"“{result.Folder.Name}”: {string.Join("; ", parts)}."
            : $"“{result.Folder.Name}” is already up to date.";
    }

    /// <summary>
    /// The Q35 gate, shared with the picker: name the edited copies before the
    /// destructive act, with keeping them as the default-safe button.
    /// </summary>
    internal static async Task<bool> ConfirmReplaceEditedAsync(Window owner, IReadOnlyList<string> names)
    {
        var replace = false;
        var dialog = new Window
        {
            Title = "Edited since import",
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var keep = new Button { Content = "Keep my versions", MinWidth = 130, IsDefault = true, IsCancel = true };
        var overwrite = new Button { Content = "Replace with library's", MinWidth = 150 };
        keep.Click += (_, _) => dialog.Close();
        overwrite.Click += (_, _) => { replace = true; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    // Named rather than counted — ConfirmDiscardAsync's rule:
                    // the list is what makes Replace a decision, not a gamble.
                    Text = names.Count == 1
                        ? $"You have edited “{names[0]}” since it was imported."
                        : $"You have edited {names.Count} of these since they were imported:\n"
                          + string.Join("\n", names.Select(n => $"    • {n}")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Replacing takes the library's version and discards those edits. "
                        + "Everything else updates either way.",
                    FontSize = 11,
                    Opacity = 0.7,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    // The destructive button furthest from the default-safe one.
                    Children = { overwrite, keep },
                },
            },
        };
        await dialog.ShowDialog(owner);
        return replace;
    }
}
