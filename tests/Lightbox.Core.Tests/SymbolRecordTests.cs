using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Pillar 3, step one: what a symbol and a placement are, and what they cost
/// a document that never uses one.
/// </summary>
/// <remarks>
/// No registry, no rendering and no UI yet — those are S2 and S3 of
/// <c>docs/DESIGN-symbols.md</c>. This is the record, and the record is the
/// decision the rest of the pillar rests on.
/// </remarks>
public class SymbolRecordTests
{
    private static Symbol Sword() => new()
    {
        Name = "Sword",
        Kind = SymbolKind.Prop,
        Tags = ["knight", "act two"],
        PivotX = 12,
        PivotY = 40,
        Layers = Symbol.Flat("Sword", [new Frame { Strokes = [Line(0, 0, 10, 10)] }]),
    };

    private static Stroke Line(double x0, double y0, double x1, double y1) => new()
    {
        Points = [new StrokePoint(x0, y0, 1), new StrokePoint(x1, y1, 1)],
    };

    /// <summary>A document with one painted cel, which <c>new Doc()</c> is not.</summary>
    private static (Doc Doc, Frame Frame) DocWithACel()
    {
        var frame = new Frame();
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer
        {
            Name = "L",
            Kind = LayerKind.Painted,
            Cels = { new Cel { Frame = frame } },
        });
        return (doc, frame);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lb-sym-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---- absence ------------------------------------------------------------------

