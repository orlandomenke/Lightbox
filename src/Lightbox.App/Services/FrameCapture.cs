using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// The last few frames Lightbox published, and the two buffers behind each one,
/// kept in a ring so an artifact can be looked at after it has gone.
/// </summary>
/// <remarks>
/// <para>
/// <b>The render report's counterpart, pointed at pixels instead of time.</b>
/// That file answers "how long did it take"; this one answers "what did it
/// look like", which is the question nothing in this codebase could answer.
/// </para>
/// <para>
/// <b>A ring, because the failure it exists for is transient.</b> The artifact
/// that produced it — rectangles appearing inside a live stroke — is gone
/// before a hand can reach a screenshot key, and asking an artist to
/// photograph their own screen mid-stroke is asking them to stop drawing.
/// Recording continuously and writing out afterwards is the only shape that
/// catches something you cannot predict the moment of.
/// </para>
/// <para>
/// <b>Three images per frame, and the second and third are the point.</b> The
/// composite says an artifact exists. The raw dab scratch and the processed
/// buffer say <em>which stage produced it</em> — mark that is in the scratch
/// and not on screen is the compositor losing ink it was handed; mark that is
/// wrong in the processed buffer is the live post-process. A session was spent
/// guessing between those two with only the screen to look at, and guessed
/// wrong twice.
/// </para>
/// <para>
/// <b>Off unless armed, and it says so.</b> Three scaled blits per publish is
/// not free on a path that runs per pointer event, and 24 frames of three
/// images is about 80 MB held. Neither is acceptable as a default and neither
/// matters while somebody is deliberately hunting a bug.
/// </para>
/// </remarks>
internal sealed class FrameCapture
{
    /// <summary>
    /// How many publishes are kept. At the measured rate of 17 pointer events
    /// to a publish this is a couple of seconds of drawing — enough to hold an
    /// artifact AND the frames either side of it, which is what makes one
    /// legible rather than merely present.
    /// </summary>
    private const int Frames = 24;

    /// <summary>
    /// Widest edge of a kept image. Big enough that a rectangle inside a stroke
    /// is unmistakable, small enough that the ring is tens of megabytes rather
    /// than hundreds — a 4K document at full size would be 33 MB per image.
    /// </summary>
    private const int MaxEdge = 700;

    private readonly Shot?[] _ring = new Shot?[Frames];
    private int _next;
    private long _seen;

    private sealed record Shot(SKBitmap? Screen, SKBitmap? Raw, SKBitmap? Processed, string Note)
    {
        public void Dispose()
        {
            Screen?.Dispose();
            Raw?.Dispose();
            Processed?.Dispose();
        }
    }

    /// <summary>Whether frames are being recorded. Off at startup, always.</summary>
    public bool Armed { get; private set; }

    /// <summary>How many publishes have been recorded since arming.</summary>
    public long Recorded => _seen;

    /// <summary>Start or stop recording. Stopping keeps what was recorded.</summary>
    public void Arm(bool on)
    {
        if (on == Armed) return;
        Armed = on;
        if (on) Clear();
    }

    /// <summary>
    /// Keep this publish, if armed. Costs three scaled blits and nothing else —
    /// no encoding, which is what would make it too expensive to leave on while
    /// drawing. Encoding happens once, in <see cref="Write"/>.
    /// </summary>
    /// <param name="screen">
    /// The composite, or null when this publish only described one for the
    /// render thread to perform (the culled and tiled routes). A null here is
    /// itself worth seeing: it says the frame was never composited on this
    /// thread, so the screen image cannot be compared against the buffers.
    /// </param>
    /// <param name="raw">The live dab scratch: what the brush engine stamped.</param>
    /// <param name="processed">The live post-process buffer: what the pass rendered.</param>
    /// <param name="note">One line of context — event counts, the dirty rect, the route.</param>
    public void Note(SKImage? screen, SKBitmap? raw, SKBitmap? processed, string note)
    {
        if (!Armed) return;
        _seen++;
        _ring[_next]?.Dispose();
        _ring[_next] = new Shot(Shrink(screen), Shrink(raw), Shrink(processed), note);
        _next = (_next + 1) % Frames;
    }

