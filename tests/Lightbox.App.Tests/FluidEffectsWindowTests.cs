using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The effects window (Q123): where fire, smoke and water are authored.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these guard is the wiring, not the fluid.</b> The solver, the tracer
/// and the baker have their own suites; what has no other guard is that a
/// control exists for each thing, that pressing it reaches the record, and that
/// the two costs stay visibly apart. A window whose sliders are bound to
/// nothing passes every test the layers below it have.
/// </para>
/// </remarks>
public class FluidEffectsWindowTests
{
    private static FluidEffectsWindow Opened(out MainViewModel vm)
    {
        vm = new MainViewModel(null);
        var window = new FluidEffectsWindow(vm);
        window.Model.NewElement("fire");
        return window;
    }

    /// <summary>
    /// A new element has to arrive already burning. One that simulates still air
    /// and draws nothing reads as the window being broken rather than as an
    /// element waiting to be told where the fire is.
    /// </summary>
    [AvaloniaFact]
    public void A_New_Element_Arrives_Already_Burning()
    {
        var window = Opened(out var vm);

        var element = Assert.Single(vm.Doc.Sims!.Values);
        var emitter = Assert.Single(element.Emitters);
        Assert.True(emitter.Heat > 0, "a fire element's emitter has to be hot or nothing rises");
        Assert.True(emitter.Radius > 0);
        Assert.NotEmpty(window.Model.Preview(element.FrameCount - 1));

        window.Close();
    }

    /// <summary>
    /// Every number the solver and the tracer read has to have a control. This is
    /// the check that catches a parameter added to the record and never surfaced
    /// — which is the failure the "land the places it shows up" rule is about,
    /// and which nothing else here would notice.
    /// </summary>
    [AvaloniaFact]
    public void Every_Solver_Parameter_Has_A_Row()
    {
        var window = Opened(out _);
        var model = window.Model;

        var physics = model.PhysicsFields.Select(f => f.Name).ToList();
        foreach (var name in new[]
                 {
                     "Buoyancy", "Weight", "Vorticity", "Drag", "Turbulence",
                     "Turbulence scale", "Turbulence drift", "Dissipation", "Cooling",
                     "Wind X", "Wind Y",
                 })
        {
            Assert.Contains(name, physics);
        }

        // Every SimParams property, by reflection, so adding one to the record
        // and forgetting the window fails here rather than shipping invisible.
        foreach (var property in typeof(SimParams).GetProperties())
        {
            Assert.True(
                physics.Any(n => n.Replace(" ", string.Empty)
                    .Equals(property.Name, StringComparison.OrdinalIgnoreCase)),
                $"SimParams.{property.Name} has no row in the effects window");
        }

        Assert.All(model.PhysicsFields, f => Assert.True(f.Resolves, $"{f.Name} must force a re-solve"));

        window.Close();
    }

    /// <summary>
    /// The window's central claim: a style edit is cheap and previews as the
    /// slider moves, a fluid edit is not and waits to be asked for. If these two
    /// ever answered the same way, the whole reason this is a window rather than
    /// a bake button would be gone.
    /// </summary>
    [AvaloniaFact]
    public void A_Style_Edit_Previews_And_A_Fluid_Edit_Waits()
    {
        var window = Opened(out _);
        var model = window.Model;

        model.Resolve();
        Assert.False(model.Stale);
        Assert.NotEmpty(model.PreviewStrokes);

        model.TreatmentFields.Single(f => f.Name == "Bands").Value = 4;
        Assert.False(model.Stale);
        Assert.False(model.LastBakeResolved, "restyling must not re-simulate");

        model.PhysicsFields.Single(f => f.Name == "Buoyancy").Value = 1.2;
        Assert.True(model.Stale, "a fluid edit must say the picture is out of date");

        model.Resolve();
        Assert.False(model.Stale);
        Assert.True(model.LastBakeResolved);

        window.Close();
    }

