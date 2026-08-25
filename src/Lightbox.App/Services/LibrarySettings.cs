namespace Lightbox.App.Services;

/// <summary>
/// Where character libraries live on this machine.
/// </summary>
/// <remarks>
/// App-level (Q138): which disks hold libraries is a property of the machine,
/// not of any artwork — the same reasoning that puts onion depths here. Empty
/// by default, so the library surfaces are absent until an artist points at a
/// folder; a root may be a library project itself or a directory holding
/// several, which is <c>CharacterLibrary.Scan</c>'s contract. Scanning happens
/// when a surface opens, never at startup — a slow network drive must not tax
/// the launch of an app that may never open the library.
/// </remarks>
public sealed class LibrarySettings
{
    public List<string> Roots { get; set; } = [];
}
