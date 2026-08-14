using System.Diagnostics;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Services;

public enum VideoFormat
{
    /// <summary>H.264 in MP4 — review and sharing.</summary>
    Mp4,

    /// <summary>ProRes 422 HQ in MOV — editorial handoff.</summary>
    ProRes,
}

/// <summary>
/// The render pipeline (Q56): every timeline frame composited by the same
/// code the PNG sequence uses, piped raw into a bundled FFmpeg, the scratch
/// track muxed alongside. FFmpeg runs as a subprocess on purpose — an encoder
/// crash cannot take the application down, and the LGPL boundary stays a
/// process boundary.
///
/// Absent FFmpeg is a sentence, not a crash: the packaged application ships
/// the binary beside itself (packaging/), a dev machine falls back to PATH,
/// and a machine with neither is told what to install.
/// </summary>
public static class VideoExporter
{
    /// <summary>
    /// The bundled binary first — packaging puts it in ffmpeg/ beside the
    /// executable — then the machine's own PATH.
    /// </summary>
    public static string? FindFfmpeg()
    {
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg", exe);
        if (File.Exists(bundled)) return bundled;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// The FFmpeg argument vector, pure so a test can hold it still: raw RGBA
    /// frames on stdin, the audio file as a second input when there is one,
    /// the codec block per format.
    /// </summary>
    public static List<string> BuildArgs(
        VideoFormat format, int width, int height, int fps, string outputPath,
        string? audioPath = null, int audioOffsetFrames = 0, double audioVolume = 1.0,
        int audioTrimStartFrames = 0, int? audioLengthFrames = null,
        int? quality = null)
    {
        var args = new List<string>
        {
            "-y",
            "-f", "rawvideo",
            "-pix_fmt", "rgba",
            "-s", $"{width}x{height}",
            "-r", fps.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-i", "pipe:0",
        };

        if (audioPath is not null)
        {
            var offsetSeconds = audioOffsetFrames / (double)Math.Max(1, fps);
            // The clip bar's in-trim (Q57) and a negative offset both consume
            // the head of the source; they add.
            var trimSeconds = Math.Max(0, audioTrimStartFrames) / (double)Math.Max(1, fps)
                + (offsetSeconds < 0 ? -offsetSeconds : 0);
            if (offsetSeconds > 0)
            {
                // The sound starts later than frame zero: delay the input.
                args.AddRange(["-itsoffset", Seconds(offsetSeconds)]);
            }
            if (trimSeconds > 0)
            {
                args.AddRange(["-ss", Seconds(trimSeconds)]);
            }
            if (audioLengthFrames is { } lengthFrames)
            {
                // The out-trim: only this much of the source plays.
                args.AddRange(["-t", Seconds(lengthFrames / (double)Math.Max(1, fps))]);
            }
            args.AddRange(["-i", audioPath]);
            if (Math.Abs(audioVolume - 1.0) > 1e-9)
            {
                args.AddRange(["-filter:a",
                    $"volume={audioVolume.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"]);
            }
        }

        switch (format)
        {
            case VideoFormat.Mp4:
                // yuv420p wants even dimensions; pad the odd edge rather than
                // rescale the art.
                args.AddRange([
                    "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    "-crf", Math.Clamp(quality ?? 18, 0, 51).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-preset", "medium",
                    "-movflags", "+faststart",
                ]);
                if (audioPath is not null) args.AddRange(["-c:a", "aac", "-b:a", "192k"]);
                break;
            case VideoFormat.ProRes:
                // yuv422p10le wants an even width for the same reason, and a
                // document is free to be an odd number of pixels wide.
                args.AddRange([
                    "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
                    "-c:v", "prores_ks",
                    "-profile:v", Math.Clamp(quality ?? 3, 0, 5).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-pix_fmt", "yuv422p10le", "-vendor", "apl0",
                ]);
                if (audioPath is not null) args.AddRange(["-c:a", "pcm_s16le"]);
                break;
        }

        if (audioPath is not null) args.Add("-shortest");
        args.Add(outputPath);
        return args;

        static string Seconds(double s) =>
            s.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The extension a format writes, for the save dialog.</summary>
    public static string ExtensionOf(VideoFormat format) =>
        format switch { VideoFormat.ProRes => "mov", _ => "mp4" };

    /// <summary>
    /// Render and encode the whole timeline at the document's own size and
    /// rate — the settings-free path the menu used before there was a window
    /// to choose in, kept because most exports want exactly this.
    /// </summary>
    public static string? Export(
        Doc doc, VideoFormat format, string outputPath,
        string? audioPath = null, Action<int, int>? progress = null) =>
        Export(doc, VideoExportSettings.For(doc.Scene, format), outputPath, audioPath, progress);

    /// <summary>
    /// Render and encode the frames <paramref name="settings"/> asks for.
    /// Returns null on success or a sentence saying why not — including when
    /// the artist cancelled, because a half-written file needs saying out
    /// loud. <paramref name="audioPath"/> is the resolved sound file, already
    /// null when the track is muted, missing or switched off for this export.
    /// </summary>
    public static string? Export(
        Doc doc, VideoExportSettings settings, string outputPath,
        string? audioPath = null, Action<int, int>? progress = null,
        CancellationToken cancel = default)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            return "FFmpeg was not found — reinstall Lightbox, or install FFmpeg and put it on PATH.";
        }

        var scene = doc.Scene;
        var (width, height) = settings.OutputSize(scene);
        var (fromFrame, toFrame) = settings.ClampedRange(scene);
        var fps = Math.Max(1, settings.Fps);
        var track = settings.IncludeAudio ? scene.Audio : null;
        if (!settings.IncludeAudio) audioPath = null;
        // A split track arrives already laid out on the timeline (Q57) — the
        // sections have been assembled into one WAV with silence in the gaps,
        // anchored to the document's frame zero — so applying the track's own
        // offset and trim again would move the sound twice. What still
        // applies is the render range: an excerpt shifts the assembled sound
        // exactly as it shifts an ordinary one.
        var assembled = track?.Segments is { Count: > 0 };
        var audioOffset = assembled ? -fromFrame : settings.AudioOffsetFrames(scene);
        var args = BuildArgs(
            format: settings.Format, width, height, fps, outputPath,
            audioPath, audioOffset, track?.Volume ?? 1.0,
            assembled ? 0 : track?.TrimStartFrames ?? 0,
            assembled ? null : track?.TrimLengthFrames, settings.Quality);

        var info = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) info.ArgumentList.Add(a);

