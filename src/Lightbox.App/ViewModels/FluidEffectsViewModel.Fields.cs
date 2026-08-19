using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;
using Lightbox.Core.Effects;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Every tunable on an effect, as rows rather than as controls.
/// </summary>
/// <remarks>
/// <para>
/// <b>Adding a solver parameter is a line here and a line in
/// <c>SolveFingerprint</c>, and nothing in XAML.</b> The alternative — a control
/// per number — is how a panel of forty numbers drifts: two ranges that disagree
/// with the record, one field that forgot to mark the preview stale, one that
/// nobody wired at all. A row carries its own range, its own cost and its own
/// revert, so the window is four lists and one template.
/// </para>
/// <para>
/// <b>The ranges are authoring limits, not physical ones.</b> They are what a
/// slider spans, and several of them are far narrower than the solver will
/// accept — wind is the worked example: the useful range for a figure in motion
/// is under a tenth of what the field's own maximum speed suggests, which is not
/// what anybody would guess from the number. Where a range was measured by
/// rendering it, the hint says so.
/// </para>
/// </remarks>
public sealed partial class FluidEffectsViewModel
{
    /// <summary>Where the element sits and how long it lasts.</summary>
    public ObservableCollection<EffectFieldRow> PlacementFields { get; } = [];

    /// <summary>What the fluid does — every one of these forces a re-simulation.</summary>
    public ObservableCollection<EffectFieldRow> PhysicsFields { get; } = [];

    /// <summary>Where the bands land in the field's range.</summary>
    public ObservableCollection<EffectFieldRow> BandFields { get; } = [];

    /// <summary>The line treatment, which is the one cascade in the window.</summary>
    public ObservableCollection<EffectFieldRow> TreatmentFields { get; } = [];

    /// <summary>The selected emitter's own numbers.</summary>
    public ObservableCollection<EffectFieldRow> EmitterFields { get; } = [];

    /// <summary>The element's emitters.</summary>
    public ObservableCollection<EmitterRow> Emitters { get; } = [];

    [ObservableProperty]
    private EmitterRow? _selectedEmitter;

    // ---- the plain-text and list-valued settings ---------------------------------

    /// <summary>The artist's name for the element, or empty.</summary>
    public string ElementName
    {
        get => Element?.Name ?? string.Empty;
        set
        {
            if (Element is not { } element) return;
            element.Name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
            Selected?.Relabel();
        }
    }

    /// <summary>A colour per band, outermost first, as space-separated hexes.</summary>
    /// <remarks>
    /// Text rather than a swatch strip, for now, and deliberately not disguised
    /// as finished: it is the shape of the setting an artist actually edits — a
    /// ramp — and a proper ramp editor is a control, not a field. Splitting on
    /// whitespace and commas both, because somebody will type either.
    /// </remarks>
    public string BandColorsText
    {
        get => Element is { } element ? string.Join(" ", element.BandColors) : string.Empty;
        set
        {
            if (Element is not { } element) return;
            element.BandColors = (value ?? string.Empty)
                .Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            OnPropertyChanged();
            RefreshPreview();
        }
    }

    public string OutlineColorHex
    {
        get => Element?.OutlineColor ?? "#1a1a1a";
        set
        {
            if (Element is not { } element || string.IsNullOrWhiteSpace(value)) return;
            element.OutlineColor = value.Trim();
            OnPropertyChanged();
            RefreshPreview();
        }
    }

    /// <summary>Every layer in the document, plus "none", for the two layer pickers.</summary>
    public ObservableCollection<string> LayerChoices { get; } = [];

    /// <summary>The layer whose ink the fluid cannot pass through.</summary>
    public string ObstacleLayer
    {
        get => NameOfLayer(Element?.ObstacleLayerId);
        set
        {
            if (Element is not { } element) return;
            element.ObstacleLayerId = IdOfLayer(value);
            OnPropertyChanged();
            MarkStale();
        }
    }

    /// <summary>The layer whose ink the selected emitter emits from.</summary>
    public string EmitterMaskLayer
    {
        get => NameOfLayer(SelectedEmitter?.Emitter?.MaskLayerId);
        set
        {
            if (SelectedEmitter?.Emitter is not { } emitter) return;
            emitter.MaskLayerId = IdOfLayer(value);
            OnPropertyChanged();
            SelectedEmitter.Relabel();
            MarkStale();
        }
    }

    private const string NoLayer = "— none —";

