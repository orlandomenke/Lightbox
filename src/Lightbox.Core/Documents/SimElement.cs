using Lightbox.Core.Effects;

namespace Lightbox.Core.Documents;

/// <summary>
/// Fuel turning into heat: what lets a piece of flame that has left the column
/// go on burning instead of going out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> Without it heat is only ever stamped at an emitter and
/// then decays, so a parcel that detaches has nothing sustaining it and fades
/// below the outermost band inside one frame. Measured on the window's own
/// fire: six real sheds over forty frames, <em>every one of them lasting a
/// single frame</em>. Real flame tips detach and keep burning, because they
/// carry their fuel with them.
/// </para>
/// <para>
/// <b>Density is the fuel</b>, which is why this needs no field of its own. An
/// emitter already stamps density beside heat, advection already carries it,
/// and a detached parcel therefore already holds a fuel supply — the only thing
/// missing was permission to spend it.
/// </para>
/// <para>
/// <b>It is self-limiting, and that is the part worth keeping.</b> Burning
/// consumes the fuel, so heat production falls away as the parcel spends
/// itself while <c>Cooling</c> goes on taking its cut at a constant rate.
/// Burning holds a parcel up while it has fuel, the fuel runs out, and cooling
/// has it again — what is left is cool density, which is to say smoke. That is
/// the same arc a fireball has, so <see cref="Emitter.Burst"/> gets it for free
/// rather than needing a second mechanism.
/// </para>
/// <para>
/// <b>Which is the reason it is not simply a lower <c>Cooling</c>.</b> Cooling
/// slowly does keep a detached parcel alive — measured, a flame at
/// <c>Cooling = 0.01</c> sheds pieces that last 12 and 26 frames instead of one
/// — but it is the same number that gives a flame its length, so the flame goes
/// from 21 cells tall to 43 on the way. One knob was doing two jobs; this is
/// the second job moved somewhere it can be set on its own.
/// </para>
/// <para>
/// <b>A value type, deliberately.</b> <see cref="SimParams"/> is a record and
/// <c>SimElement.Clone</c> copies it with <c>with { }</c>, which copies a
/// reference rather than what it points at — so a class here would leave two
/// elements sharing one block, and editing a duplicated effect would edit the
/// original. <see cref="EmitterScatter"/> can be a class because its owner
/// clones it by hand; this cannot.
/// </para>
/// </remarks>
/// <remarks>
/// <b>The defaults were measured, not chosen.</b> A torch on a 48x88 grid, forty
/// frames, counting only sheds of five cells or more:
///
/// <code>
///   off                          21 cells tall | 6 sheds, every one 1 frame
///   0.02 / 0.3 / 3, vorticity .35  29 cells   | 10 sheds, up to 4 frames
///   0.02 / 0.3 / 3, vorticity .7   26 cells   |  7 sheds, up to 8 frames
///   0.02 / 0.3 / 3, vorticity 1.0  23 cells   | 11 sheds, up to 4 frames
/// </code>
///
/// <b>Burning makes a fire hotter, and a hotter fire climbs</b> — so on a grid
/// with room overhead this lengthens the flame, and <c>Vorticity</c> is what
/// takes the rise back by spending the buoyancy on curl instead. That trade is
/// the recipe rather than a defect and the manual states it. It is also
/// <em>grid-dependent</em>, which is the part worth not assuming: on the 44-cell
/// grid a new element gets, the flame has no room to use the extra heat and the
/// same block costs 3% of the height with vorticity left alone.
/// </remarks>
public readonly record struct Combustion
{
    public Combustion() { }

    /// <summary>
    /// Fraction of the fuel present that burns per step, where it is hot enough
    /// to burn at all.
    /// </summary>
    public double Rate { get; init; } = 0.02;

    /// <summary>
    /// How hot fuel has to be before it burns, in the same units as
    /// <see cref="Emitter.Heat"/> — which is 1 at an emitter's centre by
    /// default, so the useful range is a fraction of that.
    /// </summary>
    /// <remarks>
    /// <b>Absolute rather than a fraction of the element's peak</b>, which
    /// <see cref="SimElement.BandLow"/> is and which would be wrong here. A band
    /// level reads a field that has already been computed; an ignition point
    /// decides what the field becomes, so scaling it by the peak would make the
    /// threshold depend on the burning it is gating. Bands may follow the
    /// result. This has to stand outside it.
    /// </remarks>
    public double Ignition { get; init; } = 0.3;

    /// <summary>Heat released per unit of fuel burned.</summary>
    /// <remarks>
    /// Separate from <see cref="Rate"/> because the two say different things:
    /// rate is how long the parcel lasts, yield is how fiercely it burns while
    /// it does. Fast and dim is a flash; slow and bright is an ember.
    /// </remarks>
    public double Yield { get; init; } = 3;
}

