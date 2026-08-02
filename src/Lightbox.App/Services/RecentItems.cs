namespace Lightbox.App.Services;

public enum RecentKind
{
    Document,
    Project,
}

/// <summary>Something you had open, and when.</summary>
public sealed class RecentItem
{
    public string Path { get; set; } = "";

    public string Name { get; set; } = "";

    public RecentKind Kind { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>The folder it is in, for telling two "walk" files apart.</summary>
    public string Where => System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } dir ? dir : Path;

    public string Glyph => Kind == RecentKind.Project ? "🗀" : "▣";
}

/// <summary>
/// What you had open last, newest first.
/// </summary>
/// <remarks>
/// <para>
/// Global rather than per document, and in settings rather than in any file:
/// "what was I working on" is a question about the person, and a list that
/// lived inside a document could only ever answer it for the document you had
/// already found.
/// </para>
/// <para>
/// The list logic is here rather than in the view model because all the ways
/// it goes wrong are list-shaped — the same file twice, the newest entry at
/// the bottom, a list that grows forever, a path that no longer exists — and
/// none of those need a window to catch.
/// </para>
/// </remarks>
public sealed class RecentItems
{
    /// <summary>
    /// How many to keep.
    /// </summary>
    /// <remarks>
    /// Short on purpose. A recents list is for resuming, and past a dozen you
    /// are searching rather than resuming — at which point Open… is the better
    /// tool and this one is just in the way.
    /// </remarks>
    public const int Limit = 12;

    public List<RecentItem> Items { get; set; } = [];

    /// <summary>
    /// Record that something was opened: newest first, no duplicates, capped.
    /// </summary>
    /// <param name="now">
    /// Passed in rather than read from the clock, so the ordering can be
    /// tested. A list whose order depends on wall time is a list that is
    /// tested by sleeping.
    /// </param>
    public void Add(string path, string name, RecentKind kind, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        // Re-opening something moves it to the top rather than adding a second
        // row — the list is about where you were, not how often you went.
        Items.RemoveAll(i => SamePath(i.Path, path));
        Items.Insert(0, new RecentItem
        {
            Path = path,
            Name = string.IsNullOrWhiteSpace(name) ? DisplayNameOf(path) : name,
            Kind = kind,
            OpenedAt = now,
        });
        if (Items.Count > Limit) Items.RemoveRange(Limit, Items.Count - Limit);
    }

    /// <summary>
    /// What to call a file whose name has two extensions.
    /// </summary>
    /// <remarks>
    /// A document is <c>walk.lightbox.json</c> and a project is
    /// <c>Knight.lbproj</c>. Stripping one extension leaves "walk.lightbox",
    /// which is not what anybody called it.
    /// </remarks>
    public static string DisplayNameOf(string path)
    {
        var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
        foreach (var suffix in (string[])[".lightbox.json", ".lbproj", ".json"])
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }
        return name;
    }

    public void Forget(string path) => Items.RemoveAll(i => SamePath(i.Path, path));

    public void Clear() => Items.Clear();

    /// <summary>
    /// The entries that are still on disk.
    /// </summary>
    /// <remarks>
    /// Filtered on read rather than pruned on write. A file on a drive that is
    /// not plugged in right now has not been deleted, and dropping it from the
    /// list the one time you opened the app without that drive would be losing
    /// the entry for good.
    /// </remarks>
    public IReadOnlyList<RecentItem> Existing() =>
        Items.Where(i => i.Kind == RecentKind.Project
            ? Directory.Exists(i.Path)
            : File.Exists(i.Path)).ToList();

    /// <summary>
    /// Case-insensitive on Windows and macOS, exact on Linux.
    /// </summary>
    /// <remarks>
    /// Getting this wrong shows the same file twice on Windows, which is the
    /// commonest way a recents list looks broken.
    /// </remarks>
    private static bool SamePath(string a, string b) =>
        string.Equals(
            System.IO.Path.TrimEndingDirectorySeparator(a),
            System.IO.Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
