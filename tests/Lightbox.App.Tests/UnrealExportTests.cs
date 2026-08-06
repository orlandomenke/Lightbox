using System.Text.Json;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;

using Lightbox.Core.Projects;
namespace Lightbox.App.Tests;

/// <summary>
/// Export for Unreal: the atlas, an additive block, and a Python importer.
/// </summary>
/// <remarks>
/// <para>
/// The Python cannot be run here, which is the position all three engine importers are
/// in. The difference with Unreal is that there was never an alternative: a
/// <c>.uasset</c> is binary and cannot be written from outside the editor, so the
/// question "should Lightbox write the asset format" does not arise.
/// </para>
/// <para>
/// What that leaves is a script we ship and cannot execute, so the assertions are about
/// what Lightbox writes and about the rules the script must obey — checked as text. The
/// most important of those rules is that the script <em>reports</em> a property it could
/// not set, because a silent no-op against a renamed engine property is the exact defect
/// this repository already shipped once in the Unity importer.
/// </para>
/// </remarks>
public class UnrealExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-unreal-" + Guid.NewGuid().ToString("N"));

    public UnrealExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string At(string name) => Path.Combine(_dir, name);

    private static Doc Walking(int frames = 4)
    {
        var doc = DocumentFactory.CreateDoc(200, 120, 12, null);
        doc.Scene.FrameCount = frames;
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);
        while (layer.Cels.Count < frames) layer.Cels.Add(new Cel { Frame = new PaintedFrame() });

        for (var i = 0; i < frames; i++)
        {
            if (layer.Cels[i].Frame is not PaintedFrame p) continue;
            double x = 40 + i * 10;
            p.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Fill,
                Color = "#2040c0",
                Points =
                [
                    new StrokePoint(x, 30, 1), new StrokePoint(x + 30, 30, 1),
                    new StrokePoint(x + 30, 90, 1), new StrokePoint(x, 90, 1),
                ],
                Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            });
        }
        return doc;
    }

    private static JsonElement Block(UnrealExportResult r) =>
        JsonDocument.Parse(File.ReadAllText(r.MetadataPath)).RootElement.GetProperty("unreal");

    // ---- what Lightbox writes ------------------------------------------------------

    [Fact]
    public void NoAssetFileIsWrittenBecauseLightboxCannotWriteOne()
    {
        var result = UnrealExporter.Export(Walking(3), At("hero.png"));

        Assert.Empty(Directory.GetFiles(_dir, "*.uasset", SearchOption.AllDirectories));
        Assert.True(File.Exists(result.ImporterPath!));
        Assert.EndsWith(".py", result.ImporterPath);
    }

    [Fact]
    public void TheGenericSidecarKeepsEveryKeyItAlreadyHad()
    {
        var plain = SpriteSheetExporter.Export(Walking(3), At("plain.png"));
        var plainKeys = JsonDocument.Parse(File.ReadAllText(plain.MetadataPath))
            .RootElement.EnumerateObject().Select(p => p.Name).ToList();

        var unreal = UnrealExporter.Export(Walking(3), At("ue.png"));
        var keys = JsonDocument.Parse(File.ReadAllText(unreal.MetadataPath))
            .RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.All(plainKeys, k => Assert.Contains(k, keys));
        Assert.Contains("unreal", keys);
    }

    [Fact]
    public void TheBlockCarriesOnlyWhatTheGenericFileLacks()
    {
        var result = UnrealExporter.Export(Walking(3), At("lean.png"));
        var keys = Block(result).EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("pixelsPerUnrealUnit", keys);
        Assert.Contains("frameRuns", keys);
        Assert.DoesNotContain("frames", keys);
        Assert.DoesNotContain("fps", keys);
        Assert.DoesNotContain("frameTags", keys);
    }

    [Fact]
    public void ThePixelsPerUnitIsUnrealsCentimetreFigureAndNotUnitys()
    {
        // The whole point of a separate exporter rather than a flag on the Unity one.
        // A 120-pixel-tall document asked to stand 1.5 m gives 120/150 for Unreal and
        // 120/1.5 for Unity — two orders of magnitude apart in the same JSON key name.
        var result = UnrealExporter.Export(
            Walking(2), At("units.png"), new UnrealExportOptions { WorldHeightUnits = 1.5 });

        var ppuu = Block(result).GetProperty("pixelsPerUnrealUnit").GetDouble();

        Assert.Equal(120 / 150.0, ppuu, 6);
        Assert.NotEqual(120 / 1.5, ppuu, 3);
    }

    [Fact]
    public void FrameRunsAreCountsOfFramesAndNeverZero()
    {
        var result = UnrealExporter.Export(Walking(4), At("runs.png"));

        var runs = Block(result).GetProperty("frameRuns")
            .EnumerateArray().Select(d => d.GetInt32()).ToList();

        Assert.Equal(4, runs.Count);
        Assert.All(runs, r => Assert.Equal(1, r));
    }

    [Fact]
    public void ADocumentWithNoPivotCarriesNoPivotPoints()
    {
        // The camera's rule: absent, not a list of zeroes that an importer would apply
        // as a pivot in the drawing's top-left corner.
        var result = UnrealExporter.Export(Walking(2), At("nopivot.png"));

        Assert.False(Block(result).TryGetProperty("pivotPoints", out _));
    }

    [Fact]
    public void AFeetPivotArrivesRelativeToTheCellAndInsideIt()
    {
        var doc = Walking(3);
        doc.Scene.Pivot = new Pivot { X = 55, Y = 90 };

        var result = UnrealExporter.Export(
            doc, At("pivot.png"),
            new UnrealExportOptions { Sheet = new SpriteSheetOptions { Trim = SpriteTrim.Union } });

        var points = Block(result).GetProperty("pivotPoints").EnumerateArray().ToList();
        Assert.Equal(3, points.Count);

        // Rect-relative, so with a union trim the pivot lands inside the trimmed cell
        // rather than keeping its document coordinates.
        var x = points[0][0].GetDouble();
        var y = points[0][1].GetDouble();
        Assert.True(x is > 0 and < 55, $"pivot x came out {x}, expected inside the trimmed cell");
        Assert.True(y is > 0 and < 90, $"pivot y came out {y}, expected inside the trimmed cell");
    }

    // ---- asset names, which the script now takes rather than derives ---------------

    [Fact]
    public void TheSidecarNamesTheAssetsSoTheScriptNeverHasTo()
    {
        var result = UnrealExporter.Export(Walking(2), At("Hero sheet (v2).png"));
        var block = Block(result);

        // Spaces and brackets are not legal in an Unreal object name, and a sheet named
        // like this is what an artist actually types.
        Assert.Equal("Hero_sheet_v2", block.GetProperty("assetBase").GetString());
        Assert.Equal(
            "Hero_sheet_v2_Flipbook",
            block.GetProperty("flipbookNames").EnumerateArray().Single().GetString());
    }

    [Fact]
    public void AnUntaggedSheetStillGetsExactlyOneFlipbookName()
    {
        // So the script iterates a list in both cases instead of branching and inventing
        // a name in one branch — which is where a name stops matching the sidecar.
        var result = UnrealExporter.Export(Walking(3), At("plainname.png"));

        Assert.Single(Block(result).GetProperty("flipbookNames").EnumerateArray());
    }

    [Fact]
    public void EachTagBecomesAFlipbookNameInTagOrder()
    {
        var doc = Walking(4);
        doc.Scene.Tags =
        [
            new AnimationTag { Name = "run cycle", Start = 0, End = 1 },
            new AnimationTag { Name = "idle", Start = 2, End = 3 },
        ];

        var result = UnrealExporter.Export(doc, At("Knight.png"));
        var names = Block(result).GetProperty("flipbookNames")
            .EnumerateArray().Select(n => n.GetString()).ToList();

        Assert.Equal(["Knight_run_cycle", "Knight_idle"], names);
    }

    [Fact]
    public void TwoTagsWithTheSameNameDoNotCollapseIntoOneAsset()
    {
        // Left to Unreal this becomes "Knight_run" and "Knight_run1" — a name that no
        // longer matches the tag it came from, and worse, one that depends on what was
        // already in the folder.
        var doc = Walking(4);
        doc.Scene.Tags =
        [
            new AnimationTag { Name = "run", Start = 0, End = 1 },
            new AnimationTag { Name = "run", Start = 2, End = 3 },
        ];

        var names = Block(UnrealExporter.Export(doc, At("Knight.png")))
            .GetProperty("flipbookNames").EnumerateArray().Select(n => n.GetString()).ToList();

        Assert.Equal(["Knight_run", "Knight_run_1"], names);
        Assert.Equal(2, names.Distinct().Count());
    }

    [Fact]
    public void AnEditedImporterIsNotOverwritten()
    {
        var first = UnrealExporter.Export(Walking(2), At("a.png"));
        File.WriteAllText(first.ImporterPath!, "# mine now");

        UnrealExporter.Export(Walking(2), At("b.png"));

        Assert.Equal("# mine now", File.ReadAllText(first.ImporterPath!));
    }

    [Fact]
    public void TheImporterCanBeSuppressed()
    {
        var result = UnrealExporter.Export(
            Walking(2), At("noscript.png"), new UnrealExportOptions(WriteImporter: false));

        Assert.Null(result.ImporterPath);
        Assert.Empty(Directory.GetFiles(_dir, "*.py"));
    }

    // ---- the rules the script must obey, checked as text --------------------------

    [Fact]
    public void EveryPropertyWriteGoesThroughTheHelperThatCannotFailSilently()
    {
        // The load-bearing assertion of this file. Paper2D's scripting surface is
        // experimental; a set_editor_property against a renamed property is the defect
        // that shipped a Unity importer which sliced nothing. Counted rather than merely
        // mentioned, so adding a bare write later fails here.
        var source = UnrealExporter.ImporterSource;
        var lines = source.Split('\n');

        var bare = lines
            .Where(l => l.Contains("set_editor_property") && !l.Contains("def _apply"))
            .Where(l => !l.TrimStart().StartsWith('#'))
            .ToList();

        // Exactly one: the one inside _apply.
        Assert.Single(bare);
        Assert.Contains("_apply(", source);
        Assert.Contains("missing.append", source);
    }

    [Fact]
    public void AFailedPropertyWriteIsReportedAsAnErrorAndNotJustLogged()
    {
        // "It printed a warning nobody read" is the same outcome as silence.
        var source = UnrealExporter.ImporterSource;

        Assert.Contains("log_error", source);
        Assert.Contains("incomplete", source);
        // And it names the likely cause, because "property write failed" alone does not
        // tell anybody what to do about it.
        Assert.Contains("renamed", source);
    }

    [Fact]
    public void TheScriptBuildsAssetsThroughUnrealsOwnApi()
    {
        var source = UnrealExporter.ImporterSource;

        Assert.Contains("create_asset(", source);
        Assert.Contains("unreal.PaperSprite", source);
        Assert.Contains("unreal.PaperSpriteFactory()", source);
        Assert.Contains("unreal.PaperFlipbook", source);
        Assert.Contains("unreal.PaperFlipbookFactory()", source);
        Assert.Contains("unreal.PaperFlipbookKeyFrame()", source);
        Assert.Contains("save_loaded_asset", source);
    }

    [Fact]
    public void TheScriptOnlyClaimsSidecarsThatAreOurs()
    {
        var source = UnrealExporter.ImporterSource;

        Assert.Contains("\"unreal\" not in data", source);
        Assert.Contains("!= \"Lightbox\"", source);
    }

    [Fact]
    public void TheScriptTurnsMipsOffBecauseThatIsWhatBleedsAnAtlas()
    {
        // Godot's equivalent is filter_clip. Here it is the mip chain: a lower mip
        // averages across region boundaries, so a sliver of the neighbouring sprite ends
        // up on this one.
        var source = UnrealExporter.ImporterSource;

        Assert.Contains("TMGS_NO_MIPMAPS", source);
        Assert.Contains("TF_NEAREST", source);
    }

    [Fact]
    public void TheScriptSetsTheRegionAfterCreationBecauseTheFactoryTakesNoTexture()
    {
        // Recorded because it looks like an oversight and is not: PaperSpriteFactory
        // exposes only the base Factory properties, so there is nowhere to hand it a
        // texture at creation time.
        var source = UnrealExporter.ImporterSource;

        Assert.Contains("factory exposes no texture", source);
        Assert.Contains("source_texture", source);
        Assert.Contains("source_uv", source);
        Assert.Contains("source_dimension", source);
    }

    [Fact]
    public void TheScriptSaysWhyItIsAScriptAtAll()
    {
        // Somebody will eventually ask why Lightbox does not just write the asset. The
        // answer belongs in the file they will be reading when they ask.
        var source = UnrealExporter.ImporterSource;

        Assert.Contains("binary", source);
        Assert.Contains("no .uasset", source);
    }

    [Fact]
    public void TheScriptDoesNoNameCleaningOfItsOwn()
    {
        // The rules for a valid Unreal object name are tested in UnrealConvertTests.
        // Implemented a second time in the script they would drift, and the drift would
        // show up as an asset named differently from the one the sidecar describes.
        var source = UnrealExporter.ImporterSource;

        Assert.Contains("block.get(\"assetBase\"", source);
        Assert.Contains("block.get(\"flipbookNames\"", source);
        // The Python-side sanitiser that used to be here, and the character test that
        // was its tell.
        Assert.DoesNotContain("isalnum()", source);
    }

    [Fact]
    public void TheShippedScriptIsStructurallyIntactPython()
    {
        // What this does and does not prove, stated plainly: it is not a parser, and CI
        // has no Python to be one. It catches the realistic failure — a hand-edit to a
        // long raw string literal that mangles the indentation or loses a bracket —
        // which would otherwise ship and fail on the artist's machine. The script was
        // parsed with a real Python at authoring time; this is the part that can run
        // afterwards, on every build.
        Assert.Empty(Complaints(UnrealExporter.ImporterSource));
    }

    [Theory]
    [InlineData("def run():", "def run()", "a block opened without its colon")]
    [InlineData("    try:", "\ttry:", "a tab crept in")]
    [InlineData("import json", " import json", "a statement lost a space")]
    [InlineData("    return found", "    return found)", "a bracket went unmatched")]
    public void AndItActuallyCatchesEachOfThoseMistakes(string original, string broken, string why)
    {
        // Without this the check above is a test that can only pass. Each mutation is one
        // of the four things the check claims to notice, applied to the real script.
        var source = UnrealExporter.ImporterSource;
        Assert.Contains(original, source);

        var mangled = ReplaceFirst(source, original, broken);
        Assert.NotEmpty(Complaints(mangled));
        Assert.True(Complaints(mangled).Count > 0, why);
    }

    private static string ReplaceFirst(string text, string find, string replace)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        return text[..at] + replace + text[(at + find.Length)..];
    }

    /// <summary>Everything structurally wrong with a candidate script, or nothing.</summary>
    private static List<string> Complaints(string source)
    {
        var complaints = new List<string>();
        var lines = source.Split('\n');

        // Depth matters: a continuation line inside an open bracket is aligned to that
        // bracket, so PEP 8 puts it at 11 or 23 spaces on purpose. Only lines that
        // actually begin a statement are held to the four-space rule.
        var depth = 0;
        foreach (var (line, number) in lines.Select((l, i) => (l, i + 1)))
        {
            if (line.Contains('\t')) complaints.Add($"line {number} contains a tab");

            var code = WithoutComment(line);
            var trimmed = code.TrimEnd();
            var startsStatement = depth == 0;
            depth += code.Count(c => c is '(' or '[' or '{') - code.Count(c => c is ')' or ']' or '}');

            if (trimmed.Trim().Length == 0 || !startsStatement) continue;

            var indent = line.Length - line.TrimStart(' ').Length;
            if (indent % 4 != 0) complaints.Add($"line {number} is indented {indent} spaces: {line}");

            var opener = trimmed.TrimStart().Split(' ')[0].TrimEnd(':');
            if (opener is "def" or "if" or "for" or "while" or "else" or "elif" or "try" or "except"
                && !trimmed.EndsWith(':') && depth <= 0)
            {
                // Either the block opens here, or the statement continues onto the next
                // line — which the depth counter has already noticed.
                complaints.Add($"line {number} opens a block without a colon: {line}");
            }
        }

        if (source.Count(c => c == '(') != source.Count(c => c == ')'))
            complaints.Add("unbalanced parentheses");
        if (source.Count(c => c == '[') != source.Count(c => c == ']'))
            complaints.Add("unbalanced square brackets");
        if (source.Count(c => c == '{') != source.Count(c => c == '}'))
            complaints.Add("unbalanced braces");

        return complaints;
    }

    /// <summary>
    /// A line with any trailing <c>#</c> comment removed, respecting quotes.
    /// </summary>
    /// <remarks>
    /// Quote-aware rather than "cut at the first hash", because the script contains
    /// both <c>"#"</c> inside strings and real trailing comments after a colon — and the
    /// naive version fails on the legitimate ones, which is a false alarm the next person
    /// would have to work out from scratch.
    /// </remarks>
    private static string WithoutComment(string line)
    {
        char quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quote != '\0')
            {
                if (c == '\\') i++;
                else if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'') quote = c;
            else if (c == '#') return line[..i];
        }
        return line;
    }

    // ---- through the runner --------------------------------------------------------

    [Fact]
    public void AnUnrealPresetWritesTheSheetTheSidecarAndTheScript()
    {
        var run = ExportRunner.Run(
            Walking(3), new ExportPreset { Name = "u", Target = ExportTarget.Unreal },
            At("run.png"));

        Assert.Equal(3, run.Files.Count);
        Assert.Contains("lightbox_import.py", run.Files[2]);
        Assert.Contains("for Unreal", run.Summary);
        Assert.Contains("flipbook", run.Summary);
        Assert.All(run.Files, f => Assert.True(File.Exists(f), $"{f} was not written"));
    }

    [Fact]
    public void TheRunnerPassesTheWorldHeightThroughRatherThanDefaultingIt()
    {
        // The preset's field is the one the window shows; a target that ignored it would
        // silently export at 1 m whatever the artist typed.
        var run = ExportRunner.Run(
            Walking(2),
            new ExportPreset { Name = "u", Target = ExportTarget.Unreal, WorldHeightUnits = 2.4 },
            At("wh.png"));

        var json = JsonDocument.Parse(File.ReadAllText(run.Files[1]));
        var ppuu = json.RootElement.GetProperty("unreal")
            .GetProperty("pixelsPerUnrealUnit").GetDouble();

        Assert.Equal(120 / 240.0, ppuu, 6);
    }

    [Fact]
    public void AnUnrealPresetStillReportsWhatItLeftOut()
    {
        var doc = Walking(2);
        doc.Scene.Layers.First(l => !l.IsBackground).OmitFromExport = true;

        var run = ExportRunner.Run(
            doc, new ExportPreset { Name = "u", Target = ExportTarget.Unreal }, At("rep.png"));

        Assert.Contains(run.Omitted, o => o.Signal == BackgroundSignal.Pinned);
    }

    [Fact]
    public void TheWorldHeightFieldIsOfferedForUnrealAndNotOnlyUnity()
    {
        // Both engines need it and they disagree about what a unit is, which is exactly
        // why the field must be visible for both rather than only for the one it was
        // added for.
        Assert.True(new ExportPreset { Name = "u", Target = ExportTarget.Unreal }.UsesEngineSettings);
        Assert.True(new ExportPreset { Name = "y", Target = ExportTarget.Unity }.UsesEngineSettings);
        Assert.False(new ExportPreset { Name = "s", Target = ExportTarget.SpriteSheet }.UsesEngineSettings);
    }
}
