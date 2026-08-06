using Lightbox.Core.Documents;
using Lightbox.Core.Export;
using Lightbox.Core.Projects;

namespace Lightbox.App.Services;

/// <param name="Files">Everything written, in the order it was written.</param>
/// <param name="Summary">
/// One line for a person: what came out and how big. Shown in the status line, so it
/// says the numbers rather than "Export complete".
/// </param>
/// <param name="Omitted">
/// Layers the export left out, with a reason each. Empty for a PNG sequence, which
/// leaves nothing out.
/// </param>
/// <param name="Suspected">Layers kept that might not have been meant to be.</param>
public sealed record ExportRun(
    IReadOnlyList<string> Files,
    string Summary,
    IReadOnlyList<OmittedLayer> Omitted,
    IReadOnlyList<SuspectedBackground> Suspected);

/// <summary>
/// Running an <see cref="ExportPreset"/>. The single place a target becomes files.
/// </summary>
/// <remarks>
/// <para>
/// Pillar 5's last mile, and it is deliberately thin: every exporter it calls already
/// existed and was already tested. What did not exist was <b>one entry point</b>, and
/// the consequence of not having one was that none of the pillar was reachable from
/// the application at all — the sheet writer, the packer, the sidecar, the anchors,
/// the collision rectangles and the Unity importer were callable from an agent over
/// MCP and from tests, and from nowhere an artist could press.
/// </para>
/// <para>
/// So this holds the mapping and nothing else. No rendering, no layout, no format
/// knowledge: a preset in, files and a report out. Which means the interesting
/// behaviour stays where its tests are, and this can be checked by asserting which
/// files appeared.
/// </para>
/// </remarks>
public static class ExportRunner
{
    /// <summary>
    /// Run a preset. <paramref name="path"/> is the sheet file, or the folder for a
    /// PNG sequence.
    /// </summary>
    public static ExportRun Run(Doc doc, ExportPreset preset, string path) =>
        Run([doc], preset, path);

    /// <summary>
    /// Run a preset over the documents one artifact holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q30.</b> <see cref="ExportPlan"/> has always been able to describe an
    /// artifact holding forty documents; this is where one becomes files. The
    /// single-document signature delegates here, so the grouped and ungrouped
    /// paths are the same code rather than a branch around each other.
    /// </para>
    /// <para>
    /// <b>A PNG sequence of several documents is refused, not flattened.</b> A
    /// sequence writes numbered frames into a folder, so concatenating two
    /// documents would silently interleave two animations into one run of
    /// numbers with nothing in the output saying where one ended. A sheet has
    /// frame tags to say so; a folder of PNGs has nothing.
    /// </para>
    /// </remarks>
    public static ExportRun Run(
        IReadOnlyList<Doc> docs, ExportPreset preset, string path,
        IReadOnlyList<string>? names = null)
    {
        if (docs.Count == 0) throw new ArgumentException("An export needs a document.", nameof(docs));

        return preset.Target switch
        {
            ExportTarget.PngSequence when docs.Count > 1 => Refused(
                "A PNG sequence is one animation's frames in a folder, so it cannot hold "
                + $"{docs.Count} documents. Use a sheet, or export per document."),
            ExportTarget.PngSequence => Sequence(docs[0], path),
            // A GameMaker sprite is one animation: one strip, one origin, one
            // image speed. Several documents are several sprites, not one
            // artifact, and packing them into a strip would give the second
            // cycle the first one's origin and speed with nothing in the file
            // saying so.
            ExportTarget.GameMaker when docs.Count > 1 => Refused(
                $"A GameMaker sprite is one animation with one origin and one speed, so {docs.Count} "
                + "documents cannot be one of them. Export per document."),
            ExportTarget.Unity => Unity(docs, preset, path),
            ExportTarget.Godot => Godot(docs, preset, path),
            ExportTarget.Unreal => Unreal(docs, preset, path),
            ExportTarget.GameMaker => GameMaker(docs[0], preset, path),
            _ => Sheet(docs, preset, path, names),
        };
    }

    /// <summary>
    /// An export that did not happen, and says why in the place the summary goes.
    /// </summary>
    /// <remarks>
    /// A refusal rather than an exception: one artifact in a plan of nine failing
    /// must not take the other eight with it, and the artist needs to be told
    /// which one and why. No files, so nothing downstream mistakes it for a
    /// success with an empty result.
    /// </remarks>
    private static ExportRun Refused(string why) => new([], why, [], []);

    private static ExportRun Sequence(Doc doc, string directory)
    {
        var written = SequenceExporter.ExportPngSequence(doc, directory);
        return new ExportRun(
            written,
            $"{written.Count} PNG frame(s) → {Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar))}",
            // A sequence composes the whole scene, paper included, and always has.
            // Reporting an empty list is the honest answer rather than pretending
            // background handling applied.
            [],
            []);
    }

    private static SpriteSheetOptions SheetOptions(ExportPreset preset) => new()
    {
        Trim = preset.Trim,
        Pack = preset.Pack,
        Columns = preset.Columns,
        Padding = preset.Padding,
        Background = preset.Background,
    };

