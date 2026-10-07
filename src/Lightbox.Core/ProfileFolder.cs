namespace Lightbox.Core;

/// <summary>
/// Where Lightbox keeps the artist's settings, brushes, workspaces, symbols,
/// logs and AI configuration — one folder, so one variable can move all of it.
/// </summary>
/// <remarks>
/// <para>
/// <c>LIGHTBOX_PROFILE_DIR</c> points the whole profile somewhere else. It
/// exists for the performance lab (Q209): the September harness that preceded
/// it drove the real app against the owner's real profile and left nine
/// built-in brushes at Size 70, and overriding <c>APPDATA</c> did not redirect
/// the app — every store built its own path from the known folder. Now they
/// all start here.
/// </para>
/// <para>
/// A profile moved this way holds no key from the artist's own profile. A key
/// in the <c>ANTHROPIC_API_KEY</c> environment variable still applies — it is
/// not stored in any profile — so the lab clears it in the process it starts.
/// </para>
/// <para>
/// <b>Read once, and only an absolute local path is honoured</b> (the
/// sensitivity review). Some stores fix their path when their class first
/// loads and others re-read it, so a relative override resolved against a
/// changing current directory would split one profile across two folders; and
/// a network or shared folder would put plaintext settings and full autosave
/// copies where someone else can read them. Anything else is ignored and the
/// normal profile is used.
/// </para>
/// </remarks>
public static class ProfileFolder
{
    private static readonly Lazy<string> Resolved = new(Resolve);

    /// <summary>The profile folder: the override when honoured, otherwise <c>%APPDATA%\Lightbox</c>.</summary>
    public static string Root => Resolved.Value;

    /// <summary>Why an override was ignored, if one was. For the diagnostic log.</summary>
    public static string? IgnoredOverride { get; private set; }

    /// <summary>Resolve the override now, uncached. For tests; the app reads <see cref="Root"/>.</summary>
    public static string Resolve()
    {
        var standard = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lightbox");
        if (Environment.GetEnvironmentVariable("LIGHTBOX_PROFILE_DIR") is not { Length: > 0 } custom)
        {
            return standard;
        }
        if (!Path.IsPathFullyQualified(custom))
        {
            IgnoredOverride = $"LIGHTBOX_PROFILE_DIR is not an absolute path: {custom}";
            return standard;
        }
        if (custom.StartsWith(@"\\", StringComparison.Ordinal) || custom.StartsWith("//", StringComparison.Ordinal))
        {
            IgnoredOverride = $"LIGHTBOX_PROFILE_DIR is a network path: {custom}";
            return standard;
        }
        return Path.GetFullPath(custom);
    }
}
