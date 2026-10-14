using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>The AI bar: provider, AI Inbetween and its directions menu, progress and status.</summary>
public partial class AiBar : UserControl
{
    /// <summary>Whether the press that is about to become a click held Shift.</summary>
    /// <remarks>
    /// A button's Click carries no modifiers, so the press is read on its way
    /// in (tunnelling, before the button takes it) and the click consults it.
    /// A keyboard activation has no press and reads false, which is right:
    /// Space on a focused button is the plain button.
    /// </remarks>
    private bool _pressHeldShift;

    public AiBar()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("AiInbetweenButton")!.AddHandler(
            PointerPressedEvent,
            (_, e) => _pressHeldShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            RoutingStrategies.Tunnel);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnAiInbetweenClick(object? sender, RoutedEventArgs e)
    {
        var withDirection = _pressHeldShift;
        _pressHeldShift = false;
        if (Vm is not { } vm) return;
        _ = withDirection
            ? vm.AiInbetweenWithDirectionCommand.ExecuteAsync(null)
            : vm.AiInbetweenCommand.ExecuteAsync(null);
    }

    private void OnAiInbetweenWithDirection(object? sender, RoutedEventArgs e) =>
        _ = Vm?.AiInbetweenWithDirectionCommand.ExecuteAsync(null);

    private void OnAiRevise(object? sender, RoutedEventArgs e) =>
        _ = Vm?.AiReviseCommand.ExecuteAsync(null);

    private void OnReadPose(object? sender, RoutedEventArgs e) =>
        _ = Vm?.ReadPoseCommand.ExecuteAsync(null);

    private void OnTogglePoseGuide(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.PoseGuideVisible = !vm.PoseGuideVisible;
    }

    private void OnTogglePoseEdit(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.PoseEditMode = !vm.PoseEditMode;
    }

    /// <summary>The ticks are read from the view model as the menu opens: a binding from inside the popup finds nothing.</summary>
    private void OnFlyoutOpening(object? sender, EventArgs e)
    {
        if (Vm is not { } vm || sender is not MenuFlyout flyout) return;
        foreach (var item in flyout.Items.OfType<MenuItem>())
        {
            if (item.Name == "ShowPoseGuideItem") item.IsChecked = vm.PoseGuideVisible;
            else if (item.Name == "EditPoseGuideItem") item.IsChecked = vm.PoseEditMode;
        }
    }
}
