using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Raster;
using Lightbox.Raster.Media;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>One effects element in the list.</summary>
/// <remarks>
/// <b>It holds an id, not the element.</b> Undo does not edit the document in
/// place — it swaps a whole <see cref="Doc"/> back in — so a row that held the
/// object would go on editing a document nobody is looking at, silently, with
/// every slider still moving. Resolving by id each time costs a dictionary
/// lookup and cannot do that.
/// </remarks>
public sealed partial class SimElementRow : ObservableObject
{
    private readonly Func<string, SimElement?> _resolve;

    internal SimElementRow(string id, Func<string, SimElement?> resolve)
    {
        Id = id;
        _resolve = resolve;
    }

    public string Id { get; }

    /// <summary>The element this row stands for, or null once it has been deleted.</summary>
    public SimElement? Element => _resolve(Id);

    public string Label
    {
        get
        {
            if (Element is not { } e) return "(missing)";
            return string.IsNullOrWhiteSpace(e.Name)
                ? $"{e.Kind} · {e.FirstFrame}–{e.FirstFrame + e.FrameCount - 1}"
                : e.Name!;
        }
    }

    /// <summary>Nudge the list when something the label reads has changed.</summary>
    public void Relabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>How a treatment field is edited.</summary>
public enum TreatmentFieldKind
{
    Number,
    Choice,
}

/// <summary>
/// One line-treatment field, and whether this element states it or inherits it.
/// </summary>
/// <remarks>
/// <b>The cascade's one unavoidable cost, paid</b> (Q118, Q123). An overridden
/// field has to be visible as overridden and revertible in one action, or "two
/// places to look" reads to an artist as the application being inconsistent
/// rather than as a cascade. That is the whole reason this is a row with an
/// <see cref="IsOverridden"/> flag rather than sixteen plain properties.
/// </remarks>
public sealed partial class TreatmentFieldRow : ObservableObject
{
    private readonly Func<double> _effective;
    private readonly Func<bool> _stated;
    private readonly Action<double?> _write;
    private readonly Action _changed;
    private bool _quiet;

    internal TreatmentFieldRow(
        string name, TreatmentFieldKind kind,
        Func<double> effective, Func<bool> stated, Action<double?> write, Action changed,
        double min = 0, double max = 1, double step = 0.05,
        IReadOnlyList<string>? choices = null)
    {
        Name = name;
        Kind = kind;
        Minimum = min;
        Maximum = max;
        Step = step;
        Choices = choices;
        _effective = effective;
        _stated = stated;
        _write = write;
        _changed = changed;
        _value = effective();
    }

    public string Name { get; }

    public TreatmentFieldKind Kind { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Step { get; }

    /// <summary>Options for a <see cref="TreatmentFieldKind.Choice"/>; the value is the index.</summary>
    public IReadOnlyList<string>? Choices { get; }

    [ObservableProperty]
    private double _value;

    /// <summary>Whether this element states this field rather than inheriting it.</summary>
    public bool IsOverridden => _stated();

    /// <summary>Put the field back to whatever the shared treatment or the default says.</summary>
    public void Revert()
    {
        _write(null);
        Refresh();
        _changed();
    }

    /// <summary>Re-read from the record — after a revert, or after the element changed.</summary>
    public void Refresh()
    {
        _quiet = true;
        Value = _effective();
        _quiet = false;
        OnPropertyChanged(nameof(IsOverridden));
    }

    partial void OnValueChanged(double value)
    {
        if (_quiet) return;
        _write(value);
        OnPropertyChanged(nameof(IsOverridden));
        _changed();
    }
}

/// <summary>
/// The effects window's brain: what elements the document has, what they are
/// tuned to, and what the preview shows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every decision lives here rather than in the window</b>, so the bake, the
/// cache and the cascade can be tested without a pointer — the same division
/// <see cref="ReferenceBoardViewModel"/> follows. It is also the design's hard
/// constraint: <c>MainViewModel</c> and <c>MainWindow.axaml</c> are the two
/// hottest files in <c>HOTSPOTS.md</c>, and the effects feature was promised it
/// would not touch them beyond a registration line and one mutation seam
/// (<c>MainViewModel.Effects.cs</c>).
/// </para>
/// <para>
/// <b>Solving and drawing are kept apart, and that is the whole point of the
/// window</b> (Q123). Changing a line treatment re-draws from a cached solve in
/// tens of milliseconds; changing a simulation parameter re-solves and costs a
/// second or more. <see cref="SolveFingerprint"/> is what decides which, so the
/// distinction is mechanical rather than a matter of remembering — and
/// <see cref="LastBakeResolved"/> reports which one happened, because a UI that
/// hid the difference would make every edit feel like the slow one.
/// </para>
/// </remarks>
public sealed partial class FluidEffectsViewModel : ObservableObject
{
    private readonly MainViewModel _vm;
    private readonly SimBaker _baker = new();
    private readonly Dictionary<string, (string Fingerprint, SolvedElement Solved)> _cache = [];

