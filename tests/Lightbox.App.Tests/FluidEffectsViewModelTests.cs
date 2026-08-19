using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;


namespace Lightbox.App.Tests;

/// <summary>
/// The effects window's brain, without the window.
///
/// <para>
/// <b>The solve/draw split is the thing under test</b>, more than any single
/// property. Tuning an effect is a loop — move a slider, look, move it back —
/// and the loop is only usable if restyling costs a redraw rather than a
/// re-simulation. That distinction is decided by one string
/// (<c>SolveFingerprint</c>), so a parameter added to the solver and forgotten
/// here shows up as the cache serving a stale picture: the slider appears to do
/// nothing. Which is why the fingerprint is asserted from both sides — what it
/// must notice, and what it must ignore.
/// </para>
/// </summary>
public class FluidEffectsViewModelTests(ITestOutputHelper output)
{
    /// <summary>A small burning element — enough frames to have something to draw, few enough to be quick.</summary>
    private static SimElement Fire() => new()
    {
        Kind = "fire",
        FirstFrame = 0,
        FrameCount = 6,
        GridWidth = 40,
        GridHeight = 48,
        Scale = 4,
        Substeps = 4,
        BandsFromHeat = true,
        BandColors = ["#3a1200", "#d95f18", "#ffe9a8"],
        Emitters = { new Emitter { Id = "em1", Shape = EmitterShape.Disc, X = 20, Y = 44, Radius = 4, Density = 1, Heat = 1 } },
    };

    private static (MainViewModel Vm, FluidEffectsViewModel Effects, SimElement Element) Burning()
    {
        var vm = new MainViewModel(null);
        var element = Fire();
        (vm.Doc.Sims ??= [])[element.Id] = element;
        var effects = new FluidEffectsViewModel(vm);
        return (vm, effects, element);
    }

    // ---- the element list --------------------------------------------------------

    [AvaloniaFact]
    public void A_New_Element_Lands_In_The_Document_And_Is_Selected()
    {
        var vm = new MainViewModel(null);
        var effects = new FluidEffectsViewModel(vm);

        Assert.Empty(effects.Elements);

        var row = effects.NewElement("fire");

        Assert.Same(row, effects.Selected);
        Assert.Equal(row.Id, effects.Element?.Id);
        Assert.True(vm.Doc.Sims!.ContainsKey(row.Id));
        // A fire element bands from heat; a smoke one does not. The window's
        // "new" button is the only place that choice is made for the artist.
        Assert.True(effects.Element!.BandsFromHeat);
        Assert.False(effects.NewElement("smoke").Element!.BandsFromHeat);
    }

    [AvaloniaFact]
    public void Deleting_An_Element_Takes_Its_Drawings_With_It()
    {
        var (vm, effects, element) = Burning();

        effects.Bake();
        Assert.Contains(vm.Doc.Scene.Layers, l => l.SimId == element.Id);
        var drawn = vm.Doc.Scene.Layers.Single(l => l.SimId == element.Id)
            .Cels.Where(c => c.Frame is not null).Sum(c => c.Frame!.Strokes.Count);
        output.WriteLine($"baked {drawn} strokes");
        Assert.True(drawn > 0, "the fixture drew nothing, so the deletion below proves nothing");

        effects.DeleteElement();

        Assert.Empty(effects.Elements);
        Assert.Null(effects.Element);
        Assert.False(vm.Doc.Sims!.ContainsKey(element.Id));
        Assert.DoesNotContain(
            vm.Doc.Scene.Layers.SelectMany(l => l.Cels).Where(c => c.Frame is not null).SelectMany(c => c.Frame!.Strokes),
            s => s.SimId == element.Id);
    }

    /// <summary>
    /// Undo does not edit the document in place, it swaps a whole one back in —
    /// so a row holding the element object would go on editing a document nobody
    /// is looking at, with every slider still appearing to work. Rows hold ids
    /// for exactly this, and this is the test that would catch it going back.
    /// </summary>
    [AvaloniaFact]
    public void A_Row_Still_Finds_Its_Element_After_An_Undo_Swaps_The_Document()
    {
        var (vm, effects, element) = Burning();
        var row = effects.Selected!;

        effects.Bake();
        var before = vm.Doc;
        vm.UndoCommand.Execute(null);

        Assert.NotSame(before, vm.Doc);
        Assert.NotNull(row.Element);
        Assert.Same(vm.Doc.Sims![element.Id], row.Element);
    }

    // ---- solve versus draw -------------------------------------------------------

    [AvaloniaFact]
    public void Restyling_Redraws_Without_Re_Simulating()
    {
        var (_, effects, element) = Burning();

        effects.Bake();
        Assert.True(effects.LastBakeResolved, "the first bake has nothing cached");

        element.Treatment = new LineTreatment { Bands = 4 };
        element.BandColors = ["#111111", "#222222", "#333333", "#444444"];
        element.OutlineColor = "#ff0000";
        element.OutlineBrush = new BrushSettings { Size = 3 };
        element.BandLow = 0.05;
        effects.Bake();

        Assert.False(effects.LastBakeResolved);
    }

