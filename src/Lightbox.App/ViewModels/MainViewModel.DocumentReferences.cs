using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Another document as reference (Q284–Q286): the run cycle, flattened, laid
/// under the jump frame for frame — and kept current as the run is worked.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Reference the document at <paramref name="path"/>: its frames flattened
    /// and laid against the timeline from the playhead, one per frame. The
    /// source is read and never written. The strip, or null with a reason.
    /// </summary>
    /// <remarks>
    /// The flatten runs off the UI thread, the way an export's render does: a
    /// 31-frame 1080p run is seconds of compositing, and the window must not
    /// stop for it. The edit to this document happens back on the UI thread,
    /// like every other edit.
    /// </remarks>
    public async Task<(ReferenceStrip? Strip, string? Error)> ImportDocumentReferenceAsync(string path)
    {
        var full = Path.GetFullPath(path);
        if (SaveTargetTab?.FilePath is { Length: > 0 } own
            && string.Equals(Path.GetFullPath(own), full, StringComparison.OrdinalIgnoreCase))
        {
            return (null, "A document cannot reference itself — open the one you want to draw against it from.");
        }

        var (flat, error) = await Task.Run(() => FlattenQuietly(full));
        if (flat is null) return (null, error);
        using (flat) return (AddDocumentStrip(full, flat), null);
    }

    private ReferenceStrip AddDocumentStrip(string full, FlattenedDocument flat)
    {
        var strip = new ReferenceStrip
        {
            Name = DocumentName(full),
            Png = PngCodec.Encode(flat.Sheet),
            SheetWidth = flat.Sheet.Width,
            SheetHeight = flat.Sheet.Height,
            Cells = flat.Cells,
            Document = new DocumentSource
            {
                Path = StoredReferencePath(full),
                Stamp = flat.Stamp,
                OriginX = flat.OriginX,
                OriginY = flat.OriginY,
                Scale = flat.Scale,
            },
        };
        strip.LayOutFrom(CurrentFrameIndex);
        if (flat.SourceWidth == Scene.Width && flat.SourceHeight == Scene.Height)
        {
            // Registration rather than fit-and-centre: on a canvas the same
            // shape, the run's feet land on the jump's ground line — which is
            // the reason to reference a sibling document at all.
            strip.Scale = 1 / flat.Scale;
            strip.OffsetX = flat.OriginX;
            strip.OffsetY = flat.OriginY;
        }
        else
        {
            strip.Scale = FitScale(strip, Scene);
            strip.CentreOn(Scene.Width, Scene.Height);
        }

        var index = 0;
        _editor.Perform(doc =>
        {
            doc.Scene.References ??= [];
            index = doc.Scene.References.Count;
            doc.Scene.References.Add(strip);
            if (strip.Slots.Count > doc.Scene.FrameCount) doc.Scene.FrameCount = strip.Slots.Count;
        });

        ReferenceStripRegistry.Register([(strip.Id, strip.Png)]);
        ActiveReferenceIndex = index;
        AfterReferenceChange();
        return strip;
    }

    /// <summary>
    /// A board tile laid onto the timeline: a document tile lays out its
    /// source's own frames, linked, while the source is there — slicing its
    /// picture would be guessing back what the document already knows. Any
    /// other tile, or one whose source has gone, goes through the slicer.
    /// </summary>
    public async Task<ReferenceStrip?> LayBoardTileOntoTimelineAsync(BoardTile tile, byte[]? pixels)
    {
        if (tile.IsDocument && File.Exists(tile.Origin)
            && await ImportDocumentReferenceAsync(tile.Origin!) is { Strip: { } linked })
        {
            return linked;
        }
        return ImportBoardTileAsAnimation(tile, pixels);
    }

    /// <summary>Strips being flattened right now, so a second trigger does not start a second render.</summary>
    private readonly HashSet<string> _flatteningDocuments = new(StringComparer.Ordinal);

    /// <summary>The fire-and-forget form, for window and tab events.</summary>
    public void RefreshDocumentReferences() => _ = RefreshDocumentReferencesAsync();

    /// <summary>
    /// Re-flatten every document reference whose source file is newer than the
    /// copy it holds. Returns how many changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The stamp is the gate</b>, so calling this often costs a file-time
    /// read per document reference and nothing more. It runs when a document
    /// opens, when a tab comes forward and when the window does — the moments
    /// someone may have saved the source since this one last looked. Only a
    /// stale source is flattened, and off the UI thread.
    /// </para>
    /// <para>
    /// <b>Not an undo step</b>, the rule the live sheet-view flatten already
    /// follows: the pixels are derived from another file, and an undo that
    /// put yesterday's run back under the drawing would be undoing nothing the
    /// artist did here. A source that has gone leaves the last frames standing.
    /// </para>
    /// </remarks>
    public async Task<int> RefreshDocumentReferencesAsync()
    {
        if (Scene.References is not { Count: > 0 } strips) return 0;
        var scene = Scene;
        var stale = new List<(ReferenceStrip Strip, string Source)>();
        foreach (var strip in strips)
        {
            if (!strip.IsDocument || _flatteningDocuments.Contains(strip.Id)) continue;
            if (ResolveReferencePath(strip.Document!.Path) is not { } source || !File.Exists(source)) continue;
            if (DocumentReference.StampOf(source) == strip.Document.Stamp) continue;
            stale.Add((strip, source));
        }
        if (stale.Count == 0) return 0;

        foreach (var (strip, _) in stale) _flatteningDocuments.Add(strip.Id);
        List<FlattenedDocument?> flats;
        try
        {
            flats = await Task.Run(() => stale.Select(s => FlattenQuietly(s.Source).Flat).ToList());
        }
        finally
        {
            foreach (var (strip, _) in stale) _flatteningDocuments.Remove(strip.Id);
        }

        var changed = 0;
        for (var i = 0; i < stale.Count; i++)
        {
            if (flats[i] is not { } flat) continue;
            using (flat)
            {
                Reflatten(stale[i].Strip, flat);
                ReferenceStripRegistry.Replace(stale[i].Strip.Id, flat.Sheet.Copy());
            }
            changed++;
        }
        // The strips belong to the document they were found in; if the artist
        // has switched tabs meanwhile they are updated all the same, and that
        // document shows them when it comes back.
        if (changed > 0 && ReferenceEquals(scene, Scene))
        {
            NotifyReference();
            OnPropertyChanged(nameof(TimelineVideoClips));
            PublishSnapshot();
            ReferenceChanged?.Invoke();
        }
        return changed;
    }

    /// <summary>
    /// A flatten that never throws: a reference is an aid, and a document that
    /// fails to render as one must never be why this one misbehaves.
    /// </summary>
    internal static (FlattenedDocument? Flat, string? Error) FlattenQuietly(string path)
    {
        try
        {
            var flat = DocumentReference.Flatten(path, out var error);
            return (flat, error);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, $"“{Path.GetFileName(path)}” could not be flattened — {ex.Message}");
        }
    }

    /// <summary>
    /// Put new frames into an existing strip without losing what the artist
    /// set on it: placement, opacity and lock stay; each frame keeps its own
    /// nudge and pivot; a run that grew or shrank is laid out again from where
    /// it started.
    /// </summary>
    private static void Reflatten(ReferenceStrip strip, FlattenedDocument flat)
    {
        var old = strip.Cells;
        var cells = flat.Cells;
        for (var i = 0; i < Math.Min(old.Count, cells.Count); i++)
        {
            cells[i].Dx = old[i].Dx;
            cells[i].Dy = old[i].Dy;
            // A pivot is a mark on the drawing in sheet pixels; carry it as a
            // place inside its window, scaled with the window.
            if (old[i].HasPivot && old[i].Width > 0 && old[i].Height > 0)
            {
                var (px, py) = old[i].Pivot;
                cells[i].PivotX = cells[i].X + (px - old[i].X) * cells[i].Width / old[i].Width;
                cells[i].PivotY = cells[i].Y + (py - old[i].Y) * cells[i].Height / old[i].Height;
            }
        }

        // Placement in source terms (doc pixels per source pixel), so a new
        // window or a different shrink keeps the drawing where it was: the
        // run's feet stay on the jump's ground line however the run grew.
        var link = strip.Document!;
        var perSource = strip.Scale * link.Scale;
        strip.OffsetX += (flat.OriginX - link.OriginX) * perSource;
        strip.OffsetY += (flat.OriginY - link.OriginY) * perSource;
        strip.Scale = perSource / flat.Scale;

        if (cells.Count != old.Count)
        {
            var first = Math.Max(0, strip.FirstAssignedSlot());
            if (IsPlainLayout(strip, first, old.Count))
            {
                strip.Cells = cells;
                strip.LayOutFrom(first);
            }
            else
            {
                // Retimed by hand: keep the artist's timing, drop the slots
                // whose drawing is gone.
                for (var i = 0; i < strip.Slots.Count; i++)
                {
                    if (strip.Slots[i] >= cells.Count) strip.Slots[i] = -1;
                }
                strip.Cells = cells;
            }
        }
        else
        {
            strip.Cells = cells;
        }

        strip.Png = PngCodec.Encode(flat.Sheet);
        strip.SheetWidth = flat.Sheet.Width;
        strip.SheetHeight = flat.Sheet.Height;
        link.Stamp = flat.Stamp;
        link.OriginX = flat.OriginX;
        link.OriginY = flat.OriginY;
        link.Scale = flat.Scale;
    }

    /// <summary>Whether the strip still shows cell k at frame first + k and nothing else.</summary>
    private static bool IsPlainLayout(ReferenceStrip strip, int first, int cells)
    {
        for (var i = 0; i < strip.Slots.Count; i++)
        {
            var expected = i >= first && i < first + cells ? i - first : -1;
            if (strip.Slots[i] != expected) return false;
        }
        return true;
    }

    /// <summary>A document's name without the double extension it is saved with.</summary>
    internal static string DocumentName(string path)
    {
        var name = Path.GetFileName(path);
        const string suffix = ".lightbox.json";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>
    /// How a reference keeps a path: relative when the file is at or below
    /// this document's folder, so a project moves as a whole; absolute
    /// otherwise.
    /// </summary>
    private string StoredReferencePath(string path)
    {
        if (Path.GetDirectoryName(SaveTargetTab?.FilePath) is { Length: > 0 } docDir)
        {
            var relative = Path.GetRelativePath(docDir, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            {
                return relative;
            }
        }
        return path;
    }

    /// <summary>A stored reference path made absolute, or null when it cannot be.</summary>
    private string? ResolveReferencePath(string stored)
    {
        if (Path.IsPathRooted(stored)) return stored;
        return Path.GetDirectoryName(SaveTargetTab?.FilePath) is { Length: > 0 } docDir
            ? Path.Combine(docDir, stored)
            : null;
    }
}
