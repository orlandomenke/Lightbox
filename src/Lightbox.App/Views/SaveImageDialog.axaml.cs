using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Views;

/// <summary>
/// <c>File ▸ Save as image…</c> — pick a format and what to write.
/// </summary>
/// <remarks>
/// Nothing but the window: every decision is
/// <see cref="SaveImageDialogViewModel"/>, so the cases worth checking are
/// testable without opening one. Both constructors exist and both are exercised
/// by a test, which is B163's lesson applied before it costs anything — a dialog
/// that throws on construction is a menu item that does nothing.
/// </remarks>
public partial class SaveImageDialog : Window
{
    private readonly SaveImageDialogViewModel _vm;

    /// <summary>Whether the artist pressed Save rather than closing the window.</summary>
    public bool Confirmed { get; private set; }

    public SaveImageDialog(Scene scene)
    {
        _vm = new SaveImageDialogViewModel(scene);
        DataContext = _vm;
        InitializeComponent();
    }

    /// <summary>Parameterless for the designer and the XAML compiler only.</summary>
    public SaveImageDialog() : this(new Scene()) { }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>What was chosen, so the caller can act on it.</summary>
    public SaveImageDialogViewModel Choice => _vm;

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
