using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.App.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q78, which was 13,628 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- live palettes ------------------------------------------------------

    /// <summary>The palette docker's state.</summary>
    public PaletteDockerViewModel PaletteDocker { get; }

    /// <summary>
    /// The swatch the next stroke should reference, or null to record a
    /// literal colour. Set by selecting a swatch; cleared by choosing a colour
    /// any other way (see <see cref="OnColorHexChanged"/>).
    /// </summary>
    public string? ActiveSwatchId { get; private set; }

    /// <summary>Which palette <see cref="ActiveSwatchId"/> came from — Q30 step 2.</summary>
    public string? ActivePaletteId { get; private set; }


    /// <summary>The swatch and the colour it held when the current edit run began.</summary>
    private (string Id, string Before)? _pendingSwatchEdit;

    private void PaintWithSwatch(string swatchId)
    {
        if (PaletteRegistry.ResolveSwatch(swatchId) is not { } swatch) return;
        _settingColorFromSwatch = true;
        try
        {
            ColorHex = swatch.Color;
        }
        finally
        {
            _settingColorFromSwatch = false;
        }
        ActiveSwatchId = swatchId;
        // Q30 step 2: which palette, not just which swatch. Two palettes
        // duplicated from one another share swatch ids, so a bare id stops
        // being an answer the moment palettes are scoped.
        ActivePaletteId = PaletteRegistry.PaletteOf(swatchId);
    }

    /// <summary>A structural palette edit — one undo step, then a full resync.</summary>
    private void PerformPaletteEdit(Action<Doc> mutate)
    {
        CommitSwatchEdit();
        _editor.Perform(mutate);
    }

    private void OnSwatchRecoloured(SwatchRow row, string before)
    {
        // A run is one swatch at a time; touching a different one closes the
        // previous run off so each lands as its own undo step.
        if (_pendingSwatchEdit is { } pending && pending.Id != row.Id) CommitSwatchEdit();
        _pendingSwatchEdit ??= (row.Id, before);

        if (row.Id == ActiveSwatchId) PaintWithSwatch(row.Id);
        RepaintForSwatch(row.Id);
        MarkDocumentEdited();
    }

    /// <summary>
    /// Close off a run of colour edits as a single undo step. Dragging the
    /// colour wheel fires an edit per pointer event; one step each would bury
    /// the drawing history under sixty entries of the same swatch.
    /// </summary>
    internal void CommitSwatchEdit()
    {
        // Owed before the early returns below, because the sweep the run skipped
        // is owed whether or not the colour ended up different from where it
        // started. A drag out and back to the same colour still invalidated
        // every thumbnail on the way past.
        var owedSweep = _swatchRepaint is { Repaints: > 1 };
        _swatchRepaint = null;
        if (owedSweep) RefreshThumbnails();

        if (_pendingSwatchEdit is not { } pending) return;
        _pendingSwatchEdit = null;
        if (PaletteRegistry.ResolveSwatch(pending.Id)?.Color is not { } after) return;
        if (after == pending.Before) return;

        var (id, before) = (pending.Id, pending.Before);
        // Looked up by id inside the closure rather than captured: a snapshot
        // undo replaces Doc wholesale, so the Swatch object this ran against
        // will not be the one a later redo has to write to.
        _editor.PerformDelta(
            d => SetSwatchColor(d, id, after), d => SetSwatchColor(d, id, before),
            label: "Edit swatch colour");
    }

    /// <summary>
    /// Set a swatch wherever it actually lives — this document, or the project.
    /// </summary>
    /// <remarks>
    /// <b>B103.</b> This walked <c>doc.Palettes</c> alone, which does not contain
    /// a <em>project</em> palette's swatches. Editing looked correct because the
    /// drag mutates the <c>Swatch</c> instance in place and the registry holds
    /// that instance; only undo revealed that the recorded step and the object
    /// had parted company, and undoing a project recolour appeared to do nothing.
    /// </remarks>
    private void SetSwatchColor(Doc doc, string swatchId, string color)
    {
        var found = false;
        foreach (var palette in doc.Palettes)
        {
            foreach (var swatch in palette.Swatches)
            {
                if (swatch.Id != swatchId) continue;
                swatch.Color = color;
                found = true;
            }
        }
        if (found || ProjectDocker.Project is not { } project) return;
        foreach (var palette in project.Palettes)
        {
            foreach (var swatch in palette.Swatches)
            {
                if (swatch.Id == swatchId) swatch.Color = color;
            }
        }
    }


    private void RepaintForSwatch(string swatchId)
    {
        var scope = _swatchRepaint is { } cached && cached.SwatchId == swatchId
            ? cached
            : (SwatchId: swatchId, Frames: FramesUsing(swatchId), Repaints: 0);

        scope.Repaints++;
        _swatchRepaint = scope;

        foreach (var frame in scope.Frames)
        {
            InvalidateFrameRender(frame.Id);
            _dirtyThumbIds.Add(frame.Id);
        }
        _publish.InvalidateWholeCanvas();
        _composeRing.InvalidateAll();

        // The first repaint of a run lands immediately, so a one-shot recolour —
        // a swatch typed into, a palette applied — is as synchronous as it ever
        // was. Everything after it is a pointer event in a drag, and a drag wants
        // a repaint per *displayed frame* rather than per event: publishing sixty
        // times to show sixty times is the shape invariant 6 forbids, and the
        // coalescing post is what the shape tool's own live drag already uses.
        if (scope.Repaints == 1)
        {
            PublishSnapshot();
            RefreshThumbnails();
        }
        else
        {
            // No thumbnail sweep mid-drag: nobody reads a thumbnail while the
            // wheel is moving, and the sweep walks every cell of every row. The
            // ids are still marked dirty, so the one CommitSwatchEdit runs at the
            // end repaints exactly what changed.
            RequestSnapshot();
        }
    }

    /// <summary>
    /// Every frame holding a stroke painted with this swatch, across every open
    /// document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B102. Every open document, not just the active one.</b> A shared
    /// palette is precisely what a <em>set</em> of documents paint from — that is
    /// the whole feature — so two of a character's animations being open at once
    /// is the ordinary case rather than the edge, and repainting only the focused
    /// tab leaves the other showing the old colour from cache.
    /// </para>
    /// <para>
    /// Still only the frames that hold a stroke referencing the swatch: walking
    /// the stroke record stays far cheaper than re-rendering frames whose pixels
    /// cannot have moved. What B151 changed is <em>how often</em> — this is a
    /// scan of every stroke in every open document, and it used to run on every
    /// pointer event of a wheel drag.
    /// </para>
    /// </remarks>
    /// <summary>
    /// How many times the swatch scan has run, for the budget test.
    /// </summary>
    /// <remarks>
    /// <b>A count rather than a stopwatch, because the property is countable.</b>
    /// B151 is not "this got faster", it is "this happens once per drag instead
    /// of once per pointer event" — and a test that asserts the second is exact,
    /// instant and cannot be flaky, where a timing test on the same behaviour
    /// would be all three of the opposite. The performance test beside it still
    /// measures the end-to-end cost, because a count cannot see a scan that got
    /// slower.
    /// </remarks>
    internal int SwatchScans { get; private set; }

    private List<Frame> FramesUsing(string swatchId)
    {
        SwatchScans++;
        var frames = new List<Frame>();
        var seen = new HashSet<string>();
        foreach (var layer in Tabs.SelectMany(t => t.Doc.Scene.Layers))
        {
            foreach (var cel in layer.Cels)
            {
                // A held cel names the same frame as the one it holds, so without
                // this a ten-frame hold invalidates the same render ten times.
                if (cel.Frame is not { } frame || !seen.Add(frame.Id)) continue;
                if (!StrokesOf(frame).Any(s => s.SwatchId == swatchId)) continue;
                frames.Add(frame);
            }
        }
        return frames;
    }

    // ---- gradients ----------------------------------------------------------

    /// <summary>The gradient docker's state.</summary>
    public GradientDockerViewModel GradientDocker { get; }

    /// <summary>Opacity the gradient tool lays its ramp down at, 0–1.</summary>
    [ObservableProperty]
    private double _gradientOpacity = 1.0;

    private void PerformGradientEdit(Action<Doc> mutate)
    {
        CommitSwatchEdit();
        _editor.Perform(mutate);
    }

    /// <summary>
    /// A gradient definition changed. Same scoping as a swatch edit: only
    /// frames holding a stroke that paints this gradient are dropped.
    /// </summary>
    private void OnGradientEdited(string gradientId)
    {
        // The registry holds the same Gradient object the docker just edited,
        // so nothing needs re-registering — only the cached pixels are stale.
        foreach (var layer in Scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is not { } frame) continue;
                if (!StrokesOf(frame).Any(s => s.GradientId == gradientId)) continue;
                InvalidateFrameRender(frame.Id);
                _dirtyThumbIds.Add(frame.Id);
            }
        }
        // A gradient being dragged right now redefines its own preview.
        if (_liveGradient is not null) RenderGradientPreview();
        _publish.InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        MarkDocumentEdited();
        PublishSnapshot();
        RefreshThumbnails();
    }


    public Doc Doc => _editor.Doc;

    private Scene Scene => _editor.Doc.Scene;

    private Layer ActiveLayer => Scene.Layers[Math.Clamp(ActiveLayerIndex, 0, Scene.Layers.Count - 1)];

    // ---- observable state ---------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameLabel))]
    private int _currentFrameIndex;

    // ---- the active colour pair -------------------------------------------------
    //
    // Foreground and background, the way every painting tool has had them
    // since Photoshop 1: one pair, shared by the brush, the fill and the
    // gradient, swapped with X and reset with D. They are global on purpose —
    // reaching for the same colour in three tools and finding three different
    // answers is the thing this arrangement exists to prevent.

    /// <summary>The colour tools paint with. <c>ColorHex</c> is its old name.</summary>
    [ObservableProperty]
    private string _colorHex = "#000000";

    /// <summary>
    /// The other one. What the eraser reveals conceptually, what a
    /// foreground-to-background gradient ends on, and what X swaps to.
    /// </summary>
    [ObservableProperty]
    private string _backgroundColorHex = "#ffffff";

    /// <summary>Alias, so views can say what they mean.</summary>
    public string ForegroundColorHex
    {
        get => ColorHex;
        set => ColorHex = value;
    }


    /// <summary>
    /// Trade foreground and background (X).
    /// </summary>
    /// <remarks>
    /// The swatch link goes with them. Swapping to a palette colour and back
    /// has to leave the stroke still following that swatch, or X quietly turns
    /// live palette colours into literals — which is a data loss you would not
    /// notice until the recolour did nothing.
    /// </remarks>
    [RelayCommand]
    public void SwapColors()
    {
        var (foreground, background) = (ColorHex, BackgroundColorHex);
        var (foregroundSwatch, backgroundSwatch) = (ActiveSwatchId, _backgroundSwatchId);
        BackgroundColorHex = foreground;
        _backgroundSwatchId = foregroundSwatch;
        if (backgroundSwatch is not null) PaintWithSwatch(backgroundSwatch);
        else
        {
            ActiveSwatchId = null;
            ColorHex = background;
        }
    }


    /// <summary>Back to black over white (D).</summary>
    [RelayCommand]
    public void ResetColors()
    {
        BackgroundColorHex = "#ffffff";
        _backgroundSwatchId = DocumentFactory.WhiteSwatchId;
        if (Doc.Palettes.SelectMany(p => p.Swatches)
                .Any(sw => sw.Id == DocumentFactory.BlackSwatchId))
        {
            PaintWithSwatch(DocumentFactory.BlackSwatchId);
        }
        else
        {
            ActiveSwatchId = null;
            ColorHex = "#000000";
        }
    }
}
