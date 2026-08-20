using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Raster;
using Lightbox.Raster.Media;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

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
/// second or more. So a style edit previews live, and a physics edit marks the
/// preview <see cref="Stale"/> and waits for the artist to ask — which makes
/// the cost legible instead of making every edit feel like the slow one.
/// <see cref="SolveFingerprint"/> is the mechanical half of the same line.
/// </para>
/// </remarks>
public sealed partial class FluidEffectsViewModel : ObservableObject
{
    private readonly MainViewModel _vm;
    private readonly SimBaker _baker = new();
    private readonly Dictionary<string, (string Fingerprint, SolvedElement Solved)> _cache = [];
    private bool _building;

    /// <param name="libraryPath">
    /// Where the effects library lives. Null is the artist's own file; a test
    /// passes a temporary one so a run never reads or writes the real library.
    /// </param>
    public FluidEffectsViewModel(MainViewModel vm, string? libraryPath = null)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        LibraryPath = libraryPath;
        Reload();
    }

    public ObservableCollection<SimElementRow> Elements { get; } = [];

    [ObservableProperty]
    private SimElementRow? _selected;

    /// <summary>The frame the preview is parked on.</summary>
    [ObservableProperty]
    private int _previewFrame;

    /// <summary>How far through a solve, 0..1.</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// Whether the picture on screen was drawn from a solve that no longer
    /// describes the element.
    /// </summary>
    /// <remarks>
    /// Set rather than computed on demand so the button that clears it can be
    /// the thing an artist presses when they are ready to pay for a solve —
    /// which is the whole reason a physics edit does not preview live.
    /// </remarks>
    [ObservableProperty]
    private bool _stale;

    /// <summary>What the last bake or preview cost, in the artist's words.</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>Whether the last draw had to re-simulate, or only re-drew.</summary>
    public bool LastBakeResolved { get; private set; }

    public SimElement? Element => Selected?.Element;

    /// <summary>The strokes the preview last produced, so a view can paint them.</summary>
    public IReadOnlyList<Stroke> PreviewStrokes { get; private set; } = [];

    /// <summary>
    /// Which frame <see cref="PreviewStrokes"/> describes, so a repaint can tell
    /// "nothing to draw here" from "not drawn yet".
    /// </summary>
    /// <remarks>
    /// <b>An empty list is an answer, not a miss.</b> Without this the repaint
    /// re-traced every time the preview sat on a frame the element does not
    /// cover — wasted work, and it silently overwrote
    /// <see cref="LastBakeResolved"/> with the result of that second call, so
    /// the flag reported the repaint rather than the operation the artist asked
    /// for.
    /// </remarks>
    private int _previewedFrame = int.MinValue;

    /// <summary>Raised when the preview's content changed and a view should repaint.</summary>
    public event Action? PreviewChanged;

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
            foreach (var element in sims.Values
                         .OrderBy(e => e.FirstFrame).ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                Elements.Add(new SimElementRow(element.Id, Find, GroupNameOf));
            }
        }

        Selected = Elements.FirstOrDefault(r => r.Id == wasSelected) ?? Elements.FirstOrDefault();
        ReloadGroups();
        if (!_libraryLoaded) { _libraryLoaded = true; ReloadPresets(); }
        BuildFields();
    }

    private SimElement? Find(string id) =>
        _vm.Doc.Sims is { } sims && sims.TryGetValue(id, out var element) ? element : null;

    /// <summary>Add an element that draws nothing until it is given an emitter.</summary>
    public SimElementRow NewElement(string kind = "fire")
    {
        var fire = kind == "fire";
        var element = new SimElement
        {
            Kind = kind,
            FirstFrame = 0,
            // An effect is an animation, so a new one is a second of it —
            // never the document's own length, which on a fresh document is
            // **one frame**. That produced a one-frame element whose preview
            // showed the same drawing whatever the scrubber said, and it was
            // mistaken for the plume reaching a steady state twice before a
            // direct measurement of the solver caught it. Baking grows the
            // timeline to hold the element (`SimBakeOps.Apply` → `Grow`), so
            // asking for 24 frames on a 1-frame document is not a conflict.
            FrameCount = Math.Max(24, _vm.Doc.Scene.FrameCount),
            // 48x44 cells at 4 document pixels each — a torch-sized element,
            // measured rather than guessed. `SimElement`'s own 192x108 at scale
            // 10 describes a *canvas*, and a plume in it is a speck: at the
            // tuned parameters a flame reaches about 28 cells above its emitter
            // and stops, because Cooling is what gives it its length. So the
            // grid is sized to the flame, and the flame fills about two thirds
            // of it. Anything wanting a bonfire raises the grid, which is one
            // slider and now visible.
            GridWidth = 48,
            GridHeight = 44,
            Scale = 4,
            // Opens on an established plume rather than on still air, which is
            // the commonest complaint about the first half-second of an effect.
            PreRoll = 16,
            BandsFromHeat = fire,
            BandColors = fire
                ? ["#3a1200", "#d95f18", "#ffe9a8"]
                : ["#2a2a30", "#5a5a66", "#9a9aa8"],
            // A flame is the light source, so nothing lights it and its bands
            // stay concentric — the ramp from dull red to white *is* the
            // drawing. Smoke is lit from outside, and without that it is an
            // onion: three rings round one centre, which is a cross-section
            // rather than a volume. So smoke arrives shaded and fire does not.
            Treatment = fire ? null : new LineTreatment { ShadeOffset = 2 },
            // A flame that cannot shed its tip is the thing a person watching a
            // render notices straight away, and until `Burning` existed there was
            // nothing to shed with: heat was stamped at the emitter and only
            // decayed, so a piece that detached went out inside one frame. Fire
            // therefore arrives burning.
            //
            // Nothing else moves with it, and that was measured rather than
            // assumed. Burning makes a fire hotter and a hotter fire climbs, so
            // the expectation was that `Vorticity` would have to come up to spend
            // the extra rise on curl — which is true on a tall grid and is not
            // true on this one, because 44 cells does not give the flame the room
            // to use it. On the grid a new element actually gets:
            //
            //   today                  50% of the grid's height, sheds lasting 1 frame
            //   burning, vorticity .35  47%,                     sheds lasting 4
            //   burning, vorticity .7   43%,                     sheds lasting 7
            //
            // So the default changes one thing. An artist who wants the pieces to
            // last longer raises `Vorticity` and pays for it in height, which the
            // manual says. Smoke does not burn and writes no keys about it.
            Params = fire ? new SimParams { Burning = new Combustion() } : new SimParams(),
        };

        // An element with no emitter simulates still air and draws nothing, which
        // reads as the window being broken rather than as an element waiting to be
        // told where the fire is. So a new one arrives already burning, centred on
        // the floor of its own grid.
        element.Emitters.Add(new Emitter
        {
            Shape = EmitterShape.Disc,
            X = element.GridWidth / 2.0,
            Y = element.GridHeight - 4,
            Radius = 4,
            Density = 1,
            // Smoke rises because it is hot, and a smoke emitter with no heat
            // at all does not rise — `Weight` pulls it down and it spreads on
            // the floor of its own grid as a pancake. Measured by rendering
            // it: 0.4 gives a billow that climbs and still keeps its mass,
            // where fire's 1 would send it up like a flame.
            Heat = fire ? 1 : 0.4,
        });

        (_vm.Doc.Sims ??= [])[element.Id] = element;
        var row = new SimElementRow(element.Id, Find, GroupNameOf);
        Elements.Add(row);
        Selected = row;
        _vm.NoteEffectEdited();
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
        OnPropertyChanged(nameof(PenSummary));
        RefreshPreview();
        _vm.NoteEffectEdited();
    }

    /// <summary>Put the element back on the default pen.</summary>
    public void ClearBrush()
    {
        if (Element is not { } element) return;
        element.OutlineBrush = null;
        OnPropertyChanged(nameof(PenSummary));
        RefreshPreview();
        _vm.NoteEffectEdited();
    }

    /// <summary>What pen this element draws with, for the button beside it to say.</summary>
    public string PenSummary => Element?.OutlineBrush is { } brush
        ? $"{brush.Size:F0} px, hardness {brush.Hardness:F2}"
        : "the default pen";

    // ---- solving, drawing and baking ---------------------------------------------

    /// <summary>
    /// Simulate the element, whatever the cache holds. The button an artist
    /// presses when the preview has gone <see cref="Stale"/>.
    /// </summary>
    public void Resolve(IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (Element is not { } element) return;
        _cache.Remove(element.Id);
        RefreshPreview(progress, cancel);
    }

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
        Stale = false;
        Status = $"Baked {baked.Count} drawing{(baked.Count == 1 ? string.Empty : "s")}, " +
                 $"{baked.Sum(b => b.Strokes.Count)} strokes.";
    }

    /// <summary>Drop this element's drawings without deleting the element.</summary>
    public void ClearBake()
    {
        if (Element is not { } element) return;
        _vm.ClearSimBake(element);
        Status = "Cleared.";
    }

    /// <summary>
    /// Redraw the preview from whatever the cache holds, solving only if it holds
    /// nothing usable.
    /// </summary>
    public void RefreshPreview(IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (_building) return;
        if (Element is not { } element)
        {
            PreviewStrokes = [];
            _previewedFrame = int.MinValue;
            PreviewChanged?.Invoke();
            return;
        }

        PreviewStrokes = Preview(PreviewFrame, progress, cancel);
        _previewedFrame = PreviewFrame;
        Stale = false;
        Status = LastBakeResolved
            ? $"Simulated {element.FrameCount} frames."
            : $"{PreviewStrokes.Count} strokes on frame {PreviewFrame}.";
        PreviewChanged?.Invoke();
    }

    /// <summary>
    /// The strokes this element would draw on a frame, without touching the
    /// document — what the preview shows while a treatment is being tuned.
    /// </summary>
    public IReadOnlyList<Stroke> Preview(
        int frame, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (Element is not { } element) return [];

        // One frame, not all of them. Re-tracing a 48-frame element is 44 ms,
        // which is fine once per bake and is three frames of lag per tick of a
        // slider drag. `DrawAt` also answers "the nearest drawing at or before",
        // so scrubbing across a hold shows what is exposed there rather than
        // nothing.
        var solved = SolveIfNeeded(element, progress, cancel);
        var treatment = _vm.Doc.TreatmentFor(element);
        return _baker.DrawAt(solved, element, treatment, element.OutlineBrush, frame)?.Strokes ?? [];
    }

    /// <summary>The preview as a picture, fitted to a box.</summary>
    public SKBitmap? RenderPreview(int frame, int width, int height)
    {
        if (Element is not { } element) return null;

        var strokes = frame == _previewedFrame ? PreviewStrokes : Preview(frame);
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
                      $"|{e.Density:R}|{e.Heat:R}|{e.VelocityX:R}|{e.VelocityY:R}|{e.Burst}" +
                      $"|{e.EmitFrom}|{e.EmitUntil}" +
                      $"|{e.MaskLayerId}|{Keys(e.MotionX)}|{Keys(e.MotionY)}" +
                      $"|{Scatter(e.Scatter)}");
        }

        return string.Join(";", parts);

        static string Scatter(EmitterScatter? s) =>
            s is null ? "-"
                : $"{s.Coverage:R}/{s.Spacing:R}/{s.SizeVariation:R}/{s.HeatVariation:R}/{s.Drift:R}";

        static string Keys(EffectParam? param) =>
            param is null ? "-"
                : param.Value.ToString("R") + "@" +
                  string.Join(",", param.Keys?.Select(k => $"{k.Frame}:{k.Value:R}:{k.Ease}") ?? []);
    }

    /// <summary>
    /// Scrubbing redraws — unless the picture is already out of date, in which
    /// case it would <em>solve</em>, and a solve on a slider drag is seconds per
    /// tick. The artist has already been told to press Simulate.
    /// </summary>
    partial void OnPreviewFrameChanged(int value)
    {
        if (Stale) return;
        RefreshPreview();
    }
}