    [AvaloniaFact]
    public void Changing_What_The_Fluid_Does_Re_Simulates()
    {
        var (_, effects, element) = Burning();

        effects.Bake();
        element.Params = element.Params with { Buoyancy = element.Params.Buoyancy * 2 };
        effects.Bake();

        Assert.True(effects.LastBakeResolved);
    }

    /// <summary>
    /// The fingerprint from both sides, as one table — a solver parameter added
    /// without a line here is a stale cache, and a style field added to it is a
    /// re-simulation on every colour change.
    /// </summary>
    [AvaloniaFact]
    public void The_Fingerprint_Notices_Physics_And_Ignores_Style()
    {
        var baseline = FluidEffectsViewModel.SolveFingerprint(Fire());

        Assert.All(
            new (string What, Action<SimElement> Change)[]
            {
                ("buoyancy", e => e.Params = e.Params with { Buoyancy = 1 }),
                ("drag", e => e.Params = e.Params with { Drag = 0.5 }),
                ("vorticity", e => e.Params = e.Params with { Vorticity = 0 }),
                ("turbulence scale", e => e.Params = e.Params with { TurbulenceScale = 20 }),
                ("substeps", e => e.Substeps = 2),
                ("pre-roll", e => e.PreRoll = 12),
                ("obstacle layer", e => e.ObstacleLayerId = "layer7"),
                ("emitter position", e => e.Emitters[0].X += 1),
                ("emitter travel", e => e.Emitters[0].MotionX = new Lightbox.Core.Effects.EffectParam { Value = 3 }),
                ("wind", e => e.WindX = new Lightbox.Core.Effects.EffectParam { Value = 0.2 }),
                ("which field the bands read", e => e.BandsFromHeat = false),
            },
            row =>
            {
                var element = Fire();
                row.Change(element);
                Assert.True(
                    FluidEffectsViewModel.SolveFingerprint(element) != baseline,
                    $"changing the {row.What} must force a re-solve");
            });

        Assert.All(
            new (string What, Action<SimElement> Change)[]
            {
                ("band count", e => e.Treatment = new LineTreatment { Bands = 5 }),
                ("band colours", e => e.BandColors = ["#000000"]),
                ("band window", e => { e.BandLow = 0.1; e.BandHigh = 0.9; }),
                ("outline colour", e => e.OutlineColor = "#ff0000"),
                ("the pen", e => e.OutlineBrush = new BrushSettings { Size = 9 }),
                ("the shared treatment", e => e.TreatmentId = "inky"),
                ("its name", e => e.Name = "hearth"),
            },
            row =>
            {
                var element = Fire();
                row.Change(element);
                Assert.True(
                    FluidEffectsViewModel.SolveFingerprint(element) == baseline,
                    $"changing the {row.What} must not force a re-solve");
            });
    }

    // ---- the treatment cascade ---------------------------------------------------

    [AvaloniaFact]
    public void A_Field_Reads_The_Resolved_Value_And_Says_It_Is_Inherited()
    {
        var (_, effects, element) = Burning();

        var bands = effects.TreatmentFields.Single(f => f.Name == "Bands");

        Assert.Equal(LineTreatment.Defaults.Bands, bands.Value);
        Assert.False(bands.IsOverridden);
        Assert.Null(element.Treatment);
    }

    [AvaloniaFact]
    public void Setting_A_Field_Overrides_Only_That_Field()
    {
        var (_, effects, element) = Burning();

        var bands = effects.TreatmentFields.Single(f => f.Name == "Bands");
        bands.Value = 5;

        Assert.True(bands.IsOverridden);
        Assert.Equal(5, element.Treatment?.Bands);
        Assert.Null(element.Treatment?.Simplify);
        Assert.False(effects.TreatmentFields.Single(f => f.Name == "Simplify").IsOverridden);
    }

    /// <summary>
    /// Reverting the last override has to leave no record at all. An empty
    /// override set would write <c>"treatment": {}</c> into every document that
    /// had ever touched a slider and put it back — which is the half of
    /// "optional" that is easy to miss.
    /// </summary>
    [AvaloniaFact]
    public void Reverting_The_Last_Override_Leaves_No_Record()
    {
        var (_, effects, element) = Burning();

        var bands = effects.TreatmentFields.Single(f => f.Name == "Bands");
        bands.Value = 5;
        bands.Revert();

        Assert.False(bands.IsOverridden);
        Assert.Null(element.Treatment);
        Assert.Equal(LineTreatment.Defaults.Bands, bands.Value);
        Assert.DoesNotContain("\"treatment\"", DocJson.Serialize(new Doc { Sims = new() { [element.Id] = element } }));
    }

