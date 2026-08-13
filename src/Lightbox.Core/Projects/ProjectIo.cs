using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Projects;

/// <summary>
/// Reading and writing a <c>.lbproj</c> folder.
///
/// A folder of plain JSON rather than an archive, deliberately. The document
/// format's stated contract is that everything in it is plain JSON so an agent
/// can read and write any part of it; an archive breaks that for every script
/// that would otherwise just open a file. It also gives incremental saves —
/// touching one animation rewrites one file, not a forty-animation project —
/// and it diffs in git.
///
/// <code>
/// Knight.lbproj/
///   project.json                  the manifest: an index, not the artwork
///   characters/knight/
///     character.json              name, palette ref, pivot, animation index
///     animations/walk.lightbox.json     a Doc, in today's exact format
///     references/front.png
///   palettes/knight.gpl
///   gradients/gradients.json
/// </code>
///
/// Animations keep the <c>.lightbox.json</c> extension and today's schema.
/// That is the migration path: an old loose file <em>is</em> an animation.
/// </summary>
public static class ProjectIo
{
    public const string Extension = ".lbproj";

    private const string ManifestName = "project.json";

    /// <summary>
    /// Where a document that belongs to the project and to no folder is filed.
    /// </summary>
    /// <remarks>
    /// <b>B105.</b> Was <c>documents</c>, which is the least informative name
    /// available in a folder full of documents — every drawing in the project is
    /// one, so the directory read as "the documents" rather than as "the ones
    /// you have not filed". The new name says which it is, and it says it in the
    /// file manager, which is where the artist met it.
    /// <para>
    /// Public because the docker has to recognise a path as unassigned in order
    /// to rename inside it, and a second copy of the string is a second thing to
    /// keep in step.
    /// </para>
    /// </remarks>
    public const string DocumentsDir = "unassigned-documents";

    /// <summary>
    /// What <see cref="DocumentsDir"/> was called before B105.
    /// </summary>
    /// <remarks>
    /// Kept, and not only for the name: every path in a <c>.lbproj</c> written
    /// before the rename is recorded in its manifest, so an existing project
    /// goes on reading and writing <c>documents/</c> and nothing moves under an
    /// artist who has not asked for anything. The one thing that had to be
    /// taught both names is <see cref="SystemFolders"/> — B83 reports a
    /// top-level directory the manifest cannot explain, and an old project's
    /// <c>documents/</c> is explained.
    /// </remarks>
    public const string LegacyDocumentsDir = "documents";
    private const string PalettesFile = "palettes/palettes.json";

    /// <summary>
    /// The palette folders, in their own file rather than alongside the
    /// palettes. A project that never makes a folder then writes nothing new,
    /// and reads byte-identically to one written before folders existed.
    /// </summary>
    private const string PaletteFoldersFile = "palettes/folders.json";
    private const string GradientsFile = "gradients/gradients.json";

    /// <summary>
    /// Shared symbols, in the assets directory the layout already declared.
    /// </summary>
    /// <remarks>
    /// One file rather than one per symbol. A symbol is a handful of strokes,
    /// and a project with two hundred props should not be two hundred file
    /// opens on load — the same argument that made the palettes one file.
    /// </remarks>
    private const string SymbolsFile = "assets/symbols.json";

    // ---- create -------------------------------------------------------------

    /// <summary>A new, empty project in memory. Nothing is written until Save.</summary>
    public static Project Create(string name, string root, ProjectType? type = null) =>
        new(new ProjectManifest { Name = name, Type = type }, root);

    // ---- subjects and running order -------------------------------------------
    //
    // There is no AddCharacter and no AddScene. B114 and
    // `docs/DESIGN-project-scoping.md`: a character is a folder that holds a
    // character's work, and a scene is a folder with a running order. Both are
    // made with `ProjectFolders.Add`, because a second creation verb is a
    // second container, and a second container is the bug.
    //
    // A folder becomes a subject when it gets a reading — which is also the
    // honest answer to "how do you make a character before you have read one":
    // you do not. You make a folder, put the work in it, and read it when there
    // is something to read.

    /// <summary>
    /// How long everything in a folder runs: total frames, and seconds when
    /// every document's length is known.
    /// </summary>
    /// <remarks>
    /// Was <c>SceneDuration</c>. Null seconds rather than a low number when a
    /// document has no hint yet — a running time that silently omits what it
    /// could not measure is worse than none, because it is the number somebody
    /// schedules against.
    /// </remarks>
    public static (int Frames, double? Seconds) FolderDuration(
        ProjectManifest manifest, ProjectFolder folder)
    {
        var documents = ProjectFolders.InOrder(manifest, folder);
        if (documents.Count == 0) return (0, 0);

        var frames = 0;
        double seconds = 0;
        var known = true;
        foreach (var document in documents)
        {
            frames += document.Frames;
            if (document.Seconds is { } s) seconds += s;
            else known = false;
        }
        return (frames, known ? seconds : null);
    }

    /// <summary>
    /// Add a variant of the subject a folder describes, with its own copy of
    /// the palette.
    /// </summary>
    /// <remarks>
    /// The copy <b>keeps every swatch id</b>. That is the entire trick: the art
    /// references swatches by id, so a palette carrying the same ids with
    /// different colours repaints the same drawings without a second copy of
    /// them existing. Fresh ids would make the variant paint nothing.
    /// </remarks>
    public static SubjectVariant AddVariant(
        Project project, ProjectFolder folder, string name, string? basePaletteId = null)
    {
        var variant = new SubjectVariant { Name = name };
        // Null means "whatever this subject paints with", which is the ordinary
        // gesture — an artist adding Winter Armour is varying the palette the
        // knight already has, and naming it again would be a second way to say
        // the same thing.
        basePaletteId ??= ResourceScopes.NearestAt(project.Manifest, folder, PaletteScopes.Kind)?.Id;
        if (project.Palettes.FirstOrDefault(p => p.Id == basePaletteId) is { } basePalette)
        {
            var copy = new Palette
            {
                Name = $"{basePalette.Name} — {name}",
                Columns = basePalette.Columns,
                Swatches = basePalette.Swatches
                    .Select(w => new Swatch { Id = w.Id, Color = w.Color, Name = w.Name })
                    .ToList(),
            };
            project.Palettes.Add(copy);
            variant.PaletteId = copy.Id;
        }
        (folder.Variants ??= []).Add(variant);
        return variant;
    }

