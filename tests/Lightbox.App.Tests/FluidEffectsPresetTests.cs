using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The effects library, driven the way the window drives it.
/// </summary>
/// <remarks>
/// <b>Every test here writes to a temporary library</b>, never the artist's own
/// — a suite that appends to <c>effects.json</c> would leave a developer's real
/// shelf full of test fire, and would pass either way.
/// </remarks>
public sealed class FluidEffectsPresetTests(Xunit.ITestOutputHelper output) : IDisposable
{
    private readonly string _library =
        Path.Combine(Path.GetTempPath(), $"lightbox-effects-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_library)) File.Delete(_library);
    }

    private (FluidEffectsViewModel Model, MainViewModel Vm) AnExplosion()
    {
        var vm = new MainViewModel(null);
        var model = new FluidEffectsViewModel(vm, _library);

        var flash = model.NewElement("fire").Element!;
        flash.Name = "flash";
        model.NewGroup("Explosion");

        var smoke = model.NewElement("smoke").Element!;
        smoke.Name = "smoke";
        smoke.FirstFrame = 4;
        smoke.OriginX = 30;
        model.AddToGroup();

        return (model, vm);
    }

    [AvaloniaFact]
    public void An_Effect_Can_Be_Kept_And_Made_Again_In_Another_Document()
    {
        var (model, _) = AnExplosion();

        var kept = model.SavePreset("Big bang");
        Assert.NotNull(kept);
        Assert.True(File.Exists(_library), "the library was not written");

        // A fresh document, and a fresh view model reading the same shelf.
        var elsewhereVm = new MainViewModel(null);
        var elsewhere = new FluidEffectsViewModel(elsewhereVm, _library);
        Assert.Contains(elsewhere.Presets, p => p.Label == "Big bang");
        elsewhere.SelectedPreset = elsewhere.Presets.Single(p => p.Label == "Big bang");

        var made = elsewhere.UsePreset();

        Assert.NotNull(made);
        var members = SimGroupOps.Members(elsewhereVm.Doc, made!.Group!);
        output.WriteLine($"made {members.Count} elements: {string.Join(", ", members.Select(m => m.Name))}");
        Assert.Equal(2, members.Count);
        Assert.Equal(["flash", "smoke"], members.Select(m => m.Name));
        // The spacing that makes it one event survived the trip.
        Assert.Equal(4, members[1].FirstFrame - members[0].FirstFrame);
        Assert.Equal(30, members[1].OriginX - members[0].OriginX);
    }

    [AvaloniaFact]
    public void A_Preset_Stores_Parameters_Rather_Than_Drawings()
    {
        var (model, _) = AnExplosion();
        model.SavePreset("Big bang");

        var json = File.ReadAllText(_library);
        output.WriteLine($"{json.Length} bytes on the shelf");

        // Parameters are there…
        Assert.Contains("\"buoyancy\"", json);
        Assert.Contains("\"emitters\"", json);
        // …and nothing that was drawn is. A preset that carried strokes would be
        // a symbol wearing the wrong name, and would not re-simulate.
        Assert.DoesNotContain("\"strokes\"", json);
        Assert.DoesNotContain("\"pngBase64\"", json);
        Assert.DoesNotContain("\"points\"", json);
    }

    [AvaloniaFact]
    public void Using_A_Preset_Twice_Makes_Two_Effects_That_Move_Apart()
    {
        var (model, vm) = AnExplosion();
        model.SavePreset("Big bang");
        model.SelectedPreset = model.Presets.Single();

        var first = model.UsePreset()!;
        model.GroupOriginX = 900;
        var second = model.UsePreset()!;

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(3, vm.Doc.SimGroups!.Count);
        Assert.Equal(6, vm.Doc.Sims!.Count);

        // The second landed where the first had been moved to, and moving it
        // again leaves the first alone.
        Assert.Equal(900, SimGroupOps.Members(vm.Doc, second.Group!).Min(e => e.OriginX));
        model.GroupOriginX = 1500;
        Assert.Equal(900, SimGroupOps.Members(vm.Doc, first.Group!).Min(e => e.OriginX));
    }

    /// <summary>
    /// A preset points at layers by name, and says so when the name is not
    /// there — an emitter pointed at a missing layer emits nothing at all, which
    /// is the kind of thing that looks fine until somebody bakes.
    /// </summary>
    [AvaloniaFact]
    public void A_Missing_Layer_Is_Reported_Rather_Than_Left_To_Be_Discovered()
    {
        var (model, vm) = AnExplosion();
        var cloak = new Layer { Name = "cloak" };
        vm.Doc.Scene.Layers.Add(cloak);
        model.Element!.Emitters[0].MaskLayerId = cloak.Id;
        model.SavePreset("Burning cloak");

        var bare = new FluidEffectsViewModel(new MainViewModel(null), _library);
        bare.SelectedPreset = bare.Presets.Single();
        Assert.Equal(["cloak"], bare.MissingLayersForSelectedPreset);

        bare.UsePreset();
        output.WriteLine(bare.Status);
        Assert.Contains("cloak", bare.Status);
        Assert.Contains("emitting from nothing", bare.Status);
    }

