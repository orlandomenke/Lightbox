using System.Diagnostics;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// B142 — an undo snapshot is a direct graph clone, and it gives back
/// exactly what it was handed.
/// </summary>
/// <remarks>
/// <para>
/// The bug was cost: <c>Perform</c> serialized the whole document <em>and</em>
/// parsed the result back, on every structural edit, through the indented
/// options meant for files a human reads. 615 ms and 72.5 MB on a 5 000-stroke
/// painting, to build a second document that most of the time nobody ever
/// looks at. These tests were written against the first fix (freeze to bytes,
/// thaw lazily) and survived the second (<c>Doc.Clone</c>) unchanged, because
/// they assert what any snapshot mechanism owes, not how it is built.
/// </para>
/// <para>
/// <b>Fidelity is what these guard, not speed.</b> The undo system is the one
/// place in this codebase where a mistake corrupts a drawing rather than
/// slowing one down, so the shape of every test here is the same: do the edit,
/// undo it, and compare the serialized document to what it was before — the
/// whole document, not the field the edit touched. A clone that drops a key is
/// silent, survives the next save, and is discovered by an artist rather than
/// by a suite.
/// </para>
/// </remarks>
public class UndoSnapshotFidelityTests(ITestOutputHelper output)
{
    /// <summary>
    /// A document using as many optional features as possible.
    /// </summary>
    /// <remarks>
    /// Deliberately not a default document. Most of what an undo snapshot can
    /// lose is exactly the material that is <em>absent by default</em> — a
    /// nullable dictionary, a feature override, a template link — because those
    /// are the keys a hand-written copy forgets and a default document never
    /// exercises.
    /// </remarks>
    private static Doc Loaded()
    {
        var doc = DocumentFactory.CreateDoc(400, 300, 6);
        var frame = doc.Scene.Layers[^1].Cels[0].Frame!;

        for (var i = 0; i < 12; i++)
        {
            var s = new Stroke
            {
                Brush = new BrushSettings { Size = 20 + i, Hardness = 0.4, Flow = 0.6, Spacing = 0.08 },
                Color = "#3366cc",
            };
            for (var p = 0; p < 6; p++)
            {
                s.Points.Add(new StrokePoint { X = i * 7 + p, Y = i * 3 + p * 2, Pressure = 0.25 + p * 0.1 });
            }
            frame.Strokes.Add(s);
        }

        doc.BrushTips["tip-a"] = "iVBORw0KGgo=";
        doc.Textures = new Dictionary<string, string> { ["paper-a"] = "iVBORw0KGgo=" };
        doc.ClipRegions["clip-a"] = new ClipRegion
        {
            Feather = 2.5,
            Contours = [[new StrokePoint { X = 0, Y = 0 }, new StrokePoint { X = 10, Y = 10 }]],
        };
        doc.Palettes.Add(new Palette { Name = "Skin" });
        doc.Gradients["grad-a"] = new Gradient();
        doc.IsTemplate = true;
        doc.TemplateId = "tpl-7";
        doc.Features = new Dictionary<string, bool> { ["UnboundedCanvas"] = true };

        return doc;
    }

    /// <summary>What the file would say — the only comparison that misses nothing.</summary>
    private static string Shape(Doc doc) => DocJson.Serialize(doc);

    [Fact]
    public void AStructuralEditIsUndoneByteIdentically()
    {
        var editor = new DocumentEditor(Loaded());
        var before = Shape(editor.Doc);

        editor.AddFrameAfter(0);
        Assert.NotEqual(before, Shape(editor.Doc)); // not vacuous: the edit did something

        editor.Undo();
        Assert.Equal(before, Shape(editor.Doc));
    }

    /// <summary>
    /// Every optional key survives the round trip, including the ones a default
    /// document never has.
    /// </summary>
    [Fact]
    public void NothingOptionalIsLostOnTheWayThroughASnapshot()
    {
        var editor = new DocumentEditor(Loaded());
        editor.AddFrameAfter(0);
        editor.Undo();

        var json = Shape(editor.Doc);
        output.WriteLine($"{json.Length / 1024} KB after a snapshot round trip");

        foreach (var key in new[]
                 { "brushTips", "textures", "clipRegions", "palettes", "gradients", "isTemplate",
                   "templateId", "features", "strokes", "points" })
        {
            Assert.Contains($"\"{key}\"", json);
        }
        // And the values, not merely the keys: a converter that wrote an empty
        // object for each of these would pass every line above.
        Assert.Contains("tpl-7", json);
        Assert.Contains("paper-a", json);
        Assert.Contains("#3366cc", json);
        Assert.Contains("UnboundedCanvas", json);
    }

