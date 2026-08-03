using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Services;

/// <summary>
/// The artist's own symbol library: available with no project open, and in every
/// project they make.
/// </summary>
/// <remarks>
/// <para>
/// The global scope of <see cref="Lightbox.Core.Projects.SymbolScopes"/>, stored
/// beside the brushes, tips and timing patterns — because a sword an artist drew
/// belongs to them, not to whichever job it first appeared in. Project symbols
/// stay in <c>assets/symbols.json</c> and are reachable from nothing outside that
/// project, which is the whole point of having two scopes.
/// </para>
/// <para>
/// It is a <b>source to choose from</b>, never a live dependency: placing one
/// copies it into the project. So this file being missing, corrupt or on another
/// machine cannot stop a project rendering — which is why every failure here is
/// swallowed into "you have no library" rather than raised.
/// </para>
/// <para>
/// Written through <see cref="DocJson.Options"/> rather than a local set: a
/// <see cref="Symbol"/> holds polymorphic <see cref="Frame"/>s, and only the
/// document's own converter can round-trip those. A library saved with plain
/// options would reload as symbols with no drawings in them.
/// </para>
/// </remarks>
public static class SymbolLibrary
{
    /// <summary>Test seam. Null means the real file beside the other settings.</summary>
    public static string? PathOverride { get; set; }

    public static string Path =>
        PathOverride ?? System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!, "symbols.json");

    /// <summary>The library, keyed by id. Empty when there is not one yet.</summary>
    public static Dictionary<string, Symbol> Load()
    {
        try
        {
            if (!File.Exists(Path)) return new Dictionary<string, Symbol>(StringComparer.Ordinal);
            var read = JsonSerializer.Deserialize<Dictionary<string, Symbol>>(
                File.ReadAllText(Path), DocJson.Options);
            return read is null
                ? new Dictionary<string, Symbol>(StringComparer.Ordinal)
                : new Dictionary<string, Symbol>(read, StringComparer.Ordinal);
        }
        catch
        {
            // A corrupt library must never stop the application opening, and
            // cannot cost anybody artwork: every symbol that has been placed was
            // copied into its project.
            return new Dictionary<string, Symbol>(StringComparer.Ordinal);
        }
    }

    public static void Save(IReadOnlyDictionary<string, Symbol> library)
    {
        try
        {
            if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            // Write then move: a crash mid-save must not leave a half-written
            // file where the library was.
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(library, DocJson.Options));
            File.Move(temp, Path, overwrite: true);
        }
        catch
        {
            // Failing to save the library must not take the drawing down with it.
        }
    }
}
