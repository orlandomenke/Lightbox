using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The paint path's first rule as a number (docs/DESIGN-paint-path.md): the
/// mark the live path holds when the pen lifts, laid onto the layer the way a
/// commit lays it, against what replaying the stroke's record produces. They
/// have to be the same bytes before pen-up can promote the live mark instead of
/// stamping the stroke a second time — and before "what you draw is what you
/// see" is true rather than approximately true.
/// </summary>
/// <remarks>
/// A measurement first and a gate second: every shipped preset is measured and
/// printed, and the presets on <see cref="Exact"/> are held to zero. The list
/// starts with what is already exact and only grows; a preset leaves it when
/// somebody has decided the rule no longer applies to it, which nobody should.
/// </remarks>
[Collection("BrushState")]
public class PenUpChangesNothingTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>Presets whose live mark is, byte for byte, the replay. The ratchet.</summary>
    private static readonly HashSet<string> Exact =
    [
        "builtin-ink", "builtin-soft-round", "builtin-airbrush", "builtin-pencil",
        // Phase 2: wet edge, grain and paper land under the pen for the flats.
        "builtin-watercolor", "builtin-gouache", "builtin-oil-flat",
    ];

    public static IEnumerable<object[]> Shipped() =>
        BuiltInPresets.Create().Where(p => p.Settings.Kind == BrushKind.Paint).SelectMany(p => new[]
            {
                // The stabiliser's default smoothing on, which is how the app draws;
                // and off, the mode most pixel tests use.
                new object[] { p.Id, 0, true }, new object[] { p.Id, 60, true }, new object[] { p.Id, 60, false },
                // And broad, where a soft brush keeps its ceiling at a fraction of the document.
                new object[] { p.Id, 300, true },
            });

    private static MainViewModel Vm(bool smooth)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = smooth;
        vm.ColorHex = "#203060";
        // Passes run where they are asked for, so a worker brush's mark is as
        // finished as it will get before the pen lifts.
        vm.LivePostRunner = work => { work(); return Task.CompletedTask; };
        return vm;
    }

    private readonly record struct Gap(int Differing, int Worst, double Mean, int Inked);

    private static Gap Compare(SKBitmap a, SKBitmap b)
    {
        var x = a.GetPixelSpan(); var y = b.GetPixelSpan();
        int differing = 0, worst = 0, inked = 0; long sum = 0;
        for (var i = 0; i < x.Length; i += 4)
        {
            var d = Math.Max(Math.Max(Math.Abs(x[i] - y[i]), Math.Abs(x[i + 1] - y[i + 1])),
                             Math.Max(Math.Abs(x[i + 2] - y[i + 2]), Math.Abs(x[i + 3] - y[i + 3])));
            if (y[i + 3] > 0) inked++;
            if (d == 0) continue;
            differing++; sum += d; if (d > worst) worst = d;
        }
        return new Gap(differing, worst, differing == 0 ? 0 : sum / (double)differing, inked);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Shipped))]
    public void TheLiveMarkAtPenUpIsTheReplay(string presetId, int size, bool smooth)
    {
        var vm = Vm(smooth);
        var preset = vm.BrushPresetChoices.Single(p => p.Id == presetId);
        vm.ApplyPreset(preset);
        if (size > 0) vm.BrushSize = size;
        var brush = vm.CurrentToolSettingsForTest;
        var scene = vm.Doc.Scene;

        // The layer as it stands before the stroke.
        using var before = vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height).Copy();

        vm.BeginStroke(120, 300, 0.35);
        for (var i = 1; i <= 48; i++)
        {
            vm.MoveStroke(120 + i * 14.0, 300 + Math.Sin(i * 0.22) * 110, 0.35 + 0.6 * i / 48.0);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // What promotion would write: the live mark over the layer, at the
        // stroke's opacity and blend, exactly as StampPaint's last draw does.
        using var promoted = before.Copy();
        var mark = vm.LiveMarkSource;
        Assert.NotNull(mark);
        var stroke = new Stroke { Tool = ToolKind.Brush, Brush = brush };
        using (var canvas = new SKCanvas(promoted))
        using (var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(brush.Opacity, 0, 1) * 255)),
            BlendMode = BlendModes.ForStroke(stroke),
        })
        {
            canvas.DrawBitmap(mark!, 0, 0, paint);
            canvas.Flush();
        }

        vm.EndStroke();
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // The replay: the record rendered from nothing, the way a reload does.
        using var replay = FrameRasterizer.Materialize(vm.PaintedCel(), scene.Width, scene.Height);
        // And what the artist is left looking at: the frame the commit wrote.
        var kept = vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
        var keptGap = Compare(kept, replay);
        if (keptGap.Differing > 0)
        {
            var ka = kept.GetPixelSpan(); var kb = replay.GetPixelSpan(); var n = 0; var at = new List<string>();
            for (var i = 0; i < ka.Length && n < 8; i += 4)
            {
                if (ka[i] == kb[i] && ka[i + 1] == kb[i + 1] && ka[i + 2] == kb[i + 2] && ka[i + 3] == kb[i + 3]) continue;
                var px = i / 4;
                at.Add($"({px % kept.Width},{px / kept.Width}) kept {ka[i]},{ka[i + 1]},{ka[i + 2]},{ka[i + 3]} record {kb[i]},{kb[i + 1]},{kb[i + 2]},{kb[i + 3]}");
                n++;
            }
            output.WriteLine($"PENUP   {preset.Name} kept≠record at: {string.Join("; ", at)}");
        }
        Assert.True(keptGap.Differing == 0,
            $"{preset.Name}: the committed frame is not what its record renders ({keptGap.Differing} pixels, worst {keptGap.Worst}) — invariant 1");

        var gap = Compare(promoted, replay);

        var route = brush.Medium.Kind != MediumKind.None ? "medium"
            : BrushEngine.DrawsAsOneSilhouette(brush) ? "silhouette"
            : BrushEngine.NeedsToothCeiling(brush) ? "tooth"
            : BrushEngine.NeedsFootprintCap(brush) ? "soft cap"
            : brush.Mixing is not null ? "mixing"
            : brush.WetEdge > 0 || brush.Granulation > 0 || brush.TextureSurface is not null ? "pass"
            : "dabs";
        output.WriteLine(
            $"PENUP {preset.Name,-22} size {brush.Size,5:0} {(smooth ? "smooth" : "raw"),-6} {route,-10} {(vm.PromotedStrokes == 1 ? "promoted" : "replayed"),-9} differing {gap.Differing,7} of {gap.Inked,7} inked  worst {gap.Worst,3}  mean {gap.Mean,5:F2}");

        if (gap.Differing > 0)
        {
            // Where: the first few differing pixels, promoted against replay.
            var a = promoted.GetPixelSpan(); var b = replay.GetPixelSpan(); var shown = 0;
            var where = new List<string>();
            for (var i = 0; i < a.Length && shown < 10; i += 4)
            {
                if (a[i] == b[i] && a[i + 1] == b[i + 1] && a[i + 2] == b[i + 2] && a[i + 3] == b[i + 3]) continue;
                var px = i / 4;
                where.Add($"({px % promoted.Width},{px / promoted.Width}) {a[i]},{a[i + 1]},{a[i + 2]},{a[i + 3]} vs {b[i]},{b[i + 1]},{b[i + 2]},{b[i + 3]}");
                shown++;
            }
            output.WriteLine($"PENUP   where: {string.Join("; ", where)}");
        }

        Assert.True(gap.Inked > 50, $"{preset.Name} drew almost nothing: {gap.Inked} inked pixels");
        if (Exact.Contains(presetId))
        {
            Assert.True(vm.PromotedStrokes == 1,
                $"{preset.Name} is exact and its pen-up still rendered the stroke a second time");
            Assert.True(gap.Differing == 0,
                $"{preset.Name} at size {brush.Size:0}: pen-up changes {gap.Differing} pixels (worst {gap.Worst}) — the live mark is not the replay");
        }
    }
}
