using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Views;

/// <summary>
/// <c>File ▸ Export image sequence…</c> — which frames, as what, called what.
/// </summary>
/// <remarks>
/// Nothing but the window: every decision is
/// <see cref="ImageSequenceDialogViewModel"/>. Both constructors exist and both
/// are exercised by a test, because a window that throws on construction is a
/// menu item that does nothing (B163).
/// </remarks>
public partial class ImageSequenceWindow : Window
{
    private readonly ImageSequenceDialogViewModel _vm;

    /// <summary>Whether the artist pressed Export rather than closing the window.</summary>
    public bool Confirmed { get; private set; }

    public ImageSequenceWindow(Scene scene)
    {
        _vm = new ImageSequenceDialogViewModel(scene);
        DataContext = _vm;
        InitializeComponent();
    }

    /// <summary>Parameterless for the designer and the XAML compiler only.</summary>
    public ImageSequenceWindow() : this(new Scene()) { }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>What was chosen, so the caller can act on it.</summary>
    public ImageSequenceDialogViewModel Choice => _vm;

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