    public FluidEffectsViewModel(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        Reload();
    }

    public ObservableCollection<SimElementRow> Elements { get; } = [];

    public ObservableCollection<TreatmentFieldRow> TreatmentFields { get; } = [];

    [ObservableProperty]
    private SimElementRow? _selected;

    /// <summary>The frame the preview is parked on.</summary>
    [ObservableProperty]
    private int _previewFrame;

    /// <summary>How far through a bake, 0..1.</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>Whether the last bake had to re-simulate, or only re-drew.</summary>
    public bool LastBakeResolved { get; private set; }

    public SimElement? Element => Selected?.Element;

    // ---- the document's elements ---------------------------------------------

    /// <summary>
    /// Rebuild the list from the document. Called on open, and after anything
    /// that can swap the document out from under it — undo, redo, a load.
    /// </summary>
    public void Reload()
    {
        var wasSelected = Selected?.Id;

        Elements.Clear();
        if (_vm.Doc.Sims is { } sims)
        {
            foreach (var element in sims.Values.OrderBy(e => e.FirstFrame).ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                Elements.Add(new SimElementRow(element.Id, Find));
            }
        }

        Selected = Elements.FirstOrDefault(r => r.Id == wasSelected) ?? Elements.FirstOrDefault();
        BuildTreatmentFields();
    }

    private SimElement? Find(string id) =>
        _vm.Doc.Sims is { } sims && sims.TryGetValue(id, out var element) ? element : null;

    /// <summary>Add an element that draws nothing until it is given an emitter.</summary>
    public SimElementRow NewElement(string kind = "fire")
    {
        var element = new SimElement
        {
            Kind = kind,
            FirstFrame = 0,
            FrameCount = Math.Max(1, _vm.Doc.Scene.FrameCount),
            BandsFromHeat = kind == "fire",
            BandColors = kind == "fire"
                ? ["#3a1200", "#d95f18", "#ffe9a8"]
                : ["#2a2a30", "#5a5a66", "#9a9aa8"],
        };

        (_vm.Doc.Sims ??= [])[element.Id] = element;
        var row = new SimElementRow(element.Id, Find);
        Elements.Add(row);
        Selected = row;
        return row;
    }

    /// <summary>Remove an element and everything it baked.</summary>
    public void DeleteElement()
    {
        if (Selected is not { } row || row.Element is not { } element) return;

        _vm.ClearSimBake(element);
        _vm.Doc.Sims?.Remove(element.Id);
        _cache.Remove(element.Id);

        Elements.Remove(row);
        Selected = Elements.FirstOrDefault();
    }

    // ---- the pen -----------------------------------------------------------------

    /// <summary>
    /// Give this element the brush the toolbar is holding.
    /// </summary>
    /// <remarks>
    /// A copy, and taken on a button press rather than read at bake time — see
    /// <see cref="SimElement.OutlineBrush"/> for why the alternative draws a
    /// different line every time the same element is baked.
    /// </remarks>
    public void UseCurrentBrush()
    {
        if (Element is not { } element) return;
        element.OutlineBrush = _vm.CurrentBrushCopy();
    }

    /// <summary>Put the element back on the default pen.</summary>
    public void ClearBrush()
    {
        if (Element is not { } element) return;
        element.OutlineBrush = null;
    }

    // ---- baking ----------------------------------------------------------------

    /// <summary>
    /// Bring this element's drawings up to date, re-simulating only if something
    /// the simulation depends on has moved.
    /// </summary>
    public void Bake(IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (Element is not { } element) return;

        var baked = DrawFrames(element, progress, cancel);
        _vm.ApplySimBake(element, baked.Select(b => (b.Frame, b.Strokes)));
        Progress = 1;
    }

