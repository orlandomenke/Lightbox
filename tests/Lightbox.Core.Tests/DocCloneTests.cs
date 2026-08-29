using System.Collections;
using System.Reflection;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// <see cref="Doc.Clone"/> copies everything and shares nothing.
/// </summary>
/// <remarks>
/// <para>
/// B142 replaced the undo snapshot's <c>DocJson.Clone</c> — serialize the whole
/// document to JSON, parse it back — with a direct graph walk, because the
/// round-trip cost 615 ms on a 5 000-stroke painting and every structural edit paid
/// it. The risk that buys is not slowness, it is <b>silence</b>: a reference member
/// nobody deepened leaves the snapshot sharing an object with the live document, so
/// undo appears to work and quietly fails to restore that one thing.
/// </para>
/// <para>
/// <b>So neither test here enumerates fields.</b> One compares the fast clone
/// against <c>DocJson.Clone</c>, which is the known-correct implementation and stays
/// in the codebase for exactly this purpose. The other walks both object graphs in
/// parallel and asserts that no mutable object appears in both — exhaustive by
/// construction, and it keeps working as the model grows, which a hand-written
/// checklist does not.
/// </para>
/// </remarks>
public class DocCloneTests(ITestOutputHelper output)
{
    /// <summary>
    /// Types deliberately shared rather than copied, each for a stated reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept as a list the walker consults rather than as special cases inside it, so
    /// every exemption is visible in one place and has to be argued for. An entry
    /// added here without a reason is the defect this file exists to catch.
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="AnchorPoint"/> and <see cref="ShapeBox"/> are positional
    /// records — their only settable state is the constructor parameters, and the
    /// extra members are computed <c>[JsonIgnore]</c> getters. Nothing can mutate
    /// one, so a new dictionary over the same values is a complete copy. Both were
    /// found by this test rather than reasoned about in advance.</item>
    /// <item><see cref="BakedSample"/>'s own remarks record that it is "immutable in
    /// practice — nothing edits a baked sample, it is replaced or dropped", and that
    /// it can be a megabyte of base64. <see cref="Stroke.Clone"/> has always shared
    /// it.</item>
    /// <item><see cref="StrokeCheckpoint"/> is <see cref="BakedSample"/>'s argument
    /// exactly, one level up and larger: several megabytes of pixels, never edited —
    /// a checkpoint is replaced or dropped — and, unlike the others,
    /// <b>derived</b>, so the worst a shared reference can cost is a stale cache
    /// that the fingerprint refuses anyway. Copying it per undo snapshot would buy a
    /// guarantee nothing needs and pay megabytes a step for it.</item>
    /// </list>
    /// </remarks>
    private static readonly HashSet<Type> DeliberatelyShared =
        [typeof(AnchorPoint), typeof(ShapeBox), typeof(BakedSample), typeof(StrokeCheckpoint)];

