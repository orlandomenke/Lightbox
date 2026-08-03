using Lightbox.Core.Documents;
using Lightbox.Core.Export;

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
    public static ExportRun Run(Doc doc, ExportPreset preset, string path)
    {
        return preset.Target switch
        {
            ExportTarget.PngSequence => Sequence(doc, path),
            ExportTarget.Unity => Unity(doc, preset, path),
            ExportTarget.Godot => Godot(doc, preset, path),
            ExportTarget.Unreal => Unreal(doc, preset, path),
            _ => Sheet(doc, preset, path),
        };
    }

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

    private static ExportRun Sheet(Doc doc, ExportPreset preset, string sheetPath)
    {
        var result = SpriteSheetExporter.Export(doc, sheetPath, SheetOptions(preset));
        var files = new List<string> { result.SheetPath, result.MetadataPath };
        var summary = SummaryOf(result);

        if (preset.NormalMap)
        {
            files.Add(NormalMapWriter.Write(result.SheetPath, preset.Normal));
            summary += ", + normal map";
        }

        return new ExportRun(files, summary, result.OmittedLayers, result.SuspectedBackgrounds);
    }

    private static ExportRun Unity(Doc doc, ExportPreset preset, string sheetPath)
    {
        var result = UnityExporter.Export(doc, sheetPath, new UnityExportOptions(
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

    private static ExportRun Godot(Doc doc, ExportPreset preset, string sheetPath)
    {
        var result = GodotExporter.Export(doc, sheetPath, new GodotExportOptions(preset.WriteImporter)
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

    private static ExportRun Unreal(Doc doc, ExportPreset preset, string sheetPath)
    {
        var result = UnrealExporter.Export(doc, sheetPath, new UnrealExportOptions(preset.WriteImporter)
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

    private static string SummaryOf(SpriteSheetResult result)
    {
        var layout = result.Pack == SpritePack.Skyline
            ? $"packed, {result.Occupancy:P0} used"
            : $"{result.Columns}x{result.Rows} grid";
        return $"{result.FrameCount} frame(s), {result.SheetWidth}x{result.SheetHeight} px, {layout}";
    }
}
