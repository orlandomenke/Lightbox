using System.Globalization;
using System.Text;
using Avalonia.Input;

namespace Lightbox.App.Services;

/// <summary>
/// The replay section of an input trace: every event the report already prints
/// in prose, written once more in a form a test can read back exactly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a second copy of the same events.</b> The prose list is rounded for
/// reading — position to one decimal, pressure to two — and the report's own
/// tests say its wording may be improved freely. Both of those are right for a
/// human and fatal for a replay: <em>fractional coordinates are the discriminating
/// evidence</em> in B126 (the pen reports 89% fractional, Windows Ink's phantom
/// mouse 0%), so a format that rounds them throws away the fact the capture was
/// taken to establish. This section is the machine's copy — full precision,
/// fixed columns, versioned — and <c>InputTraceReplay</c> in the test suite is
/// its only reader.
/// </para>
/// <para>
/// <b>In the same file rather than a sidecar, deliberately.</b> The read-out
/// ritual in B126 is "press F9, hover for a minute, send the file". A second
/// file is a second thing to forget, and a capture that arrives without its
/// replay data cannot be turned into a fixture afterwards — the events are
/// gone. One file keeps the ritual exactly as the manual already describes it.
/// </para>
/// <para>
/// <b>The version is in the marker because fixtures outlive the format.</b> A
/// trace checked in as a regression fixture is read by every future build, and
/// <c>v1</c> captures are still read here: they carry no contact flag and no
/// device timestamp, and a replay of one has to infer contact rather than
/// pretend it was recorded. Reading them is the point of the number.
/// </para>
/// </remarks>
internal static class InputTraceLog
{
    /// <summary>The line that opens the section. Everything before it is prose.</summary>
    internal const string Marker = "replay v2";

    /// <summary>
    /// The first format: no <c>inContact</c>, no <c>deviceTime</c>, no rig line.
    /// Still read, because captures already taken are still evidence.
    /// </summary>
    internal const string MarkerV1 = "replay v1";

    internal const string Columns =
        "seconds\tkind\tdevice\tid\tx\ty\tpressure\ttiltX\ttiltY\tmodifiers\tdetail"
        + "\tinContact\tdeviceTime";

    private const string ColumnsV1 =
        "seconds\tkind\tdevice\tid\tx\ty\tpressure\ttiltX\ttiltY\tmodifiers\tdetail";

    private const int FieldsV1 = 11;
    private const int FieldsV2 = 13;

    private const string RigPrefix = "# rig ";

    /// <summary>
    /// A capture: its events and the little that has to be true around them for
    /// a replay to mean anything.
    /// </summary>
    /// <param name="CanvasWidth">
    /// The canvas the positions are relative to. Zero in a v1 capture, and in a
    /// v2 one taken before any pointer reached the canvas.
    /// </param>
    /// <param name="Wrapped">
    /// The ring dropped the earliest events, so this capture starts mid-stream —
    /// possibly with the pointer already inside the canvas and no <c>Enter</c>
    /// to say so. A replay that ignored this could report a teardown that is an
    /// artefact of where the recording begins.
    /// </param>
    internal sealed record Capture(
        int Version,
        IReadOnlyList<InputTrace.Entry> Entries,
        double CanvasWidth,
        double CanvasHeight,
        double ZoomPercent,
        bool Wrapped);

    /// <summary>Append the replay section to a report being built.</summary>
    internal static void Append(
        StringBuilder sb,
        IReadOnlyList<InputTrace.Entry> entries,
        (double Width, double Height, double ZoomPercent) rig,
        bool wrapped)
    {
        var c = CultureInfo.InvariantCulture;
        sb.AppendLine(Marker);
        sb.AppendLine("# the same events at full precision, for the replay harness in the test");
        sb.AppendLine("# suite. The list above is this list rounded for reading.");
        sb.AppendLine(string.Create(c, $"{RigPrefix}canvas={rig.Width:F2}x{rig.Height:F2} "
            + $"zoom={rig.ZoomPercent:F2} wrapped={(wrapped ? "true" : "false")}"));
        sb.AppendLine(Columns);
        foreach (var e in entries) sb.AppendLine(Format(e));
    }

    /// <summary>One event as one line. Round-trips through <see cref="TryParse"/>.</summary>
    /// <remarks>
    /// Every number goes out with the shortest representation that reads back
    /// identically — .NET's default for <c>float</c> and <c>double</c> since
    /// Core 3.0 — so a replay stamps the dab the capture stamped rather than one
    /// hash away from it (invariant 2: position seeds every dynamic).
    /// </remarks>
    internal static string Format(in InputTrace.Entry e)
    {
        var c = CultureInfo.InvariantCulture;
        return string.Join('\t',
            e.Seconds.ToString(c),
            e.Kind.ToString(),
            e.Device.ToString(),
            e.DeviceId.ToString(c),
            e.X.ToString(c),
            e.Y.ToString(c),
            e.Pressure.ToString(c),
            e.TiltX.ToString(c),
            e.TiltY.ToString(c),
            e.Modifiers.ToString(),
            Clean(e.Detail),
            e.InContact ? "1" : "0",
            e.DeviceTime.ToString(c));
    }