    [AvaloniaFact]
    public void Reverting_Everything_Drops_The_Whole_Override()
    {
        var (_, effects, element) = Burning();

        effects.TreatmentFields.Single(f => f.Name == "Bands").Value = 5;
        effects.TreatmentFields.Single(f => f.Name == "Simplify").Value = 0.6;
        Assert.NotNull(element.Treatment);

        effects.RevertTreatment();

        Assert.Null(element.Treatment);
        Assert.All(effects.TreatmentFields, f => Assert.False(f.IsOverridden));
        Assert.Equal(LineTreatment.Defaults.Simplify, effects.TreatmentFields.Single(f => f.Name == "Simplify").Value);
    }

    // ---- the pen -----------------------------------------------------------------

    /// <summary>
    /// Taking the toolbar's brush is an action with a result, not a dependency
    /// read at bake time — and it has to be a copy, or the size slider goes on
    /// editing the element's line afterwards.
    /// </summary>
    [AvaloniaFact]
    public void Taking_The_Current_Brush_Copies_It()
    {
        var (vm, effects, element) = Burning();

        vm.BrushSize = 17;
        effects.UseCurrentBrush();
        Assert.Equal(17, element.OutlineBrush?.Size);

        vm.BrushSize = 3;
        Assert.Equal(17, element.OutlineBrush?.Size);

        effects.ClearBrush();
        Assert.Null(element.OutlineBrush);
    }

    [AvaloniaFact]
    public void The_Pen_Sets_The_Width_Of_The_Lines_It_Draws()
    {
        var (_, effects, element) = Burning();

        element.OutlineBrush = new BrushSettings { Size = 11 };
        var strokes = effects.Preview(element.FirstFrame + element.FrameCount - 1);

        var outlines = strokes.Where(s => s.Tool == ToolKind.Brush).ToList();
        Assert.NotEmpty(outlines);
        Assert.All(outlines, s => Assert.Equal(11, s.Brush.Size));
    }

    // ---- the preview -------------------------------------------------------------

    /// <summary>
    /// The preview must not reach the document. Tuning a treatment is a loop of
    /// tens of redraws, and a loop that each time wrote strokes an artist would
    /// then have to undo is not a preview.
    /// </summary>
    [AvaloniaFact]
    public void Previewing_Draws_Nothing_Into_The_Document()
    {
        var (vm, effects, element) = Burning();

        var strokes = effects.Preview(element.FrameCount - 1);

        Assert.NotEmpty(strokes);
        Assert.DoesNotContain(vm.Doc.Scene.Layers, l => l.SimId == element.Id);
        Assert.Equal(0, vm.Doc.Scene.Layers.SelectMany(l => l.Cels)
            .Count(c => c.Frame is { } f && f.Strokes.Any(s => s.SimId == element.Id)));
    }

    /// <summary>
    /// Scrubbing across a hold shows the drawing that is exposed there. On 2s
    /// there is no drawing on the odd frames, and answering "nothing" would make
    /// the preview blink on every other frame of an element that is perfectly
    /// fine.
    /// </summary>
    [AvaloniaFact]
    public void Scrubbing_Across_A_Hold_Shows_The_Drawing_That_Is_Exposed()
    {
        var (_, effects, element) = Burning();
        element.ExposeOn = 2;

        // Drawings land on 0, 2 and 4. Frame 5 is the second half of frame 4's
        // hold, and frame 2 is a different drawing — so "the nearest at or
        // before" has to be doing real work rather than always answering the
        // last one or always answering nothing.
        var theHold = effects.Preview(4);
        var acrossIt = effects.Preview(5);
        var earlier = effects.Preview(2);

        Assert.NotEmpty(theHold);
        Assert.NotEmpty(earlier);
        Assert.Equal(Outline(theHold), Outline(acrossIt));
        Assert.NotEqual(Outline(theHold), Outline(earlier));

        static string Outline(IReadOnlyList<Stroke> strokes) =>
            string.Join(";", strokes.Select(s => $"{s.Tool}:{s.Points.Count}:{s.Points[0].X:F3},{s.Points[0].Y:F3}"));
    }

    [AvaloniaFact]
    public void The_Preview_Picture_Frames_The_Element_Rather_Than_The_Canvas()
    {
        var (_, effects, _) = Burning();

        using var picture = effects.RenderPreview(5, 200, 200);

        Assert.NotNull(picture);
        output.WriteLine($"{picture!.Width}×{picture.Height} for an element of 160×192");

        // 40×48 cells at scale 4 is 160×192 document pixels, and fitting that
        // into a 200-square box is a scale of 200/192 — so the picture keeps the
        // element's shape rather than the box's, and is nowhere near the
        // document's own 1920×1080. Invariant 7: the surface is scaled, the
        // geometry is not.
        Assert.Equal(200, picture.Height);
        Assert.Equal((int)Math.Ceiling(160 * (200 / 192.0)), picture.Width);
        Assert.True(picture.Width < picture.Height, "a tall element must not come back landscape");
    }
}
