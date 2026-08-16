using System.Reflection;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// A field added to <see cref="Frame"/> is a field the frame converter has to
/// be told about.
/// </summary>
/// <remarks>
/// <para>
/// <c>Frame</c> is the one document type that does <b>not</b> go through
/// <c>System.Text.Json</c>'s reflection — <c>FrameConverter</c> writes and
/// reads a named list of properties, because a frame's optional blocks are
/// written only when used and an imported baseline has to be dropped rather
/// than round-tripped. The cost of that control is that a new property is
/// silently not saved: it compiles, it works all session, and it is gone on
/// reload.
/// </para>
/// <para>
/// This is the sweep that stops the next one. Found the hard way by
/// <c>Frame.Correctives</c> (Q100), which round-tripped to null and took a
/// failing test to notice.
/// </para>
/// </remarks>
public class FrameConverterCoverageTests
{
    [Fact]
    public void EveryPropertyOnAFrameSurvivesARoundTrip()
    {
        // Every settable public property either survives a save and load or is
        // named here as deliberately dropped. Nothing else may be added
        // silently.
        var deliberatelyDropped = new HashSet<string>
        {
            // An imported baseline is flattened into strokes on merge; keeping
            // it would round-trip back out the key it was supposed to lose.
            // FrameConverter's own comment says so, and it is tested there.
            nameof(Frame.PngBase64),
        };

        var properties = typeof(Frame)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true })
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Where(p => !deliberatelyDropped.Contains(p.Name))
            .ToList();

        var populated = Populated();
        var back = DocJson.Deserialize(DocJson.Serialize(Wrap(populated))).Scene.Layers[^1].Cels[0].Frame!;

        var lost = new List<string>();
        foreach (var property in properties)
        {
            var before = property.GetValue(populated);
            var after = property.GetValue(back);
            // Reference types are compared for presence rather than equality:
            // this asks "did the converter carry it at all", which is the
            // failure mode. Value-by-value correctness is each field's own
            // test's job.
            if (before is null) continue;
            if (after is null || (after is System.Collections.ICollection { Count: 0 } && before is System.Collections.ICollection { Count: > 0 }))
                lost.Add(property.Name);
        }

        Assert.True(
            lost.Count == 0,
            $"FrameConverter does not carry {string.Join(", ", lost)}. Add it to Read and Write, "
            + "or name it in this test's dropped list and say why.");
    }

    /// <summary>A frame with one of everything a frame can hold.</summary>
    private static Frame Populated() => new()
    {
        Role = FrameRole.Breakdown,
        Strokes = [new Stroke { Points = [new StrokePoint(1, 1, 1), new StrokePoint(9, 9, 1)] }],
        Anchors = new Dictionary<string, AnchorPoint> { ["a"] = new(1, 2) },
        Shapes = new Dictionary<string, ShapeBox> { ["s"] = new(1, 2, 3, 4) },
        Placements = [new SymbolPlacement { SymbolId = "sym" }],
        Ai = new AiProvenance("test"),
        Chart = [0.5],
        Correctives =
        [
            new Corrective
            {
                DriverBoneId = "fore",
                Stops = [new CorrectiveStop { AngleDeg = 90, Strokes = [new StrokeCorrection { StrokeId = "x", Offsets = [new PointOffset(1, 2)] }] }],
            },
        ],
    };

    private static Doc Wrap(Frame frame)
    {
        var doc = DocumentFactory.CreateDoc();
        doc.Scene.Layers.Add(new Layer { Cels = [new Cel { Frame = frame }] });
        return doc;
    }
}