        try
        {
            using var process = Process.Start(info)
                ?? throw new InvalidOperationException("FFmpeg did not start.");
            // Drain stderr concurrently or a chatty encode deadlocks the pipe.
            var stderr = new System.Text.StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stderr.AppendLine(e.Data);
                    if (stderr.Length > 16384) stderr.Remove(0, stderr.Length - 8192);
                }
            };
            process.BeginErrorReadLine();

            var buffer = new byte[width * height * 4];
            var total = toFrame - fromFrame + 1;
            var cancelled = false;
            using (var cache = new FrameBitmapCache())
            using (var stdin = process.StandardInput.BaseStream)
            {
                cache.PoseResolver = (f, cel) => Skinning.PoseFrameForRender(doc, f, cel);
                for (var i = fromFrame; i <= toFrame; i++)
                {
                    if (cancel.IsCancellationRequested) { cancelled = true; break; }
                    using var image = SequenceExporter.RenderFrame(doc, cache, i, settings.Scale, (width, height));
                    unsafe
                    {
                        fixed (byte* p = buffer)
                        {
                            var pixmap = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                            if (!image.ReadPixels(pixmap, (IntPtr)p, width * 4, 0, 0))
                            {
                                return $"Frame {i + 1} could not be read back for encoding.";
                            }
                        }
                    }
                    stdin.Write(buffer, 0, buffer.Length);
                    progress?.Invoke(i - fromFrame + 1, total);
                }
            }

            process.WaitForExit();
            if (cancelled)
            {
                // Closing the pipe finishes whatever FFmpeg already had, so the
                // file exists and is short. Saying so beats leaving the artist
                // to discover it.
                return $"Export cancelled — “{Path.GetFileName(outputPath)}” holds the frames rendered so far.";
            }
            if (process.ExitCode != 0)
            {
                var tail = stderr.ToString();
                var lines = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var last = lines.Length > 0 ? lines[^1].Trim() : "no error text";
                return $"FFmpeg failed (exit {process.ExitCode}): {last}";
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception
                                      or UnauthorizedAccessException or InvalidOperationException)
        {
            // Every way starting or feeding FFmpeg can fail — a broken pipe, a
            // binary that will not execute, a folder that will not take the
            // file — comes back as a sentence rather than as an exception the
            // caller has to guess at.
            return $"Video export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// The decoded scratch track written back as 16-bit PCM WAV — what the
    /// PNG-sequence export drops beside its frames, so a comp has the sound
    /// in the one encoding everything reads.
    /// </summary>
    public static void WriteWavPcm16(Core.Audio.AudioClip clip, string path)
    {
        var channels = Math.Min(clip.Channels, 2);
        var samples = clip.Channels <= 2 ? clip.Samples : clip.MonoMixdown();
        if (clip.Channels > 2) channels = 1;

        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        var dataBytes = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(clip.SampleRate);
        w.Write(clip.SampleRate * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in samples)
        {
            w.Write((short)Math.Clamp(s * 32767f, short.MinValue, short.MaxValue));
        }
    }
}
