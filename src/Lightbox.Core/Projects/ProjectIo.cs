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
    private const string PalettesDir = "palettes";
    private const string GradientsFile = "gradients/gradients.json";

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
        foreach (var relative in project.Manifest.Palettes)
        {
            var path = Path.Combine(project.Root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            project.Palettes.Add(GimpPalette.Read(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path)));
        }

        var gradients = Path.Combine(project.Root, GradientsFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(gradients)) return;
        var read = JsonSerializer.Deserialize<Dictionary<string, Gradient>>(
            File.ReadAllText(gradients), DocJson.Options);
        foreach (var (id, gradient) in read ?? []) project.Gradients[id] = gradient;
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

        // Resources first: writing them is what fills in Manifest.Palettes, so
        // the manifest has to be written after, not twice.
        SaveResources(project);

        DocJson.WriteAtomic(
            Path.Combine(project.Root, ManifestName),
            JsonSerializer.Serialize(project.Manifest, DocJson.Options));

        foreach (var character in project.Manifest.Characters)
        {
            DocJson.WriteAtomic(
                CharacterPath(project.Root, character.Slug),
                JsonSerializer.Serialize(character, DocJson.Options));
        }

        foreach (var reference in project.AllDocuments)
        {
            if (dirty is not null && !dirty.Contains(reference.Id)) continue;
            if (!project.Loaded.TryGetValue(reference.Id, out var doc)) continue;
            DocJson.Save(doc, project.PathOf(reference));
        }
    }

    private static void SaveResources(Project project)
    {
        project.Manifest.Palettes.Clear();
        foreach (var palette in project.Palettes)
        {
            var relative = $"{PalettesDir}/{Slug(palette.Name)}.gpl";
            project.Manifest.Palettes.Add(relative);
            DocJson.WriteAtomic(
                Path.Combine(project.Root, relative.Replace('/', Path.DirectorySeparatorChar)),
                GimpPalette.Write(palette));
        }

        if (project.Gradients.Count == 0) return;
        DocJson.WriteAtomic(
            Path.Combine(project.Root, GradientsFile.Replace('/', Path.DirectorySeparatorChar)),
            JsonSerializer.Serialize(project.Gradients, DocJson.Options));
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
        var strokes = copy.Scene.Layers
            .SelectMany(l => l.Cels)
            .Select(c => c.Frame)
            .Concat(copy.ReferenceSheets.SelectMany(s => s.Views).SelectMany(v => v.Layers).SelectMany(l => l.Cels).Select(c => c.Frame))
            .OfType<Frame>()
            .SelectMany(StrokesOf)
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
}