/// <summary>
/// What the solver does with the fluid, frame after frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>In the document rather than in the solver, for the reason
/// <see cref="BrushSettings"/> is.</b> Core describes what a mark is and Raster
/// carries it out; a solver that owned its own parameter type would make every
/// tuning change a file-format change, and would leave two sets of the same
/// numbers to drift apart. `FluidSolver` reads this directly, exactly as
/// `BrushEngine` reads a brush.
/// </para>
/// <para>
/// Every value is per <em>step</em>, not per frame or per second — a step is the
/// solver's unit and substeps are how an element buys speed. Velocities are in
/// cells per step, which is what makes the whole record independent of how
/// finely the element is simulated.
/// </para>
/// </remarks>
/// <remarks>
/// <para>
/// <b>The defaults were tuned against a render, not taken from the solver's test
/// fixture</b> — which is what they were at first, and it showed. Every test
/// passed: momentum conserved, fluid incompressible, smoke neither created nor
/// destroyed. It simply did not look like fire. Three separate things were
/// wrong and only a picture could have said so:
/// </para>
/// <list type="bullet">
/// <item><b>The turbulence was acting as wind.</b> A noise scale of twelve cells
/// on a seventy-cell element is a push the width of the plume, so the flame was
/// blown sideways rather than made wispy. Six reads as turbulence.</item>
/// <item><b>The flame stalled.</b> Buoyancy is proportional to heat, so a plume
/// that cools slowly is a plume that stops climbing while it is still visible.
/// Hotter and cooling faster gives the short bright tongue a flame actually
/// has.</item>
/// <item><b>Nothing damped the flow</b>, so a sustained plume in a closed box
/// accumulated circulation until the flame lay over. See <see cref="Drag"/>.</item>
/// </list>
/// </remarks>
public sealed record SimParams
{
    /// <summary>How hard heat lifts. Up is −Y, the document's own convention.</summary>
    public double Buoyancy { get; set; } = 0.35;

    /// <summary>How hard density sinks — smoke is heavier than the air it rides.</summary>
    public double Weight { get; set; } = 0.01;

    /// <summary>How much of the curl advection eats is put back. What keeps a plume curling.</summary>
    public double Vorticity { get; set; } = 0.35;

    /// <summary>
    /// Fraction of the flow's speed lost per step — the air's own viscosity, and
    /// the thing that stops a closed element turning into a lava lamp.
    /// </summary>
    /// <remarks>
    /// <b>Added at step 4 because the render demanded it, and measured rather
    /// than assumed.</b> Worst sideways drift of the plume's centre of mass over
    /// the second half of a forty-frame element:
    ///
    /// <code>
    ///   drag 0.00    7.0 cells
    ///   drag 0.05    3.6 cells
    ///   drag 0.12    0.9 cells
    /// </code>
    ///
    /// The default is 0.05 rather than 0.12 on purpose: the higher figure gives
    /// a flame that stands dead still, and a flame that never wavers reads as
    /// fake. Halving the drift while leaving it some life is the trade.
    /// An open top — letting the plume leave rather than recirculate — is the
    /// other half of this and stays deferred; drag is what a closed element
    /// needs either way.
    /// </remarks>
    public double Drag { get; set; } = 0.05;

