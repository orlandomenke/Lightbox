using System.Runtime.InteropServices;
using System.Text.Json;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// What an artist decides before a run of numbered pictures is written (Q219):
/// format, size, which frames, whether a held drawing is written once, and
/// what the files are called.
/// </summary>
/// <remarks>
/// <para>
/// A record for the reason <see cref="VideoExportSettings"/> is one: the
/// window, the summary sentence and the frame loop have to agree about the
/// same answers, and the answers are worth testing with no window on screen.
/// </para>
/// <para>
/// <b>The defaults are <c>Export PNGs…</c> as it always was</b> — every frame,
/// PNG, the document's own size, <c>frame_0001.png</c> — and a test holds the
/// bytes to that. Frame numbers here count from zero; the window is the only
/// place that counts from one.
/// </para>
/// </remarks>
/// <param name="Step">Every Nth frame of the range. Less than one is one.</param>
/// <param name="UniqueOnly">
/// Write a held drawing once. Judged on the rendered picture, not on any one
/// layer's holds — see <see cref="ImageSequenceExporter"/>.
/// </param>
/// <param name="TimelineNumbers">
/// Name each file by the frame it sits on rather than by its place in the run,
/// so a file keeps its name whatever else was exported with it.
/// <c>StartNumber</c> does not apply when this is set.
/// </param>
/// <param name="TransparentPaper">
/// Leave the paper out, for this export only. Meaningless in a format with no
/// alpha, where <c>Matte</c> is what applies instead.
/// </param>
public sealed record ImageSequenceSettings(
    ImageSaveFormat Format = ImageSaveFormat.Png,
    int Quality = 90,
    double Scale = 1.0,
    int FromFrame = 0,
    int ToFrame = int.MaxValue,
    int Step = 1,
    bool UniqueOnly = false,
    bool TimelineNumbers = false,
    string Prefix = ImageSequenceSettings.DefaultPrefix,
    int Digits = 4,
    int StartNumber = 1,
    bool TransparentPaper = false,
    string Matte = "#ffffff")
{
    public const string DefaultPrefix = "frame";

    /// <summary>The whole timeline, with everything else at its default.</summary>
    public static ImageSequenceSettings For(Scene scene) =>
        new(ToFrame: Math.Max(0, scene.FrameCount - 1));

    /// <summary>The same settings over the frames a tag covers.</summary>
    public ImageSequenceSettings WithRange(AnimationTag tag) =>
        this with { FromFrame = tag.Start, ToFrame = tag.End };

    /// <summary>The frames this export covers, clamped to what the scene has.</summary>
    public (int From, int To) ClampedRange(Scene scene)
    {
        var last = Math.Max(0, scene.FrameCount - 1);
        var from = Math.Clamp(Math.Min(FromFrame, ToFrame), 0, last);
        var to = Math.Clamp(Math.Max(FromFrame, ToFrame), from, last);
        return (from, to);
    }

    /// <summary>The frames that are rendered: the range, taken a step at a time.</summary>
    /// <remarks>
    /// A timeline with no frames has none to write, which is what the export
    /// did before it had settings. The step is held to the length of the range
    /// before it is added to anything: a step near <c>int.MaxValue</c> would
    /// otherwise wrap the counter negative and never leave the loop.
    /// </remarks>
    public IReadOnlyList<int> Frames(Scene scene)
    {
        if (scene.FrameCount <= 0) return [];
        var (from, to) = ClampedRange(scene);
        var step = Math.Clamp(Step, 1, to - from + 1);
        var frames = new List<int>((to - from) / step + 1);
        for (var i = from; i <= to; i += step) frames.Add(i);
        return frames;
    }

    /// <summary>
    /// The name of one file. <paramref name="ordinal"/> is its place in the
    /// run counted from zero; <paramref name="frameIndex"/> is where it sits
    /// on the timeline.
    /// </summary>
    public string FileName(int ordinal, int frameIndex)
    {
        var number = TimelineNumbers ? frameIndex + 1 : Math.Max(0, StartNumber) + ordinal;
        var digits = Math.Clamp(Digits, 1, 10);
        return $"{SafePrefix}_{number.ToString("D" + digits)}{ImageSaveFormats.Extension(Format)}";
    }

    /// <summary>The holds, written beside the pictures when they say something.</summary>
    public string TimingFileName => $"{SafePrefix}_timing.json";

    /// <summary>
    /// <c>Prefix</c> as something that can only ever name a file in the folder
    /// that was picked.
    /// </summary>
    /// <remarks>
    /// The character list is spelled out rather than asked of the platform:
    /// <c>Path.GetInvalidFileNameChars</c> is two characters on Linux, and a
    /// sequence named there has to open on the Windows machine it is sent to.
    /// </remarks>
    public string SafePrefix
    {
        get
        {
            const string illegal = "<>:\"/\\|?*";
            var kept = new string((Prefix ?? "")
                .Where(c => !char.IsControl(c) && !illegal.Contains(c))
                .ToArray()).Trim().Trim('.').Trim();
            return kept.Length == 0 ? DefaultPrefix : kept;
        }
    }
}

/// <param name="File">The file name, without its folder.</param>
/// <param name="FrameIndex">Where the picture sits on the timeline, from zero.</param>
/// <param name="Hold">
/// How many timeline frames it stands for: until the next file written, or
/// the end of the range for the last one.
/// </param>
public sealed record SequenceFrame(string File, int FrameIndex, int Hold);

