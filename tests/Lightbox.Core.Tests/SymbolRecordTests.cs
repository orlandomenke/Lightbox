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
        Frames = [new Frame { Strokes = [Line(0, 0, 10, 10)] }],
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
            Assert.Single(((Frame)read.Frames[0]).Strokes);
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
    public void ANestedPlacementIsRefusedOnLoadRatherThanHalfSupported()
    {
        // Nesting needs a cycle check and a depth limit that have to be right
        // before anything renders. Dropping the placement is a smaller lie
        // than rendering an unbounded recursion.
        var root = TempDir();
        try
        {
            var project = ProjectIo.Create("Knight", root);
            var sword = Sword();
            ((Frame)sword.Frames[0]).Placements =
                [new SymbolPlacement { SymbolId = "sym_itself" }];
            project.Symbols[sword.Id] = sword;
            ProjectIo.Save(project);

            var back = ProjectIo.Load(root);

            var read = Assert.Single(back!.Symbols).Value;
            Assert.Null(((Frame)read.Frames[0]).Placements);
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