    [AvaloniaFact]
    public void A_Preset_Reconnects_To_A_Layer_Of_The_Same_Name()
    {
        var (model, vm) = AnExplosion();
        var cloak = new Layer { Name = "cloak" };
        vm.Doc.Scene.Layers.Add(cloak);
        model.Element!.Emitters[0].MaskLayerId = cloak.Id;
        model.SavePreset("Burning cloak");

        var elsewhere = new MainViewModel(null);
        var theirCloak = new Layer { Name = "cloak" };
        elsewhere.Doc.Scene.Layers.Add(theirCloak);
        var there = new FluidEffectsViewModel(elsewhere, _library);
        there.SelectedPreset = there.Presets.Single();

        Assert.Empty(there.MissingLayersForSelectedPreset);
        var made = there.UsePreset()!;

        var reconnected = SimGroupOps.Members(elsewhere.Doc, made.Group!)
            .SelectMany(e => e.Emitters).Where(e => e.MaskLayerId is not null).ToList();
        Assert.NotEmpty(reconnected);
        Assert.All(reconnected, e => Assert.Equal(theirCloak.Id, e.MaskLayerId));
        Assert.DoesNotContain(reconnected, e => e.MaskLayerId == cloak.Id);
    }

    [AvaloniaFact]
    public void Removing_A_Preset_Leaves_The_Effects_Made_From_It()
    {
        var (model, vm) = AnExplosion();
        model.SavePreset("Big bang");
        model.SelectedPreset = model.Presets.Single();
        model.UsePreset();

        model.DeletePreset();

        Assert.Empty(model.Presets);
        Assert.Empty(EffectPresetStore.Load(_library).Presets);
        Assert.Equal(2, vm.Doc.SimGroups!.Count);
    }

    /// <summary>A corrupt library must never stop somebody making an effect by hand.</summary>
    [AvaloniaFact]
    public void A_Corrupt_Library_Loads_As_Empty()
    {
        File.WriteAllText(_library, "{ this is not json");

        var model = new FluidEffectsViewModel(new MainViewModel(null), _library);

        Assert.Empty(model.Presets);
        Assert.NotNull(model.NewElement("fire"));
    }

    /// <summary>
    /// <b>The claim the whole feature rests on: it draws the same effect.</b>
    /// </summary>
    /// <remarks>
    /// Not "it looks similar" — the solver is deterministic and a preset carries
    /// every parameter, so an effect made from one draws <em>exactly</em> what
    /// the original drew, translated to wherever it was put. Compared relative
    /// to each element's own origin, because a preset is stored relative on
    /// purpose: it landed 40 px lower here, which is the feature working rather
    /// than a discrepancy. An absolute comparison reports a difference and means
    /// nothing, and that is how this test read on its first run.
    /// </remarks>
    [AvaloniaFact]
    public void An_Effect_Made_From_A_Preset_Draws_Exactly_What_The_Original_Drew()
    {
        var (model, _) = AnExplosion();
        // Something with enough going on that a coincidence is not plausible.
        var fire = model.Elements.First(e => e.Element?.Name == "flash").Element!;
        fire.Emitters[0].Scatter = new EmitterScatter { Coverage = 0.6, Spacing = 9, SizeVariation = 0.5 };
        fire.Params = fire.Params with { Vorticity = 0.7, Turbulence = 0.5 };
        model.SavePreset("Bonfire");

        var thereVm = new MainViewModel(null);
        var there = new FluidEffectsViewModel(thereVm, _library);
        there.SelectedPreset = there.Presets.Single();
        there.UsePreset();

        model.Selected = model.Elements.First(e => e.Element?.Name == "flash");
        there.Selected = there.Elements.First(e => e.Element?.Name == "flash");
        var original = model.Preview(23);
        var copy = there.Preview(23);

        Assert.NotEmpty(original);
        Assert.Equal(original.Count, copy.Count);
        output.WriteLine($"{original.Count} strokes either side; origins " +
                         $"({model.Element!.OriginX},{model.Element.OriginY}) and " +
                         $"({there.Element!.OriginX},{there.Element.OriginY})");
        Assert.Equal(Relative(original, model.Element!), Relative(copy, there.Element!));

        static string Relative(IReadOnlyList<Stroke> strokes, SimElement element) =>
            string.Join(";", strokes.Select(s =>
                $"{s.Tool}:{s.Color}:" + string.Join("|", s.Points.Select(
                    p => $"{p.X - element.OriginX:F4},{p.Y - element.OriginY:F4},{p.Pressure:F4}"))));
    }

    [AvaloniaFact]
    public void Using_A_Preset_Is_One_Undoable_Step()
    {
        var (model, vm) = AnExplosion();
        model.SavePreset("Big bang");
        model.SelectedPreset = model.Presets.Single();

        var before = vm.RecordedStepCount;
        model.UsePreset();
        Assert.Equal(before + 1, vm.RecordedStepCount);

        vm.UndoCommand.Execute(null);
        Assert.Single(vm.Doc.SimGroups!);
    }
}
