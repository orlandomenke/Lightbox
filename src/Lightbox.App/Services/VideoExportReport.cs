using Lightbox.Core.Documents;

namespace Lightbox.App.Services;

/// <summary>
/// The sentences the export window says, kept out of the window (B146).
///
/// The defect this file answers was not that the encoder was missing — it was
/// that nothing said so. Every reason an export cannot start, and every
/// description of what it is about to do, is decided here so a test can read
/// it without a window on screen.
/// </summary>
public static class VideoExportReport
{
    /// <summary>The sizes the window offers, as (label, scale).</summary>
    public static readonly (string Label, double Scale)[] Scales =
    [
        ("25%", 0.25),
        ("50%", 0.5),
        ("100%", 1.0),
        ("200%", 2.0),
    ];

    /// <summary>
    /// The quality choices for a format, best first, as (label, value). H.264
    /// counts down in CRF; ProRes counts up in profile — hence labels rather
    /// than numbers in front of the artist.
    /// </summary>
    public static (string Label, int Value)[] QualitiesFor(VideoFormat format) =>
        format switch
        {
            VideoFormat.ProRes =>
            [
                ("422 HQ — editorial", 3),
                ("422 — lighter", 2),
                ("4444 — with alpha headroom", 4),
            ],
            _ =>
            [
                ("High — visually lossless", 16),
                ("Standard — review copy", 18),
                ("Small — quick look", 26),
            ],
        };

    /// <summary>
    /// What to say about the encoder before anything else. Null when FFmpeg
    /// was found and there is nothing to warn about.
    /// </summary>
    public static string? EncoderNotice(string? ffmpegPath) =>
        ffmpegPath is null
            ? "FFmpeg was not found, so no video can be written. Install FFmpeg and put it on PATH, "
              + "or reinstall Lightbox — the packaged application ships a copy beside itself. "
              + "File ▸ Export PNGs… still works and any comp package will encode the sequence."
            : null;

    /// <summary>Whether an export can start at all.</summary>
    public static bool CanExport(string? ffmpegPath) => ffmpegPath is not null;

    /// <summary>
    /// One line describing the render about to happen: pixels, frames,
    /// seconds. What an artist checks before pressing the button.
    /// </summary>
    public static string Summarise(VideoExportSettings settings, Scene scene)
    {
        var (width, height) = settings.OutputSize(scene);
        var frames = settings.FrameCount(scene);
        var seconds = settings.DurationSeconds(scene);
        return $"{width}×{height} · {frames} frame{(frames == 1 ? "" : "s")} · "
            + $"{seconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} s";
    }

    /// <summary>
    /// Why this export cannot start, or null when it can. Checked before the
    /// encoder runs so the reason arrives in the window rather than as an
    /// FFmpeg error nobody can read.
    /// </summary>
    public static string? Validate(VideoExportSettings settings, Scene scene, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return "Choose where the file goes first.";
        }
        if (scene.FrameCount <= 0 || settings.FrameCount(scene) <= 0)
        {
            return "There are no frames in that range to render.";
        }
        if (settings.Fps < 1)
        {
            return "A frame rate below 1 is not a video.";
        }
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            return $"The folder “{directory}” does not exist.";
        }
        return null;
    }

    /// <summary>The sentence after a finished render.</summary>
    public static string Done(string outputPath, VideoExportSettings settings, Scene scene)
    {
        var size = new FileInfo(outputPath) is { Exists: true } f ? " · " + FileSize(f.Length) : "";
        return $"Exported “{Path.GetFileName(outputPath)}” — {Summarise(settings, scene)}{size}.";
    }

    /// <summary>
    /// A file size a person reads. In megabytes past a megabyte and in
    /// kilobytes below it — a two-second test render is a couple of KB, and
    /// rounding it to "0 MB" reads as a failed export.
    /// </summary>
    public static string FileSize(long bytes)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return bytes >= 1024 * 1024
            ? $"{(bytes / 1024.0 / 1024.0).ToString("0.#", culture)} MB"
            : $"{Math.Max(1, bytes / 1024)} KB";
    }
}
