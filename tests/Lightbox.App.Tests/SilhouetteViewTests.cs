using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The silhouette view (Q135): the classic pose-reading check — the ink as
/// solid black on white, paper, references and ghosts left out. View-only,
/// the channel solos' side of the line: the record never hears about it.
/// The load-bearing half is the COMPOSITE — the paper is baked in there, so
/// no filter alone could produce this view (the scout's finding), and the
/// published snapshot itself must lose the background layer.
/// </summary>
[Collection("BrushState")]
public sealed class SilhouetteViewTests(ITestOutputHelper output) : BrushStateIsolated
{
    // ---- the filter, at the pixel ------------------------------------------

    [AvaloniaFact]
    public void SilhouetteInkIsBlackWhateverTheColour()
    {
        using var bmp = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { ColorFilter = ChannelSoloFilters.For(ChannelSolo.Silhouette) };
        paint.Color = new SKColor(200, 80, 30, 255);
        canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);
        canvas.Flush();

        var got = bmp.GetPixel(0, 0);
        output.WriteLine($"opaque orange reads {got}");
        Assert.Equal(0, got.Red);
        Assert.Equal(0, got.Green);
        Assert.Equal(0, got.Blue);
        Assert.Equal(255, got.Alpha);
    }

    [AvaloniaFact]
    public void CoverageSurvivesTheFlattening()
    {
        // A soft edge stays a soft edge — the silhouette keeps alpha, or the
        // antialiasing on every line would turn to staircases.
        using var bmp = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { ColorFilter = ChannelSoloFilters.For(ChannelSolo.Silhouette) };
        paint.Color = new SKColor(200, 80, 30, 128);
        canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);
        canvas.Flush();

        var got = bmp.GetPixel(0, 0);
        output.WriteLine($"half-alpha orange reads {got}");
        Assert.InRange(got.Alpha, 120, 136);
    }

    // ---- the composite -----------------------------------------------------

    private static Layer LayerWith(string name, int cels, bool background = false)
    {
        var layer = new Layer { Name = name, IsBackground = background };
        for (var i = 0; i < cels; i++) layer.Cels.Add(new Cel { Frame = new Frame() });
        return layer;
    }

    [AvaloniaFact]
    public void TheSilhouetteComposesWithoutThePaperOrTheGhosts()
    {
        var paper = LayerWith("Paper", 3, background: true);
        var art = LayerWith("Art", 3);
        var scene = new Scene { Width = 64, Height = 48, FrameCount = 3 };
        scene.Layers.AddRange([paper, art]);
        var onion = new OnionSettings { Enabled = true, Before = 1, After = 1 };

        using var cache = new FrameBitmapCache();
        var plain = ScenePassBuilder.Build(
            scene,
            new ScenePassBuilder.State(1, art.Id, false, false, false, onion),
            cache, new TileFallbackTally(), ScenePassBuilder.LiveEdit.None);
        var silhouette = ScenePassBuilder.Build(
            scene,
            new ScenePassBuilder.State(1, art.Id, false, false, false, onion, Silhouette: true),
            cache, new TileFallbackTally(), ScenePassBuilder.LiveEdit.None);

        output.WriteLine($"plain {plain.Passes.Count} passes, silhouette {silhouette.Passes.Count}");
        // Plain: paper + two ghosts + the drawing. Silhouette: the drawing.
        Assert.True(plain.Passes.Count > silhouette.Passes.Count,
            "the silhouette composite did not get smaller — nothing was excluded");
        Assert.Single(silhouette.Passes);
        Assert.DoesNotContain(silhouette.Passes, p => p.Tint is not null);
    }

    // ---- the toggle, end to end ---------------------------------------------

    [AvaloniaFact]
    public void TogglingSilhouetteRecompositesThePaperAwayAndBack()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false, ColorHex = "#000000", BrushSize = 12 };
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(200, 100, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using (var bmp = SKBitmap.FromImage(latest!.Image))
        {
            var paper = bmp.GetPixel(30, 200);
            output.WriteLine($"plain paper pixel {paper}");
            Assert.True(paper.Alpha == 255, "the paper should be in the plain composite");
        }

        vm.ChannelSolo = ChannelSolo.Silhouette;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsSilhouette);
        using (var bmp = SKBitmap.FromImage(latest!.Image))
        {
            var paper = bmp.GetPixel(30, 200);
            var ink = bmp.GetPixel(150, 100);
            output.WriteLine($"silhouette paper pixel {paper}, ink pixel {ink}");
            // Both numbers, the measuring rule: the paper is GONE from the
            // composite (the white comes from the canvas at draw time), and
            // the ink is still there for the filter to flatten.
            Assert.Equal(0, paper.Alpha);
            Assert.True(ink.Alpha > 200, "the ink left the composite with the paper");
        }

        vm.ChannelSolo = ChannelSolo.None;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using (var bmp = SKBitmap.FromImage(latest!.Image))
        {
            Assert.Equal(255, bmp.GetPixel(30, 200).Alpha);
        }
    }

    [AvaloniaFact]
    public void TheToggleIsRegisteredSoItCanBeFoundAndRebound()
    {
        var map = new Services.ShortcutMap { StorePathOverride = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"silhouette-shortcuts-{Guid.NewGuid():N}.json") };
        Assert.Contains(map.Definitions, d => d.Id == "canvas.silhouette");
    }
}
