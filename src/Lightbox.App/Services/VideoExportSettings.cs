using Lightbox.Core.Documents;

namespace Lightbox.App.Services;

/// <summary>
/// What an artist decides before a render starts (B146): format, size, range,
/// rate, quality and whether the scratch track rides along.
///
/// A record rather than a pile of arguments because the export window, the
/// argument vector and the frame loop all have to agree about the same
/// answers, and because the numbers are worth testing without a window on
/// screen — which is where the previous version of this feature had nothing
/// at all: it rendered the whole timeline at the document's size, every time,
/// with no way to say otherwise.
/// </summary>
public sealed record VideoExportSettings(
    VideoFormat Format,
    double Scale,
    int FromFrame,
    int ToFrame,
    int Fps,
    bool IncludeAudio,
    int Quality)
{
    /// <summary>
    /// The defaults a scene exports with if the artist changes nothing: the
    /// whole timeline, at its own size and rate, at the quality the pipeline
    /// shipped with before this window existed.
    /// </summary>
    public static VideoExportSettings For(Scene scene, VideoFormat format = VideoFormat.Mp4) =>
        new(format,
            Scale: 1.0,
            FromFrame: 0,
            ToFrame: Math.Max(0, scene.FrameCount - 1),
            Fps: Math.Max(1, scene.Fps),
            IncludeAudio: true,
            Quality: DefaultQuality(format));

    /// <summary>
    /// H.264's CRF (lower is bigger and better; 18 is the visually-lossless
    /// convention) or ProRes's profile number (3 = 422 HQ). One field, because
    /// only one of them is ever live and two would have to be kept in step.
    /// </summary>
    public static int DefaultQuality(VideoFormat format) =>
        format switch { VideoFormat.ProRes => 3, _ => 18 };

    /// <summary>
    /// The encoded size: the scene's output size — the camera's when there is
    /// one — taken through <see cref="Scale"/>. Never below 2 px, and never
    /// odd on a side, because H.264's yuv420p cannot encode an odd dimension
    /// and padding a deliberate half-size render is a worse answer than
    /// landing on the even number next door.
    /// </summary>
    public (int Width, int Height) OutputSize(Scene scene)
    {
        var (w, h) = SequenceExporter.OutputSize(scene);
        return (Even(w * Scale), Even(h * Scale));

        static int Even(double v) => Math.Max(2, (int)Math.Round(v / 2, MidpointRounding.AwayFromZero) * 2);
    }

    /// <summary>The frames this render covers, clamped to what the scene has.</summary>
    public (int From, int To) ClampedRange(Scene scene)
    {
        var last = Math.Max(0, scene.FrameCount - 1);
        var from = Math.Clamp(Math.Min(FromFrame, ToFrame), 0, last);
        var to = Math.Clamp(Math.Max(FromFrame, ToFrame), from, last);
        return (from, to);
    }

    public int FrameCount(Scene scene)
    {
        var (from, to) = ClampedRange(scene);
        return to - from + 1;
    }

    /// <summary>
    /// How long the render will be, in seconds — the one number that tells an
    /// artist their range and rate mean what they think they mean.
    /// </summary>
    public double DurationSeconds(Scene scene) => FrameCount(scene) / (double)Math.Max(1, Fps);

    /// <summary>
    /// Where the scratch track starts relative to the <em>render</em>. The
    /// sound is anchored to frame zero of the document, so a range that
    /// starts at frame 24 starts the sound 24 frames earlier — otherwise an
    /// excerpt would carry the wrong seconds of dialogue.
    /// </summary>
    public int AudioOffsetFrames(Scene scene) =>
        (IncludeAudio ? scene.Audio?.OffsetFrames ?? 0 : 0) - ClampedRange(scene).From;
}