    /// <summary>
    /// Detail is free text from a popup's own name, so it is flattened rather
    /// than trusted: a tab or a newline inside it would move every column after
    /// it, and a fixture that silently loses its last field is worse than one
    /// that fails to parse.
    /// </summary>
    private static string Clean(string? detail) =>
        detail is null ? string.Empty : detail.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    internal static bool TryParse(string line, out InputTrace.Entry entry)
    {
        entry = default;
        var f = line.Split('\t');
        if (f.Length is not (FieldsV1 or FieldsV2)) return false;
        var c = CultureInfo.InvariantCulture;
        if (!double.TryParse(f[0], NumberStyles.Float, c, out var seconds)) return false;
        if (!Enum.TryParse<InputTrace.Kind>(f[1], out var kind)) return false;
        if (!Enum.TryParse<PointerType>(f[2], out var device)) return false;
        if (!int.TryParse(f[3], NumberStyles.Integer, c, out var id)) return false;
        if (!float.TryParse(f[4], NumberStyles.Float, c, out var x)) return false;
        if (!float.TryParse(f[5], NumberStyles.Float, c, out var y)) return false;
        if (!float.TryParse(f[6], NumberStyles.Float, c, out var pressure)) return false;
        if (!float.TryParse(f[7], NumberStyles.Float, c, out var tiltX)) return false;
        if (!float.TryParse(f[8], NumberStyles.Float, c, out var tiltY)) return false;
        if (!Enum.TryParse<KeyModifiers>(f[9], out var modifiers)) return false;

        var inContact = false;
        ulong deviceTime = 0;
        if (f.Length == FieldsV2)
        {
            if (f[11] is not ("0" or "1")) return false;
            inContact = f[11] == "1";
            if (!ulong.TryParse(f[12], NumberStyles.Integer, c, out deviceTime)) return false;
        }

        entry = new InputTrace.Entry(
            seconds, kind, device, id, x, y, pressure, tiltX, tiltY, modifiers,
            f[10].Length == 0 ? null : f[10],
            inContact, deviceTime);
        return true;
    }

    /// <summary>
    /// A whole capture, oldest event first.
    /// </summary>
    /// <remarks>
    /// <b>Strict on purpose.</b> A missing section or a line that will not parse
    /// throws rather than returning what it managed, because the alternative is a
    /// fixture that loads half a capture and passes: the assertion would then be
    /// about an arbitrary prefix of somebody's minute, and nothing would say so.
    /// </remarks>
    internal static Capture Read(TextReader reader)
    {
        string? line;
        var version = 0;
        var number = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            number++;
            var trimmed = line.Trim();
            if (trimmed == Marker) { version = 2; break; }
            if (trimmed == MarkerV1) { version = 1; break; }
        }
        if (version == 0)
        {
            throw new FormatException(
                $"no '{Marker}' section — this is either not an input trace or it was written "
                + "by a build older than the replay format.");
        }

        double width = 0, height = 0, zoom = 0;
        var wrapped = false;
        var entries = new List<InputTrace.Entry>();
        while ((line = reader.ReadLine()) is not null)
        {
            number++;
            if (line.StartsWith(RigPrefix, StringComparison.Ordinal))
            {
                (width, height, zoom, wrapped) = ParseRig(line);
                continue;
            }
            if (line.Length == 0 || line[0] == '#' || line == Columns || line == ColumnsV1) continue;
            if (!TryParse(line, out var entry))
            {
                throw new FormatException($"line {number} is not a replay event: {line}");
            }
            entries.Add(entry);
        }
        return new Capture(version, entries, width, height, zoom, wrapped);
    }

    /// <summary>
    /// The rig line, forgivingly: an unreadable one costs the geometry and not
    /// the capture. The events are the evidence; this is the context around them,
    /// and refusing a whole minute over a malformed comment would be the wrong
    /// trade.
    /// </summary>
    private static (double Width, double Height, double Zoom, bool Wrapped) ParseRig(string line)
    {
        var c = CultureInfo.InvariantCulture;
        double width = 0, height = 0, zoom = 0;
        var wrapped = false;
        foreach (var part in line[RigPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            if (split.Length != 2) continue;
            switch (split[0])
            {
                case "canvas":
                    var wh = split[1].Split('x', 2);
                    if (wh.Length == 2
                        && double.TryParse(wh[0], NumberStyles.Float, c, out var w)
                        && double.TryParse(wh[1], NumberStyles.Float, c, out var h))
                    {
                        (width, height) = (w, h);
                    }
                    break;
                case "zoom":
                    double.TryParse(split[1], NumberStyles.Float, c, out zoom);
                    break;
                case "wrapped":
                    wrapped = split[1] == "true";
                    break;
            }
        }
        return (width, height, zoom, wrapped);
    }

    internal static Capture ReadFile(string path)
    {
        using var reader = new StreamReader(path);
        return Read(reader);
    }
}
