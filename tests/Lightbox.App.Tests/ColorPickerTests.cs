using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

public class ColorSpaceTests
{
    [Fact]
    public void KnownColor_Red_ConvertsToEveryModel()
    {
        var (h, s, v) = ColorSpace.RgbToHsv(1, 0, 0);
        Assert.Equal(0, h, 6);
        Assert.Equal(1, s, 6);
        Assert.Equal(1, v, 6);

        var (hl, sl, l) = ColorSpace.RgbToHsl(1, 0, 0);
        Assert.Equal(0, hl, 6);
        Assert.Equal(1, sl, 6);
        Assert.Equal(0.5, l, 6);

        var (c, m, y, k) = ColorSpace.RgbToCmyk(1, 0, 0);
        Assert.Equal(0, c, 6);
        Assert.Equal(1, m, 6);
        Assert.Equal(1, y, 6);
        Assert.Equal(0, k, 6);
    }

    [Theory]
    [InlineData("#1a1a1a")]
    [InlineData("#ff8800")]
    [InlineData("#3060c0")]
    [InlineData("#000000")]
    [InlineData("#ffffff")]
    public void EveryModel_RoundTripsThroughHex(string hex)
    {
        var (r, g, b) = ColorSpace.HexToRgb(hex)!.Value;

        var (h, s, v) = ColorSpace.RgbToHsv(r, g, b);
        var hsv = ColorSpace.HsvToRgb(h, s, v);
        Assert.Equal(hex, ColorSpace.RgbToHex(hsv.R, hsv.G, hsv.B));

        var (hl, sl, l) = ColorSpace.RgbToHsl(r, g, b);
        var hsl = ColorSpace.HslToRgb(hl, sl, l);
        Assert.Equal(hex, ColorSpace.RgbToHex(hsl.R, hsl.G, hsl.B));

        var (c, m, y, k) = ColorSpace.RgbToCmyk(r, g, b);
        var cmyk = ColorSpace.CmykToRgb(c, m, y, k);
        Assert.Equal(hex, ColorSpace.RgbToHex(cmyk.R, cmyk.G, cmyk.B));
    }

    [Fact]
    public void InvalidHex_ReturnsNull()
    {
        Assert.Null(ColorSpace.HexToRgb("nope"));
        Assert.Null(ColorSpace.HexToRgb("#12345"));
        Assert.Null(ColorSpace.HexToRgb(""));
    }
}

public class ColorPickerViewModelTests
{
    [AvaloniaFact]
    public void ChannelEdits_CommitToBrushColor()
    {
        var vm = new MainViewModel(null);
        vm.ColorPicker.Red = 255;
        vm.ColorPicker.Green = 0;
        vm.ColorPicker.Blue = 0;
        Assert.Equal("#ff0000", vm.ColorHex);
    }

    [AvaloniaFact]
    public void ExternalHex_UpdatesEveryRepresentation()
    {
        var vm = new MainViewModel(null);
        vm.ColorHex = "#00ff00";

        var p = vm.ColorPicker;
        Assert.Equal(120, p.Hue, 3);
        Assert.Equal(100, p.HsvSaturation, 3);
        Assert.Equal(100, p.HsvValue, 3);
        Assert.Equal(50, p.HslLightness, 3);
        Assert.Equal(0, p.Red, 3);
        Assert.Equal(255, p.Green, 3);
        Assert.Equal(0, p.Blue, 3);
        Assert.Equal(100, p.Cyan, 3);
        Assert.Equal(0, p.Magenta, 3);
    }

    [AvaloniaFact]
    public void HuePersists_WhenColorTurnsBlack()
    {
        var vm = new MainViewModel(null);
        vm.ColorHex = "#3060c0"; // a blue → hue ~220
        var hueBefore = vm.ColorPicker.Hue;
        Assert.True(hueBefore > 200 && hueBefore < 240);

        vm.ColorPicker.HsvValue = 0; // black — hue would degenerate to 0
        Assert.Equal("#000000", vm.ColorHex);
        Assert.Equal(hueBefore, vm.ColorPicker.Hue, 3);
        Assert.Equal(hueBefore, vm.ColorPicker.WheelHsv.H, 3);
    }

    [AvaloniaFact]
    public void CmykEdits_ProduceExpectedColor()
    {
        var vm = new MainViewModel(null);
        var p = vm.ColorPicker;
        p.Cyan = 0;
        p.Magenta = 100;
        p.Yellow = 100;
        p.Black = 0;
        Assert.Equal("#ff0000", vm.ColorHex);
    }

    [AvaloniaFact]
    public void InvalidHexTyped_IsIgnoredByPicker()
    {
        var vm = new MainViewModel(null);
        vm.ColorHex = "#ff8800";
        vm.ColorHex = "not-a-color";
        Assert.Equal("#ff8800", vm.ColorPicker.Hex);
    }

    [AvaloniaFact]
    public void ModeFlags_FollowSelection()
    {
        var p = new MainViewModel(null).ColorPicker;
        Assert.True(p.IsWheelMode);
        p.Mode = ColorMode.Cmyk;
        Assert.True(p.IsCmykMode);
        Assert.False(p.IsWheelMode);
    }

    /// <summary>
    /// The panel's wheel and the flyout body are the same instrument: a hue
    /// ring with the SV square inside it. The flyout kept the old disc when
    /// the panel was redesigned, and the New file dialog is where the drift
    /// got noticed — the template exists so the picker is learned once, so
    /// the two constructions are held to the same parts here.
    /// </summary>
    [Fact]
    public void TheFlyoutBodyDrawsTheSameWheelAsThePanel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        var app = Path.Combine(dir!.FullName, "src", "Lightbox.App");
        var body = File.ReadAllText(Path.Combine(app, "Styles", "ColorPicker.axaml"));
        var panel = File.ReadAllText(Path.Combine(app, "Views", "MainWindow.axaml"));

        foreach (var source in new[] { body, panel })
        {
            Assert.Contains("HueRing", source);
            Assert.Contains("Shape=\"Box\" Components=\"SaturationValue\"", source);
        }
        // The disc the redesign replaced must not linger in either file.
        Assert.DoesNotContain("Components=\"HueSaturation\"", body);
        Assert.DoesNotContain("Components=\"HueSaturation\"", panel);
    }
}
