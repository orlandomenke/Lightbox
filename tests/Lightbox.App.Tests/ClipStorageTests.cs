using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The storage choice (Q57): a clip is referenced by path, embedded as
/// frames, or embedded whole as production material — and only the last one
/// reaches an exported pixel.
/// </summary>
public class ClipStorageModelTests
{
    [Fact]
    public void TheNewFieldsAreAbsentUntilUsed()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        doc.Scene.Audio = new AudioTrack { Path = "take.wav" };
        doc.Scene.References =
        [
            new ReferenceStrip { Name = "ref", VideoPath = "shot.mp4", SheetWidth = 1, SheetHeight = 1 },
        ];

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("\"data\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("videoData", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmbeddedAudioAndFootageRoundTrip()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        doc.Scene.Audio = new AudioTrack { Path = "take.wav", Data = "QUJD" };
        doc.Scene.References =
        [
            new ReferenceStrip
            {
                Name = "shot", VideoData = "REVG", RendersInExport = true,
                SheetWidth = 4, SheetHeight = 4,
            },
        ];

        var back = DocJson.Deserialize(DocJson.Serialize(doc))!;

        Assert.Equal("QUJD", back.Scene.Audio!.Data);
        var strip = Assert.Single(back.Scene.References!);
        Assert.Equal("REVG", strip.VideoData);
        Assert.True(strip.RendersInExport);
    }
}

[Collection("BrushState")]
public sealed class ClipStorageTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static string WriteWav(string name)
    {
        var sr = 8000;
        var data = new byte[sr * 2];
        for (var i = 0; i < sr; i++)
        {
            var v = i % 2 == 0 ? short.MaxValue : short.MinValue;
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        var path = Path.Combine(Path.GetTempPath(), name);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write("RIFF"u8);
            w.Write(36 + data.Length);
            w.Write("WAVE"u8);
            w.Write("fmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(sr);
            w.Write(sr * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(data.Length);
            w.Write(data);
        }
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    [AvaloniaFact]
    public void EmbeddedAudioSurvivesItsFileGoingAway()
    {
        var (_, vm) = Open();
        var wav = WriteWav("lightbox-embed-audio.wav");

        Assert.Null(vm.ImportAudio(wav, embed: true));
        File.Delete(wav);   // the file is gone; the document does not care

        Assert.NotNull(vm.Doc.Scene.Audio!.Data);
        // The shape a fresh load has: only the record, no caches.
        vm.Doc.Scene.Audio = new AudioTrack
        {
            Path = "long-gone.wav",
            Data = vm.Doc.Scene.Audio.Data,
        };
        Assert.False(vm.AudioMissing);
        Assert.NotNull(vm.TimelineAudioPeaks);
        Assert.True(vm.TimelineAudioPeaks![0].Max > 0.9f);
    }

    [AvaloniaFact]
    public void ReferencedAudioStaysLight()
    {
        var (_, vm) = Open();
        var wav = WriteWav("lightbox-ref-audio.wav");
        try
        {
            Assert.Null(vm.ImportAudio(wav, embed: false));
            Assert.Null(vm.Doc.Scene.Audio!.Data);
            Assert.DoesNotContain("\"data\"",
                DocJson.Serialize(vm.Doc), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(wav);
        }
    }

    [AvaloniaFact]
    public void ProductionFootageCompositesIntoTheExportAndAReferenceDoesNot()
    {
        var (_, vm) = Open();
        // A solid red 8x8 sheet with one full-window cell at frame 0.
        var sheet = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(SKColors.Red);
        }
        var strip = new ReferenceStrip
        {
            Name = "shot",
            SheetWidth = 8,
            SheetHeight = 8,
            Cells = [new ReferenceCell { X = 0, Y = 0, Width = 8, Height = 8 }],
            Opacity = 1.0,
            Scale = 1.0,
        };
        strip.Assign(0, 0);
        vm.Doc.Scene.References = [strip];
        Lightbox.Raster.ReferenceStripRegistry.Register(strip.Id, sheet);

        using var cache = new FrameBitmapCache();

        SKColor At00(SKImage image)
        {
            using var raster = SKBitmap.FromImage(image);
            return raster.GetPixel(0, 0);
        }

        // As a reference: the export must not carry a trace of it — the
        // pixel stays the white paper (red footage would kill the green).
        strip.RendersInExport = false;
        using (var image = SequenceExporter.RenderFrame(vm.Doc, cache, 0))
        {
            Assert.True(At00(image).Green > 200, $"a reference leaked into the export ({At00(image)})");
        }

        // As production material: the footage is under the paint.
        strip.RendersInExport = true;
        using (var image = SequenceExporter.RenderFrame(vm.Doc, cache, 0))
        {
            var pixel = At00(image);
            Assert.True(pixel.Red > 200 && pixel.Green < 60,
                $"production footage did not composite (pixel {pixel})");
        }
    }

    [AvaloniaFact]
    public async Task TheThreeVideoModesStoreWhatTheySay()
    {
        if (VideoExporter.FindFfmpeg() is not { } ffmpeg) return;

        var clip = Path.Combine(Path.GetTempPath(), "lightbox-storage-clip.mp4");
        var make = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-y -f lavfi -i testsrc=duration=1:size=64x48:rate=12 \"{clip}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        make.StandardError.ReadToEnd();
        make.WaitForExit();

        try
        {
            var (_, vm) = Open();
            Assert.Null(await vm.ImportVideoReference(clip, ClipStorage.ReferenceByPath));
            Assert.Null(await vm.ImportVideoReference(clip, ClipStorage.ReferenceEmbedded));
            Assert.Null(await vm.ImportVideoReference(clip, ClipStorage.Production));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var strips = vm.Doc.Scene.References!;
            Assert.Equal(3, strips.Count);

            var byPath = strips[0];
            Assert.NotNull(byPath.VideoPath);
            Assert.Equal("", byPath.Png);
            Assert.Null(byPath.VideoData);
            Assert.False(byPath.RendersInExport);

            var embedded = strips[1];
            Assert.Null(embedded.VideoPath);
            Assert.NotEqual("", embedded.Png);
            Assert.Null(embedded.VideoData);
            Assert.False(embedded.RendersInExport);

            var production = strips[2];
            Assert.Null(production.VideoPath);
            Assert.Equal("", production.Png);
            Assert.NotNull(production.VideoData);
            Assert.True(production.RendersInExport);
            Assert.Equal(1.0, production.Opacity, 3);
        }
        finally
        {
            File.Delete(clip);
        }
    }
}