    /// <summary>Amplitude of the seeded curl-noise force.</summary>
    public double Turbulence { get; set; } = 0.35;

    /// <summary>Cells per noise cell. Small is fine detail, large is slow billow.</summary>
    public double TurbulenceScale { get; set; } = 6;

    /// <summary>Noise-space units the field travels per step, so the turbulence evolves.</summary>
    public double TurbulenceDrift { get; set; } = 0.05;

    /// <summary>Fraction of density lost per step.</summary>
    public double Dissipation { get; set; } = 0.004;

    /// <summary>Fraction of temperature lost per step.</summary>
    public double Cooling { get; set; } = 0.06;

    /// <summary>
    /// Fuel burning into heat, or null for a fluid that only ever cools.
    /// </summary>
    /// <remarks>
    /// Absent by default, on <see cref="SimElement.PreRoll"/>'s precedent: every
    /// field above describes any fluid and is always written, while this is a
    /// feature somebody switches on for fire. Steam does not burn, and a
    /// document full of smoke should not carry three keys saying so.
    /// </remarks>
    public Combustion? Burning { get; set; }
}

/// <summary>Where an emitter puts fluid in.</summary>
public enum EmitterShape
{
    Point,

    /// <summary>A line from (X,Y) to (X2,Y2) — a burner, a trail, the lip of a waterfall.</summary>
    Segment,

    Disc,
}

/// <summary>
/// One source feeding an element: where fluid enters, how much, and how hot.
/// </summary>
/// <remarks>
/// <b>Positions are in cell units, element-local</b>, not document pixels. An
/// element is a box that can be moved around the canvas, and an emitter has to
/// travel with it; storing canvas coordinates would leave the flame behind when
/// the box moved. Grid resolution is a property of the element and changing it
/// is re-authoring rather than rescaling, so cell units are stable under
/// everything that is not already a re-author.
/// </remarks>
/// <summary>
/// Turns one emitter into many small ones scattered over the same area —
/// flames along a hem rather than a hem that is on fire.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem it solves is that an area emitter has no gaps.</b> A masked
/// emitter feeds every inked cell every frame, so a garment reads as one
/// continuous burning edge: nothing can detach, because whatever leaves is
/// immediately replaced from below. Q125 found this by rendering it, and the
/// first answer proposed was emission that flickered *in time*. Scatter is the
/// better answer to the same problem because it is <em>spatial and stable</em>:
/// the gaps are in the same places every frame, so what rises off a site
/// actually leaves. Flicker is still wanted, for shimmer, and it is now a
/// separate want rather than the fix for this.
/// </para>
/// <para>
/// <b>Coverage rather than a count.</b> A site count would have to be
/// re-chosen every time the garment was redrawn bigger; a fraction does not,
/// which is the whole point — paint a longer hem and you get proportionally
/// more flames with the same numbers.
/// </para>
/// <para>
/// <b>Height varies on its own, and that was measured rather than designed.</b>
/// The plan was that <see cref="HeatVariation"/> would put a tall flame beside
/// a short one, on the reasoning that a flame is as tall as its heat survives
/// <c>Cooling</c>. It does not: with <em>both</em> variations at zero, a
/// scattered hem already burns at heights of 10, 16, 24, 30, 38, 42 and 44
/// cells, and turning either variation up moves the spread by less than the
/// noise. The fluid is what makes them differ — a site with neighbours either
/// side is fed by their rising column and runs tall, one on the end of a run
/// does not and stays short. So scatter gives height variation for free, and
/// the two controls below do something else.
/// </para>
/// <para>
/// Every draw is <see cref="DeterministicHash"/> over the site's own position,
/// never an index and never a clock, so invariant 2 holds exactly as it does
/// for a brush: the same document scatters the same way on every machine, and a
/// re-bake is identical.
/// </para>
/// </remarks>
public sealed class EmitterScatter
{
    /// <summary>
    /// What fraction of the candidate sites actually burn, 0..1.
    /// </summary>
    /// <remarks>
    /// At 1 every lattice point is a site and the effect is an even field of
    /// flames; low values leave most of the surface alone, which is what a
    /// garment catching light looks like before it is properly alight.
    /// </remarks>
    public double Coverage { get; set; } = 0.5;

