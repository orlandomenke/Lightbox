using System.Text.Json;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// <c>File ▸ Export image sequence…</c> — a run of numbered pictures, with a
/// range, a step, a format and the option of writing a held drawing once (Q220).
/// </summary>
/// <remarks>
/// <para>
/// Every drawing here is a square at a different x, so "which frame is this
/// file" is answered by reading one row of pixels rather than by trusting a
/// file name the code under test chose.
/// </para>
/// <para>
/// The first test is the one that makes the rest safe: with nothing changed,
/// this writes byte for byte what <c>Export PNGs…</c> always wrote. Everything
/// after it is an option, and an option nobody touched must not move a pixel.
/// </para>
/// </remarks>
public class ImageSequenceExportTests(Xunit.ITestOutputHelper output) : IDisposable
{
    private const int Size = 16;

    private readonly string _dir =
        Directory.CreateTempSubdirectory("lightbox-image-sequence").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Dir(string name) => Path.Combine(_dir, name);

    /// <summary>A 2×2 opaque square whose left edge is at <paramref name="x"/>.</summary>
    private static Frame SquareAt(int x, string colour = "#ff0000") => new()
    {
        Strokes =
        [
            new Stroke
            {
                Tool = ToolKind.Fill,
                Color = colour,
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
                Points =
                [
                    new StrokePoint(x, 0, 1),
                    new StrokePoint(x + 2, 0, 1),
                    new StrokePoint(x + 2, 2, 1),
                    new StrokePoint(x, 2, 1),
                ],
            },
        ],
    };

    /// <summary>
    /// One layer over transparent paper. Each entry is the x of that frame's
    /// drawing, or null for a hold of the drawing before it.
    /// </summary>
    private static Doc Timeline(params int?[] cels)
    {
        var doc = DocumentFactory.CreateDoc(Size, Size, fps: 12);
        doc.Scene.TransparentBackground = true;
        doc.Scene.FrameCount = cels.Length;
        var layer = doc.Scene.Layers[^1];
        layer.Cels.Clear();
        foreach (var x in cels)
        {
            layer.Cels.Add(x is { } at ? new Cel { Frame = SquareAt(at) } : new Cel());
        }
        return doc;
    }

    /// <summary>Six frames, six different drawings: x = 0, 2, 4, 6, 8, 10.</summary>
    private static Doc SixDrawings() => Timeline(0, 2, 4, 6, 8, 10);

