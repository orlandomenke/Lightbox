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

/// <summary>The fill tool and its tolerance, gap and grow settings.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q78, which was 12,749 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- fill tool -------------------------------------------------------------

    [ObservableProperty]
    private double _fillTolerance = 32;

    /// <summary>Openings up to this many pixels still count as closed ("connected").</summary>
    [ObservableProperty]
    private double _fillGapPx = 4;

    /// <summary>Overfill (+) or underfill (−) the region by pixels.</summary>
    [ObservableProperty]
    private double _fillGrowPx = 2;

    /// <summary>Sample every visible layer (fill what LOOKS empty) instead of only the active one.</summary>
    [ObservableProperty]
    private bool _smartFill = true;

    /// <summary>Insert the fill under the line work (tucks beneath the line); off = fill on top, preserving lines.</summary>
    [ObservableProperty]
    private bool _fillBelowLines = true;

    /// <summary>
    /// Where a "below line work" fill belongs in the stroke list.
    ///
    /// Index 0 was the obvious answer and the wrong one: it put the fill under
    /// EVERYTHING, so a second fill disappeared beneath the first, and a fill
    /// made after erasing was wiped by the eraser it had been slipped behind.
    /// Both read to the artist as "the fill did nothing".
    ///
    /// The rule that holds instead: go under the line work, but no further
    /// back than the last stroke that would swallow you. Only a brush stroke
    /// is line work to tuck beneath; a fill, a gradient or an eraser already
    /// on the layer is content this fill must sit on top of — an eraser
    /// especially, because it removed what was there when it ran, and putting
    /// later content underneath makes it delete something that never existed.
    /// </summary>
    internal static int UnderLineWorkIndex(IReadOnlyList<Stroke> strokes)
    {
        for (var i = strokes.Count - 1; i >= 0; i--)
        {
            if (strokes[i].Tool != ToolKind.Brush) return i + 1;
        }
        return 0;
    }

    /// <summary>Fill tool click: flood at a document position, record a fill stroke.</summary>
    /// <summary>
    /// A colour was dragged from the swatch onto the canvas. Fills there,
    /// choosing the sensible method rather than making the artist pick one:
    /// inside a selection the selection is the region, otherwise it is a
    /// flood fill from the dropped point. The colour becomes the current
    /// colour too — dragging it out is a statement of intent.
    /// </summary>
    public void DropColorAt(string hex, double x, double y)
    {
        if (IsPlaying) return;
        if (!CanEdit(ActiveLayer, "fill on it")) return;

        ColorHex = hex;

        // Inside a selection, the selection is obviously the region the
        // artist means — filling only the contiguous patch under the drop
        // point would be a strange reading of the gesture. Outside one, fall
        // back to a flood fill from where it landed.
        if (HasSelection && SelectionContainsPoint(x, y))
        {
            FillWholeSelection();
            return;
        }
        FillAtInternal(x, y);
    }

    private bool SelectionContainsPoint(double x, double y)
    {
        if (_selectionContours.Count == 0) return false;
        using var path = BrushEngine.PathFromContours(_selectionContours);
        return path.Contains((float)x, (float)y);
    }

    /// <summary>Fill every pixel of the current selection, as one undo step.</summary>
    private void FillWholeSelection() =>
        StrokeOverWholeSelection(ToolKind.Fill, ColorHex, ActiveSwatchId, "fill-selection");

    /// <summary>
    /// Lay one region-shaped stroke over the whole selection, as one undo step.
    /// </summary>
    /// <remarks>
    /// <b>B173.</b> The fill and the clear are the same operation with a
    /// different <see cref="ToolKind"/>, so they share a body rather than the
    /// clear being a copy that drifts. Both go through the record — invariant 1
    /// and invariant 3 — which is what makes undo free and what stops "delete"
    /// meaning something the reload cannot reproduce.
    /// </remarks>
    /// <param name="swatchId">
    /// Null for the eraser and for the background fill: a swatch reference
    /// exists so recolouring a palette entry moves the art with it, and neither
    /// of these is art in that sense — the eraser has no colour at all, and
    /// Backspace means "the background as it is now" rather than "follow this
    /// swatch forever".
    /// </param>
    private void StrokeOverWholeSelection(
        ToolKind tool, string color, string? swatchId, string label)
    {
        if (_selectionContours.Count == 0) return;
        if (PaintTargetOrKey() is not { } target) return;
        var scene = Scene;

        var stroke = new Stroke
        {
            Tool = tool,
            Color = color,
            SwatchId = swatchId,
            PaletteId = swatchId is null ? null : ActivePaletteId,
            Brush = new BrushSettings { Opacity = 1, AntiAlias = AntiAliasing },
            Points = [.. _selectionContours[0]],
            Holes = _selectionContours.Count > 1
                ? _selectionContours.Skip(1).Select(c => c.ToList()).ToList()
                : null,
            Label = label,
        };
        if (PrepareClipForSelection() is { } clip) stroke.ClipId = clip.Id;

        AppendToFrameRender(target, stroke);
        _committingScopedEdit = true;
        try
        {
            _editor.Perform(_ => StrokesOf(target).Add(stroke));
        }
        finally
        {
            _committingScopedEdit = false;
        }
        _dirtyThumbIds.Add(target.Id);
        _publish.InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = tool == ToolKind.ClearRegion
            ? "Cleared the selection."
            : $"Filled the selection with {color}.";
    }

    /// <summary>
    /// Delete: clear what is inside the selection, leaving the outline up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B173, and the precedence is the decided part.</b> A live marquee wins
    /// and the selected lines are the fallback — Photoshop's rule, and the one
    /// an artist arrives with. It is worth writing down that this goes
    /// <em>against</em> the precedent next door: <see cref="NudgeSelection"/>
    /// asks the line selection first and says why. So the two keys disagree
    /// about which selection they mean, knowingly.
    /// </para>
    /// <para>
    /// The cost of that disagreement is real and is why <b>B171 had to land
    /// first</b>: Delete silently changes meaning while a stale marquee is up,
    /// and before B171 a marquee could be left up by a document the artist had
    /// already closed. With selections scoped to their document, a marquee
    /// being up is something the artist did to *this* drawing.
    /// </para>
    /// <para>
    /// The outline stays after the clear, because the next thing an artist does
    /// with an emptied region is usually put something else in it.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void DeleteSelectionContents()
    {
        if (IsPlaying) return;
        if (HasSelection)
        {
            if (!CanEdit(ActiveLayer, "erase on it")) return;
            StrokeOverWholeSelection(ToolKind.ClearRegion, ColorHex, null, "clear-selection");
            return;
        }
        DeleteSelectedLinesCommand.Execute(null);
    }

    /// <summary>
    /// Backspace: flood the selection with the background colour.
    /// </summary>
    /// <remarks>
    /// <b>B173.</b> The counterpart to <see cref="DeleteSelectionContents"/>,
    /// and deliberately a *fill* rather than an erase — the two keys differ in
    /// what is left behind, which is the whole reason both exist. No fallback
    /// when nothing is selected: Backspace has never meant anything on the
    /// canvas, so there is no established behaviour to preserve, and inventing
    /// one here would be a second decision smuggled in beside the asked-for one.
    /// </remarks>
    [RelayCommand]
    private void FillSelectionWithBackground()
    {
        if (IsPlaying) return;
        if (!HasSelection) return;
        if (!CanEdit(ActiveLayer, "fill on it")) return;
        StrokeOverWholeSelection(
            ToolKind.Fill, BackgroundColorHex, null, "fill-selection-background");
    }

    /// <param name="invertSmart">
    /// Shift was held. Fill the other way from whatever the option currently
    /// says — a one-click override, not a setting change, so the option is
    /// still where it was for the next fill.
    /// </param>
    public void FillAt(double x, double y, bool invertSmart = false)
    {
        if (ActiveTool != ToolId.Fill) return;
        FillAtInternal(x, y, invertSmart);
    }

    /// <summary>
    /// The fill itself, without the tool check — a colour dropped on the
    /// canvas fills whatever tool happens to be selected.
    /// </summary>
    private void FillAtInternal(double x, double y, bool invertSmart = false)
    {
        if (IsPlaying) return;
        if (!CanEdit(ActiveLayer, "fill on it")) return;
        if (PaintTargetOrKey() is not { } target) return;

        var scene = Scene;
        SKBitmap? owned = null;
        try
        {
            // Held Shift flips it for this click only. A line-art layer over a
            // painted background wants smart fill nine times out of ten and
            // the active layer alone on the tenth; going to the options bar
            // and back for that one is the interruption worth removing.
            var smart = SmartFill ^ invertSmart;
            SKBitmap sample;
            if (smart)
            {
                owned = CompositeVisibleLayers();
                sample = owned;
            }
            else
            {
                sample = _cache.Get(target, scene.Width, scene.Height);
            }

            var (seedX, seedY) = ToSurface(x, y);
            var result = FloodFill.Fill(
                sample,
                seedX,
                seedY,
                new FloodFill.Options(FillTolerance, FillGapPx, FillGrowPx),
                SelectionMask(scene.Width, scene.Height));
            if (result is null)
            {
                AiStatus = "Nothing fillable at that spot.";
                return;
            }

            var stroke = new Stroke
            {
                Tool = ToolKind.Fill,
                Color = ColorHex,
                SwatchId = ActiveSwatchId,
                PaletteId = ActivePaletteId,
                Brush = new BrushSettings { Opacity = 1, AntiAlias = AntiAliasing },
                // Out of surface space: a fill is a stroke, and a stroke's
                // points are the record's coordinates (invariant 1).
                Points = ToDocument([result.Outer])[0],
                Holes = result.Holes.Count > 0 ? ToDocument(result.Holes) : null,
                Label = "fill",
            };
            var clip = PrepareClipForSelection();
            if (clip is not null) stroke.ClipId = clip.Value.Id;
            var below = FillBelowLines;

            // Fill-above stamps incrementally onto the cached frame; fill-below
            // changes stroke order, so only that path pays a frame re-render.
            if (below)
            {
                InvalidateFrameRender(target.Id);
            }
            else
            {
                AppendToFrameRender(target, stroke);
            }

            var frameId = target.Id;
            var addedClip = false;
            _committingScopedEdit = true;
            try
            {
                _editor.PerformDelta(
                    apply: doc =>
                    {
                        if (clip is { } c) addedClip = doc.ClipRegions.TryAdd(c.Id, c.Region);
                        var list = StrokeListIn(doc, frameId);
                        if (list is null) return;
                        if (below) list.Insert(UnderLineWorkIndex(list), stroke);
                        else list.Add(stroke);
                    },
                    revert: doc =>
                    {
                        RemoveStrokeById(doc, frameId, stroke.Id);
                        if (clip is { } c && addedClip) doc.ClipRegions.Remove(c.Id);
                    },
                    affectedFrameId: frameId);
            }
            finally
            {
                _committingScopedEdit = false;
            }
            _dirtyThumbIds.Add(target.Id);
            PublishSnapshot();
            RefreshThumbnails();
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>
    /// Every visible layer composited over transparency at the playhead —
    /// "what the eye sees minus the paper". Caller owns the returned bitmap.
    /// </summary>
    private SKBitmap CompositeVisibleLayers()
    {
        var scene = Scene;
        var passes = new List<RenderPass>();
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            var frame = ExposureSheet.ExposedFrame(layer, CurrentFrameIndex);
            if (frame is null) continue;
            passes.Add(new RenderPass(
                _cache.Get(frame, scene.Width, scene.Height, celIndex: CurrentFrameIndex), null, layer.Opacity,
                SceneRenderer.ToSkia(layer.BlendMode)));
        }
        using var image = SceneRenderer.Compose(scene.Width, scene.Height, passes, SkiaSharp.SKColors.Transparent);
        return SKBitmap.FromImage(image);
    }
}
