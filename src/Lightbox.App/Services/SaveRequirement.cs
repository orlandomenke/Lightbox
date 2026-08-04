namespace Lightbox.App.Services;

/// <summary>What has to happen before an action that depends on the file on disc.</summary>
public enum SaveGate
{
    /// <summary>There is a file and it holds the current edits. Go ahead.</summary>
    Ready,

    /// <summary>There is a file; write the pending edits into it first.</summary>
    SaveInPlaceFirst,

    /// <summary>Never saved. Ask where it goes, and do not proceed if refused.</summary>
    AskWhereItGoes,

    /// <summary>There was a file and it is not there any more. Ask again rather than guess.</summary>
    FileHasVanished,
}

/// <summary>
/// Whether an action that hands the document to something else may go ahead yet.
/// </summary>
/// <remarks>
/// <para>
/// <b>Exporting, and marking an asset Ready, are both claims about a file.</b> An export
/// says "this is what the drawing looks like"; a status says "this drawing is finished, go
/// and use it". Both are made *to somebody else* — a game engine, a designer waiting on the
/// asset — and both are worthless if the drawing they describe was never written down.
/// </para>
/// <para>
/// So they share one gate, and it is a pure decision over three facts: is there a path, is
/// there a file at it, and are there edits the file does not have. Pure so it can be
/// tested without a window, and shared so export and status cannot drift into disagreeing
/// about what "saved" means.
/// </para>
/// <para>
/// <b>The order matters and is the load-bearing part.</b> The status is written first and
/// the export follows as a consequence — that is <c>AutoExport</c>'s own rule, so a failed
/// export never costs the artist their status. This gate sits <em>before</em> both: it is
/// the question of whether the status change should be allowed to happen at all, which is
/// different from whether the export that follows it worked.
/// </para>
/// </remarks>
public static class SaveRequirement
{
    /// <summary>
    /// What the artist has to do first, if anything.
    /// </summary>
    /// <param name="filePath">Where the document was last written, or null.</param>
    /// <param name="hasUnsavedEdits">Whether the document has changed since then.</param>
    /// <param name="fileExists">
    /// Test seam. Defaults to the real filesystem; a test supplies its own rather than
    /// creating files to describe a state.
    /// </param>
    public static SaveGate For(
        string? filePath, bool hasUnsavedEdits, Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return SaveGate.AskWhereItGoes;

        var exists = fileExists ?? File.Exists;
        // A recorded path whose file is gone is NOT the same as never having saved, and it
        // is worth keeping apart: the artist moved or deleted it, so guessing that the old
        // path is still where they want it would write the file back somewhere they
        // deliberately emptied. Ask, and say why.
        if (!exists(filePath)) return SaveGate.FileHasVanished;

        return hasUnsavedEdits ? SaveGate.SaveInPlaceFirst : SaveGate.Ready;
    }

    /// <summary>Whether this gate cannot be settled without asking the artist.</summary>
    /// <remarks>
    /// <see cref="SaveGate.SaveInPlaceFirst"/> is deliberately <em>not</em> blocking: the
    /// document already has a home, so writing to it needs no permission and asking would
    /// be a dialog in the way of an artist who has already told us where the file goes.
    /// </remarks>
    public static bool NeedsTheArtist(SaveGate gate) =>
        gate is SaveGate.AskWhereItGoes or SaveGate.FileHasVanished;

    /// <summary>
    /// Why the artist is being asked, in terms of the thing they were trying to do.
    /// </summary>
    /// <param name="action">
    /// What was attempted, as a phrase that fits after "before" — "exporting",
    /// "marking this Ready".
    /// </param>
    public static string Explain(SaveGate gate, string action) => gate switch
    {
        SaveGate.AskWhereItGoes =>
            $"This drawing has not been saved yet, so there is nothing on disk to match. "
            + $"Save it before {action}.",
        SaveGate.FileHasVanished =>
            $"The file this drawing was saved to is no longer there. Save it again before "
            + $"{action}.",
        SaveGate.SaveInPlaceFirst =>
            $"Saving the open changes before {action}.",
        _ => "",
    };
}