    /// <summary>A document with something in every optional block, so nothing is untested by being absent.</summary>
    private static Doc Populated()
    {
        var doc = DocumentFactory.CreateDoc(320, 180, 24);

        var frame = doc.Scene.Layers[^1].Cels[0].Frame!;
        frame.PngBase64 = Convert.ToBase64String([9, 8, 7]);
        frame.Anchors = new Dictionary<string, AnchorPoint> { ["hand"] = new(12, 34) };
        frame.Shapes = new Dictionary<string, ShapeBox> { ["hit"] = new(1, 2, 3, 4) };
        frame.Placements = [new SymbolPlacement { SymbolId = "sym_1", X = 5, Y = 6 }];
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#ff8800",
            SwatchId = "sw_1",
            PaletteId = "pal_1",
            GradientId = "grad_1",
            Label = "left-arm",
            ClipId = "clip_1",
            Points = [new(1, 2, 0.5), new(3, 4, 0.9)],
            Holes = [[new(5, 6, 1)]],
            Brush = new BrushSettings
            {
                Size = 12,
                Curves = new Dictionary<BrushDynamic, ResponseCurve>
                {
                    [BrushDynamic.Size] = new() { Points = [new(0, 0), new(1, 1)] },
                },
                Stabilisation = new BrushStabilisation(),
            },
            Baked = new BakedSample { PngBase64 = "AAAA", X = 1, Y = 2 },
        });

        frame.Checkpoint = new StrokeCheckpoint
        {
            Strokes = 1, Fingerprint = "deadbeef", PixelsBase64 = "AAAA", Width = 8, Height = 8,
        };

        doc.Scene.Camera = new Camera { Keys = [new CameraKey { Frame = 0, X = 1 }] };
        doc.Scene.Markers.Add(new FrameMarker { Frame = 0, Color = "#ff0000" });
        doc.Scene.LayerGroups.Add(new LayerGroup { Name = "Character" });
        doc.Scene.FrameGroups = [new FrameGroup { PlacementIds = ["p1", "p2"] }];
        doc.Scene.GhostFrames = [1, 2];
        doc.Scene.Guides = [new Guide()];
        doc.Scene.Pivot = new Pivot();
        doc.Scene.Anchors = [new Anchor()];
        doc.Scene.Tags = [new AnimationTag { Name = "walk" }];
        doc.Scene.Shapes = [new CollisionShape()];
        doc.Scene.Audio = new AudioTrack();
        doc.Scene.References = [new ReferenceStrip { Cells = [new ReferenceCell()], Slots = [0, 1] }];

        doc.BrushTips["tip_1"] = "AAAA";
        doc.Textures = new Dictionary<string, string> { ["tex_1"] = "BBBB" };
        doc.ClipRegions["clip_1"] = new ClipRegion { Contours = [[new(0, 0, 1), new(9, 9, 1)]] };
        doc.Palettes.Add(new Palette { Name = "Skin", Swatches = [new Swatch { Color = "#ffddcc" }] });
        doc.PaletteFolders = [new PaletteFolder { Name = "Characters" }];
        doc.Gradients["grad_1"] = new Gradient
        {
            Stops = [new GradientStop { Color = "#000000" }],
            AlphaStops = [new GradientAlphaStop()],
        };
        doc.Symbols = new Dictionary<string, Symbol>
        {
            ["sym_1"] = new() { Name = "Sword", Tags = ["prop"], Layers = Symbol.Flat("Sword", [new Frame()]) },
        };
        doc.Features = new Dictionary<string, bool> { ["camera"] = true };
        doc.ReferenceSheets.Add(new ReferenceSheet
        {
            Views = [new ReferenceView { Layers = [new Layer { Cels = [new Cel { Frame = new Frame() }] }] }],
        });

        return doc;
    }

    [Fact]
    public void TheFastCloneMatchesTheSerializerItReplaced()
    {
        var doc = Populated();

        // DocJson.Clone is the implementation B142 removed from the undo path and
        // the reason this comparison is meaningful: if the graph walk drops or
        // alters anything, the two serializations diverge.
        Assert.Equal(DocJson.Serialize(DocJson.Clone(doc)), DocJson.Serialize(doc.Clone()));
    }

    [Fact]
    public void CloningChangesNothingAboutTheOriginal()
    {
        var doc = Populated();
        var before = DocJson.Serialize(doc);

        var clone = doc.Clone();
        Assert.Equal(before, DocJson.Serialize(doc));

        // And the copy is the same document rather than a similar one: a snapshot
        // that renamed ids would restore a record nothing else refers to.
        Assert.Equal(before, DocJson.Serialize(clone));
    }

    [Fact]
    public void NoMutableObjectAppearsInBothGraphs()
    {
        var doc = Populated();
        var clone = doc.Clone();

        var shared = new List<string>();
        Walk(doc, clone, "Doc", shared, []);

        foreach (var s in shared.Take(20)) output.WriteLine(s);
        Assert.True(shared.Count == 0,
            $"{shared.Count} object(s) are shared between a document and its clone, so undo would "
            + $"not restore them:\n{string.Join('\n', shared.Take(20))}");
    }

    /// <summary>
    /// Walk two graphs in lockstep, recording every place they hold the same
    /// instance.
    /// </summary>
    /// <remarks>
    /// Strings and value types are skipped — sharing an immutable object is not a
    /// defect, it is the point. Everything else that is reachable is checked, which
    /// is what makes this survive somebody adding a property next month.
    /// </remarks>
    private static void Walk(object? a, object? b, string path, List<string> shared, HashSet<object> seen)
    {
        if (a is null || b is null) return;
        var type = a.GetType();
        if (type.IsValueType || a is string) return;
        if (!seen.Add(a)) return;                       // cycles, and shared-by-design graphs

        if (ReferenceEquals(a, b))
        {
            if (!DeliberatelyShared.Contains(type)) shared.Add($"{path} : {type.Name}");
            return;                                     // no point descending into one object
        }

        if (a is IDictionary da && b is IDictionary db)
        {
            foreach (var key in da.Keys)
            {
                if (!db.Contains(key)) continue;
                Walk(da[key], db[key], $"{path}[{key}]", shared, seen);
            }
            return;
        }

        if (a is IEnumerable ea && b is IEnumerable eb)
        {
            var left = ea.Cast<object?>().ToList();
            var right = eb.Cast<object?>().ToList();
            for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
            {
                Walk(left[i], right[i], $"{path}[{i}]", shared, seen);
            }
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead) continue;
            object? va, vb;
            try
            {
                va = property.GetValue(a);
                vb = property.GetValue(b);
            }
            catch (Exception)
            {
                continue;                               // a computed property that throws is not state
            }
            Walk(va, vb, $"{path}.{property.Name}", shared, seen);
        }
    }
}
