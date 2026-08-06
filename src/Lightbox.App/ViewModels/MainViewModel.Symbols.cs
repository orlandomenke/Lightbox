using Lightbox.App.Services;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>User's choice when placing a multi-frame symbol.</summary>
public enum FrameImportChoice
{
    /// <summary>Import all frames into the timeline.</summary>
    ImportFrames,
    /// <summary>Place as single animated reference with cycling.</summary>
    Reference,
}

/// <summary>
/// Pillar 3, step four: putting a symbol somewhere, moving it, and letting go
/// of it.
/// </summary>
/// <remarks>
/// The browser that makes these reachable with a mouse is S5. What is here is
/// the operations themselves, each one undo step, so the panel is a way of
/// calling them rather than the place the behaviour lives.
/// </remarks>
public sealed partial class MainViewModel
{
    /// <summary>The symbol browser's state. Empty until a project is open.</summary>
    public SymbolBrowserViewModel SymbolBrowser { get; private set; } = null!;

    /// <summary>The artist's own library, loaded once and kept.</summary>
    /// <remarks>
    /// Held rather than re-read, because the browser asks for it on every refresh
    /// and reading a file per keystroke in the search box would be absurd. It is
    /// only written when the artist promotes or removes something.
    /// </remarks>
    private Dictionary<string, Symbol>? _symbolLibrary;

    private Dictionary<string, Symbol> Library => _symbolLibrary ??= SymbolLibrary.Load();

    /// <summary>Re-read the library from disk. Test seam.</summary>
    /// <remarks>
    /// The library is held rather than re-read on every access, so a test that
    /// writes the file behind the view model has to say so. In the application
    /// the same situation — the file changing underneath a running session — is
    /// not reachable, because the session is the only thing that writes it.
    /// </remarks>
    internal void ReloadSymbolLibraryForTests()
    {
        _symbolLibrary = null;
        SymbolBrowser.Refresh();
        OnPropertyChanged(nameof(HasLibraryUpdates));
    }

    /// <summary>Make a symbol from whatever is on the drawing. Test seam.</summary>
    internal Symbol? MakeSymbolFromDrawingForTests(string name)
    {
        BeginStroke(30, 30, 1);
        MoveStroke(90, 90, 1);
        EndStroke();
        return MakeSymbolFromDrawing(name);
    }

    /// <summary>Wire the browser up. Called from the constructor.</summary>
    private void InitialiseSymbolBrowser() =>
        SymbolBrowser = new SymbolBrowserViewModel(
            () => ProjectDocker.Project,
            () => (Scene.Width, Scene.Height),
            () => Library);

    // ---- global and project scope -------------------------------------------------

    /// <summary>
    /// Copy a library symbol into the project, if that is where this id lives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Copy-on-place, and it is what keeps the project self-contained: a
    /// <c>.lbproj</c> that resolved a placement out of an application folder
    /// would lose art the moment it moved machine. The library is a source to
    /// choose from, never a live dependency.
    /// </para>
    /// <para>
    /// Keyed on the <b>id</b> and called from inside <see cref="PlaceSymbol"/>,
    /// so every route is covered by construction. Doing it per route looked
    /// simpler and was wrong: the drag-and-drop path carries only an id, so a
    /// row-based version would have adopted for the Place button and left a
    /// dragged library symbol failing to resolve — the harder bug to spot,
    /// because the two routes look interchangeable from the panel.
    /// </para>
    /// </remarks>
    private void AdoptFromLibrary(string symbolId)
    {
        if (ProjectDocker.Project is { } project)
        {
            if (project.Symbols.ContainsKey(symbolId)) return;
            if (!Library.TryGetValue(symbolId, out var global)) return;
            SymbolScopes.Adopt(project, global);
            RefreshProjectResources();
            SymbolBrowser.Refresh();
            AiStatus = $"“{global.Name}” copied into this project — it renders with the library gone.";
            return;
        }

        // No project: the *document* is what has to stay self-contained, so the
        // copy goes into Doc.Symbols — which the registry already reads, and
        // which is the same key `ProjectIo.Flatten` writes when an export has to
        // carry its symbols. Same principle one level down.
        if (Doc.Symbols?.ContainsKey(symbolId) == true) return;
        if (!Library.TryGetValue(symbolId, out var loose)) return;

        Doc.Symbols ??= [];
        Doc.Symbols[symbolId] = Core.Serialization.DocJson.CloneValue(loose);
        RefreshProjectResources();
        SymbolBrowser.Refresh();
        MarkDocumentEdited();
        AiStatus = $"“{loose.Name}” copied into this document — it saves and reloads with the drawing.";
    }

