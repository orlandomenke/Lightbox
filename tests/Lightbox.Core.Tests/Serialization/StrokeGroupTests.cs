using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// Q265, the model and the file: a group is an entry in a drawing's stroke
/// list that holds strokes of its own with the blend, opacity, clip, mask and
/// effects a layer had, so a merge can keep them instead of flattening.
/// </summary>
/// <remarks>
/// Two rules these tests hold, both from <c>docs/DESIGN-stroke-groups.md</c>:
/// a document with no group is byte-for-byte what it was before groups existed,
/// and only a document that contains one is marked as needing a newer build —
/// an older build cannot name the kind and must refuse it rather than open it
/// with the drawing missing.
/// </remarks>
public class StrokeGroupTests
{
    private static Stroke Line(double y) => new()
    {
        Points = [new(10, y, 0.5), new(90, y, 0.8)],
    };

    private static Stroke Group(params Stroke[] strokes) => new()
    {
        Tool = ToolKind.Group,
        Group = new StrokeGroup { Strokes = [.. strokes] },
    };

    private static Doc DocWith(params Stroke[] strokes)
    {
        var doc = DocumentFactory.CreateDoc(320, 180, 24);
        doc.Scene.Layers[0].Cels[0].Frame!.Strokes.AddRange(strokes);
        return doc;
    }

    private static List<Stroke> StrokesOf(Doc doc) => doc.Scene.Layers[0].Cels[0].Frame!.Strokes;

    [Fact]
    public void ADocumentWithNoGroupIsWrittenExactlyAsBefore()
    {
        var json = DocJson.Serialize(DocWith(Line(20)));

        Assert.DoesNotContain("\"group\"", json);
        Assert.Contains("\"version\": 1", json);
    }

    [Fact]
    public void AGroupRoundTripsWithEverythingItCarries()
    {
        var eraser = Line(30);
        eraser.Tool = ToolKind.Eraser;
        var group = Group(Line(30), eraser);
        group.Group!.Blend = LayerBlendMode.Multiply;
        group.Group.Opacity = 0.6;
        group.Group.Clip = true;
        group.Group.PngBase64 = "AAAA";
        group.Group.Mask = new LayerMask { Frame = new Frame { Strokes = [Line(40)] }, Inverted = true };

        var restored = StrokesOf(DocJson.Deserialize(DocJson.Serialize(DocWith(Line(20), group))));

        Assert.Equal(2, restored.Count);
        Assert.Equal(ToolKind.Group, restored[1].Tool);
        var g = restored[1].Group!;
        Assert.Equal(2, g.Strokes.Count);
        Assert.Equal(ToolKind.Eraser, g.Strokes[1].Tool);
        Assert.Equal(LayerBlendMode.Multiply, g.Blend);
        Assert.Equal(0.6, g.Opacity);
        Assert.True(g.Clip);
        Assert.Equal("AAAA", g.PngBase64);
        Assert.True(g.Mask!.Inverted);
        Assert.Single(g.Mask.Frame.Strokes);
    }

    [Fact]
    public void AGroupInsideAGroupRoundTrips()
    {
        var restored = StrokesOf(DocJson.Deserialize(DocJson.Serialize(DocWith(Group(Line(20), Group(Line(30)))))));

        Assert.Equal(30, restored[0].Group!.Strokes[1].Group!.Strokes[0].Points[0].Y);
    }

