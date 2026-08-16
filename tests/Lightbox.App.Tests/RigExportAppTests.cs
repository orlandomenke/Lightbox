using System.Text.Json;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Rig export through the app: the Godot export carries the rig beside the
/// sheet when there is one (and nothing when there is not), the DragonBones
/// target writes the skeleton file, and the refusals say why in a sentence.
/// </summary>
public class RigExportAppTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-rig-export-" + Guid.NewGuid().ToString("N"));

    public RigExportAppTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string At(string name) => Path.Combine(_dir, name);

    private static Doc Drawn(bool rigged)
    {
        var doc = DocumentFactory.CreateDoc(200, 120, 12, null);
        doc.Scene.FrameCount = 2;
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        while (layer.Cels.Count < 2) layer.Cels.Add(new Cel { Frame = new Frame() });
        foreach (var cel in layer.Cels)
        {
            (cel.Frame ??= new Frame()).Strokes.Add(new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#c02040",
                Points =
                [
                    new StrokePoint(40, 30, 1), new StrokePoint(70, 30, 1),
                    new StrokePoint(70, 90, 1), new StrokePoint(40, 90, 1),
                ],
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            });
        }

        if (rigged)
        {
            var root = new Bone { Name = "spine", X = 50, Y = 60, Length = 30 };
            doc.Armature = new Armature { Bones = [root] };
            doc.Scene.PoseTrack = new PoseTrack
            {
                Keys = [new PoseKey { Frame = 1, Bones = { [root.Id] = new BonePose { RotationDeg = 30 } } }],
            };
        }
        return doc;
    }

    [Fact]
    public void TheGodotExportCarriesTheRigBesideTheSheet()
    {
        var result = GodotExporter.Export(Drawn(rigged: true), At("hero.png"));

        var rigPath = Assert.Single(result.RigPaths);
        Assert.EndsWith("hero_rig.json", rigPath);
        Assert.NotNull(result.RigImporterPath);

        var rig = JsonDocument.Parse(File.ReadAllText(rigPath)).RootElement;
        Assert.Equal(1, rig.GetProperty("lightboxRig").GetInt32());
        Assert.Equal("spine", rig.GetProperty("bones").EnumerateArray().Single()
            .GetProperty("name").GetString());

        // The importer follows the sheet importer's rules: builds through
        // Godot's own API, never writes into project.godot or .godot/.
        var script = File.ReadAllText(result.RigImporterPath!);
        Assert.Contains("Skeleton2D", script);
        Assert.Contains("Bone2D", script);
        Assert.Contains("AnimationPlayer", script);
        Assert.Contains("ResourceSaver", script);
        Assert.DoesNotContain("project.godot", script.Replace("It never touches project.godot", ""));
    }

    [Fact]
    public void AnUnriggedGodotExportWritesNoRigFiles()
    {
        var result = GodotExporter.Export(Drawn(rigged: false), At("plain.png"));

        // Optional means absent: no rig JSON, no rig importer, and nothing in
        // the directory pretending otherwise.
        Assert.Empty(result.RigPaths);
        Assert.Null(result.RigImporterPath);
        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.Contains("_rig"));
    }

    [Fact]
    public void TheDragonBonesTargetWritesTheSkeletonAndTheSource()
    {
        var preset = new ExportPreset { Name = "db", Target = ExportTarget.DragonBones };
        var run = ExportRunner.Run([Drawn(rigged: true)], preset, At("hero.png"));

        Assert.Equal(2, run.Files.Count);
        var ske = run.Files.Single(f => f.EndsWith("hero_ske.json"));
        Assert.Contains(run.Files, f => f.EndsWith("hero_rig.json"));

        var root = JsonDocument.Parse(File.ReadAllText(ske)).RootElement;
        Assert.Equal("5.5", root.GetProperty("version").GetString());
        Assert.Contains("DragonBones", run.Summary);
    }

    [Fact]
    public void TheRefusalsSayWhyInASentence()
    {
        var preset = new ExportPreset { Name = "db", Target = ExportTarget.DragonBones };

        var unrigged = ExportRunner.Run([Drawn(rigged: false)], preset, At("x.png"));
        Assert.Empty(unrigged.Files);
        Assert.Contains("no rig", unrigged.Summary);

        var pair = ExportRunner.Run([Drawn(rigged: true), Drawn(rigged: true)], preset, At("y.png"));
        Assert.Empty(pair.Files);
        Assert.Contains("per document", pair.Summary);
    }
}
