using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// What the tool options bar's effect control is wired to.
/// </summary>
/// <remarks>
/// <para>
/// <b>B90.</b> The bar shows one control for an effect brush, and for a smudge it
/// used to read and write <see cref="BrushSettings.SmudgeLength"/> under the label
/// "Length". That put a drag distance where the artist reaches for strength, and
/// left flow — the value <c>BrushEngine.StampSmudge</c> actually hands to
/// <c>LerpDab</c> as the blending weight — unreachable from the bar for the two
/// brushes whose whole job is blending.
/// </para>
/// <para>
/// The manual was already right (<c>docs/manual/04-brushes.md</c>: flow on a smudge
/// "is how hard each dab <i>pulls</i>"), so these tests pin the app to its own
/// documentation rather than to a new opinion.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class BrushParameterTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 220, H = 90;

    private static MainViewModel Vm() => new(null);

    private static MainViewModel WithPreset(string id)
    {
        var vm = Vm();
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == id));
        return vm;
    }

    [AvaloniaTheory]
    [InlineData("builtin-smudge")]
    [InlineData("builtin-blender")]
    public void SmudgeAndBlenderUseStrengthNotLength(string presetId)
    {
        var vm = WithPreset(presetId);
        Assert.Equal(BrushKind.Smudge, vm.CurrentToolSettingsForTest.Kind);

        var lengthBefore = vm.BrushSmudgeLength;
        var flowBefore = vm.BrushFlow;
        Assert.Equal(flowBefore, vm.EffectStrength, 1e-9);

        vm.EffectStrength = 0.42;

        // The bar's control moved flow, and left smudge length exactly where the
        // preset put it. Both halves matter: the second is what says the parameter
        // was re-pointed rather than merely relabelled.
        Assert.Equal(0.42, vm.BrushFlow, 1e-9);
        Assert.Equal(0.42, vm.CurrentToolSettingsForTest.Flow, 1e-9);
        Assert.Equal(lengthBefore, vm.BrushSmudgeLength, 1e-9);

        output.WriteLine(
            $"{presetId}: flow {flowBefore:F2} -> {vm.BrushFlow:F2}, "
            + $"smudge length {lengthBefore:F2} -> {vm.BrushSmudgeLength:F2}");
    }

    /// <summary>
    /// A blur's strength was already flow, and must not have moved.
    /// </summary>
    [AvaloniaFact]
    public void ABlurStrengthIsStillItsFlow()
    {
        var vm = WithPreset("builtin-blur");
        vm.EffectStrength = 0.5;
        Assert.Equal(0.5, vm.BrushFlow, 1e-9);
    }

    /// <summary>
    /// Smudge length is not gone — it keeps its own row on the ⚙ → Effects page.
    /// </summary>
    /// <remarks>
    /// The ledger's original anchor for this was <c>LengthIsNotASmudgeParameter</c>,
    /// which asserts something we decided against: length is a real control (Krita
    /// has the same one) and deleting it would repaint the shipped Blender, which
    /// ships at <c>SmudgeLength 0.35</c>. What B90 is actually about is <i>which
    /// surface</i> carries it, so that is what this checks.
    /// </remarks>
    [AvaloniaFact]
    public void LengthStaysOnTheEffectsPageRatherThanTheBar()
    {
        var vm = WithPreset("builtin-blender");

        Assert.Equal(0.35, vm.BrushSmudgeLength, 1e-9);

        vm.BrushSmudgeLength = 0.8;
        Assert.Equal(0.8, vm.CurrentToolSettingsForTest.SmudgeLength, 1e-9);

        // And it is still independent of the bar in the other direction.
        vm.EffectStrength = 0.2;
        Assert.Equal(0.8, vm.BrushSmudgeLength, 1e-9);
    }

    /// <summary>
    /// The control has to reach pixels, not just a property.
    /// </summary>
    /// <remarks>
    /// <b>Measured across the stroke, not along it</b>, per the saturation note in
    /// <c>CLAUDE.md</c>: at this spacing dabs overlap about ten deep, so alpha down
    /// the middle of a stroke saturates and two very different flows read within a
    /// hair of each other. What is measured here is how far a smudge drags a hard
    /// colour boundary sideways, which does not accumulate the same way — and both
    /// numbers are printed so "the assertion passed" cannot hide "0.93 versus 1.00".
    /// </remarks>
    [AvaloniaFact]
    public void StrengthControlsBlendingIntensity()
    {
        var weak = Displaced(0.02);
        var strong = Displaced(0.30);

        output.WriteLine($"boundary displacement: flow 0.02 -> {weak} px, flow 0.30 -> {strong} px");

        Assert.True(weak > 0, "the gentle smudge did nothing at all — this would pass on a dead control");
        Assert.True(
            strong > weak * 1.5,
            $"strength barely changed the mark: {weak} px at flow 0.02 against {strong} px at flow 0.30");
    }

    /// <summary>
    /// How far the black/white boundary of a block moves when smudged across it.
    /// </summary>
    private static int Displaced(double flow)
    {
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [.. Enumerable.Range(0, 16).Select(i => new StrokePoint(60 + i * 6, 45, 1))],
            Brush = new BrushSettings
            {
                Kind = BrushKind.Smudge, Size = 18, Hardness = 0.5, Flow = flow, Spacing = 0.1,
                SmudgeLength = 0.5, SmudgeRadius = 0.5,
            },
        };

        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            canvas.DrawRect(new SKRect(0, 0, 110, H), paint);
            canvas.Flush();
            BrushEngine.StampStroke(canvas, stroke, info, bmp);
            canvas.Flush();
        }

        // The last column past the original edge that still carries real coverage.
        var reach = 0;
        for (var x = 110; x < W; x++)
        {
            if (bmp.GetPixel(x, 45).Alpha > 16) reach = x - 110;
        }
        return reach;
    }
}
