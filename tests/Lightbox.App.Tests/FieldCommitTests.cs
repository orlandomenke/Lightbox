using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Controls;
using Xunit;

namespace Lightbox.App.Tests;

public partial class FieldCommitVm : ObservableObject
{
    [ObservableProperty] private double _fps = 12;
}

/// <summary>
/// The numeric fields' commit path, driven through the real control: focus,
/// type, and leave by each of the three exits (Enter, Tab, clicking away).
/// </summary>
/// <remarks>
/// <c>FieldBehaviourTests</c> proves the converter; this proves the control
/// consults it. The distinction is not academic: the first fix for the
/// emptied-field bug lived only in the converter, passed its unit test, and
/// missed the app — the control short-circuits empty text to a null value
/// without ever calling the converter.
/// </remarks>
public class FieldCommitTests(ITestOutputHelper output)
{
    private static (Window Window, NumericUpDown Field, TextBox Other, FieldCommitVm Vm) Shown()
    {
        var vm = new FieldCommitVm();
        var field = new NumericUpDown { Minimum = 1, Maximum = 60, Increment = 1, FormatString = "0" };
        FieldMath.SetEnabled(field, true);
        field.Bind(NumericUpDown.ValueProperty,
            new Binding(nameof(vm.Fps)) { Mode = BindingMode.TwoWay, Source = vm });
        var other = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(field);
        panel.Children.Add(other);
        var window = new Window { Content = panel, Width = 300, Height = 200 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, field, other, vm);
    }

    private static TextBox Inner(NumericUpDown field) => field.FindDescendantOfType<TextBox>()!;

    /// <summary>
    /// The reported brick, end to end: empty the fps field, click away, and
    /// the field must neither go invalid nor lose its value.
    /// </summary>
    [AvaloniaFact]
    public void EmptyingAFieldKeepsItsValueThroughTheRealCommit()
    {
        var (_, field, other, vm) = Shown();

        var inner = Inner(field);
        inner.Focus();
        inner.Text = "";
        other.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"Value={field.Value}, vm={vm.Fps}, Text='{field.Text}', errors={DataValidationErrors.GetHasErrors(field)}");
        Assert.False(DataValidationErrors.GetHasErrors(field));
        Assert.Equal(12m, field.Value);
        Assert.Equal(12, vm.Fps);
        Assert.False(string.IsNullOrEmpty(field.Text));
    }

    [AvaloniaFact]
    public void EmptyingAFieldAndPressingEnterAlsoKeepsIt()
    {
        var (window, field, _, vm) = Shown();

        Inner(field).Focus();
        Inner(field).Text = "";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(DataValidationErrors.GetHasErrors(field));
        Assert.Equal(12, vm.Fps);
    }

    [AvaloniaFact]
    public void ArithmeticCommitsOnEnter()
    {
        var (window, field, _, vm) = Shown();

        Inner(field).Focus();
        Inner(field).Text = "12*2";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"after Enter: vm={vm.Fps}");
        Assert.Equal(24, vm.Fps);
    }

    [AvaloniaFact]
    public void ArithmeticCommitsOnTab()
    {
        var (window, field, _, vm) = Shown();

        Inner(field).Focus();
        Inner(field).Text = "12*2";
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"after Tab: vm={vm.Fps}, focus left={!field.IsKeyboardFocusWithin}");
        Assert.Equal(24, vm.Fps);
        Assert.False(field.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void ArithmeticCommitsOnClickingAway()
    {
        var (_, field, other, vm) = Shown();

        Inner(field).Focus();
        Inner(field).Text = "12*2";
        other.Focus();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        output.WriteLine($"after unfocus: vm={vm.Fps}");
        Assert.Equal(24, vm.Fps);
    }
}