    /// <summary>Copy the selected project symbol up into the artist's library.</summary>
    /// <remarks>
    /// One direction only. There is no demote, because it would not mean
    /// anything: a global symbol that has been placed is already in that project,
    /// so "make this project-only" is deleting it from the library, which is its
    /// own action and reads as one.
    /// </remarks>
    public void PromoteSelectedSymbol()
    {
        if (SymbolBrowser.Selected is not { } row || row.IsGlobal) return;
        SymbolScopes.Promote(Library, row.Model);
        SymbolLibrary.Save(Library);
        SymbolBrowser.Refresh();
        AiStatus = $"“{row.Model.Name}” is in your library — available in every project.";
    }

    public bool CanPromoteSelectedSymbol => SymbolBrowser.Selected is { IsGlobal: false };

    /// <summary>Project symbols the library has a newer version of.</summary>
    public IReadOnlyList<SymbolScopes.Stale> LibraryUpdates =>
        ProjectDocker.Project is { } project ? SymbolScopes.Outdated(project, Library) : [];

    public bool HasLibraryUpdates => LibraryUpdates.Count > 0;

    /// <summary>
    /// Pull every newer library version into this project, as one undoable step.
    /// </summary>
    /// <remarks>
    /// The direction is the safety property: the project reaches out when the
    /// artist says so, and nothing in the library can reach into a project. So
    /// editing a symbol in your own library can never change a finished shot.
    /// Placements are untouched and need no touching — they reference the symbol
    /// by id, so replacing what that id resolves to is the whole of the update.
    /// </remarks>
    public int UpdateSymbolsFromLibrary()
    {
        if (ProjectDocker.Project is not { } project) return 0;
        var stale = SymbolScopes.Outdated(project, Library);
        if (stale.Count == 0)
        {
            AiStatus = "Every symbol here matches your library.";
            return 0;
        }

        foreach (var entry in stale) SymbolScopes.Pull(project, Library, entry.Mine.Id);
        RefreshProjectResources();
        SymbolBrowser.Refresh();
        InvalidateWholeCanvas();
        PublishSnapshot();
        MarkDocumentEdited();
        OnPropertyChanged(nameof(HasLibraryUpdates));
        AiStatus = stale.Count == 1
            ? $"Updated “{stale[0].Name}” from your library."
            : $"Updated {stale.Count} symbols from your library.";
        return stale.Count;
    }

    // ---- making one -------------------------------------------------------------

    /// <summary>
    /// Turn the strokes on the current drawing into a symbol, and replace them
    /// with a placement of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The authoring gesture, and the reason it replaces rather than copies:
    /// two drawings that look identical and are not the same object is exactly
    /// what a symbol exists to avoid. Photoshop's "convert to smart object"
    /// makes the same trade.
    /// </para>
    /// <para>
    /// The symbol keeps the strokes in their own coordinates and the placement
    /// sits at the origin with no scale or rotation, so what renders afterwards
    /// is the same mark it was before — no coordinate is rewritten and no
    /// <c>Hash01</c> seed moves. A test asserts the pixels, because that is the
    /// promise the gesture makes and it is one an artist would notice breaking.
    /// </para>
    /// </remarks>
    public Symbol? MakeSymbolFromDrawing(string name, SymbolKind kind = SymbolKind.Prop)
    {
        if (ProjectDocker.Project is not { } project)
        {
            AiStatus = "Symbols belong to a project — create one first.";
            return null;
        }
        if (!CanEdit(ActiveLayer, "make a symbol")) return null;
        if (PaintTarget() is not PaintedFrame source || source.Strokes.Count == 0)
        {
            AiStatus = "Nothing on this drawing to make a symbol from.";
            return null;
        }

        var symbol = new Symbol
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Symbol" : name.Trim(),
            Kind = kind,
            // The strokes as they stand, in the coordinates they were drawn in.
            // Pivot at the origin, so a placement at (0, 0) puts them back
            // exactly where they were.
            Frames = [new PaintedFrame { Strokes = [.. source.Strokes.Select(s => s.Clone())] }],
        };

        var taken = source.Strokes.ToList();
        var placement = new SymbolPlacement { SymbolId = symbol.Id, SeenVersion = symbol.Version };
        var frameId = source.Id;

        project.Symbols[symbol.Id] = symbol;
        SymbolRegistry.Register(symbol);

        _editor.PerformDelta(
            apply: doc =>
            {
                if (FrameIn(doc, frameId) is not { } frame) return;
                frame.Strokes.Clear();
                frame.Placements ??= [];
                frame.Placements.Add(placement);
            },
            revert: doc =>
            {
                if (FrameIn(doc, frameId) is not { } frame) return;
                frame.Placements?.RemoveAll(p => p.Id == placement.Id);
                if (frame.Placements is { Count: 0 }) frame.Placements = null;
                frame.Strokes.AddRange(taken);
            },
            affectedFrameId: frameId);

