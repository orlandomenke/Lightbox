using Lightbox.Core.Documents;
using Lightbox.Core.Export;

namespace Lightbox.App.Services;

public sealed record DragonBonesExportResult(string SkeletonPath, string RigPath, int BoneCount, int FrameCount);

/// <summary>
/// Export for DragonBones: the skeleton file (<c>*_ske.json</c>) and our own
/// rig JSON beside it — phase 6 of <c>docs/DESIGN-bones.md</c>.
/// </summary>
/// <remarks>
/// Both files, on purpose: the DragonBones file is the reach into engines
/// with existing importers, and the rig JSON is the source of truth it was
/// converted from — an importer that mangles something (the design doc's own
/// warning is that DragonBones importers vary in quality) leaves the artist
/// holding the file that says what the rig actually is.
/// </remarks>
public static class DragonBonesExporter
{
    /// <summary>
    /// Write the rig to <paramref name="path"/>'s directory: the base name
    /// carries <c>_ske.json</c> (DragonBones' convention) and <c>_rig.json</c>.
    /// </summary>
    public static DragonBonesExportResult Export(Doc doc, string path)
    {
        if (!RigExport.HasRig(doc))
            throw new InvalidOperationException("The document has no rig to export.");

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var stem = Path.GetFileNameWithoutExtension(path);
        // The DragonBones convention doubles suffixes when a caller passes a
        // path that already carries one; strip ours so `tail_ske.json` in
        // does not become `tail_ske_ske.json` out.
        foreach (var suffix in new[] { "_ske", "_rig" })
            if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                stem = stem[..^suffix.Length];

        var rig = RigExport.Build(doc);
        var rigPath = Path.Combine(directory, stem + "_rig.json");
        var skePath = Path.Combine(directory, stem + "_ske.json");
        File.WriteAllText(rigPath, RigExport.ToJson(rig));
        File.WriteAllText(skePath, DragonBonesConvert.Convert(rig, stem));
        return new DragonBonesExportResult(skePath, rigPath, rig.Bones.Count, rig.FrameCount);
    }
}
