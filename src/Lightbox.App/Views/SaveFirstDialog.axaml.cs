using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lightbox.App.Views;

/// <summary>What the artist chose when told the drawing has to be saved first.</summary>
public enum SaveFirstChoice
{
    /// <summary>Open the picker and carry on with what they were doing.</summary>
    SaveAs,

    /// <summary>Put it back the way it was.</summary>
    Revert,
}

/// <summary>
/// "This has not been saved" — with the two answers that actually exist.
/// </summary>
/// <remarks>
/// <para>
/// Used by anything that hands the document to something outside the app: an export, and
/// a status change that a designer will read as "this is ready to use". Both are claims
/// about a file, so both are meaningless until there is one.
/// </para>
/// <para>
/// <b>The refusal is spelled out rather than called "Cancel"</b>, and the caller supplies
/// the wording. By the time this window appears the artist has already clicked something —
/// a status, an export — and "Cancel" leaves them guessing whether that click survived.
/// "Revert status change" answers it.
/// </para>
/// </remarks>
public partial class SaveFirstDialog : Window
{
    public SaveFirstDialog() : this("This drawing has not been saved yet.", "Cancel") { }

    /// <param name="message">Why they are being asked, in terms of what they were doing.</param>
    /// <param name="refuseLabel">
    /// What refusing does, named after the thing it undoes — "Revert status change".
    /// </param>
    public SaveFirstDialog(string message, string refuseLabel)
    {
        InitializeComponent();
        Message.Text = message;
        RevertButton.Content = refuseLabel;
    }

    private void OnSaveAsClicked(object? sender, RoutedEventArgs e) => Close(SaveFirstChoice.SaveAs);

    private void OnRevertClicked(object? sender, RoutedEventArgs e) => Close(SaveFirstChoice.Revert);
}
