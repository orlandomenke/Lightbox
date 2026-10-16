using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// The suite closes what it opens (<see cref="TestWindows"/>), including a main
/// window with work in it, without stopping to ask about saving.
/// </summary>
public class TestWindowsTests
{
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static MainWindow Open()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        Pump();
        return window;
    }

    [AvaloniaFact]
    public void AWindowATestShowsIsRecorded()
    {
        var before = TestWindows.OpenCount;

        Open();

        Assert.Equal(before + 1, TestWindows.OpenCount);
    }

    [AvaloniaFact]
    public void AMainWindowWithUnsavedWorkClosesWithoutAsking()
    {
        // Asking would open the unsaved-changes dialog — a second window — and
        // refuse the close, so the cleanup would leave two where it meant none.
        var window = Open();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Unsaved", 200, 150, 12, 72, "#ffffff", false));
        vm.AddPaintedLayerCommand.Execute(null);
        vm.MarkDocumentEditedForTests();
        Pump();
        Assert.Contains(vm.Tabs, t => t.HasWorkToLose);

        TestWindows.CloseAll();
        Pump();

        Assert.False(window.IsVisible, "the main window must be closed");
        Assert.Equal(0, TestWindows.OpenCount);
    }

    [AvaloniaFact]
    public void TheAfterTestHookClosesEveryWindowLeftOpen()
    {
        var main = Open();
        var other = new Window { Width = 200, Height = 100 };
        other.Show();
        Pump();

        new ClosesItsWindowsAttribute().After(null!, null!);

        Assert.False(main.IsVisible);
        Assert.False(other.IsVisible);
        Assert.Equal(0, TestWindows.OpenCount);
    }
}
