using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The lines an artist copied, held between documents for the length of the
/// session.
/// </summary>
/// <remarks>
/// <para>
/// <b>Static, and that is the feature rather than a shortcut.</b> Copying a
/// character out of one shot and into another is the reason to have this at
/// all, and a clipboard owned by a document could not do it. It is not the
/// operating system's clipboard: nothing leaves the app, so there is no wire
/// format to promise anybody and no way for a half-written stroke record to
/// arrive from outside (Q-copy-paste, recorded with the alternatives).
/// </para>
/// <para>
/// <b>What it holds is a copy, and what it hands back is another copy.</b> The
/// strokes are cloned on the way in, so editing or deleting the originals
/// afterwards cannot reach into the clipboard, and cloned again on the way out,
/// so two pastes are two independent drawings rather than one shared list. Ids
/// are fresh per paste for the same reason — the record refers to strokes by
/// id, and pasting the same line twice into one document must not produce two
/// entries claiming to be the same line.
/// </para>
/// <para>
/// <b>The clip regions travel with the strokes.</b> A partial copy carries its
/// selection as a clip (invariant 3), and a clip is only meaningful if it can
/// be resolved — so the regions are carried here by value and re-registered
/// into the target document on every paste. Carrying only the id would work
/// inside one session and produce strokes clipped by nothing the moment the
/// pasted document was saved and reopened somewhere else.
/// </para>
/// </remarks>
internal static class StrokeClipboard
{
    /// <summary>One copy: the lines, and the clips they are carved by.</summary>
    internal sealed record Payload(
        IReadOnlyList<Stroke> Strokes, IReadOnlyDictionary<string, ClipRegion> Clips);

    private static Payload? _held;
    private static long _order;

    internal static bool HasContent => _held is { Strokes.Count: > 0 };

    /// <summary>
    /// When this clipboard was last filled, on a counter it shares with the
    /// cel clipboard.
    /// </summary>
    /// <remarks>
    /// <b>Because Ctrl+V has to answer for two clipboards.</b> Lines and cels
    /// are copied by different gestures and kept in different places, and an
    /// artist who has used both does not think of them as two — they think
    /// "the last thing I copied". Comparing stamps is what makes the key mean
    /// that. Asking "are there lines?" first would paste a line copied ten
    /// minutes ago over the cel copied a second ago.
    /// </remarks>
    internal static long Stamp { get; private set; }

    /// <summary>The next value on the shared order, for the cel clipboard to stamp itself with.</summary>
    internal static long NextOrder() => ++_order;

    /// <summary>How many lines are held, for the status line.</summary>
    internal static int Count => _held?.Strokes.Count ?? 0;

    /// <summary>
    /// Take a copy of these strokes, with every clip they reference.
    /// </summary>
    internal static void Put(IEnumerable<Stroke> strokes, Func<string, ClipRegion?> resolve)
    {
        var copies = new List<Stroke>();
        var clips = new Dictionary<string, ClipRegion>();
        foreach (var stroke in strokes)
        {
            copies.Add(stroke.Clone(newId: false));
            if (stroke.ClipId is not { } id || clips.ContainsKey(id)) continue;
            if (resolve(id) is { } region) clips[id] = region.Clone();
        }
        _held = new Payload(copies, clips);
        Stamp = NextOrder();
    }

    /// <summary>
    /// A fresh copy of what was held — new stroke ids, and the clips to
    /// register before rendering them — or null when nothing has been copied.
    /// </summary>
    internal static Payload? Take()
    {
        if (_held is not { Strokes.Count: > 0 } held) return null;
        return held with
        {
            Strokes = [.. held.Strokes.Select(s => s.Clone())],
            Clips = held.Clips.ToDictionary(e => e.Key, e => e.Value.Clone()),
        };
    }

    /// <summary>Forget everything held. For tests, which must not leak into each other.</summary>
    internal static void Clear()
    {
        _held = null;
        Stamp = 0;
    }
}
