using Lightbox.Core.Documents;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// An effect tuned once and used again — its parameters, not its drawings.
/// </summary>
/// <remarks>
/// <b>What is under test is the two things a preset has to survive</b>: being
/// put somewhere other than where it was made, and being carried into a document
/// that has never heard of the layers it was pointed at. The second is the one
/// that would otherwise look fine until somebody baked.
/// </remarks>
public class EffectPresetTests(ITestOutputHelper output)
{
    private static SimElement Element(string id, double x, double y, int first)
    {
        var e = new SimElement
        {
            Id = id, Kind = "fire", OriginX = x, OriginY = y, FirstFrame = first, FrameCount = 24,
        };
        e.Emitters.Add(new Emitter { Id = $"em_{id}", X = 10, Y = 10, Radius = 3 });
        return e;
    }

    private static (Doc Doc, SimGroup Group) Explosion()
    {
        var doc = new Doc { Sims = [] };
        doc.Scene.Layers.Add(new Layer { Id = "lay_cloak", Name = "cloak" });
        doc.Scene.Layers.Add(new Layer { Id = "lay_figure", Name = "figure" });

        var flash = Element("flash", 100, 200, 10);
        var smoke = Element("smoke", 130, 190, 14);
        smoke.ObstacleLayerId = "lay_figure";
        smoke.Emitters[0].MaskLayerId = "lay_cloak";

        foreach (var e in new[] { flash, smoke }) doc.Sims![e.Id] = e;
        return (doc, SimGroupOps.Create(doc, "Explosion", [flash, smoke]));
    }

    // ---- keeping it ---------------------------------------------------------------

    /// <summary>
    /// Stored relative to the effect's own corner, so it can be put somewhere —
    /// and the offsets between members survive untouched, because those are the
    /// effect.
    /// </summary>
    [Fact]
    public void A_Preset_Keeps_The_Shape_Rather_Than_The_Place()
    {
        var (doc, group) = Explosion();

        var preset = EffectPresets.Capture(doc, group, "Big bang");

        Assert.Equal("Big bang", preset.Name);
        Assert.Equal(2, preset.Elements.Count);
        Assert.Equal(0, preset.Elements.Min(e => e.OriginX));
        Assert.Equal(0, preset.Elements.Min(e => e.OriginY));
        Assert.Equal(0, preset.Elements.Min(e => e.FirstFrame));

        // 30 px apart and 4 frames apart, exactly as they were.
        Assert.Equal(30, preset.Elements[1].OriginX - preset.Elements[0].OriginX);
        Assert.Equal(4, preset.Elements[1].FirstFrame - preset.Elements[0].FirstFrame);
    }

    /// <summary>
    /// A layer id names a layer in <em>this</em> document and nothing anywhere
    /// else, so the preset keeps the layer's name instead.
    /// </summary>
    [Fact]
    public void A_Preset_Keeps_Layer_Names_Rather_Than_Ids()
    {
        var (doc, group) = Explosion();

        var preset = EffectPresets.Capture(doc, group, "Burning cloak");

        var smoke = preset.Elements.Single(e => e.ObstacleLayerId is not null);
        Assert.Equal("figure", smoke.ObstacleLayerId);
        Assert.Equal("cloak", smoke.Emitters[0].MaskLayerId);
        // And no id survived, which is the thing that would silently name nothing.
        Assert.DoesNotContain(preset.Elements, e => e.ObstacleLayerId?.StartsWith("lay_") == true);
    }

    /// <summary>The shared look travels with it, or the effect arrives as a plain line.</summary>
    [Fact]
    public void A_Preset_Carries_The_Shared_Treatment_Its_Elements_Name()
    {
        var (doc, group) = Explosion();
        doc.LineTreatments = new Dictionary<string, LineTreatment>
        {
            ["inky"] = new() { Bands = 5, ShadeOffset = 3 },
            ["unused"] = new() { Bands = 2 },
        };
        SimGroupOps.Members(doc, group)[0].TreatmentId = "inky";

        var preset = EffectPresets.Capture(doc, group, "Inky fire");

        Assert.Equal(5, preset.Treatments!["inky"].Bands);
        Assert.DoesNotContain("unused", preset.Treatments.Keys);
    }

    [Fact]
    public void A_Preset_Whose_Elements_State_Their_Own_Look_Carries_No_Treatments()
    {
        var (doc, group) = Explosion();
        Assert.Null(EffectPresets.Capture(doc, group, "Plain").Treatments);
    }

    // ---- using it -----------------------------------------------------------------

    /// <summary>
    /// Used, it lands where it is asked to, keeping its internal spacing — and
    /// it is a group, so everything the effect window does to a group works on
    /// it immediately.
    /// </summary>
    [Fact]
    public void Using_A_Preset_Puts_The_Effect_Where_It_Is_Asked_For()
    {
        var (source, group) = Explosion();
        var preset = EffectPresets.Capture(source, group, "Big bang");

        var doc = new Doc { Sims = [] };
        var made = EffectPresets.Instantiate(doc, preset, 500, 40, 60);

        Assert.NotNull(made);
        var members = SimGroupOps.Members(doc, made!);
        Assert.Equal(2, members.Count);
        output.WriteLine($"placed at {string.Join(", ", members.Select(m => $"({m.OriginX},{m.OriginY})@{m.FirstFrame}"))}");

        Assert.Equal(500, members.Min(e => e.OriginX));
        Assert.Equal(40, members.Min(e => e.OriginY));
        Assert.Equal(60, SimGroupOps.FirstFrame(doc, made));
        Assert.Equal(30, members[1].OriginX - members[0].OriginX);
        Assert.Equal(4, members[1].FirstFrame - members[0].FirstFrame);
    }