    /// <summary>
    /// Cells between candidate sites, before jitter.
    /// </summary>
    /// <remarks>
    /// <b>A lattice rather than a per-cell roll</b>, because a per-cell roll
    /// clumps: neighbouring cells are independent, so sites land in twos and
    /// threes with bald patches between them, which reads as noise rather than
    /// as flames. A jittered lattice gives an even spread and a natural-looking
    /// irregularity for the same cost. Below the emitter's own radius the sites
    /// overlap into a continuous sheet, which is the old behaviour and is
    /// sometimes what is wanted.
    /// </remarks>
    public double Spacing { get; set; } = 6;

    /// <summary>
    /// How far a site's own size may stray, as a fraction — the <em>width</em>
    /// of each flame's base.
    /// </summary>
    /// <remarks>
    /// A fraction of <see cref="Spacing"/> rather than of the emitter's radius;
    /// see there for why. It reads at the base and not up the flame: measured
    /// with the sites far enough apart not to touch, 0.6 gives bases of 7 to 21
    /// cells against a flat 15, and once they overlap the merged runs hide it
    /// again.
    /// </remarks>
    public double SizeVariation { get; set; } = 0.4;

    /// <summary>
    /// How far a site's own heat may stray, as a fraction — how <em>fiercely</em>
    /// each flame burns.
    /// </summary>
    /// <remarks>
    /// <b>Intensity, not height</b>, and the difference was found by measuring
    /// rather than by reasoning — see the remark on the class. It widens the
    /// spread of peak temperature across flames by about a third (0.177 to
    /// 0.238 at 0.6), and because fire bands <em>from</em> temperature that is
    /// exactly which colours each flame reaches: some running up into the pale
    /// core, others staying in the dull red. On a smoke element, where the
    /// bands read density, it changes how hard each site lifts instead.
    /// </remarks>
    public double HeatVariation { get; set; } = 0.4;

    /// <summary>
    /// A sideways push given to each site, in cells per step, varying per site.
    /// </summary>
    /// <remarks>
    /// Without it every flame rides the same turbulence field and they sway in
    /// lockstep, which reads as a printed pattern moving rather than as a row
    /// of separate flames. A per-site lean is the cheapest thing that
    /// decorrelates them, and being seeded from the site's position it is
    /// steady: a given flame leans the same way for as long as it burns.
    /// </remarks>
    public double Drift { get; set; }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public EmitterScatter Clone() => (EmitterScatter)MemberwiseClone();
}

public sealed class Emitter
{
    public string Id { get; set; } = Ids.NewId("em");

    public EmitterShape Shape { get; set; } = EmitterShape.Disc;

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>The far end, for <see cref="EmitterShape.Segment"/>.</summary>
    public double X2 { get; set; }

    public double Y2 { get; set; }

    /// <summary>Cells, for <see cref="EmitterShape.Disc"/>.</summary>
    public double Radius { get; set; } = 3;

    /// <summary>Density added per frame at the centre.</summary>
    public double Density { get; set; } = 1;

    /// <summary>Heat added per frame at the centre. This is what makes fire rise.</summary>
    public double Heat { get; set; } = 1;

    /// <summary>An initial push, in cells per step.</summary>
    public double VelocityX { get; set; }

    public double VelocityY { get; set; }

