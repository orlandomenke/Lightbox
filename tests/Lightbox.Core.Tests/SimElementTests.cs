using Lightbox.Core.Documents;
using System.Text.Json;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// The effects element's record. Most of these are about what is <em>not</em>
/// in the file: the medium block cost twenty-one keys on every stroke of every
/// document before anybody looked at the JSON, and this is the cheap half of
/// that lesson paid up front.
/// </summary>
public class SimElementTests
{
    /// <summary>
    /// One element on its own. Asserting against a whole document's JSON is a
    /// trap for a key as common as <c>name</c> — a layer has one, a palette has
    /// one, and the assertion then passes or fails for reasons nothing to do
    /// with the element.
    /// </summary>
    private static string Json(SimElement element) => JsonSerializer.Serialize(element, DocJson.Options);

    private static Frame FirstFrame(Doc doc) => (Frame)doc.Scene.Layers[0].Cels[0].Frame!;

    private static SimElement Fire() => new()
    {
        Id = "sim1",
        Kind = "fire",
        Name = "torch",
        FirstFrame = 4,
        FrameCount = 36,
        ExposeOn = 2,
        GridWidth = 128,
        GridHeight = 96,
        OriginX = 220,
        OriginY = 40,
        Scale = 8,
        Substeps = 6,
        BandsFromHeat = true,
        BandLow = 0.05,
        BandHigh = 0.95,
        BandColors = ["#3a1200", "#d95f18", "#ffe9a8"],
        OutlineColor = "#2b1400",
        Params = new SimParams { Buoyancy = 0.11, Cooling = 0.05 },
        Emitters =
        [
            new Emitter { Id = "em1", Shape = EmitterShape.Segment, X = 40, Y = 90, X2 = 88, Y2 = 90, Heat = 2 },
        ],
    };

    // ---- absent until used ----------------------------------------------------

    [Fact]
    public void A_Document_That_Never_Makes_An_Effect_Writes_No_Keys()
    {
        var json = DocJson.Serialize(new Doc());

        Assert.DoesNotContain("\"sims\"", json);
        Assert.DoesNotContain("\"lineTreatments\"", json);
        Assert.DoesNotContain("\"hasSims\"", json);
        Assert.DoesNotContain("\"simId\"", json);
    }

    [Fact]
    public void A_Hand_Drawn_Stroke_Writes_No_SimId()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        FirstFrame(doc).Strokes.Add(new Stroke
        {
            Points = [new StrokePoint(1, 1, 1), new StrokePoint(9, 9, 1)],
        });

