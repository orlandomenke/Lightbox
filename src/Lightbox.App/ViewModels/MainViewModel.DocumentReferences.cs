using Lightbox.App.Services;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
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
        if (!DocumentReference.IsLocal(path))
        {
            return (null, $"“{Path.GetFileName(path)}” is on a network share — copy it to this machine to draw against it.");
        }
        var full = Path.GetFullPath(path);
        // "Is this me" asks the file path and the project row both — a project
        // tab may carry its row and no path, and TabFor already asks both.
        if (SaveTargetTab is { } current && IsTabFor(current, full))
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
        if (tile.IsDocument && DocumentReference.IsLocal(tile.Origin!) && File.Exists(tile.Origin)
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
    public Task<int> RefreshDocumentReferencesAsync() =>
        Scene.References is { Count: > 0 } strips ? RefreshAsync(strips, force: false) : Task.FromResult(0);

    /// <summary>
    /// Re-flatten <paramref name="candidates"/> — those whose source is newer,
    /// or every one whose source is there when <paramref name="force"/> is set.
    /// </summary>
    private async Task<int> RefreshAsync(IEnumerable<ReferenceStrip> candidates, bool force)
    {
        var scene = Scene;
        var stale = new List<(ReferenceStrip Strip, string Source)>();
        foreach (var strip in candidates)
        {
            if (!strip.IsDocument || _flatteningDocuments.Contains(strip.Id)) continue;
            if (ResolveReferencePath(strip.Document!.Path) is not { } source || !File.Exists(source)) continue;
            if (!force && DocumentReference.StampOf(source) == strip.Document.Stamp) continue;
            stale.Add((strip, source));
        }
        if (stale.Count == 0)
        {
            // Nothing to flatten is still news when a source has gone: the
            // link line says so.
            NotifyReferenceLink();
            return 0;
        }

        foreach (var (strip, _) in stale) _flatteningDocuments.Add(strip.Id);
        NotifyReferenceLink();
        List<FlattenedDocument?> flats;
        try
        {
            flats = await Task.Run(() => stale.Select(s => FlattenQuietly(s.Source).Flat).ToList());
        }
        finally
        {
            foreach (var (strip, _) in stale) _flatteningDocuments.Remove(strip.Id);
            NotifyReferenceLink();
        }

        var changed = 0;
        for (var i = 0; i < stale.Count; i++)
        {
            if (flats[i] is not { } flat) continue;
            using (flat)
            {
                // Taken down while it was being flattened: registering it now
                // would hold a sheet for a strip nobody can see.
                if (scene.References?.Contains(stale[i].Strip) != true) continue;
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
        catch (OutOfMemoryException)
        {
            // Caught on purpose, against the usual rule: the alternative is an
            // async click handler taking the app down with unsaved work in the
            // document the artist is drawing in. The flatten's own budgets make
            // this the unexpected case; the crops it held are garbage now.
            return (null, $"“{Path.GetFileName(path)}” is too large to flatten as a reference.");
        }
        catch (Exception ex)
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
    internal static void Reflatten(ReferenceStrip strip, FlattenedDocument flat)
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
                // Retimed by hand: keep the artist's timing exactly. A slot
                // whose drawing is gone shows nothing (CellAt answers null past
                // the end) and shows it again when the drawing comes back —
                // clearing it would lose timing nobody can undo.
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

    /// <summary>
    /// Reference a document in the project — the row's right-click, or the row
    /// dragged onto the canvas. Returns the sentence for the status line.
    /// </summary>
    /// <remarks>
    /// The road from where the animations already are, which is where an
    /// artist reaches for "draw the jump against the run" — the Reference
    /// panel's ＋ and a file picker is the long way round to a file the
    /// project panel is already showing.
    /// </remarks>
    public async Task<string> ReferenceProjectDocumentAsync(DocumentRef reference)
    {
        if (ProjectDocker.Project is not { } project) return "There is no project open.";
        var path = project.PathOf(reference);
        if (!File.Exists(path))
        {
            return $"Save “{reference.Name}” first — a reference reads the saved file.";
        }
        var (strip, error) = await ImportDocumentReferenceAsync(path);
        if (strip is null) return error ?? $"“{reference.Name}” could not be referenced.";
        ReferenceDockerVisible = true;
        return HasUnsavedChanges(path)
            ? $"Drawing against “{reference.Name}” as last saved — it has unsaved changes; save it to update the reference."
            : $"Drawing against “{reference.Name}” — {strip.Cells.Count} frames, kept current as it is saved.";
    }

    /// <summary>
    /// What the active reference is linked to, for the Reference panel — or
    /// null for a reference that is not another document.
    /// </summary>
    /// <remarks>
    /// The link is otherwise invisible: a name in a list cannot say that it
    /// follows a file, which save it shows, or that the file has gone. The save
    /// time is the source's own, not when this document last looked, because
    /// "which version of the run is this" is the question the artist has.
    /// </remarks>
    public string? ReferenceLinkLine
    {
        get
        {
            if (ActiveReference is not { IsDocument: true } strip) return null;
            var name = DocumentName(strip.Document!.Path);
            if (_flatteningDocuments.Contains(strip.Id)) return $"Linked to “{name}” · flattening…";
            if (ResolveReferencePath(strip.Document.Path) is not { } source || !File.Exists(source))
            {
                return $"Linked to “{name}” · not found — showing its last frames";
            }
            var saved = new DateTime(strip.Document.Stamp, DateTimeKind.Utc).ToLocalTime();
            var when = saved.Date == DateTime.Today ? saved.ToString("HH:mm") : saved.ToString("d MMM HH:mm");
            var line = $"Linked to “{name}” · from its save at {when}";
            return HasUnsavedChanges(source) ? line + " · it has unsaved changes — save it to update" : line;
        }
    }

    /// <summary>Whether the active reference follows another document.</summary>
    public bool ActiveReferenceIsDocument => ActiveReference is { IsDocument: true };

    /// <summary>
    /// Flatten the active reference's source again now, whatever its stamp
    /// says — a copy restored from a backup or a sync client that kept the old
    /// time defeat the stamp, and the artist needs a way past it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(ActiveReferenceIsDocument))]
    public async Task RefreshReferenceSourceAsync()
    {
        if (ActiveReference is not { IsDocument: true } strip) return;
        var changed = await RefreshAsync([strip], force: true);
        AiStatus = changed > 0
            ? $"“{strip.Name}” flattened again from its last save."
            : ReferenceLinkLine ?? "";
    }

    /// <summary>
    /// Open the active reference's source — its tab if it is already open, so
    /// the artist edits the one copy rather than starting a second.
    /// </summary>
    [RelayCommand(CanExecute = nameof(ActiveReferenceIsDocument))]
    public async Task OpenReferenceSourceAsync()
    {
        if (ActiveReference is not { IsDocument: true } strip) return;
        if (ResolveReferencePath(strip.Document!.Path) is not { } source || !File.Exists(source))
        {
            AiStatus = ReferenceLinkLine ?? "";
            return;
        }
        if (TabFor(source) is { } open)
        {
            ActiveTab = open;
            return;
        }
        await OpenDocumentFileAsync(source);
    }

    /// <summary>The open tab editing the file at <paramref name="path"/>, or null.</summary>
    private DocumentTab? TabFor(string path)
    {
        var full = Path.GetFullPath(path);
        return Tabs.FirstOrDefault(t => IsTabFor(t, full));
    }

    private bool IsTabFor(DocumentTab tab, string full) =>
        (tab.FilePath is { Length: > 0 } file && SamePath(file, full))
        || (ProjectDocker.Project is { } project && tab.Source is { } source && SamePath(project.PathOf(source), full));

    /// <summary>
    /// Whether the link line is saying something the artist should act on —
    /// the source has gone, or has edits the reference cannot see — so the
    /// panel can show it as a warning rather than a dimmed note.
    /// </summary>
    public bool ReferenceLinkNeedsAttention
    {
        get
        {
            if (ActiveReference is not { IsDocument: true } strip || _flatteningDocuments.Contains(strip.Id)) return false;
            return ResolveReferencePath(strip.Document!.Path) is not { } source
                || !File.Exists(source)
                || HasUnsavedChanges(source);
        }
    }

    /// <summary>Tell the panel the link line, its warning and its buttons may have changed.</summary>
    private void NotifyReferenceLink()
    {
        OnPropertyChanged(nameof(ReferenceLinkLine));
        OnPropertyChanged(nameof(ReferenceLinkNeedsAttention));
        OnPropertyChanged(nameof(ActiveReferenceIsDocument));
        RefreshReferenceSourceCommand.NotifyCanExecuteChanged();
        OpenReferenceSourceCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Hear about every tab's unsaved state, so the link line notices the
    /// source gaining or losing edits while this document is the one on screen
    /// — Save All, a split view, an agent through MCP. A tab raises IsDirty once
    /// per edit or save, never per pointer event, so this costs nothing.
    /// </summary>
    private void WatchTabsForReferenceLinks()
    {
        Tabs.CollectionChanged += (_, e) =>
        {
            foreach (var tab in e.NewItems?.OfType<DocumentTab>() ?? []) tab.PropertyChanged += OnTabChangedForReferenceLink;
            foreach (var tab in e.OldItems?.OfType<DocumentTab>() ?? []) tab.PropertyChanged -= OnTabChangedForReferenceLink;
        };
    }

    private void OnTabChangedForReferenceLink(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTab.IsDirty) && ActiveReferenceIsDocument) NotifyReferenceLink();
    }

    /// <summary>Whether the file at <paramref name="path"/> is open with edits not yet saved.</summary>
    private bool HasUnsavedChanges(string path) => TabFor(path) is { IsDirty: true };

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// A stored reference path made absolute, or null when it cannot be — or
    /// must not be followed.
    /// </summary>
    /// <remarks>
    /// The path came out of a document, which may have come from anyone, and
    /// it is followed without the artist asking. So a network share is never
    /// followed, and a relative path stays relative: it resolves inside this
    /// document's folder or not at all, never up out of it with "..".
    /// Absolute local paths are followed, as <see cref="ReferenceStrip.VideoPath"/>
    /// already is, because that is what an artist's own pick outside the
    /// folder is stored as.
    /// </remarks>
    internal string? ResolveReferencePath(string stored)
    {
        if (!DocumentReference.IsLocal(stored)) return null;
        if (Path.IsPathRooted(stored)) return stored;
        if (Path.GetDirectoryName(SaveTargetTab?.FilePath) is not { Length: > 0 } docDir) return null;
        var root = Path.GetFullPath(docDir);
        var resolved = Path.GetFullPath(Path.Combine(root, stored));
        var inside = Path.GetRelativePath(root, resolved);
        return inside.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(inside) ? null : resolved;
    }
}
