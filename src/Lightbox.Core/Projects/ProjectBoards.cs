using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Projects;

/// <summary>
/// Where a reference board is kept, and how an imported image gets into the
/// project beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One board per scope, not per document</b> (Q87). A board is filed on the
/// same folder a sheet made from that document would be filed on — the top of
/// the subtree, via <see cref="ProjectSheets.DefaultScope"/> — so every animation
/// of the knight looks at the knight's wall rather than each rebuilding its own.
/// That is the arrangement persisting, which is the whole point: the roadmap
/// calls losing it the highest-friction gap in reference handling.
/// </para>
/// <para>
/// <b>Not in the manifest.</b> The path is derived from the folder's id, so a
/// board needs no registry entry, no migration and no second place to go wrong;
/// a scope with no board has no file, which is the ordinary "absent until used"
/// rule applied to a whole record. Renaming a folder does not move the file
/// because the id is what it is named after.
/// </para>
/// <para>
/// <b>Images are copied in, never linked</b> (Q87). A dragged file lands in
/// <see cref="ImagesDir"/> under its own slug; the tile keeps the original path
/// only as provenance. Linking would be free and would break silently the first
/// time a downloads folder was cleared — and a picture dragged out of a browser
/// has no durable path at all.
/// </para>
/// </remarks>
public static class ProjectBoards
{
    /// <summary>Where boards live, one file per scope.</summary>
    public const string RootDir = "boards";

    /// <summary>Where imported reference images are copied to.</summary>
    public const string ImagesDir = "references";

    /// <summary>What a board file ends in.</summary>
    public const string Extension = ".board.json";

    /// <summary>What a board filed nowhere in particular is called.</summary>
    public const string ProjectWideName = "project";

    /// <summary>Image extensions an import will accept.</summary>
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(
        [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The scope whose board this document looks at — the top folder above it,
    /// or null for a document filed nowhere, which sees the project-wide board.
    /// </summary>
    public static ProjectFolder? ScopeOf(ProjectManifest manifest, DocumentRef? document) =>
        ProjectSheets.DefaultScope(manifest, document);

    /// <summary>The project-relative path of a scope's board file.</summary>
    public static string PathFor(ProjectFolder? folder) =>
        $"{RootDir}/{folder?.Id ?? ProjectWideName}{Extension}";

    /// <summary>
    /// The board filed on <paramref name="folder"/>, or an empty one when the
    /// scope has never had a board — never null, because "no wall yet" and "an
    /// empty wall" are the same thing to everything above this.
    /// </summary>
    public static ReferenceBoard Load(Project project, ProjectFolder? folder)
    {
        if (ProjectIo.ResolveInProject(project, PathFor(folder)) is not { } path) return new ReferenceBoard();
        if (!File.Exists(path)) return new ReferenceBoard();
        try
        {
            return JsonSerializer.Deserialize<ReferenceBoard>(File.ReadAllText(path), DocJson.Options)
                   ?? new ReferenceBoard();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A board is an arrangement of reference, not the art. Losing one to a
            // corrupt file must not stop the project opening.
            return new ReferenceBoard();
        }
    }

    /// <summary>
    /// Write a scope's board — or remove the file when the board is empty, so a
    /// wall that was cleared leaves nothing behind.
    /// </summary>
    public static void Save(Project project, ProjectFolder? folder, ReferenceBoard board)
    {
        if (ProjectIo.ResolveInProject(project, PathFor(folder)) is not { } path) return;
        if (board.Tiles.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        DocJson.WriteAtomic(path, JsonSerializer.Serialize(board, DocJson.Options));
    }

    /// <summary>
    /// Copy an image into the project's reference directory.
    /// </summary>
    /// <returns>
    /// The project-relative path of the copy, or null when the file is not an
    /// image, is missing, or the project cannot be written to.
    /// </returns>
    public static string? ImportImage(Project project, string sourcePath)
    {
        if (!ImageExtensions.Contains(Path.GetExtension(sourcePath))) return null;
        if (!File.Exists(sourcePath)) return null;
        var bytes = File.ReadAllBytes(sourcePath);
        return ImportImage(project, bytes, Path.GetFileName(sourcePath));
    }

    /// <summary>
    /// Write bytes that came from somewhere with no path — a picture dragged out
    /// of a browser — into the project's reference directory under a name of our
    /// choosing.
    /// </summary>
    public static string? ImportImage(Project project, byte[] bytes, string fileName)
    {
        if (bytes.Length == 0) return null;
        var extension = Path.GetExtension(fileName);
        if (!ImageExtensions.Contains(extension)) extension = ".png";

        var slug = ProjectIo.Slug(Path.GetFileNameWithoutExtension(fileName));
        if (slug.Length == 0) slug = "reference";

        if (ProjectIo.ResolveInProject(project, ImagesDir) is not { } directory) return null;
        Directory.CreateDirectory(directory);

        // Deduped against what is already there rather than against the board:
        // two boards in one project share the directory, and a name taken by a
        // file nobody has pinned up is still taken.
        var unique = slug;
        for (var n = 2; File.Exists(Path.Combine(directory, unique + extension)); n++) unique = $"{slug}-{n}";

        try
        {
            File.WriteAllBytes(Path.Combine(directory, unique + extension), bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return $"{ImagesDir}/{unique}{extension}";
    }

    /// <summary>The absolute path of a tile's copied image, or null.</summary>
    public static string? ResolveImage(Project project, BoardTile tile) =>
        tile.Path is { Length: > 0 } relative ? ProjectIo.ResolveInProject(project, relative) : null;
}
