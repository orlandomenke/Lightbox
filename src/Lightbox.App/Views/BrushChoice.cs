using Avalonia.Media.Imaging;
using Lightbox.App.Services;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Views;

/// <summary>
/// One tile in the brush picker: the preset, and a picture of the mark it makes.
/// </summary>
/// <remarks>
/// <para>
/// The same argument as <see cref="TipChoice"/>, one level up. A list of names is
/// unusable for marks: "Wet bristle 04" and "Wet bristle 07" are different brushes and
/// the names do not say how, and an imported collection arrives with fifty-six names
/// somebody else chose. The picture is the control and the name is the caption.
/// </para>
/// <para>
/// <b>Cached on the preset's id <em>and</em> its settings.</b> Ids alone would be the
/// obvious key and the wrong one — a preset is editable, so the tile would keep showing
/// the mark the brush used to make. <see cref="BrushComparison.Fingerprint"/> is derived
/// from the same canonical form the modified-dot uses, so the two can never disagree
/// about whether a brush changed.
/// </para>
/// </remarks>
public sealed class BrushChoice
{
    /// <summary>
    /// How many tiles are kept. Bounded because the cache outlives the picker and an
    /// imported collection plus a session of tweaking would otherwise grow without end —
    /// which is what a leak looks like from the outside.
    /// </summary>
    private const int Keep = 320;

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Ages = new();

    private BrushChoice(BrushPreset preset, Bitmap? preview)
    {
        Preset = preset;
        Preview = preview;
    }

    public BrushPreset Preset { get; }

    /// <summary>The mark, or null when the brush could not be drawn at all.</summary>
    public Bitmap? Preview { get; }

    public string Name => Preset.Name;

    /// <summary>
    /// The brush's real size, because the picture does not carry it.
    /// </summary>
    /// <remarks>
    /// A tile maps size onto a range it can draw (see
    /// <see cref="BrushPreviewRenderer.PreviewSizeFor"/>), so a 40 px and a 300 px brush
    /// read as different weights rather than as their actual widths. The number is the
    /// part that has to be exact, so it is written down rather than inferred from the
    /// picture.
    /// </remarks>
    public string SizeLabel => $"{Preset.Settings.Size:0.#}";

    public string CostBadge => Preset.CostBadge;

    /// <summary>Everything the picture cannot say, on hover.</summary>
    public string Tip
    {
        get
        {
            var tags = Preset.Tags is { Count: > 0 } t ? $"\n{string.Join(", ", t)}" : "";
            return $"{Preset.Name} — {Preset.Settings.Size:0.#} px{tags}\n{Preset.CostTip}";
        }
    }

    public static BrushChoice For(BrushPreset preset) => new(preset, Render(preset));

    private static Bitmap? Render(BrushPreset preset)
    {
        var key = $"{preset.Id}:{BrushComparison.Fingerprint(preset.Settings)}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        Bitmap? bitmap;
        try
        {
            // An imported brush carries its tip on the preset and only registers it when
            // the brush is first used — so without this the tile for every .abr in a
            // collection would draw a round dab, in the one case where the tip is the
            // whole difference between them. Registration is by globally unique id and
            // skips what it already has, so doing it here is free and idempotent.
            if (preset.TipPng is { } png && preset.Settings.TipId is { } tipId)
            {
                BrushTipRegistry.Register(new Dictionary<string, string> { [tipId] = png });
            }

            using var mark = BrushPreviewRenderer.Render(preset.Settings, width: 112, height: 40);
            bitmap = Encode(mark);
        }
        catch
        {
            bitmap = null; // a tile with no picture, rather than a picker that throws
        }

        Cache[key] = bitmap;
        Ages.Enqueue(key);
        while (Ages.Count > Keep && Ages.TryDequeue(out var stale))
        {
            // Not disposed: a bitmap evicted here may still be on screen in an open
            // picker, and disposing one that a visual is drawing crashes. Dropping the
            // reference is enough — the collector takes it once nothing is showing it.
            Cache.Remove(stale);
        }
        return bitmap;
    }

    private static Bitmap Encode(SKBitmap mark)
    {
        using var image = SKImage.FromBitmap(mark)
            ?? throw new InvalidOperationException("Could not snapshot a brush preview.");
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
