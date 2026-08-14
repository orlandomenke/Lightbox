using System.Reflection;

namespace Lightbox.Mcp;

/// <summary>
/// Which build of the MCP server this is.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because B195 was fixed and stayed broken.</b> The bug was in
/// this project; the fix landed in <c>main</c> the same morning; the report came
/// back unchanged, because Claude Desktop launches a <em>published</em>
/// <c>Lightbox.Mcp.exe</c> and nothing in the protocol said which one. Neither
/// end of the conversation could tell a current server from a stale one, so the
/// only way to answer "is this fixed in what I am actually talking to" was to go
/// and look at a file's timestamp.
/// </para>
/// <para>
/// The stamp is the same one <c>DiagnosticLog.Build</c> uses on the app side —
/// the SDK appends <c>+&lt;git sha&gt;</c> to
/// <c>AssemblyInformationalVersion</c>, so a build names its exact commit rather
/// than a version number forty builds have shared.
/// </para>
/// </remarks>
public static class BuildInfo
{
    public static string Build =>
        typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
