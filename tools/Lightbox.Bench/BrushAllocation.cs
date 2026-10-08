using System.Security.Cryptography;
using System.Text.Json;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Bench;

/// <summary>
/// Playback phase 2a, step 2: what one committed stroke allocates on the managed
/// heap, for every shipped brush preset — and the hash of what it drew.
/// </summary>
/// <remarks>
/// <para>
/// The garbage is what stops renders scaling across cores (phase 1: 24 MB a
/// drawing, GC pause growing with every worker). A per-dab allocation shows as
/// bytes per dab well above zero; a per-stroke one as a constant.
/// </para>
/// <para>
/// <b>The hash is the point as much as the bytes.</b> Reusing a paint or an array
/// inside <c>BrushEngine</c> must change nothing on the page. <c>--save</c> writes
/// every preset's hash; <c>--check</c> compares against it, preset by preset, so
/// media brushes — which take their own path through the engine — are proven
/// identical rather than assumed so.
/// </para>
/// </remarks>
public static class BrushAllocation
{
    //   dotnet run --project tools/Lightbox.Bench -c Release -- brushalloc [--save file.json | --check file.json]
    public static int Run(string[] args)
    {
        var save = Arg(args, "--save");
        var check = Arg(args, "--check");
        var expected = check is not null
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(check))!
            : null;
        var hashes = new Dictionary<string, string>();
        var info = new SKImageInfo(1920, 1080, SKColorType.Rgba8888, SKAlphaType.Premul);
        var mismatches = 0;

        Console.WriteLine($"{"preset",-28} {"medium",-10} {"dabs",5} {"KB/stroke",10} {"B/dab",7}  hash");
        foreach (var preset in BuiltInPresets.Create())
        {
            var stroke = Mark(preset.Settings);
            var dabs = BrushEngine.WalkDabs(stroke).Count;
            // Warm once (JIT, first native allocations), then the least of three:
            // allocation is deterministic, the minimum only removes stragglers.
            Render(stroke, info);
            long bytes = long.MaxValue;
            string hash = "";
            for (var i = 0; i < 3; i++)
            {
                var before = GC.GetTotalAllocatedBytes(precise: true);
                hash = Render(stroke, info);
                bytes = Math.Min(bytes, GC.GetTotalAllocatedBytes(precise: true) - before);
            }
            hashes[preset.Name] = hash;
            var verdict = "";
            if (expected is not null)
            {
                var same = expected.TryGetValue(preset.Name, out var was) && was == hash;
                verdict = same ? "  same" : "  DIFFERENT";
                if (!same) mismatches++;
            }
            Console.WriteLine($"{preset.Name,-28} {preset.Settings.Medium.Kind,-10} {dabs,5} {bytes / 1024.0,10:0.0} " +
                              $"{(dabs == 0 ? 0 : bytes / dabs),7}  {hash[..12]}{verdict}");
            if (args.Contains("--types"))
            {
                // Sampled about every 100 KB, so ten strokes for a readable share.
                using var ticks = new TileMemory.AllocationTicks();
                for (var i = 0; i < 10; i++) Render(stroke, info);
                Thread.Sleep(1200);
                foreach (var (type, kb) in ticks.Top(6)) Console.WriteLine($"      {kb / 10.0,8:0} KB/stroke  {type}");
            }
        }

        if (save is not null)
        {
            File.WriteAllText(save, JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"\nhashes saved to {save}");
        }
        if (expected is not null)
        {
            Console.WriteLine(mismatches == 0
                ? $"\nevery preset draws the same bytes as {check}"
                : $"\n{mismatches} preset(s) draw DIFFERENT bytes from {check}");
        }
        return mismatches == 0 ? 0 : 1;
    }

    private static string Render(Stroke stroke, SKImageInfo info)
    {
        using var target = new SKBitmap(info);
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(new SKColor(0xF4, 0xF2, 0xEC));
            // Something under the stroke, so smudge, blender and blur have paint
            // to move: on a blank page all three hash the same and prove nothing.
            using (var stripe = new SKPaint { IsAntialias = false })
            {
                for (var x = 0; x < info.Width; x += 24)
                {
                    stripe.Color = new SKColor((byte)(x * 7), (byte)(255 - x % 256), (byte)(x * 3), 255);
                    canvas.DrawRect(SKRect.Create(x, 250, 12, 320), stripe);
                }
            }
            BrushEngine.StampStroke(canvas, stroke, info, target);
        }
        return Convert.ToHexString(SHA256.HashData(target.GetPixelSpan()));
    }

    /// <summary>The preset sweep's ordinary mark: forty events along a wave, pressure varying.</summary>
    private static Stroke Mark(BrushSettings settings) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#204060",
        Points = Enumerable.Range(0, 40)
            .Select(i => new StrokePoint(120 + i * 18, 400 + Math.Sin(i * 0.4) * 90, 0.4 + i % 3 * 0.3))
            .ToList(),
        Brush = settings.Clone(),
    };

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