    /// <summary>Throw away everything recorded so far.</summary>
    public void Clear()
    {
        for (var i = 0; i < _ring.Length; i++)
        {
            _ring[i]?.Dispose();
            _ring[i] = null;
        }
        _next = 0;
        _seen = 0;
    }

    /// <summary>
    /// Write the ring out, oldest first, and return the folder. Null when there
    /// is nothing recorded or the write failed — this is a diagnostic and must
    /// never be able to take the application down with it, which is
    /// <see cref="DiagnosticLog"/>'s rule and holds here for its reason.
    /// </summary>
    public string? Write(string intoDirectory)
    {
        try
        {
            if (_seen == 0) return null;
            var folder = Path.Combine(intoDirectory, $"frames-{DateTime.Now:yyyyMMdd-HHmmss}");
            System.IO.Directory.CreateDirectory(folder);

            var index = new System.Text.StringBuilder();
            index.AppendLine("Lightbox frame capture");
            index.AppendLine($"written   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            index.AppendLine($"build     {DiagnosticLog.Build}");
            index.AppendLine($"publishes recorded since arming   {_seen}");
            index.AppendLine();
            index.AppendLine("Oldest first. Each frame has up to three images:");
            index.AppendLine("  -screen     what was composited for the canvas");
            index.AppendLine("  -raw        the dab scratch, the marks the brush engine stamped");
            index.AppendLine("  -processed  the live post-process buffer, what the pass rendered");
            index.AppendLine();
            index.AppendLine("Mark that is in -raw and not in -screen is ink the compositor was");
            index.AppendLine("handed and did not draw. Mark that is wrong in -processed is the");
            index.AppendLine("live post-process. That distinction is the whole point of the pair.");
            index.AppendLine();

            var n = 0;
            for (var i = 0; i < Frames; i++)
            {
                var shot = _ring[(_next + i) % Frames];
                if (shot is null) continue;
                var stem = $"{n:000}";
                SavePng(shot.Screen, Path.Combine(folder, stem + "-screen.png"));
                SavePng(shot.Raw, Path.Combine(folder, stem + "-raw.png"));
                SavePng(shot.Processed, Path.Combine(folder, stem + "-processed.png"));
                index.AppendLine($"{stem}  {shot.Note}");
                n++;
            }

            File.WriteAllText(Path.Combine(folder, "index.txt"), index.ToString());
            return folder;
        }
        catch (Exception ex)
        {
            DiagnosticLog.WriteNote("frame-capture", ex.ToString());
            return null;
        }
    }

    /// <summary>
    /// A copy small enough to keep <see cref="Frames"/> of, over paper so that
    /// alpha reads as ink rather than as a checkerboard — the scratch buffers
    /// are almost entirely transparent and would otherwise be unreadable.
    /// </summary>
    private static SKBitmap? Shrink(SKBitmap? src)
    {
        if (src is null || src.Width <= 0 || src.Height <= 0) return null;
        using var image = SKImage.FromBitmap(src);
        return Shrink(image);
    }

    /// <inheritdoc cref="Shrink(SKBitmap?)"/>
    private static SKBitmap? Shrink(SKImage? src)
    {
        if (src is null || src.Width <= 0 || src.Height <= 0) return null;
        var scale = Math.Min(1.0, (double)MaxEdge / Math.Max(src.Width, src.Height));
        var w = Math.Max(1, (int)(src.Width * scale));
        var h = Math.Max(1, (int)(src.Height * scale));
        var small = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(small);
        canvas.Clear(SKColors.White);
        canvas.DrawImage(src, SKRect.Create(w, h),
            new SKSamplingOptions(SKFilterMode.Linear));
        canvas.Flush();
        return small;
    }

    private static void SavePng(SKBitmap? bmp, string path)
    {
        if (bmp is null) return;
        using var image = SKImage.FromBitmap(bmp);
        using var data = image?.Encode(SKEncodedImageFormat.Png, 90);
        if (data is null) return;
        using var file = File.Create(path);
        data.SaveTo(file);
    }
}