    [Fact]
    public void ADocumentWithNoPlacementsWritesNoPlacementKey()
    {
        // Optional means absent. The camera's rule, for the fifth time — and
        // this is the one that proves symbols cost nothing to a painting.
        //
        // With a cel on purpose: a bare `new Doc()` has no frames, so the frame
        // converter never runs and the test passes without exercising the rule
        // it is named after.
        var (doc, _) = DocWithACel();

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("placements", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("symbol", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFreshPaintedFrameHasNoPlacements()
    {
        var frame = new Frame();

        Assert.Null(frame.Placements);
        Assert.False(frame.HasPlacements);
    }

    [Fact]
    public void AProjectWithNoSymbolsWritesNoSymbolFile()
    {
        var root = TempDir();
        try
        {
            var project = ProjectIo.Create("Empty", root);
            ProjectIo.Save(project);

            Assert.False(File.Exists(Path.Combine(root, "assets", "symbols.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // ---- the record round-trips --------------------------------------------------

    [Fact]
    public void APlacementSurvivesASaveAndReload()
    {
        var (doc, frame) = DocWithACel();
        frame.Placements =
        [
            new SymbolPlacement
            {
                SymbolId = "sym_sword",
                X = 100, Y = 200,
                ScaleX = 0.5, ScaleY = 0.5,
                Angle = 30,
                Opacity = 0.8,
                FrameOffset = 3,
                SwatchOverrideId = "sw_enemy",
                SeenVersion = 4,
            },
        ];

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        var placement = Assert.Single(((Frame)back.Scene.Layers[^1].Cels[0].Frame!).Placements!);
        Assert.Equal("sym_sword", placement.SymbolId);
        Assert.Equal(100, placement.X);
        Assert.Equal(0.5, placement.ScaleX);
        Assert.Equal(30, placement.Angle);
        Assert.Equal(3, placement.FrameOffset);
        Assert.Equal("sw_enemy", placement.SwatchOverrideId);
        Assert.Equal(4, placement.SeenVersion);
    }

    [Fact]
    public void SymbolsSurviveASaveAndReloadOfTheProject()
    {
        // Held by the project, not the document — which is the whole promise:
        // the sword lives above the animations that hold it.
        var root = TempDir();
        try
        {
            var project = ProjectIo.Create("Knight", root);
            var sword = Sword();
            project.Symbols[sword.Id] = sword;
            ProjectIo.Save(project);

            var back = ProjectIo.Load(root);

            Assert.NotNull(back);
            var read = Assert.Single(back!.Symbols).Value;
            Assert.Equal("Sword", read.Name);
            Assert.Equal(SymbolKind.Prop, read.Kind);
            Assert.Equal(["knight", "act two"], read.Tags);
            Assert.Equal(12, read.PivotX);
            Assert.Single(read.AllFrames.First().Strokes);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DeletingTheLastSymbolReachesTheDisk()
    {
        // Or reopening the project brings it back, which is the bug the last
        // palette folder already taught.
        var root = TempDir();
        try
        {
            var project = ProjectIo.Create("Knight", root);
            var sword = Sword();
            project.Symbols[sword.Id] = sword;
            ProjectIo.Save(project);
            Assert.True(File.Exists(Path.Combine(root, "assets", "symbols.json")));

            project.Symbols.Clear();
            ProjectIo.Save(project);

            Assert.False(File.Exists(Path.Combine(root, "assets", "symbols.json")));
            Assert.Empty(ProjectIo.Load(root)!.Symbols);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ANestedPlacementSurvivesAReload()
    {
        // Q289. Loading used to drop every placement inside a project symbol,
        // waiting for a cycle check that B487 supplied. The application let an
        // artist nest all along, so the drop was work disappearing on reopen.
        var root = TempDir();
        try
        {
            var project = ProjectIo.Create("Knight", root);
            var sword = Sword();
            var knight = Sword();
            knight.Name = "Knight";
            knight.AllFrames.First().Placements =
                [new SymbolPlacement { SymbolId = sword.Id, X = 30, Y = 5 }];
            project.Symbols[sword.Id] = sword;
            project.Symbols[knight.Id] = knight;
            ProjectIo.Save(project);

            var back = ProjectIo.Load(root);

            var placement = Assert.Single(back!.Symbols[knight.Id].AllFrames.First().Placements!);
            Assert.Equal(sword.Id, placement.SymbolId);
            Assert.Equal(30, placement.X);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AProjectWhoseSymbolsHoldEachOtherStillLoads()
    {
        // The loop is kept as written. Placing refuses one and the renderer
        // draws nothing for one (B487), so loading is not where it is judged.
        var root = TempDir();
        try
        {
            var project = ProjectIo.Create("Knight", root);
            var a = Sword();
            var b = Sword();
            a.AllFrames.First().Placements = [new SymbolPlacement { SymbolId = b.Id }];
            b.AllFrames.First().Placements = [new SymbolPlacement { SymbolId = a.Id }];
            project.Symbols[a.Id] = a;
            project.Symbols[b.Id] = b;
            ProjectIo.Save(project);

            var back = ProjectIo.Load(root);

            Assert.Equal(2, back!.Symbols.Count);
            Assert.NotNull(back.Symbols[a.Id].AllFrames.First().Placements);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AFlattenCarriesTheSymbolsInsideTheSymbolsItPlaces()
    {
        // The document places the knight; the knight holds the sword. An export
        // that carried only the knight would arrive without his sword.
        var root = TempDir();
        try
        {
            var project = ProjectIo.Create("Knight", root);
            var sword = Sword();
            var hand = Sword();
            hand.AllFrames.First().Placements = [new SymbolPlacement { SymbolId = sword.Id }];
            var knight = Sword();
            knight.AllFrames.First().Placements = [new SymbolPlacement { SymbolId = hand.Id }];
            foreach (var s in new[] { sword, hand, knight }) project.Symbols[s.Id] = s;
            var (doc, frame) = DocWithACel();
            frame.Placements = [new SymbolPlacement { SymbolId = knight.Id }];

            var flat = ProjectIo.Flatten(doc, project);

            Assert.NotNull(flat.Symbols);
            Assert.True(flat.Symbols!.ContainsKey(knight.Id));
            Assert.True(flat.Symbols.ContainsKey(hand.Id));
            Assert.True(flat.Symbols.ContainsKey(sword.Id));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // ---- what a placement means -----------------------------------------------------

    [Fact]
    public void APropShowsItsOneFrameOnEveryCel()
    {
        var placement = new SymbolPlacement();

        Assert.Equal(0, placement.FrameIndexAt(0, 1));
        Assert.Equal(0, placement.FrameIndexAt(7, 1));
    }

    [Fact]
    public void ACycleWrapsAcrossTheTimeline()
    {
        var placement = new SymbolPlacement();

        Assert.Equal(0, placement.FrameIndexAt(0, 4));
        Assert.Equal(3, placement.FrameIndexAt(3, 4));
        Assert.Equal(0, placement.FrameIndexAt(4, 4));
        Assert.Equal(1, placement.FrameIndexAt(9, 4));
    }

    [Fact]
    public void AnOffsetRunsTheSameCycleOutOfStep()
    {
        // One stored walk, two characters half a stride apart. This is the
        // reason a placement carries an offset at all.
        var behind = new SymbolPlacement { FrameOffset = 2 };

        Assert.Equal(2, behind.FrameIndexAt(0, 4));
        Assert.Equal(3, behind.FrameIndexAt(1, 4));
        Assert.Equal(0, behind.FrameIndexAt(2, 4));
    }

    [Fact]
    public void ANegativeOffsetStartsPartWayThroughRatherThanGoingOutOfRange()
    {
        var early = new SymbolPlacement { FrameOffset = -1 };

        Assert.Equal(3, early.FrameIndexAt(0, 4));
        Assert.Equal(0, early.FrameIndexAt(1, 4));
    }

    [Fact]
    public void AnEmptySymbolStillReportsOneFrameSoNothingDividesByZero()
    {
        Assert.Equal(1, new Symbol().FrameCount);
        Assert.Equal(0, new SymbolPlacement().FrameIndexAt(5, new Symbol().FrameCount));
    }

    [Fact]
    public void APlacementRemembersWhatItWasPlacedAgainst()
    {
        // Compared, never acted on by itself: "the sword changed since you
        // placed this" is a thing to report, not to fix behind somebody's back.
        var sword = Sword();
        var placement = new SymbolPlacement { SymbolId = sword.Id, SeenVersion = sword.Version };

        sword.Version++;

        Assert.NotEqual(sword.Version, placement.SeenVersion);
    }
}
