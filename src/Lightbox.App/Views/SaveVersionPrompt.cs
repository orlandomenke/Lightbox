using Avalonia.Controls;

namespace Lightbox.App.Views;

/// <summary>
/// The "Save version…" modal: a label, optional notes, keep or cancel.
/// </summary>
/// <remarks>
/// Built in code beside <see cref="TextPrompt"/> rather than as XAML, because
/// it is that dialog plus one field and the two should read as siblings.
/// Returns null when the artist cancels; an empty notes box returns null
/// notes, so an untouched field writes no key into the history file — the
/// serialization rule "optional means absent", applied to prose.
/// </remarks>
internal static class SaveVersionPrompt
{
    public sealed record Answer(string Label, string? Notes);

    public static async Task<Answer?> ShowAsync(Window owner, string resourceName, string suggestedLabel)
    {
        var labelBox = new TextBox { Text = suggestedLabel, PlaceholderText = "Label" };
        var notesBox = new TextBox
        {
            PlaceholderText = "Notes (optional)",
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 56,
            MaxHeight = 120,
        };
        Answer? answer = null;

        var dialog = new Window
        {
            Title = $"Save version — {resourceName}",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "Keep version", IsDefault = true, MinWidth = 72 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
        ok.Click += (_, _) =>
        {
            var label = labelBox.Text?.Trim();
            // A version with no label is unfindable in a month; the derived
            // suggestion means accepting the default is always possible.
            if (label is not { Length: > 0 }) { labelBox.Focus(); return; }
            var notes = notesBox.Text?.Trim();
            answer = new Answer(label, notes is { Length: > 0 } ? notes : null);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(14),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Keeps a copy of the saved file in the project's history.",
                    Opacity = 0.8,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                labelBox,
                notesBox,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 6,
                    Children = { cancel, ok },
                },
            },
        };
        labelBox.Focus();
        // B107: the caret continues the offered label rather than fronting it.
        labelBox.CaretIndex = labelBox.Text?.Length ?? 0;
        await dialog.ShowDialog(owner);
        return answer;
    }
}
