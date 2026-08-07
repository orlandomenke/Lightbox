using System.Runtime.InteropServices;

namespace Lightbox.App.Services;

/// <summary>
/// A console window, on request, for watching a problem happen.
/// </summary>
/// <remarks>
/// <para>
/// The log answers <em>what went wrong</em> after the fact. This answers
/// <em>what is going wrong</em> while it does, which is the half a file cannot
/// give you — you cannot tail a crash you have not caused yet.
/// </para>
/// <para>
/// <b>Why this is not just <c>AttachConsole</c>.</b> Attaching only works when
/// the app was launched from a terminal, and the ordinary way to launch
/// Lightbox is to double-click it — where there is no parent console to attach
/// to and nothing to inherit. <see cref="AllocConsole"/> is what actually
/// produces a window in that case, and it is the piece that makes "give me a
/// console when something goes wrong" answerable at all.
/// </para>
/// <para>
/// <b>And why the streams are reopened afterwards.</b> Attaching or allocating
/// gives the process a console; it does not necessarily repoint the standard
/// streams that .NET has already bound. Opening <c>CONOUT$</c> and pointing
/// <see cref="Console.Out"/> and <see cref="Console.Error"/> at it explicitly
/// removes the question — which was left genuinely open under B115, where
/// whether a GUI process inherits its launcher's stderr could not be settled
/// from a Linux container. It stops mattering rather than being answered.
/// </para>
/// </remarks>
internal static class DiagnosticsConsole
{
    private static bool _open;

    /// <summary>Whether a console is attached to this process.</summary>
    public static bool IsOpen => _open;

    /// <summary>
    /// Attach to the launching terminal, or make a console of our own.
    /// </summary>
    /// <returns>Whether there is now somewhere for console output to land.</returns>
    /// <remarks>
    /// Windows only. Everywhere else a process started from a terminal already
    /// has usable stdout and stderr, so there is nothing to arrange.
    /// </remarks>
    public static bool Open()
    {
        if (_open) return true;
        if (!OperatingSystem.IsWindows())
        {
            // Not a failure: stderr already goes somewhere on this platform.
            _open = true;
            return true;
        }

        // Prefer the terminal that launched us — output belongs where the
        // person is already looking. Failing that, make one.
        if (!AttachConsole(AttachParentProcess) && !AllocConsole()) return false;

        try
        {
            RebindStreams();
        }
        catch
        {
            // A console with streams we could not repoint is still better than
            // none: some output may reach it, and nothing here is worth
            // failing a menu click over.
        }

        _open = true;
        return true;
    }

    /// <summary>
    /// Point <c>Console.Out</c> and <c>Console.Error</c> at the console device
    /// itself, whatever the process inherited.
    /// </summary>
    private static void RebindStreams()
    {
        var stream = new FileStream("CONOUT$", FileMode.Open, FileAccess.Write);
        var writer = new StreamWriter(stream) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
}