    private string NameOfLayer(string? id) =>
        id is null ? NoLayer
            : _vm.Doc.Scene.Layers.FirstOrDefault(l => l.Id == id)?.Name ?? NoLayer;

    private string? IdOfLayer(string? name) =>
        name is null or NoLayer ? null : _vm.Doc.Scene.Layers.FirstOrDefault(l => l.Name == name)?.Id;

    // ---- emitters -----------------------------------------------------------------

    public void NewEmitter()
    {
        if (Element is not { } element) return;

        var emitter = new Emitter
        {
            Shape = EmitterShape.Disc,
            X = element.GridWidth / 2.0,
            Y = element.GridHeight - 6,
            Radius = 4,
            Density = 1,
            Heat = element.BandsFromHeat ? 1 : 0,
        };
        element.Emitters.Add(emitter);

        var row = new EmitterRow(emitter.Id, FindEmitter);
        Emitters.Add(row);
        SelectedEmitter = row;
        MarkStale();
    }

    public void DeleteEmitter()
    {
        if (Element is not { } element || SelectedEmitter is not { } row) return;

        element.Emitters.RemoveAll(e => e.Id == row.Id);
        Emitters.Remove(row);
        SelectedEmitter = Emitters.FirstOrDefault();
        MarkStale();
    }

    private Emitter? FindEmitter(string id) => Element?.Emitters.FirstOrDefault(e => e.Id == id);

    partial void OnSelectedEmitterChanged(EmitterRow? value)
    {
        BuildEmitterFields();
        OnPropertyChanged(nameof(EmitterMaskLayer));
    }

    // ---- building -----------------------------------------------------------------

    partial void OnSelectedChanged(SimElementRow? value) => BuildFields();

    /// <summary>
    /// Rebuild every row for the selected element.
    /// </summary>
    /// <remarks>
    /// <b><c>_building</c> is not a tidiness guard.</b> Constructing a row reads
    /// its value, and assigning <see cref="SelectedEmitter"/> below raises a
    /// change that rebuilds more rows — without the fence, opening an element
    /// would kick off a solve per row on a window that has not been shown yet.
    /// It is raised and lowered in a <c>finally</c> for B39's reason: a guard
    /// left up silently suppresses every subsequent edit.
    /// </remarks>
    private void BuildFields()
    {
        _building = true;
        try
        {
            LayerChoices.Clear();
            LayerChoices.Add(NoLayer);
            foreach (var layer in _vm.Doc.Scene.Layers) LayerChoices.Add(layer.Name);

            BuildPlacementFields();
            BuildPhysicsFields();
            BuildBandFields();
            BuildTreatmentFields();

            Emitters.Clear();
            if (Element is { } element)
            {
                foreach (var emitter in element.Emitters) Emitters.Add(new EmitterRow(emitter.Id, FindEmitter));
            }
            SelectedEmitter = Emitters.FirstOrDefault();
            BuildEmitterFields();
        }
        finally
        {
            _building = false;
        }

        foreach (var name in new[]
                 {
                     nameof(ElementName), nameof(BandColorsText), nameof(OutlineColorHex),
                     nameof(ObstacleLayer), nameof(EmitterMaskLayer), nameof(PenSummary),
                 })
        {
            OnPropertyChanged(name);
        }

        MarkStale();
    }

    /// <summary>
    /// Say the picture no longer describes the element, without paying for a
    /// solve. The artist presses Simulate when they are ready.
    /// </summary>
    private void MarkStale()
    {
        if (_building) return;
        Stale = true;
        Status = "Simulate to see this.";
    }

    /// <summary>
    /// What a changed row costs: a re-simulation the artist has to ask for, or a
    /// redraw they get straight away.
    /// </summary>
    private void OnFieldChanged(EffectFieldRow row)
    {
        if (_building) return;
        Selected?.Relabel();
        if (row.Resolves) MarkStale();
        else RefreshPreview();
    }

