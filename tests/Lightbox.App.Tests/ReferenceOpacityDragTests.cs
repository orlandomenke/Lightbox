using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B191: dragging a reference dial (opacity, scale, visibility) on a
/// painted-on document. Each slider tick used to run the full document-edit
/// funnel — re-baking every all-layers-live stroke (a full-canvas compose and
/// a PNG encode per stroke) and publishing directly per tick, with the frame
/// replayed from its whole stroke record each time because a live frame is
/// never cached. A few minutes of smudge painting made one tick cost hundreds
/// of milliseconds, a drag queue minutes of UI-thread work, and the
/// allocation storm behind it took the process down with no managed crash.
/// </summary>
/// <remarks>
/// A reference strip is not a layer: nothing derived from the artwork — live
/// bakes, linked-strip flattens, reference-view PNGs — can see its dials. So
/// the dial path owes a repaint (coalesced, through <c>RequestSnapshot</c>)
/// and the unsaved-changes bookkeeping, and nothing else. These tests pin
/// both halves.
/// </remarks>
[Collection("BrushState")]
public class ReferenceOpacityDragTests : BrushStateIsolated
{
    /// <summary>An opaque "photo" with enough content for the slicer to find.</summary>
    private static string Sheet()
    {
        using var bmp = new SKBitmap(160, 120, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(new SKColor(180, 160, 140));
            using var paint = new SKPaint { Color = new SKColor(40, 60, 90) };
            for (var i = 0; i < 6; i++)
            {
                canvas.DrawRect(SKRect.Create(10 + i * 24, 15 + (i % 3) * 30, 18, 22), paint);
            }
        }
        return PngCodec.Encode(bmp);
    }

    private static Stroke Paint(double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#204080",
        Points = [new StrokePoint(30, y, 1), new StrokePoint(360, y, 1)],
        Brush = new BrushSettings { Size = 20, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.15 },
    };

    private static Stroke LiveSmudge(double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = [new StrokePoint(40, y, 1), new StrokePoint(350, y, 1)],
        Brush = new BrushSettings
        {
            Kind = BrushKind.Smudge, Size = 22, Flow = 1, Spacing = 0.15,
            SmudgeLength = 0.95, ColorRate = 0, SampleSource = SampleSource.AllLayersLive,
        },
    };

    /// <summary>
    /// The reported scene: a reference imported, then painted over — some
    /// ordinary strokes, some all-layers-live smudges.
    /// </summary>
    private static MainViewModel Painted(out Frame frame)
    {
        var vm = VmLayers.PaperVm();
        var scene = vm.Doc.Scene;

        var top = new Layer { Name = "Top", Kind = LayerKind.Painted };
        var painted = new Frame();
        top.Cels.Add(new Cel { Frame = painted });
        for (var i = 1; i < scene.Layers[^1].Cels.Count; i++) top.Cels.Add(new Cel());
        scene.Layers.Add(top);
        vm.ActiveLayerIndex = scene.Layers.Count - 1;

        Assert.NotNull(vm.ImportReference("Photo", Sheet()));
        Assert.NotNull(vm.ActiveReference);

        for (var i = 0; i < 4; i++) painted.Strokes.Add(Paint(20 + i * 6));
        for (var i = 0; i < 3; i++) painted.Strokes.Add(LiveSmudge(25 + i * 6));
        frame = painted;
        return vm;
    }

    private static void Drag(MainViewModel vm, int ticks = 20)
    {
        for (var i = 0; i < ticks; i++)
        {
            vm.ReferenceOpacity = 0.30 + i * 0.02;
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ADragTickDoesNotRebakeLiveStrokes()
    {
        var vm = Painted(out var frame);
        vm.MarkDocumentEditedForTests(); // one real funnel pass, so the bakes exist
        var live = frame.Strokes.Where(s => s.Brush.SampleSource == SampleSource.AllLayersLive).ToList();
        Assert.NotEmpty(live);
        var baked = live.Select(s => s.Baked).ToList();
        Assert.All(baked, Assert.NotNull);

        Drag(vm);

        // The bake is derived from the layers beneath the stroke, and a
        // reference is not a layer — so a dial that replaces it is spending a
        // full-canvas compose and a PNG encode per stroke per tick on
        // reproducing it, which is B191's freeze.
        for (var i = 0; i < live.Count; i++)
        {
            Assert.Same(baked[i], live[i].Baked);
        }
    }

    [AvaloniaFact]
    public void ADragCoalescesIntoOnePublishInsteadOfOnePerTick()
    {
        var vm = Painted(out _);
        var publishes = 0;
        vm.SnapshotChanged += _ => publishes++;

        Drag(vm);

        // Twenty ticks before the dispatcher runs again is one repaint's worth
        // of change. Publishing each of them replays the whole frame each time
        // when a live stroke is on it — a live frame is never cached — which is
        // the other half of the freeze.
        Assert.Equal(1, publishes);
    }

    [AvaloniaFact]
    public void TheDialStillLandsAndStillShows()
    {
        var vm = Painted(out _);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        Drag(vm);

        Assert.Equal(0.68, vm.ReferenceOpacity, 3);
        Assert.Equal(0.68, vm.ActiveReference!.Opacity, 3);
        // The coalesced publish actually reached the canvas.
        Assert.NotNull(latest);
    }
}
