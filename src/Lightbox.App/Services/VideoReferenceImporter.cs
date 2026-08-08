using System.Diagnostics;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// Footage to draw against (Q56): FFmpeg extracts the clip's frames at the
/// scene's own fps, and they are tiled into one contact sheet that flows
/// through the ordinary reference machinery — a window per frame, mapped to
/// the timeline, never exported. The document keeps the path, not the pixels.
/// </summary>
public static class VideoReferenceImporter
{
    /// <summary>
    /// The extraction budget. Reference frames are for registration, not for
    /// grading: 480 px wide is plenty to draw against, and 240 frames is
    /// twenty seconds at 12 fps — a shot, not a reel. Both caps keep the
    /// contact sheet's memory honest (240 frames at 480×270 is ~124 MB, the
    /// ceiling of acceptable; unbounded HD footage would be gigabytes).
    /// </summary>
    public const int MaxFrames = 240;

    public const int MaxWidth = 480;

    /// <summary>Grid shape for <paramref name="count"/> tiles — near-square, wide first.</summary>
    internal static (int Columns, int Rows) GridFor(int count)
    {
        if (count <= 0) return (0, 0);
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);
        return (columns, rows);
    }

    /// <summary>
    /// Extract the clip at <paramref name="fps"/> and tile the frames into a
    /// contact sheet. Returns the sheet (caller owns it) and one window cell
    /// per frame, or an error sentence.
    /// </summary>
    public static (SKBitmap Sheet, List<ReferenceCell> Cells)? Extract(
        string ffmpegPath, string videoPath, int fps, out string? error)
    {
        error = null;
        var temp = Directory.CreateTempSubdirectory("lightbox-video-ref").FullName;
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in new[]
            {
                "-i", videoPath,
                "-vf", $"fps={fps},scale='min(iw,{MaxWidth})':-2",
                "-frames:v", MaxFrames.ToString(),
                Path.Combine(temp, "f_%05d.png"),
            })
            {
                info.ArgumentList.Add(a);
            }

            using (var process = Process.Start(info))
            {
                if (process is null)
                {
                    error = "FFmpeg did not start.";
                    return null;
                }
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    error = $"The clip could not be read: {(lines.Length > 0 ? lines[^1].Trim() : "no detail")}";
                    return null;
                }
            }

            var frames = Directory.GetFiles(temp, "f_*.png").Order().ToList();
            if (frames.Count == 0)
            {
                error = "The clip contained no video frames.";
                return null;
            }

            using var first = SKBitmap.Decode(frames[0]);
            if (first is null)
            {
                error = "The extracted frames could not be decoded.";
                return null;
            }
            var (fw, fh) = (first.Width, first.Height);
            var (columns, rows) = GridFor(frames.Count);

            var sheet = new SKBitmap(columns * fw, rows * fh, SKColorType.Rgba8888, SKAlphaType.Premul);
            var cells = new List<ReferenceCell>(frames.Count);
            using (var canvas = new SKCanvas(sheet))
            {
                canvas.Clear(SKColors.Transparent);
                for (var i = 0; i < frames.Count; i++)
                {
                    var x = i % columns * fw;
                    var y = i / columns * fh;
                    using var frame = SKBitmap.Decode(frames[i]);
                    if (frame is not null) canvas.DrawBitmap(frame, x, y);
                    cells.Add(new ReferenceCell { X = x, Y = y, Width = fw, Height = fh });
                }
            }
            return (sheet, cells);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Video import failed: {ex.Message}";
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
                // A stuck temp dir is untidy, not a failure.
            }
        }
    }
}
