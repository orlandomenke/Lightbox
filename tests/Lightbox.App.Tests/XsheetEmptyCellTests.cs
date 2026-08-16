using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The X-sheet has two kinds of empty cell and they used to look alike (Q89).
/// </summary>
/// <remarks>
/// A cel inside the scene with no drawing is a <b>hold or a blanked drawing</b>:
/// the playhead goes there, a mark keys it, it can be selected and dragged. A
/// cell past <c>Scene.FrameCount</c> is <b>virtual</b> — nothing at all, and
/// every one of those gestures refuses it. Both were a dark box separated only
/// by an opacity, which is far too quiet to carry a distinction that decides
/// whether a click does anything at all.
/// </remarks>
public class XsheetEmptyCellTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "Lightbox.sln")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }
        return dir;
    }

    [AvaloniaFact]
    public void PastTheEndOfTheSceneIsHatched_NotJustFainter()
    {
        Assert.True(
            Application.Current!.TryFindResource("CelVirtualHatchBrush", out var found),
            "the past-the-end hatch does not resolve");

        // A tiling brush rather than a flat colour is the whole point: a solid
        // fill at any opacity is the same *kind* of thing as an empty cel's
        // fill, which is what made the two indistinguishable.
        var hatch = Assert.IsType<DrawingBrush>(found);
        Assert.Equal(TileMode.Tile, hatch.TileMode);
        Assert.NotEqual(default, hatch.DestinationRect);
    }

    [AvaloniaFact]
    public void TheHatchIsGreyRatherThanAnAccent()
    {
        // The reason the hover and band tints give: a state that is not a
        // meaning must not wear a colour that means something. An accent here
        // would read as a role — key, breakdown, inbetween — which is exactly
        // what past-the-end is not.
        Application.Current!.TryFindResource("CelVirtualHatchBrush", out var found);
        var pen = Assert.IsType<DrawingBrush>(found).Drawing is GeometryDrawing drawing
            ? drawing.Pen
            : null;
        Assert.NotNull(pen);
        var colour = pen!.Brush switch
        {
            ISolidColorBrush solid => solid.Color,
            _ => throw new Xunit.Sdk.XunitException($"the hatch pen is not a solid colour: {pen.Brush}"),
        };
        Assert.True(
            colour.R == colour.G && colour.G == colour.B,
            $"the hatch is tinted ({colour}) — it must be neutral");
    }

    [AvaloniaFact]
    public void TheVirtualCelStyleActuallyUsesTheHatch()
    {
        // The resource existing proves nothing on its own; the failure this
        // guards is a brush defined and never referenced, which looks correct
        // in the palette and changes nothing on screen.
        var xaml = File.ReadAllText(
            Path.Combine(RepoRoot(), "src/Lightbox.App/Views/MainWindow.axaml"));
        var style = Regex.Match(
            xaml,
            @"<Style Selector=""Button\.cel\.virtualCell"">(.*?)</Style>",
            RegexOptions.Singleline);
        Assert.True(style.Success, "the virtual-cel style is gone");
        Assert.Contains("CelVirtualHatchBrush", style.Groups[1].Value);
    }
}
