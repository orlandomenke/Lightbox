using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Lightbox.App.Services;

namespace Lightbox.App.Views;

/// <summary>What the artist decided about the work a crashed session left.</summary>
public enum RecoveryChoice
{
    /// <summary>Keep the copies where they are and ask again later.</summary>
    Later,

    /// <summary>Open them all as recovered tabs.</summary>
    Restore,

    /// <summary>Delete them, confirmed.</summary>
    Discard,
}

/// <summary>
/// Offers back the documents a crashed session left recovery copies of (B394).
/// </summary>
public partial class RecoveryDialog : Window
{
    public RecoveryDialog() : this([]) { }

    public RecoveryDialog(IReadOnlyList<RecoverableDocument> copies)
    {
        InitializeComponent();
        Message.Text = copies.Count == 1
            ? "Lightbox closed before this document was saved. Its unsaved work was kept:"
            : $"Lightbox closed before these {copies.Count} documents were saved. Their unsaved work was kept:";
        Copies.ItemsSource = copies.Select(Describe).ToList();
        Note.Text = "Restored documents open as new tabs and are not tied to their original files — " +
                    "save them where you choose. Not now keeps them; File ▸ Recover unsaved work… brings this back.";
    }

    /// <summary>"protagonist — kept 12:36, 7 Oct" plus where it came from.</summary>
    internal static string Describe(RecoverableDocument copy)
    {
        var when = copy.WrittenAt.ToString("HH:mm, d MMM", CultureInfo.CurrentCulture);
        var from = copy.OriginalPath is { Length: > 0 } path ? $"\n    from {path}" : "";
        return $"{copy.Title} — kept {when}{from}";
    }

    private void OnRestoreClicked(object? sender, RoutedEventArgs e) => Close(RecoveryChoice.Restore);

    private void OnLaterClicked(object? sender, RoutedEventArgs e) => Close(RecoveryChoice.Later);

    private void OnDiscardClicked(object? sender, RoutedEventArgs e)
    {
        Message.Text = "Delete this unsaved work for good? It cannot be brought back.";
        Note.Text = "";
        Choices.IsVisible = false;
        Confirm.IsVisible = true;
    }

    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        Message.Text = "Lightbox closed before saving. Its unsaved work was kept:";
        Choices.IsVisible = true;
        Confirm.IsVisible = false;
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e) => Close(RecoveryChoice.Discard);
}