    private static bool Reorder<T>(List<T>? items, int from, int to)
    {
        if (items is null) return false;
        if (from < 0 || from >= items.Count || to < 0 || to >= items.Count || from == to) return false;
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
        return true;
    }

    /// <summary>
    /// Register a document in the project, optionally filed in a folder, and
    /// put it in the loaded cache so the caller can open it without a round
    /// trip through the disk.
    /// </summary>
    /// <remarks>
    /// <b>The only way to add a document</b>, since B114. There used to be
    /// three — this, <c>AddAnimation</c> and <c>AddShot</c> — each appending to
    /// a different list, and only this one's list was read by export planning
    /// and scoped resources.
    ///
    /// <b>The path comes from <see cref="ProjectFolders.PathFor"/></b>, which is
    /// the same function <see cref="ProjectFolders.FileDocument"/> uses when a
    /// document is moved. One rule for where bytes go, rather than a creation
    /// path and a move path that agree until somebody changes one.
    ///
    /// After that, <c>Path</c> and <c>FolderId</c> stay separate: renaming a
    /// folder moves nothing on disk, because deriving the path on every read
    /// would rename files underneath an artist who only renamed a row.
    /// </remarks>
    public static DocumentRef AddDocument(
        Project project, string name, Doc doc, ProjectFolder? folder = null)
    {
        var reference = new DocumentRef
        {
            Name = name,
            FolderId = folder?.Id,
            Frames = doc.Scene.FrameCount,
            Fps = doc.Scene.Fps,
        };
        reference.Path = ProjectFolders.PathFor(project.Manifest, reference, folder);
        project.Manifest.Documents.Add(reference);
        project.Loaded[reference.Id] = doc;
        return reference;
    }



    /// <summary>
    /// Give a variant its own version of one animation — the escape hatch for
    /// a difference colour cannot express. Everything else stays inherited.
    /// </summary>
    public static DocumentRef OverrideDocument(
        Project project, ProjectFolder folder, SubjectVariant variant, DocumentRef inherited, Doc doc)
    {
        var reference = AddDocument(project, $"{inherited.Name} ({variant.Name})", doc, folder);
        variant.Overrides[inherited.Id] = reference.Id;
        return reference;
    }

    // ---- load ---------------------------------------------------------------

    /// <summary>
    /// Read the manifest and the shared palettes — <b>not</b> the documents.
    /// Those come through
    /// <see cref="LoadDocument"/> when something actually needs them.
    /// </summary>
    public static Project Load(string root)
    {
        var manifestPath = Path.Combine(root, ManifestName);
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("Not a Lightbox project.", manifestPath);

        var manifest = JsonSerializer.Deserialize<ProjectManifest>(
            File.ReadAllText(manifestPath), DocJson.Options)
            ?? throw new JsonException("project.json deserialized to null.");

        // A version-1 manifest is refused with a sentence rather than crashed
        // on. Q36 decided against a migration — alpha, one user, nothing
        // produced — and the honest cost of that is a project that will not
        // open, so it has to be said rather than discovered.
        if (manifest.Version < ProjectManifest.CurrentVersion)
        {
            throw new NotSupportedException(
                "This project was made with an earlier alpha and cannot be opened. "
                + "Its drawings are intact — the .lightbox.json files inside it can be "
                + "opened individually.");
        }

        var project = new Project(manifest, root);
        LoadResources(project);
        return project;
    }

    private static void LoadResources(Project project)
    {
        var palettes = Path.Combine(project.Root, PalettesFile.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(palettes))
        {
            var stored = JsonSerializer.Deserialize<List<Palette>>(File.ReadAllText(palettes), DocJson.Options);
            project.Palettes.AddRange(stored ?? []);
        }

        var folders = Path.Combine(project.Root, PaletteFoldersFile.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(folders))
        {
            var stored = JsonSerializer.Deserialize<List<PaletteFolder>>(
                File.ReadAllText(folders), DocJson.Options);
            project.PaletteFolders.AddRange(stored ?? []);
        }

        // A palette filed under a folder that is no longer in the file has to
        // appear somewhere, or it colours the art from a place nobody can find.
        PaletteTree.Prune(project.PaletteFolders, project.Palettes);

        var gradients = Path.Combine(project.Root, GradientsFile.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(gradients))
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, Gradient>>(
                File.ReadAllText(gradients), DocJson.Options);
            foreach (var (id, gradient) in read ?? []) project.Gradients[id] = gradient;
        }