    private void BuildPlacementFields()
    {
        PlacementFields.Clear();
        if (Element is null) return;

        Number(PlacementFields, "First frame", () => E.FirstFrame, v => E.FirstFrame = (int)v,
            0, 480, 1, resolves: true);
        Number(PlacementFields, "Frames", () => E.FrameCount, v => E.FrameCount = Math.Max(1, (int)v),
            1, 480, 1, resolves: true);
        Number(PlacementFields, "Expose on", () => E.ExposeOn, v => E.ExposeOn = Math.Max(1, (int)v),
            1, 6, 1, resolves: true,
            hint: "Bake one drawing every N frames and hold it. Two is animating on 2s, which halves the boil per-frame tracing produces.");
        Number(PlacementFields, "Pre-roll", () => E.PreRoll ?? 0, v => E.PreRoll = (int)v <= 0 ? null : (int)v,
            0, 60, 1, resolves: true,
            hint: "Frames simulated before the first drawn one, so the element opens on an established plume rather than on still air.");

        Number(PlacementFields, "Grid width", () => E.GridWidth, v => E.GridWidth = Math.Max(8, (int)v),
            8, 512, 8, resolves: true);
        Number(PlacementFields, "Grid height", () => E.GridHeight, v => E.GridHeight = Math.Max(8, (int)v),
            8, 512, 8, resolves: true);
        Number(PlacementFields, "Cell size", () => E.Scale, v => E.Scale = Math.Max(0.5, v),
            0.5, 40, 0.5, resolves: true,
            hint: "Document pixels per cell. The grid is deliberately coarser than the document — this is the main lever on cost.");
        Number(PlacementFields, "Origin X", () => E.OriginX, v => E.OriginX = v, -4096, 4096, 8, resolves: true);
        Number(PlacementFields, "Origin Y", () => E.OriginY, v => E.OriginY = v, -4096, 4096, 8, resolves: true);
        Number(PlacementFields, "Substeps", () => E.Substeps, v => E.Substeps = Math.Max(1, (int)v),
            1, 32, 1, resolves: true,
            hint: "Solver steps per frame. The way to buy accuracy without breaking the CFL bound — and the other main lever on cost.");
    }

    private void BuildPhysicsFields()
    {
        PhysicsFields.Clear();
        if (Element is null) return;

        Number(PhysicsFields, "Buoyancy", () => E.Params.Buoyancy, v => E.Params = E.Params with { Buoyancy = v },
            0, 2, 0.05, resolves: true, hint: "How hard heat lifts. A flame with none of it stalls.");
        Number(PhysicsFields, "Weight", () => E.Params.Weight, v => E.Params = E.Params with { Weight = v },
            0, 0.5, 0.005, resolves: true, hint: "How hard the fluid's own mass pulls down. Smoke has some; fire has almost none.");
        Number(PhysicsFields, "Vorticity", () => E.Params.Vorticity, v => E.Params = E.Params with { Vorticity = v },
            0, 2, 0.05, resolves: true, hint: "Puts back the curl a coarse grid loses. This is what makes the tongues and the curls.");
        Number(PhysicsFields, "Drag", () => E.Params.Drag, v => E.Params = E.Params with { Drag = v },
            0, 0.5, 0.01, resolves: true,
            hint: "Air resistance. Without it a closed element circulates forever and reads as a lava lamp.");
        Number(PhysicsFields, "Turbulence", () => E.Params.Turbulence, v => E.Params = E.Params with { Turbulence = v },
            0, 2, 0.05, resolves: true, hint: "A standing eddy field, seeded from position so it never flickers.");
        Number(PhysicsFields, "Turbulence scale", () => E.Params.TurbulenceScale,
            v => E.Params = E.Params with { TurbulenceScale = v },
            1, 32, 1, resolves: true,
            hint: "The size of those eddies, in cells. Larger than the element and it stops being turbulence and becomes wind.");
        Number(PhysicsFields, "Turbulence drift", () => E.Params.TurbulenceDrift,
            v => E.Params = E.Params with { TurbulenceDrift = v },
            0, 1, 0.01, resolves: true, hint: "How fast the eddy field itself moves, so a hold does not look frozen.");
        Number(PhysicsFields, "Dissipation", () => E.Params.Dissipation,
            v => E.Params = E.Params with { Dissipation = v },
            0, 0.1, 0.002, resolves: true, hint: "How fast the stuff thins out. Smoke wants a little; steam wants a lot.");
        Number(PhysicsFields, "Cooling", () => E.Params.Cooling, v => E.Params = E.Params with { Cooling = v },
            0, 0.5, 0.01, resolves: true, hint: "How fast heat leaves. This is what gives a flame its length.");

        Number(PhysicsFields, "Wind X", () => E.WindX?.Value ?? 0,
            v => E.WindX = v == 0 && E.WindX?.IsAnimated != true ? null : Param(E.WindX, v),
            -1, 1, 0.01, resolves: true,
            hint: "Ambient flow across the element, in cells per step. A plume rises at about 0.15, so 0.15 bends a flame by half a right angle and 0.5 lays it flat. A character running right is wind blowing left.");
        Number(PhysicsFields, "Wind Y", () => E.WindY?.Value ?? 0,
            v => E.WindY = v == 0 && E.WindY?.IsAnimated != true ? null : Param(E.WindY, v),
            -1, 1, 0.01, resolves: true);
    }

