using System.Diagnostics;

namespace Lightbox.App.Services;

/// <summary>
/// What happened to the layer selection, click by click, while an input trace
/// is armed — for the report that Ctrl+click and Shift+click in the Layers
/// docker pick one layer at a time on the owner's machine.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an instrument and not a fix.</b> On the same commit every route the
/// headless suite can drive works: Ctrl and Shift on the row body, the eye and
/// the reorder arrows, with and without a real key press around the click. So
/// what collapses the selection is something present in the owner's window and
/// absent from the test's, and a fix written now would be a guess. Q115 made
/// the same call for the pen problems: spend the one round-trip identifying the
/// mechanism.
/// </para>
/// <para>
/// <b>What it records.</b> Each docker press — which element it landed on,
/// which modifiers and pointer type arrived, which handler took it — each
/// selection change, and every reset of the selection to the active layer
/// alone, with the methods that asked for it. The reset is the suspect: it is
/// the one path that turns a selection of several into one.
/// </para>
/// <para>
/// <b>Counts and positions, never names.</b> The log is written to be attached
/// to a bug report (<see cref="DiagnosticLog"/>), so a layer is its row index.
/// Disarmed, every call is the one volatile read of <see cref="InputTrace.Armed"/>.
/// </para>
/// </remarks>
internal static class LayerSelectionTrace
{
    public static bool On => InputTrace.Armed;

    public static void Note(string what)
    {
        if (!InputTrace.Armed) return;
        DiagnosticLog.WriteNote("layer-selection", what);
    }

    /// <summary>The methods that led here, innermost first, without this one.</summary>
    public static string Callers(int depth = 8)
    {
        try
        {
            var frames = new StackTrace(2, fNeedFileInfo: false).GetFrames();
            return string.Join(" <- ", frames
                .Take(depth)
                .Select(f => f.GetMethod() is { } m ? $"{m.DeclaringType?.Name}.{m.Name}" : "?"));
        }
        catch
        {
            return "?";
        }
    }
}
