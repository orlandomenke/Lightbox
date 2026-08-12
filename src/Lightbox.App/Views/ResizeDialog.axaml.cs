using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Views;

/// <summary>
/// Resize canvas and resize image.
/// </summary>
/// <remarks>
/// <para>
/// Almost nothing here: the arithmetic, the linking and the summary are
/// <see cref="ResizeDialogViewModel"/>, which is what lets the interesting cases
/// be tested without a window. What the window owns is the anchor grid, because
/// nine toggle buttons that behave as one selection is a control rather than a
/// value.
/// </para>
/// <para>
/// The grid is built in code rather than declared as nine buttons in XAML. Nine
/// near-identical blocks is nine places to get a binding wrong, and the order is
/// exactly <see cref="ResizeAnchor"/>'s own — so building it from the enum makes
/// the two impossible to disagree, and a tenth anchor would appear here for
/// free.
/// </para>
/// </remarks>
public partial class ResizeDialog : Window
{
    private readonly ResizeDialogViewModel _vm;

    /// <summary>Whether the artist pressed Resize.</summary>
    public bool Confirmed { get; private set; }

    public ResizeDialog(Scene scene, ResizeMode mode)
    {
        _vm = new ResizeDialogViewModel(scene, mode).WithDefaults();
        DataContext = _vm;
        InitializeComponent();
        Title = mode == ResizeMode.Image ? "Resize image" : "Resize canvas";
        BuildAnchorGrid();
    }

    /// <summary>Parameterless for the designer and the XAML compiler only.</summary>
    public ResizeDialog()
    {
        _vm = new ResizeDialogViewModel(new Scene(), ResizeMode.Canvas).WithDefaults();
        DataContext = _vm;
        InitializeComponent();
        BuildAnchorGrid();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>The view model, so the caller can apply what was chosen.</summary>
    public ResizeDialogViewModel Choice => _vm;

    private void BuildAnchorGrid()
    {
        if (this.FindControl<UniformGrid>("AnchorGrid") is not { } grid) return;
        grid.Children.Clear();

        foreach (var anchor in Enum.GetValues<ResizeAnchor>())
        {
            var button = new RadioButton
            {
                GroupName = "anchor",
                Tag = anchor,
                IsChecked = anchor == _vm.Anchor,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(2),
            };
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true) _vm.SetAnchor(anchor);
            };
            grid.Children.Add(button);
        }
    }

    private void OnReset(object? sender, RoutedEventArgs e) => _vm.Reset();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (!_vm.CanApply) return;
        Confirmed = true;
        Close();
    }
}