    /// <summary>The x of the square in a written file, or -1 when there is none.</summary>
    private static int SquareIn(string path)
    {
        using var bitmap = SKBitmap.Decode(path);
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, 0).Alpha > 127) return x;
        }
        return -1;
    }

    private static string[] Names(ImageSequenceResult result) =>
        result.Paths.Select(p => Path.GetFileName(p)!).ToArray();

    // ---- an option nobody touched moves nothing --------------------------------

    /// <summary>
    /// Documents that take different branches of the render: bare transparent
    /// paper, a real paper layer (the branch the paper option sits on), and a
    /// camera, whose output size is not the canvas's.
    /// </summary>
    private Doc Fixture(string kind)
    {
        switch (kind)
        {
            case "paper":
                return OnPaper(0, 2, null, 6);
            case "camera":
                var shot = SixDrawings();
                shot.Scene.Camera = new Camera { OutputWidth = 12, OutputHeight = 8 };
                return shot;
            default:
                return SixDrawings();
        }
    }

    [Theory]
    [InlineData("transparent")]
    [InlineData("paper")]
    [InlineData("camera")]
    public void TheDefaultsWriteByteForByteWhatExportPngsAlwaysWrote(string kind)
    {
        // ExportPngSequence is untouched by this change, so it is the "before"
        // for everything this exporter adds around the render. It cannot be
        // the "before" for RenderFrame itself, which both call — the paper
        // tests further down hold that, and were checked by breaking it.
        var doc = Fixture(kind);

        var before = SequenceExporter.ExportPngSequence(doc, Dir("before-" + kind));
        var after = ImageSequenceExporter.Export(doc, Dir("after-" + kind));

        Assert.NotEmpty(before);
        Assert.Equal(before.Select(Path.GetFileName), after.Paths.Select(Path.GetFileName));
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(File.ReadAllBytes(before[i]), File.ReadAllBytes(after.Paths[i]));
        }
        Assert.Equal("frame_0001.png", Names(after)[0]);
        Assert.False(after.LostTransparency);
    }

    [Fact]
    public void ATimelineWithNoFramesWritesNothingAsItAlwaysDid()
    {
        var doc = SixDrawings();
        doc.Scene.FrameCount = 0;

        var before = SequenceExporter.ExportPngSequence(doc, Dir("none-before"));
        var after = ImageSequenceExporter.Export(doc, Dir("none-after"));

        Assert.Empty(before);
        Assert.Empty(after.Paths);
        Assert.Empty(after.Frames);
    }

    [Fact]
    public void EveryFrameWritesNoTimingFile()
    {
        // Absent unless used: a run with every frame in it has nothing to say
        // about holds, so there is no file saying nothing.
        var result = ImageSequenceExporter.Export(Timeline(0, null, 4), Dir("all"));

        Assert.Null(result.TimingPath);
        Assert.Equal(3, result.Paths.Count);
        Assert.Empty(Directory.GetFiles(Dir("all"), "*.json"));
    }

    // ---- range -----------------------------------------------------------------

    [Fact]
    public void ARangeWritesOnlyTheFramesInsideIt()
    {
        var result = ImageSequenceExporter.Export(
            SixDrawings(), Dir("range"), new ImageSequenceSettings(FromFrame: 2, ToFrame: 4));

        output.WriteLine(string.Join(", ", Names(result)));
        Assert.Equal([4, 6, 8], result.Paths.Select(SquareIn).ToArray());
        // Counted from the start number, not from where the range sits: a run
        // of three files is 1, 2, 3 unless timeline numbers are asked for.
        Assert.Equal(["frame_0001.png", "frame_0002.png", "frame_0003.png"], Names(result));
        Assert.Equal(3, Directory.GetFiles(Dir("range")).Length);
    }

    [Theory]
    [InlineData(4, 2, new[] { 4, 6, 8 })] // typed backwards
    [InlineData(-5, 1, new[] { 0, 2 })] // before the start
    [InlineData(4, 99, new[] { 8, 10 })] // past the end
    [InlineData(99, 99, new[] { 10 })] // wholly past the end: the last frame, not nothing
    public void ARangeThatDoesNotFitIsClampedRatherThanRefused(int from, int to, int[] expected)
    {
        var result = ImageSequenceExporter.Export(
            SixDrawings(), Dir($"clamp{from}_{to}"), new ImageSequenceSettings(FromFrame: from, ToFrame: to));

        Assert.Equal(expected, result.Paths.Select(SquareIn).ToArray());
    }

    // ---- step ------------------------------------------------------------------

    [Fact]
    public void AStepWritesEveryNthFrameOfTheRange()
    {
        var result = ImageSequenceExporter.Export(
            SixDrawings(), Dir("step"), new ImageSequenceSettings(FromFrame: 1, Step: 2));

        Assert.Equal([2, 6, 10], result.Paths.Select(SquareIn).ToArray());
        Assert.Equal(["frame_0001.png", "frame_0002.png", "frame_0003.png"], Names(result));
    }

    [Fact]
    public void AStepLongerThanTheRangeWritesItsFirstFrameAndStops()
    {
        // int.MaxValue on purpose: added to a frame number it wraps negative,
        // and a loop that tests "i <= to" then never ends.
        var settings = new ImageSequenceSettings(FromFrame: 2, Step: int.MaxValue);

        Assert.Equal([2], settings.Frames(SixDrawings().Scene));

        var result = ImageSequenceExporter.Export(SixDrawings(), Dir("hugestep"), settings);
        Assert.Equal([4], result.Paths.Select(SquareIn).ToArray());
        Assert.Equal([4], result.Frames.Select(f => f.Hold).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AStepOfNothingIsAStepOfOne(int step)
    {
        // A step of zero is an endless loop over frame one; it must be a
        // sequence, not a hang.
        var result = ImageSequenceExporter.Export(
            SixDrawings(), Dir($"step{step}"), new ImageSequenceSettings(Step: step));

        Assert.Equal(6, result.Paths.Count);
    }

    // ---- unique frames ---------------------------------------------------------

    [Fact]
    public void UniqueFramesWritesAHeldDrawingOnceAndCountsFromOne()
    {
        // Three drawings held for 3, 2 and 1 frames.
        var doc = Timeline(0, null, null, 6, null, 10);

        var result = ImageSequenceExporter.Export(
            doc, Dir("unique"), new ImageSequenceSettings(UniqueOnly: true));

        output.WriteLine(string.Join(", ", result.Frames.Select(f => $"{f.File}@{f.FrameIndex}x{f.Hold}")));
        Assert.Equal([0, 6, 10], result.Paths.Select(SquareIn).ToArray());
        // Q220: an unbroken run by default, because that is what an engine or
        // a comp package expects to be handed.
        Assert.Equal(["frame_0001.png", "frame_0002.png", "frame_0003.png"], Names(result));
        Assert.Equal([0, 3, 5], result.Frames.Select(f => f.FrameIndex).ToArray());
        Assert.Equal([3, 2, 1], result.Frames.Select(f => f.Hold).ToArray());
    }

    [Fact]
    public void TimelineNumbersKeepsEachFilesFrameNumberAndLeavesTheGaps()
    {
        var doc = Timeline(0, null, null, 6, null, 10);

        var result = ImageSequenceExporter.Export(
            doc, Dir("gaps"), new ImageSequenceSettings(UniqueOnly: true, TimelineNumbers: true));

        Assert.Equal(["frame_0001.png", "frame_0004.png", "frame_0006.png"], Names(result));
        Assert.Equal([0, 6, 10], result.Paths.Select(SquareIn).ToArray());
    }

    [Fact]
    public void TimelineNumbersOnARangeNamesFilesByWhereTheySitNotByTheirOrder()
    {
        // Not only for unique frames: an excerpt re-exported under timeline
        // numbers overwrites the same files the full export wrote.
        var result = ImageSequenceExporter.Export(
            SixDrawings(), Dir("excerpt"),
            new ImageSequenceSettings(FromFrame: 3, ToFrame: 4, TimelineNumbers: true));

        Assert.Equal(["frame_0004.png", "frame_0005.png"], Names(result));
    }

    [Fact]
    public void UniqueFramesWritesATimingFileThatSaysHowLongEachOneHolds()
    {
        var doc = Timeline(0, null, null, 6, null, 10);

        var result = ImageSequenceExporter.Export(
            doc, Dir("timing"), new ImageSequenceSettings(UniqueOnly: true));

        Assert.NotNull(result.TimingPath);
        Assert.Equal("frame_timing.json", Path.GetFileName(result.TimingPath));
        // Not among the pictures: a caller that globs Paths gets images only.
        Assert.DoesNotContain(result.TimingPath, result.Paths);

        var json = File.ReadAllText(result.TimingPath!);
        output.WriteLine(json);
        using var timing = JsonDocument.Parse(json);
        var root = timing.RootElement;
        Assert.Equal(12, root.GetProperty("fps").GetInt32());
        // One-based, like the file names and like the timeline the artist reads.
        Assert.Equal(1, root.GetProperty("from").GetInt32());
        Assert.Equal(6, root.GetProperty("to").GetInt32());
        var frames = root.GetProperty("frames").EnumerateArray().ToArray();
        Assert.Equal(3, frames.Length);
        Assert.Equal("frame_0002.png", frames[1].GetProperty("file").GetString());
        Assert.Equal(4, frames[1].GetProperty("frame").GetInt32());
        Assert.Equal(2, frames[1].GetProperty("hold").GetInt32());
        // The holds account for every frame of the range, which is the one
        // thing the files alone cannot say about the last drawing.
        Assert.Equal(6, frames.Sum(f => f.GetProperty("hold").GetInt32()));
    }

    [Fact]
    public void TheLastDrawingHoldsToTheEndOfTheRangeNotTheEndOfTheTimeline()
    {
        var doc = Timeline(0, null, null, 6, null, null);

        var result = ImageSequenceExporter.Export(
            doc, Dir("lasthold"), new ImageSequenceSettings(ToFrame: 4, UniqueOnly: true));

        Assert.Equal([3, 2], result.Frames.Select(f => f.Hold).ToArray());
    }

    [Fact]
    public void AHoldIsCountedInTimelineFramesEvenWhenStepping()
    {
        // Step 2 over 0,2,4 of: A A A B B C. Samples are A, A, B — two files —
        // and A covers four timeline frames, not two samples.
        var doc = Timeline(0, null, null, 6, null, 10);

        var result = ImageSequenceExporter.Export(
            doc, Dir("stephold"), new ImageSequenceSettings(Step: 2, UniqueOnly: true));

        Assert.Equal([0, 6], result.Paths.Select(SquareIn).ToArray());
        Assert.Equal([4, 2], result.Frames.Select(f => f.Hold).ToArray());
    }

    [Fact]
    public void UniqueIsJudgedOnThePictureNotOnOneLayersHolds()
    {
        // The top layer holds one drawing throughout; the layer beneath it
        // changes every frame. Reading holds off a layer would write one file.
        var doc = Timeline(0, null, null);
        doc.Scene.Layers.Insert(0, new Layer
        {
            Name = "Under",
            Kind = LayerKind.Painted,
            Cels =
            [
                new Cel { Frame = SquareAt(4, "#0000ff") },
                new Cel { Frame = SquareAt(8, "#0000ff") },
                new Cel { Frame = SquareAt(12, "#0000ff") },
            ],
        });

        var result = ImageSequenceExporter.Export(
            doc, Dir("layers"), new ImageSequenceSettings(UniqueOnly: true));

        Assert.Equal(3, result.Paths.Count);
        Assert.Equal([1, 1, 1], result.Frames.Select(f => f.Hold).ToArray());
    }

    [Fact]
    public void TwoSeparateDrawingsThatLookTheSameAreOnePicture()
    {
        // Keyed twice with identical strokes — a duplicated cel. The record
        // says two drawings; the eye and the file say one.
        var doc = Timeline(4, 4, 8);

        var result = ImageSequenceExporter.Export(
            doc, Dir("twins"), new ImageSequenceSettings(UniqueOnly: true));

        Assert.Equal([4, 8], result.Paths.Select(SquareIn).ToArray());
        Assert.Equal([2, 1], result.Frames.Select(f => f.Hold).ToArray());
    }

    [Fact]
    public void ADrawingThatComesBackLaterIsWrittenAgain()
    {
        // A B A. "Unique" collapses a hold — a run of the same picture — and
        // nothing else, so the files stay in the order they play and a reader
        // that knows nothing of the timing file still gets the animation.
        var doc = Timeline(0, 6, 0);

        var result = ImageSequenceExporter.Export(
            doc, Dir("aba"), new ImageSequenceSettings(UniqueOnly: true));

        Assert.Equal([0, 6, 0], result.Paths.Select(SquareIn).ToArray());
    }

    // ---- naming ----------------------------------------------------------------

    [Fact]
    public void ThePrefixThePaddingAndTheFirstNumberAreTheArtists()
    {
        var result = ImageSequenceExporter.Export(
            Timeline(0, 2, 4), Dir("names"),
            new ImageSequenceSettings(Prefix: "walk", Digits: 3, StartNumber: 10));

        Assert.Equal(["walk_010.png", "walk_011.png", "walk_012.png"], Names(result));
    }

    [Fact]
    public void TheTimingFileTakesThePrefixToo()
    {
        var result = ImageSequenceExporter.Export(
            Timeline(0, null), Dir("prefixed"),
            new ImageSequenceSettings(Prefix: "walk", UniqueOnly: true));

        Assert.Equal("walk_timing.json", Path.GetFileName(result.TimingPath));
    }

    [Fact]
    public void AStartNumberDoesNotApplyToTimelineNumbers()
    {
        // The two answer the same question differently; asking for the
        // timeline's numbers and then offsetting them would give neither.
        var result = ImageSequenceExporter.Export(
            Timeline(0, 2), Dir("startignored"),
            new ImageSequenceSettings(TimelineNumbers: true, StartNumber: 50));

        Assert.Equal(["frame_0001.png", "frame_0002.png"], Names(result));
    }

    [Theory]
    [InlineData("../escape", "escape")]
    [InlineData("a/b\\c", "abc")]
    [InlineData("  ", "frame")]
    [InlineData("", "frame")]
    [InlineData("con:walk*?", "conwalk")]
    public void APrefixCannotNameAnotherFolderOrAnIllegalFile(string prefix, string expected)
    {
        var dir = Dir("safe-" + expected);

        var result = ImageSequenceExporter.Export(
            Timeline(0), dir, new ImageSequenceSettings(Prefix: prefix));

        Assert.Equal($"{expected}_0001.png", Names(result)[0]);
        // The file is in the folder that was picked and nowhere else.
        Assert.Equal(Path.GetFullPath(dir), Path.GetDirectoryName(Path.GetFullPath(result.Paths[0])));
    }

    [Theory]
    [InlineData(0, "frame_1.png")]
    [InlineData(-2, "frame_1.png")]
    [InlineData(99, "frame_0000000001.png")]
    public void PaddingIsHeldToSomethingAFileNameCanBe(int digits, string expected)
    {
        var result = ImageSequenceExporter.Export(
            Timeline(0), Dir($"digits{digits}"), new ImageSequenceSettings(Digits: digits));

        Assert.Equal(expected, Names(result)[0]);
    }

    // ---- format, size and paper ------------------------------------------------

    [Theory]
    [InlineData(ImageSaveFormat.Jpeg, ".jpg", SKEncodedImageFormat.Jpeg)]
    [InlineData(ImageSaveFormat.Webp, ".webp", SKEncodedImageFormat.Webp)]
    public void ASequenceCanBeAnyFormatAPictureCan(
        ImageSaveFormat format, string extension, SKEncodedImageFormat encoded)
    {
        var result = ImageSequenceExporter.Export(
            Timeline(0, 2), Dir(format.ToString()), new ImageSequenceSettings(Format: format));

        Assert.All(result.Paths, p => Assert.EndsWith(extension, p));
        using var codec = SKCodec.Create(result.Paths[1]);
        Assert.Equal(encoded, codec!.EncodedFormat);
    }

    [Fact]
    public void AJpegSequenceFillsTheTransparencyWithTheMatteAndSaysItDid()
    {
        var result = ImageSequenceExporter.Export(
            Timeline(0, 2), Dir("matte"),
            new ImageSequenceSettings(Format: ImageSaveFormat.Jpeg, Quality: 100, Matte: "#0000ff"));

        Assert.True(result.LostTransparency);
        using var bitmap = SKBitmap.Decode(result.Paths[0]);
        var empty = bitmap.GetPixel(Size - 2, Size - 2);
        output.WriteLine($"empty corner → {empty}");
        Assert.True(empty.Blue > 240 && empty.Red < 15 && empty.Green < 15, empty.ToString());
    }

    [Fact]
    public void ScaleRendersEveryFrameBiggerOnABiggerSurface()
    {
        var result = ImageSequenceExporter.Export(
            Timeline(0, 2), Dir("scale"), new ImageSequenceSettings(Scale: 2));

        using var bitmap = SKBitmap.Decode(result.Paths[1]);
        Assert.Equal((Size * 2, Size * 2), (bitmap.Width, bitmap.Height));
        // The square at x=2 lands at x=4: the surface grew, the stroke did not move.
        Assert.Equal(4, SquareIn(result.Paths[1]));
    }

    /// <summary>The same timeline on opaque white paper — a real background layer.</summary>
    private static Doc OnPaper(params int?[] cels)
    {
        var doc = DocumentFactory.CreateDoc(Size, Size, fps: 12, paperColor: "#ffffff");
        doc.Scene.FrameCount = cels.Length;
        var layer = doc.Scene.Layers[^1];
        layer.Cels.Clear();
        foreach (var x in cels)
        {
            layer.Cels.Add(x is { } at ? new Cel { Frame = SquareAt(at) } : new Cel());
        }
        return doc;
    }

    [Fact]
    public void ThePaperIsInTheFramesUnlessItIsAskedOut()
    {
        var result = ImageSequenceExporter.Export(OnPaper(0, 2), Dir("paper"));

        using var bitmap = SKBitmap.Decode(result.Paths[0]);
        Assert.Equal(255, bitmap.GetPixel(Size - 2, Size - 2).Alpha);
    }

    [Fact]
    public void TransparentPaperLeavesThePaperOutAndTheDrawingIn()
    {
        var doc = OnPaper(0, 2);

        var result = ImageSequenceExporter.Export(
            doc, Dir("nopaper"), new ImageSequenceSettings(TransparentPaper: true));

        using var bitmap = SKBitmap.Decode(result.Paths[0]);
        Assert.Equal(0, bitmap.GetPixel(Size - 2, Size - 2).Alpha);
        Assert.Equal(255, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(255, bitmap.GetPixel(0, 0).Red);
        // Per export, never per document: the paper is still there afterwards.
        Assert.Contains(doc.Scene.Layers, l => l.IsBackground && l.Visible);
        Assert.False(doc.Scene.TransparentBackground);
    }

    // ---- the settings record ---------------------------------------------------

    [Fact]
    public void TheSettingsForASceneCoverAllOfIt()
    {
        var scene = SixDrawings().Scene;

        var settings = ImageSequenceSettings.For(scene);

        Assert.Equal((0, 5), settings.ClampedRange(scene));
        Assert.Equal([0, 1, 2, 3, 4, 5], settings.Frames(scene));
        Assert.Equal(ImageSaveFormat.Png, settings.Format);
        Assert.False(settings.UniqueOnly);
    }

    [Fact]
    public void ARangeFromATagIsThatTagsFrames()
    {
        var scene = SixDrawings().Scene;
        var tag = new AnimationTag { Name = "walk", Start = 2, End = 4 };

        var settings = ImageSequenceSettings.For(scene).WithRange(tag);

        Assert.Equal([2, 3, 4], settings.Frames(scene));
    }
}