    /// <summary>
    /// An initial push <em>outward from this emitter's own centre</em>, in cells
    /// per step. Zero on everything that is not a blast.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the difference between an explosion and a plume</b>, and it is
    /// not a special case of <see cref="VelocityX"/>. Those two are one vector
    /// for the whole emitter, so a disc pushed by them moves sideways as a lump;
    /// an explosion pushes every part of itself in a <em>different</em>
    /// direction, which is what makes the front expand as a ring rather than
    /// translate. There is no pair of numbers that does that, so it is its own
    /// one.
    /// </para>
    /// <para>
    /// The two compose, and usefully: a burst plus a directional push is a
    /// muzzle flash or a vent — expanding, and going somewhere.
    /// </para>
    /// <para>
    /// It is carried as a <em>volume source</em> in the pressure solve rather
    /// than as an outward velocity — see <c>FluidSolver.AddExpansion</c>, which
    /// also records the measurement, and the part of it that refuted the
    /// obvious reasoning. Either way it is one number per emitter rather than a
    /// vector, because "outward" is not a direction: it is a different direction
    /// at every point of the stamp, which is exactly what no pair of numbers can
    /// say.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Nullable so an emitter that is not a blast writes no key, on
    /// <see cref="SimElement.PreRoll"/>'s precedent: <see cref="VelocityX"/> and
    /// <see cref="X2"/> describe every emitter and are always written, while
    /// this is a feature somebody switches on.
    /// </remarks>
    public double? Burst { get; set; }

    /// <summary>
    /// The first frame this emitter feeds the fluid on, or null for "from the
    /// element's own first frame".
    /// </summary>
    /// <remarks>
    /// <b>Absent by default because most emitters run for the whole element</b> —
    /// a fire burns, a chimney smokes. What needs these is the thing that
    /// happens *once*: a blast is a frame or two of emission and then a lot of
    /// consequences, and an emitter that keeps feeding it refuels the fireball
    /// every frame so it never becomes smoke and never disperses. That is the
    /// same failure a painted area mask has (Q125's finding), arriving through a
    /// different door.
    /// </remarks>
    public int? EmitFrom { get; set; }

    /// <summary>
    /// The frame this emitter stops on, exclusive, or null for "to the end of
    /// the element".
    /// </summary>
    public int? EmitUntil { get; set; }

    /// <summary>Whether this emitter feeds the fluid on a timeline frame.</summary>
    public bool EmitsOn(int frame) =>
        (EmitFrom is not { } from || frame >= from) && (EmitUntil is not { } until || frame < until);

    /// <summary>Whether emission is bounded at all. Derived; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTimed => EmitFrom is not null || EmitUntil is not null;

    /// <summary>
    /// The layer whose ink says where this emitter emits, or null for one of the
    /// plain shapes above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The mask is the emission, whole</b> (Q125). It is not intersected with
    /// anything at bake time, and in particular not with whatever the costume is
    /// drawing this frame: alpha lock belongs to the <em>painter</em>, keeping a
    /// brush on the garment while a mask is made, and it is optional even there.
    /// An intersection at bake time would let a hem swinging away silently
    /// extinguish the fire on it, with nothing in the record to say why.
    /// </para>
    /// <para>
    /// Because a mask is an ordinary layer it has cels, so it can be redrawn on
    /// the frames that need it, held on the ones that do not, carried by the
    /// inbetweener and bound to a rig — none of which this emitter has to know
    /// about. What the emitter supplies is <em>where the stamp sits</em>; the
    /// layer supplies what shape it is.
    /// </para>
    /// </remarks>
    public string? MaskLayerId { get; set; }

    /// <summary>
    /// Where this emitter travels, keyed, as an offset from <see cref="X"/> and
    /// <see cref="Y"/> in cells. Null on an emitter that stays put.
    /// </summary>
    /// <remarks>
    /// An offset rather than the position itself, so placing an emitter and
    /// animating it stay separate acts: drag it where it belongs, then key the
    /// travel relative to that, and moving the element later does not invalidate
    /// the keys.
    /// </remarks>
    public EffectParam? MotionX { get; set; }

