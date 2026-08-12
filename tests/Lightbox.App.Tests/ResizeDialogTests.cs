using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The resize dialog, driven with no window.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic lives in the view model precisely so the cases nobody clicks
/// through by hand are cheap to assert: a link that would divide by zero, a crop
/// to one pixel, a PPI change that is not a resize, a link that oscillates
/// between its two fields.
/// </para>
/// <para>
/// <b>What the dialog is <em>for</em> is keeping the two modes apart</b>, and
/// most of this file is about that. Resizing the paper must move nothing;
/// rescaling the artwork must move everything. An artist who asked for one and
/// got the other has lost the grain of every stroke in the document, and the
/// only cheap moment to prevent that is before the button.
/// </para>
/// </remarks>
public class ResizeDialogTests(ITestOutputHelper output)
{
    private static Doc Doc(int w = 400, int h = 300)
    {
        var doc = DocumentFactory.CreateDoc(w, h, 1);
        var frame = (Frame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#2050b0",
            Points = [new StrokePoint(50, 60, 1), new StrokePoint(150, 160, 1)],
            Brush = new BrushSettings { Size = 12, Opacity = 1, Flow = 1 },
        });
        return doc;
    }

    private static ResizeDialogViewModel Vm(Doc doc, ResizeMode mode) =>
        new ResizeDialogViewModel(doc.Scene, mode).WithDefaults();

    // ---- the fields ---------------------------------------------------------------------

    [Fact]
    public void ItOpensOnTheSizeTheDocumentAlreadyIs()
    {
        var doc = Doc();
        var vm = Vm(doc, ResizeMode.Canvas);

        Assert.Equal(400, vm.Width);
        Assert.Equal(300, vm.Height);
        Assert.Equal(doc.Scene.Ppi, vm.Ppi);
        // Nothing to do yet, so the button is off rather than reporting a
        // successful no-op.
        Assert.False(vm.CanApply);
    }

    /// <summary>
    /// The link is on for an image and off for a canvas.
    /// </summary>
    /// <remarks>
    /// Different questions: rescaling artwork non-uniformly distorts it and
    /// almost nobody means to, while adding paper to one side is the ordinary
    /// reason to open the canvas mode at all.
    /// </remarks>
    [Fact]
    public void TheLinkStartsWhereEachModeWantsIt()
    {
        Assert.True(Vm(Doc(), ResizeMode.Image).Linked);
        Assert.False(Vm(Doc(), ResizeMode.Canvas).Linked);
    }

    [Fact]
    public void LinkedDimensionsKeepTheAspectRatio()
    {
        var vm = Vm(Doc(400, 300), ResizeMode.Image);

        vm.Width = 800;

        Assert.Equal(600, vm.Height);
        output.WriteLine(vm.Summary);
    }

    [Fact]
    public void TheLinkWorksFromEitherField()
    {
        var vm = Vm(Doc(400, 300), ResizeMode.Image);

        vm.Height = 150;

        Assert.Equal(200, vm.Width);
    }

    /// <summary>
    /// Setting one field moves the other and then stops.
    /// </summary>
    /// <remarks>
    /// Each setter drives the other, so without a guard the pair would ping-pong
    /// — and with rounding at each step it would not even converge on the ratio
    /// it started from. The guard is why <c>_linking</c> exists.
    /// </remarks>
    [Fact]
    public void TheLinkDoesNotOscillate()
    {
        var vm = Vm(Doc(400, 300), ResizeMode.Image);

        vm.Width = 401;

        output.WriteLine($"{vm.Width} x {vm.Height}");
        Assert.Equal(401, vm.Width);
        Assert.Equal(301, vm.Height);
    }

    [Fact]
    public void UnlinkedDimensionsMoveIndependently()
    {
        var vm = Vm(Doc(400, 300), ResizeMode.Canvas);

        vm.Width = 900;

        Assert.Equal(300, vm.Height);
    }

    // ---- refusing what cannot be done -----------------------------------------------------

    [Theory]
    [InlineData(0, 300)]
    [InlineData(400, 0)]
    [InlineData(-5, 300)]
    public void ASizeBelowOnePixelCannotBeApplied(int w, int h)
    {
        var vm = Vm(Doc(), ResizeMode.Canvas);
        vm.Linked = false;
        vm.Width = w;
        vm.Height = h;

        output.WriteLine(vm.Summary);
        Assert.False(vm.CanApply);
        Assert.Contains("at least 1 pixel", vm.Summary);
    }

    [Fact]
    public void APpiOfZeroCannotBeApplied()
    {
        var vm = Vm(Doc(), ResizeMode.Image);
        vm.Ppi = 0;

        Assert.False(vm.CanApply);
        Assert.Contains("PPI", vm.Summary);
    }

    /// <summary>PPI alone is a change, and only in image mode.</summary>
    /// <remarks>
    /// It is metadata about how big a pixel prints, so it belongs to the
    /// artwork rather than to the paper — and it is the one way to leave this
    /// dialog having changed something without changing a single coordinate.
    /// </remarks>
    [Fact]
    public void ChangingOnlyThePpiIsStillAChange()
    {
        var vm = Vm(Doc(), ResizeMode.Image);
        Assert.False(vm.CanApply);

        vm.Ppi += 150;

        Assert.True(vm.CanApply);
        output.WriteLine(vm.Summary);
    }

    [Fact]
    public void SwapTradesWidthForHeight()
    {
        var vm = Vm(Doc(400, 300), ResizeMode.Canvas);

        vm.Swap();

        output.WriteLine($"{vm.Width} x {vm.Height}");
        Assert.Equal(300, vm.Width);
        Assert.Equal(400, vm.Height);
        Assert.True(vm.CanApply);
    }

    /// <summary>
    /// Swap steps around the aspect link on purpose.
    /// </summary>
    /// <remarks>
    /// A swap changes the aspect ratio by definition; a linked field that
    /// "corrected" the other half back would turn the button into a no-op.
    /// </remarks>
    [Fact]
    public void SwapIgnoresTheLink()
    {
        var vm = Vm(Doc(400, 300), ResizeMode.Image);
        Assert.True(vm.Linked);

        vm.Swap();

        output.WriteLine($"{vm.Width} x {vm.Height}, linked {vm.Linked}");
        Assert.Equal(300, vm.Width);
        Assert.Equal(400, vm.Height);
    }

    [Fact]
    public void ResetPutsTheFieldsBack()
    {
        var vm = Vm(Doc(400, 300), ResizeMode.Image);
        vm.Width = 1200;
        vm.Ppi = 300;

        vm.Reset();

        Assert.Equal(400, vm.Width);
        Assert.Equal(300, vm.Height);
        Assert.False(vm.CanApply);
    }

    // ---- the two modes, which is the whole design (Q61) ------------------------------------

    /// <summary>
    /// Resizing the paper moves not one coordinate.
    /// </summary>
    /// <remarks>
    /// The origin shifts instead, so the render is bit-identical outside the new
    /// margin. This is the assertion that stops the canvas mode quietly becoming
    /// the image mode — they differ by exactly this.
    /// </remarks>
    [Fact]
    public void ACanvasResizeMovesNothingThatIsDrawn()
    {
        var doc = Doc(400, 300);
        var before = ((Frame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0]
            .Points.Select(p => (p.X, p.Y)).ToList();

        var vm = Vm(doc, ResizeMode.Canvas);
        vm.Width = 600;
        vm.Height = 500;
        vm.SetAnchor(ResizeAnchor.BottomRight);
        Assert.True(vm.Apply(doc, new PixelResampler()));

        var after = ((Frame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0]
            .Points.Select(p => (p.X, p.Y)).ToList();

        output.WriteLine($"origin now ({doc.Scene.Left},{doc.Scene.Top}), size {doc.Scene.Width}x{doc.Scene.Height}");
        Assert.Equal(before, after);
        // Anchored bottom-right, so the paper grew on the left and top and the
        // origin went negative to say so.
        Assert.True(doc.Scene.Left < 0);
        Assert.True(doc.Scene.Top < 0);
    }

    /// <summary>
    /// Rescaling the artwork moves everything, and that is allowed.
    /// </summary>
    /// <remarks>
    /// The artist asked for different art. The mark re-rolls because every dab
    /// dynamic is seeded from a coordinate — Q26 permits that for a line
    /// somebody deliberately moved, which this is.
    /// </remarks>
    [Fact]
    public void AnImageResizeMovesTheGeometryAndTheBrushWithIt()
    {
        var doc = Doc(400, 300);
        var stroke = ((Frame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0];
        var beforeX = stroke.Points[0].X;
        var beforeSize = stroke.Brush.Size;

        var vm = Vm(doc, ResizeMode.Image);
        vm.Width = 800;
        Assert.Equal(600, vm.Height);
        Assert.True(vm.Apply(doc, new PixelResampler()));

        var after = ((Frame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0];
        output.WriteLine($"x {beforeX} -> {after.Points[0].X}, size {beforeSize} -> {after.Brush.Size}");

        Assert.Equal(beforeX * 2, after.Points[0].X, 3);
        Assert.Equal(beforeSize * 2, after.Brush.Size, 3);
        // The paper is bigger and still starts where it did: a rescale writes no
        // origin, which is what keeps that key out of a document that never
        // resized its canvas.
        Assert.Equal(800, doc.Scene.Width);
        Assert.Null(doc.Scene.OriginX);
    }

    /// <summary>
    /// A canvas resize never writes the keys an image resize does, and the other
    /// way round.
    /// </summary>
    /// <remarks>
    /// The two modes are one dialog, so the cheapest way for them to blur is for
    /// one to start doing a little of the other's work. Comparing what each
    /// leaves behind is the test that notices.
    /// </remarks>
    [Fact]
    public void TheTwoModesLeaveDifferentTracks()
    {
        var canvasDoc = Doc(400, 300);
        var canvas = Vm(canvasDoc, ResizeMode.Canvas);
        canvas.Width = 500;
        canvas.Apply(canvasDoc, new PixelResampler());

        var imageDoc = Doc(400, 300);
        var image = Vm(imageDoc, ResizeMode.Image);
        image.Width = 500;
        image.Apply(imageDoc, new PixelResampler());

        // Paper: an origin appears, the drawing does not move.
        Assert.NotNull(canvasDoc.Scene.OriginX);
        // Artwork: no origin, and the drawing moved.
        Assert.Null(imageDoc.Scene.OriginX);

        var canvasX = ((Frame)canvasDoc.Scene.Layers[0].Cels[0].Frame!).Strokes[0].Points[0].X;
        var imageX = ((Frame)imageDoc.Scene.Layers[0].Cels[0].Frame!).Strokes[0].Points[0].X;
        output.WriteLine($"paper kept x at {canvasX}, artwork moved it to {imageX}");
        Assert.Equal(50, canvasX, 3);
        Assert.NotEqual(50, imageX, 3);
    }

    // ---- what the artist is told ----------------------------------------------------------

    /// <summary>
    /// The summary names the one consequence that cannot be undone by eye.
    /// </summary>
    [Fact]
    public void TheSummarySaysWhetherTheMarksWillChange()
    {
        var image = Vm(Doc(), ResizeMode.Image);
        image.Width = 800;
        Assert.Contains("marks change", image.Summary);

        var canvas = Vm(Doc(), ResizeMode.Canvas);
        canvas.Width = 800;
        Assert.Contains("Nothing drawn moves", canvas.Summary);
    }

    [Theory]
    [InlineData(600, 300, "added")]
    [InlineData(200, 150, "cropped")]
    [InlineData(600, 150, "changed")]
    public void TheSummarySaysWhichWayThePaperWent(int w, int h, string expected)
    {
        var vm = Vm(Doc(400, 300), ResizeMode.Canvas);
        vm.Width = w;
        vm.Height = h;

        output.WriteLine(vm.Summary);
        Assert.Contains(expected, vm.Summary);
    }

    [Fact]
    public void TheSummaryNamesTheAnchor()
    {
        var vm = Vm(Doc(), ResizeMode.Canvas);
        vm.Width = 600;

        Assert.Contains("centre", vm.Summary);
        vm.SetAnchor(ResizeAnchor.TopLeft);
        Assert.Contains("top-left", vm.Summary);
    }

    // ---- the window itself, and the registry ----------------------------------------------

    /// <summary>
    /// The dialog opens, in both modes.
    /// </summary>
    /// <remarks>
    /// B163's lesson, applied before it costs anything: the project window
    /// crashed on a static field initialiser that 38 view-model tests could not
    /// reach, because none of them ever built the window. Everything above this
    /// line has the same blind spot, so this is the test that constructs one.
    /// </remarks>
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(ResizeMode.Canvas)]
    [InlineData(ResizeMode.Image)]
    public void TheDialogOpens(ResizeMode mode)
    {
        var doc = Doc();

        var dialog = new Lightbox.App.Views.ResizeDialog(doc.Scene, mode);

        Assert.NotNull(dialog.DataContext);
        Assert.False(dialog.Confirmed);
        Assert.Equal(mode, dialog.Choice.Mode);
        output.WriteLine($"{mode}: \"{dialog.Title}\"");
        Assert.Contains(mode == ResizeMode.Image ? "image" : "canvas", dialog.Title!);
    }

    /// <summary>
    /// Both commands are in <c>ShortcutMap</c>, so an artist can find and rebind
    /// them.
    /// </summary>
    /// <remarks>
    /// A command wired straight to a menu item works perfectly and is invisible
    /// to the whole configuration system — which is the failure the map exists
    /// to prevent, and the one CLAUDE.md puts first in its list for having a
    /// history.
    /// </remarks>
    [Fact]
    public void BothResizeCommandsCanBeRebound()
    {
        var map = new Lightbox.App.Services.ShortcutMap();

        var ids = map.Definitions.Select(d => d.Id).ToList();
        Assert.Contains("image.resizeCanvas", ids);
        Assert.Contains("image.resizeImage", ids);

        foreach (var id in new[] { "image.resizeCanvas", "image.resizeImage" })
        {
            var def = map.Definitions.First(d => d.Id == id);
            output.WriteLine($"{def.Id}: {def.Category} / {def.Name} / {def.Default}");
            Assert.False(string.IsNullOrWhiteSpace(def.Name));
            Assert.NotNull(def.Default);
        }
    }

    /// <summary>Every anchor has a word, so the summary can never go blank.</summary>
    [Fact]
    public void EveryAnchorReadsAsADirection()
    {
        var vm = Vm(Doc(), ResizeMode.Canvas);
        vm.Width = 600;

        foreach (var anchor in Enum.GetValues<ResizeAnchor>())
        {
            vm.SetAnchor(anchor);
            var summary = vm.Summary;
            output.WriteLine($"{anchor}: {summary}");
            Assert.Contains("anchored ", summary);
            Assert.DoesNotContain("anchored .", summary);
        }
    }
}