    /// <summary>
    /// Twice in one document is two effects, not one edited twice — every
    /// element and every emitter gets a fresh id.
    /// </summary>
    [Fact]
    public void Using_A_Preset_Twice_Makes_Two_Separate_Effects()
    {
        var (source, group) = Explosion();
        var preset = EffectPresets.Capture(source, group, "Big bang");

        var doc = new Doc { Sims = [] };
        var first = EffectPresets.Instantiate(doc, preset)!;
        var second = EffectPresets.Instantiate(doc, preset, 400)!;

        Assert.Equal(4, doc.Sims!.Count);
        Assert.Equal(2, doc.SimGroups!.Count);
        Assert.Empty(first.ElementIds.Intersect(second.ElementIds));

        var emitters = doc.Sims.Values.SelectMany(e => e.Emitters).Select(e => e.Id).ToList();
        Assert.Equal(emitters.Count, emitters.Distinct().Count());

        // Moving one leaves the other where it is.
        SimGroupOps.Move(doc, first, 1000, 0);
        Assert.Equal(400, SimGroupOps.Members(doc, second).Min(e => e.OriginX));
    }

    /// <summary>
    /// A layer of the right name is reconnected; anything else is left off and
    /// reported, because an emitter pointed at a layer that is not there emits
    /// nothing at all.
    /// </summary>
    [Fact]
    public void Layers_Reconnect_By_Name_And_What_Cannot_Is_Reported()
    {
        var (source, group) = Explosion();
        var preset = EffectPresets.Capture(source, group, "Burning cloak");

        var welcoming = new Doc { Sims = [] };
        welcoming.Scene.Layers.Add(new Layer { Id = "other_id", Name = "cloak" });

        Assert.Equal(["figure"], EffectPresets.MissingLayers(welcoming, preset));

        var made = EffectPresets.Instantiate(welcoming, preset)!;
        var smoke = SimGroupOps.Members(welcoming, made).Single(e => e.Emitters[0].MaskLayerId is not null);
        output.WriteLine($"mask reconnected to {smoke.Emitters[0].MaskLayerId}, obstacle {smoke.ObstacleLayerId ?? "(none)"}");

        Assert.Equal("other_id", smoke.Emitters[0].MaskLayerId);
        Assert.Null(smoke.ObstacleLayerId);
    }

    [Fact]
    public void A_Document_With_None_Of_The_Layers_Reports_Them_All()
    {
        var (source, group) = Explosion();
        var preset = EffectPresets.Capture(source, group, "Burning cloak");

        var bare = new Doc { Sims = [] };
        Assert.Equal(["figure", "cloak"], EffectPresets.MissingLayers(bare, preset));

        var made = EffectPresets.Instantiate(bare, preset)!;
        Assert.All(SimGroupOps.Members(bare, made), e =>
        {
            Assert.Null(e.ObstacleLayerId);
            Assert.All(e.Emitters, em => Assert.Null(em.MaskLayerId));
        });
    }

    /// <summary>
    /// Two effects from one preset share the shared treatment rather than
    /// growing a copy each — SymbolScopes' rule, and what keeps a look one look.
    /// </summary>
    [Fact]
    public void The_Shared_Treatment_Keeps_Its_Id_So_Two_Uses_Share_One_Look()
    {
        var (source, group) = Explosion();
        source.LineTreatments = new Dictionary<string, LineTreatment> { ["inky"] = new() { Bands = 5 } };
        SimGroupOps.Members(source, group)[0].TreatmentId = "inky";
        var preset = EffectPresets.Capture(source, group, "Inky");

        var doc = new Doc { Sims = [] };
        EffectPresets.Instantiate(doc, preset);
        EffectPresets.Instantiate(doc, preset, 300);

        Assert.Single(doc.LineTreatments!);
        Assert.Equal(5, doc.LineTreatments!["inky"].Bands);
        Assert.All(doc.Sims!.Values.Where(e => e.TreatmentId is not null),
            e => Assert.Equal("inky", e.TreatmentId));
    }

    /// <summary>
    /// The library is a source to choose from, not a live dependency: editing a
    /// preset afterwards must not reach into effects already made from it.
    /// </summary>
    [Fact]
    public void Editing_A_Preset_Does_Not_Reach_Into_Effects_Already_Made()
    {
        var (source, group) = Explosion();
        var preset = EffectPresets.Capture(source, group, "Big bang");

        var doc = new Doc { Sims = [] };
        var made = EffectPresets.Instantiate(doc, preset)!;

        preset.Elements[0].FrameCount = 999;
        preset.Name = "renamed";

        Assert.Equal(24, SimGroupOps.Members(doc, made)[0].FrameCount);
        Assert.Equal("Big bang", made.Name);
    }

    [Fact]
    public void Capturing_An_Empty_Group_Gives_An_Empty_Preset_Rather_Than_Throwing()
    {
        var doc = new Doc { Sims = [] };
        var group = SimGroupOps.Create(doc, "Nothing", []);

        var preset = EffectPresets.Capture(doc, group, "Nothing");

        Assert.Empty(preset.Elements);
        Assert.Null(EffectPresets.Instantiate(doc, preset));
    }

    [Fact]
    public void Cloning_A_Preset_Shares_Nothing_With_It()
    {
        var (doc, group) = Explosion();
        doc.LineTreatments = new Dictionary<string, LineTreatment> { ["inky"] = new() { Bands = 5 } };
        SimGroupOps.Members(doc, group)[0].TreatmentId = "inky";
        var preset = EffectPresets.Capture(doc, group, "Inky");

        var copy = preset.Clone();
        copy.Elements[0].FrameCount = 1;
        copy.Treatments!["inky"].Bands = 1;

        Assert.Equal(24, preset.Elements[0].FrameCount);
        Assert.Equal(5, preset.Treatments!["inky"].Bands);
    }
}
