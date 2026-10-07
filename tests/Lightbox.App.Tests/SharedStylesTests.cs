using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The app and the component gallery (tools/Lightbox.Gallery, Q203) load one
/// set of styles. These keep it one set.
/// </summary>
public sealed class SharedStylesTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]));

    [Fact]
    public void TheAppLoadsItsStylesOnlyThroughTheSharedFiles()
    {
        // A style added straight into App.axaml would reach the app and not the
        // gallery, and every look judged there would be judged without it.
        var app = Read("src", "Lightbox.App", "App.axaml");
        var includes = Regex.Matches(app, @"<(StyleInclude|ResourceInclude) Source=""([^""]+)""")
            .Select(m => m.Groups[2].Value).ToList();

        Assert.Equal(
            ["avares://Lightbox.App/Styles/AppResources.axaml", "avares://Lightbox.App/Styles/AppStyles.axaml"],
            includes);
        Assert.DoesNotContain("<Style ", app);
        Assert.DoesNotContain("<FluentTheme", app);
    }

    [Fact]
    public void TheGalleryLoadsTheSameFiles()
    {
        var look = Read("tools", "Lightbox.Gallery", "Look.cs");
        Assert.Contains("avares://Lightbox.App/Styles/AppResources.axaml", look);
        Assert.Contains("avares://Lightbox.App/Styles/AppStyles.axaml", look);
    }

    [Theory]
    [InlineData("PART_Frame")]
    [InlineData("PART_Bar")]
    public void AFramesOutlineIsAStyleSoALookCanChangeIt(string part)
    {
        // A value written inline in a ControlTemplate outranks every style, so
        // the docker's outline could not be restyled by any look until it moved
        // into a setter. The gallery's first snapshots showed it: every lit look
        // kept the shipped outline.
        var styles = Read("src", "Lightbox.App", "Styles", "AppStyles.axaml");
        var element = Regex.Match(styles, $@"<Border x:Name=""{part}""[^>]*>");
        Assert.True(element.Success, $"{part} is no longer in AppStyles.axaml");
        Assert.DoesNotContain("BorderThickness", element.Value);
        Assert.DoesNotContain("CornerRadius", element.Value);
        Assert.Contains($"/template/ Border#{part}\"", styles);
    }

    /// <summary>
    /// The text checks above say where the values are written; this says what
    /// the real window ends up with. The two risks the review could only reason
    /// about: Fluent nested one level deeper inside AppStyles.axaml, and
    /// /template/ selectors reaching a frame through ScaledChrome.
    /// </summary>
    [AvaloniaFact]
    public void TheShippedWindowStillWearsTheShippedValues()
    {
        var window = new Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var borders = window.GetVisualDescendants().OfType<Border>().ToList();
        // The values of the look (Q203, step 3): an island with no outline that
        // does not clip its own shadow, a darker header strip, a bar with the
        // edge light and the container's corners, tabs rounded on top.
        var frame = borders.First(b => b.Name == "PART_Frame");
        Assert.Equal(default, frame.BorderThickness);
        Assert.Equal(new CornerRadius(6), frame.CornerRadius);
        Assert.False(frame.ClipToBounds);
        Assert.NotEqual(default, frame.BoxShadow);

        var header = borders.First(b => b.Name == "PART_Header");
        Assert.True(window.TryFindResource("BackgroundSecondaryBrush", window.ActualThemeVariant, out var strip));
        Assert.Equal(((ISolidColorBrush)strip!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(header.Background).Color);

        var bar = borders.First(b => b.Name == "PART_Bar");
        Assert.Equal(new Thickness(1), bar.BorderThickness);
        Assert.Equal(new CornerRadius(6), bar.CornerRadius);
        Assert.IsType<ConicGradientBrush>(bar.BorderBrush);

        var tabs = borders.Where(b => b.Name == "PART_Tab").ToList();
        Assert.NotEmpty(tabs);
        var resting = tabs.First(t => t.TemplatedParent is ListBoxItem { IsSelected: false });
        Assert.Equal(new CornerRadius(4, 4, 0, 0), resting.CornerRadius);

        // Fluent's accent, from the ColorPaletteResources inside AppStyles.axaml:
        // the raised neutral since Q203 (no colour for "on").
        Assert.True(window.TryFindResource("SystemAccentColor", window.ActualThemeVariant, out var accent));
        Assert.Equal(Color.Parse("#3C3C41"), Assert.IsType<Color>(accent));
    }
}