    private void BuildBandFields()
    {
        BandFields.Clear();
        if (Element is null) return;

        Choice(BandFields, "Bands read", () => E.BandsFromHeat ? 1 : 0, v => E.BandsFromHeat = v >= 0.5,
            ["Density", "Temperature"], resolves: true,
            hint: "Fire bands from temperature — the ramp is the drawing. Smoke bands from density.");
        Number(BandFields, "Band low", () => E.BandLow, v => E.BandLow = v, 0, 1, 0.01,
            hint: "The outermost band, as a fraction of the highest value the element reaches. A plume's field is steeply peaked: only 4% of cells hold more than a hundredth of the peak, so this belongs low.");
        Number(BandFields, "Band high", () => E.BandHigh, v => E.BandHigh = v, 0, 1, 0.01,
            hint: "The innermost band, on the same scale. Taken over the whole element rather than per frame — a band that moves because its scale moved is flicker.");
    }

    private void BuildTreatmentFields()
    {
        TreatmentFields.Clear();
        if (Selected is not { } row || row.Element is null) return;

        Treatment("Bands", EffectFieldKind.Number,
            () => Resolved().Bands, t => t.Bands is not null,
            (t, v) => t.Bands = v is null ? null : (int)Math.Round(v.Value), 1, 6, 1);

        Treatment("Outlined", EffectFieldKind.Choice,
            () => (double)Resolved().Outlined, t => t.Outlined is not null,
            (t, v) => t.Outlined = v is null ? null : (OutlinedBands)(int)Math.Round(v.Value),
            0, 2, 1,
            [nameof(OutlinedBands.None), nameof(OutlinedBands.Silhouette), nameof(OutlinedBands.Every)]);

        Treatment("Band spacing", EffectFieldKind.Choice,
            () => (double)Resolved().Spacing, t => t.Spacing is not null,
            (t, v) => t.Spacing = v is null ? null : (BandSpacing)(int)Math.Round(v.Value),
            0, 1, 1, [nameof(BandSpacing.Even), nameof(BandSpacing.CoreBiased)]);

        Treatment("Line weight", EffectFieldKind.Number,
            () => Resolved().BaseWeight, t => t.BaseWeight is not null,
            (t, v) => t.BaseWeight = v, 0.02, 1, 0.05);

        Treatment("Offset", EffectFieldKind.Number,
            () => Resolved().Offset, t => t.Offset is not null, (t, v) => t.Offset = v, -4, 4, 0.25);

        Treatment("Simplify", EffectFieldKind.Number,
            () => Resolved().Simplify, t => t.Simplify is not null, (t, v) => t.Simplify = v, 0, 2, 0.05);

        Treatment("Smoothing", EffectFieldKind.Number,
            () => Resolved().Smoothing, t => t.Smoothing is not null, (t, v) => t.Smoothing = v, 0, 1, 0.1);

        Treatment("Break length", EffectFieldKind.Number,
            () => Resolved().BreakLength, t => t.BreakLength is not null, (t, v) => t.BreakLength = v, 0, 12, 0.5);

        Treatment("Break gap", EffectFieldKind.Number,
            () => Resolved().BreakGap, t => t.BreakGap is not null, (t, v) => t.BreakGap = v, 0, 12, 0.5);

        Treatment("Light angle", EffectFieldKind.Number,
            () => Resolved().LightAngleDeg, t => t.LightAngleDeg is not null,
            (t, v) => t.LightAngleDeg = v, -180, 180, 15);

        ResolvedTreatment Resolved() =>
            row.Element is { } e ? _vm.Doc.TreatmentFor(e) : LineTreatment.Defaults;

        void Treatment(
            string name, EffectFieldKind kind,
            Func<double> effective, Func<LineTreatment, bool> stated, Action<LineTreatment, double?> write,
            double min, double max, double step, IReadOnlyList<string>? choices = null)
        {
            TreatmentFields.Add(new EffectFieldRow(
                name, kind, effective,
                () => row.Element is { Treatment: { } t } && stated(t),
                v =>
                {
                    if (row.Element is not { } e) return;
                    if (v is null && e.Treatment is null) return;
                    write(e.Treatment ??= new LineTreatment(), v);

                    // An override set that states nothing must not linger: it
                    // would write "treatment": {} into every document that had
                    // ever touched a slider and put it back.
                    if (!e.Treatment.StatedFields().Any()) e.Treatment = null;
                },
                OnFieldChanged, min, max, step, choices));
        }
    }