    /// <summary>
    /// The strokes this element would draw on a frame, without touching the
    /// document — what the preview shows while a treatment is being tuned.
    /// </summary>
    public IReadOnlyList<Stroke> Preview(int frame)
    {
        if (Element is not { } element) return [];

        // The nearest drawing at or before the frame, so scrubbing across a hold
        // shows the drawing that is actually exposed there rather than nothing.
        BakedFrame? found = null;
        foreach (var candidate in DrawFrames(element, null, default))
        {
            if (candidate.Frame <= frame && (found is null || candidate.Frame > found.Frame)) found = candidate;
        }
        return found?.Strokes ?? [];
    }

    /// <summary>The preview as a picture, at the element's own size.</summary>
    public SKBitmap? RenderPreview(int frame, int width, int height)
    {
        if (Element is not { } element) return null;

        var strokes = Preview(frame);
        if (strokes.Count == 0) return null;

        // Rendered through the element's own placement so the preview frames the
        // element rather than the whole canvas — an element in the corner of a 4K
        // document would otherwise be a speck. Invariant 7: the surface is scaled,
        // the geometry is not.
        var elementWidth = Math.Max(1, (int)Math.Round(element.GridWidth * element.Scale));
        var elementHeight = Math.Max(1, (int)Math.Round(element.GridHeight * element.Scale));
        var scale = Math.Min(width / (double)elementWidth, height / (double)elementHeight);
        if (!double.IsFinite(scale) || scale <= 0) return null;

        return FrameRasterizer.Rasterize(
            strokes, elementWidth, elementHeight, outputScale: scale,
            origin: new SKPointI((int)Math.Round(element.OriginX), (int)Math.Round(element.OriginY)));
    }

    private List<BakedFrame> DrawFrames(
        SimElement element, IProgress<double>? progress, CancellationToken cancel)
    {
        var solved = SolveIfNeeded(element, progress, cancel);
        var treatment = _vm.Doc.TreatmentFor(element);
        return _baker.Draw(solved, element, treatment, element.OutlineBrush);
    }

    private SolvedElement SolveIfNeeded(
        SimElement element, IProgress<double>? progress, CancellationToken cancel)
    {
        var fingerprint = SolveFingerprint(element);
        if (_cache.TryGetValue(element.Id, out var cached) && cached.Fingerprint == fingerprint)
        {
            LastBakeResolved = false;
            return cached.Solved;
        }

        var relay = progress ?? new Progress<double>(p => Progress = p);
        var solved = _baker.Solve(element, new LayerMasks(_vm.Doc, element), relay, cancel);
        _cache[element.Id] = (fingerprint, solved);
        LastBakeResolved = true;
        return solved;
    }

    /// <summary>
    /// Everything the <em>simulation</em> depends on, and nothing the drawing
    /// does.
    /// </summary>
    /// <remarks>
    /// This is the line between a 40 ms restyle and a 1.5 s re-solve, so it is
    /// written out in full rather than derived: band colours, band range, the
    /// outline pen and the whole line treatment are deliberately absent, and
    /// adding a solver parameter without adding it here would leave the cache
    /// serving a stale picture — which looks like the slider doing nothing.
    /// </remarks>
    internal static string SolveFingerprint(SimElement element)
    {
        var p = element.Params;
        var parts = new List<string>
        {
            element.GridWidth.ToString(), element.GridHeight.ToString(),
            element.Scale.ToString("R"), element.OriginX.ToString("R"), element.OriginY.ToString("R"),
            element.FirstFrame.ToString(), element.FrameCount.ToString(),
            element.ExposeOn.ToString(), element.Substeps.ToString(),
            (element.PreRoll ?? 0).ToString(), element.BandsFromHeat.ToString(),
            element.ObstacleLayerId ?? "-",
            p.Buoyancy.ToString("R"), p.Weight.ToString("R"), p.Vorticity.ToString("R"),
            p.Turbulence.ToString("R"), p.TurbulenceScale.ToString("R"),
            p.TurbulenceDrift.ToString("R"), p.Dissipation.ToString("R"),
            p.Cooling.ToString("R"), p.Drag.ToString("R"),
            Keys(element.WindX), Keys(element.WindY),
            element.Particles is { } s ? $"{s.PerFrame}/{s.Lifetime}" : "-",
        };

        foreach (var e in element.Emitters)
        {
            parts.Add($"{e.Shape}|{e.X:R}|{e.Y:R}|{e.X2:R}|{e.Y2:R}|{e.Radius:R}" +
                      $"|{e.Density:R}|{e.Heat:R}|{e.VelocityX:R}|{e.VelocityY:R}" +
                      $"|{e.MaskLayerId}|{Keys(e.MotionX)}|{Keys(e.MotionY)}");
        }

        return string.Join(";", parts);

        static string Keys(EffectParam? param) =>
            param is null ? "-"
                : param.Value.ToString("R") + "@" +
                  string.Join(",", param.Keys?.Select(k => $"{k.Frame}:{k.Value:R}:{k.Ease}") ?? []);
    }

