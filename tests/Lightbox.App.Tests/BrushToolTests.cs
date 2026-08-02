using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

public class HiddenLayerTests
{
    [AvaloniaFact]
    public void PaintingOnAHiddenLayer_IsBlocked_UntilVisibleAgain()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.LayerRows[0].Visible = false; // hide via the docker/timeline row

        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(50, 50, 0.5);
        vm.EndStroke();

        var frame = vm.PaintedCel();
        Assert.Empty(frame.Strokes);
        Assert.Contains("hidden", vm.AiStatus);

        vm.LayerRows[0].Visible = true;
        vm.BeginStroke(10, 10, 0.5);
        vm.EndStroke();
        Assert.Single(frame.Strokes);
    }

    [AvaloniaFact]
    public void AiDraw_RefusesAHiddenLayer()
    {
        var fake = new FakeArtist();
        var vm = new MainViewModel(fake) { AiPrompt = "a circle" };
        vm.LayerRows[0].Visible = false;

        vm.AiDrawCommand.Execute(null);

        Assert.Null(fake.LastDrawRequest); // never reached the artist
        Assert.Contains("hidden", vm.AiStatus);
    }
}

/// <remarks>
/// In the <c>BrushState</c> collection because it sets brush parameters, and
/// those live in a process-wide store — running beside a test that assumes
/// defaults hands it this one’s brush.
/// </remarks>
[Collection("BrushState")]
public class BrushPresetTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void SelectingAPreset_AppliesItsSettings_ToTheStrokeRecord()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var restore = vm.SelectedBrushPreset;
        var watercolor = vm.BrushPresetChoices.First(p => p.Name == "Watercolor");
        vm.SelectedBrushPreset = watercolor;

        Assert.False(vm.IsEraser);
        Assert.Equal(watercolor.Settings.Size, vm.BrushSize);

        vm.BeginStroke(10, 10, 0.5);
        vm.MoveStroke(40, 40, 0.5);
        vm.EndStroke();

        var stroke = (vm.PaintedCel()).Strokes[0];
        Assert.Equal(watercolor.Settings.WetEdge, stroke.Brush.WetEdge);
        Assert.Equal(watercolor.Settings.Granulation, stroke.Brush.Granulation);
        Assert.Equal(watercolor.Settings.Flow, stroke.Brush.Flow);

        // Watercolor carries its character in the medium rather than in the
        // flat texture knobs, so those two assertions above are now 0 == 0.
        // This is the part that would actually notice the preset being lost.
        Assert.Equal(MediumKind.Watercolour, stroke.Brush.Medium.Kind);
        Assert.Equal(watercolor.Settings.Medium.Wetness, stroke.Brush.Medium.Wetness);
        Assert.Equal(watercolor.Settings.Medium.EdgePull, stroke.Brush.Medium.EdgePull);
        Assert.Equal(watercolor.Settings.Medium.Paper, stroke.Brush.Medium.Paper);

        vm.SelectedBrushPreset = restore;
    }

    [AvaloniaFact]
    public void EachSimulatedMedium_ReachesTheStrokeRecord_WithItsOwnPhysics()
    {
        // Four presets that must differ by physics, not just by colour. If two
        // of them ever collapse onto the same numbers, the distinction the
        // artist picked between them has quietly stopped existing.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var seen = new List<(MediumKind Kind, double Wetness, double Viscosity, double Hiding)>();
        // The brush store is shared across tests, and a medium brush now
        // actually changes how strokes render — leaving one selected quietly
        // changes what later tests paint with.
        var restore = vm.SelectedBrushPreset;

        foreach (var name in new[] { "Watercolor", "Gouache", "Oil", "Ink wash" })
        {
            vm.SelectedBrushPreset = vm.BrushPresetChoices.First(p => p.Name == name);
            vm.BeginStroke(10, 10, 0.7);
            vm.MoveStroke(40, 40, 0.7);
            vm.EndStroke();

            var brush = (vm.PaintedCel()).Strokes[^1].Brush;
            Assert.NotEqual(MediumKind.None, brush.Medium.Kind);
            seen.Add((brush.Medium.Kind, brush.Medium.Wetness, brush.Medium.Viscosity, brush.Medium.Hiding));
        }

        Assert.Equal(4, seen.Select(m => m.Kind).Distinct().Count());
        Assert.Equal(4, seen.Distinct().Count());

        // And the physics must be the right way round: watercolour is the
        // wettest and least hiding, oil the most viscous and most hiding.
        var water = seen.Single(m => m.Kind == MediumKind.Watercolour);
        var oil = seen.Single(m => m.Kind == MediumKind.Oil);
        Assert.True(water.Wetness > oil.Wetness, "watercolour should be wetter than oil");
        Assert.True(oil.Viscosity > water.Viscosity, "oil should be more viscous than watercolour");
        Assert.True(oil.Hiding > water.Hiding, "oil should hide more than watercolour");

        vm.SelectedBrushPreset = restore;
    }

    [AvaloniaFact]
    public void BrushAndEraser_KeepSeparateConfigurations()
    {
        var vm = new MainViewModel(null);
        vm.IsEraser = false;
        vm.BrushSize = 33;
        vm.IsEraser = true;
        vm.BrushSize = 55;

        vm.IsEraser = false; // "B"
        Assert.Equal(33, vm.BrushSize);
        vm.IsEraser = true; // "E"
        Assert.Equal(55, vm.BrushSize);
    }

    [AvaloniaFact]
    public void LastConfiguredBrush_SurvivesANewSession()
    {
        var store = Path.Combine(Path.GetTempPath(), $"lightbox-brush-persist-{Guid.NewGuid():N}.json");
        var before = MainViewModel.BrushStorePath;
        try
        {
            MainViewModel.BrushStorePath = store;
            var vm = new MainViewModel(null);
            var ink = vm.BrushPresetChoices.First(p => p.Name == "Ink");
            vm.SelectedBrushPreset = ink;
            vm.BrushSize = 17; // tweak on top of the preset

            var vm2 = new MainViewModel(null); // "restart"
            Assert.Equal("Ink", vm2.SelectedBrushPreset?.Name);
            Assert.Equal(17, vm2.BrushSize); // the tweak came back, not the preset default
        }
        finally
        {
            MainViewModel.BrushStorePath = before;
            File.Delete(store);
        }
    }

    [AvaloniaFact]
    public void SaveCurrentAsPreset_PersistsUserPresets()
    {
        var store = Path.Combine(Path.GetTempPath(), $"lightbox-brush-save-{Guid.NewGuid():N}.json");
        var before = MainViewModel.BrushStorePath;
        try
        {
            MainViewModel.BrushStorePath = store;
            var vm = new MainViewModel(null);
            vm.BrushSize = 42;
            vm.BrushWetEdge = 0.33;
            var preset = vm.SaveCurrentAsPreset("My wash");

            var vm2 = new MainViewModel(null);
            var restored = vm2.BrushPresetChoices.First(p => p.Name == "My wash");
            Assert.Equal(42, restored.Settings.Size);
            Assert.Equal(0.33, restored.Settings.WetEdge, 6);
            Assert.Equal(preset.Id, restored.Id);
        }
        finally
        {
            MainViewModel.BrushStorePath = before;
            File.Delete(store);
        }
    }

    /// <summary>Synthesize a minimal GBR v2 brush file (solid square tip).</summary>
    private static byte[] GbrFixture(int size)
    {
        using var ms = new MemoryStream();
        void BE(uint v)
        {
            Span<byte> b = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b, v);
            ms.Write(b);
        }
        var name = "fixture"u8.ToArray();
        BE((uint)(28 + name.Length + 1));
        BE(2);
        BE((uint)size);
        BE((uint)size);
        BE(1);
        BE(0x47494D50);
        BE(20);
        ms.Write(name);
        ms.WriteByte(0);
        for (var i = 0; i < size * size; i++) ms.WriteByte(255);
        return ms.ToArray();
    }

    [AvaloniaFact]
    public void ImportedBrush_BecomesAPreset_AndItsTipEntersTheDocument()
    {
        var vm = new MainViewModel(null);
        var gbr = GbrFixture(24);
        var (added, failed) = vm.ImportBrushFiles([("square.gbr", gbr)]);

        Assert.Equal(1, added);
        Assert.Equal(0, failed);
        var preset = vm.BrushPresetChoices.Last();
        Assert.Equal("fixture", preset.Name);
        Assert.NotNull(preset.TipPng);
        Assert.NotNull(preset.Settings.TipId);

        vm.SelectedBrushPreset = preset; // first use copies the tip into the document
        Assert.True(vm.Doc.BrushTips.ContainsKey(preset.Settings.TipId!));
        Assert.True(vm.Tabs[0].IsDirty);
    }
}
