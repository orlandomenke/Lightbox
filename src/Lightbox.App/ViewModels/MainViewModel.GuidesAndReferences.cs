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
    // ---- imported references ------------------------------------------------------

    // ---- guides -----------------------------------------------------------------

    /// <summary>The guides on this document, or an empty list.</summary>
    public IReadOnlyList<Guide> Guides => Scene.Guides ?? [];

    public bool HasGuides => Scene.HasGuides;

    /// <summary>
    /// Whether guides constrain what you draw right now.
    /// </summary>
    /// <remarks>
    /// A working state rather than a document property, the same side of the
    /// line as onion skin: the guides are authored and saved, but whether you
    /// are currently drawing against them is how you are working this minute.
    /// It survives the session through settings, not through the file.
    /// </remarks>
    [ObservableProperty]
    private bool _snapToGuides = true;


    partial void OnSnapToleranceChanged(double value)
    {
        if (Math.Abs(Settings.SnapTolerance - value) < 1e-9) return;
        Settings.SnapTolerance = value;
        Settings.Save();
    }

    /// <summary>
    /// The pitch a new grid is made with, in document pixels.
    /// </summary>
    /// <remarks>
    /// A preference, not document data: once a grid exists, its spacing lives
    /// on the guide, so changing this never moves a lattice somebody has
    /// already drawn against.
    /// </remarks>
    public double GridSpacing
    {
        get => Settings.GridSpacing;
        set
        {
            var clamped = Math.Clamp(value, 1, 4096);
            if (Math.Abs(Settings.GridSpacing - clamped) < 1e-9) return;
            Settings.GridSpacing = clamped;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>The grid guides on this document, if any.</summary>
    public IReadOnlyList<Guide> GridGuides =>
        Guides.Where(g => g.Kind == GuideKind.Grid).ToList();

    /// <summary>
    /// Change a placed grid's pitch, as one undoable step.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GridSpacing"/> on purpose. That is what the
    /// next grid will be; this reaches into one that exists, and only an
    /// explicit edit should ever do that.
    /// </remarks>
    public void SetGridSpacing(Guide guide, double spacing)
    {
        var clamped = Math.Clamp(spacing, 1, 4096);
        var before = guide.Spacing;
        if (Math.Abs(before - clamped) < 1e-9) return;
        _editor.PerformDelta(_ => guide.Spacing = clamped, _ => guide.Spacing = before);
        NotifyGuides();
    }

    /// <summary>Change a placed grid's angle, as one undoable step.</summary>
    public void SetGridAngle(Guide guide, double angle)
    {
        var before = guide.Angle;
        if (Math.Abs(before - angle) < 1e-9) return;
        _editor.PerformDelta(_ => guide.Angle = angle, _ => guide.Angle = before);
        NotifyGuides();
    }

    /// <summary>Turn a placed guide's drawing or snapping on or off, undoably.</summary>
    public void SetGuideFlags(Guide guide, bool visible, bool snaps)
    {
        var before = (guide.Visible, guide.Snaps);
        if (before == (visible, snaps)) return;
        _editor.PerformDelta(
            _ => { guide.Visible = visible; guide.Snaps = snaps; },
            _ => { guide.Visible = before.Visible; guide.Snaps = before.Snaps; });
        NotifyGuides();
    }




    /// <summary>
    /// Put a raw point where the guides say it belongs.
    /// </summary>
    /// <remarks>
    /// After stabilisation, not before. Snapping first and smoothing after
    /// would drag the point back off the guide, which is the wrong way round —
    /// the wobble is what you want removed, the guide is what you want obeyed.
    /// </remarks>
    private (double X, double Y) Guided(double x, double y)
    {
        if (!SnapToGuides || Scene.Guides is not { Count: > 0 } guides) return (x, y);

        // Locked already: hold the line, and stop reconsidering. A wobbly hand
        // that re-chooses mid-stroke makes the line kink.
        if (_lockedGuide is { } locked)
        {
            return Snapper.Along(locked, _strokeAnchor.X, _strokeAnchor.Y, x, y);
        }
        if (!_lockDecided)
        {
            if (Snapper.Lock(guides, _strokeAnchor.X, _strokeAnchor.Y, x, y) is { } found)
            {
                _lockedGuide = found;
                _lockDecided = true;
                return Snapper.Along(found, _strokeAnchor.X, _strokeAnchor.Y, x, y);
            }
            // Far enough to have meant something, and it matched nothing:
            // this is a freehand stroke and asking again every event would
            // only let a late wobble grab it.
            var dx = x - _strokeAnchor.X;
            var dy = y - _strokeAnchor.Y;
            if (Math.Sqrt(dx * dx + dy * dy) >= Snapper.LockDistance) _lockDecided = true;
        }
        return Snapper.Point(guides, x, y, SnapTolerance);
    }

    /// <summary>Add a guide. The first one brings the machinery into being.</summary>
    public Guide AddGuide(GuideKind kind, double x, double y, double angle = 0, double spacing = 32)
    {
        var guide = new Guide { Kind = kind, X = x, Y = y, Angle = angle, Spacing = spacing };
        _editor.Perform(doc => (doc.Scene.Guides ??= []).Add(guide));
        NotifyGuides();
        return guide;
    }

    public void RemoveGuide(Guide guide)
    {
        var id = guide.Id;
        _editor.Perform(doc =>
        {
            doc.Scene.Guides?.RemoveAll(g => g.Id == id);
            // Absent, not empty: a document whose last guide goes writes no
            // guide key again.
            if (doc.Scene.Guides is { Count: 0 }) doc.Scene.Guides = null;
        });
        NotifyGuides();
    }

    /// <summary>Move a guide's anchor, in document pixels.</summary>
    public void MoveGuide(Guide guide, double dx, double dy)
    {
        if (guide.Locked) return;
        _editor.PerformDelta(
            _ => { guide.X += dx; guide.Y += dy; },
            _ => { guide.X -= dx; guide.Y -= dy; });
        NotifyGuides();
    }

    private (double X, double Y) _guideDragTotal;

    /// <summary>
    /// Move a guide while the pointer is still down.
    /// </summary>
    /// <remarks>
    /// Nothing is recorded until the drag ends. A pointer move arrives every
    /// few milliseconds, so recording each one would bury the last real edit
    /// under fifty identical nudges and make undoing a drag a job rather than
    /// a keystroke.
    /// </remarks>
    public void DragGuide(Guide guide, double dx, double dy)
    {
        if (guide.Locked) return;
        guide.X += dx;
        guide.Y += dy;
        _guideDragTotal = (_guideDragTotal.X + dx, _guideDragTotal.Y + dy);
        NotifyGuides();
    }

    /// <summary>Close a guide drag: the whole of it becomes one undo step.</summary>
    public void EndGuideDrag(Guide guide)
    {
        var (dx, dy) = _guideDragTotal;
        _guideDragTotal = default;
        if (dx == 0 && dy == 0) return;
        // Back to where the drag started, then forward again through the
        // recorded path — so undo returns it to the place it was picked up
        // from rather than to the last pointer event.
        guide.X -= dx;
        guide.Y -= dy;
        MoveGuide(guide, dx, dy);
    }

    [RelayCommand]
    private void ClearGuides()
    {
        if (!HasGuides) return;
        _editor.Perform(doc => doc.Scene.Guides = null);
        NotifyGuides();
    }

    private void NotifyGuides()
    {
        OnPropertyChanged(nameof(Guides));
        OnPropertyChanged(nameof(HasGuides));
        OnPropertyChanged(nameof(GridGuides));
        GuidesChanged?.Invoke();
        PublishSnapshot();
        MarkDocumentEdited();
    }

    /// <summary>The guides changed; the canvas redraws its chrome from this.</summary>
    public event Action? GuidesChanged;

    /// <summary>The references on this document, or an empty list.</summary>
    public IReadOnlyList<ReferenceStrip> References =>
        Scene.References is { } strips ? strips : [];

    public bool HasReferences => Scene.HasReferences;

    /// <summary>
    /// The reference being edited. Index rather than the object, so the
    /// selection survives an undo — which replaces the whole document.
    /// </summary>
    [ObservableProperty]
    private int _activeReferenceIndex;

    public ReferenceStrip? ActiveReference =>
        Scene.References is { } strips && ActiveReferenceIndex >= 0 && ActiveReferenceIndex < strips.Count
            ? strips[ActiveReferenceIndex]
            : null;

    partial void OnActiveReferenceIndexChanged(int value) => NotifyReference();

    /// <summary>The cell of the active reference showing at the playhead, or null.</summary>
    public ReferenceCell? ActiveReferenceCell => ActiveReference?.CellAt(CurrentFrameIndex);

    public bool HasReferenceCell => ActiveReferenceCell is not null;

    /// <summary>
    /// Import a sheet, slice it, and lay it against the timeline from the
    /// playhead.
    /// </summary>
    /// <param name="addFrames">
    /// Extend the timeline to fit the reference. On by default because it is
    /// what importing a run cycle means: you are here to draw those frames,
    /// and being handed a twelve-frame reference on a one-frame document with
    /// eleven of it invisible is not a state anybody asked for.
    /// </param>
    /// <summary>
    /// Import an image file as a reference. Everything becomes PNG on the way
    /// in: the document carries the image itself rather than a path — a
    /// reference that broke when the file moved would break silently, and you
    /// would not notice until you were drawing against nothing. False when
    /// the file cannot be read as an image.
    /// </summary>
    public bool ImportReferenceImageFile(string path)
    {
        string png;
        try
        {
            using var decoded = SKBitmap.Decode(path);
            if (decoded is null) return false;
            png = Lightbox.Raster.PngCodec.Encode(decoded);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return ImportReference(System.IO.Path.GetFileNameWithoutExtension(path), png) is not null;
    }

    public ReferenceStrip? ImportReference(
        string name, string pngBase64, SliceOptions options = default, bool addFrames = true)
    {
        SKBitmap sheet;
        try
        {
            sheet = Lightbox.Raster.PngCodec.Decode(pngBase64);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            return null;
        }

        var strip = new ReferenceStrip
        {
            Name = name,
            Png = pngBase64,
            SheetWidth = sheet.Width,
            SheetHeight = sheet.Height,
            Cells = SliceSheet(sheet, options),
        };
        sheet.Dispose();
        if (strip.Cells.Count == 0) return null;

        strip.LayOutFrom(CurrentFrameIndex);
        strip.Scale = FitScale(strip, Scene);
        strip.CentreOn(Scene.Width, Scene.Height);

        var index = 0;
        _editor.Perform(doc =>
        {
            doc.Scene.References ??= [];
            index = doc.Scene.References.Count;
            doc.Scene.References.Add(strip);
            if (addFrames && strip.Slots.Count > doc.Scene.FrameCount)
            {
                doc.Scene.FrameCount = strip.Slots.Count;
            }
        });

        Lightbox.Raster.ReferenceStripRegistry.Register([(strip.Id, strip.Png)]);
        ActiveReferenceIndex = index;
        AfterReferenceChange();
        return strip;
    }

    /// <summary>
    /// Footage to draw against (Q56): the clip's frames extracted at the
    /// scene's fps and laid against the timeline like any reference. The
    /// document keeps the path — relative when the file lives near it — and
    /// the pixels are rebuilt from the footage on load. Returns null on
    /// success or a sentence saying why not.
    /// </summary>
    public async Task<string?> ImportVideoReference(
        string path, Services.ClipStorage storage = Services.ClipStorage.ReferenceByPath)
    {
        if (Services.VideoExporter.FindFfmpeg() is not { } ffmpeg)
        {
            return "FFmpeg was not found — reinstall Lightbox, or install FFmpeg and put it on PATH.";
        }
        // The extraction is an FFmpeg run — seconds, off the UI thread. The
        // document edit below stays on it, like every other edit.
        var fps = Math.Max(1, Scene.Fps);
        var (extracted, error) = await Task.Run(() =>
        {
            var r = Services.VideoReferenceImporter.Extract(ffmpeg, path, fps, out var e);
            return (r, e);
        });
        if (extracted is not { } result) return error ?? "The clip could not be read.";

        var stored = path;
        if (System.IO.Path.GetDirectoryName(SaveTargetTab?.FilePath) is { Length: > 0 } docDir)
        {
            var relative = System.IO.Path.GetRelativePath(docDir, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal)
                && !System.IO.Path.IsPathRooted(relative))
            {
                stored = relative;
            }
        }

        var strip = new ReferenceStrip
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            SheetWidth = result.Sheet.Width,
            SheetHeight = result.Sheet.Height,
            Cells = result.Cells,
        };
        switch (storage)
        {
            case Services.ClipStorage.ReferenceByPath:
                strip.VideoPath = stored;
                break;
            case Services.ClipStorage.ReferenceEmbedded:
                // The contact sheet itself, stored the way image references
                // store — self-contained, no path, no FFmpeg on reopen.
                strip.Png = Lightbox.Raster.PngCodec.Encode(result.Sheet);
                break;
            case Services.ClipStorage.Production:
                strip.VideoData = Convert.ToBase64String(File.ReadAllBytes(path));
                strip.RendersInExport = true;
                // Material, not a ghost: production footage shows and
                // exports at full strength unless the artist dials it back.
                strip.Opacity = 1.0;
                break;
        }
        strip.LayOutFrom(CurrentFrameIndex);
        strip.Scale = FitScale(strip, Scene);
        strip.CentreOn(Scene.Width, Scene.Height);

        var index = 0;
        _editor.Perform(doc =>
        {
            doc.Scene.References ??= [];
            index = doc.Scene.References.Count;
            doc.Scene.References.Add(strip);
            if (strip.Slots.Count > doc.Scene.FrameCount)
            {
                doc.Scene.FrameCount = strip.Slots.Count;
            }
        });

        Lightbox.Raster.ReferenceStripRegistry.Register(strip.Id, result.Sheet);
        ActiveReferenceIndex = index;
        AfterReferenceChange();
        return null;
    }

    /// <summary>
    /// Rebuild a loaded video reference's pixels from its footage. Quiet when
    /// FFmpeg or the file is gone — drawing against nothing is the reference
    /// system's standing answer to a source it cannot read.
    /// </summary>
    private void RegisterVideoReference(ReferenceStrip strip)
    {
        if (Lightbox.Raster.ReferenceStripRegistry.Resolve(strip.Id) is not null) return;
        if (Services.VideoExporter.FindFfmpeg() is not { } ffmpeg) return;

        string resolved;
        if (strip.VideoData is { } data)
        {
            // Production footage (Q57): the clip travels in the document, so
            // the extraction reads a temp copy of its own bytes.
            resolved = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"lightbox-footage-{strip.Id}.bin");
            try
            {
                if (!File.Exists(resolved))
                {
                    File.WriteAllBytes(resolved, Convert.FromBase64String(data));
                }
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                return;
            }
        }
        else
        {
            resolved = strip.VideoPath!;
            if (!System.IO.Path.IsPathRooted(resolved))
            {
                if (System.IO.Path.GetDirectoryName(SaveTargetTab?.FilePath) is not { Length: > 0 } docDir) return;
                resolved = System.IO.Path.Combine(docDir, resolved);
            }
        }
        if (!File.Exists(resolved)) return;

        var extracted = Services.VideoReferenceImporter.Extract(ffmpeg, resolved, Math.Max(1, Scene.Fps), out _);
        if (extracted is { } result)
        {
            Lightbox.Raster.ReferenceStripRegistry.Register(strip.Id, result.Sheet);
        }
    }

    private static List<ReferenceCell> SliceSheet(SKBitmap sheet, SliceOptions options)
    {
        // Grid mode never reads a pixel, so a sheet the artist has described
        // does not pay for the scan.
        if (options.Columns > 0 && options.Rows > 0)
        {
            return StripSlicer.Grid(sheet.Width, sheet.Height, options.Columns, options.Rows);
        }

        using var rgba = sheet.ColorType == SKColorType.Rgba8888
            ? null
            : sheet.Copy(SKColorType.Rgba8888);
        var source = rgba ?? sheet;
        using var pixmap = source.PeekPixels();
        var occupied = pixmap is null
            ? new bool[source.Width * source.Height]
            : StripSlicer.Occupancy(pixmap.GetPixelSpan(), source.Width, source.Height, options);
        // Detect finds the drawings and discards the furniture — a title
        // banner, a watermark, a signature. Slice projects occupancy onto the
        // axes, which is exact for a clean atlas and hopeless for a page: the
        // banner is content in every column, so the projection never returns
        // to zero and the whole sheet reads as one cell. Fall back to Slice
        // only when nothing looked like a drawing.
        var found = StripSlicer.Detect(occupied, source.Width, source.Height, options);
        return found.Count > 0
            ? found
            : StripSlicer.Slice(occupied, source.Width, source.Height, options);
    }

    /// <summary>
    /// Shrink an oversized sheet to fit the canvas. A reference bigger than
    /// the document is the common case — a 2000px sprite sheet against a 960px
    /// scene — and landing at 1:1 puts the character off screen with no
    /// obvious way back.
    /// </summary>
    private static double FitScale(ReferenceStrip strip, Scene scene)
    {
        var cell = strip.Cells[0];
        if (cell.Width <= 0 || cell.Height <= 0) return 1;
        var fit = Math.Min(scene.Width / (double)cell.Width, scene.Height / (double)cell.Height);
        return fit >= 1 ? 1 : Math.Round(fit, 3);
    }

    /// <summary>
    /// Columns and rows for the grid override. Zero on both means "work it
    /// out from the pixels", which is what <c>Detect</c> restores.
    /// </summary>
    [ObservableProperty]
    private int _referenceColumns;

    [ObservableProperty]
    private int _referenceRows;

    /// <summary>Dragging on the canvas lines the reference up instead of drawing.</summary>
    [ObservableProperty]
    private bool _referenceAlignMode;

    [RelayCommand]
    private void ApplyReferenceGrid() =>
        ResliceReference(new SliceOptions(Math.Max(0, ReferenceColumns), Math.Max(0, ReferenceRows)));

    [RelayCommand]
    private void DetectReferenceFrames()
    {
        ReferenceColumns = 0;
        ReferenceRows = 0;
        ResliceReference(default);
    }

    /// <summary>Cut the active sheet up again — a different grid, or auto-detect.</summary>
    public void ResliceReference(SliceOptions options)
    {
        if (ActiveReference is not { } strip) return;
        if (Lightbox.Raster.ReferenceStripRegistry.Resolve(strip.Id) is not { } sheet) return;

        var first = strip.Slots.FindIndex(s => s >= 0);
        _editor.Perform(doc =>
        {
            var live = doc.Scene.References![ActiveReferenceIndex];
            live.Cells = SliceSheet(sheet, options);
            live.LayOutFrom(Math.Max(0, first));
        });
        AfterReferenceChange();
    }

    [RelayCommand]
    private void RemoveReference()
    {
        if (ActiveReference is not { } strip) return;
        var id = strip.Id;
        _editor.Perform(doc =>
        {
            doc.Scene.References?.RemoveAt(ActiveReferenceIndex);
            // Absent, not empty: a document whose last reference is removed
            // goes back to writing no key at all.
            if (doc.Scene.References is { Count: 0 }) doc.Scene.References = null;
        });
        Lightbox.Raster.ReferenceStripRegistry.Forget(id);
        ActiveReferenceIndex = Math.Max(0, ActiveReferenceIndex - 1);
        AfterReferenceChange();
    }

    // ---- editing the grid by hand ---------------------------------------------

    /// <summary>
    /// The grid gizmos are showing and everything else is off.
    /// </summary>
    /// <remarks>
    /// A mode rather than a tool. While it is on, every box on the sheet is
    /// editable at once and the canvas is not a place to draw — the same
    /// bargain <see cref="ReferenceAlignMode"/> makes, for the same reason: a
    /// half-drawn mark made while adjusting a grid is one you then have to
    /// find and undo. Escape leaves it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuppressesPainting))]
    private bool _referenceGridEditMode;

    partial void OnReferenceGridEditModeChanged(bool value)
    {
        if (!value) SelectedReferenceCell = -1;
        PublishSnapshot();
    }

    /// <summary>Whether some mode has taken the canvas away from the tools.</summary>
    public bool SuppressesPainting => ReferenceGridEditMode;

    /// <summary>Which box the gizmos have selected, or -1.</summary>
    [ObservableProperty]
    private int _selectedReferenceCell = -1;

    /// <summary>
    /// Where a cell lands on the canvas, in document pixels.
    /// </summary>
    /// <remarks>
    /// The same arithmetic the compositor does in <see cref="ScenePassBuilder.ReferencePasses"/>,
    /// exposed so the gizmos can be drawn and hit-tested against exactly what
    /// is on screen. Two copies of this would drift and the boxes would stop
    /// sitting on the drawings they describe.
    /// </remarks>
    public (double X, double Y, double W, double H) CellRect(ReferenceStrip strip, ReferenceCell cell)
    {
        var scale = Math.Max(0.01, strip.Scale);
        return (
            strip.OffsetX + cell.Dx + cell.X * scale,
            strip.OffsetY + cell.Dy + cell.Y * scale,
            cell.Width * scale,
            cell.Height * scale);
    }

    /// <summary>A document point in the active sheet's own pixels.</summary>
    public (double X, double Y) DocToSheet(ReferenceStrip strip, ReferenceCell cell, double x, double y)
    {
        var scale = Math.Max(0.01, strip.Scale);
        return ((x - strip.OffsetX - cell.Dx) / scale, (y - strip.OffsetY - cell.Dy) / scale);
    }

    /// <summary>The box under a document point, or -1.</summary>
    public int ReferenceCellAt(double x, double y)
    {
        if (ActiveReference is not { } strip) return -1;
        // Backwards, so the box drawn last — the one on top — wins.
        for (var i = strip.Cells.Count - 1; i >= 0; i--)
        {
            var (cx, cy, w, h) = CellRect(strip, strip.Cells[i]);
            if (x >= cx && x <= cx + w && y >= cy && y <= cy + h) return i;
        }
        return -1;
    }

    /// <summary>Move one box, in document pixels.</summary>
    public void MoveReferenceCell(int index, double dx, double dy)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var cell = strip.Cells[index];
        _editor.PerformDelta(
            _ => { cell.Dx += dx; cell.Dy += dy; },
            _ => { cell.Dx -= dx; cell.Dy -= dy; });
        AfterReferenceChange();
    }

    /// <summary>
    /// Resize one box by dragging a corner, in document pixels.
    /// </summary>
    /// <remarks>
    /// The window onto the sheet changes, not the nudge: growing a box shows
    /// more of the drawing rather than scaling it. A box that scaled its
    /// contents would be a second zoom control with no way to tell it from the
    /// first.
    /// </remarks>
    public void ResizeReferenceCell(int index, bool left, bool top, double dx, double dy)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var scale = Math.Max(0.01, strip.Scale);
        var cell = strip.Cells[index];
        var before = cell.Clone();

        var sx = (int)Math.Round(dx / scale);
        var sy = (int)Math.Round(dy / scale);
        var x = left ? cell.X + sx : cell.X;
        var y = top ? cell.Y + sy : cell.Y;
        var w = left ? cell.Width - sx : cell.Width + sx;
        var h = top ? cell.Height - sy : cell.Height + sy;
        // A box with no area is a box you cannot get hold of again.
        if (w < 4 || h < 4) return;

        _editor.PerformDelta(
            _ => { cell.X = x; cell.Y = y; cell.Width = w; cell.Height = h; },
            _ =>
            {
                cell.X = before.X;
                cell.Y = before.Y;
                cell.Width = before.Width;
                cell.Height = before.Height;
            });
        AfterReferenceChange();
    }

    /// <summary>
    /// Put a box's pivot at a document point.
    /// </summary>
    /// <remarks>
    /// Recorded in sheet pixels, so it stays on the same part of the drawing
    /// when the sheet is nudged or rescaled afterwards.
    /// </remarks>
    public void SetReferencePivot(int index, double docX, double docY)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var cell = strip.Cells[index];
        var (x, y) = DocToSheet(strip, cell, docX, docY);
        var (beforeX, beforeY) = (cell.PivotX, cell.PivotY);
        _editor.PerformDelta(
            _ => { cell.PivotX = x; cell.PivotY = y; },
            _ => { cell.PivotX = beforeX; cell.PivotY = beforeY; });
        AfterReferenceChange();
    }

    /// <summary>Remove one box. The sheet is untouched; only the window goes.</summary>
    public void DeleteReferenceCell(int index)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var first = strip.Slots.FindIndex(s => s >= 0);
        _editor.Perform(doc =>
        {
            var live = doc.Scene.References![ActiveReferenceIndex];
            live.Cells.RemoveAt(index);
            live.LayOutFrom(Math.Max(0, first));
        });
        SelectedReferenceCell = -1;
        AfterReferenceChange();
    }

    /// <summary>
    /// Draw a box by hand, from a rectangle in document pixels.
    /// </summary>
    /// <remarks>
    /// The escape hatch from detection. A sheet whose figures overlap, or
    /// whose rows have no gutter, cannot be found from the pixels — no amount
    /// of looking finds a boundary that is not there — so the answer is to let
    /// the artist draw it rather than to guess.
    /// </remarks>
    public void AddReferenceCell(double x, double y, double w, double h)
    {
        if (ActiveReference is not { } strip) return;
        if (w < 4 || h < 4) return;
        var scale = Math.Max(0.01, strip.Scale);
        var sheetX = (int)Math.Round((x - strip.OffsetX) / scale);
        var sheetY = (int)Math.Round((y - strip.OffsetY) / scale);
        var cell = new ReferenceCell
        {
            X = sheetX,
            Y = sheetY,
            Width = (int)Math.Round(w / scale),
            Height = (int)Math.Round(h / scale),
        };
        var first = strip.Slots.FindIndex(s => s >= 0);
        _editor.Perform(doc =>
        {
            var live = doc.Scene.References![ActiveReferenceIndex];
            live.Cells.Add(cell);
            live.LayOutFrom(Math.Max(0, first));
        });
        SelectedReferenceCell = strip.Cells.Count - 1;
        AfterReferenceChange();
    }

    /// <summary>Whether the timeline is short of frames for the boxes found.</summary>
    public bool ReferenceNeedsKeyframes =>
        ActiveReference is { Cells.Count: > 0 } strip && strip.Cells.Count > Scene.FrameCount;

    /// <summary>
    /// One keyframe per box, lined up on the pivots.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things at once, because they are one intention: the timeline grows
    /// to hold the reference, and the cells are registered so the pivot sits
    /// still. Being handed an eight-frame reference on a one-frame document is
    /// not a state anybody asked for, and neither is a run cycle that has to
    /// be nudged into place eight times.
    /// </para>
    /// <para>
    /// Alignment is separable — <see cref="ReferenceStrip.AlignByPivot"/> is
    /// its own call — because someone matching a walk wants the travel left in
    /// and someone matching a drawing does not.
    /// </para>
    /// </remarks>
    [RelayCommand]
    public void GenerateReferenceKeyframes()
    {
        if (ActiveReference is not { Cells.Count: > 0 } strip) return;
        var wanted = strip.Cells.Count;
        var first = Math.Max(0, strip.Slots.FindIndex(s => s >= 0));

        _editor.Perform(doc =>
        {
            var scene = doc.Scene;
            while (scene.FrameCount < first + wanted) DocumentEditor.AppendFrame(scene);
            var live = scene.References![ActiveReferenceIndex];
            live.LayOutFrom(first);
            live.AlignByPivot();
        });
        AfterReferenceChange();
        AiStatus = $"{wanted} frames from “{strip.Name}”, aligned on their pivots.";
    }

    /// <summary>Move the cell showing at the playhead, in document pixels.</summary>
    public void NudgeReferenceCell(double dx, double dy)
    {
        if (ActiveReferenceCell is not { } cell) return;
        _editor.PerformDelta(
            _ => { cell.Dx += dx; cell.Dy += dy; },
            _ => { cell.Dx -= dx; cell.Dy -= dy; });
        AfterReferenceChange();
    }

    /// <summary>Move the whole sheet, every frame together.</summary>
    public void NudgeReference(double dx, double dy)
    {
        if (ActiveReference is not { } strip) return;
        _editor.PerformDelta(
            _ => { strip.OffsetX += dx; strip.OffsetY += dy; },
            _ => { strip.OffsetX -= dx; strip.OffsetY -= dy; });
        AfterReferenceChange();
    }

    /// <summary>Undo every per-frame nudge on the active sheet.</summary>
    [RelayCommand]
    private void ClearReferenceAlignment()
    {
        if (ActiveReference is not { } strip) return;
        var before = strip.Cells.ConvertAll(c => (c.Dx, c.Dy));
        _editor.PerformDelta(
            _ => { foreach (var c in strip.Cells) (c.Dx, c.Dy) = (0, 0); },
            _ =>
            {
                for (var i = 0; i < strip.Cells.Count && i < before.Count; i++)
                {
                    (strip.Cells[i].Dx, strip.Cells[i].Dy) = before[i];
                }
            });
        AfterReferenceChange();
    }

    public double ReferenceScale
    {
        get => ActiveReference?.Scale ?? 1;
        set => SetReference(Math.Clamp(value, 0.05, 8), (s, v) => s.Scale = v, ActiveReference?.Scale ?? 1);
    }

    public double ReferenceOpacity
    {
        get => ActiveReference?.Opacity ?? 0.5;
        set => SetReference(Math.Clamp(value, 0, 1), (s, v) => s.Opacity = v, ActiveReference?.Opacity ?? 0.5);
    }

    public bool ReferenceVisible
    {
        get => ActiveReference?.Visible ?? false;
        set => SetReference(value, (s, v) => s.Visible = v, ActiveReference?.Visible ?? false);
    }

    public bool ReferenceFollowsTimeline
    {
        get => ActiveReference?.FollowsTimeline ?? true;
        set => SetReference(value, (s, v) => s.FollowsTimeline = v, ActiveReference?.FollowsTimeline ?? true);
    }

    public double ReferenceCellDx
    {
        get => ActiveReferenceCell?.Dx ?? 0;
        set => NudgeReferenceCell(value - (ActiveReferenceCell?.Dx ?? 0), 0);
    }

    public double ReferenceCellDy
    {
        get => ActiveReferenceCell?.Dy ?? 0;
        set => NudgeReferenceCell(0, value - (ActiveReferenceCell?.Dy ?? 0));
    }

    /// <summary>Which frame of the sheet the playhead is on, for the panel's label.</summary>
    public string ReferenceCellLabel
    {
        get
        {
            if (ActiveReference is not { } strip) return "";
            var slot = CurrentFrameIndex < strip.Slots.Count ? strip.Slots[CurrentFrameIndex] : -1;
            return slot < 0 ? "no reference on this frame" : $"reference frame {slot + 1} of {strip.Cells.Count}";
        }
    }

    private void SetReference<T>(T value, Action<ReferenceStrip, T> apply, T current,
        [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (ActiveReference is not { } strip || EqualityComparer<T>.Default.Equals(value, current)) return;
        // A view setting, not an edit to the artwork: no undo entry, the same
        // treatment layer visibility gets.
        apply(strip, value);
        OnPropertyChanged(property);
        AfterReferenceChange();
    }

    /// <summary>
    /// A reference or one of its cells changed. The window redraws the grid
    /// gizmos from this — they are a snapshot, so nothing else would tell it.
    /// </summary>
    public event Action? ReferenceChanged;

    private void AfterReferenceChange()
    {
        NotifyReference();
        OnPropertyChanged(nameof(TimelineVideoClips));
        PublishSnapshot();
        MarkDocumentEdited();
        ReferenceChanged?.Invoke();
    }
}
