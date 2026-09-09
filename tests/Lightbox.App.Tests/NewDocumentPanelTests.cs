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

    /// <summary>
    /// A hex that parses to nothing falls back to the default paper.
    /// </summary>
    /// <remarks>
    /// It used to fall back to white, which was the default then. White now
    /// would be a third answer — neither what the field says nor what the panel
    /// offers — so the fallback follows the default rather than being pinned.
    /// </remarks>
    [AvaloniaFact]
    public void AnUnparseableBackgroundFallsBackToTheDefaultPaper()
    {
        var panel = new NewDocumentPanel();
        panel.BackgroundField.Hex = "not a colour";

        Assert.Equal(NewDocumentPanel.DefaultBackground, panel.Collect().BackgroundColor);
    }

    /// <summary>
    /// A fresh panel opens at 180 ppi — in the box the artist reads, and in what
    /// <c>Collect()</c> hands the document.
    /// </summary>
    /// <remarks>
    /// <b>Q183.</b> Both halves, because they can disagree: the default is set
    /// in the constructor now, and <c>Collect()</c> falls back to the same
    /// constant when the box is empty. Assert only the settings and a build that
    /// forgot to fill the box in would still pass while showing a blank field.
    /// </remarks>
    [AvaloniaFact]
    public void ANewDocumentOpensAt180Ppi()
    {
        var panel = new NewDocumentPanel();

        output.WriteLine($"box {panel.PpiBox.Value}, collected {panel.Collect().Ppi}");
        Assert.Equal(180m, panel.PpiBox.Value);
        Assert.Equal(180, panel.Collect().Ppi);
    }

    /// <summary>A fresh panel opens on mid grey paper, shown and collected.</summary>
    /// <remarks><b>Q183.</b> Same two halves as the resolution, for the same reason.</remarks>
    [AvaloniaFact]
    public void ANewDocumentOpensOnMidGreyPaper()
    {
        var panel = new NewDocumentPanel();

        var settings = panel.Collect();
        output.WriteLine($"field {panel.BackgroundField.Hex}, collected {settings.BackgroundColor}");
        Assert.Equal("#808080", panel.BackgroundField.Hex);
        Assert.Equal("#808080", settings.BackgroundColor);
        Assert.False(settings.TransparentBackground);
    }

    /// <summary>
    /// "Half black, half white" is 128 per channel, not the lighter grey that
    /// splits the difference in luminance.
    /// </summary>
    /// <remarks>
    /// <b>Q183.</b> The trap this pins: mid grey by *luminance* is around
    /// <c>#bcbcbc</c>, and it is a defensible-sounding correction that would
    /// give an artist a paper visibly lighter than the 50% grey every other
    /// tool hands them. The number is what was asked for, so the number is what
    /// is asserted — against the parsed channels rather than the constant, so
    /// this cannot pass by comparing a typo with itself.
    /// </remarks>
    [AvaloniaFact]
    public void FiftyPercentGreyIs128PerChannel()
    {
        var rgb = Lightbox.App.Services.ColorSpace.HexToRgb(NewDocumentPanel.DefaultBackground);

        Assert.NotNull(rgb);
        // HexToRgb hands back 0..1 per channel, so the byte is what to compare.
        var (r, g, b) = (
            (int)Math.Round(rgb.Value.R * 255),
            (int)Math.Round(rgb.Value.G * 255),
            (int)Math.Round(rgb.Value.B * 255));
        output.WriteLine(
            $"{NewDocumentPanel.DefaultBackground} -> {r},{g},{b} "
            + "(mid grey by luminance would be about 188)");
        Assert.Equal(128, r);
        Assert.Equal(128, g);
        Assert.Equal(128, b);
    }
}