        var symbols = Path.Combine(project.Root, SymbolsFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(symbols)) return;
        var loaded = JsonSerializer.Deserialize<Dictionary<string, Symbol>>(
            File.ReadAllText(symbols), DocJson.Options);
        foreach (var (id, symbol) in loaded ?? [])
        {
            // Nesting is refused rather than half-supported. A symbol holding a
            // placement needs a cycle check, a depth limit and a dependency
            // graph that all have to be right before anything renders, and
            // dropping the placement is a smaller lie than rendering an
            // unbounded recursion.
            foreach (var frame in symbol.Frames) frame.Placements = null;
            project.Symbols[id] = symbol;
        }
    }

    /// <summary>Read a document, or return the one already in the cache.</summary>
    public static Doc? LoadDocument(Project project, DocumentRef reference)
    {
        if (project.Loaded.TryGetValue(reference.Id, out var cached)) return cached;
        var path = project.PathOf(reference);
        if (!File.Exists(path)) return null;
        var doc = DocJson.Load(path);
        ApplyFeatureDefaults(doc, project.Manifest.Type);
        // Q25 re-answered: sheets written into documents under the old model
        // are lifted into the project the first time the document is read.
        ProjectSheets.Promote(project, reference, doc);
        project.Loaded[reference.Id] = doc;
        return doc;
    }

    /// <summary>
    /// Resolve missing features in a document by applying project type defaults.
    /// If the document has no Feature overrides, or the project has no type,
    /// nothing changes (features remain absent/default).
    /// </summary>
    private static void ApplyFeatureDefaults(Doc doc, ProjectType? projectType)
    {
        if (projectType is null) return;

        var defaults = new FeatureDefaults();
        var features = Enum.GetValues<FeatureKey>();

        // Build the effective features: explicit overrides + project defaults
        var effective = new Dictionary<string, bool>();
        foreach (var feature in features)
        {
            var overrideValue = false;
            var hasOverride = doc.Features?.TryGetValue(feature.ToString(), out overrideValue) == true;
            if (hasOverride)
            {
                effective[feature.ToString()] = overrideValue;
            }
            else
            {
                var defaultValue = defaults.GetDefault(projectType.Value, feature);
                // Only store if it's true; false is the implicit default
                if (defaultValue)
                {
                    effective[feature.ToString()] = true;
                }
            }
        }

        // Replace Features with the merged result if anything was true
        if (effective.Count > 0)
        {
            doc.Features = effective;
        }
        else
        {
            doc.Features = null;
        }
    }

    // ---- save ---------------------------------------------------------------

    /// <summary>
    /// Write the manifest, the character files and the shared resources — plus
    /// <b>only</b> the documents named in <paramref name="dirty"/>.
    ///
    /// That selectivity is the whole reason for the folder. A project with
    /// forty animations must not rewrite forty files because one stroke landed
    /// in one of them. Pass null to write every loaded document, which is what
    /// a Save As wants.
    /// </summary>
    public static void Save(Project project, IReadOnlySet<string>? dirty = null)
    {
        Directory.CreateDirectory(project.Root);

        // B64/B86/B87. A folder the artist made exists on disk even when it
        // holds nothing yet. Until this, a directory only appeared when a
        // document was written into it — so an empty folder was real in the
        // panel and absent in a file manager, renaming one moved nothing, and
        // deleting one deleted nothing while reporting success. Three tests
        // passed vacuously on the strength of it.
        foreach (var folder in ProjectFolders.All(project.Manifest))
        {
            if (ResolveInProject(project, ProjectFolders.PathOf(project.Manifest, folder)) is { } path)
            {
                Directory.CreateDirectory(path);
            }
        }

        // B188: the disk mirrors the tree. Files first — any filed document or
        // sheet recorded outside its folder's directory is brought home, before
        // the manifest below writes the corrected paths.
        ReconcileFiledPaths(project);

        // Resources first: writing them is what fills in Manifest.Palettes, so
        // the manifest has to be written after, not twice.
        SaveResources(project);

        // Sheets before the manifest for the same reason: Save refreshes each
        // entry's Name from its loaded content, and a manifest written first
        // would carry yesterday's names.
        ProjectSheets.Save(project);

        // Duration hints next, and for the same reason: they live on the
        // manifest, so refreshing them inside the document loop below would
        // update them after the manifest had already been written and the
        // scene list would report yesterday's lengths for ever.
        foreach (var reference in DocumentsToWrite(project, dirty))
        {
            reference.Frames = project.Loaded[reference.Id].Scene.FrameCount;
            reference.Fps = project.Loaded[reference.Id].Scene.Fps;
            // The template hint, beside the duration hints and refreshed at
            // the same moment — it is what lets a row wear the 📄 without
            // loading the file. True or null, never false, so an ordinary
            // document writes no key.
            reference.IsTemplate =
                project.Loaded[reference.Id].IsTemplateDocument ? true : null;
            // New work enters the pipeline as Draft the moment it first lands
            // on disk. First write only — a file already on disk with no
            // status is a document somebody imported or predates statuses, and
            // backfilling it would invent a pipeline position nobody chose.
            // "Nobody has said" stays sayable: clearing the status afterwards
            // is one gesture and it stays cleared.
            //
            // Never a template: a template is reference machinery rather than
            // a deliverable, the same reasoning that keeps sheets out of
            // statuses — a Draft that can never become Ready would sit on the
            // status board for ever. (The hint above is set first, on purpose.)
            //
            // In this loop rather than the write loop below, for the reason
            // Version gives: the manifest is serialized between the two, and a
            // status set after that never reaches the file.
            if (reference.Status is null && reference.IsTemplate != true
                && !File.Exists(project.PathOf(reference)))
            {
                reference.Status = AssetStatus.Draft;
            }
            // The version moves when the file does, so anything built from this
            // document — an exported sheet — can tell it has moved on. On save
            // rather than on edit, because an edit nobody saved has not changed
            // what the sheet was built from; Symbol.Version bumps on save for
            // exactly the same reason.
            //
            // In *this* loop rather than the one that writes the documents,
            // because that one runs after the manifest is serialized and the
            // bump would never reach the file — the same trap the comment above
            // records for Frames and Fps.
            reference.Version++;
        }

        DocJson.WriteAtomic(
            Path.Combine(project.Root, ManifestName),
            JsonSerializer.Serialize(project.Manifest, DocJson.Options));

        // There is no second index file any more. `character.json` existed so a
        // save that touched one character need not rewrite the others; with one
        // folder tree in one manifest there is nothing to split, and the
        // side-file was the mechanism B114's two containers were built on.

        foreach (var reference in DocumentsToWrite(project, dirty))
        {
            DocJson.Save(project.Loaded[reference.Id], project.PathOf(reference));
        }

        // B188 again, the other half: with every file home, directories no
        // layout explains — and nothing lives in — go. After the writes, so a
        // directory about to receive its first document is not judged empty.
        RemoveEmptiedDirectories(project);
    }

    /// <summary>
    /// The documents this save will actually write: loaded, and dirty when a
    /// dirty set was given.
    /// </summary>
    /// <remarks>
    /// Shared by the hint pass and the write pass so the two cannot disagree
    /// about which files are being produced — a hint recorded for a file that
    /// was not written is a length that never existed.
    /// </remarks>
    private static List<DocumentRef> DocumentsToWrite(Project project, IReadOnlySet<string>? dirty) =>
        project.AllDocuments
            .Where(r => (dirty is null || dirty.Contains(r.Id)) && project.Loaded.ContainsKey(r.Id))
            .ToList();

    /// <summary>
    /// Palettes and gradients, as JSON.
    /// </summary>
    /// <remarks>
    /// JSON rather than <c>.gpl</c>, and the distinction is load-bearing. A
    /// GIMP palette carries names and RGB; it <b>cannot carry ids</b>. Storing
    /// shared palettes that way meant every <c>Stroke.SwatchId</c> and every
    /// <c>Character.PaletteId</c> pointed at an id that no longer existed after
    /// a reload, so the whole live-palette feature quietly stopped working the
    /// first time a project was reopened.
    ///
    /// <c>.gpl</c> is still what the palette docker imports and exports. It is
    /// an interchange format, which is a different job from being the store.
    /// </remarks>
    private static void SaveResources(Project project)
    {
        project.Manifest.Palettes.Clear();
        if (project.Palettes.Count > 0)
        {
            project.Manifest.Palettes.Add(PalettesFile);
            DocJson.WriteAtomic(
                Path.Combine(project.Root, PalettesFile.Replace('/', Path.DirectorySeparatorChar)),
                JsonSerializer.Serialize(project.Palettes, DocJson.Options));
        }

        var folderPath = Path.Combine(
            project.Root, PaletteFoldersFile.Replace('/', Path.DirectorySeparatorChar));
        if (project.PaletteFolders.Count > 0)
        {
            DocJson.WriteAtomic(
                folderPath, JsonSerializer.Serialize(project.PaletteFolders, DocJson.Options));
        }
        else if (File.Exists(folderPath))
        {
            // Deleting the last folder has to reach the disk too, or reopening
            // the project brings the filing system back.
            File.Delete(folderPath);
        }

        if (project.Gradients.Count > 0)
        {
            DocJson.WriteAtomic(
                Path.Combine(project.Root, GradientsFile.Replace('/', Path.DirectorySeparatorChar)),
                JsonSerializer.Serialize(project.Gradients, DocJson.Options));
        }

        var symbolPath = Path.Combine(
            project.Root, SymbolsFile.Replace('/', Path.DirectorySeparatorChar));
        if (project.Symbols.Count > 0)
        {
            DocJson.WriteAtomic(
                symbolPath, JsonSerializer.Serialize(project.Symbols, DocJson.Options));
        }
        else if (File.Exists(symbolPath))
        {
            // Deleting the last symbol has to reach the disk, for the reason the
            // last palette folder does: otherwise reopening brings it back.
            File.Delete(symbolPath);
        }
    }

    // ---- conversion -----------------------------------------------------------

    /// <summary>
    /// What changed, and what the artist should know about it.
    /// </summary>
    /// <param name="Notes">
    /// Plain sentences, not warnings. Nothing here is a problem — conversion
    /// cannot break anything — but the tooling around the work changes, and
    /// finding that out by noticing an export looks different is worse than
    /// being told.
    /// </param>
    public sealed record ConversionReport(ProjectType? From, ProjectType? To, IReadOnlyList<string> Notes);

    /// <summary>
    /// Change what a project is for, without touching a single drawing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The type is a statement about intent that tooling and export read; it is
    /// not a format. So conversion is exactly a change of that statement, and
    /// the guarantee worth making — the one the tests hold — is the negative
    /// one: <b>no document is read, rewritten or recreated</b>. An illustration
    /// that becomes an animation is the same file, byte for byte.
    /// </para>
    /// <para>
    /// Nothing authored is dropped either, even when the new type has no use
    /// for it. A camera keyframed under Animation survives conversion to Game
    /// art, because a conversion that quietly deleted the shot work would make
    /// the operation one nobody could risk. What the new type ignores, it
    /// ignores; it does not erase.
    /// </para>
    /// <para>
    /// The workspace is deliberately <i>not</i> switched here. Which panels
    /// somebody wants is a preference, converting a project is a decision about
    /// the project, and rearranging the screen as a side effect of a menu item
    /// is how a tool loses trust. The caller offers it.
    /// </para>
    /// </remarks>
    public static ConversionReport Convert(Project project, ProjectType? to)
    {
        var from = project.Manifest.Type;
        project.Manifest.Type = to;
        return new ConversionReport(from, to, ConversionNotes(project, from, to));
    }

    private static List<string> ConversionNotes(Project project, ProjectType? from, ProjectType? to)
    {
        var notes = new List<string>();
        if (from == to)
        {
            notes.Add("Already that type — nothing changed.");
            return notes;
        }

        var documents = project.AllDocuments.ToList();
        notes.Add(documents.Count == 1
            ? "1 document kept as it is. Conversion never rewrites artwork."
            : $"{documents.Count} documents kept as they are. Conversion never rewrites artwork.");

        switch (to)
        {
            case ProjectType.Animation or ProjectType.Storyboard:
                notes.Add("The timeline and the camera apply now; scenes can be added to plan shots.");
                break;
            case ProjectType.GameArt:
                // The pivot is what asset export registers frames on, so its
                // absence is the one thing genuinely worth mentioning.
                // Q40: folders that hold work, not "subjects" — a sprite sheet
                // is packed from whatever a folder contains, and calling that a
                // character is a designation the project does not have.
                var holding = ProjectFolders.All(project.Manifest)
                    .Where(f => ProjectFolders.DocumentsIn(project.Manifest, f).Count > 0)
                    .ToList();
                var without = holding.Count(f => f.Pivot is null);
                notes.Add(holding.Count == 0 || without == 0
                    ? "Export packs sprite sheets, registered on each folder's pivot."
                    : $"Export packs sprite sheets. {without} folder(s) have no pivot yet, "
                        + "so their frames register on the canvas instead.");
                break;
            case ProjectType.Illustration or ProjectType.Comic:
                notes.Add("Playback and camera tooling stop being offered. Nothing already authored is removed.");
                break;
            case ProjectType.AssetLibrary:
                notes.Add("Other projects can import these folders, bringing their documents and palette.");
                break;
            case null:
                notes.Add("No declared type. Every panel stays available and the type key leaves the file.");
                break;
        }

        if (from is ProjectType.Animation or ProjectType.Storyboard
            && to is ProjectType.GameArt or ProjectType.Illustration or ProjectType.AssetLibrary)
        {
            notes.Add("Cameras and scenes are kept but no longer used by export — "
                + "convert back and they are where you left them.");
        }
        return notes;
    }

    // ---- migration ----------------------------------------------------------

    /// <summary>
    /// Wrap a loose <c>.lightbox.json</c> in a one-document project,
    /// <b>in memory</b>. Nothing is written until the artist chooses
    /// "Save as project…", so opening an old file keeps working exactly as it
    /// did and the container is offered rather than imposed.
    /// </summary>
    /// <remarks>
    /// <b>No folder is invented.</b> It used to make a character named after the
    /// file and file the drawing under it, which is B83/B84 — a layout the artist
    /// never asked for, leaking out of a special-cased container. One document at
    /// the root is what was opened, so it is what the project holds.
    /// </remarks>
    public static Project Migrate(Doc doc, string documentPath)
    {
        var name = Path.GetFileNameWithoutExtension(documentPath);
        if (name.EndsWith(".lightbox", StringComparison.OrdinalIgnoreCase)) name = name[..^9];

        var project = Create(name, Path.ChangeExtension(documentPath, null) + Extension);
        AddDocument(project, name, doc);

        // The document's own palettes and gradients become the project's: the
        // point of migrating is that they are now shared, not that a copy sits
        // in one animation.
        project.Palettes.AddRange(doc.Palettes);
        foreach (var (id, gradient) in doc.Gradients) project.Gradients[id] = gradient;
        return project;
    }

    // ---- flatten ------------------------------------------------------------

    /// <summary>
    /// A standalone copy of a document, with every project resource it
    /// references inlined.
    /// </summary>
    /// <remarks>
    /// This is what keeps invariant 1 honest. Inside a project the strokes
    /// reference shared swatches, gradients and brush tips by id, so the
    /// <em>project</em> is the unit that re-renders. The moment a document
    /// leaves — exported, emailed, handed to another tool — it has to carry
    /// what it needs, or it renders as something else.
    ///
    /// Deep-cloned through JSON first, so flattening never mutates the document
    /// the artist still has open.
    /// </remarks>
    public static Doc Flatten(Doc doc, Project project)
    {
        var copy = DocJson.Clone(doc);
        var frames = copy.Scene.Layers
            .SelectMany(l => l.Cels)
            .Select(c => c.Frame)
            .Concat(copy.ReferenceSheets.SelectMany(s => s.Views).SelectMany(v => v.Layers).SelectMany(l => l.Cels).Select(c => c.Frame))
            .OfType<Frame>()
            .ToList();

        var symbols = InlineSymbols(copy, frames, project);
        var strokes = frames
            .SelectMany(StrokesOf)
            // A symbol's own strokes reference shared swatches and gradients
            // like any others, so they have to join the walk below. Leaving
            // them out gave an exported sword the literal colours its strokes
            // were carrying rather than the ones it was painted in.
            .Concat(symbols.SelectMany(s => s.Frames).SelectMany(StrokesOf))
            .ToList();

        var swatches = strokes.Select(s => s.SwatchId).OfType<string>().ToHashSet();
        if (swatches.Count > 0)
        {
            var inlined = project.Palettes
                .SelectMany(p => p.Swatches)
                .Where(s => swatches.Contains(s.Id))
                .ToList();
            // One palette holding exactly what this document uses, rather than
            // every palette in the project: an exported walk cycle should not
            // arrive carrying the colours of every other character.
            if (inlined.Count > 0)
            {
                copy.Palettes.Add(new Palette { Name = "Inlined", Swatches = inlined });
            }
        }

        foreach (var id in strokes.Select(s => s.GradientId).OfType<string>().Distinct())
        {
            if (!copy.Gradients.ContainsKey(id) && project.Gradients.TryGetValue(id, out var gradient))
            {
                copy.Gradients[id] = gradient;
            }
        }
        return copy;
    }

    /// <summary>
    /// Copy the symbols an exported document places into the document itself,
    /// and return them so their own strokes join the resource walk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design originally said "inline placements as ordinary strokes", and
    /// that is the one part of it that could not survive contact with
    /// invariant 2. Baking a placement's transform into stroke coordinates
    /// means multiplying them, and every dab dynamic is seeded by
    /// <c>Hash01</c> from the bits of a dab position — so the flattened sword
    /// would come out with different scatter, size and colour jitter from the
    /// one the artist approved. The export would be a different drawing, which
    /// is precisely the failure the pixel-identity test exists to catch.
    /// </para>
    /// <para>
    /// So the symbols travel instead of being dissolved. The exported document
    /// carries what it references and renders through exactly the same pass,
    /// which makes it self-contained — invariant 1 satisfied where it must be —
    /// without the export being a different mark from the original.
    /// </para>
    /// </remarks>
    private static List<Symbol> InlineSymbols(Doc copy, List<Frame> frames, Project project)
    {
        var placed = frames
            .Where(f => f.HasPlacements)
            .SelectMany(f => f.Placements!)
            .Select(p => p.SymbolId)
            .Where(id => id.Length > 0)
            .ToHashSet();
        if (placed.Count == 0) return [];

        var inlined = new List<Symbol>();
        foreach (var id in placed)
        {
            // Already the document's own: a flatten of a flatten must not
            // replace what travelled with the file the first time.
            if (copy.Symbols?.ContainsKey(id) == true)
            {
                inlined.Add(copy.Symbols[id]);
                continue;
            }
            if (!project.Symbols.TryGetValue(id, out var symbol)) continue;
            copy.Symbols ??= [];
            copy.Symbols[id] = symbol;
            inlined.Add(symbol);
        }
        return inlined;
    }

    private static IEnumerable<Stroke> StrokesOf(Frame frame) => frame.Strokes;

    // ---- naming -------------------------------------------------------------

    /// <summary>
    /// A filesystem-safe folder name. Never empty, so a path is never
    /// malformed — a character called "///" would otherwise write itself into
    /// the project root and take the manifest with it.
    ///
    /// Public because it is part of the on-disk contract: it is how a caller
    /// predicts where a character's files will land.
    /// </summary>
    public static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length == 0 ? "untitled" : slug;
    }

    private static string Unique(string wanted, HashSet<string> taken)
    {
        if (!taken.Contains(wanted)) return wanted;
        for (var n = 2; ; n++)
        {
            var candidate = $"{wanted}-{n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    // ---- deleting from disk (B87) --------------------------------------------

    /// <summary>
    /// Delete something inside the project, named by its project-relative path.
    /// True when it was there and is not now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The containment check is the point of this method existing.</b> Every
    /// path here comes from the manifest, and a manifest is plain JSON that a
    /// person or an agent can edit — so a <c>path</c> of
    /// <c>../../../Documents</c> is not a hypothetical, it is one careless
    /// entry. The full path is resolved and compared against the resolved root
    /// before anything is removed, and a path that escapes deletes nothing.
    /// </para>
    /// <para>
    /// Everything else about it is deliberately dull: a missing file is a
    /// success, because the artist asked for it to be gone and it is, and an
    /// <c>IOException</c> is a false rather than a crash — a file open in
    /// another application is a thing to report, not to die on.
    /// </para>
    /// </remarks>
    public static bool DeleteInProject(Project project, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        if (string.IsNullOrEmpty(project.Root)) return false;

        if (ResolveInProject(project, relativePath) is not { } full) return false;

        try
        {
            if (File.Exists(full)) File.Delete(full);
            else if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// A project-relative path as a full path, or null when it does not stay
    /// inside the project.
    /// </summary>
    /// <remarks>
    /// <b>B87, then B64.</b> Extracted the moment a second caller needed it,
    /// because a containment check that exists in one place and is re-typed in
    /// another is a containment check that will differ. Every path handed to
    /// these comes from the manifest — plain JSON a person or an agent can edit
    /// — so an entry of <c>../../../Documents</c> is one slip away from an
    /// operation that deletes or overwrites a tree.
    ///
    /// The separator is part of the comparison on purpose:
    /// <c>Knight.lbproj-old</c> starts with <c>Knight.lbproj</c>, so a plain
    /// prefix test would call a sibling project "inside".
    /// </remarks>
    public static string? ResolveInProject(Project project, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        if (string.IsNullOrEmpty(project.Root)) return null;

        var root = Path.GetFullPath(project.Root);
        var full = Path.GetFullPath(Path.Combine(
            root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var inside = full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return inside && full != root ? full : null;
    }

    /// <summary>
    /// A full path as a project-relative one with forward slashes, or null when
    /// it is not inside the project.
    /// </summary>
    /// <remarks>
    /// <b>The counterpart to <see cref="ResolveInProject"/>, and it exists for
    /// one question: did this save land inside the project or outside it?</b>
    /// Save As can put a document anywhere, and the two answers are opposite —
    /// inside, the project's record of it follows the file; outside, the artist
    /// has chosen a home elsewhere and the project should let go of it.
    /// <para>
    /// The same separator-aware containment test as its counterpart, for the same
    /// reason: <c>Knight.lbproj-old</c> starts with <c>Knight.lbproj</c>, so a
    /// plain prefix test would call a sibling project "inside".
    /// </para>
    /// </remarks>
    public static string? RelativeInProject(Project project, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return null;
        if (string.IsNullOrEmpty(project.Root)) return null;

        var root = Path.GetFullPath(project.Root);
        var full = Path.GetFullPath(fullPath);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;

        return full[(root.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Move a file or folder inside the project. True when it moved, or when
    /// there was nothing there to move.
    /// </summary>
    /// <remarks>
    /// <b>B64.</b> A rename that only changes the manifest leaves a file called
    /// the old thing on disk, which is half a rename and the more confusing
    /// half — the panel and the folder disagree, and the artist believes the
    /// panel until they open a file manager.
    ///
    /// **Nothing to move is a success.** A document created and renamed before
    /// its first save has no file yet, and refusing that would make the rename
    /// fail for the most ordinary case there is.
    ///
    /// <b>A project with no root on disk is the same case, one level up</b>
    /// (B106). <c>ProjectIo.Create</c> builds a project in memory and nothing is
    /// written until <see cref="Save"/>, so every path in it is a path that does
    /// not exist yet. Refusing there would make moving a document fail for a
    /// project that has never been saved — the state every new project is in for
    /// its first few minutes. A path that leaves the project is still refused;
    /// that is a different question and <see cref="ResolveInProject"/> answers it.
    /// </remarks>
    public static bool MoveInProject(Project project, string fromRelative, string toRelative)
    {
        if (string.IsNullOrEmpty(project.Root)) return true;
        if (ResolveInProject(project, fromRelative) is not { } from) return false;
        if (ResolveInProject(project, toRelative) is not { } to) return false;
        if (string.Equals(from, to, StringComparison.Ordinal)) return true;

        var isFile = File.Exists(from);
        var isDirectory = Directory.Exists(from);
        if (!isFile && !isDirectory) return true;
        // Never silently write over somebody's work.
        if (File.Exists(to) || Directory.Exists(to)) return false;

        try
        {
            if (Path.GetDirectoryName(to) is { Length: > 0 } parent) Directory.CreateDirectory(parent);
            if (isFile) File.Move(from, to);
            else Directory.Move(from, to);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---- refiling and renaming, disk-first --------------------------------------
    //
    // These were the docker's private orchestration and moved here when the
    // project window grew the same gestures — Q29's rule: two surfaces onto one
    // project must share one implementation of what a drag and a rename do.

    /// <summary>
    /// File a document in a folder, moving its file first. False when the disk
    /// refused, in which case nothing changed anywhere.
    /// </summary>
    /// <remarks>
    /// <b>B106.</b> Where the file has to end up is worked out before anything
    /// moves; <see cref="ProjectFolders.PathFor"/> reads the manifest without
    /// changing it, so the disk can be moved first and the manifest only if it
    /// worked. The alternative orders each leave a drawing in two places or in
    /// none.
    /// </remarks>
    public static bool RefileDocument(Project project, DocumentRef document, ProjectFolder? destination)
    {
        var to = ProjectFolders.PathFor(project.Manifest, document, destination);
        if (!MoveInProject(project, document.Path, to)) return false;
        return ProjectFolders.FileDocument(project.Manifest, document, destination);
    }

    /// <summary>
    /// Move a folder under a new parent — or to the root — carrying its
    /// directory and everything inside it. False when it cannot go, in which
    /// case nothing changed anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B188.</b> <see cref="ProjectFolders.Move"/> alone is the manifest
    /// half, and for a long time it was the whole implementation of the drag:
    /// the panel showed the new tree while every file and directory stayed
    /// where the <em>old</em> tree put it, and the next save materialised the
    /// new directory beside the fossil of the old one. A project rearranged a
    /// few times became an archaeology dig — each layout it had ever had,
    /// still on disk.
    /// </para>
    /// <para>
    /// Disk first, manifest reverted if the disk refuses — the same contract
    /// as <see cref="RenameFolder"/>. The recorded paths of everything below
    /// are rewritten by prefix, not re-derived: <see cref="Directory.Move"/>
    /// carried the files under their existing names, and re-deriving from
    /// display names would part company with any deduped or legacy leaf.
    /// </para>
    /// </remarks>
    public static bool MoveFolder(Project project, ProjectFolder folder, ProjectFolder? destination)
    {
        var manifest = project.Manifest;
        var parentWas = folder.ParentId;
        var was = ProjectFolders.PathOf(manifest, folder);
        if (!ProjectFolders.Move(manifest, folder, destination)) return false;

        var now = ProjectFolders.PathOf(manifest, folder);
        if (!MoveInProject(project, was, now))
        {
            folder.ParentId = parentWas;
            return false;
        }
        RepathUnder(manifest, was, now);
        return true;
    }

    /// <summary>
    /// Rewrite every recorded path under a directory that moved from
    /// <paramref name="was"/> to <paramref name="now"/> — documents and
    /// sheets both.
    /// </summary>
    /// <remarks>
    /// By prefix, deliberately: the files themselves moved with the directory,
    /// names untouched, so the only honest rewrite is the same substitution.
    /// Re-deriving with <c>PathFor</c> here once desynced a legacy-shaped
    /// document and silently skipped sheets — the recorded path then pointed
    /// at a file that did not exist, which reads as lost work.
    /// </remarks>
    private static void RepathUnder(ProjectManifest manifest, string was, string now)
    {
        var prefix = $"{was}/";
        foreach (var document in manifest.Documents)
        {
            if (document.Path.StartsWith(prefix, StringComparison.Ordinal))
            {
                document.Path = $"{now}/{document.Path[prefix.Length..]}";
            }
        }
        foreach (var sheet in manifest.Sheets ?? [])
        {
            if (sheet.Path.StartsWith(prefix, StringComparison.Ordinal))
            {
                sheet.Path = $"{now}/{sheet.Path[prefix.Length..]}";
            }
        }
    }

    /// <summary>What a rename did, so a surface can say which refusal it was.</summary>
    public enum RenameOutcome { Renamed, NameTaken, DiskRefused }

    /// <summary>Rename a folder, on disk as well as in the manifest.</summary>
    /// <remarks>
    /// <b>B64.</b> Refused whole on a disk failure — the manifest is put back,
    /// because a panel that says one thing while the disk says another is worse
    /// than a refused rename: only one of those is visible. Everything filed
    /// below it moved with it, so the recorded paths follow.
    /// </remarks>
    public static RenameOutcome RenameFolder(Project project, ProjectFolder folder, string name)
    {
        var originalName = folder.Name;
        var was = ProjectFolders.PathOf(project.Manifest, folder);
        if (!ProjectFolders.Rename(project.Manifest, folder, name)) return RenameOutcome.NameTaken;

        var now = ProjectFolders.PathOf(project.Manifest, folder);
        if (!MoveInProject(project, was, now))
        {
            ProjectFolders.Rename(project.Manifest, folder, originalName);
            return RenameOutcome.DiskRefused;
        }

        // By prefix, like MoveFolder, and for its reason: the files kept their
        // leaf names when the directory moved, so the paths must too.
        RepathUnder(project.Manifest, was, now);
        return RenameOutcome.Renamed;
    }

    /// <summary>Rename a document, file included. The manifest follows the disk.</summary>
    public static RenameOutcome RenameDocument(Project project, DocumentRef document, string name)
    {
        var was = document.Path;
        var originalName = document.Name;
        document.Name = name;
        var now = document.FolderId is null && !IsUnfiled(was)
            // A document keeps the shape of the path it already has; only the
            // file's own name changes.
            ? RenamedLeaf(was, name)
            : ProjectFolders.PathFor(
                project.Manifest, document, ProjectFolders.ById(project.Manifest, document.FolderId));

        if (!MoveInProject(project, was, now))
        {
            document.Name = originalName;
            return RenameOutcome.DiskRefused;
        }
        document.Path = now;
        return RenameOutcome.Renamed;
    }

    /// <summary>
    /// Whether a path is in the directory that holds documents belonging to no
    /// folder.
    /// </summary>
    /// <remarks>
    /// <b>B105.</b> Both names, because the directory was renamed and a project
    /// written before that keeps its recorded paths.
    /// </remarks>
    private static bool IsUnfiled(string path) =>
        path.StartsWith($"{DocumentsDir}/", StringComparison.Ordinal)
        || path.StartsWith($"{LegacyDocumentsDir}/", StringComparison.Ordinal);

    /// <summary>Swap the file's own name, keeping the folders above it.</summary>
    private static string RenamedLeaf(string path, string name)
    {
        var cut = path.LastIndexOf('/');
        var directory = cut < 0 ? "" : path[..(cut + 1)];
        return $"{directory}{Slug(name)}.lightbox.json";
    }

    // ---- keeping the disk and the tree the same thing (B188) --------------------

    /// <summary>
    /// Move every filed document and sheet whose recorded path is not inside
    /// its folder's directory — the self-healing half of B188.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a project rearranged before moves carried the disk, the recorded
    /// paths are scattered over every layout the tree has ever had. They are
    /// all still <em>readable</em> — the manifest records where each file is —
    /// but the folder on disk stops meaning anything. This pass runs on every
    /// save, so opening such a project and saving it once puts every file
    /// where its folder says.
    /// </para>
    /// <para>
    /// The leaf name travels unchanged, for RepathUnder's reason. Unfiled
    /// documents are left alone entirely: B105 promises legacy shapes
    /// (<c>animations/…</c>) keep working untouched, and this pass touching
    /// them would be a forced migration nobody asked for. A move the disk
    /// refuses — the target exists, a file is open elsewhere — is skipped,
    /// not failed: the recorded path is still true, and the next save tries
    /// again.
    /// </para>
    /// </remarks>
    private static void ReconcileFiledPaths(Project project)
    {
        foreach (var document in project.Manifest.Documents)
        {
            if (ProjectFolders.ById(project.Manifest, document.FolderId) is not { } folder) continue;
            var home = ProjectFolders.PathOf(project.Manifest, folder);
            if (Reconciled(project, document.Path, home) is { } moved) document.Path = moved;
        }
        foreach (var sheet in project.Manifest.Sheets ?? [])
        {
            if (ProjectFolders.ById(project.Manifest, sheet.FolderId) is not { } folder) continue;
            var home = ProjectFolders.PathOf(project.Manifest, folder);
            if (Reconciled(project, sheet.Path, home) is { } moved) sheet.Path = moved;
        }
    }

    /// <summary>The path after moving a stray file home, or null for no change.</summary>
    private static string? Reconciled(Project project, string recorded, string home)
    {
        if (recorded.Length == 0) return null;
        var cut = recorded.LastIndexOf('/');
        var directory = cut < 0 ? "" : recorded[..cut];
        if (directory == home) return null;
        var to = $"{home}/{recorded[(cut + 1)..]}";
        return MoveInProject(project, recorded, to) ? to : null;
    }

    /// <summary>
    /// Delete directories the manifest cannot explain, when they hold nothing —
    /// the fossils a rearrangement used to leave behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty only, checked at the moment of deletion, so nothing an artist made
    /// can be lost here — a stray directory with so much as one file in it is
    /// left for B83's <see cref="UnexplainedFolders"/> to report rather than
    /// for this to judge. Deepest first, so a directory emptied by its
    /// children's removal goes in the same pass.
    /// </para>
    /// <para>
    /// A directory the manifest <em>does</em> explain survives even when empty
    /// — an empty folder is real (B64/B86/B87), and the materialisation step
    /// above just created it on purpose. System directories and anything
    /// hidden are not this method's to touch.
    /// </para>
    /// </remarks>
    private static void RemoveEmptiedDirectories(Project project)
    {
        if (string.IsNullOrEmpty(project.Root) || !Directory.Exists(project.Root)) return;
        var wanted = ProjectFolders.All(project.Manifest)
            .Select(f => ProjectFolders.PathOf(project.Manifest, f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var all = Directory.GetDirectories(project.Root, "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(project.Root, d).Replace('\\', '/'))
            .OrderByDescending(d => d.Count(c => c == '/'))
            .ToList();
        foreach (var relative in all)
        {
            var top = relative.Split('/')[0];
            if (SystemFolders.Contains(top) || top.StartsWith('.')) continue;
            if (wanted.Contains(relative)) continue;
            if (ResolveInProject(project, relative) is not { } absolute) continue;
            try
            {
                if (Directory.Exists(absolute)
                    && !Directory.EnumerateFileSystemEntries(absolute).Any())
                {
                    Directory.Delete(absolute);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A directory that refuses to go is a report for B83, not a
                // failed save.
            }
        }
    }

    // ---- what a project folder may contain (B83) ------------------------------

    /// <summary>
    /// Top-level directory names Lightbox itself owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B83.</b> The report asked that "every top folder created in the
    /// project folder should be included in Project.lbproj", and the useful
    /// reading of that is <em>accountability</em>: nothing appears at the top
    /// of a project that the manifest cannot explain. It is what would have
    /// caught the original defect, which was <c>characters/</c> and
    /// <c>scenes/</c> arriving unasked.
    /// </para>
    /// <para>
    /// <b>Those two names left this set with B114.</b> Nothing creates them any
    /// more — a character is a folder the artist made — so a <c>characters/</c>
    /// directory at the top of a project is now exactly the unexplained thing
    /// this list exists to report, rather than something Lightbox owns.
    /// </para>
    /// <para>
    /// Declared here rather than listed in <c>Folders</c>, which was the other
    /// reading. Putting them in the artist's tree would make <c>palettes</c> a
    /// row that can be renamed, dragged and deleted like any other, and every
    /// operation would need a "system" flag to refuse — a lot of special-casing
    /// to express "Lightbox owns this name".
    /// </para>
    /// <para>
    /// Each is created on demand and none is a default: a new project has
    /// <c>palettes</c> and, if a drawing was adopted, <c>documents</c>. The rest
    /// appear when the artist makes the thing that needs them.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<string> SystemFolders = new HashSet<string>(
        [
            DocumentsDir, LegacyDocumentsDir, ProjectSheets.RootDir,
            "palettes", "gradients", "assets", ".autosave",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Top-level directories the manifest cannot explain. Empty is the promise.
    /// </summary>
    /// <remarks>
    /// A folder is explained when Lightbox owns the name or when the artist made
    /// it and it is in <c>Folders</c>. Anything else is either a bug that
    /// invented a directory or something a person dropped in by hand — and the
    /// first of those is exactly what B83 reported.
    /// </remarks>
    public static IReadOnlyList<string> UnaccountedFolders(Project project)
    {
        if (string.IsNullOrEmpty(project.Root) || !Directory.Exists(project.Root)) return [];

        var mine = ProjectFolders.ChildrenOf(project.Manifest, null)
            .Select(f => Slug(f.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateDirectories(project.Root)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !SystemFolders.Contains(name) && !mine.Contains(name))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