    public EffectParam? MotionY { get; set; }

    /// <summary>
    /// Scatter this emitter into many small ones over the same area, or null to
    /// feed the whole of it as one source.
    /// </summary>
    /// <remarks>
    /// Absent by default: a torch is one flame and should write no keys about
    /// not being several.
    /// </remarks>
    public EmitterScatter? Scatter { get; set; }

    /// <summary>Whether this emitter travels at all. Derived; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Travels => MotionX is not null || MotionY is not null;

    /// <summary>Where this emitter's origin sits on a timeline frame, in cells.</summary>
    public (double X, double Y) OriginAt(int frame) =>
        (X + (MotionX?.At(frame) ?? 0), Y + (MotionY?.At(frame) ?? 0));

    public Emitter Clone()
    {
        var copy = (Emitter)MemberwiseClone();
        copy.MotionX = MotionX?.Clone();
        copy.MotionY = MotionY?.Clone();
        copy.Scatter = Scatter?.Clone();
        return copy;
    }
}

/// <summary>
/// The particle pass riding the same velocity field: embers, spray, sparks.
/// </summary>
/// <remarks>
/// Absent unless used, like everything else optional here. Q116 put particles in
/// the first slice rather than later, and fire is what makes that pay — a flame
/// without embers reads as a flat shape.
/// </remarks>
public sealed class ParticleSpec
{
    /// <summary>How many are spawned per frame.</summary>
    public int PerFrame { get; set; } = 12;

    /// <summary>How many frames one survives.</summary>
    public int Lifetime { get; set; } = 8;

    /// <summary>Stroke width, in document pixels, before the brush's own dynamics.</summary>
    public double Size { get; set; } = 2;

    public string Color { get; set; } = "#ffcc66";

    public string? BrushPresetId { get; set; }

    public ParticleSpec Clone() => (ParticleSpec)MemberwiseClone();
}

/// <summary>
/// One authored effects element: a deterministic simulation that writes
/// drawings into a range of frames.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the authoring record, not the output</b> (Q116). Baking writes
/// ordinary <see cref="ToolKind.Fill"/> and <see cref="ToolKind.Brush"/> strokes
/// into the frames, each carrying <see cref="Stroke.SimId"/> so a re-bake knows
/// what to replace; the strokes are what the renderer, the picker, the
/// transform, undo, export and the AI payload all see, and none of them needs to
/// know an element exists. That is the same relationship
/// <see cref="StrokePath"/> has with <c>Stroke.Points</c>: an authored
/// description alongside, with the geometry staying the truth.
/// </para>
/// <para>
/// <b><see cref="Kind"/> is a string id resolved through a registry</b>, not an
/// enum, for the reason the brush tip registry already took: an element of a
/// kind this build does not know — a document from a newer version — is
/// preserved on save and skipped on bake, never dropped.
/// </para>
/// <para>
/// <b>Absent until authored.</b> A document with no elements writes no
/// <c>sims</c> key at all, and every optional part of an element that is not
/// used writes no key either. <c>SimElementSerializationTests</c> is the cheap
/// half of that promise and ships beside the record, because the medium block
/// paid for the expensive half once already.
/// </para>
/// </remarks>
public sealed class SimElement
{
    public string Id { get; set; } = Ids.NewId("sim");

    /// <summary>What is being simulated: <c>fire</c>, <c>smoke</c>, <c>steam</c>.</summary>
    public string Kind { get; set; } = "smoke";

    /// <summary>An artist's name for this element, or null.</summary>
    public string? Name { get; set; }

    public int FirstFrame { get; set; }

    public int FrameCount { get; set; } = 24;

    /// <summary>
    /// Bake one drawing every this many frames and hold it. Two is animating on
    /// 2s, which halves the boil per-frame tracing produces (Q116) and is what an
    /// animator would do anyway.
    /// </summary>
    public int ExposeOn { get; set; } = 1;

    public int GridWidth { get; set; } = 192;