/// <param name="Paths">The pictures, in playing order. Never the timing file.</param>
/// <param name="Frames">One entry per picture: its name, its frame and its hold.</param>
/// <param name="TimingPath">The timing file, or null when none was written.</param>
/// <param name="LostTransparency">
/// Measured from the rendered frames, exactly as <see cref="ImageSaveResult"/> does.
/// </param>
public sealed record ImageSequenceResult(
    IReadOnlyList<string> Paths,
    IReadOnlyList<SequenceFrame> Frames,
    string? TimingPath,
    bool LostTransparency)
{
    public ImageSaveFormat Format { get; init; } = ImageSaveFormat.Png;

    /// <summary>The one sentence worth putting in front of the artist, or null.</summary>
    public string? Warning => LostTransparency
        ? $"{ImageSaveFormats.Label(Format)} has no transparency — the see-through "
            + "areas were filled in. Export as PNG or WebP to keep them."
        : null;
}

/// <summary>
/// Writes a run of numbered pictures: <c>File ▸ Export image sequence…</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every frame goes through <see cref="SequenceExporter.RenderFrame"/> and
/// <see cref="SaveAsImage.Encode"/></b>, the composite and the encoder a saved
/// picture already uses — so a frame from here and that frame saved as an
/// image are the same pixels by construction.
/// </para>
/// <para>
/// <b>"Unique" means a hold, and it is read off the picture.</b> A frame is
/// left out when it rendered to exactly the pixels of the one written before
/// it. Reading holds off the record instead would be cheaper and wrong three
/// ways at once: two layers hold different lengths, a camera move changes a
/// held drawing, and a duplicated cel is two drawings that are one picture.
/// It costs a render of every frame in the range, which an export of every
/// frame pays anyway, and one frame's pixels held in memory.
/// </para>
/// <para>
/// A picture that comes back later (A B A) is written again. The files stay in
/// the order they play, so a reader that ignores the timing file still gets
/// the animation with its holds dropped rather than a shuffled one.
/// </para>
/// <para>
/// <b>The timing file is absent unless unique frames were asked for.</b> A run
/// with every frame in it has no holds to report.
/// </para>
/// </remarks>
public static class ImageSequenceExporter
{
    private static readonly JsonSerializerOptions TimingJson = new() { WriteIndented = true };

    public static ImageSequenceResult Export(
        Doc doc, string directory, ImageSequenceSettings? settings = null)
    {
        settings ??= ImageSequenceSettings.For(doc.Scene);
        if (ImageSaveFormats.IsVector(settings.Format))
        {
            throw new NotSupportedException(
                $"A sequence of {ImageSaveFormats.Label(settings.Format)} frames is not built. "
                + "Save a single frame with Save as image, or export PNG, JPEG or WebP.");
        }
        Directory.CreateDirectory(directory);
        var scene = doc.Scene;

        using var cache = new FrameBitmapCache();
        cache.Rig = RigIndex.For(doc);
        cache.PoseResolver = (f, cel) => Skinning.PoseFrameForRender(doc, f, cel, cache.Rig);

        var options = new ImageSaveOptions(settings.Format, settings.Quality, settings.Scale, settings.Matte);
        var withoutPaper = settings.TransparentPaper && ImageSaveFormats.SupportsAlpha(settings.Format);
        var (_, to) = settings.ClampedRange(scene);

        var paths = new List<string>();
        var written = new List<int>();
        var lostTransparency = false;
        byte[]? previous = null;
        byte[]? current = null;

        foreach (var index in settings.Frames(scene))
        {
            using var image = SequenceExporter.RenderFrame(
                doc, cache, index, settings.Scale, withoutPaper: withoutPaper);

            if (settings.UniqueOnly)
            {
                current = Pixels(image, current);
                if (previous is not null && current.AsSpan().SequenceEqual(previous)) continue;
                (previous, current) = (current, previous);
            }

            var path = Path.Combine(directory, settings.FileName(paths.Count, index));
            if (SaveAsImage.Encode(image, path, options)) lostTransparency = true;
            paths.Add(path);
            written.Add(index);
        }

        var frames = new List<SequenceFrame>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            var until = i + 1 < written.Count ? written[i + 1] : to + 1;
            frames.Add(new SequenceFrame(Path.GetFileName(paths[i]), written[i], until - written[i]));
        }

        string? timingPath = null;
        if (settings.UniqueOnly)
        {
            timingPath = Path.Combine(directory, settings.TimingFileName);
            File.WriteAllText(timingPath, Timing(scene, settings, frames));
        }

        return new ImageSequenceResult(paths, frames, timingPath, lostTransparency) { Format = settings.Format };
    }

    /// <summary>
    /// The holds as a file: frame numbers from one, like the file names and
    /// like the timeline the artist reads them off.
    /// </summary>
    private static string Timing(
        Scene scene, ImageSequenceSettings settings, IReadOnlyList<SequenceFrame> frames)
    {
        var (from, to) = settings.ClampedRange(scene);
        return JsonSerializer.Serialize(new
        {
            fps = Math.Max(1, scene.Fps),
            from = from + 1,
            to = to + 1,
            step = Math.Max(1, settings.Step),
            frames = frames.Select(f => new { file = f.File, frame = f.FrameIndex + 1, hold = f.Hold }).ToArray(),
        }, TimingJson);
    }

    /// <summary>The frame's pixels, into <paramref name="reuse"/> when it fits.</summary>
    private static byte[] Pixels(SKImage image, byte[]? reuse)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var buffer = reuse is { } r && r.Length == info.BytesSize ? r : new byte[info.BytesSize];
        var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            if (!image.ReadPixels(info, pin.AddrOfPinnedObject(), info.RowBytes, 0, 0))
            {
                throw new InvalidOperationException("Could not read a rendered frame back.");
            }
        }
        finally
        {
            pin.Free();
        }
        return buffer;
    }
}
