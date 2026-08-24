using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// A headline and a body of text the artist has to be able to read and copy.
/// </summary>
/// <remarks>
/// <para>
/// The application had no way to say more than a status line. That was fine
/// while every message fitted one, and stopped being fine with the first refused
/// PSD: the refusal is a <em>list</em> — every unsupported feature, the layer
/// carrying it, and the Photoshop menu path that fixes it — and a list truncated
/// into a status bar sends the artist back for one fix at a time.
/// </para>
/// <para>
/// The body is a <c>SelectableTextBlock</c> on purpose, so layer names can be
/// copied out and searched for in Photoshop rather than retyped from a
/// screenshot.
/// </para>
/// </remarks>
public partial class NoticeDialog : Window
{
    public NoticeDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Fill in the window and hand it back ready to show.</summary>
    public NoticeDialog Show(string title, string headline, string body)
    {
        Title = title;
        this.FindControl<TextBlock>("Headline")!.Text = headline;
        this.FindControl<SelectableTextBlock>("Body")!.Text = body;
        return this;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
