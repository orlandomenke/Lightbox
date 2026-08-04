using Lightbox.Core.Documents;

namespace Lightbox.App.Services;

/// <summary>How far an import has got, for a progress bar.</summary>
/// <param name="Done">Files finished, including the ones that could not be read.</param>
/// <param name="Total">Files handed over.</param>
/// <param name="File">The file being read now — the part worth showing, because a
/// collection is a folder of names and "which one is it on" is the question.</param>
/// <param name="Found">Brushes produced so far. One <c>.abr</c> can hold dozens.</param>
public sealed record BrushImportProgress(int Done, int Total, string File, int Found)
{
    /// <summary>A fraction for a bar, 0–1. Zero files is complete rather than undefined.</summary>
    public double Fraction => Total <= 0 ? 1 : Math.Clamp((double)Done / Total, 0, 1);
}

/// <summary>What an import produced, including what it could not read.</summary>
/// <remarks>
/// <b>Unreadable files are named, not counted.</b> "3 files could not be read" out of
/// fifty-six is unactionable; the names tell an artist which ones to look at, and a
/// collection usually fails in a pattern — one format, one folder, one bad download.
/// </remarks>
public sealed record BrushImportOutcome(List<BrushPreset> Presets, List<string> Unreadable)
{
    public static BrushImportOutcome Empty => new([], []);
}

/// <summary>
/// Reading brush files into presets — the whole of the expensive part, with no UI in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separated out because it was reported as a crash.</b> Importing a fifty-six brush
/// collection ran on the UI thread: every <c>.abr</c> parsed, every tip decoded and
/// re-encoded, with no pump in between, so the window stopped answering the compositor and
/// went transparent — which reads exactly like an application about to die. Nothing was
/// wrong except where the work was happening.
/// </para>
/// <para>
/// So this is a pure function over bytes: no view model, no <c>ObservableCollection</c>, no
/// dispatcher. It can be called from a background thread, it reports progress per file, and
/// it can be given up on halfway. The caller does the two things that must be on the UI
/// thread — adding to the bound list and saving — once, at the end.
/// </para>
/// <para>
/// <b>One save, not one per brush.</b> The old path persisted the whole store after each
/// preset, so a fifty-six brush import wrote the file fifty-six times, each write larger
/// than the last. That is quadratic in the size of the collection and it is why the last
/// few brushes of a big import were the slowest.
/// </para>
/// </remarks>
public static class BrushImportJob
{
    /// <summary>The formats an artist can hand over, for the file picker and the manual.</summary>
    public static readonly string[] Patterns = ["*.abr", "*.gbr", "*.gih", "*.kpp"];

    /// <summary>
    /// Turn brush files into presets, reporting progress and stopping if asked.
    /// </summary>
    /// <remarks>
    /// A file that cannot be read is recorded and skipped. An import of fifty brushes where
    /// three are corrupt must produce forty-seven brushes, not an exception — the artist
    /// chose a folder, not fifty individual files, and refusing the lot because of one is
    /// a worse answer than saying which one.
    /// </remarks>
    public static BrushImportOutcome Read(
        IReadOnlyList<(string Name, byte[] Bytes)> files,
        IProgress<BrushImportProgress>? progress = null,
        CancellationToken cancel = default)
    {
        var presets = new List<BrushPreset>();
        var unreadable = new List<string>();

        progress?.Report(new BrushImportProgress(0, files.Count, files.Count > 0 ? files[0].Name : "", 0));

        for (var i = 0; i < files.Count; i++)
        {
            if (cancel.IsCancellationRequested) break;
            var (name, bytes) = files[i];

            try
            {
                foreach (var imported in Lightbox.Import.BrushImport.Read(name, bytes))
                {
                    presets.Add(PresetFor(imported));
                }
            }
            catch
            {
                unreadable.Add(name);
            }

            progress?.Report(new BrushImportProgress(i + 1, files.Count, name, presets.Count));
        }

        return new BrushImportOutcome(presets, unreadable);
    }

    /// <summary>
    /// One imported brush as a preset.
    /// </summary>
    /// <remarks>
    /// The hardness fallback is the interesting line: a brush that brought its own tip has
    /// its shape in the tip's alpha, so softening it again would blur a mask somebody already
    /// drew. A brush that brought no tip is a plain round dab, and 0.8 is a believable edge
    /// for one — the formats do not carry hardness in any form we can read.
    /// </remarks>
    private static BrushPreset PresetFor(Lightbox.Import.ImportedBrush imported) => new()
    {
        Name = imported.Name,
        Tool = ToolKind.Brush,
        TipPng = imported.TipPngBase64,
        Settings = new BrushSettings
        {
            Size = Math.Clamp(imported.SizePx, 1, 500),
            Spacing = imported.Spacing,
            Opacity = imported.Opacity,
            Flow = imported.Flow,
            Hardness = imported.TipPngBase64 is null ? 0.8 : 1,
            TipId = imported.TipPngBase64 is null ? null : Ids.NewId("tip"),
        },
    };

    /// <summary>What to tell the artist when it is done.</summary>
    /// <remarks>
    /// In the outcome's own file rather than in the window, so the wording is the same
    /// wherever an import is started from and a test can read it.
    /// </remarks>
    public static string Summarise(BrushImportOutcome outcome)
    {
        var added = outcome.Presets.Count;
        var brushes = added == 1 ? "1 brush" : $"{added} brushes";
        if (outcome.Unreadable.Count == 0)
        {
            return added == 0 ? "Nothing to import — no brushes were found in those files." : $"Imported {brushes}.";
        }

        // Named, and capped: a folder of thirty bad files should not produce a paragraph.
        var named = string.Join(", ", outcome.Unreadable.Take(3));
        var rest = outcome.Unreadable.Count - 3;
        var tail = rest > 0 ? $", and {rest} more" : "";
        return $"Imported {brushes}. Could not read {named}{tail}.";
    }
}
