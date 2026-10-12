using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The two brushes that shipped running the fluid simulation until Q251, as
/// they were, for the tests whose subject is the simulation.
/// </summary>
/// <remarks>
/// No shipped brush runs the simulation any more: Gouache and Ink wash became
/// brushes built from parts that land under the pen, after Watercolor (Q244)
/// and Oil (Q246). The simulation is still in the engine - a brush an artist
/// makes can switch a medium on, and drawings made with one still render with
/// it - until phase 4 of the paint path deletes it (Q240). Tests of it used to
/// take a shipped brush as their subject; they take these instead, and go when
/// the simulation does.
/// </remarks>
internal static class SimulatedBrushes
{
    public static BrushSettings InkWash() => new()
    {
        Size = 30, Hardness = 0.35, Opacity = 0.8, Flow = 0.6, Spacing = 0.07,
        PressureSizeGamma = 1.4,
        Mixing = new ColourMixing { Amount = 0.6, Length = 0.2, Reach = 0.9 },
        Medium = new MediumSettings
        {
            Kind = MediumKind.Ink,
            Wetness = 0.9, Viscosity = 0.05, Drag = 0.15, FlowSteps = 18,
            Absorbency = 0.75, EdgePull = 0.3,
            PigmentDensity = 0.8, Granularity = 0.05, Hiding = 0.4,
            Paper = PaperKind.Smooth, PaperScale = 6, PaperInfluence = 0.25,
            PressureWater = 0.5, Rewetting = 0,
        },
    };

    public static BrushSettings Gouache() => new()
    {
        Size = 26, Hardness = 0.7, Opacity = 0.95, Flow = 0.85, Spacing = 0.1,
        TipId = "tip-builtin-paintbrush",
        AngleFollowsDirection = true, SizeJitter = 0.1,
        Mixing = new ColourMixing { Amount = 0.8, Length = 0.15, Reach = 0.9 },
        Medium = new MediumSettings
        {
            Kind = MediumKind.Gouache,
            Wetness = 0.3, Viscosity = 0.75, Drag = 0.7, FlowSteps = 6,
            Absorbency = 0.8, EdgePull = 0.05,
            PigmentDensity = 0.9, Granularity = 0.15, Hiding = 0.9,
            Paper = PaperKind.ColdPress, PaperScale = 10, PaperInfluence = 0.35,
            Body = 0.35, Relief = 0.2, PaintLoad = 0.63,
            PressureWater = 0.15, PressureMix = 0.8, Rewetting = 0,
        },
    };
}
