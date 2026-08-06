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
    private const string CharactersDir = "characters";
    private const string AnimationsDir = "animations";
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

    /// <summary>
    /// Add a character, with a folder slug derived from its name. The slug is
    /// what appears on disk and never changes again — renaming a character
    /// must not move its files out from under a half-written save.
    /// </summary>
    public static Character AddCharacter(Project project, string name)
    {
        var character = new Character { Name = name, Slug = UniqueSlug(project, Slug(name)) };
        project.Manifest.Characters.Add(character);
        return character;
    }

    // ---- scenes ---------------------------------------------------------------

    private const string ScenesDir = "scenes";

    private const string ShotsDir = "shots";

    /// <summary>
    /// Add a scene. The first one brings the scene list into being.
    /// </summary>
    /// <remarks>
    /// The list is null until now, so a project that never plans a film writes
    /// no scene key — the camera's rule, applied to the second axis.
    /// </remarks>
    public static ProjectScene AddScene(Project project, string name)
    {
        var manifest = project.Manifest;
        manifest.Scenes ??= [];
        var taken = manifest.Scenes.Select(s => s.Slug).ToHashSet();
        var wanted = Slug(name);
        var slug = wanted;
        for (var n = 2; taken.Contains(slug); n++) slug = $"{wanted}-{n}";

        var scene = new ProjectScene { Name = name, Slug = slug };
        manifest.Scenes.Add(scene);
        return scene;
    }

    /// <summary>Register a shot in a scene and cache its document.</summary>
    public static DocumentRef AddShot(Project project, ProjectScene scene, string name, Doc doc)
    {
        var taken = scene.Shots
            .Select(s => System.IO.Path.GetFileName(s.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = Slug(name);
        var slug = wanted;
        for (var n = 2; taken.Contains($"{slug}.lightbox.json"); n++) slug = $"{wanted}-{n}";

        var reference = new DocumentRef
        {
            Name = name,
            Path = $"{ScenesDir}/{scene.Slug}/{ShotsDir}/{slug}.lightbox.json",
            Frames = doc.Scene.FrameCount,
            Fps = doc.Scene.Fps,
        };
        scene.Shots.Add(reference);
        project.Loaded[reference.Id] = doc;
        return reference;
    }

    /// <summary>
    /// Delete a scene, keeping its shots.
    /// </summary>
    /// <remarks>
    /// The shots become loose documents rather than going with it, for the same
    /// reason deleting a palette folder keeps the palettes: reorganising a film
    /// must not be the fastest way to delete it. The files are never touched
    /// either way — only the index changes.
    /// </remarks>
    public static void RemoveScene(Project project, ProjectScene scene)
    {
        if (project.Manifest.Scenes is not { } scenes) return;
        if (!scenes.Remove(scene)) return;
        project.Manifest.Documents.AddRange(scene.Shots);
        // Back to absent once the last one goes.
        if (scenes.Count == 0) project.Manifest.Scenes = null;
    }

    /// <summary>Move a scene in the running order. Out-of-range indices do nothing.</summary>
    public static bool MoveScene(Project project, int from, int to) =>
        Reorder(project.Manifest.Scenes, from, to);

    /// <summary>Move a shot within its scene.</summary>
    public static bool MoveShot(ProjectScene scene, int from, int to) =>
        Reorder(scene.Shots, from, to);

    /// <summary>Move a shot from one scene to another, at the end.</summary>
    public static bool MoveShotToScene(ProjectScene from, ProjectScene to, DocumentRef shot)
    {
        if (ReferenceEquals(from, to) || !from.Shots.Remove(shot)) return false;
        to.Shots.Add(shot);
        return true;
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
    /// How long a scene runs: total frames, and seconds when every shot's
    /// length is known.
    /// </summary>
    /// <remarks>
    /// Null seconds rather than a low number when a shot has no hint yet.
    /// A running time that silently omits the shots it could not measure is
    /// worse than no running time, because it is the number somebody schedules
    /// against.
    /// </remarks>
    public static (int Frames, double? Seconds) SceneDuration(ProjectScene scene)
    {
        if (scene.Shots.Count == 0) return (0, 0);
        var frames = 0;
        double seconds = 0;
        var known = true;
        foreach (var shot in scene.Shots)
        {
            frames += shot.Frames;
            if (shot.Seconds is { } s) seconds += s;
            else known = false;
        }
        return (frames, known ? seconds : null);
    }

    /// <summary>
    /// Register a new animation under a character and put its document in the
    /// loaded cache, so the caller can open it immediately without a round trip
    /// through the disk.
    /// </summary>
    public static DocumentRef AddAnimation(Project project, Character character, string name, Doc doc)
    {
        var slug = UniqueFileSlug(character, Slug(name));
        var reference = new DocumentRef
        {
            Name = name,
            Path = $"{CharactersDir}/{character.Slug}/{AnimationsDir}/{slug}.lightbox.json",
        };
        character.Animations.Add(reference);
        project.Loaded[reference.Id] = doc;
        return reference;
    }

    /// <summary>
    /// Register a document that belongs to the project but to no character —
    /// a background, a colour test, a one-off illustration.
    /// </summary>
    public static DocumentRef AddDocument(Project project, string name, Doc doc)
    {
        var taken = project.Manifest.Documents.Select(d => d.Path).ToHashSet();
        var slug = Slug(name);
        var candidate = slug;
        for (var n = 2; taken.Contains($"{DocumentsDir}/{candidate}.lightbox.json"); n++)
        {
            candidate = $"{slug}-{n}";
        }
        var reference = new DocumentRef
        {
            Name = name,
            Path = $"{DocumentsDir}/{candidate}.lightbox.json",
        };
        project.Manifest.Documents.Add(reference);
        project.Loaded[reference.Id] = doc;
        return reference;
    }

    /// <summary>
    /// Move a document to another character, or to the project itself when
    /// <paramref name="destination"/> is null.
    /// </summary>
    /// <remarks>
    /// The <b>id is kept</b> and the path is recomputed. Keeping the id is what
    /// lets an open tab stay bound to the document it is showing, so moving a
    /// row in the tree does not orphan the window you are drawing in.
    ///
    /// <b>B106. The file moves with it.</b> This used to change the manifest and
    /// stop, on the reasoning that a move which deleted an artist's file would
    /// be a poor trade for tidiness — and the reasoning was sound about deleting
    /// and wrong about the outcome. The next save wrote the document at its new
    /// path and left the old file where it was, so one drawing became two: the
    /// artist saw the copy they had moved and the original still sitting in the
    /// folder they moved it out of, with no way to tell which one the app would
    /// open. A move is a rename on disk, which is what
    /// <see cref="MoveInProject"/> does and what renaming a folder has always
    /// done — nothing is deleted and nothing is left behind.
    ///
    /// <b>The disk goes first, and the manifest only if it succeeded.</b> A
    /// manifest pointing at a path the file never reached is the one outcome
    /// worse than either half, because it is invisible: the panel would show the
    /// document in its new home and the drawing would not be there.
    /// </remarks>
    public static bool MoveDocument(Project project, DocumentRef reference, Character? destination)
    {
        var from = project.Manifest.Characters.FirstOrDefault(c => c.Animations.Any(a => a.Id == reference.Id));
        var atProjectLevel = project.Manifest.Documents.Any(d => d.Id == reference.Id);
        if (from is null && !atProjectLevel) return false;
        if (ReferenceEquals(from, destination)) return false;

        // Worked out before anything is touched, so the move can be attempted
        // and refused without leaving the manifest half-changed. Both branches
        // read only state this method has not modified yet.
        var to = destination is null
            ? $"{DocumentsDir}/{Slug(reference.Name)}.lightbox.json"
            : $"{CharactersDir}/{destination.Slug}/{AnimationsDir}/"
              + $"{UniqueFileSlug(destination, Slug(reference.Name))}.lightbox.json";

        if (!MoveInProject(project, reference.Path, to)) return false;

        from?.Animations.RemoveAll(a => a.Id == reference.Id);
        if (atProjectLevel) project.Manifest.Documents.RemoveAll(d => d.Id == reference.Id);

        reference.Path = to;
        if (destination is null) project.Manifest.Documents.Add(reference);
        else destination.Animations.Add(reference);
        return true;
    }

    /// <summary>
    /// Add a variant of a character, with its own copy of the palette.
    /// </summary>
    /// <remarks>
    /// The copy <b>keeps every swatch id</b>. That is the entire trick: the
    /// art references swatches by id, so a palette carrying the same ids with
    /// different colours repaints the same drawings without a second copy of
    /// them existing. Fresh ids would make the variant paint nothing.
    /// </remarks>
    public static CharacterVariant AddVariant(Project project, Character character, string name)
    {
        var variant = new CharacterVariant { Name = name };
        if (project.Palettes.FirstOrDefault(p => p.Id == character.PaletteId) is { } basePalette)
        {
            var copy = new Palette
            {
                Name = $"{basePalette.Name} — {name}",
                Columns = basePalette.Columns,
                Swatches = basePalette.Swatches
                    .Select(s => new Swatch { Id = s.Id, Color = s.Color, Name = s.Name })
                    .ToList(),
            };
            project.Palettes.Add(copy);
            variant.PaletteId = copy.Id;
        }
        character.Variants.Add(variant);
        return variant;
    }

    /// <summary>
    /// Give a variant its own version of one animation — the escape hatch for
    /// a difference colour cannot express. Everything else stays inherited.
    /// </summary>
    public static DocumentRef OverrideAnimation(
        Project project, Character character, CharacterVariant variant, DocumentRef inherited, Doc doc)
    {
        var slug = UniqueFileSlug(character, Slug($"{inherited.Name}-{variant.Name}"));
        var reference = new DocumentRef
        {
            Name = $"{inherited.Name} ({variant.Name})",
            Path = $"{CharactersDir}/{character.Slug}/{AnimationsDir}/{slug}.lightbox.json",
        };
        variant.AnimationOverrides[inherited.Id] = reference;
        project.Loaded[reference.Id] = doc;
        return reference;
    }

    // ---- load ---------------------------------------------------------------

    /// <summary>
    /// Read the manifest, the character files and the shared palettes —
    /// <b>not</b> the documents. Those come through
    /// <see cref="LoadDocument"/> when something actually needs them.
    /// </summary>
    public static Project Load(string root)
    {
        var manifestPath = Path.Combine(root, ManifestName);
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("Not a Lightbox project.", manifestPath);

        var manifest = JsonSerializer.Deserialize<ProjectManifest>(
            File.ReadAllText(manifestPath), DocJson.Options)
            ?? throw new JsonException("project.json deserialized to null.");

        // The manifest indexes characters by slug; each character's own file is
        // the authority on its animations, so a save that touched one character
        // never has to rewrite the others.
        for (var i = 0; i < manifest.Characters.Count; i++)
        {
            var path = CharacterPath(root, manifest.Characters[i].Slug);
            if (!File.Exists(path)) continue;
            if (JsonSerializer.Deserialize<Character>(File.ReadAllText(path), DocJson.Options) is { } loaded)
            {
                manifest.Characters[i] = loaded;
            }
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
            foreach (var frame in symbol.Frames.OfType<PaintedFrame>()) frame.Placements = null;
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
        project.Loaded[reference.Id] = doc;
        return doc;
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

        // Resources first: writing them is what fills in Manifest.Palettes, so
        // the manifest has to be written after, not twice.
        SaveResources(project);

        // Duration hints next, and for the same reason: they live on the
        // manifest, so refreshing them inside the document loop below would
        // update them after the manifest had already been written and the
        // scene list would report yesterday's lengths for ever.
        foreach (var reference in DocumentsToWrite(project, dirty))
        {
            reference.Frames = project.Loaded[reference.Id].Scene.FrameCount;
            reference.Fps = project.Loaded[reference.Id].Scene.Fps;
        }

        DocJson.WriteAtomic(
            Path.Combine(project.Root, ManifestName),
            JsonSerializer.Serialize(project.Manifest, DocJson.Options));

        foreach (var character in project.Manifest.Characters)
        {
            DocJson.WriteAtomic(
                CharacterPath(project.Root, character.Slug),
                JsonSerializer.Serialize(character, DocJson.Options));
        }

        foreach (var reference in DocumentsToWrite(project, dirty))
        {
            DocJson.Save(project.Loaded[reference.Id], project.PathOf(reference));
        }
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
                var without = project.Manifest.Characters.Count(c => c.Pivot is null);
                notes.Add(without == 0
                    ? "Export packs sprite sheets, registered on each character's pivot."
                    : $"Export packs sprite sheets. {without} character(s) have no pivot yet, "
                        + "so their frames register on the canvas instead.");
                break;
            case ProjectType.Illustration or ProjectType.Comic:
                notes.Add("Playback and camera tooling stop being offered. Nothing already authored is removed.");
                break;
            case ProjectType.AssetLibrary:
                notes.Add("Other projects can import these characters, bringing their animations and palette.");
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
    /// Wrap a loose <c>.lightbox.json</c> in a one-character project,
    /// <b>in memory</b>. Nothing is written until the artist chooses
    /// "Save as project…", so opening an old file keeps working exactly as it
    /// did and the container is offered rather than imposed.
    /// </summary>
    public static Project Migrate(Doc doc, string documentPath)
    {
        var name = Path.GetFileNameWithoutExtension(documentPath);
        if (name.EndsWith(".lightbox", StringComparison.OrdinalIgnoreCase)) name = name[..^9];

        var project = Create(name, Path.ChangeExtension(documentPath, null) + Extension);
        var character = AddCharacter(project, name);
        AddAnimation(project, character, name, doc);

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
            .OfType<PaintedFrame>()
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

    private static IEnumerable<Stroke> StrokesOf(Frame frame) => frame switch
    {
        PaintedFrame p => p.Strokes,
        VectorFrame v => v.Strokes,
        _ => [],
    };

    // ---- naming -------------------------------------------------------------

    private static string CharacterPath(string root, string slug) =>
        Path.Combine(root, CharactersDir, slug, "character.json");

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

    private static string UniqueSlug(Project project, string wanted)
    {
        var taken = project.Manifest.Characters.Select(c => c.Slug).ToHashSet();
        return Unique(wanted, taken);
    }

    private static string UniqueFileSlug(Character character, string wanted)
    {
        var taken = character.Animations
            .Concat(character.Variants.SelectMany(v => v.AnimationOverrides.Values))
            .Select(a => Path.GetFileName(a.Path).Replace(".lightbox.json", ""))
            .ToHashSet();
        return Unique(wanted, taken);
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
            CharactersDir, DocumentsDir, LegacyDocumentsDir, ScenesDir,
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