    public int GridHeight { get; set; } = 108;

    /// <summary>Where cell (0,0)'s corner sits in the document.</summary>
    public double OriginX { get; set; }

    public double OriginY { get; set; }

    /// <summary>Document pixels per cell. The grid is deliberately coarser than the document.</summary>
    public double Scale { get; set; } = 10;

    /// <summary>Solver steps per frame. The way to buy speed without breaking the CFL bound.</summary>
    public int Substeps { get; set; } = 8;

    /// <summary>
    /// Frames to simulate before the first drawn one, so an element opens on an
    /// established plume rather than on still air.
    /// </summary>
    /// <remarks>
    /// Q122. It fixes the commonest complaint before anybody reports it — the
    /// first half-second of an effect looking thin — and it does <em>not</em>
    /// make a cycle seamless, which is a separate and unsolved thing. Costs one
    /// frame of solve each, and nothing at all when it is zero.
    /// <para>
    /// Nullable so an element that does not pre-roll writes no key. The
    /// distinction is worth keeping: <see cref="Substeps"/> and
    /// <see cref="GridWidth"/> describe every element and are always written,
    /// while this is a feature somebody switches on — the same line
    /// <see cref="Particles"/> sits on. Caught by its own test rather than
    /// reasoned about, which is the point of writing that test first.
    /// </para>
    /// </remarks>
    public int? PreRoll { get; set; }

    /// <summary>
    /// Ambient flow across the element, in cells per step, keyed over its frames.
    /// Null on an element nobody has blown on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two scalars rather than an angle and a strength</b>, and that is not a
    /// convenience. Keying an angle wraps: a gust swinging from 10° to 350° is a
    /// twenty-degree shift that interpolates the long way round, through every
    /// direction the artist did not ask for. Components cannot do that. The UI
    /// is free to present a dial and a length; the record stores what
    /// interpolates correctly.
    /// </para>
    /// <para>
    /// <b>Calibration, measured by rendering it.</b> Wind is in the same units as
    /// the flow it acts on, and a plume rises at roughly 0.15 cells per step — so
    /// a wind of that order bends a flame by about half a right angle, and 0.5
    /// lays it flat and horizontal. The useful range for a figure in motion is
    /// well under a tenth of what the field's own maximum speed suggests, which
    /// is not what anybody would guess from the number.
    /// </para>
    /// <para>
    /// <b>A character running right is wind blowing left.</b> That is a change of
    /// reference frame rather than weather, and it is the one thing about this
    /// field an artist will get backwards — a run cycle runs on the spot, so
    /// nothing here can be derived from her motion. The manual has to say it.
    /// </para>
    /// </remarks>
    public EffectParam? WindX { get; set; }

    public EffectParam? WindY { get; set; }

    /// <summary>Whether this element is blown on at all. Derived; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasWind => WindX is not null || WindY is not null;

    /// <summary>The ambient flow on a timeline frame, in cells per step.</summary>
    public (double X, double Y) WindAt(int frame) => (WindX?.At(frame) ?? 0, WindY?.At(frame) ?? 0);

    public SimParams Params { get; set; } = new();

    public List<Emitter> Emitters { get; set; } = [];

    /// <summary>
    /// Which field the bands read. Fire bands from temperature — the ramp is the
    /// drawing — and smoke from density.
    /// </summary>
    public bool BandsFromHeat { get; set; }

