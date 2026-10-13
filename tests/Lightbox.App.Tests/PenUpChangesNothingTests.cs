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
        // Phase 3: the media rebuilt as recipes. They mix colour with what is
        // under them, which is a mark the live path keeps too.
        "builtin-watercolor-wet", "builtin-oil", "builtin-gouache-body", "builtin-ink-wash",
    ];

    public static IEnumerable<object[]> Shipped() =>
        // Brushes, not erasers: on the blank sheet this draws on, an eraser (the Blot) has nothing to take.
        BuiltInPresets.Create().Where(p => p.Settings.Kind == BrushKind.Paint && p.Tool == ToolKind.Brush).SelectMany(p => new[]
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

    public static IEnumerable<object[]> Overlapping() =>
        from id in new[] { "builtin-watercolor", "builtin-gouache", "builtin-oil-flat", "builtin-soft-round", "builtin-airbrush", "builtin-pencil" }
        from shape in new[] { "u-turn", "zigzag", "scribble" }
        from smooth in new[] { false, true }
        select new object[] { id, shape, smooth };

    /// <summary>
    /// A stroke that comes back alongside itself. A ceiling and a wet edge are
    /// functions of how far a pixel is from the edge of everything the stroke
    /// has covered, so a later dab can change a pixel it never touches: the
    /// second leg of a U-turn swallows the first leg's facing edge, and the rim
    /// that was drawn there must go. The band a pointer event re-finishes has
    /// to reach that far, or the screen keeps a rim the record does not have —
    /// and promotion would then write it into the frame (invariant 1).
    /// </summary>
    /// <remarks>
    /// Found by review, not by the suite: the sine the first theory draws never
    /// meets itself. Legs a hair under one brush width apart are the worst
    /// case, where the new leg just covers the old edge and the pixels behind
    /// it lie outside the new dabs' own reach.
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(Overlapping))]
    public void AStrokeThatMeetsItselfIsStillTheReplay(string presetId, string shape, bool smooth)
    {
        const int Size = 100;
        var vm = Vm(smooth);
        var preset = vm.BrushPresetChoices.Single(p => p.Id == presetId);
        vm.ApplyPreset(preset);
        vm.BrushSize = Size;
        var scene = vm.Doc.Scene;

        var points = new List<(double X, double Y)>();
        switch (shape)
        {
            case "u-turn":
                for (var i = 0; i <= 40; i++) points.Add((200 + i * 12.0, 250));
                for (var i = 1; i <= 8; i++) points.Add((680 + Math.Sin(i * Math.PI / 8) * 50, 250 + (Size - 1) * i / 8.0));
                for (var i = 1; i <= 40; i++) points.Add((680 - i * 12.0, 250 + Size - 1));
                break;
            case "zigzag":
                for (var leg = 0; leg < 5; leg++)
                    for (var i = 0; i <= 20; i++)
                        points.Add((200 + (leg % 2 == 0 ? i : 20 - i) * 22.0, 180 + leg * Size * 0.75 + i * 1.5));
                break;
            default:
                for (var i = 0; i <= 120; i++)
                    points.Add((500 + Math.Cos(i * 0.31) * (60 + i * 1.6), 360 + Math.Sin(i * 0.47) * (40 + i * 1.1)));
                break;
        }

        vm.BeginStroke(points[0].X, points[0].Y, 0.8);
        for (var i = 1; i < points.Count; i++)
        {
            vm.MoveStroke(points[i].X, points[i].Y, 0.55 + 0.4 * i / points.Count);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.EndStroke();
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var replay = FrameRasterizer.Materialize(vm.PaintedCel(), scene.Width, scene.Height);
        var kept = vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
        var gap = Compare(kept, replay);
        output.WriteLine(
            $"MEETS {preset.Name,-18} {shape,-8} {(smooth ? "smooth" : "raw"),-6} {(vm.PromotedStrokes == 1 ? "promoted" : "replayed"),-9} differing {gap.Differing,7} of {gap.Inked,7} inked  worst {gap.Worst,3}");

        Assert.True(gap.Inked > 5000, $"{preset.Name} drew almost nothing: {gap.Inked} inked pixels");
        Assert.True(gap.Differing == 0,
            $"{preset.Name}, {shape}: the committed frame is not what its record renders ({gap.Differing} pixels, worst {gap.Worst}) — invariant 1");
        Assert.True(vm.PromotedStrokes == 1, $"{preset.Name}, {shape}: an exact brush rendered its stroke a second time at pen-up");
    }

    public static IEnumerable<object[]> DeadStraight() =>
        from id in new[] { "builtin-watercolor", "builtin-gouache", "builtin-oil-flat", "builtin-soft-round", "builtin-pencil", "builtin-ink" }
        from size in new[] { 60, 150 }
        from smooth in new[] { true, false }
        select new object[] { id, size, smooth };

    /// <summary>
    /// A stroke with dead-straight, evenly paced, constant-pressure runs — what
    /// a ruler, a steady hand or a mouse draws — broken by a change of pressure
    /// and a turn, with several samples to an event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The perf lab found this and the suite did not: about one stroke in
    /// eight on the flats was rendered again at pen-up, every one reporting
    /// that a dab already stamped for good had changed. The live path judged a
    /// dab settled when two consecutive walks of the stroke agreed on it. On a
    /// straight even run they agree about <em>everything</em>, including the
    /// dabs at the very tip that the next point is still free to move — so the
    /// tip was stamped as final, and the first bend or change of pressure
    /// moved it. Every other stroke in this file curves and ramps its pressure
    /// from the first point, which is why none of them saw it.
    /// </para>
    /// <para>
    /// Settled is now a matter of structure: a dab is final when the points it
    /// was walked from can no longer change, whatever two walks happen to say.
    /// </para>
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(DeadStraight))]
    public void AStrokeWithDeadStraightRunsIsKept(string presetId, int size, bool smooth)
    {
        var vm = Vm(smooth);
        var preset = vm.BrushPresetChoices.Single(p => p.Id == presetId);
        vm.ApplyPreset(preset);
        vm.BrushSize = size;
        var scene = vm.Doc.Scene;

        // Straight right at constant pressure; a harder press; straight on; a
        // right-angle turn down; straight; back the way it came.
        var samples = new List<MainViewModel.PointerSample>();
        double x = 200, y = 200;
        void Run(int n, double dx, double dy, double pressure)
        {
            for (var i = 0; i < n; i++) { x += dx; y += dy; samples.Add(new MainViewModel.PointerSample(x, y, pressure)); }
        }
        Run(40, 8, 0, 0.6);
        Run(30, 8, 0, 0.9);
        Run(25, 0, 8, 0.9);
        Run(30, -8, 0, 0.5);
        Run(20, -8, 0, 1.0);

        vm.BeginStroke(200, 200, 0.6);
        var batch = new[] { 1, 3, 2, 4, 1, 2 };
        for (int i = 0, b = 0; i < samples.Count; b++)
        {
            var take = Math.Min(batch[b % batch.Length], samples.Count - i);
            vm.MoveStrokeBatch(samples.GetRange(i, take));
            i += take;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.EndStroke();
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var replay = FrameRasterizer.Materialize(vm.PaintedCel(), scene.Width, scene.Height);
        var kept = vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
        var gap = Compare(kept, replay);
        output.WriteLine(
            $"STRAIGHT {preset.Name,-18} {size,4} {(smooth ? "smooth" : "raw"),-6} {(vm.PromotedStrokes == 1 ? "kept" : "REPLAYED: " + vm.WhyReplayed)}  differing {gap.Differing}");

        Assert.True(gap.Differing == 0,
            $"{preset.Name}: the committed frame is not what its record renders ({gap.Differing} pixels, worst {gap.Worst}) — invariant 1");
        Assert.True(vm.PromotedStrokes == 1, $"{preset.Name} at {size}: rendered again at pen-up — {vm.WhyReplayed}");
    }

    public static IEnumerable<object[]> Mixing() =>
        from id in new[] { "builtin-watercolor-wet", "builtin-oil", "builtin-gouache-body", "builtin-ink-wash" }
        from size in new[] { 0, 90 }
        from smooth in new[] { true, false }
        select new object[] { id, size, smooth };

    /// <summary>
    /// A brush that mixes colour with what is under it, drawn across paint
    /// that is already there. On a blank layer such a brush picks up nothing
    /// and is exactly its own colour, so the strokes above do not test the
    /// mixing at all; here every dab reads the ground, carries some of it on,
    /// and the next dab reads what this one laid.
    /// </summary>
    /// <remarks>
    /// The chain is what makes it hard: a dab's colour depends on the pixels
    /// of the dabs before it, so a level's difference in how one dab is
    /// rasterised is carried into every dab after. The replay used to stamp a
    /// mixing stroke into a stroke-sized scratch under a translation, where
    /// the live path stamps into the document's own coordinates - a level
    /// apart on a scattering of pixels, by Skia's gradient arithmetic - so a
    /// mixing stroke was never one the live path could keep, and Watercolor
    /// and Oil, rebuilt without the simulation, were still rendered again at
    /// every pen-up.
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(Mixing))]
    public void AMixingStrokeAcrossPaintIsKept(string presetId, int size, bool smooth)
    {
        var vm = Vm(smooth);
        var scene = vm.Doc.Scene;

        // Something to pick up: three fat bars of plain ink in other colours.
        vm.ApplyPreset(vm.BrushPresetChoices.Single(p => p.Id == "builtin-ink"));
        vm.BrushSize = 40;
        foreach (var (x, hex) in new[] { (300.0, "#c8301c"), (480.0, "#e2a11c"), (660.0, "#1c7a3a") })
        {
            vm.ColorHex = hex;
            vm.BeginStroke(x, 150, 1);
            for (var i = 1; i <= 12; i++) vm.MoveStroke(x + (i * 2), 150 + (i * 25), 1);
            vm.EndStroke();
            for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        var before = vm.PromotedStrokes;

        var preset = vm.BrushPresetChoices.Single(p => p.Id == presetId);
        vm.ApplyPreset(preset);
        if (size > 0) vm.BrushSize = size;
        vm.ColorHex = "#203060";
        Assert.NotNull(vm.CurrentToolSettingsForTest.Mixing);

        vm.BeginStroke(180, 300, 0.5);
        for (var i = 1; i <= 50; i++)
        {
            vm.MoveStroke(180 + (i * 12.0), 300 + (Math.Sin(i * 0.2) * 60), 0.5 + (0.4 * i / 50.0));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.EndStroke();
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var replay = FrameRasterizer.Materialize(vm.PaintedCel(), scene.Width, scene.Height);
        var kept = vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
        var gap = Compare(kept, replay);
        var promoted = vm.PromotedStrokes - before == 1;
        output.WriteLine(
            $"MIXING {preset.Name,-12} size {vm.BrushSize,4:0} {(smooth ? "smooth" : "raw"),-6} {(promoted ? "kept" : "REPLAYED: " + vm.WhyReplayed)}  differing {gap.Differing} worst {gap.Worst}");

        Assert.True(gap.Differing == 0,
            $"{preset.Name}: the committed frame is not what its record renders ({gap.Differing} pixels, worst {gap.Worst}) — invariant 1");
        Assert.True(promoted, $"{preset.Name}: a mixing stroke was rendered again at pen-up — {vm.WhyReplayed}");
    }
    public static IEnumerable<object[]> Erasing() =>
        from how in new[] { "alt:builtin-ink", "alt:builtin-soft-round", "alt:builtin-pencil", "alt:builtin-watercolor-wet", "eraser", "towel", "towel-off-the-page", "faint" }
        from size in new[] { 60, 200 }
        from smooth in new[] { false, true }
        select new object[] { how, size, smooth };

    /// <summary>
    /// An eraser stroke is kept at pen-up too. What is on screen while rubbing
    /// out is the live mark taken out of the layer, which is the same last
    /// draw a replay makes - so there was never a reason to render it again,
    /// only a rule that kept brush strokes and nothing else.
    /// </summary>
    /// <remarks>
    /// Found by the perf lab's rework-stroke scenario, 2026-10-12: every
    /// eraser stroke was rendered again from its record at pen-up - 111, 175
    /// and 177 ms at 60, 150 and 300 px for the Blot, 68 to 164 ms for plain
    /// Alt-erase, where a painted stroke took 37 to 83. Erasing with the brush
    /// in hand (Alt), the Eraser tool, and a towel-like eraser with a texture
    /// and an opacity below one, over three bars of paint.
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(Erasing))]
    public void AnEraserStrokeAcrossPaintIsKept(string how, int size, bool smooth)
    public static IEnumerable<object[]> Rewetting() =>
        from size in new[] { 0, 60, 200 }
        from smooth in new[] { false, true }
        from pressed in new[] { false, true }
        select new object[] { size, smooth, pressed };

    /// <summary>
    /// The Water brush (Q255): what is on screen while rewetting is what the
    /// record replays, and lifting the pen changes nothing.
    /// </summary>
    /// <remarks>
    /// It reaches the screen a different way from paint - a blur brush writes
    /// the layer's own pixels into a working copy - so it is held to the same
    /// two things separately: the working copy the pen leaves is the committed
    /// frame, and the committed frame is a render of the record from nothing.
    /// The live path renders only the rectangle each event changed; this is
    /// what proves those rectangles add up to the whole.
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(Rewetting))]
    public void WaterUnderThePenIsTheReplay(int size, bool smooth, bool pressed)
    {
        var vm = Vm(smooth);
        var scene = vm.Doc.Scene;

        vm.ApplyPreset(vm.BrushPresetChoices.Single(p => p.Id == "builtin-ink"));
        vm.BrushSize = 60;
        foreach (var (x, hex) in new[] { (300.0, "#c8301c"), (480.0, "#e2a11c"), (660.0, "#1c7a3a") })
        {
            vm.ColorHex = hex;
            vm.BeginStroke(x, 150, 1);
            for (var i = 1; i <= 12; i++) vm.MoveStroke(x + (i * 2), 150 + (i * 25), 1);
            vm.EndStroke();
            for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        using var painted = vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height).Copy();
        var strokesBefore = vm.PaintedCel().Strokes.Count;

        var withAlt = how.StartsWith("alt:", StringComparison.Ordinal);
        if (withAlt)
        {
            vm.ApplyPreset(vm.BrushPresetChoices.Single(p => p.Id == how[4..]));
        }
        else
        {
            vm.IsEraser = true;
            if (how == "faint")
            {
                // An opacity that does not land on a whole level: 0.29 of 255 is 73.95.
                vm.CurrentToolSettingsForTest.Opacity = 0.29;
            }
            if (how.StartsWith("towel", StringComparison.Ordinal))
            {
                var towel = vm.CurrentToolSettingsForTest;
                towel.Hardness = 0.6; towel.Opacity = 0.8; towel.Flow = 0.6; towel.Spacing = 0.1;
                towel.PressureFlowGamma = 1.2;
                towel.TextureSurface = PaperKind.Fibre; towel.TextureScale = 6; towel.TextureDepth = 0.6;
            }
        }
        vm.BrushSize = size;
        var before = vm.PromotedStrokes;

        // Across the bars - and, for one case, from outside the page on the
        // left to outside it on the right, where the mark's window is cut by
        // the page's edge.
        var offPage = how.EndsWith("off-the-page", StringComparison.Ordinal);
        double x0 = offPage ? -90 : 180, step = offPage ? (scene.Width + 180) / 50.0 : 12.0;
        vm.BeginStroke(x0, 300, 0.5, eraseWithCurrentBrush: withAlt);
        for (var i = 1; i <= 50; i++)
        {
            vm.MoveStroke(x0 + (i * step), 300 + (Math.Sin(i * 0.2) * 60), 0.5 + (0.4 * i / 50.0));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.EndStroke();
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.IsEraser = false;

        Assert.True(vm.PaintedCel().Strokes.Count == strokesBefore + 1, $"{how}: the eraser stroke was not recorded");
        Assert.Equal(ToolKind.Eraser, vm.PaintedCel().Strokes[^1].Tool);

        using var replay = FrameRasterizer.Materialize(vm.PaintedCel(), scene.Width, scene.Height);
        var kept = vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
        var gap = Compare(kept, replay);
        var took = Compare(kept, painted);
        var promoted = vm.PromotedStrokes - before == 1;
        output.WriteLine(
            $"ERASING {how,-28} size {size,4} {(smooth ? "smooth" : "raw"),-6} {(promoted ? "kept" : "REPLAYED: " + vm.WhyReplayed)}  "
            + $"rubbed out {took.Differing} pixels, differing from the replay {gap.Differing} worst {gap.Worst}");

        Assert.True(took.Differing > 500, $"{how}: the eraser took almost nothing ({took.Differing} pixels) - nothing was tested");
        Assert.True(gap.Differing == 0,
            $"{how}: the committed frame is not what its record renders ({gap.Differing} pixels, worst {gap.Worst}) - invariant 1");
        Assert.True(promoted, $"{how} at {size}: an eraser stroke was rendered again at pen-up - {vm.WhyReplayed}");

        vm.ApplyPreset(vm.BrushPresetChoices.Single(p => p.Id == "builtin-water"));
        if (size > 0) vm.BrushSize = size;
        Assert.NotNull(vm.CurrentToolSettingsForTest.Wet);

        // Across the bars and back over them, so the stroke meets itself and its own tail is rewritten.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        vm.BeginStroke(200, 250, 0.6);
        for (var i = 1; i <= 70; i++)
        {
            var back = i > 45;
            var t = back ? 45 - ((i - 45) * 1.4) : i;
            vm.MoveStroke(200 + (t * 12.0), (back ? 330 : 250) + (Math.Sin(i * 0.2) * 40), pressed ? 0.3 + (0.6 * i / 70.0) : 0.8);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var perEvent = clock.Elapsed.TotalMilliseconds / 70;
        using var underThePen = vm.LiveCompositeForTests!.Copy();
        clock.Restart();
        vm.EndStroke();
        var penUp = clock.Elapsed.TotalMilliseconds;
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var kept = vm.FrameCache.Get(vm.PaintedCel(), scene.Width, scene.Height);
        using var replay = FrameRasterizer.Materialize(vm.PaintedCel(), scene.Width, scene.Height);
        var live = Compare(underThePen, kept);
        var record = Compare(kept, replay);
        var did = Compare(kept, painted);
        output.WriteLine(
            $"WATER size {vm.BrushSize,4:0} {(smooth ? "smooth" : "raw"),-6} {(pressed ? "rising" : "level"),-6} changed {did.Differing,6} pixels; "
            + $"under the pen vs kept {live.Differing}, kept vs replay {record.Differing}; {perEvent:0.00} ms an event, pen-up {penUp:0.0} ms");

        Assert.True(did.Differing > 2000, $"the water changed almost nothing ({did.Differing} pixels) - nothing was tested");
        Assert.True(live.Differing == 0,
            $"lifting the pen changed {live.Differing} pixels (worst {live.Worst}): what was on screen is not what was kept");
        Assert.True(record.Differing == 0,
            $"the committed frame is not what its record renders ({record.Differing} pixels, worst {record.Worst}) - invariant 1");
    }
}
