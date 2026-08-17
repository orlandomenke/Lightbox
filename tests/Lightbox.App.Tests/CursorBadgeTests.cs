using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The cursor's +/− badge (owner's request, 2026-08-17): any press that adds
/// to or carves from something must say which beside the pointer — the select
/// family under Shift/Alt, and the weight brush's Add/Subtract mode beside
/// its ring.
/// </summary>
public class CursorBadgeTests
{
    // ---- the decision -------------------------------------------------------------

    [Fact]
    public void ShiftAddsAndAltSubtractsOnTheSelectFamily()
    {
        Assert.Equal(CursorBadge.Add, CanvasCursor.CombineBadge(ToolId.Select, KeyModifiers.Shift));
        Assert.Equal(CursorBadge.Subtract, CanvasCursor.CombineBadge(ToolId.Select, KeyModifiers.Alt));
        Assert.Equal(CursorBadge.None, CanvasCursor.CombineBadge(ToolId.Select, KeyModifiers.None));
        // Both down: the combine itself prefers add, so the badge must too.
        Assert.Equal(CursorBadge.Add,
            CanvasCursor.CombineBadge(ToolId.Select, KeyModifiers.Shift | KeyModifiers.Alt));
    }

    [Fact]
    public void ToolsThatIgnoreTheModifierShowNoBadge()
    {
        // A + on a tool that does not branch on Shift would be inventing
        // behaviour — the same rule Effective() keeps for cursor kinds.
        foreach (var tool in new[] { ToolId.Brush, ToolId.Eraser, ToolId.Fill, ToolId.Move, ToolId.Bone })
        {
            Assert.Equal(CursorBadge.None, CanvasCursor.CombineBadge(tool, KeyModifiers.Shift));
            Assert.Equal(CursorBadge.None, CanvasCursor.CombineBadge(tool, KeyModifiers.Alt));
        }
    }

    // ---- the view model's answer ----------------------------------------------------

    private static MainViewModel Rigged()
    {
        var vm = new MainViewModel(artist: null);
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        return vm;
    }

    [AvaloniaFact]
    public void TheArmedWeightBrushWearsItsModeAsTheBadge()
    {
        var vm = Rigged();
        vm.SelectToolCommand.Execute(ToolId.Bone);
        vm.WeightPainting = true;

        vm.WeightBrushMode = WeightBrushMode.Add;
        Assert.Equal(CursorBadge.Add, vm.PointerBadge);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);
        vm.WeightBrushMode = WeightBrushMode.Subtract;
        Assert.Equal(CursorBadge.Subtract, vm.PointerBadge);
        Assert.Contains(nameof(MainViewModel.PointerBadge), fired);

        // Smooth neither adds nor carves; a badge would claim it does.
        vm.WeightBrushMode = WeightBrushMode.Smooth;
        Assert.Equal(CursorBadge.None, vm.PointerBadge);
    }

    [AvaloniaFact]
    public void AHeldShiftBadgesTheSelectToolWithoutAPointerMove()
    {
        var vm = Rigged();
        vm.SelectToolCommand.Execute(ToolId.Select);

        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);
        vm.ApplyHeldModifiers(KeyModifiers.Shift);

        Assert.Equal(CursorBadge.Add, vm.PointerBadge);
        Assert.Contains(nameof(MainViewModel.PointerBadge), fired);

        vm.ApplyHeldModifiers(KeyModifiers.None);
        Assert.Equal(CursorBadge.None, vm.PointerBadge);
    }

    [AvaloniaFact]
    public void ArmingAWeightModeIsOneCallFromAnyTool()
    {
        var vm = Rigged();
        vm.SelectToolCommand.Execute(ToolId.Brush);

        vm.ArmWeightBrush(WeightBrushMode.Subtract);

        Assert.Equal(ToolId.Bone, vm.ActiveTool);
        Assert.True(vm.WeightPainting);
        Assert.Equal(WeightBrushMode.Subtract, vm.WeightBrushMode);
    }

    // ---- the ring the badge rides (B253) --------------------------------------------

    [AvaloniaFact]
    public void TheArmedWeightBrushRingIsItsOwnRadiusNotThePaintBrushs()
    {
        var vm = Rigged();
        vm.BrushSize = 80;
        vm.SelectToolCommand.Execute(ToolId.Bone);
        vm.WeightPainting = true;
        vm.WeightBrushRadius = 24;

        Assert.Equal(48, vm.BrushCursorDiameter, 3);
        Assert.Null(vm.BrushCursorTipId);
        Assert.Equal(1, vm.BrushCursorRoundness, 6);
        Assert.Equal(0, vm.BrushCursorAngle, 6);

        // Disarmed, the paint brush owns the ring again.
        vm.WeightPainting = false;
        Assert.NotEqual(48, vm.BrushCursorDiameter, 3);
    }

    // ---- the glyph itself -----------------------------------------------------------

    [Fact]
    public void PlusAndMinusReadDifferentlyInPixels()
    {
        using var bmp = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        CursorBadgePainter.Draw(canvas, 16, 32, CursorBadge.Add);
        CursorBadgePainter.Draw(canvas, 48, 32, CursorBadge.Subtract);
        canvas.Flush();

        static int Lum(SKColor c) => (c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000;

        // Both carry the horizontal stroke…
        Assert.True(Lum(bmp.GetPixel(16, 32)) < 120, "the plus has no horizontal stroke");
        Assert.True(Lum(bmp.GetPixel(48, 32)) < 120, "the minus has no horizontal stroke");
        // …and only the plus the vertical one.
        Assert.True(Lum(bmp.GetPixel(16, 35)) < 120, "the plus has no vertical stroke");
        Assert.True(Lum(bmp.GetPixel(48, 35)) > 200, "the minus grew a vertical stroke — it reads as a plus");
    }

    [Fact]
    public void NoneDrawsNothing()
    {
        using var bmp = new SKBitmap(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        CursorBadgePainter.Draw(canvas, 16, 16, CursorBadge.None);
        canvas.Flush();
        Assert.Equal(SKColors.White, bmp.GetPixel(16, 16));
    }
}
