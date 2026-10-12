using System.Runtime.InteropServices;

namespace Lightbox.App.Services;

/// <summary>
/// One millisecond timer resolution from Windows while something needs it —
/// playback — counted, so the request is made once and given back once.
/// </summary>
/// <remarks>
/// <para>
/// Windows wakes timers in steps of the system timer, about 15.6 ms unless a
/// process asks for finer. A 24 fps frame is 41.7 ms, which no whole number of
/// those steps makes, so playback's <c>DispatcherTimer</c> woke at 31 or 47 ms
/// and the pacing dropped a frame every so often: 19-23 fps of 24 in the lab
/// with every frame prepared (play-steady, 2026-10-11). Media players ask for
/// 1 ms while playing; this is that, for as long as a clock runs.
/// </para>
/// <para>
/// Since Windows 10 2004 the request belongs to the process alone, so it costs
/// nothing outside Lightbox, and only while playing. Elsewhere it does nothing.
/// </para>
/// </remarks>
public static class TimerResolution
{
    private static int _holders;

    /// <summary>How many hold it now — for tests.</summary>
    public static int Holders => Volatile.Read(ref _holders);

    /// <summary>Ask for 1 ms timing; the first holder makes the request.</summary>
    public static void Hold()
    {
        if (Interlocked.Increment(ref _holders) == 1 && OperatingSystem.IsWindows()) _ = TimeBeginPeriod(1);
    }

    /// <summary>Give it back; the last holder ends the request.</summary>
    public static void Release()
    {
        // Never below zero: a release without its hold is a bug elsewhere, and
        // ending a request this process never made would be another.
        int now;
        do
        {
            now = Volatile.Read(ref _holders);
            if (now == 0) return;
        }
        while (Interlocked.CompareExchange(ref _holders, now - 1, now) != now);
        if (now == 1 && OperatingSystem.IsWindows()) _ = TimeEndPeriod(1);
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);
}
