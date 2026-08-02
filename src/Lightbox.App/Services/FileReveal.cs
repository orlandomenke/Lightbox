using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lightbox.App.Services;

/// <summary>The three desktops, named so the argument-building can be tested.</summary>
public enum Desktop
{
    Windows,
    MacOs,
    Linux,
}

/// <summary>What to run, and with what.</summary>
public readonly record struct ShellCommand(string File, IReadOnlyList<string> Args);

/// <summary>
/// Handing a path to the desktop: show it in the file manager, or open it with
/// whatever application owns it.
/// </summary>
/// <remarks>
/// <para>
/// The interesting part is not launching a process — it is that each desktop
/// spells this differently, and getting it wrong is silent. Windows selects a
/// file inside its folder with <c>explorer /select,</c>; macOS does the same
/// with <c>open -R</c>; Linux has no portable reveal-and-select at all, so the
/// honest answer there is to open the containing folder. That choice is pure,
/// so it is separated from <see cref="Process"/> and tested.
/// </para>
/// <para>
/// Nothing here throws. A desktop with no file manager configured is a bad
/// afternoon, not a reason for the drawing application to fall over.
/// </para>
/// </remarks>
public static class FileReveal
{
    public static Desktop Current =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Desktop.Windows
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? Desktop.MacOs
        : Desktop.Linux;

    /// <summary>
    /// Show <paramref name="path"/> in the file manager, selected where the
    /// platform can do it.
    /// </summary>
    /// <param name="isDirectory">
    /// A directory is opened, not selected — "reveal the folder" means show
    /// what is inside it, which is what the folder button in the project
    /// panel is for.
    /// </param>
    public static ShellCommand RevealCommand(Desktop desktop, string path, bool isDirectory) =>
        desktop switch
        {
            // The comma is part of the switch, not a separator: `/select,path`.
            // Passed as one argument, or Explorer opens Documents instead and
            // says nothing about why.
            Desktop.Windows => isDirectory
                ? new ShellCommand("explorer.exe", [path])
                : new ShellCommand("explorer.exe", [$"/select,{path}"]),
            Desktop.MacOs => isDirectory
                ? new ShellCommand("open", [path])
                : new ShellCommand("open", ["-R", path]),
            // No portable equivalent of "select this file". Opening the folder
            // it is in is the nearest true thing; pretending otherwise would
            // mean shipping a Nautilus-only feature and calling it Linux.
            _ => new ShellCommand("xdg-open", [isDirectory ? path : Directory(path)]),
        };

    /// <summary>Hand the path to whatever application owns it.</summary>
    public static ShellCommand OpenCommand(Desktop desktop, string path) => desktop switch
    {
        // Through the shell, because on Windows "open with the associated
        // program" is a shell verb rather than an executable.
        Desktop.Windows => new ShellCommand(path, []),
        Desktop.MacOs => new ShellCommand("open", [path]),
        _ => new ShellCommand("xdg-open", [path]),
    };

    /// <summary>The folder a path lives in, or the path itself if it has no parent.</summary>
    public static string Directory(string path) =>
        Path.GetDirectoryName(path) is { Length: > 0 } parent ? parent : path;

    /// <summary>Show a file or folder in the desktop's file manager.</summary>
    public static bool Reveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var isDirectory = System.IO.Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            // The file has not been written yet — a new animation before the
            // first save. Showing the folder it will land in is more use than
            // doing nothing.
            path = Directory(path);
            if (!System.IO.Directory.Exists(path)) return false;
            isDirectory = true;
        }
        return Run(RevealCommand(Current, path, isDirectory), shellExecute: false);
    }

    /// <summary>Open a file with the application the desktop associates with it.</summary>
    public static bool Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!File.Exists(path) && !System.IO.Directory.Exists(path)) return false;
        // Shell execute is what makes "open with the associated program" mean
        // anything; without it Windows tries to execute the .json.
        return Run(OpenCommand(Current, path), shellExecute: Current == Desktop.Windows);
    }

    private static bool Run(ShellCommand command, bool shellExecute)
    {
        try
        {
            var info = new ProcessStartInfo(command.File) { UseShellExecute = shellExecute };
            foreach (var arg in command.Args) info.ArgumentList.Add(arg);
            Process.Start(info);
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
            or InvalidOperationException or PlatformNotSupportedException or IOException)
        {
            return false;
        }
    }
}
