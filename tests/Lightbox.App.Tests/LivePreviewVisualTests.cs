using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Testing;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// What the canvas actually publishes while a stroke is being drawn, as a picture.
/// </summary>
/// <remarks>
/// <para>
/// The engine-level sheets in <c>BrushVisualTests</c> drive <c>BrushEngine</c> directly, which is
/// the right place to compare a draft against a commit. This file is the other question: does the
/// <b>application</b> put that on screen? It goes through <c>BeginStroke</c>/<c>MoveStroke</c>/
/// <c>EndStroke</c> and reads the published <c>RenderSnapshot</c> — the same path
/// <c>LivePreviewPixelTests</c> asserts on, photographed rather than measured.
/// </para>
/// <para>
/// Worth having separately because the two have failed independently. B54 was an engine defect the
/// application's feed provoked; B39 was an application defect with a clean engine underneath, and
/// eight engine measurements said so before anyone looked above it. A sheet from each level says
/// which half a regression is in without a second run.
/// </para>
/// </remarks>
[Collection("BrushState")]
[Trait("Category", "Visual")]
public class LivePreviewVisualTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static SKBitmap Pixels(RenderSnapshot snapshot) => SKBitmap.FromImage(snapshot.Image)!;

    /// <summary>A document with a checkered bar to work on, on a paintable layer.</summary>
    private static MainViewModel WithBar(out RenderSnapshot? latest)
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var scene = vm.Doc.Scene;

        var top = new Layer { Name = "Paint", Kind = LayerKind.Painted };
        top.Cels.Add(new Cel { Frame = new PaintedFrame() });
        for (var i = 1; i < scene.Layers[^1].Cels.Count; i++) top.Cels.Add(new Cel());
        scene.Layers.Add(top);
        vm.ActiveLayerIndex = scene.Layers.Count - 1;

        // Through AppendExternalStrokes, not by mutating the frame: a direct write never runs the
        // edit funnel, the frame cache keeps serving the empty layer, and the sheet is a picture of
        // an empty canvas. That mistake cost a probe earlier in this session.
        var strokes = new List<Stroke>
        {
            new()
            {
                Tool = ToolKind.Brush, Color = "#20c040",
                Points = [new StrokePoint(180, 250, 1), new StrokePoint(760, 250, 1)],
                Brush = new BrushSettings { Size = 90, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
            },
        };
        for (var x = 180; x < 760; x += 60)
        {
            strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush, Color = "#101820",
                Points = [new StrokePoint(x, 215, 1), new StrokePoint(x, 285, 1)],
                Brush = new BrushSettings { Size = 16, Hardness = 1, Opacity = 0.8, Flow = 1, Spacing = 0.2 },
            });
        }
        Assert.Equal(strokes.Count, vm.AppendExternalStrokes(top.Id, 0, strokes));

        RenderSnapshot? seen = null;
        vm.SnapshotChanged += s => seen = s;
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        latest = seen;
        return vm;
    }

    /// <summary>
    /// Mid-drag against after-release, on the real path — B69's "what we paint is what we see".
    /// </summary>
    /// <remarks>
    /// The assertion is deliberately weak and the picture is the deliverable. B69 is open and its
    /// standard is an artist's, not a tolerance: the sheet is what lets that judgement be made
    /// without installing the application. What is asserted is only that both frames <em>have</em> a
    /// mark, because a sheet of two blank panels agrees with itself perfectly.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(BrushKind.Blur)]
    [InlineData(BrushKind.Smudge)]
    public void WhatTheCanvasShowsMidDragAgainstWhatItKeeps(BrushKind kind)
    {
        var vm = WithBar(out var latest);
        vm.SelectedBrushPreset = vm.BrushPresetChoices.First(p => p.Settings.Kind == kind);

        RenderSnapshot? live = null;
        vm.SnapshotChanged += s => live = s;

        // A diagonal drag across the bar's edge — the case that discriminates, per B33 and B49.
        vm.BeginStroke(220, 190, 1);
        for (var x = 260; x <= 720; x += 40) vm.MoveStroke(x, 190 + (x - 260) / 4.0, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(live);
        using var midDrag = Pixels(live!);

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var afterRelease = Pixels(live!);

        int differing = 0, worst = 0;
        for (var y = 0; y < Math.Min(midDrag.Height, afterRelease.Height); y++)
        {
            for (var x = 0; x < Math.Min(midDrag.Width, afterRelease.Width); x++)
            {
                var a = midDrag.GetPixel(x, y);
                var b = afterRelease.GetPixel(x, y);
                if (a == b) continue;
                differing++;
                worst = Math.Max(worst, Math.Abs(a.Red - b.Red));
            }
        }
        output.WriteLine(
            $"{kind}: {differing} px change on release, worst {worst} of 255 "
            + $"({midDrag.Width}×{midDrag.Height} published)");

        VisualSheet.WriteComparison(
            $"canvas-{kind}-release".ToLowerInvariant(),
            $"B69 — {kind} through the real canvas: the last frame with the pen down against the "
            + $"first frame after release. {differing} px change, worst {worst}/255.",
            "pen down", midDrag, "released", afterRelease, amplify: 4);

        // Not vacuous: both frames must carry the bar, or the comparison is between two blanks.
        Assert.True(Ink(midDrag) > 1000, "the mid-drag frame has almost nothing in it");
        Assert.True(Ink(afterRelease) > 1000, "the released frame has almost nothing in it");
    }

    private static int Ink(SKBitmap bmp)
    {
        var n = 0;
        for (var y = 0; y < bmp.Height; y += 2)
        {
            for (var x = 0; x < bmp.Width; x += 2)
            {
                if (bmp.GetPixel(x, y).Green > 100 && bmp.GetPixel(x, y).Red < 200) n++;
            }
        }
        return n;
    }

    /// <summary>
    /// The whole brush library on one sheet, at the size each preset ships with.
    /// </summary>
    /// <remarks>
    /// The picker's tiles are shrunk to fit a tile and a medium's lattice is size-dependent, so a
    /// tile cannot answer "is this brush right" — B50's original measurement had to be retaken at
    /// true size for exactly that reason. This is the sheet that can: every shipped preset, drawn at
    /// its own size, in one image. It is also the fastest way to notice a brush that has quietly
    /// stopped making a mark at all.
    /// </remarks>
    [AvaloniaFact]
    public void EveryShippedBrushDrawsItsOwnStroke()
    {
        if (!VisualSheet.Wanted) return;

        var vm = new MainViewModel(null) { SmoothStrokes = false, ColorHex = "#101828" };
        var panels = new List<VisualSheet.Panel>();
        var sheets = new List<SKBitmap>();
        var faint = new List<string>();

        foreach (var preset in vm.BrushPresetChoices)
        {
            var info = new SKImageInfo(260, 110, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var layer = new SKBitmap(info);
            using (var canvas = new SKCanvas(layer))
            {
                canvas.Clear(SKColors.Transparent);
                // Effect brushes have nothing to work on a blank sheet, so give them something —
                // otherwise three tiles are legitimately empty and the sheet reads as three bugs.
                if (preset.Settings.Kind is BrushKind.Smudge or BrushKind.Blur)
                {
                    using var bar = new SKPaint { Color = new SKColor(0x20, 0xC0, 0x40) };
                    canvas.DrawRect(new SKRect(20, 40, 240, 78), bar);
                    using var stripe = new SKPaint { Color = new SKColor(0x10, 0x10, 0x10, 200) };
                    for (var x = 20; x < 240; x += 18) canvas.DrawRect(new SKRect(x, 40, x + 9, 78), stripe);
                }
                canvas.Flush();

                var stroke = new Stroke
                {
                    Tool = ToolKind.Brush,
                    Color = "#101828",
                    Points = [.. Enumerable.Range(0, 9).Select(i =>
                        new StrokePoint(30 + i * 25.0, 55 + Math.Sin(i * 0.7) * 14, 0.35 + i * 0.07))],
                    Brush = preset.Settings.Clone(),
                };
                Lightbox.Raster.BrushEngine.StampStroke(canvas, stroke, info, layer);
                canvas.Flush();
            }

            var peak = 0;
            for (var y = 0; y < layer.Height; y++)
            {
                for (var x = 0; x < layer.Width; x++)
                {
                    var p = layer.GetPixel(x, y);
                    if (p.Alpha == 0) continue;
                    peak = Math.Max(peak, 255 - (p.Red * p.Alpha + 255 * (255 - p.Alpha)) / 255);
                }
            }
            if (peak < 40) faint.Add($"{preset.Name} ({peak})");

            var flat = VisualSheet.OverPaper(layer);
            sheets.Add(flat);
            panels.Add(new VisualSheet.Panel($"{preset.Name} — {preset.Settings.Size:0} px, peak {peak}", flat));
        }

        output.WriteLine($"{panels.Count} presets drawn; faint (<40/255): "
            + (faint.Count == 0 ? "none" : string.Join(", ", faint)));

        // Four to a row: one row of thirty is a picture nobody can look at.
        const int perRow = 4;
        for (var i = 0; i < panels.Count; i += perRow)
        {
            var row = panels.Skip(i).Take(perRow).ToArray();
            VisualSheet.Write(
                $"brushes-{i / perRow + 1:00}",
                $"Shipped brushes {i + 1}–{Math.Min(i + perRow, panels.Count)} of {panels.Count}, "
                + "each at the size its preset ships with. Effect brushes are given a bar to work on.",
                row);
        }
        foreach (var s in sheets) s.Dispose();
    }
}