    /// <summary>
    /// The range the bands are spread through, as a fraction of the highest
    /// value this element's field reaches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A fraction rather than an absolute level, and that was found by
    /// looking.</b> Temperature is unbounded — a flame that burns longer is
    /// simply hotter — so an absolute <c>BandHigh</c> of 1 against a field
    /// peaking at 3 put every band inside the brightest core and drew none of
    /// the plume. The artist would have had to discover the number by
    /// experiment, and rediscover it after every change to the emitter.
    /// </para>
    /// <para>
    /// Taken over the <em>whole element</em> (<c>SolvedElement.PeakBand</c>), never
    /// per frame: a range following each frame's own peak would rescale the
    /// bands every frame, and a band that moves because its scale moved is
    /// flicker.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>The window sits low in the range, and that was measured.</b> A plume's
    /// field is steeply peaked — at ordinary settings only 4% of an element's
    /// cells hold more than a hundredth of its peak, and 0.3% more than a
    /// quarter of it. Bands spread over the whole range therefore all land
    /// inside the brightest core and draw scraps. From 2% to a third of peak
    /// puts the outermost band around the visible edge of the plume, which is
    /// where a silhouette belongs.
    /// </remarks>
    public double BandLow { get; set; } = 0.02;

    public double BandHigh { get; set; } = 0.34;

    /// <summary>A colour per band, outermost first. How <em>many</em> bands there are is style, and lives on the treatment.</summary>
    public List<string> BandColors { get; set; } = [];

    public string OutlineColor { get; set; } = "#1a1a1a";

    /// <summary>
    /// The pen the outlines are drawn with, or null for the default brush.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>On the element rather than read from the toolbar at bake time.</b> A
    /// bake that took whatever brush happened to be selected would draw a
    /// different line every time it ran — the same picture re-baked after
    /// picking up a marker would come back inked with a marker, and an artist
    /// would have no way to say what an element's line <em>is</em>. That is
    /// invariant 4's reasoning one level up: a setting that reaches pixels
    /// belongs to the record that produced them.
    /// </para>
    /// <para>
    /// Null is the ordinary case and writes no key. The window's "use the
    /// current brush" button copies the toolbar's settings in here, which makes
    /// taking the brush an action with a result rather than an invisible
    /// dependency. The brush's <see cref="BrushSettings.Size"/> is also the
    /// stroke width every treatment distance is measured in, so it is not only
    /// decoration.
    /// </para>
    /// </remarks>
    public BrushSettings? OutlineBrush { get; set; }

    /// <summary>
    /// The layer whose ink blocks the fluid — the figure and her costume — or
    /// null for an element nothing gets in the way of.
    /// </summary>
    /// <remarks>
    /// Named separately from any emitter's mask on purpose (Q125). What blocks
    /// and what emits are different questions: fire leaves a sleeve while the
    /// whole torso still occludes it, and folding the two together would send
    /// flames straight through the character — which is exactly what makes an
    /// effect read as pasted on rather than attached.
    /// </remarks>
    public string? ObstacleLayerId { get; set; }

    /// <summary>The shared line treatment this element follows, or null for the defaults.</summary>
    public string? TreatmentId { get; set; }

    /// <summary>
    /// Overrides on top of that treatment — the same record, because every field
    /// is nullable and the defaults live in one place (Q118).
    /// </summary>
    public LineTreatment? Treatment { get; set; }

    /// <summary>The ember pass, or null — and null is every element that does not want one.</summary>
    public ParticleSpec? Particles { get; set; }

    /// <summary>
    /// How many bands this element draws — a style decision, so it comes from
    /// the treatment rather than from here.
    /// </summary>
    public int Bands() => LineTreatment.Resolve(null, Treatment).Bands;

    /// <summary>Frames this element covers, as a half-open range.</summary>
    public bool Covers(int frame) => frame >= FirstFrame && frame < FirstFrame + FrameCount;

    /// <summary>A copy holding no reference in common with this one.</summary>
    public SimElement Clone()
    {
        var copy = (SimElement)MemberwiseClone();
        copy.Params = Params with { };
        copy.Emitters = Emitters.Select(e => e.Clone()).ToList();
        copy.BandColors = [.. BandColors];
        copy.Treatment = Treatment?.Clone();
        copy.Particles = Particles?.Clone();
        copy.WindX = WindX?.Clone();
        copy.WindY = WindY?.Clone();
        copy.OutlineBrush = OutlineBrush?.Clone();
        return copy;
    }
}