        Assert.DoesNotContain("\"simId\"", DocJson.Serialize(doc));
    }

    [Fact]
    public void A_Fluid_That_Does_Not_Burn_Writes_No_Combustion_Keys()
    {
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["sim1"] = Fire() } };

        // Steam does not burn, and a document full of smoke should not carry
        // three keys saying so. The cheap half of "optional means absent" — the
        // medium block paid for the expensive half once already.
        var json = DocJson.Serialize(doc);
        Assert.DoesNotContain("\"burning\"", json);
        Assert.DoesNotContain("\"ignition\"", json);
        Assert.DoesNotContain("\"yield\"", json);
    }

    [Fact]
    public void A_Burning_Fluid_Round_Trips_Its_Combustion_Block()
    {
        var element = Fire();
        element.Params = element.Params with
        {
            Burning = new Combustion { Rate = 0.04, Ignition = 0.25, Yield = 2.5 },
        };
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["sim1"] = element } };

        var back = DocJson.Deserialize(DocJson.Serialize(doc)).Sims!["sim1"].Params.Burning;

        Assert.NotNull(back);
        Assert.Equal(0.04, back!.Value.Rate, 6);
        Assert.Equal(0.25, back.Value.Ignition, 6);
        Assert.Equal(2.5, back.Value.Yield, 6);
    }

    [Fact]
    public void Editing_A_Copys_Combustion_Leaves_The_Original_Alone()
    {
        // `SimParams` is a record and `Clone` copies it with `with { }`, which
        // copies a reference rather than what it points at. A value type is what
        // keeps a duplicated effect from editing the one it was copied from, and
        // this is the assertion that would fail if it ever became a class.
        var element = Fire();
        element.Params = element.Params with { Burning = new Combustion { Rate = 0.04 } };

        var copy = element.Clone();
        copy.Params = copy.Params with { Burning = copy.Params.Burning!.Value with { Rate = 0.2 } };

        Assert.Equal(0.04, element.Params.Burning!.Value.Rate, 6);
        Assert.Equal(0.2, copy.Params.Burning!.Value.Rate, 6);
    }

    [Fact]
    public void An_Element_With_No_Embers_And_No_Overrides_Writes_Neither_Key()
    {
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["sim1"] = Fire() } };

        var json = DocJson.Serialize(doc);

        Assert.Contains("\"sims\"", json);
        Assert.DoesNotContain("\"particles\"", json);
        Assert.DoesNotContain("\"treatmentId\"", json);
        Assert.DoesNotContain("\"treatment\"", json);

        // …and an element nobody has named writes no name either.
        Assert.DoesNotContain("\"name\"", Json(new SimElement()));
    }

    /// <summary>
    /// The pen is on the element so a re-bake draws the same line — but an
    /// element on the default pen must still write no key. This is the shape
    /// <c>BlendOrNormal</c> got wrong: an optional block whose "unset" value
    /// reaches the file under some name is not optional, it is a default with
    /// extra bytes.
    /// </summary>
    [Fact]
    public void An_Element_On_The_Default_Pen_Writes_No_Brush()
    {
        Assert.DoesNotContain("\"outlineBrush\"", Json(new SimElement()));

        var pen = Fire();
        pen.OutlineBrush = new BrushSettings { Size = 3 };
        Assert.Contains("\"outlineBrush\"", Json(pen));
    }

    /// <summary>
    /// A blast's three settings are absent on every emitter that is not one.
    /// </summary>
    /// <remarks>
    /// Three keys on every emitter of every document, for a feature nobody
    /// switched on, is the medium block's mistake at a smaller scale — and the
    /// smaller scale is exactly why it would not be noticed.
    /// </remarks>
    [Fact]
    public void A_Burst_Writes_No_Keys_Until_There_Is_One()
    {
        var plain = new SimElement();
        plain.Emitters.Add(new Emitter());
        var json = Json(plain);

        Assert.DoesNotContain("\"burst\"", json);
        Assert.DoesNotContain("\"emitFrom\"", json);
        Assert.DoesNotContain("\"emitUntil\"", json);
        // The derived companion must not reach the file under its own name —
        // which is the trap `BlendOrNormal` fell into.
        Assert.DoesNotContain("\"isTimed\"", json);

        var blast = new SimElement();
        blast.Emitters.Add(new Emitter { Burst = 0.8, EmitFrom = 3, EmitUntil = 5 });
        var loud = Json(blast);
        Assert.Contains("\"burst\"", loud);
        Assert.Contains("\"emitFrom\"", loud);
        Assert.Contains("\"emitUntil\"", loud);
    }

    /// <summary>
    /// An emitter nobody scattered writes nothing about not being scattered.
    /// </summary>
    [Fact]
    public void Scatter_Is_Absent_Until_It_Is_Switched_On()
    {
        var plain = new SimElement();
        plain.Emitters.Add(new Emitter());
        Assert.DoesNotContain("\"scatter\"", Json(plain));

        var scattered = new SimElement();
        scattered.Emitters.Add(new Emitter { Scatter = new EmitterScatter { Coverage = 0.3 } });
        var json = Json(scattered);
        Assert.Contains("\"scatter\"", json);
        Assert.Contains("\"coverage\"", json);
    }

    /// <summary>
    /// Cloning copies the scatter block rather than sharing it — an undo
    /// snapshot and the live document must not tune the same flames.
    /// </summary>
    [Fact]
    public void Cloning_An_Emitter_Copies_Its_Scatter()
    {
        var emitter = new Emitter { Scatter = new EmitterScatter { Coverage = 0.3, Spacing = 9 } };

        var copy = emitter.Clone();
        copy.Scatter!.Coverage = 0.9;

        Assert.Equal(0.3, emitter.Scatter!.Coverage);
    }

    /// <summary>
    /// Shading is the newest treatment field and the one most likely to be
    /// forgotten in the three places a nullable field has to appear: absent
    /// unless stated, carried by the cascade, and copied by a clone.
    /// </summary>
    [Fact]
    public void Shading_Is_Absent_Until_Stated_And_Survives_The_Cascade()
    {
        Assert.DoesNotContain("\"shadeOffset\"", Json(new SimElement { Treatment = new LineTreatment { Bands = 2 } }));
        Assert.Contains("\"shadeOffset\"", Json(new SimElement { Treatment = new LineTreatment { ShadeOffset = 3 } }));

        Assert.Equal(0, LineTreatment.Defaults.ShadeOffset);
        Assert.Equal(3, LineTreatment.Resolve(new LineTreatment { ShadeOffset = 3 }).ShadeOffset);
        Assert.Equal(5, LineTreatment.Resolve(
            new LineTreatment { ShadeOffset = 3 }, new LineTreatment { ShadeOffset = 5 }).ShadeOffset);

        Assert.Contains(nameof(LineTreatment.ShadeOffset), new LineTreatment { ShadeOffset = 1 }.StatedFields());
        Assert.Equal(1, new LineTreatment { ShadeOffset = 1 }.Clone().ShadeOffset);
    }

    /// <summary>
    /// Cloning has to copy the pen rather than share it, or an undo snapshot and
    /// the live document edit the same brush — which is the bug where changing an
    /// element's line silently rewrites history.
    /// </summary>
    [Fact]
    public void Cloning_An_Element_Copies_Its_Pen()
    {
        var element = Fire();
        element.OutlineBrush = new BrushSettings { Size = 3 };

        var copy = element.Clone();
        copy.OutlineBrush!.Size = 9;

        Assert.Equal(3, element.OutlineBrush.Size);
    }

    /// <summary>
    /// A treatment override that states nothing has to write nothing —
    /// <c>{}</c>, not sixteen nulls and not sixteen defaults. That is what makes
    /// the cascade cost one record rather than two (Q118), and it is exactly
    /// where <c>BlendOrNormal</c> went wrong: a value that means "unset" must not
    /// reach the file under any name.
    /// </summary>
    [Fact]
    public void An_Override_That_States_Nothing_Writes_Nothing()
    {
        var element = Fire();
        element.Treatment = new LineTreatment();
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["sim1"] = element } };

        var json = DocJson.Serialize(doc);

        Assert.Contains("\"treatment\": {}", json);
        Assert.DoesNotContain("\"simplify\"", json);
        Assert.DoesNotContain("\"baseWeight\"", json);
        Assert.DoesNotContain("\"outlined\"", json);
        Assert.DoesNotContain("\"weights\"", json);
    }

    [Fact]
    public void An_Override_Writes_Only_The_Fields_It_States()
    {
        var element = Fire();
        element.Treatment = new LineTreatment { Bands = 4, Offset = 0 };
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["sim1"] = element } };

        var json = DocJson.Serialize(doc);

        Assert.Contains("\"bands\": 4", json);
        Assert.Contains("\"offset\": 0", json);   // zero is a value, not an absence
        Assert.DoesNotContain("\"smoothing\"", json);
        Assert.DoesNotContain("\"coverageAngleDeg\"", json);
    }

    // ---- round trips -----------------------------------------------------------

    [Fact]
    public void An_Element_Survives_A_Round_Trip_Intact()
    {
        var element = Fire();
        element.Particles = new ParticleSpec { PerFrame = 30, Lifetime = 12, Color = "#ffaa22" };
        element.TreatmentId = "house-fire";
        element.Treatment = new LineTreatment
        {
            Bands = 4,
            Covers = LineCoverage.Facing,
            Weights = [new WeightDriver(WeightSource.Curvature, 0.6), new WeightDriver(WeightSource.ArcTaper, -0.2)],
        };

        var doc = new Doc
        {
            Sims = new Dictionary<string, SimElement> { ["sim1"] = element },
            LineTreatments = new Dictionary<string, LineTreatment>
            {
                ["house-fire"] = new() { Bands = 2, Simplify = 0.3, Outlined = OutlinedBands.Every },
            },
        };

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        var got = back.Sims!["sim1"];
        Assert.Equal("fire", got.Kind);
        Assert.Equal("torch", got.Name);
        Assert.Equal(2, got.ExposeOn);
        Assert.True(got.BandsFromHeat);
        Assert.Equal(0.11, got.Params.Buoyancy);
        Assert.Equal(0.05, got.Params.Cooling);
        Assert.Equal(["#3a1200", "#d95f18", "#ffe9a8"], got.BandColors);

        var emitter = Assert.Single(got.Emitters);
        Assert.Equal(EmitterShape.Segment, emitter.Shape);
        Assert.Equal(88, emitter.X2);
        Assert.Equal(2, emitter.Heat);

        Assert.Equal(30, got.Particles!.PerFrame);
        Assert.Equal("house-fire", got.TreatmentId);
        Assert.Equal(LineCoverage.Facing, got.Treatment!.Covers);
        Assert.Equal(2, got.Treatment.Weights!.Count);
        Assert.Equal(WeightSource.ArcTaper, got.Treatment.Weights[1].Source);
        Assert.Equal(-0.2, got.Treatment.Weights[1].Amount);

        Assert.Equal(OutlinedBands.Every, back.LineTreatments!["house-fire"].Outlined);
    }

    [Fact]
    public void A_Baked_Stroke_Keeps_Its_Element_Across_A_Round_Trip()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        FirstFrame(doc).Strokes.Add(new Stroke
        {
            SimId = "sim1",
            Points = [new StrokePoint(1, 1, 1), new StrokePoint(9, 9, 1)],
        });

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        Assert.Equal("sim1", FirstFrame(back).Strokes[0].SimId);
    }

    /// <summary>
    /// An element of a kind this build does not know — a document from a newer
    /// version — is preserved on save and skipped on bake, never dropped. That
    /// is what a string id buys over an enum, and it is the trade the brush tip
    /// registry already made.
    /// </summary>
    [Fact]
    public void An_Unknown_Kind_Is_Preserved_Rather_Than_Dropped()
    {
        var element = Fire();
        element.Kind = "lightning-from-a-later-build";
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["sim1"] = element } };

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        Assert.Equal("lightning-from-a-later-build", back.Sims!["sim1"].Kind);
    }

    // ---- the cascade, resolved in one place -------------------------------------

    [Fact]
    public void An_Element_Follows_Its_Shared_Treatment_And_Its_Own_Overrides()
    {
        var element = Fire();
        element.TreatmentId = "house";
        element.Treatment = new LineTreatment { Bands = 4 };

        var doc = new Doc
        {
            Sims = new Dictionary<string, SimElement> { ["sim1"] = element },
            LineTreatments = new Dictionary<string, LineTreatment>
            {
                ["house"] = new() { Bands = 2, BaseWeight = 0.8, Outlined = OutlinedBands.Every },
            },
        };

        var resolved = doc.TreatmentFor(element);

        Assert.Equal(4, resolved.Bands);                          // the element's override
        Assert.Equal(0.8, resolved.BaseWeight);                   // the shared treatment
        Assert.Equal(OutlinedBands.Every, resolved.Outlined);     // …and this
        Assert.Equal(LineTreatment.Defaults.Simplify, resolved.Simplify);   // stated nowhere
    }

    /// <summary>
    /// A treatment deleted out from under an element should leave a plain line,
    /// not an unopenable document.
    /// </summary>
    [Fact]
    public void A_Missing_Treatment_Resolves_To_The_Defaults()
    {
        var element = Fire();
        element.TreatmentId = "deleted-last-week";

        var resolved = new Doc().TreatmentFor(element);

        Assert.Equal(LineTreatment.Defaults, resolved);
    }

    // ---- cloning ------------------------------------------------------------------

    [Fact]
    public void Cloning_A_Document_Copies_Its_Elements_Rather_Than_Sharing_Them()
    {
        var element = Fire();
        element.Particles = new ParticleSpec();
        element.Treatment = new LineTreatment { Bands = 4 };
        var doc = new Doc
        {
            Sims = new Dictionary<string, SimElement> { ["sim1"] = element },
            LineTreatments = new Dictionary<string, LineTreatment> { ["house"] = new() { Bands = 2 } },
        };

        var copy = doc.Clone();
        copy.Sims!["sim1"].Params.Buoyancy = 9;
        copy.Sims["sim1"].Emitters[0].Heat = 9;
        copy.Sims["sim1"].BandColors.Add("#ffffff");
        copy.Sims["sim1"].Treatment!.Bands = 9;
        copy.Sims["sim1"].Particles!.PerFrame = 99;
        copy.LineTreatments!["house"].Bands = 9;

        Assert.Equal(0.11, doc.Sims!["sim1"].Params.Buoyancy);
        Assert.Equal(2, doc.Sims["sim1"].Emitters[0].Heat);
        Assert.Equal(3, doc.Sims["sim1"].BandColors.Count);
        Assert.Equal(4, doc.Sims["sim1"].Treatment!.Bands);
        Assert.Equal(12, doc.Sims["sim1"].Particles!.PerFrame);
        Assert.Equal(2, doc.LineTreatments!["house"].Bands);
    }

    [Fact]
    public void An_Element_Knows_Which_Frames_It_Covers()
    {
        var element = new SimElement { FirstFrame = 4, FrameCount = 3 };

        Assert.False(element.Covers(3));
        Assert.True(element.Covers(4));
        Assert.True(element.Covers(6));
        Assert.False(element.Covers(7));
    }
}