    private void BuildEmitterFields()
    {
        EmitterFields.Clear();
        if (SelectedEmitter?.Emitter is null) return;

        Choice(EmitterFields, "Shape", () => (double)Em.Shape, v => Em.Shape = (EmitterShape)(int)Math.Round(v),
            [.. Enum.GetNames<EmitterShape>()], resolves: true);
        Number(EmitterFields, "X", () => Em.X, v => Em.X = v, -512, 512, 1, resolves: true);
        Number(EmitterFields, "Y", () => Em.Y, v => Em.Y = v, -512, 512, 1, resolves: true);
        Number(EmitterFields, "X2", () => Em.X2, v => Em.X2 = v, -512, 512, 1, resolves: true,
            hint: "The far end, for a line emitter. Ignored by the others.");
        Number(EmitterFields, "Y2", () => Em.Y2, v => Em.Y2 = v, -512, 512, 1, resolves: true);
        Number(EmitterFields, "Radius", () => Em.Radius, v => Em.Radius = Math.Max(0.5, v),
            0.5, 64, 0.5, resolves: true);
        Number(EmitterFields, "Density", () => Em.Density, v => Em.Density = v, 0, 4, 0.05, resolves: true,
            hint: "How much stuff it puts in per step.");
        Number(EmitterFields, "Heat", () => Em.Heat, v => Em.Heat = v, 0, 4, 0.05, resolves: true,
            hint: "How hot. Buoyancy reads this, so it is what makes a flame rise rather than sit.");
        Number(EmitterFields, "Velocity X", () => Em.VelocityX, v => Em.VelocityX = v, -2, 2, 0.05, resolves: true);
        Number(EmitterFields, "Velocity Y", () => Em.VelocityY, v => Em.VelocityY = v, -2, 2, 0.05, resolves: true);

        Number(EmitterFields, "Travel X", () => Em.MotionX?.Value ?? 0,
            v => Em.MotionX = v == 0 && Em.MotionX?.IsAnimated != true ? null : Param(Em.MotionX, v),
            -512, 512, 1, resolves: true,
            hint: "Where the emitter's origin goes, in cells. Key this to carry an effect along with a running figure — the emitter moves, the fluid it has already laid down does not.");
        Number(EmitterFields, "Travel Y", () => Em.MotionY?.Value ?? 0,
            v => Em.MotionY = v == 0 && Em.MotionY?.IsAnimated != true ? null : Param(Em.MotionY, v),
            -512, 512, 1, resolves: true);
    }

    // ---- the small print ----------------------------------------------------------

    /// <summary>The selected element, for the builders above. Never reached with none selected.</summary>
    private SimElement E => Element!;

    /// <summary>The selected emitter, likewise.</summary>
    private Emitter Em => SelectedEmitter!.Emitter!;

    /// <summary>
    /// Write a value into a keyable parameter without losing the keys it already
    /// has — the window edits the constant, and the timeline edits the curve.
    /// </summary>
    private static EffectParam Param(EffectParam? existing, double value)
    {
        if (existing is null) return new EffectParam { Value = value };
        existing.Value = value;
        return existing;
    }

    private void Number(
        ObservableCollection<EffectFieldRow> into, string name,
        Func<double> read, Action<double> write,
        double min, double max, double step, bool resolves = false, string? hint = null) =>
        into.Add(new EffectFieldRow(
            name, EffectFieldKind.Number, read, null,
            v => { if (v is { } value) write(Math.Clamp(value, min, max)); },
            OnFieldChanged, min, max, step, null, resolves, hint));

    private void Choice(
        ObservableCollection<EffectFieldRow> into, string name,
        Func<double> read, Action<double> write,
        IReadOnlyList<string> choices, bool resolves = false, string? hint = null) =>
        into.Add(new EffectFieldRow(
            name, EffectFieldKind.Choice, read, null,
            v => { if (v is { } value) write(Math.Clamp(value, 0, choices.Count - 1)); },
            OnFieldChanged, 0, choices.Count - 1, 1, choices, resolves, hint));

    /// <summary>Put every treatment field back to what the shared treatment says.</summary>
    public void RevertTreatment()
    {
        if (Element is not { } element) return;
        element.Treatment = null;
        foreach (var field in TreatmentFields) field.Refresh();
        RefreshPreview();
    }
}
