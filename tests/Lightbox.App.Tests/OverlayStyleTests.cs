using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The overlay style's own vocabulary — the states and the second colour every
/// painter on the canvas draws with (Q249, Q252) — rendered and read back.
/// </summary>
/// <remarks>
/// The pen's painter is tested where it lives; this is the part the rest of the
/// canvas shares. Each rule here is one the owner ruled on, so a change that
/// breaks a test is a change to a decision and should arrive as one.
/// </remarks>
public class OverlayStyleTests(ITestOutputHelper output)
{
    private const int W = 120, H = 80;

    private static SKBitmap Render(SKColor page, Action<SKCanvas, OverlayStyle.Pens> draw, float scale = 1f)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)!;
        surface.Canvas.Clear(page);
        surface.Canvas.Scale(scale);
        using (var pens = new OverlayStyle.Pens(scale)) draw(surface.Canvas, pens);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static int Difference(SKColor a, SKColor b) => Math.Max(
        Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));

    /// <summary>How many pixels along a row or a column through a point are not the page.</summary>
    private static int Extent(SKBitmap bmp, SKColor page, int x, int y, bool across)
    {
        var count = 0;
        for (var i = 0; i < (across ? W : H); i++)
        {
            var p = across ? bmp.GetPixel(i, y) : bmp.GetPixel(x, i);
            if (Difference(p, page) > 24) count++;
        }
        return count;
    }

    /// <summary>The same line through the middle of a pixel row, for one-pixel marks.</summary>
    private static SKPath Centred()
    {
        var path = new SKPath();
        path.MoveTo(10, 40.5f);
        path.LineTo(110, 40.5f);
        return path;
    }

    private static SKPath Level()
    {
        var path = new SKPath();
        path.MoveTo(10, 40);
        path.LineTo(110, 40);
        return path;
    }

    /// <summary>
    /// Hover grows a grip by two pixels and leaves it open: bigger says "this
    /// one", and it cannot be read as selected, which is filled.
    /// </summary>
    [Fact]
    public void AHoveredGripIsTwoPixelsBiggerAndStillOpen()
    {
        var page = new SKColor(0x80, 0x80, 0x80);
        using var rest = Render(page, (c, pens) => pens.Grip(c, 60, 40, round: false, selected: false));
        using var hover = Render(page, (c, pens) => pens.Grip(c, 60, 40, round: false, selected: false, hover: true));

        var restWide = Extent(rest, page, 60, 40, across: true);
        var hoverWide = Extent(hover, page, 60, 40, across: true);
        output.WriteLine($"grip at rest {restWide} px wide, hovered {hoverWide} px; hovered centre {hover.GetPixel(60, 40)}");
        Assert.InRange(hoverWide - restWide, 2, 3);
        Assert.True(Difference(hover.GetPixel(60, 40), SKColors.White) < 12, "a hovered grip is still open");
    }

    [Fact]
    public void AHoveredLineIsAPixelThickerInTheSameColour()
    {
        var page = SKColors.White;
        using var path = Level();
        using var rest = Render(page, (c, pens) => pens.Line(c, path));
        using var hover = Render(page, (c, pens) => pens.Line(c, path, hover: true));

        var restHigh = Extent(rest, page, 60, 40, across: false);
        var hoverHigh = Extent(hover, page, 60, 40, across: false);
        output.WriteLine($"line at rest {restHigh} px high, hovered {hoverHigh} px");
        Assert.InRange(hoverHigh - restHigh, 1, 2);
        // The middle of the line is the accent either way.
        Assert.True(Difference(rest.GetPixel(60, 40), OverlayStyle.Accent) < 40);
        Assert.True(Difference(hover.GetPixel(60, 40), OverlayStyle.Accent) < 12);
    }

    /// <summary>
    /// The second colour: a mark that is a reference point rather than the
    /// thing being edited — a pivot, a warp band, a guide's anchor.
    /// </summary>
    [Fact]
    public void AReferenceMarkIsAmberAndCasedLikeEverythingElse()
    {
        using var path = Level();
        using var onWhite = Render(SKColors.White, (c, pens) => pens.Line(c, path, role: OverlayStyle.Role.Reference));
        using var onAmber = Render(OverlayStyle.Reference, (c, pens) => pens.Line(c, path, role: OverlayStyle.Role.Reference));

        var middle = onWhite.GetPixel(60, 40);
        var strongest = 0;
        for (var y = 34; y <= 46; y++) strongest = Math.Max(strongest, Difference(onAmber.GetPixel(60, y), OverlayStyle.Reference));
        output.WriteLine($"reference line centre {middle}; on a page of its own colour it differs by {strongest}");
        // A 1.5 px line across a pixel row is three-quarters amber over its casing.
        Assert.True(Difference(middle, OverlayStyle.Reference) < 60);
        Assert.True(strongest >= 90, $"a reference line on its own colour differs by only {strongest}");
    }

    [Fact]
    public void TheReferenceColourIsThePalettesAmber()
    {
        var palette = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lightbox.App", "Styles", "Palette.axaml"));
        var c = OverlayStyle.Reference;
        Assert.Contains($"<Color x:Key=\"AccentAmber\">#{c.Red:X2}{c.Green:X2}{c.Blue:X2}</Color>", palette);
    }

    /// <summary>
    /// A guide being drawn against is one quiet pass — no casing, nothing that
    /// reads as selected. Thirty of them must not look like thirty selections.
    /// </summary>
    [Fact]
    public void AQuietLineIsOneFaintPass()
    {
        var page = SKColors.White;
        using var path = Level();
        using var quiet = Render(page, (c, pens) => pens.Quiet(c, path));
        using var full = Render(page, (c, pens) => pens.Line(c, path));

        var quietHigh = Extent(quiet, page, 60, 40, across: false);
        var fullHigh = Extent(full, page, 60, 40, across: false);
        var quietStrength = Difference(quiet.GetPixel(60, 40), page);
        var fullStrength = Difference(full.GetPixel(60, 40), page);
        output.WriteLine($"quiet line {quietHigh} px high at strength {quietStrength}; styled line {fullHigh} px at {fullStrength}");
        Assert.True(quietHigh < fullHigh);
        Assert.True(quietStrength > 24, "it still has to be there to draw against");
        Assert.True(quietStrength < fullStrength);
    }

    /// <summary>
    /// A dragged-out area is the styled line around a faint wash: a region,
    /// with the artwork under it still legible.
    /// </summary>
    [Fact]
    public void AnAreaIsAFaintWashInsideTheStyledLine()
    {
        var page = SKColors.White;
        using var bmp = Render(page, (c, pens) => pens.Area(c, new SKRect(20, 20, 100, 60)));

        var inside = Difference(bmp.GetPixel(60, 40), page);
        var edge = 0;
        for (var y = 14; y <= 26; y++) edge = Math.Max(edge, Difference(bmp.GetPixel(60, y), page));
        var outside = Difference(bmp.GetPixel(60, 70), page);
        output.WriteLine($"inside the area {inside}, its edge {edge}, outside {outside}");
        Assert.InRange(inside, 8, 60);
        Assert.True(edge >= 90);
        Assert.Equal(0, outside);
    }

    /// <summary>
    /// The outline of a line the arrow has picked is thin enough that the ink
    /// shows either side of it.
    /// </summary>
    /// <remarks>
    /// The owner's ruling on the first render of the cased line over a picked
    /// stroke: <em>"make the outline thinner so the ink shows"</em>. At
    /// fit-to-window a drawn line is two or three pixels on screen, and a
    /// 3.5-pixel cased outline replaced it with chrome — the fault the white
    /// arrow's retrace had. The cost was named and accepted: an uncased
    /// hairline can be lost on artwork of the accent's own colour.
    /// </remarks>
    [Fact]
    public void AnOutlineOverInkLeavesTheInkShowing()
    {
        var ink = new SKColor(0xC0, 0x20, 0x20);
        SKBitmap Over(Action<SKCanvas, OverlayStyle.Pens, SKPath> draw) => Render(SKColors.White, (c, pens) =>
        {
            using var path = Centred();
            using var paint = new SKPaint { Color = ink, StrokeWidth = 6, Style = SKPaintStyle.Stroke };
            c.DrawPath(path, paint);
            draw(c, pens, path);
        });

        using var outlined = Over((c, pens, path) => pens.Outline(c, path));
        using var cased = Over((c, pens, path) => pens.Line(c, path));

        // The row next to the centre row of a six-pixel line.
        var beside = Difference(outlined.GetPixel(60, 39), ink);
        var besideCased = Difference(cased.GetPixel(60, 39), ink);
        var centre = Difference(outlined.GetPixel(60, 40), OverlayStyle.Accent);
        output.WriteLine($"beside the outline the ink differs by {beside} (under the cased line: {besideCased}); the outline's centre is {centre} from the accent");
        Assert.True(beside < 24, "the ink is still there beside the outline");
        Assert.True(besideCased > 60, "which is what the cased line did not leave");
        Assert.True(centre < 12, "and the outline is the accent");
    }

    [Fact]
    public void AHoveredOutlineIsThickerAndStillLeavesInkAtTheEdge()
    {
        var page = SKColors.White;
        using var path = Centred();
        using var rest = Render(page, (c, pens) => pens.Outline(c, path));
        using var hover = Render(page, (c, pens) => pens.Outline(c, path, hover: true));
        var restHigh = Extent(rest, page, 60, 40, across: false);
        var hoverHigh = Extent(hover, page, 60, 40, across: false);
        output.WriteLine($"outline at rest {restHigh} px high, hovered {hoverHigh} px");
        Assert.True(hoverHigh > restHigh);
        Assert.True(hoverHigh <= 3, "a hovered outline is still narrower than a drawn line");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lightbox.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
