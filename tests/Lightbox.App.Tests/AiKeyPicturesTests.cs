using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Lightbox.Ai;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The keys go to the model as pictures as well as strokes (2026-10-14): the
/// layer's own drawing at each key, cropped to where either key has ink, and
/// the whole visible scene at each key for context. Without them a model pairs
/// stroke 5 with stroke 5, which is the wrong line whenever the keys were drawn
/// with different strokes.
/// </summary>
public class AiKeyPicturesTests(ITestOutputHelper output)
{
    private static Stroke Seg(double y) => new()
    {
        Points = [new(10, y, 0.5), new(30, y, 0.5)],
        Brush = new BrushSettings { Size = 6, Hardness = 1 },
    };

    private static AiResult<List<InbetweenFrameResult>> Answer(params (double T, double Y)[] frames) =>
        AiResult<List<InbetweenFrameResult>>.Success(
            frames.Select(f => new InbetweenFrameResult(f.T, [Seg(f.Y)])).ToList());

    /// <summary>Keys at frames 1 and 2: a line across x 10–30, at y 10 and then y 60.</summary>
    private static MainViewModel VmWithTwoKeys(FakeArtist artist, string? note = null)
    {
        var vm = new MainViewModel(artist) { AskDirection = _ => Task.FromResult(note) };
        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(30, 10, 0.5);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(10, 60, 0.5);
        vm.MoveStroke(30, 60, 0.5);
        vm.EndStroke();
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        return vm;
    }

    private static SKBitmap Decode(KeyPicture picture) =>
        SKBitmap.Decode(Convert.FromBase64String(picture.PngBase64));

    /// <summary>The ink-weighted middle of a picture's dark pixels, in picture pixels.</summary>
    private static (double X, double Y, int Count) Ink(SKBitmap bitmap)
    {
        double sx = 0, sy = 0;
        var n = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y).Red > 128) continue;
            sx += x;
            sy += y;
            n++;
        }
        return n == 0 ? (double.NaN, double.NaN, 0) : (sx / n, sy / n, n);
    }

    [AvaloniaFact]
    public async Task AnInbetweenRequestCarriesBothKeysAloneAndTheWholeSceneAtEach()
    {
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist);

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        var pictures = artist.LastInbetweenRequest!.KeyPictures!;
        foreach (var p in pictures) output.WriteLine(p.Caption);
        Assert.Equal(4, pictures.Count);
        Assert.StartsWith("Key A (frame 1), the layer being inbetweened", pictures[0].Caption);
        Assert.StartsWith("Key B (frame 2), the layer being inbetweened", pictures[1].Caption);
        Assert.StartsWith("Key A (frame 1), the whole scene", pictures[2].Caption);
        Assert.StartsWith("Key B (frame 2), the whole scene", pictures[3].Caption);
        foreach (var p in pictures)
        {
            using var bitmap = Decode(p);
            Assert.True(Math.Max(bitmap.Width, bitmap.Height) <= MainViewModel.PictureLongEdge,
                $"{bitmap.Width}x{bitmap.Height} is over the budget");
        }
    }

    [AvaloniaFact]
    public async Task TheCroppedPictureIsBlackOnWhiteAndItsCaptionMapsBackToTheScene()
    {
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist);

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        var pictures = artist.LastInbetweenRequest!.KeyPictures!;
        for (var k = 0; k < 2; k++)
        {
            using var bitmap = Decode(pictures[k]);
            Assert.Equal(SKColors.White, bitmap.GetPixel(0, 0));   // paper, not transparency

            // Follow the caption's own formula from the picture's ink back to
            // the scene: it must land on the stroke the key holds.
            var m = Regex.Match(pictures[k].Caption,
                @"scene pixel \((\d+) \+ px / ([\d.]+), (\d+) \+ py / ([\d.]+)\)");
            Assert.True(m.Success, pictures[k].Caption);
            double D(int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            var (ix, iy, count) = Ink(bitmap);
            var (sx, sy) = (D(1) + ix / D(2), D(3) + iy / D(4));
            output.WriteLine($"key {k}: {count} ink pixels, middle at scene ({sx:0.0}, {sy:0.0})");
            Assert.True(count > 0, "the key's line is in its picture");
            Assert.InRange(sx, 17, 23);
            Assert.InRange(sy, k == 0 ? 7 : 57, k == 0 ? 13 : 63);
        }
    }

    [AvaloniaFact]
    public async Task OtherLayersShowInTheScenePicturesAndNotInTheLayersOwn()
    {
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist);
        var keys = vm.ActiveLayerIndex;
        vm.AddPaintedLayerCommand.Execute(null);
        vm.BeginStroke(400, 300, 0.5);   // far from the keys
        vm.MoveStroke(500, 340, 0.5);
        vm.EndStroke();
        vm.ActiveLayerIndex = keys;
        vm.CurrentFrameIndex = 0;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        var pictures = artist.LastInbetweenRequest!.KeyPictures!;
        // The crop is the keys' own ink plus a margin: the other layer is not in it.
        var box = Regex.Match(pictures[0].Caption, @"scene x (\d+)–(\d+), y (\d+)–(\d+)");
        Assert.True(box.Success, pictures[0].Caption);
        Assert.InRange(int.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture), 30, 100);
        Assert.InRange(int.Parse(box.Groups[4].Value, CultureInfo.InvariantCulture), 60, 120);
        using var scene = Decode(pictures[2]);
        var scale = (double)scene.Width / vm.Doc.Scene.Width;
        Assert.True(scene.GetPixel((int)(450 * scale), (int)(320 * scale)).Alpha > 0
            && scene.GetPixel((int)(450 * scale), (int)(320 * scale)).Red < 200,
            "the other layer's line is in the scene picture");
    }

    [AvaloniaFact]
    public async Task ARevisionIsShownTheKeysToo()
    {
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist, "Tighter arc.");
        await vm.AiInbetweenCommand.ExecuteAsync(null);
        artist.InbetweenResult = Answer((0.5, 33));
        vm.CurrentFrameIndex = 1;

        await vm.AiReviseCommand.ExecuteAsync(null);

        var request = artist.LastInbetweenRequest!;
        Assert.Equal("Tighter arc.", request.Direction);
        Assert.Equal(4, request.KeyPictures!.Count);
        Assert.StartsWith("Key B (frame 3)", request.KeyPictures[1].Caption);
    }

    [AvaloniaFact]
    public async Task KeysDrawnWithDifferentStrokesStillGetTheirInbetweenAndTheStatusSaysHowItWasChecked()
    {
        // Key B draws the same line as key A, but as two strokes, the second
        // running the other way: what a hand, or a trace, does.
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = new MainViewModel(artist);
        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(30, 10, 0.5);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(10, 60, 0.5);
        vm.MoveStroke(20, 60, 0.5);
        vm.EndStroke();
        vm.BeginStroke(30, 60, 0.5);
        vm.MoveStroke(20, 60, 0.5);
        vm.EndStroke();
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        output.WriteLine(vm.AiStatus);
        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        Assert.Contains("checked by shape", vm.AiStatus);
    }

    [AvaloniaFact]
    public async Task KeysDrawnStrokeForStrokeSayNothingAboutIt()
    {
        var artist = new FakeArtist { InbetweenResult = Answer((0.5, 35)) };
        var vm = VmWithTwoKeys(artist);

        await vm.AiInbetweenCommand.ExecuteAsync(null);

        Assert.DoesNotContain("by shape", vm.AiStatus);
    }
}
