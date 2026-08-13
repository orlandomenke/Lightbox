using Avalonia.Controls;

namespace Lightbox.App.Views;

/// <summary>
/// The one-field modal every "what is it called" gesture uses.
/// </summary>
/// <remarks>
/// Extracted from <c>MainWindow.PromptForText</c> when the project window
/// started creating documents and folders too — two copies of this dialog is
/// two chances for the B107 caret rule below to hold in one and not the other.
/// Returns null when the user cancels, which is not the same as an empty
/// string, and the callers rely on the difference.
/// </remarks>
internal static class TextPrompt
{
    public static async Task<string?> ShowAsync(Window owner, string title, string label, string initial)
    {
        var box = new TextBox { Text = initial, PlaceholderText = label };
        string? answer = null;

        var dialog = new Window
        {
            Title = title,
            Width = 320,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "Save", IsDefault = true, MinWidth = 72 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
        ok.Click += (_, _) =>
        {
            answer = box.Text ?? "";
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(14),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = label, Opacity = 0.8 },
                box,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 6,
                    Children = { cancel, ok },
                },
            },
        };
        box.Focus();
        // B107. The caret goes after what is already there rather than to the
        // front of it. The box is prefilled with a derived stem — "Knight - "
        // — and typing has to continue it; a caret at index 0 would put the
        // artist's word in front of the name they were offered.
        box.CaretIndex = box.Text?.Length ?? 0;
        await dialog.ShowDialog(owner);
        return answer;
    }
}
