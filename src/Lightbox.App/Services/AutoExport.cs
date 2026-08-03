using Lightbox.Core.Projects;

namespace Lightbox.App.Services;

/// <summary>
/// The configuration for exporting on a status change.
/// </summary>
/// <remarks>
/// <para>
/// General configuration rather than per-document, because it describes <em>how this
/// studio ships</em>: one output folder the engine watches, one preset, one status
/// that means "this is done". Lives on <see cref="AppSettings"/> beside the autosave
/// interval, which is the closest existing thing — a background action an artist
/// switches on once and then stops thinking about.
/// </para>
/// <para>
/// <b>Off by default, and that is not timidity.</b> This writes files into somebody
/// else's project on a UI click. Turning it on is consent; defaulting it on would
/// mean the first artist to try the status field silently overwrote whatever was in
/// their game's asset folder.
/// </para>
/// </remarks>
public sealed class AutoExportSettings
{
    public bool Enabled { get; set; }

    /// <summary>
    /// The status that fires an export.
    /// </summary>
    /// <remarks>
    /// Configurable rather than hard-wired to Ready, because a studio that reviews in
    /// engine wants it on <see cref="AssetStatus.Review"/> — the whole point of the
    /// feature is that the engine sees the asset, and when that should happen is a
    /// question about their pipeline rather than about ours.
    /// </remarks>
    public AssetStatus Trigger { get; set; } = AssetStatus.Ready;

    /// <summary>The preset to export with, by name. Null uses the first built-in.</summary>
    public string? PresetName { get; set; }

    /// <summary>
    /// Where the files go. Absolute, or relative to the project root.
    /// </summary>
    /// <remarks>
    /// Relative is allowed and is the useful case for a project that lives beside its
    /// game — <c>../Game/Assets/Sprites</c> keeps the whole thing portable and
    /// diffable, which is the same reasoning the project's folder-of-JSON layout
    /// follows.
    /// </remarks>
    public string? OutputFolder { get; set; }
}

/// <summary>Why an auto-export did not happen, or that it did.</summary>
public enum AutoExportOutcome
{
    Exported,
    Disabled,

    /// <summary>The status changed, but not to the one that fires.</summary>
    NotTheTrigger,

    /// <summary>Nothing changed — the status was already that.</summary>
    Unchanged,

    /// <summary>No output folder configured, so there is nowhere to put it.</summary>
    NoDestination,

    /// <summary>The export was attempted and threw.</summary>
    Failed,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Run">The export, when there was one.</param>
/// <param name="Message">
/// One line for the status bar. Always set, including on the skips — a feature that
/// silently does nothing is indistinguishable from a broken one.
/// </param>
public sealed record AutoExportReport(
    AutoExportOutcome Outcome,
    ExportRun? Run = null,
    string Message = "");

/// <summary>
/// Exporting because a document was marked done.
/// </summary>
/// <remarks>
/// <para>
/// The shortcut through the whole pillar: finish an asset, set it Ready, and the sheet
/// and its sidecar land where the engine is already looking. Nobody has to remember to
/// export, which is the failure this removes — the export that did not happen is the
/// one that makes a designer think the artist has not started.
/// </para>
/// <para>
/// <b>The load-bearing rule: the status change is authoritative and the export is a
/// consequence.</b> Setting Ready always succeeds and is always saved. The export is
/// then attempted, and if the destination is missing, locked by the engine, or on a
/// drive that is not mounted, the artist gets a message and keeps their status. The
/// reverse — refusing the status change because a file could not be written — would
/// make a production field hostage to a network share.
/// </para>
/// <para>
/// <see cref="Decide"/> holds the whole decision and touches no disk, so every branch
/// is testable without a project on disk or a game engine to write into.
/// </para>
/// </remarks>
public static class AutoExport
{
    /// <summary>
    /// Whether this status change should export, and where to.
    /// </summary>
    /// <param name="before">The status before the change, possibly null.</param>
    /// <param name="after">The status now.</param>
    /// <param name="settings">The configuration.</param>
    /// <param name="projectRoot">The project's folder, for a relative destination.</param>
    /// <returns>
    /// The folder to export into, or null with the reason it is not happening.
    /// </returns>
    public static (string? Folder, AutoExportOutcome Outcome) Decide(
        AssetStatus? before, AssetStatus after, AutoExportSettings settings, string? projectRoot)
    {
        if (!settings.Enabled) return (null, AutoExportOutcome.Disabled);

        // Re-selecting the status something already has is not a gesture. Firing on it
        // would make every click on the current status re-write the engine's files, and
        // an artist inspecting the dropdown would trigger an export.
        if (before == after) return (null, AutoExportOutcome.Unchanged);

        if (after != settings.Trigger) return (null, AutoExportOutcome.NotTheTrigger);

        if (string.IsNullOrWhiteSpace(settings.OutputFolder))
            return (null, AutoExportOutcome.NoDestination);

        var folder = settings.OutputFolder!;
        if (!Path.IsPathRooted(folder))
        {
            // Relative to the project, which is the portable case. With no project
            // there is no root to resolve against, and guessing the working directory
            // would write files somewhere nobody chose.
            if (string.IsNullOrWhiteSpace(projectRoot)) return (null, AutoExportOutcome.NoDestination);
            folder = Path.GetFullPath(Path.Combine(projectRoot!, folder));
        }
        return (folder, AutoExportOutcome.Exported);
    }

