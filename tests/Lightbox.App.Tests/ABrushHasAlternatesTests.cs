using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// A brush can name another brush to paint with while Alt is held, and a
/// second for Ctrl+Alt (Q255).
/// </summary>
/// <remarks>
/// <para>
/// Alt at pen-down already had one meaning for every brush - erase with the
/// brush in hand. That is now the alternate of a brush that names none, so
/// nothing changes for Ink or Pencil; a wash names a water brush and a blot
/// instead, which is the owner's working gesture: paint, then lift the edge
/// with a towel, without putting the brush down.
/// </para>
/// <para>
/// <b>Nothing new reaches the record.</b> An alternate stroke is an ordinary
/// stroke of the other brush at the size in hand, so a file drawn this way
/// opens anywhere a file with those two brushes would (invariants 1 and 4).
/// What is new is on the preset, and absent from it unless used.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class ABrushHasAlternatesTests : BrushStateIsolated
{
    private static (MainViewModel Vm, BrushPreset Main, BrushPreset First, BrushPreset Second) Setup()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        var first = new BrushPreset
        {
            Id = "test-water", Name = "Test water",
            Settings = new BrushSettings { Size = 80, Hardness = 0.2, Flow = 0.3, Kind = BrushKind.Blur },
        };
        var second = new BrushPreset
        {
            Id = "test-blot", Name = "Test blot", Tool = ToolKind.Eraser,
            Settings = new BrushSettings { Size = 50, Hardness = 0.6, Opacity = 0.8, Flow = 0.6 },
        };
        var main = new BrushPreset
        {
            Id = "test-wash", Name = "Test wash",
            Settings = new BrushSettings { Size = 30, Hardness = 0.75, Flow = 0.4 },
            Alternate = first.Id, SecondAlternate = second.Id,
        };
        vm.AddPresetsForTest(first, second, main);
        vm.ApplyPreset(vm.BrushPresetChoices.Single(p => p.Id == main.Id));
        return (vm, main, first, second);
    }

    private static Stroke Draw(MainViewModel vm, BrushAlternate alternate)
    {
        // Something to rework: an eraser stroke over bare paper records nothing.
        if (vm.PaintedCel().Strokes.Count == 0)
        {
            var size = vm.BrushSize;
            vm.BrushSize = 120;
            vm.BeginStroke(90, 105, 1);
            vm.MoveStroke(230, 115, 1);
            vm.EndStroke();
            vm.BrushSize = size;
        }
        var before = vm.PaintedCel().Strokes.Count;
        vm.BeginStroke(100, 100, 0.7, alternate, joinFromLast: false);
        vm.MoveStroke(160, 120, 0.7);
        vm.MoveStroke(220, 110, 0.7);
        vm.EndStroke();
        Assert.True(vm.PaintedCel().Strokes.Count == before + 1, $"holding {alternate}: no stroke was recorded");
        return vm.PaintedCel().Strokes[^1];
    }

    [AvaloniaFact]
    public void AltPaintsWithTheBrushTheBrushNames_AtTheSizeInHand()
    {
        var (vm, main, first, _) = Setup();
        vm.BrushSize = 44;

        var plain = Draw(vm, BrushAlternate.None);
        Assert.Equal(ToolKind.Brush, plain.Tool);
        Assert.Equal(BrushKind.Paint, plain.Brush.Kind);

        var alt = Draw(vm, BrushAlternate.First);
        Assert.Equal(ToolKind.Brush, alt.Tool);
        Assert.Equal(BrushKind.Blur, alt.Brush.Kind);
        Assert.Equal(first.Settings.Hardness, alt.Brush.Hardness);
        Assert.Equal(first.Settings.Flow, alt.Brush.Flow);
        // The size is the one in hand, not the one the other brush was saved at:
        // the alternate works on what this brush just laid.
        Assert.Equal(44, alt.Brush.Size);

        // And the brush in hand is still the brush in hand.
        Assert.Equal(main.Id, vm.SelectedBrushPreset?.Id);
        Assert.Equal(BrushKind.Paint, vm.CurrentToolSettingsForTest.Kind);
        Assert.Equal(44, vm.BrushSize);
        Assert.Equal(BrushKind.Paint, Draw(vm, BrushAlternate.None).Brush.Kind);
    }

    [AvaloniaFact]
    public void CtrlAltPaintsWithTheSecond_AndAnEraserAlternateErases()
    {
        var (vm, _, _, second) = Setup();
        Assert.True(vm.HasBrushAlternate(BrushAlternate.Second));
        var blot = Draw(vm, BrushAlternate.Second);
        Assert.Equal(ToolKind.Eraser, blot.Tool);
        Assert.Equal(second.Settings.Opacity, blot.Brush.Opacity);
        Assert.Equal(second.Settings.Hardness, blot.Brush.Hardness);
        Assert.Equal(vm.BrushSize, blot.Brush.Size);
    }

    [AvaloniaFact]
    public void ABrushThatNamesNoneErasesOnAlt_AndHasNoSecond()
    {
        // What Alt has always done, and still does for every brush that says
        // nothing: erase with the brush in hand, its size, shape and dynamics.
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ApplyPreset(vm.BrushPresetChoices.Single(p => p.Id == "builtin-ink"));
        var ink = vm.CurrentToolSettingsForTest.Clone();

        Assert.True(vm.HasBrushAlternate(BrushAlternate.First), "Alt always has a meaning: erase with this brush");
        var alt = Draw(vm, BrushAlternate.First);
        Assert.Equal(ToolKind.Eraser, alt.Tool);
        Assert.Equal(ink.Size, alt.Brush.Size);
        Assert.Equal(ink.Hardness, alt.Brush.Hardness);

        // Ctrl+Alt stays the eyedropper's (Ctrl), so no stroke starts.
        Assert.False(vm.HasBrushAlternate(BrushAlternate.Second));
        var before = vm.PaintedCel().Strokes.Count;
        vm.BeginStroke(100, 100, 0.7, BrushAlternate.Second, joinFromLast: false);
        vm.MoveStroke(160, 120, 0.7);
        vm.EndStroke();
        Assert.Equal(before, vm.PaintedCel().Strokes.Count);
    }

    [AvaloniaFact]
    public void AnAlternateThatIsGoneFallsBackToWhatTheKeyAlwaysDid()
    {
        // The other brush was deleted, or the preset came from a machine that
        // has it and this one does not. The key must not go dead.
        var (vm, main, _, _) = Setup();
        main.Alternate = "no-such-brush";
        main.SecondAlternate = "nor-this-one";
        Assert.Equal(ToolKind.Eraser, Draw(vm, BrushAlternate.First).Tool);
        Assert.False(vm.HasBrushAlternate(BrushAlternate.Second));
    }

    [AvaloniaFact]
    public void TheEraserToolHasNoAlternates()
    {
        // Alt with the eraser in hand has never meant anything but erasing.
        var (vm, _, _, _) = Setup();
        Draw(vm, BrushAlternate.None);
        vm.IsEraser = true;
        Assert.Equal(ToolKind.Eraser, Draw(vm, BrushAlternate.First).Tool);
        Assert.False(vm.HasBrushAlternate(BrushAlternate.Second));
        vm.IsEraser = false;
    }

    /// <summary>
    /// What the canvas is told at a press, and what the pointer shows before
    /// it: Ctrl+Alt is the eyedropper's unless the brush names a second
    /// alternate, and the cursor must not promise one and do the other.
    /// </summary>
    [AvaloniaFact]
    public void ThePressAndThePointerAgreeAboutCtrlAlt()
    {
        var (vm, _, _, _) = Setup();
        Assert.Equal((true, false), vm.BrushAlternateAtPress(BrushAlternate.None));
        Assert.Equal((true, false), vm.BrushAlternateAtPress(BrushAlternate.First));   // a blur: paints
        Assert.Equal((true, true), vm.BrushAlternateAtPress(BrushAlternate.Second));   // a blot: erases

        var both = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt;
        var paints = new Rendering.CanvasTarget(CtrlAltPaints: true);
        Assert.Equal(ToolId.Brush, Rendering.CanvasCursor.Effective(ToolId.Brush, both, paints));
        // Ctrl alone is still the eyedropper, with or without a second alternate.
        Assert.Equal(ToolId.Picker, Rendering.CanvasCursor.Effective(ToolId.Brush, Avalonia.Input.KeyModifiers.Control, paints));
        Assert.Equal(ToolId.Picker, Rendering.CanvasCursor.Effective(ToolId.Brush, both, default));
        Assert.Equal(ToolId.Picker, Rendering.CanvasCursor.Effective(ToolId.Fill, both, paints));

        vm.ApplyPreset(vm.BrushPresetChoices.Single(p => p.Id == "builtin-ink"));
        Assert.Equal((true, true), vm.BrushAlternateAtPress(BrushAlternate.First));    // erases with the brush in hand
        Assert.Equal((false, false), vm.BrushAlternateAtPress(BrushAlternate.Second)); // the eyedropper's
    }

    /// <summary>
    /// Changing a shipped brush's alternate shadows it, like a tag; a shadow
    /// that says nothing inherits what the brush ships with; and the choice
    /// travels with a brush saved or overwritten from it.
    /// </summary>
    [AvaloniaFact]
    public void TheChoiceIsKeptThroughShadowsAndSaves()
    {
        var vm = VmLayers.PaperVm();
        var watercolor = vm.BrushPresetChoices.Single(p => p.Id == "builtin-watercolor-wet");
        vm.ApplyPreset(watercolor);
        Assert.True(vm.HasBrushAlternate(BrushAlternate.Second), "Watercolor ships with the blot on Ctrl+Alt");

        // Tagging shadows the brush; the shadow still lifts with the towel.
        vm.SetPresetTags(vm.SelectedBrushPreset!, ["washes"]);
        Assert.False(vm.SelectedBrushPreset!.IsBuiltIn && ReferenceEquals(vm.SelectedBrushPreset, watercolor));
        Assert.Equal("builtin-blot", MainViewModel.AlternateIdOf(vm.SelectedBrushPreset, BrushAlternate.Second));

        // Naming another on Alt, and none on Ctrl+Alt, both stick.
        Assert.True(vm.SetPresetAlternate(vm.SelectedBrushPreset!, BrushAlternate.First, "builtin-blur"));
        Assert.True(vm.SetPresetAlternate(vm.SelectedBrushPreset!, BrushAlternate.Second, null));
        Assert.Equal("builtin-blur", MainViewModel.AlternateIdOf(vm.SelectedBrushPreset, BrushAlternate.First));
        Assert.Null(MainViewModel.AlternateIdOf(vm.SelectedBrushPreset, BrushAlternate.Second));
        Assert.False(vm.HasBrushAlternate(BrushAlternate.Second));

        // Overwriting the brush with new settings keeps them, and so does saving a new one from it.
        vm.BrushSize = 77;
        Assert.True(vm.UpdateSelectedPreset());
        Assert.Equal("builtin-blur", MainViewModel.AlternateIdOf(vm.SelectedBrushPreset, BrushAlternate.First));
        Assert.Null(MainViewModel.AlternateIdOf(vm.SelectedBrushPreset, BrushAlternate.Second));
        var saved = vm.SaveCurrentAsPreset("My wash");
        Assert.Equal("builtin-blur", saved.Alternate);

        // A brush cannot name itself, and going back to what ships writes nothing.
        Assert.False(vm.SetPresetAlternate(saved, BrushAlternate.First, saved.Id));
        var mine = vm.BrushPresetChoices.Single(p => p.Id == "builtin-watercolor-wet");
        vm.SetPresetAlternate(mine, BrushAlternate.Second, "builtin-blot");
        Assert.Null(mine.SecondAlternate);
        Assert.Equal("builtin-blot", MainViewModel.AlternateIdOf(mine, BrushAlternate.Second));
    }

    /// <summary>
    /// An alternate is put on the way picking it would put it on (review):
    /// anti-aliasing is a preference stamped per stroke and never a preset's,
    /// and a tip the other brush carries is in the document before a stroke
    /// names it - but only then, because asking what a key would do must not
    /// edit the document.
    /// </summary>
    [AvaloniaFact]
    public void AnAlternateStrokeIsMadeLikeAnyStrokeOfThatBrush()
    {
        var (vm, main, first, _) = Setup();
        using var tip = new SkiaSharp.SKBitmap(8, 8);
        tip.Erase(SkiaSharp.SKColors.White);
        using var png = tip.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        first.TipPng = Convert.ToBase64String(png.ToArray());
        first.Settings.Kind = BrushKind.Paint;
        first.Settings.TipId = "tip_test_alternate";

        var antiAliasing = vm.AntiAliasing;
        vm.AntiAliasing = false;
        try
        {
            // Hovering with the key held, and the canvas asking at the press, change nothing.
            _ = vm.HasBrushAlternate(BrushAlternate.First);
            _ = vm.BrushAlternateAtPress(BrushAlternate.First);
            Assert.False(vm.Doc.BrushTips.ContainsKey("tip_test_alternate"));

            var stroke = Draw(vm, BrushAlternate.First);
            Assert.Equal("tip_test_alternate", stroke.Brush.TipId);
            Assert.True(vm.Doc.BrushTips.ContainsKey("tip_test_alternate"), "the stroke names a tip the document does not have");
            Assert.False(stroke.Brush.AntiAlias, "the alternate took its own anti-aliasing over the preference");
            Assert.True(first.Settings.AntiAlias, "the preference was written into the other brush");
        }
        finally
        {
            vm.AntiAliasing = antiAliasing;
        }
        Assert.Equal(main.Id, vm.SelectedBrushPreset?.Id);
    }

    /// <summary>
    /// The Blot picked from the list is the eraser, with the Blot's settings,
    /// and the brush is left as it was. Picking an eraser preset used to write
    /// its settings to the brush and erase with the stock eraser.
    /// </summary>
    [AvaloniaFact]
    public void PickingTheBlotGivesTheEraserTheBlot()
    {
        var vm = VmLayers.PaperVm();
        vm.ApplyPreset(vm.BrushPresetChoices.Single(p => p.Id == "builtin-ink"));
        var ink = vm.CurrentToolSettingsForTest.Clone();
        var blot = vm.BrushPresetChoices.Single(p => p.Id == "builtin-blot");

        vm.ApplyPreset(blot);
        Assert.True(vm.IsEraser);
        Assert.Equal(blot.Settings.Opacity, vm.CurrentToolSettingsForTest.Opacity);
        Assert.Equal(blot.Settings.Hardness, vm.CurrentToolSettingsForTest.Hardness);
        Assert.Equal(blot.Settings.TextureSurface, vm.CurrentToolSettingsForTest.TextureSurface);

        vm.IsEraser = false;
        Assert.Equal(ink.Size, vm.CurrentToolSettingsForTest.Size);
        Assert.Equal(ink.Hardness, vm.CurrentToolSettingsForTest.Hardness);
        Assert.Null(vm.CurrentToolSettingsForTest.TextureSurface);
    }

    /// <summary>Absent from a preset that names none (the optional-settings rule).</summary>
    [Fact]
    public void APresetThatNamesNoAlternateWritesNoKey()
    {
        var plain = PresetStore.SerializeForTest(new BrushPreset { Id = "p", Name = "Plain" });
        Assert.DoesNotContain("lternate", plain);

        var named = PresetStore.SerializeForTest(new BrushPreset { Id = "p", Name = "Wash", Alternate = "a", SecondAlternate = "b" });
        var back = PresetStore.DeserializeForTest(named);
        Assert.Equal("a", back.Alternate);
        Assert.Equal("b", back.SecondAlternate);
        using var doc = JsonDocument.Parse(named);
        Assert.True(doc.RootElement.TryGetProperty("alternate", out _));
        Assert.True(doc.RootElement.TryGetProperty("secondAlternate", out _));
    }

    /// <summary>The shipped washes lift with a towel on Ctrl+Alt (Q255); the blot itself is in the picker.</summary>
    [Fact]
    public void TheWashesNameTheBlot_AndTheBlotIsAnEraserThatCannotTakeEverything()
    {
        var shipped = BuiltInPresets.Create();
        var blot = shipped.Single(p => p.Id == "builtin-blot");
        Assert.Equal(ToolKind.Eraser, blot.Tool);
        Assert.True(blot.Settings.Opacity is > 0.5 and < 1, "the stroke's opacity is the most a dab can lift");
        Assert.True(blot.Settings.PressureFlowGamma > 0, "pressing harder lifts more");
        Assert.NotNull(blot.Settings.TextureSurface);

        foreach (var id in new[] { "builtin-watercolor-wet", "builtin-ink-wash" })
        {
            Assert.Equal("builtin-blot", shipped.Single(p => p.Id == id).SecondAlternate);
            Assert.Equal("builtin-water", shipped.Single(p => p.Id == id).Alternate);
        }
        // Gouache works its paint again with the Damp brush, and lifts with the same Blot.
        var gouache = shipped.Single(p => p.Id == "builtin-gouache-body");
        Assert.Equal("builtin-damp-brush", gouache.Alternate);
        Assert.Equal("builtin-blot", gouache.SecondAlternate);
        var damp = shipped.Single(p => p.Id == "builtin-damp-brush").Settings;
        Assert.Equal(BrushKind.Smudge, damp.Kind);
        Assert.Equal(SmudgeMode.Dulling, damp.SmudgeMode);
        Assert.Equal(0, damp.ColorRate);
        Assert.Equal(gouache.Settings.TipId, damp.TipId);
        Assert.True(damp.Flow <= 0.35, "the damp brush was chosen for being gentle");

        // Everything else keeps Alt as it was.
        Assert.Null(shipped.Single(p => p.Id == "builtin-ink").Alternate);
        Assert.Null(shipped.Single(p => p.Id == "builtin-pencil").SecondAlternate);
    }
}