    /// <summary>
    /// Undo, redo, undo — a step traversed repeatedly keeps giving the same
    /// two documents.
    /// </summary>
    /// <remarks>
    /// Written when a snapshot held bytes until first rolled back and the
    /// document object afterwards, so the second traversal took a different
    /// branch from the first. The clone mechanism has one branch, but the
    /// property is still the contract: if repeated traversal ever disagreed
    /// with itself, it would look like the document drifting rather than like
    /// a bug in undo.
    /// </remarks>
    [Fact]
    public void UndoingAndRedoingRepeatedlyLandsOnTheSameTwoDocuments()
    {
        var editor = new DocumentEditor(Loaded());
        var before = Shape(editor.Doc);

        editor.AddFrameAfter(0);
        var after = Shape(editor.Doc);

        for (var round = 0; round < 3; round++)
        {
            editor.Undo();
            Assert.Equal(before, Shape(editor.Doc));
            editor.Redo();
            Assert.Equal(after, Shape(editor.Doc));
        }
    }

    /// <summary>
    /// A snapshot is taken of the document as it was <em>before</em> the
    /// mutation, and the mutation cannot reach back into it.
    /// </summary>
    /// <remarks>
    /// A full deep copy makes this true by construction rather than by
    /// discipline, which is worth a test precisely because the alternative
    /// design priced for B142 — sharing the stroke objects between the
    /// snapshot and the live document — does not have that property, and it
    /// is the tempting next optimisation. This is the test that says what any
    /// future attempt has to keep. <c>DocCloneTests.NoMutableObjectAppearsInBothGraphs</c>
    /// enforces the same property structurally, by reflection, over the whole
    /// graph.
    /// </remarks>
    [Fact]
    public void AMutationCannotReachIntoTheSnapshotItWasTakenAgainst()
    {
        var editor = new DocumentEditor(Loaded());
        var before = Shape(editor.Doc);

        // Mutate a stroke in place inside the Perform — the shape of edit that
        // a reference-sharing snapshot would silently fail to restore.
        editor.Perform(doc =>
        {
            var s = doc.Scene.Layers[^1].Cels[0].Frame!.Strokes[0];
            s.Color = "#ff0000";
            // Replacing rather than nudging, because StrokePoint is init-only —
            // a point cannot be moved, only swapped for another one.
            s.Points[0] = new StrokePoint { X = 250, Y = 250, Pressure = 1 };
        });
        Assert.Contains("#ff0000", Shape(editor.Doc));

        editor.Undo();
        Assert.Equal(before, Shape(editor.Doc));
    }

    /// <summary>
    /// The cost that made this a bug: taking the snapshot must cost far less
    /// than the serializer round trip it replaced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Loose on purpose, in the house style — this catches the fix being
    /// reverted, not drift. The ratio is asserted rather than a wall-clock
    /// time, because the absolute number is a property of whatever machine CI
    /// happened to allocate.
    /// </para>
    /// <para>
    /// Aimed at what <c>Perform</c> actually calls — <see cref="Doc.Clone"/> —
    /// against the round trip it replaced. A previous version of this test
    /// measured the freeze/thaw pair after the mechanism had moved on, which is
    /// the near-miss B142's ledger entry records: a benchmark aimed at the
    /// implementation being replaced reports the fix as noise.
    /// </para>
    /// <para>
    /// Both numbers are printed whatever happens. A ratio assertion that prints
    /// only on failure is the measurement trap this repository has paid for
    /// three times: the assertion passing is not evidence the thing being
    /// measured is what you think it is.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "Performance")]
    public void TakingASnapshotCostsFarLessThanRebuildingOne()
    {
        var doc = DocumentFactory.CreateDoc(2000, 1500, 12);
        var frame = doc.Scene.Layers[^1].Cels[0].Frame!;
        var seed = 11;
        for (var i = 0; i < 4000; i++)
        {
            var s = new Stroke { Brush = new BrushSettings { Size = 60, Hardness = 0.3, Spacing = 0.05 } };
            var x = Math.Abs(seed = seed * 1103515245 + 12345) % 2000;
            var y = Math.Abs(seed = seed * 1103515245 + 12345) % 1500;
            for (var p = 0; p < 8; p++) s.Points.Add(new StrokePoint { X = x + p * 5, Y = y + p * 2, Pressure = 0.5 });
            frame.Strokes.Add(s);
        }

        GC.KeepAlive(doc.Clone());                  // warm both paths
        GC.KeepAlive(DocJson.Clone(doc));

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 3; i++) GC.KeepAlive(doc.Clone());
        var clone = sw.Elapsed.TotalMilliseconds / 3;

        sw.Restart();
        for (var i = 0; i < 3; i++) GC.KeepAlive(DocJson.Clone(doc));
        var roundTrip = sw.Elapsed.TotalMilliseconds / 3;

        output.WriteLine(
            $"4000 strokes: clone {clone:F1} ms, serializer round trip {roundTrip:F1} ms");

        Assert.True(
            clone * 5 < roundTrip,
            $"cloning ({clone:F1} ms) is no longer far cheaper than the round trip "
            + $"({roundTrip:F1} ms) — the snapshot path has regressed toward what B142 fixed");
    }
}
