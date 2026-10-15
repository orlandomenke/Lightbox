using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// The reference's second slider, promoted from the gallery's prototype
/// (Q275): a pill whose track is the picture of its own value — the paint
/// fading in over transparency for opacity, over the ground for flow — with a
/// ring thumb riding it.
/// </summary>
public sealed class EffectSliderTests
{
    private static (Window Window, Slider Slider) Open(double value = 50, bool checker = false)
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = value, Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        slider.Classes.Add("effect");
        if (checker) slider.Classes.Add("checker");
        var window = new Window { Width = 400, Height = 100, Content = new StackPanel { Children = { slider } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, slider);
    }

    private static Point At(Slider s, Window w, double fraction) =>
        s.TranslatePoint(new Point(s.Bounds.Width * fraction, s.Bounds.Height / 2), w)!.Value;

    [AvaloniaFact]
    public void TheAppCarriesTheEffectTemplate()
    {
        // The prototype lived in the gallery's look files; a Slider.effect in
        // the app without them would quietly be a stock Fluent slider.
        var (_, slider) = Open(checker: true);

        var checker = slider.GetVisualDescendants().OfType<Border>().SingleOrDefault(b => b.Name == "PART_Checker");
        Assert.NotNull(checker);
        Assert.True(checker!.IsVisible, ".checker puts transparency under the effect");
        Assert.DoesNotContain(slider.GetVisualDescendants().OfType<Border>(), b => b.Name == "TrackBackground");
    }

    [AvaloniaFact]
    public void WithoutCheckerTheGroundShowsUnderTheEffect()
    {
        var (_, slider) = Open(checker: false);

        var checker = slider.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Checker");
        Assert.False(checker.IsVisible);
    }

    [AvaloniaFact]
    public void APressOnThePillPutsTheValueThere()
    {
        // The prototype's Track had no buttons either side of the thumb, and
        // a Slider moves to a press only through them: the pill could be
        // dragged by its thumb and nowhere else. A brush value is set blind.
        var (w, slider) = Open(value: 20);

        var at = At(slider, w, 0.8);
        w.MouseDown(at, MouseButton.Left);
        w.MouseUp(at, MouseButton.Left);

        Assert.InRange(slider.Value, 70, 90);
    }

    [AvaloniaFact]
    public void TheThumbDragsTheValue()
    {
        var (w, slider) = Open(value: 50);

        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        var from = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), w)!.Value;
        w.MouseDown(from, MouseButton.Left);
        w.MouseMove(from + new Point(-60, 0), RawInputModifiers.LeftMouseButton);
        w.MouseUp(from + new Point(-60, 0), MouseButton.Left);

        Assert.InRange(slider.Value, 10, 40);
    }

    [AvaloniaFact]
    public void ThePillIsARowTallAndTheThumbSitsInsideIt()
    {
        var (_, slider) = Open();

        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        Assert.Equal(24, slider.Bounds.Height);
        Assert.True(thumb.Bounds.Height < slider.Bounds.Height, "the ring rides inside the pill");

        // Its own size, not the line slider's 10 px dot: Density.axaml styles
        // every slider's thumb, and a style outranks the template.
        Assert.Equal(16, thumb.Bounds.Width);
    }
}

/// <summary>Where the brush's opacity and flow are, the effect slider is (Q275).</summary>
[Collection("BrushState")]
public sealed class EffectSliderSiteTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open(bool editor = false)
    {
        var window = new MainWindow { Width = 2400, Height = 1000 };
        window.Show();
        Pump();
        var vm = Assert.IsType<MainViewModel>(window.DataContext);
        vm.ActiveTool = ToolId.Brush;
        vm.BrushOpacity = 0.37;
        vm.BrushFlow = 0.61;
        vm.BrushSize = 33;
        vm.ForegroundColorHex = "#3366cc";
        Pump();
        if (editor)
        {
            window.OpenBrushEditor(window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ToolOptionsGear"));
            Pump();
        }

        return (window, vm);
    }

    private static void Pump()
    {
        for (var i = 0; i < 6; i++) Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Every slider in <paramref name="root"/> showing <paramref name="value"/>.</summary>
    private static List<Slider> Showing(Visual root, double value) =>
        root.GetVisualDescendants().OfType<Slider>().Where(s => Math.Abs(s.Value - value) < 1e-9).ToList();

    private static Visual Bar(MainWindow w) => w.GetVisualDescendants().OfType<DockPanel>()
        .First(p => p.GetVisualDescendants().OfType<StackPanel>().Any(s => s.Name == "QuickFixedOptions"));

    [AvaloniaTheory]
    [InlineData(0.37, true)]
    [InlineData(0.61, false)]
    public void TheBarsOpacityAndFlowAreEffectSliders(double value, bool checker)
    {
        var (w, _) = Open();

        var sliders = Showing(Bar(w), value);
        Assert.NotEmpty(sliders);
        Assert.All(sliders, s =>
        {
            Assert.Contains("effect", s.Classes);
            Assert.Equal(checker, s.Classes.Contains("checker"));
            Assert.Contains("param", s.Classes);
        });
    }

    [AvaloniaTheory]
    [InlineData(0.37, true)]
    [InlineData(0.61, false)]
    public void TheBrushEditorsOpacityAndFlowAreEffectSliders(double value, bool checker)
    {
        var (w, _) = Open(editor: true);
        var editor = w.BrushEditorForTests!;

        var sliders = Showing(editor, value);
        Assert.NotEmpty(sliders);
        Assert.All(sliders, s =>
        {
            Assert.Contains("effect", s.Classes);
            Assert.Equal(checker, s.Classes.Contains("checker"));
        });
    }

    [AvaloniaFact]
    public void SizeKeepsTheLineAndTheDot()
    {
        var (w, _) = Open(editor: true);

        var sizes = Showing(w, 33).Concat(Showing(w.BrushEditorForTests!, 33)).ToList();
        Assert.NotEmpty(sizes);
        Assert.All(sizes, s => Assert.DoesNotContain("effect", s.Classes));
    }

    [AvaloniaFact]
    public void TheTrackFadesInThePaintInHand()
    {
        var (w, vm) = Open();
        var opacity = Showing(Bar(w), 0.37).First();

        AssertRampTo(opacity, Color.Parse("#3366cc"));

        // And follows it when the colour changes.
        vm.ForegroundColorHex = "#e0703a";
        Pump();
        AssertRampTo(opacity, Color.Parse("#e0703a"));
    }

    private static void AssertRampTo(Slider slider, Color paint)
    {
        var ramp = Assert.IsAssignableFrom<ILinearGradientBrush>(slider.Background);
        var stops = ramp.GradientStops.OrderBy(s => s.Offset).ToList();
        Assert.Equal(0, stops[0].Color.A);
        Assert.Equal(paint, stops[^1].Color);
    }
}
