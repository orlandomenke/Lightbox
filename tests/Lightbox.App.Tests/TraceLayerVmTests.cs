using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Q269 from the artist's side: a layer of pixels traced onto a new layer of
/// strokes above, the pixel layer hidden, in one undo step. What the tracer
/// makes is <c>Lightbox.Raster.Tests.LineTracerTests</c>' subject.
/// </summary>
public class TraceLayerVmTests
{
    private static MainViewModel WithPixels(out Layer flat)
    {
        var vm = VmLayers.BareVm();
        flat = vm.PaintLayer();
        using var bitmap = new SKBitmap(new SKImageInfo(vm.Doc.Scene.Width, vm.Doc.Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        using (var ink = new SKPaint { Color = SKColors.Black, StrokeWidth = 6, IsAntialias = true, Style = SKPaintStyle.Stroke })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawLine(40, 120, 300, 120, ink);
            canvas.DrawLine(170, 40, 170, 260, ink);
        }
        flat.Cels[0].Frame = new Frame { PngBase64 = PngCodec.Encode(bitmap) };
        return vm;
    }

    [AvaloniaFact]
    public void TracingALayerPutsStrokesAboveAndHidesThePixels()
    {
        var vm = WithPixels(out var flat);
        var count = vm.Doc.Scene.Layers.Count;

        Assert.Equal(1, vm.DrawingsToTrace(null));
        using (var sample = vm.TraceSampleFor(null)) Assert.NotNull(sample);
        Assert.True(vm.TraceLayer(null, new LineTraceOptions()));

        Assert.Equal(count + 1, vm.Doc.Scene.Layers.Count);
        var traced = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        Assert.Equal($"{flat.Name} (traced)", traced.Name);
        Assert.False(flat.Visible);
        Assert.Equal(2, traced.Cels[0].Frame!.Strokes.Count);   // a crossing is two lines
        Assert.Contains("hidden", vm.AiStatus);
    }

    [AvaloniaFact]
    public void TracingIsOneUndoStep()
    {
        var vm = WithPixels(out _);
        var before = Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc);

        vm.TraceLayer(null, new LineTraceOptions());
        vm.UndoCommand.Execute(null);

        Assert.Equal(before, Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc));
    }

    [AvaloniaFact]
    public void ALayerOfStrokesHasNothingToTraceAndSaysSo()
    {
        var vm = VmLayers.BareVm();
        vm.BeginStroke(40, 40, 1);
        vm.EndStroke();
        var count = vm.Doc.Scene.Layers.Count;

        Assert.Equal(0, vm.DrawingsToTrace(null));
        Assert.False(vm.TraceLayer(null, new LineTraceOptions()));

        Assert.Equal(count, vm.Doc.Scene.Layers.Count);
        Assert.Contains("no pixels", vm.AiStatus);
    }

    [AvaloniaFact]
    public void TracingIsSomethingAShortcutCanReach()
    {
        Assert.Contains(new Lightbox.App.Services.ShortcutMap().Definitions, c => c.Id == "docker.traceLayer");
    }
}
