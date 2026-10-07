using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Dark-lit and Studio grey (Q203): two theme dictionaries in Palette.axaml,
/// switched live through the DynamicResource brushes step 1 put on every control.
/// </summary>
[Collection("BrushState")]
public sealed class ThemeTests : BrushStateIsolated
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Styles(params string[] parts) =>
        File.ReadAllText(Path.Combine([Root(), "src", "Lightbox.App", "Styles", .. parts]));

    private static Color BrushColor(Control on, string key)
    {
        Assert.True(on.TryFindResource(key, on.ActualThemeVariant, out var value), $"{key} not found");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    [AvaloniaFact]
    public void DarkLitIsTheDefaultAndStudioGreySwitchesLive()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        try
        {
            Assert.Equal("Dark-lit", vm.ThemeChoice);
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            Assert.Equal(Color.Parse("#222225"), BrushColor(window, "SurfacePanelBrush"));

            // A control already on screen, reading a brush dynamically, follows
            // the switch with no restart — what the DynamicResource conversion
            // in step 1 was for.
            var probe = new Border();
            probe[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SurfacePanelBrush");
            ((Panel)window.FindControl<Grid>("RootGrid")!).Children.Add(probe);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#222225"), Assert.IsAssignableFrom<ISolidColorBrush>(probe.Background).Color);

            vm.ThemeChoice = "Studio grey";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
            Assert.Equal(Color.Parse("#B9B9B7"), BrushColor(window, "SurfacePanelBrush"));
            Assert.Equal(Color.Parse("#B9B9B7"), Assert.IsAssignableFrom<ISolidColorBrush>(probe.Background).Color);
            Assert.Equal("Light", AppSettings.Load().Theme);
        }
        finally
        {
            vm.ThemeChoice = "Dark-lit";
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void ChromeDrawnInCodeReadsTheThemeItIsIn()
    {
        // The timeline, rulers and graph draw their own text and grid, so they
        // cannot name a DynamicResource. Before Q203's piece 3 they drew white
        // hairlines and #A6ABB8 text, which vanished on Studio grey.
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        try
        {
            var probe = new Lightbox.App.Controls.TrackView();
            ((Panel)window.FindControl<Grid>("RootGrid")!).Children.Add(probe);
            // The canvas reads its surround on the UI thread when the theme
            // changes, and hands it to the render thread with the frame.
            var canvas = new Lightbox.App.Rendering.CanvasControl();
            ((Panel)window.FindControl<Grid>("RootGrid")!).Children.Add(canvas);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#2B2B2B"), Lightbox.App.Controls.ThemeColour.Of(probe, "CanvasSurroundBrush", Colors.Red));
            Assert.True(Lightbox.App.Controls.ThemeColour.Hairline(probe, 0x10).R > 0x80, "Dark-lit's hairline is light ink");
            Assert.Equal(new SkiaSharp.SKColor(0x2B, 0x2B, 0x2B), canvas.SurroundForTests);

            vm.ThemeChoice = "Studio grey";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Color.Parse("#8A8A88"), Lightbox.App.Controls.ThemeColour.Of(probe, "CanvasSurroundBrush", Colors.Red));
            Assert.Equal(new SkiaSharp.SKColor(0x8A, 0x8A, 0x88), canvas.SurroundForTests);
            var ink = Lightbox.App.Controls.ThemeColour.Hairline(probe, 0x10);
            Assert.True(ink.R < 0x80 && ink.A == 0x10, $"Studio grey's hairline is {ink}: it must be dark ink, or it vanishes on the grey");
        }
        finally
        {
            vm.ThemeChoice = "Dark-lit";
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Theory]
    [InlineData("Controls/TrackView.cs")]
    [InlineData("Controls/TimelineRuler.cs")]
    [InlineData("Controls/RulerStrip.cs")]
    [InlineData("Controls/GraphView.cs")]
    [InlineData("Controls/TimingChartView.cs")]
    [InlineData("Controls/CurveEditor.cs")]
    public void ChromeDrawnInCodeRepaintsWhenTheThemeSwitches(string file)
    {
        // Reading the theme at render time is half of it; a control that is not
        // asked to render again keeps the old theme's colours on screen until
        // something else happens to invalidate it.
        var source = File.ReadAllText(Path.Combine(Root(), "src", "Lightbox.App", file));
        Assert.Contains("ThemeColour.RepaintOnThemeChange(this", source);
    }

    [Fact]
    public void AHandEditedThemeCanOnlyLandOnOneThatExists()
    {
        // Anything but "Light" is Dark: ThemeChoice maps it, ApplyTheme picks it.
        var settings = AppSettings.Deserialize("""{ "Theme": "Neon" }""");
        Assert.Equal("Neon", settings.Theme);
        Assert.NotEqual("Light", settings.Theme);
        Assert.Equal("Dark", new AppSettings().Theme);
    }

    [Fact]
    public void BothThemesDefineTheSameKeys()
    {
        // A key only one theme defines falls through to the top level, or to
        // nothing, in the other — the theme would quietly half-switch.
        static HashSet<string> Keys(string file) =>
            Regex.Matches(Styles("Themes", file), @"x:Key=""(\w+)""").Select(m => m.Groups[1].Value).ToHashSet();
        var dark = Keys("DarkLit.axaml");
        var light = Keys("StudioGrey.axaml");
        Assert.True(dark.SetEquals(light),
            "keys in one theme only: " + string.Join(", ", dark.Except(light).Concat(light.Except(dark))));
    }

    [Fact]
    public void NoThemedKeyIsShadowedAtTheTopLevel()
    {
        // A dictionary's own keys are searched before its theme dictionaries, and
        // a later merged dictionary (Theme.axaml) before an earlier one: a themed
        // brush also defined in either place never switches.
        var themed = Regex.Matches(Styles("Themes", "DarkLit.axaml"), @"x:Key=""(\w+)""")
            .Select(m => m.Groups[1].Value).ToHashSet();
        foreach (var file in new[] { "Palette.axaml", "Theme.axaml" })
        {
            var text = Regex.Replace(Styles(file), @"<ResourceDictionary\.ThemeDictionaries>.*?</ResourceDictionary\.ThemeDictionaries>", "", RegexOptions.Singleline);
            var shadows = Regex.Matches(text, @"x:Key=""(\w+)""").Select(m => m.Groups[1].Value).Where(themed.Contains).ToList();
            Assert.True(shadows.Count == 0, $"{file} shadows themed keys: {string.Join(", ", shadows)}");
        }
    }
}