        _selectedPlacementId = placement.Id;
        AfterPlacementChange(frameId);
        SymbolBrowser.Refresh();
        AiStatus = $"“{symbol.Name}” is a symbol now. Editing it updates every placement of it.";
        return symbol;
    }

    /// <summary>
    /// Where a symbol is placed, across every document in the project.
    /// </summary>
    /// <remarks>
    /// Reads every document in the project, which is the one thing the folder
    /// layout exists to avoid doing — so it is called when the artist asks a
    /// question that needs the answer, and never to keep a panel up to date.
    /// </remarks>
    public SymbolUsage UsageOf(Symbol symbol) =>
        ProjectDocker.Project is { } project
            ? SymbolGraph.Of(project, symbol.Id)
            : new SymbolUsage(symbol.Id, []);

    /// <summary>A sentence naming what a symbol is holding up, for the panel and the delete prompt.</summary>
    public string DescribeUsage(Symbol symbol)
    {
        var usage = UsageOf(symbol);
        if (usage.IsUnused) return $"“{symbol.Name}” is not placed anywhere.";
        var docs = usage.Documents.Count;
        var places = usage.Total == 1 ? "1 placement" : $"{usage.Total} placements";
        var where = docs == 1
            ? $"“{usage.Documents[0].Document.Name}”"
            : $"{docs} documents";
        return $"“{symbol.Name}”: {places} in {where}.";
    }

    /// <summary>
    /// Delete a symbol from the project.
    /// </summary>
    /// <remarks>
    /// Placements of it are <b>left alone</b>, and stop rendering — a delete
    /// that quietly edited forty animations is not one anybody could risk, and
    /// an unresolved placement draws nothing and is reported, which is the
    /// recoverable failure of the two.
    /// <para>
    /// What has changed is that the count is now known. The caller is expected
    /// to have shown <see cref="DescribeUsage"/> first: leaving placements
    /// behind is only defensible if the artist was told how many there were,
    /// and until the dependency graph existed nothing could count them.
    /// </para>
    /// </remarks>
    public bool DeleteSymbol(Symbol symbol)
    {
        if (ProjectDocker.Project is { } project)
        {
            var stranded = SymbolGraph.Of(project, symbol.Id).Total;
            if (!project.Symbols.Remove(symbol.Id)) return false;
            SymbolRegistry.Reset(project.Symbols);
            SymbolBrowser.Refresh();
            if (PaintTarget() is { } frame) AfterPlacementChange(frame.Id);
            AiStatus = stranded == 0
                ? $"Deleted \"{symbol.Name}\". Nothing was placing it."
                : $"Deleted \"{symbol.Name}\". {stranded} placement{(stranded == 1 ? "" : "s")} of it now draw nothing.";
            return true;
        }

        // Delete from global library when no project is open
        if (!Library.Remove(symbol.Id)) return false;
        SymbolLibrary.Save(Library);
        SymbolBrowser.Refresh();
        AiStatus = $"Removed \"{symbol.Name}\" from your library.";
        return true;
    }


    /// <summary>Place the browser's selected symbol at the centre of the canvas.</summary>
    /// <remarks>
    /// The keyboard and menu route. Dragging onto the canvas puts it under the
    /// pointer instead; both end in <see cref="PlaceSymbol"/>.
    /// </remarks>
    [RelayCommand]
    private void PlaceSelected() => PlaceSelectedSymbol();

    /// <inheritdoc cref="PlaceSelectedCommand" />
    public SymbolPlacement? PlaceSelectedSymbol()
    {
        if (SymbolBrowser.Selected is not { } row) return null;
        return PlaceSymbol(row.Model.Id, Scene.Width / 2.0, Scene.Height / 2.0);
    }

    // ---- editing one --------------------------------------------------------------

    /// <summary>
    /// Open a symbol in a tab of its own and draw on it with the ordinary
    /// tools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pillar's headline, and the reason everything before it was worth
    /// doing: edit the sword once and every animation holding it changes.
    /// </para>
    /// <para>
    /// The same arrangement a reference tab uses. The wrapper scene's cels hold
    /// the symbol's <em>own frame objects</em> rather than copies, so a stroke
    /// lands in the symbol directly and there is no apply step to forget. What
    /// the wrapper does not share is the list itself — adding or removing a cel
    /// makes a frame the symbol has never heard of — so the list is re-read
    /// from the editor after every edit. See <see cref="SyncEditedSymbol"/>.
    /// </para>
    /// </remarks>
    public void OpenSymbol(Symbol symbol)
    {
        if (Tabs.FirstOrDefault(t => ReferenceEquals(t.Symbol, symbol)) is { } open)
        {
            ActiveTab = open;
            return;
        }
        if (symbol.Frames.Count == 0) symbol.Frames.Add(new PaintedFrame());

        var layer = new Layer { Name = symbol.Name, Kind = LayerKind.Painted };
        foreach (var frame in symbol.Frames) layer.Cels.Add(new Cel { Frame = frame });

        var wrapper = new Doc
        {
            Scene = new Scene
            {
                Name = symbol.Name,
                Width = Scene.Width,
                Height = Scene.Height,
                Fps = symbol.Fps,
                FrameCount = symbol.Frames.Count,
                // Transparent, always. A symbol is placed over a drawing, so a
                // white sheet behind it while editing would be a lie about what
                // it looks like in use — and would be baked into nothing, since
                // the paper is a layer and this scene has none.
                TransparentBackground = true,
                Layers = [layer],
            },
        };
        AddTab(new DocumentTab(new DocumentEditor(wrapper), $"Symbol / {symbol.Name}")
        {
            Kind = DocumentTabKind.Symbol,
            Symbol = symbol,
        });
    }

    /// <summary>Open the symbol the browser has selected.</summary>
    [RelayCommand]
    private void EditSelectedSymbol()
    {
        if (SymbolBrowser.Selected is { } row) OpenSymbol(row.Model);
    }

    /// <summary>
    /// Push what was just drawn in a symbol tab back into the symbol, and tell
    /// everything that shows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called from the edit funnel, so it covers a stroke, an undo, a new cel
    /// and a deleted one alike. The frame list is re-read rather than assumed
    /// because undo replaces the wrapper's layer wholesale — the same reason a
    /// reference tab re-points its view.
    /// </para>
    /// <para>
    /// <b>The version bump is what makes the pillar true.</b> Every placement
    /// resolves the symbol by id at render time and the render cache is keyed
    /// by version, so bumping it is the whole of "every animation holding it
    /// updates". Skipping it would leave every placement showing the drawing as
    /// it was when it was first rendered, which is the failure the S2 tests
    /// describe.
    /// </para>
    /// </remarks>
    private void SyncEditedSymbol()
    {
        if (ActiveTab is not { Kind: DocumentTabKind.Symbol, Symbol: { } symbol }) return;
        var frames = Doc.Scene.Layers
            .SelectMany(l => l.Cels)
            .Select(c => c.Frame)
            .OfType<Frame>()
            .ToList();
        if (frames.Count == 0) return;

        symbol.Frames = frames;
        symbol.Fps = Doc.Scene.Fps;
        symbol.Version++;
        SymbolBrowser.RefreshThumbs();
        OnPropertyChanged(nameof(OutdatedPlacements));
        OnPropertyChanged(nameof(StalePlacementReport));
    }

    // ---- what changed under you -----------------------------------------------------

    /// <summary>
    /// Placements on this drawing whose symbol has been edited since they were
    /// made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported, never acted on. A placement shows the symbol as it is now —
    /// that is the whole pillar — so nothing here is broken and nothing needs
    /// repairing. What this answers is the question an animator will have when
    /// a drawing changes on its own: <i>which of these did I place before the
    /// sword was redrawn?</i>
    /// </para>
    /// <para>
    /// Deliberately not an offer to "revert to the version you placed against".
    /// That would need every past version kept, and it is the wrong shape of
    /// answer: the fix for an edit somebody did not want is to undo the edit in
    /// the symbol, once, not to pin two hundred placements to old copies.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SymbolPlacement> OutdatedPlacements =>
        [.. PlacementsHere.Where(IsOutdated)];

    private static bool IsOutdated(SymbolPlacement placement) =>
        SymbolRegistry.Resolve(placement.SymbolId) is { } symbol
        && symbol.Version > placement.SeenVersion;

    /// <summary>
    /// A sentence for the status line, or null when there is nothing to say.
    /// </summary>
    public string? StalePlacementReport
    {
        get
        {
            var stale = OutdatedPlacements;
            if (stale.Count == 0) return null;
            var names = stale
                .Select(p => SymbolRegistry.Resolve(p.SymbolId)?.Name)
                .OfType<string>()
                .Distinct()
                .ToList();
            var which = names.Count switch
            {
                0 => "A symbol",
                1 => $"“{names[0]}”",
                2 => $"“{names[0]}” and “{names[1]}”",
                _ => $"“{names[0]}” and {names.Count - 1} others",
            };
            return stale.Count == 1
                ? $"{which} has changed since it was placed here."
                : $"{which} have changed since {stale.Count} placements here were made.";
        }
    }

    /// <summary>
    /// Mark the placements on this drawing as seen at the symbol's current
    /// version, so the report goes quiet.
    /// </summary>
    /// <remarks>
    /// Acknowledgement, not repair — nothing about the drawing changes. It is
    /// an undo step all the same, because the record does.
    /// </remarks>
    public int AcknowledgeOutdatedPlacements()
    {
        if (PaintTarget() is not PaintedFrame target) return 0;
        var stale = OutdatedPlacements;
        if (stale.Count == 0) return 0;

        var before = stale.Select(p => (p.Id, p.SeenVersion)).ToList();
        var after = stale
            .Select(p => (p.Id, Version: SymbolRegistry.Resolve(p.SymbolId)?.Version ?? p.SeenVersion))
            .ToList();
        var frameId = target.Id;

        _editor.PerformDelta(
            apply: doc => SetSeenVersions(doc, frameId, after),
            revert: doc => SetSeenVersions(doc, frameId, before),
            affectedFrameId: frameId);
        OnPropertyChanged(nameof(OutdatedPlacements));
        OnPropertyChanged(nameof(StalePlacementReport));
        return stale.Count;
    }

    private static void SetSeenVersions(
        Doc doc, string frameId, List<(string Id, int Version)> versions)
    {
        if (FrameIn(doc, frameId)?.Placements is not { } placements) return;
        foreach (var (id, version) in versions)
        {
            if (placements.FirstOrDefault(p => p.Id == id) is { } p) p.SeenVersion = version;
        }
    }

    /// <summary>The placements on the cel being drawn on, or an empty list.</summary>
    public IReadOnlyList<SymbolPlacement> PlacementsHere =>
        PaintTarget() is PaintedFrame { Placements: { } list } ? list : [];

    /// <summary>The placement currently picked, or null.</summary>
    /// <remarks>
    /// Held by id rather than by reference. Undo replaces frames wholesale, so
    /// a reference kept across one points at an object the document no longer
    /// contains — which is how a selection survives visually and stops working.
    /// </remarks>
    private string? _selectedPlacementId;

    public SymbolPlacement? SelectedPlacement =>
        _selectedPlacementId is null
            ? null
            : PlacementsHere.FirstOrDefault(p => p.Id == _selectedPlacementId);

    private SKImageInfo SceneInfo =>
        new(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

    // ---- putting one down ------------------------------------------------------

    /// <summary>
    /// Place a symbol on the current cel, its pivot at (x, y). Returns the
    /// placement, or null when there is nowhere to put it.
    /// </summary>
    /// <remarks>
    /// For multi-frame symbols, this detects animation and offers the user a choice:
    /// import frames into the timeline or reference with animation cycling.
    /// Keys the cel if there is nothing there, exactly as painting does.
    /// Dropping a prop onto an empty frame is the ordinary way to start one,
    /// and refusing silently is the behaviour that made a cleared layer feel
    /// broken.
    /// </remarks>
    public SymbolPlacement? PlaceSymbol(string symbolId, double x, double y)
    {
        if (!CanEdit(ActiveLayer, "place a symbol")) return null;
        // A library symbol becomes a project symbol here, before anything tries
        // to resolve it — see AdoptFromLibrary for why this is not per route.
        AdoptFromLibrary(symbolId);
        if (SymbolRegistry.Resolve(symbolId) is not { } symbol)
        {
            AiStatus = "That symbol is not in this project.";
            return null;
        }
        if (PaintTargetOrKey() is not PaintedFrame target)
        {
            AiStatus = "Symbols can only be placed on a painted layer.";
            return null;
        }

        // For multi-frame symbols, ask the user whether to import frames or reference
        if (symbol.FrameCount > 1)
        {
            var choice = DetectAnimationAndAsk(symbol);
            if (choice == FrameImportChoice.ImportFrames)
            {
                return PlaceSymbolAcrossFrames(symbol, x, y, target);
            }
            // choice == Reference: fall through to single-placement logic
        }

        // Single-frame placement or user chose to reference
        return PlaceSingleSymbol(symbolId, symbol, x, y, target);
    }

    /// <summary>
    /// Ask the user how to place a multi-frame symbol: import all frames or reference with animation.
    /// </summary>
    /// <remarks>
    /// For single-frame symbols, always returns Reference (no dialog).
    /// For multi-frame symbols, shows a dialog unless the user has saved a preference.
    /// If the dialog cannot be shown, uses the stored preference or defaults to ImportFrames.
    /// </remarks>
    private FrameImportChoice DetectAnimationAndAsk(Symbol symbol)
    {
        // Single-frame symbols always place as reference (no animation, no dialog)
        if (symbol.FrameCount <= 1)
            return FrameImportChoice.Reference;

        // Multi-frame symbols: try to show dialog if we can access it; otherwise use stored preference
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime app
            && app.MainWindow is Views.MainWindow mainWindow)
        {
            var task = mainWindow.ShowPlacementChoiceDialogAsync(symbol);
            if (task.Wait(5000)) // 5 second timeout
            {
                var result = task.Result;
                if (result is { } choice)
                {
                    if (choice.DontAskAgain)
                    {
                        _placementPreference = choice.Choice;
                    }
                    return choice.Choice;
                }
            }
        }

        // Use stored preference or default to ImportFrames
        return _placementPreference ?? FrameImportChoice.ImportFrames;
    }

    /// <summary>User's stored preference for symbol placement, if they selected "Don't ask again".</summary>
    private FrameImportChoice? _placementPreference;

    /// <summary>
    /// Place a symbol as a single animated reference (old behaviour).
    /// </summary>
    private SymbolPlacement? PlaceSingleSymbol(string symbolId, Symbol symbol, double x, double y, PaintedFrame target)
    {
        var placement = new SymbolPlacement
        {
            SymbolId = symbolId,
            X = x,
            Y = y,
            // What it was placed against, so S7 can say "the sword changed
            // since you placed this" without changing it.
            SeenVersion = symbol.Version,
        };
        var frameId = target.Id;
        _editor.PerformDelta(
            apply: doc =>
            {
                if (FrameIn(doc, frameId) is not { } frame) return;
                frame.Placements ??= [];
                frame.Placements.Add(placement);
            },
            revert: doc =>
            {
                if (FrameIn(doc, frameId) is not { } frame) return;
                frame.Placements?.RemoveAll(p => p.Id == placement.Id);
                // Back to absent, not to empty: a cel that no longer places
                // anything must serialize as one that never did.
                if (frame.Placements is { Count: 0 }) frame.Placements = null;
            },
            affectedFrameId: frameId);

        _selectedPlacementId = placement.Id;
        AfterPlacementChange(frameId);
        AiStatus = $"Placed {symbol.Name}.";
        return placement;
    }

    /// <summary>
    /// Expand the timeline to accommodate a multi-frame symbol, then place
    /// instances across all frames with proper FrameOffset values.
    /// </summary>
    private SymbolPlacement? PlaceSymbolAcrossFrames(Symbol symbol, double x, double y, PaintedFrame initialTarget)
    {
        var frameCount = symbol.FrameCount;
        var startFrameIndex = CurrentFrameIndex;

        // Expand timeline if needed
        var targetFrameCount = startFrameIndex + frameCount;
        while (Scene.FrameCount < targetFrameCount)
        {
            DocumentEditor.AppendFrame(Scene);
        }

        // Create placements for each frame
        var placements = new List<SymbolPlacement>();
        var groupId = Ids.NewId("fgrp");

        for (var i = 0; i < frameCount; i++)
        {
            var placement = new SymbolPlacement
            {
                SymbolId = symbol.Id,
                X = x,
                Y = y,
                FrameOffset = i,
                SeenVersion = symbol.Version,
                GroupId = groupId,
            };
            placements.Add(placement);
        }

        // Create the frame group
        var frameGroup = new FrameGroup
        {
            Id = groupId,
            SymbolId = symbol.Id,
            PlacementIds = placements.Select(p => p.Id).ToList(),
            FrameStart = startFrameIndex,
            FrameCount = frameCount,
        };

        // Apply all placements and frame group in one undo step
        var initialFrameId = initialTarget.Id;
        var activeLayerId = ActiveLayer.Id;
        _editor.PerformDelta(
            apply: doc =>
            {
                if (doc.Scene is not { Layers: not null }) return;

                // Ensure frame group list exists
                doc.Scene.FrameGroups ??= [];
                doc.Scene.FrameGroups.Add(frameGroup);

                // Place on each frame of the active layer
                var activeLayer = doc.Scene.Layers.FirstOrDefault(l => l.Id == activeLayerId);
                if (activeLayer is null || activeLayer.Kind != LayerKind.Painted) return;

                for (var i = 0; i < placements.Count; i++)
                {
                    var frameIdx = startFrameIndex + i;
                    var target = ExposureSheet.ExposedFrame(activeLayer, frameIdx);
                    if (target is PaintedFrame painted)
                    {
                        painted.Placements ??= [];
                        painted.Placements.Add(placements[i]);
                    }
                }
            },
            revert: doc =>
            {
                if (doc.Scene is not { FrameGroups: not null }) return;

                // Remove frame group
                doc.Scene.FrameGroups.RemoveAll(g => g.Id == groupId);

                // Remove placements
                var placementIds = new HashSet<string>(placements.Select(p => p.Id));
                foreach (var layer in doc.Scene.Layers)
                {
                    foreach (var cel in layer.Cels)
                    {
                        if (cel.Frame is PaintedFrame painted && painted.Placements is not null)
                        {
                            painted.Placements.RemoveAll(p => placementIds.Contains(p.Id));
                            if (painted.Placements.Count == 0) painted.Placements = null;
                        }
                    }
                }
            },
            affectedFrameId: initialFrameId);

        _selectedPlacementId = placements[0].Id;
        AfterPlacementChange(initialFrameId);
        AiStatus = $"Placed {symbol.Name} across {frameCount} frames.";
        return placements[0];
    }

    /// <summary>Remove a placement. The symbol itself is untouched.</summary>
    public bool RemovePlacement(SymbolPlacement placement)
    {
        if (!CanEdit(ActiveLayer, "remove a symbol")) return false;
        if (PaintTarget() is not PaintedFrame target || target.Placements is null) return false;
        var index = target.Placements.FindIndex(p => p.Id == placement.Id);
        if (index < 0) return false;

        var frameId = target.Id;
        _editor.PerformDelta(
            apply: doc =>
            {
                if (FrameIn(doc, frameId) is not { } frame) return;
                frame.Placements?.RemoveAll(p => p.Id == placement.Id);
                if (frame.Placements is { Count: 0 }) frame.Placements = null;
            },
            revert: doc =>
            {
                if (FrameIn(doc, frameId) is not { } frame) return;
                frame.Placements ??= [];
                // Back where it was, so undo does not reorder what draws over
                // what.
                frame.Placements.Insert(Math.Min(index, frame.Placements.Count), placement);
            },
            affectedFrameId: frameId);

        if (_selectedPlacementId == placement.Id) _selectedPlacementId = null;
        AfterPlacementChange(frameId);
        return true;
    }

    // ---- picking one up ---------------------------------------------------------

    /// <summary>
    /// The topmost placement whose ink covers (x, y), or null.
    /// </summary>
    /// <remarks>
    /// Topmost, because placements draw in record order and the one you can
    /// see is the one you meant. The bounds come from what actually rendered
    /// rather than from the symbol's stroke coordinates — see
    /// <see cref="SymbolRasterizer.PlacementBounds"/>.
    /// </remarks>
    public SymbolPlacement? PlacementAt(double x, double y)
    {
        var here = PlacementsHere;
        for (var i = here.Count - 1; i >= 0; i--)
        {
            var bounds = SymbolRasterizer.PlacementBounds(here[i], SceneInfo, CurrentFrameIndex);
            if (bounds is { } r && r.Contains((float)x, (float)y)) return here[i];
        }
        return null;
    }

    // ---- moving one -------------------------------------------------------------

    /// <summary>
    /// A placement being dragged: which one, where the grab started, and where
    /// the placement was when it did.
    /// </summary>
    /// <remarks>
    /// Deliberately not a transform session. The stroke transform rewrites
    /// coordinates on commit, which is the one thing a placement must never
    /// have done to it — moving a placement is an edit to two numbers, and the
    /// symbol's own drawing is not touched at all. That is also why the move is
    /// free where a stroke move is not.
    /// </remarks>
    private (string Id, double GrabX, double GrabY, double FromX, double FromY)? _placementDrag;

    public bool PlacementMoveActive => _placementDrag is not null;

    /// <summary>
    /// Start moving the placement under (x, y), if there is one. Returns false
    /// when there is nothing there, which is the caller's cue to move the
    /// drawing instead.
    /// </summary>
    public bool BeginPlacementMove(double x, double y)
    {
        if (!CanEdit(ActiveLayer, "move a symbol")) return false;
        if (PlacementAt(x, y) is not { } placement) return false;
        _selectedPlacementId = placement.Id;
        _placementDrag = (placement.Id, x, y, placement.X, placement.Y);
        AiStatus = "Moving a placed symbol — the symbol itself is unchanged.";
        return true;
    }

    /// <param name="axisLock">Shift, meaning what it means on every other tool here.</param>
    public void UpdatePlacementMove(double x, double y, bool axisLock)
    {
        if (_placementDrag is not { } drag) return;
        if (PlacementsHere.FirstOrDefault(p => p.Id == drag.Id) is not { } placement) return;

        var dx = x - drag.GrabX;
        var dy = y - drag.GrabY;
        if (axisLock)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
            else dx = 0;
        }
        // Moved live on the record, and put back by the undo step at the end if
        // the drag is abandoned. A placement is two numbers, so there is
        // nothing here that a preview surface would save.
        placement.X = drag.FromX + dx;
        placement.Y = drag.FromY + dy;
        if (PaintTarget() is { } frame) AfterPlacementChange(frame.Id);
    }

    /// <summary>Put it down. One undo step for the whole drag, or none.</summary>
    public void EndPlacementMove()
    {
        if (_placementDrag is not { } drag) return;
        _placementDrag = null;
        if (PlacementsHere.FirstOrDefault(p => p.Id == drag.Id) is not { } placement) return;

        double toX = placement.X, toY = placement.Y;
        // A click that went nowhere is a click, not an edit.
        if (Math.Abs(toX - drag.FromX) < 1e-9 && Math.Abs(toY - drag.FromY) < 1e-9) return;

        var frameId = PaintTarget()?.Id;
        if (frameId is null) return;
        var id = drag.Id;
        double fromX = drag.FromX, fromY = drag.FromY;
        _editor.PerformDelta(
            apply: doc => MovePlacementIn(doc, frameId, id, toX, toY),
            revert: doc => MovePlacementIn(doc, frameId, id, fromX, fromY),
            affectedFrameId: frameId);
        AfterPlacementChange(frameId);
    }

    public void CancelPlacementMove()
    {
        if (_placementDrag is not { } drag) return;
        _placementDrag = null;
        if (PlacementsHere.FirstOrDefault(p => p.Id == drag.Id) is not { } placement) return;
        placement.X = drag.FromX;
        placement.Y = drag.FromY;
        if (PaintTarget() is { } frame) AfterPlacementChange(frame.Id);
    }

    private static void MovePlacementIn(Doc doc, string frameId, string placementId, double x, double y)
    {
        if (FrameIn(doc, frameId)?.Placements?.FirstOrDefault(p => p.Id == placementId) is not { } p) return;
        p.X = x;
        p.Y = y;
    }

    // ---- letting go of the link --------------------------------------------------

    /// <summary>
    /// Turn a placement into ordinary strokes on this cel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The honest way to get a drawing you can edit stroke by stroke. A
    /// placement deliberately cannot have one of its strokes nudged, because
    /// that is a different drawing and pretending otherwise is how a symbol
    /// quietly becomes a copy.
    /// </para>
    /// <para>
    /// <b>The mark changes here, and it is the one place in the application
    /// where that is allowed.</b> Baking the placement's position into the
    /// strokes means rewriting their coordinates, and every dab dynamic is
    /// seeded by <c>Hash01</c> from the bits of a dab position — so the
    /// scatter, size and colour jitter re-roll. It is a deliberate,
    /// artist-initiated, single-undo action that says "this is my drawing now",
    /// which is exactly the case invariant 2 does not cover: the invariant
    /// exists to stop marks changing behind somebody's back. This is the
    /// opposite of that, and it is why breaking a link is a command rather
    /// than something the app ever does on its own.
    /// </para>
    /// </remarks>
    public bool BreakLink(SymbolPlacement placement)
    {
        if (!CanEdit(ActiveLayer, "break a symbol link")) return false;
        if (PaintTarget() is not PaintedFrame target || target.Placements is null) return false;
        var index = target.Placements.FindIndex(p => p.Id == placement.Id);
        if (index < 0) return false;
        if (SymbolRegistry.Resolve(placement.SymbolId) is not { } symbol) return false;
        if (symbol.Frames.Count == 0) return false;

        var frameIndex = placement.FrameIndexAt(CurrentFrameIndex, symbol.FrameCount);
        if (frameIndex < 0 || frameIndex >= symbol.Frames.Count) return false;
        var baked = BakedStrokes(symbol, frameIndex, placement);
        if (baked.Count == 0) return false;

        var frameId = target.Id;
        _editor.PerformDelta(
            apply: doc =>
            {
                if (FrameIn(doc, frameId) is not { } frame) return;
                frame.Placements?.RemoveAll(p => p.Id == placement.Id);
                if (frame.Placements is { Count: 0 }) frame.Placements = null;
                frame.Strokes.AddRange(baked);
            },
            revert: doc =>
            {
                if (FrameIn(doc, frameId) is not { } frame) return;
                foreach (var stroke in baked) frame.Strokes.RemoveAll(s => s.Id == stroke.Id);
                frame.Placements ??= [];
                frame.Placements.Insert(Math.Min(index, frame.Placements.Count), placement);
            },
            affectedFrameId: frameId);

        if (_selectedPlacementId == placement.Id) _selectedPlacementId = null;
        AfterPlacementChange(frameId);
        AiStatus = $"{symbol.Name} is now ordinary strokes on this drawing.";
        return true;
    }

    /// <summary>The symbol's strokes with the placement's transform written into them.</summary>
    private static List<Stroke> BakedStrokes(Symbol symbol, int frameIndex, SymbolPlacement placement)
    {
        var source = symbol.Frames[frameIndex] switch
        {
            PaintedFrame p => p.Strokes,
            VectorFrame v => v.Strokes,
            _ => [],
        };
        var cos = Math.Cos(placement.Angle * Math.PI / 180);
        var sin = Math.Sin(placement.Angle * Math.PI / 180);
        var scale = Math.Max(Math.Abs(placement.ScaleX), Math.Abs(placement.ScaleY));

        var baked = new List<Stroke>(source.Count);
        foreach (var stroke in source)
        {
            var copy = stroke.Clone();
            for (var i = 0; i < copy.Points.Count; i++)
            {
                var p = copy.Points[i];
                var sx = (p.X - symbol.PivotX) * placement.ScaleX;
                var sy = (p.Y - symbol.PivotY) * placement.ScaleY;
                copy.Points[i] = p with
                {
                    X = placement.X + sx * cos - sy * sin,
                    Y = placement.Y + sx * sin + sy * cos,
                };
            }
            // The brush has to grow with the drawing or a scaled-up sword comes
            // out drawn in a scaled-down line.
            if (scale is > 0 and not 1) copy.Brush.Size *= scale;
            baked.Add(copy);
        }
        return baked;
    }

    // ---- shared plumbing -----------------------------------------------------------

    private static PaintedFrame? FrameIn(Doc doc, string frameId) =>
        doc.Scene.Layers
            .SelectMany(l => l.Cels)
            .Select(c => c.Frame)
            .OfType<PaintedFrame>()
            .FirstOrDefault(f => f.Id == frameId);

    private void AfterPlacementChange(string frameId)
    {
        _cache.Invalidate(frameId);
        _dirtyThumbIds.Add(frameId);
        PublishSnapshot();
        RefreshThumbnails();
        OnPropertyChanged(nameof(PlacementsHere));
        OnPropertyChanged(nameof(SelectedPlacement));
        OnPropertyChanged(nameof(OutdatedPlacements));
        OnPropertyChanged(nameof(StalePlacementReport));
    }
}
