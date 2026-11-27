using System.Text;

namespace Lightbox.Import.Unity;

/// <summary>A sprite clip found in the project.</summary>
/// <param name="RelativePath">From the project root, forward slashes: <c>Assets/Animation/Player/Dash.anim</c>.</param>
/// <param name="Folder">The folder holding it, the same way — what the clip browser groups by.</param>
public sealed record UnityClipEntry(string RelativePath, string Folder, UnityClip Clip);

/// <summary>A file the scan could not use, and why. The scan keeps going past it.</summary>
public sealed record UnityScanProblem(string RelativePath, string Reason);

/// <summary>
/// Where a sprite reference leads: the texture file, its slicing and the one slice,
/// or a <see cref="Problem"/> saying which step failed.
/// </summary>
public sealed record UnitySpriteResolution(
    UnitySpriteRef Ref,
    string? TexturePath,
    UnityTextureMeta? Texture,
    UnitySpriteSlice? Slice,
    string? Problem)
{
    public bool Resolved => Slice is not null;
}

/// <summary>Bounds on a scan (S1).</summary>
public sealed record UnityScanOptions
{
    public int MaxFiles { get; init; } = 200_000;
    public long MaxFileBytes { get; init; } = UnityYaml.MaxChars;
}

/// <summary>Everything a scan found: the sprite clips, the guid map, and what it skipped.</summary>
public sealed class UnityProjectIndex
{
    private readonly Dictionary<string, string> _assetByGuid;
    private readonly Dictionary<UnitySpriteRef, List<string>> _clipsBySprite;
    private readonly Dictionary<string, (UnityTextureMeta? Meta, string? Problem)> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly long _maxFileBytes;

    internal UnityProjectIndex(string root, List<UnityClipEntry> clips, List<UnityScanProblem> problems,
        Dictionary<string, string> assetByGuid, long maxFileBytes)
    {
        Root = root;
        Clips = clips;
        Problems = problems;
        _assetByGuid = assetByGuid;
        _maxFileBytes = maxFileBytes;
        _clipsBySprite = [];
        foreach (var c in clips)
            foreach (var t in c.Clip.Tracks)
                foreach (var k in t.Keys)
                {
                    if (k.Sprite is not { } s) continue;
                    if (!_clipsBySprite.TryGetValue(s, out var list)) _clipsBySprite[s] = list = [];
                    if (!list.Contains(c.RelativePath)) list.Add(c.RelativePath);
                }
    }

    /// <summary>The project folder, full path.</summary>
    public string Root { get; }

    /// <summary>Every clip with at least one sprite track, in path order.</summary>
    public IReadOnlyList<UnityClipEntry> Clips { get; }

    /// <summary>Clips that could not be read, each with why.</summary>
    public IReadOnlyList<UnityScanProblem> Problems { get; }

    /// <summary>The asset a guid names, relative to <see cref="Root"/>, or null.</summary>
    public string? AssetPath(string guid) => _assetByGuid.GetValueOrDefault(guid.ToLowerInvariant());

    /// <summary>Every clip that shows <paramref name="sprite"/> — more than one means a shared sprite.</summary>
    public IReadOnlyList<string> ClipsUsing(UnitySpriteRef sprite) =>
        _clipsBySprite.TryGetValue(sprite, out var l) ? l : [];

