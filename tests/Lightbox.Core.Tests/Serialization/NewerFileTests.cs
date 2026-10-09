using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// A file a newer build wrote keeps what this build cannot read (Q230).
/// </summary>
/// <remarks>
/// <para>
/// The owner runs a deployed build that can lag <c>main</c> by hours, so a
/// drawing saved by a newer build and reopened in the deployed one is the
/// ordinary case, not an edge. Before this, a key this build did not know was
/// skipped on open and the next save dropped it — silently, and permanently,
/// which "work is never lost" forbids. Now it is carried through: read, held
/// beside the record as raw JSON, and written back after the keys this build
/// owns.
/// </para>
/// <para>
/// <b>Absent still means absent.</b> A document that never had an unknown key
/// writes none; the holder is null until a file supplies something to hold.
/// </para>
/// </remarks>
public class NewerFileTests(ITestOutputHelper output)
{
    private const string Marker = "zzFromALaterBuild";

    /// <summary>A document with one of everything the round trip checks.</summary>
    private static Doc Sample()
    {
        var doc = new Doc();
        var layer = new Layer { Name = "L" };
        var frame = new Frame();
        frame.Strokes.Add(new Stroke
        {
            Color = "#102030",
            Brush = new BrushSettings { Size = 4 },
            Points = [new StrokePoint(1, 2, 1), new StrokePoint(5, 6, 1)],
        });
        layer.Cels.Add(new Cel { Frame = frame });
        doc.Scene.Layers.Add(layer);
        doc.Palettes.Add(new Palette { Name = "P" });
        doc.Symbols = new() { ["s1"] = new Symbol { Id = "s1", Name = "S" } };
        return doc;
    }

    /// <summary>Where the keys go: one per kind of record, each with its own value.</summary>
    private static IEnumerable<(string Where, Func<JsonNode, JsonObject> At)> Places() =>
    [
        ("doc", r => r.AsObject()),
        ("scene", r => r["scene"]!.AsObject()),
        ("layer", r => r["scene"]!["layers"]![0]!.AsObject()),
        ("cel", r => r["scene"]!["layers"]![0]!["cels"]![0]!.AsObject()),
        ("frame", r => r["scene"]!["layers"]![0]!["cels"]![0]!["frame"]!.AsObject()),
        ("stroke", r => r["scene"]!["layers"]![0]!["cels"]![0]!["frame"]!["strokes"]![0]!.AsObject()),
        ("brush", r => r["scene"]!["layers"]![0]!["cels"]![0]!["frame"]!["strokes"]![0]!["brush"]!.AsObject()),
        ("palette", r => r["palettes"]![0]!.AsObject()),
        ("symbol", r => r["symbols"]!["s1"]!.AsObject()),
    ];

    private static string WithUnknownKeys(Doc doc)
    {
        var root = JsonNode.Parse(DocJson.Serialize(doc))!;
        foreach (var (where, at) in Places())
        {
            at(root)[Marker] = new JsonObject
            {
                ["at"] = where,
                ["values"] = new JsonArray(1, 2.5, true, null, "text"),
                ["nested"] = new JsonObject { ["deeper"] = new JsonArray("a") },
            };
        }
        return root.ToJsonString();
    }

    private static void AssertKept(string json, string context)
    {
        var root = JsonNode.Parse(json)!;
        foreach (var (where, at) in Places())
        {
            var kept = at(root)[Marker];
            Assert.True(kept is not null, $"{context}: the {where}'s unknown key was dropped");
            Assert.Equal(where, kept!["at"]!.GetValue<string>());
            Assert.Equal("a", kept["nested"]!["deeper"]![0]!.GetValue<string>());
        }
    }

    [Fact]
    public void AFileFromANewerBuildKeepsWhatItCarriesThroughASave()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-newer-{Guid.NewGuid():N}.lightbox.json");
        try
        {
            var opened = DocJson.Deserialize(WithUnknownKeys(Sample()));
            DocJson.Save(opened, path); // gzip, as a real save writes it
            var reopened = DocJson.Load(path);

            AssertKept(DocJson.Serialize(reopened), "saved and reopened");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Undo snapshots are clones: an undo must not lose what the file carried.</summary>
    [Fact]
    public void ACloneKeepsWhatTheFileCarried()
    {
        var opened = DocJson.Deserialize(WithUnknownKeys(Sample()));

        AssertKept(DocJson.Serialize(opened.Clone()), "cloned");
        Assert.Equal(DocJson.Serialize(DocJson.Clone(opened)), DocJson.Serialize(opened.Clone()));
    }

    [Fact]
    public void ADocumentWithNothingUnknownWritesNothingNew()
    {
        var json = DocJson.Serialize(Sample());
        Assert.DoesNotContain(Marker, json);
        Assert.DoesNotContain("\"unknown\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(json, DocJson.Serialize(DocJson.Deserialize(json)));
    }

    /// <summary>
    /// Every record type a document reaches keeps unknown keys, so the next field
    /// a later build adds anywhere is kept too — a sweep, because a checklist of
    /// the nine places above would be right on the day it was written.
    /// </summary>
    [Fact]
    public void EveryRecordTypeADocumentReachesKeepsUnknownKeys()
    {
        // Read and written by hand-rolled converters, which keep them themselves
        // (the round trip above checks both).
        var byHand = new HashSet<Type> { typeof(Frame), typeof(Symbol) };

        var missing = new List<string>();
        foreach (var type in Reachable(typeof(Doc)))
        {
            // Structs are values with fixed shapes — a point, an offset — and an
            // extra field on StrokePoint would be paid on every point of every
            // stroke. A later build adding a field to one is a format change.
            if (type.IsValueType || byHand.Contains(type)) continue;
            var holds = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.GetCustomAttribute<JsonExtensionDataAttribute>() is not null);
            if (!holds) missing.Add(type.Name);
        }
        foreach (var m in missing) output.WriteLine(m);
        Assert.True(missing.Count == 0,
            $"{missing.Count} record type(s) drop keys a newer build wrote: {string.Join(", ", missing)}");
    }

    private static IEnumerable<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>();
        var todo = new Queue<Type>();
        todo.Enqueue(root);
        while (todo.Count > 0)
        {
            var type = todo.Dequeue();
            if (!seen.Add(type)) continue;
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || p.GetMethod is null) continue;
                if (p.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;
                foreach (var t in Inner(p.PropertyType))
                {
                    if (t.Namespace?.StartsWith("Lightbox", StringComparison.Ordinal) == true && !t.IsEnum) todo.Enqueue(t);
                }
            }
        }
        return seen;

        static IEnumerable<Type> Inner(Type t)
        {
            if (Nullable.GetUnderlyingType(t) is { } u) t = u;
            if (t.IsArray)
            {
                foreach (var x in Inner(t.GetElementType()!)) yield return x;
                yield break;
            }
            if (t.IsGenericType)
            {
                foreach (var a in t.GetGenericArguments())
                {
                    foreach (var x in Inner(a)) yield return x;
                }
                if (t.Namespace?.StartsWith("System", StringComparison.Ordinal) == true) yield break;
            }
            yield return t;
        }
    }
}
