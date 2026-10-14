using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>
/// The popup that asks for a note to the AI inbetweener (Q261).
/// </summary>
/// <remarks>
/// <para>
/// Null is a cancel, which the callers keep apart from an empty answer: empty
/// on a new inbetween means "the plain button", and on a revision it is refused
/// with a reason.
/// </para>
/// <para>
/// The examples are the kind of thing an animator types — what the drawing is
/// and how the motion should go — because that is what the strokes cannot say
/// on their own. The wording states the keys as the bound rather than warning
/// of refusal: a note is for steering inside them.
/// </para>
/// </remarks>
public partial class DirectionDialog : Window
{
    private string? _answer;

    public DirectionDialog()
    {
        AvaloniaXamlLoader.Load(this);
        Prepare(DirectionAsk.Inbetween);
    }

    /// <summary>Ask, and hand back the note — or null when the artist cancelled.</summary>
    public static async Task<string?> AskAsync(Window owner, DirectionAsk ask)
    {
        var dialog = new DirectionDialog();
        dialog.Prepare(ask);
        await dialog.ShowDialog(owner);
        return dialog._answer;
    }

    private void Prepare(DirectionAsk ask)
    {
        var revising = ask == DirectionAsk.Revise;
        Title = revising ? "Revise AI frames" : "AI Inbetween with a direction";
        this.FindControl<TextBlock>("Explanation")!.Text = revising
            ? "Your note goes to the model with the frames it drew. The keys bound the motion; "
              + "a frame it cannot keep between them stays as it was."
            : "Your note goes to the model with this layer's two keys. Say what the drawing is "
              + "and how it should move — the keys bound the motion.";
        var note = this.FindControl<TextBox>("Note")!;
        note.MaxLength = MainViewModel.MaxDirectionLength;
        note.PlaceholderText = revising
            ? "e.g. “the far arm lags a frame behind the body”, “less squash on the landing”"
            : "Optional. e.g. “running — this is the far arm”, “let the head lead”";
        this.FindControl<Button>("Go")!.Content = revising ? "Revise" : "Inbetween";
        Opened += (_, _) => note.Focus();
    }

    private void OnGo(object? sender, RoutedEventArgs e)
    {
        _answer = this.FindControl<TextBox>("Note")!.Text ?? "";
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