    /// <summary>
    /// Follows a reference to its texture and slice. Each texture's <c>.meta</c> is
    /// read once and kept.
    /// </summary>
    public UnitySpriteResolution Resolve(UnitySpriteRef sprite)
    {
        var rel = AssetPath(sprite.Guid);
        if (rel is null)
            return new(sprite, null, null, null, "no asset in the project has this guid (deleted, or outside Assets)");
        if (!rel.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return new(sprite, rel, null, null, "the sprite is in a texture that is not a PNG; only PNG sheets are supported");

        if (!_textures.TryGetValue(sprite.Guid, out var tex))
            _textures[sprite.Guid] = tex = LoadTexture(rel);
        if (tex.Meta is null) return new(sprite, rel, null, null, tex.Problem);

        var slice = tex.Meta.Find(sprite.FileId);
        return slice is null
            ? new(sprite, rel, tex.Meta, null, $"the texture has no sprite with id {sprite.FileId}")
            : new(sprite, rel, tex.Meta, slice, null);
    }

    private (UnityTextureMeta?, string?) LoadTexture(string rel)
    {
        var full = UnityProjectScanner.Inside(Root, rel);
        if (full is null) return (null, "the path leaves the project");
        try
        {
            if (UnityProjectScanner.IsLink(full) || UnityProjectScanner.IsLink(full + ".meta"))
                return (null, "the texture or its .meta is a link; links are not followed");
            int w, h;
            using (var fs = File.OpenRead(full)) (w, h) = PngHeader.ReadSize(fs, rel);
            var metaText = UnityProjectScanner.ReadBounded(full + ".meta", _maxFileBytes, rel + ".meta");
            return (UnityTextureMetaReader.Read(metaText, rel + ".meta", Path.GetFileNameWithoutExtension(full), w, h), null);
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            return (null, e.Message);
        }
    }
}

/// <summary>
/// Finds the sprite clips in a Unity project folder: walks <c>Assets/</c>, maps
/// every guid to its asset, and reads each <c>.anim</c> that swaps sprites.
/// </summary>
/// <remarks>
/// Read-only, and never outside the folder it was given: directory links are not
/// followed and every path it hands back is checked to resolve inside the root
/// (S1). Folders Unity itself ignores — hidden ones and those ending in <c>~</c> —
/// are skipped too.
/// </remarks>
public static class UnityProjectScanner
{
    /// <summary>True when <paramref name="root"/> has <c>Assets/</c> and a project version file.</summary>
    public static bool LooksLikeUnityProject(string root) =>
        Directory.Exists(Path.Combine(root, "Assets"))
        && File.Exists(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"));

    public static UnityProjectIndex Scan(string root, UnityScanOptions? options = null)
    {
        options ??= new UnityScanOptions();
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var assets = Path.Combine(fullRoot, "Assets");
        if (!Directory.Exists(assets))
            throw new DirectoryNotFoundException($"{root} has no Assets folder; it is not a Unity project.");
        if (IsLink(assets))
            throw new IOException($"{root}: Assets is a link to somewhere else; links are not followed.");

        var owners = new Dictionary<string, List<string>>();
        var anims = new List<string>();
        var problems = new List<UnityScanProblem>();
        var seen = 0;

        var pending = new Stack<string>();
        pending.Push(assets);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            List<FileSystemInfo> entries;
            // The listing carries each entry's attributes; asking per file costs a
            // round trip each, which is most of a scan on a cold disk.
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add(new(Relative(fullRoot, dir), e.Message));
                continue;
            }
            foreach (var entry in entries)
            {
                var path = entry.FullName;
                if (++seen > options.MaxFiles)
                {
                    problems.Add(new("Assets", $"more than {options.MaxFiles} files; the scan stopped there"));
                    pending.Clear();
                    break;
                }
                var name = entry.Name;
                var attrs = entry.Attributes;

                // A link, folder or file, is skipped: what it points at is outside
                // anything this scan checked.
                if (IsLink(entry)) continue;
                if (UnityIgnores(name, attrs)) continue;
                if ((attrs & FileAttributes.Directory) != 0)
                {
                    pending.Push(path);
                    continue;
                }
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    if (ReadGuid(path) is { } g)
                    {
                        if (!owners.TryGetValue(g, out var list)) owners[g] = list = [];
                        list.Add(Relative(fullRoot, path[..^5]));
                    }
                }
                else if (name.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                {
                    anims.Add(path);
                }
            }
        }