    [Fact]
    public void APlainGroupWritesNoneOfItsOptionalParts()
    {
        // Optional has two halves: the block is absent when unused, and so is
        // everything inside it. A group of plain strokes says only that.
        var json = DocJson.Serialize(DocWith(Group(Line(20))));

        // The group's own keys, read off the parsed block — its strokes have
        // an "opacity" of their own inside their brush, which is not the group's.
        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var group = parsed.RootElement.GetProperty("scene").GetProperty("layers")[0]
            .GetProperty("cels")[0].GetProperty("frame").GetProperty("strokes")[0].GetProperty("group");
        var keys = group.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(["strokes"], keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyADocumentThatContainsAGroupIsMarkedAsNewer(bool nested)
    {
        // An older build has no name for the kind and stops reading. Marking
        // the document is what lets it say "from a newer Lightbox" first — and
        // marking only this document is what keeps every other file openable.
        var doc = nested ? DocWith(Group(Group(Line(20)))) : DocWith(Line(10), Group(Line(20)));

        var json = DocJson.Serialize(doc);

        Assert.Contains("\"version\": 2", json);
        Assert.Contains("\"tool\": \"group\"", json);
    }

    [Fact]
    public void AGroupHiddenInAMaskStillMarksTheDocument()
    {
        var doc = DocWith(Line(10));
        doc.Scene.Layers[0].Mask = new LayerMask { Frame = new Frame { Strokes = [Group(Line(20))] } };

        Assert.Contains("\"version\": 2", DocJson.Serialize(doc));
    }

    [Fact]
    public void RemovingTheLastGroupMakesTheDocumentOrdinaryAgain()
    {
        var doc = DocWith(Group(Line(20)));
        _ = DocJson.Serialize(doc);
        StrokesOf(doc).Clear();

        Assert.Contains("\"version\": 1", DocJson.Serialize(doc));
    }

    [Fact]
    public void ThisBuildReadsBothVersionsWithoutCallingEitherNewer()
    {
        Assert.False(DocJson.Deserialize(DocJson.Serialize(DocWith(Line(20)))).IsFromANewerBuild);
        Assert.False(DocJson.Deserialize(DocJson.Serialize(DocWith(Group(Line(20))))).IsFromANewerBuild);
    }

    [Fact]
    public void ACopyOfAGroupSharesNothingWithTheOriginal()
    {
        // A duplicated cel and an undo snapshot are both copies. Shared
        // children would let reshaping one reshape the other.
        var original = Group(Line(20), Group(Line(30)));
        original.Group!.Mask = new LayerMask { Frame = new Frame { Strokes = [Line(40)] } };

        var copy = original.Clone();
        copy.Group!.Strokes[0].Points[0] = new StrokePoint(999, 999, 1);
        copy.Group.Strokes[1].Group!.Strokes.Clear();
        copy.Group.Mask!.Frame.Strokes.Clear();

        Assert.Equal(10, original.Group.Strokes[0].Points[0].X);
        Assert.Single(original.Group.Strokes[1].Group!.Strokes);
        Assert.Single(original.Group.Mask.Frame.Strokes);
        Assert.NotEqual(original.Group.Strokes[0].Id, copy.Group.Strokes[0].Id);
    }

    [Fact]
    public void AnUndoSnapshotKeepsEveryIdInsideAGroup()
    {
        // Frame.Clone is the snapshot's copy: same ids, so caches keyed by id
        // still mean the same strokes after an undo.
        var frame = new Frame { Strokes = [Group(Line(20))] };

        var copy = frame.Clone();

        Assert.Equal(frame.Strokes[0].Group!.Strokes[0].Id, copy.Strokes[0].Group!.Strokes[0].Id);
        Assert.NotSame(frame.Strokes[0].Group!.Strokes[0], copy.Strokes[0].Group!.Strokes[0]);
    }

    [Fact]
    public void EveryStrokeInADrawingCanBeWalkedThroughItsGroups()
    {
        var inner = Line(30);
        var strokes = new List<Stroke> { Line(10), Group(Line(20), Group(inner)) };

        var all = StrokeGroups.Flatten(strokes).ToList();

        // The groups themselves are not marks; the three lines are.
        Assert.Equal(3, all.Count);
        Assert.Contains(inner, all);
        Assert.True(StrokeGroups.Any(strokes));
        Assert.False(StrokeGroups.Any([Line(10)]));
    }

    [Fact]
    public void TheCommittedGroupedDocumentStillLoads()
    {
        // W8: a change to the serialized form ships a fixture — a committed
        // file, not one built here from the current model.
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "grouped-v2.lightbox.json");

        var doc = DocJson.Deserialize(File.ReadAllText(path));

        Assert.Equal(2, doc.Version);
        var group = doc.Scene.Layers[0].Cels[0].Frame!.Strokes[1];
        Assert.Equal(ToolKind.Group, group.Tool);
        Assert.Equal(LayerBlendMode.Multiply, group.Group!.Blend);
        Assert.Equal(0.6, group.Group.Opacity);
        Assert.Equal(2, group.Group.Strokes.Count);
        Assert.Equal(ToolKind.Eraser, group.Group.Strokes[1].Tool);
    }
}