    /// <summary>
    /// A document's file name inside the output folder, without an extension.
    /// </summary>
    /// <remarks>
    /// From the document's name rather than from its path, because the name is what the
    /// artist reads in the panel and what a designer will look for in the engine.
    /// Characters a file system will not take are replaced rather than dropped, so two
    /// documents cannot collapse onto one name by losing different punctuation.
    /// </remarks>
    public static string FileNameFor(string documentName)
    {
        var name = string.IsNullOrWhiteSpace(documentName) ? "asset" : documentName.Trim();
        foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        return name;
    }

    /// <summary>
    /// Run the export for a status change, or explain why there was not one.
    /// </summary>
    /// <remarks>
    /// Never throws. A failure is a message, because the caller has already committed
    /// the status change and must not be made to undo it.
    /// </remarks>
    public static AutoExportReport Run(
        Lightbox.Core.Documents.Doc doc,
        string documentName,
        AssetStatus? before,
        AssetStatus after,
        AutoExportSettings settings,
        string? projectRoot,
        IReadOnlyList<ExportPreset> presets)
    {
        var (folder, outcome) = Decide(before, after, settings, projectRoot);
        if (folder is null)
        {
            return new AutoExportReport(outcome, null, Explain(outcome, settings));
        }

        var preset = presets.FirstOrDefault(p => p.Name == settings.PresetName)
                     ?? ExportPreset.BuiltIns[0];

        try
        {
            Directory.CreateDirectory(folder);
            var name = FileNameFor(documentName);
            var path = preset.Target == ExportTarget.PngSequence
                ? Path.Combine(folder, name)
                : Path.Combine(folder, name + ".png");

            var run = ExportRunner.Run(doc, preset, path);
            return new AutoExportReport(
                AutoExportOutcome.Exported, run,
                $"Auto-exported on {AssetStatuses.Label(after)} — {run.Summary}");
        }
        catch (Exception ex)
        {
            // The status is already set and saved. This is the whole reason the two are
            // ordered that way round.
            return new AutoExportReport(
                AutoExportOutcome.Failed, null,
                $"Status saved, but the auto-export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Why nothing happened, in one line, or empty when it is not worth saying.
    /// </summary>
    /// <remarks>
    /// Public so a caller can explain a skip without loading a document just to be told
    /// there was nothing to do.
    /// </remarks>
    public static string Explain(AutoExportOutcome outcome, AutoExportSettings settings) =>
        outcome switch
        {
            AutoExportOutcome.NoDestination =>
                "Auto-export is on but has no output folder — set one under Configure ▸ Export.",
            AutoExportOutcome.NotTheTrigger =>
                $"Auto-export fires on {AssetStatuses.Label(settings.Trigger)}.",
            // Disabled and Unchanged are the ordinary quiet cases: an artist who has
            // not switched this on should not be told about it on every status change.
            _ => "",
        };
}