    [AvaloniaFact]
    public void Baking_Writes_Strokes_And_Undoes_In_One_Step()
    {
        var window = Opened(out var vm);
        var element = vm.Doc.Sims!.Values.Single();
        var before = vm.RecordedStepCount;

        window.Model.Bake();

        var drawn = vm.Doc.Scene.Layers.SelectMany(l => l.Cels)
            .Where(c => c.Frame is not null).SelectMany(c => c.Frame!.Strokes)
            .Count(s => s.SimId == element.Id);
        Assert.True(drawn > 0);
        Assert.Equal(before + 1, vm.RecordedStepCount);

        vm.UndoCommand.Execute(null);

        Assert.DoesNotContain(
            vm.Doc.Scene.Layers.SelectMany(l => l.Cels).Where(c => c.Frame is not null)
                .SelectMany(c => c.Frame!.Strokes),
            s => s.SimId == element.Id);

        window.Close();
    }

    /// <summary>
    /// The preview is a picture, and the box it goes in is a real one. A null
    /// source here is the window looking empty for an element that is fine.
    /// </summary>
    [AvaloniaFact]
    public void The_Preview_Box_Gets_A_Picture()
    {
        var window = Opened(out _);
        window.Model.PreviewFrame = 5;
        window.Model.Resolve();

        var image = window.GetControl<Image>("PreviewImage");
        Assert.IsType<Bitmap>(image.Source);

        window.Close();
    }

    /// <summary>
    /// The emitter list drives the record. Adding one has to reach the element,
    /// and the fields have to be pointed at the one that is selected rather than
    /// at the first.
    /// </summary>
    [AvaloniaFact]
    public void The_Emitter_Fields_Edit_The_Selected_Emitter()
    {
        var window = Opened(out var vm);
        var model = window.Model;
        var element = vm.Doc.Sims!.Values.Single();

        model.NewEmitter();
        Assert.Equal(2, element.Emitters.Count);
        Assert.Equal(2, model.Emitters.Count);
        Assert.Same(element.Emitters[1], model.SelectedEmitter!.Emitter);

        model.EmitterFields.Single(f => f.Name == "Heat").Value = 0.25;
        Assert.Equal(0.25, element.Emitters[1].Heat, 3);
        Assert.NotEqual(0.25, element.Emitters[0].Heat);

        model.DeleteEmitter();
        Assert.Single(element.Emitters);

        window.Close();
    }

    /// <summary>
    /// Wind and travel are keyable, and the window edits the constant. Typing a
    /// number must not throw away keys somebody set on the timeline — and a
    /// number back at zero on a parameter nobody keyed must leave no record.
    /// </summary>
    [AvaloniaFact]
    public void Editing_Wind_Keeps_Any_Keys_And_Writes_Nothing_At_Zero()
    {
        var window = Opened(out var vm);
        var model = window.Model;
        var element = vm.Doc.Sims!.Values.Single();
        var wind = model.PhysicsFields.Single(f => f.Name == "Wind X");

        Assert.Null(element.WindX);

        wind.Value = 0.2;
        Assert.Equal(0.2, element.WindX!.Value, 3);

        element.WindX.Keys = [new Lightbox.Core.Effects.EffectKey { Frame = 4, Value = 0.5 }];
        wind.Value = 0.3;
        Assert.Equal(0.3, element.WindX!.Value, 3);
        Assert.Single(element.WindX.Keys!);

        element.WindX.Keys = null;
        wind.Value = 0;
        Assert.Null(element.WindX);

        window.Close();
    }

    /// <summary>
    /// A command wired straight to a handler works perfectly and is invisible to
    /// the whole configuration system — which is the trap Ctrl+S fell into. The
    /// effects window is opened by a registered command, so an artist can find
    /// it, search for it and rebind it.
    /// </summary>
    [AvaloniaFact]
    public void Opening_The_Window_Is_A_Registered_Command()
    {
        var map = new ShortcutMap();

        var effects = Assert.Single(map.Definitions, s => s.Id == "effects.window");

        Assert.NotNull(effects.Default);
        Assert.Contains("Effects", effects.Name);
    }
}
