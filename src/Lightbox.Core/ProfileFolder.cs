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
/// A profile moved this way reads no stored API key from the real one, which is
/// the safe direction: a measuring run has no business with the artist's key.
/// </para>
/// </remarks>
public static class ProfileFolder
{
    /// <summary>The profile folder: the override when set, otherwise <c>%APPDATA%\Lightbox</c>.</summary>
    public static string Root =>
        Environment.GetEnvironmentVariable("LIGHTBOX_PROFILE_DIR") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lightbox");
}
