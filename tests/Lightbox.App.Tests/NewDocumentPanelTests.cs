using Avalonia.Headless.XUnit;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The File → New fields: the swap button and the background picker feed
/// <c>Collect()</c> what the artist actually chose.
/// </summary>
/// <remarks>
/// The panel is shared by the dialog and the start screen, so these tests are
/// two surfaces for the price of one — which is exactly why the fields live in
/// a control of their own.
/// </remarks>
public class NewDocumentPanelTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void SwapTradesWidthForHeight()
    {
        var panel = new NewDocumentPanel();
        panel.WidthBox.Value = 1920;
        panel.HeightBox.Value = 1080;

        panel.SwapSize();

        var settings = panel.Collect();
        output.WriteLine($"{settings.Width} x {settings.Height}");
        Assert.Equal(1080, settings.Width);
        Assert.Equal(1920, settings.Height);
    }

    [AvaloniaFact]
    public void SwappingTwiceIsWhereYouStarted()
    {
        var panel = new NewDocumentPanel();
        panel.WidthBox.Value = 960;
        panel.HeightBox.Value = 540;

        panel.SwapSize();
        panel.SwapSize();

        var settings = panel.Collect();
        Assert.Equal(960, settings.Width);
        Assert.Equal(540, settings.Height);
    }

    /// <summary>
    /// The background is a ColorField now — the same wheel every other colour
    /// in the app opens — and what it holds is what the document gets.
    /// </summary>
    [AvaloniaFact]
    public void TheBackgroundFieldFeedsTheDocument()
    {
        var panel = new NewDocumentPanel();
        panel.BackgroundField.Hex = "#1A2B3C";

        var settings = panel.Collect();
        output.WriteLine($"background {settings.BackgroundColor}");
        Assert.Equal("#1a2b3c", settings.BackgroundColor);
    }

    /// <summary>A hex that parses to nothing falls back to white, as the old box did.</summary>
    [AvaloniaFact]
    public void AnUnparseableBackgroundFallsBackToWhite()
    {
        var panel = new NewDocumentPanel();
        panel.BackgroundField.Hex = "not a colour";

        Assert.Equal("#ffffff", panel.Collect().BackgroundColor);
    }
}