    // ---- the treatment cascade ---------------------------------------------------

    partial void OnSelectedChanged(SimElementRow? value) => BuildTreatmentFields();

    /// <summary>
    /// Rebuild the field rows for the selected element. Each row reads the
    /// resolved value, writes to the element's own overrides, and knows whether
    /// this element is the one stating it.
    /// </summary>
    private void BuildTreatmentFields()
    {
        TreatmentFields.Clear();
        if (Selected is not { } row || row.Element is null) return;

        Add("Bands", TreatmentFieldKind.Number,
            () => Resolved().Bands,
            t => t.Bands is not null,
            (t, v) => t.Bands = v is null ? null : (int)Math.Round(v.Value),
            1, 6, 1);

        Add("Outlined", TreatmentFieldKind.Choice,
            () => (double)Resolved().Outlined,
            t => t.Outlined is not null,
            (t, v) => t.Outlined = v is null ? null : (OutlinedBands)(int)Math.Round(v.Value),
            0, 2, 1, [nameof(OutlinedBands.None), nameof(OutlinedBands.Silhouette), nameof(OutlinedBands.Every)]);

        Add("Band spacing", TreatmentFieldKind.Choice,
            () => (double)Resolved().Spacing,
            t => t.Spacing is not null,
            (t, v) => t.Spacing = v is null ? null : (BandSpacing)(int)Math.Round(v.Value),
            0, 1, 1, [nameof(BandSpacing.Even), nameof(BandSpacing.CoreBiased)]);

        Add("Line weight", TreatmentFieldKind.Number,
            () => Resolved().BaseWeight, t => t.BaseWeight is not null, (t, v) => t.BaseWeight = v, 0.02, 1, 0.05);

        Add("Offset", TreatmentFieldKind.Number,
            () => Resolved().Offset, t => t.Offset is not null, (t, v) => t.Offset = v, -4, 4, 0.25);

        Add("Simplify", TreatmentFieldKind.Number,
            () => Resolved().Simplify, t => t.Simplify is not null, (t, v) => t.Simplify = v, 0, 2, 0.05);

        Add("Smoothing", TreatmentFieldKind.Number,
            () => Resolved().Smoothing, t => t.Smoothing is not null, (t, v) => t.Smoothing = v, 0, 1, 0.1);

        Add("Break length", TreatmentFieldKind.Number,
            () => Resolved().BreakLength, t => t.BreakLength is not null, (t, v) => t.BreakLength = v, 0, 12, 0.5);

        Add("Break gap", TreatmentFieldKind.Number,
            () => Resolved().BreakGap, t => t.BreakGap is not null, (t, v) => t.BreakGap = v, 0, 12, 0.5);

        Add("Light angle", TreatmentFieldKind.Number,
            () => Resolved().LightAngleDeg, t => t.LightAngleDeg is not null,
            (t, v) => t.LightAngleDeg = v, -180, 180, 15);

        ResolvedTreatment Resolved() =>
            row.Element is { } e ? _vm.Doc.TreatmentFor(e) : LineTreatment.Defaults;

        void Add(
            string name, TreatmentFieldKind kind,
            Func<double> effective, Func<LineTreatment, bool> stated, Action<LineTreatment, double?> write,
            double min, double max, double step, IReadOnlyList<string>? choices = null)
        {
            TreatmentFields.Add(new TreatmentFieldRow(
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
                () => { },
                min, max, step, choices));
        }
    }

    /// <summary>Put every field back to what the shared treatment says.</summary>
    public void RevertTreatment()
    {
        if (Element is not { } element) return;
        element.Treatment = null;
        foreach (var field in TreatmentFields) field.Refresh();
    }
}
