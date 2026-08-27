using System.Runtime.InteropServices;

namespace Lightbox.App.Rendering;

/// <summary>
/// How often the screen can show a new frame, asked of the operating system
/// rather than inferred from Lightbox's own timings (B321).
/// </summary>
/// <remarks>
/// <para>
/// <b>The one number in the present chain that Lightbox does not choose, and
/// the reason it has to come from outside.</b> Every other figure in the
/// report is measured by the code being judged, so a wait that happens to
/// equal the refresh period cannot be told apart from a wait that happens to
/// equal anything else. B321's first verdict was retracted for exactly that
/// shape of coincidence: the publish dam's 250 ms backstop sat beside an
/// observed publish cadence of 241 ms, which looked like a diagnosis and was
/// not. An independently sourced period turns "17 ms, which is suspiciously
/// close to a vsync" into "one refresh", and that is a fact rather than a fit.
/// </para>
/// <para>
/// <b>Windows only, and null everywhere else on purpose.</b> The report prints
/// the arithmetic that depends on this only when there is an answer — a floor
/// computed from a guessed refresh rate would be worse than no floor at all,
/// and the machine this bug lives on is a Windows one. A headless container
/// has no display and correctly gets nothing.
/// </para>
/// <para>
/// Read once and cached: a refresh rate can change while a session runs (an
/// external monitor, a power-saving switch) and re-reading it per report would
/// still not tell anyone which rate the captured frames were drawn at. One
/// value, named in the report, is honest about what it is.
/// </para>
/// </remarks>
internal static class DisplayCadence
{
    private static bool _asked;
    private static int _hz;

    /// <summary>
    /// The primary display's refresh rate in hertz, or null when it cannot be
    /// had — which callers must treat as "do not compute a floor" rather than
    /// as any particular rate.
    /// </summary>
    public static int? Hz
    {
        get
        {
            if (!_asked)
            {
                _asked = true;
                _hz = Query();
            }

            return _hz > 0 ? _hz : null;
        }
    }

    /// <summary>One refresh, in milliseconds, or null when the rate is unknown.</summary>
    public static double? PeriodMs => Hz is { } hz and > 0 ? 1000.0 / hz : null;

    /// <summary>
    /// Force a value for one test, or clear it back to whatever the machine
    /// says. The report's arithmetic branches on this and a test that could
    /// only run on a 60 Hz screen would be a test of the screen.
    /// </summary>
    internal static void OverrideForTest(int? hz)
    {
        _asked = hz is not null;
        _hz = hz ?? 0;
    }

    private static int Query()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        try
        {
            var mode = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
            // Null device name is the primary display; -1 is the mode it is
            // actually running in rather than one it merely supports.
            if (!EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref mode)) return 0;
            // 0 and 1 are documented as "the hardware default", which is not a
            // rate and must not be turned into a 1000 ms period.
            return mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : 0;
        }
        catch
        {
            // A diagnostic must never be the reason a report fails to write.
            return 0;
        }
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(
        string? deviceName, int modeNum, ref DEVMODEW devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public int dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }
}
