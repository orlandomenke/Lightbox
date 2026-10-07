using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The interface scale (Q200): every piece of chrome drawn bigger or smaller,
/// the canvas never.
/// </summary>
[Collection("BrushState")]
public sealed class UiScaleTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static void Settle()
    {
        for (var i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1600, Height = 1000 };
        window.Show();
        Settle();
        return (window, (MainViewModel)window.DataContext!);
    }

    /// <summary>How much a visual is magnified on its way to the window.</summary>
    private static double ScaleOf(Visual v, Visual window) =>
        v.TransformToVisual(window) is { } m ? m.M11 : double.NaN;

    /// <summary>Run with the scale set, and put it back however the test ends.</summary>
    private static void At(MainViewModel vm, double percent, Action body)
    {
        try
        {
            vm.UiScalePercent = percent;
            Settle();
            body();
        }
        finally
        {
            vm.UiScalePercent = 100;
            UiScale.Current = 1.0;
        }
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.26, 1.25)]
    [InlineData(1.274, 1.25)]
    [InlineData(1.276, 1.3)]
    [InlineData(0.1, 0.75)]
    [InlineData(40, 2.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void TheFactorIsClampedAndSnappedToFivePercent(double input, double expected)
    {
        // A settings file is input. A hand-edited 40 must come out as a window
        // somebody can still read, not as one 4000% wide.
        Assert.Equal(expected, UiScale.Normalise(input));
    }

    [Fact]
    public void ASettingsFileWithoutTheKeyOpensAtOneHundredPercent()
    {
        Assert.Equal(1.0, AppSettings.Deserialize("{}").UiScale);
        var saved = new AppSettings { UiScale = 1.5 };
        Assert.Equal(1.5, AppSettings.Deserialize(saved.Serialize()).UiScale);
    }

    [AvaloniaFact]
    public void TheSettingPersistsAndAppliesAtOnce()
    {
        var (_, vm) = Open();
        At(vm, 150, () =>
        {
            Assert.Equal(1.5, UiScale.Current);
            Assert.Equal(1.5, AppSettings.Load().UiScale);
            Assert.Equal(150, vm.UiScalePercent);
        });
        Assert.Equal(1.0, AppSettings.Load().UiScale);
    }

    [AvaloniaFact]
    public void TheCanvasIsInsideNoTransformAndOnWholePixels()
    {
        // The line the whole design rests on. A canvas inside a scaled region
        // would be resampled, and at 125% its origin would sit between device
        // pixels — every stroke softened by a half-pixel shift nobody asked
        // for. 125% because it is the factor that makes fractions.
        var (window, vm) = Open();
        At(vm, 125, () =>
        {
            var canvas = window.FindControl<Control>("Canvas")!;
            Assert.DoesNotContain(canvas.GetVisualAncestors(), a => a is LayoutTransformControl);
            var m = canvas.TransformToVisual(window)!.Value;
            output.WriteLine($"canvas -> window: scale {m.M11},{m.M22} offset {m.M31},{m.M32}");
            Assert.Equal(1.0, m.M11);
            Assert.Equal(1.0, m.M22);
            Assert.Equal(Math.Round(m.M31), m.M31, 6);
            Assert.Equal(Math.Round(m.M32), m.M32, 6);
        });
    }

    [AvaloniaFact]
    public void EveryRegionOfChromeIsDrawnAtTheScale()
    {
        var (window, vm) = Open();
        At(vm, 150, () =>
        {
            // TopStrip is not in the list: nothing is docked at the top by
            // default, so it is not on screen to have a scale.
            string[] named = ["TitleBar", "Toolbar", "LeftStrip", "RightStrip", "BottomStrip", "ViewBar"];
            foreach (var name in named)
            {
                var control = window.FindControl<Control>(name)!;
                // The overlay bar scales inside its template, so its content
                // is what is magnified, not the bar's own box.
                Visual probe = control is CanvasOverlayBar bar
                    ? bar.GetVisualDescendants().OfType<ScaledChrome>().First().Child!
                    : control;
                output.WriteLine($"{name}: {ScaleOf(probe, window)}");
                Assert.Equal(1.5, ScaleOf(probe, window), 6);
            }
            var docker = window.GetVisualDescendants().OfType<Docker>().First(d => d.IsEffectivelyVisible);
            Assert.Equal(1.5, ScaleOf(docker, window), 6);
        });
    }

    [AvaloniaFact]
    public void AtOneHundredPercentThereIsNoTransformAtAll()
    {
        // Optional means absent: an artist who never touches the setting
        // pays for no transform, not an identity one.
        var (window, _) = Open();
        var regions = window.GetVisualDescendants().OfType<ScaledChrome>().ToList();
        Assert.NotEmpty(regions);
        Assert.All(regions, r => Assert.Null(r.LayoutTransform));
    }

    [AvaloniaFact]
    public void ADockerTakesMoreRoomRatherThanCrampingItsContent()
    {
        // The owner's words: the scale should "change the height and width of
        // the dockers and their contents". A side strip at 150% is half as wide
        // again on screen, and the panel inside still has its 100% width to
        // lay out in.
        var (window, vm) = Open();
        var column = window.FindControl<Grid>("WorkArea")!.ColumnDefinitions[6];
        var strip = window.FindControl<Control>("RightStrip")!;
        var before = column.ActualWidth;
        var inner = strip.Bounds.Width;
        At(vm, 150, () =>
        {
            output.WriteLine($"right column {before} -> {column.ActualWidth}; strip {inner} -> {strip.Bounds.Width}");
            Assert.Equal(before * 1.5, column.ActualWidth, 0);
            // The host's 4px margin is outside the scaled region, so the strip
            // gets a pixel or two more than it had — not a third less.
            Assert.InRange(strip.Bounds.Width, inner, inner + 3);
        });
        Settle();
        Assert.Equal(before, column.ActualWidth, 0);
    }

    [AvaloniaFact]
    public void ADraggedSidebarKeepsItsWidthThroughAScaleChange()
    {
        // Rescaled by ratio, not laid out again: re-running the layout would
        // put a sidebar back to its saved extent every time the slider moved.
        var (window, vm) = Open();
        var col = window.FindControl<Grid>("WorkArea")!.ColumnDefinitions[6];
        col.Width = new GridLength(410, GridUnitType.Pixel);
        Settle();
        At(vm, 120, () => Assert.Equal(410 * 1.2, col.Width.Value, 6));
        Assert.Equal(410, col.Width.Value, 6);
    }

    [AvaloniaFact]
    public void TheRailsDragRangeFollowsTheScaleAndComesBack()
    {
        // The adversary's find: the rail's 40–220 range never scaled, so at
        // 150% the labelled rail (150 units) was out of reach, and a dragged
        // rail's limits drifted on a round trip of scale changes.
        var (window, vm) = Open();
        var rail = window.FindControl<Grid>("WorkArea")!.ColumnDefinitions[0];
        At(vm, 150, () =>
        {
            Assert.Equal(330, rail.MaxWidth, 6);
            rail.Width = new GridLength(300, GridUnitType.Pixel);
            vm.UiScalePercent = 75;
            Settle();
            Assert.Equal(150, rail.Width.Value, 6);
            vm.UiScalePercent = 100;
            Settle();
        });
        Assert.Equal(40, rail.MinWidth, 6);
        Assert.Equal(220, rail.MaxWidth, 6);
        Assert.Equal(200, rail.Width.Value, 6);
    }

    [AvaloniaFact]
    public void AMenuOpensAtTheScaleOfTheBarItDroppedFrom()
    {
        var (window, vm) = Open();
        At(vm, 150, () =>
        {
            var file = window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().First();
            file.Open();
            Settle();
            var item = file.Items.OfType<MenuItem>().First();
            Visual root = item;
            while (root.GetVisualParent() is { } p) root = p;
            var scale = item.TransformToVisual(root)!.Value.M11;
            output.WriteLine($"menu item scale {scale}");
            file.Close();
            Assert.Equal(1.5, scale, 6);
        });
    }

    [AvaloniaFact]
    public void AFloatingPanelsWindowIsSizedAtTheScaleAndStoredWithout()
    {
        var (window, vm) = Open();
        At(vm, 150, () =>
        {
            vm.Workspace.Float(DockPanelId.Layers, 100, 100, 300, 400);
            Settle();
            var floater = window.FloatingWindowsForTests.Single(w => w.PanelId == DockPanelId.Layers);
            Assert.Equal(450, floater.Width, 6);
            Assert.Equal(600, floater.Height, 6);
            Assert.Equal((300.0, 400.0), floater.UnscaledSize);
            Assert.Equal(1.5, ScaleOf(floater.Panel!, floater), 6);

            vm.UiScalePercent = 100;
            Settle();
            Assert.Equal(300, floater.Width, 6);
        });
    }

    [AvaloniaFact]
    public void TheInterfacePageDrivesTheScale()
    {
        var (_, vm) = Open();
        var config = new ConfigureWindow(new ShortcutMap(), vm);
        config.Show();
        Settle();
        // Opening the window is not a choice. The slider's Minimum coerces its
        // value while the XAML loads, and the first version took that event as
        // the artist dragging to 75%.
        Assert.Equal(1.0, UiScale.Current);
        Assert.Equal(1.0, AppSettings.Load().UiScale);
        var list = config.FindControl<ListBox>("CategoryList")!;
        list.SelectedIndex = list.Items.Cast<ListBoxItem>().ToList().FindIndex(i => (string?)i.Content == "Interface");
        Settle();
        Assert.True(config.FindControl<ScrollViewer>("InterfacePage")!.IsVisible);

        try
        {
            var slider = config.FindControl<Slider>("UiScaleSlider")!;
            slider.Value = 137; // between steps: the slider snaps, the field shows what applied
            Settle();
            Assert.Equal(1.35, UiScale.Current);
            Assert.Equal(135m, config.FindControl<NumericUpDown>("UiScaleBox")!.Value);

            config.FindControl<NumericUpDown>("UiScaleBox")!.Value = 175;
            Settle();
            Assert.Equal(1.75, UiScale.Current);
            Assert.Equal(175, slider.Value);
        }
        finally
        {
            vm.UiScalePercent = 100;
            UiScale.Current = 1.0;
        }
    }

    /// <summary>
    /// B281's lesson applied up front: the scale is process-wide, and a static
    /// that held its followers strongly would pin every never-closed test
    /// window — and every closed floating panel — for the life of the process.
    /// </summary>
    [Fact]
    public void FollowingTheScaleDoesNotKeepAControlAlive()
    {
        static WeakReference Make()
        {
            var region = new ScaledChrome();
            UiScale.Follow(region);
            return new WeakReference(region);
        }

        var dropped = Make();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(dropped.IsAlive);
    }
}