    private static ExportRun Sheet(
        IReadOnlyList<Doc> docs, ExportPreset preset, string sheetPath, IReadOnlyList<string>? names)
    {
        var result = SpriteSheetExporter.Export(docs, sheetPath, SheetOptions(preset), names);
        var files = new List<string> { result.SheetPath, result.MetadataPath };
        var summary = SummaryOf(result);

        if (preset.NormalMap)
        {
            files.Add(NormalMapWriter.Write(result.SheetPath, preset.Normal));
            summary += ", + normal map";
        }

        return new ExportRun(files, summary, result.OmittedLayers, result.SuspectedBackgrounds);
    }

    private static ExportRun Unity(IReadOnlyList<Doc> docs, ExportPreset preset, string sheetPath)
    {
        var result = UnityExporter.Export(docs, sheetPath, new UnityExportOptions(
            preset.WorldHeightUnits, preset.WriteImporter)
        {
            Sheet = SheetOptions(preset),
        });

        var files = new List<string> { result.SheetPath, result.MetadataPath };
        if (result.ImporterPath is { } importer) files.Add(importer);
        if (preset.NormalMap) files.Add(NormalMapWriter.Write(result.SheetPath, preset.Normal));

        // The report comes off the sheet result the Unity export already produced.
        // Exporting again to get it would rewrite the sidecar and strip the Unity
        // block back off — which is why UnityExportResult carries the sheet.
        return new ExportRun(
            files,
            $"{result.SpriteCount} sprite(s), {result.ClipCount} clip(s) for Unity → "
            + Path.GetFileName(result.SheetPath),
            result.Sheet?.OmittedLayers ?? [],
            result.Sheet?.SuspectedBackgrounds ?? []);
    }

    private static ExportRun Godot(IReadOnlyList<Doc> docs, ExportPreset preset, string sheetPath)
    {
        var result = GodotExporter.Export(docs, sheetPath, new GodotExportOptions(preset.WriteImporter)
        {
            Sheet = SheetOptions(preset),
        });

        var files = new List<string> { result.SheetPath, result.MetadataPath };
        if (result.ImporterPath is { } importer) files.Add(importer);
        if (preset.NormalMap) files.Add(NormalMapWriter.Write(result.SheetPath, preset.Normal));

        return new ExportRun(
            files,
            $"{result.SpriteCount} sprite(s), {result.ClipCount} animation(s) for Godot → "
            + Path.GetFileName(result.SheetPath),
            result.Sheet?.OmittedLayers ?? [],
            result.Sheet?.SuspectedBackgrounds ?? []);
    }

    private static ExportRun Unreal(IReadOnlyList<Doc> docs, ExportPreset preset, string sheetPath)
    {
        var result = UnrealExporter.Export(docs, sheetPath, new UnrealExportOptions(preset.WriteImporter)
        {
            Sheet = SheetOptions(preset),
            WorldHeightUnits = preset.WorldHeightUnits,
        });

        var files = new List<string> { result.SheetPath, result.MetadataPath };
        if (result.ImporterPath is { } importer) files.Add(importer);
        if (preset.NormalMap) files.Add(NormalMapWriter.Write(result.SheetPath, preset.Normal));

        return new ExportRun(
            files,
            $"{result.SpriteCount} sprite(s), {result.FlipbookCount} flipbook(s) for Unreal → "
            + Path.GetFileName(result.SheetPath),
            result.Sheet?.OmittedLayers ?? [],
            result.Sheet?.SuspectedBackgrounds ?? []);
    }

    private static ExportRun GameMaker(Doc doc, ExportPreset preset, string sheetPath)
    {
        var result = GameMakerExporter.Export(doc, sheetPath, new GameMakerExportOptions
        {
            Sheet = SheetOptions(preset),
        });

        var files = new List<string>(result.StripPaths) { result.MetadataPath };
        if (preset.NormalMap && result.StripPaths.Count > 0)
        {
            // One map per strip: the strips are separate images, so a single map could
            // only line up with one of them.
            files.AddRange(result.StripPaths.Select(s => NormalMapWriter.Write(s, preset.Normal)));
        }

        var summary =
            $"{result.FrameCount} frame(s) in {result.StripPaths.Count} strip(s) for GameMaker → "
            + string.Join(", ", result.StripPaths.Select(Path.GetFileName));

        // The notes go in the summary rather than only in the file, because "your
        // ping-pong tag will not ping-pong" is worth reading before you import.
        if (result.Notes.Count > 0) summary += " — " + string.Join(" ", result.Notes);

        return new ExportRun(
            files, summary,
            result.Sheet?.OmittedLayers ?? [],
            result.Sheet?.SuspectedBackgrounds ?? []);
    }

    private static string SummaryOf(SpriteSheetResult result)
    {
        var layout = result.Pack == SpritePack.Skyline
            ? $"packed, {result.Occupancy:P0} used"
            : $"{result.Columns}x{result.Rows} grid";
        return $"{result.FrameCount} frame(s), {result.SheetWidth}x{result.SheetHeight} px, {layout}";
    }
}