        var clips = new List<UnityClipEntry>();
        foreach (var path in anims.Order(StringComparer.OrdinalIgnoreCase))
        {
            var rel = Relative(fullRoot, path);
            try
            {
                var text = ReadBounded(path, options.MaxFileBytes, rel);
                if (!text.Contains("m_Sprite", StringComparison.Ordinal)) continue;
                var clip = UnityAnimReader.Read(text, rel);
                if (clip.Tracks.Count == 0) continue;
                var folder = rel.Contains('/') ? rel[..rel.LastIndexOf('/')] : "";
                clips.Add(new UnityClipEntry(rel, folder, clip));
            }
            catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
            {
                problems.Add(new(rel, e.Message));
            }
        }

        // A guid on two assets is what copying a folder outside Unity leaves. Unity
        // reassigns one on its next import; until then the choice must not depend
        // on the order a disk lists files in, so the first path in ordinal order
        // wins, and the clash is reported.
        var guids = new Dictionary<string, string>(owners.Count);
        foreach (var (g, paths) in owners)
        {
            paths.Sort(StringComparer.Ordinal);
            guids[g] = paths[0];
            if (paths.Count > 1)
                problems.Add(new(paths[0], $"guid {g} is shared by {string.Join(", ", paths)}; using {paths[0]}"));
        }

        return new UnityProjectIndex(fullRoot, clips, problems, guids, options.MaxFileBytes);
    }

    // The guid sits in the first few lines of a .meta; read no more than that.
    private static string? ReadGuid(string metaPath)
    {
        try
        {
            // The head only: a .meta with no newline in its first few KB has no guid
            // worth trusting, and ReadLine would otherwise buffer the whole file.
            using var fs = new FileStream(metaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
            var head = new byte[GuidHeadBytes];
            var n = 0;
            while (n < head.Length)
            {
                var r = fs.Read(head, n, head.Length - n);
                if (r == 0) break;
                n += r;
            }
            foreach (var line in Encoding.UTF8.GetString(head, 0, n).Split('\n').Take(8))
            {
                if (line.StartsWith("guid: ", StringComparison.Ordinal))
                {
                    var g = line[6..].Trim();
                    return UnityAnimReader.IsGuid(g) ? g.ToLowerInvariant() : null;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private const int GuidHeadBytes = 4096;

    /// <summary>
    /// What Unity's own asset database skips: hidden folders, and files and folders
    /// starting with '.', ending with '~', named <c>cvs</c>, or with a <c>.tmp</c>
    /// extension. The scan skips the same, so it lists what Unity can see.
    /// </summary>
    internal static bool UnityIgnores(string name, FileAttributes attrs) =>
        name.StartsWith('.')
        || name.EndsWith('~')
        || name.Equals("cvs", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
        || (attrs & (FileAttributes.Directory | FileAttributes.Hidden)) == (FileAttributes.Directory | FileAttributes.Hidden);

    /// <summary>
    /// A symbolic link or junction. Other reparse points — a cloud-synced
    /// folder's placeholders — are ordinary files that happen to live in the
    /// cloud, and are walked like any other.
    /// </summary>
    internal static bool IsLink(FileSystemInfo entry)
    {
        try { return entry.LinkTarget is not null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>True when <paramref name="path"/> is a symbolic link or junction (or cannot be checked).</summary>
    public static bool IsLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.Exists && IsLink(info);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>
    /// The file as text, reading at most <paramref name="max"/> bytes whatever its
    /// reported length says (S1): a length is metadata, the read is the limit.
    /// </summary>
    /// <exception cref="FormatException">Past the limit.</exception>
    internal static string ReadBounded(string path, long max, string label)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        while (true)
        {
            var r = fs.Read(buf, 0, buf.Length);
            if (r == 0) break;
            if (ms.Length + r > max)
                throw new FormatException($"{label}: larger than {max / (1024 * 1024)} MB; not read.");
            ms.Write(buf, 0, r);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static string Relative(string root, string full) =>
        Path.GetRelativePath(root, full).Replace('\\', '/');

    /// <summary>
    /// <paramref name="relative"/> as a full path, or null if it resolves outside
    /// <paramref name="root"/> — the check every path from a file goes through.
    /// </summary>
    public static string? Inside(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        return full.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? full
            : null;
    }
}
