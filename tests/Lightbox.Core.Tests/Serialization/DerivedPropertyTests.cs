using System.Reflection;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests.Serialization;

/// <summary>
/// A convenience getter beside a field is a serialized property.
/// </summary>
/// <remarks>
/// <para>
/// <c>System.Text.Json</c> writes any public getter, so a derived property
/// added for readability quietly becomes a key in every document — under a
/// second name, carrying a value nothing reads back, and typically saying the
/// opposite of what the nullable field beside it was made nullable to avoid.
/// <c>BlendOrNormal</c> was the first (it wrote <c>"blendOrNormal": "normal"</c>
/// on every stroke); these four were found the same way, by serializing a
/// document and looking rather than by reading the model.
/// </para>
/// <para>
/// The per-property tests below name what each one cost. The sweep at the end
/// is what stops the fifth.
/// </para>
/// </remarks>
public class DerivedPropertyTests
{
    /// <summary>A document holding one of everything that has a derived getter.</summary>
    private static Doc Populated()
    {
        var doc = new Doc();
        doc.Scene.GhostFrames = [1];
        doc.Scene.Guides = [new Guide { Kind = GuideKind.Line, Angle = 30 }];
        doc.Scene.References = [new ReferenceStrip { Cells = [new ReferenceCell { PivotX = 4 }] }];
        doc.Gradients["g"] = new Gradient { AlphaStops = [] };
        doc.Symbols = new Dictionary<string, Symbol> { ["s"] = new() };
        return doc;
    }

    [Fact]
    public void APinnedFrameDoesNotAlsoWriteAFlagSayingSo()
    {
        var json = DocJson.Serialize(Populated());

        // GhostFrames is nullable and absent until used, precisely so a
        // document that pins nothing carries no machinery for pinning. The
        // flag beside it wrote "hasGhostFrames": false onto every document
        // ever saved, which is the whole saving undone.
        Assert.DoesNotContain("\"hasGhostFrames\"", json);
        Assert.Contains("\"ghostFrames\"", json);
    }

    [Fact]
    public void AGuideDoesNotCarryTheAnglesDerivedFromItsKind()
    {
        var json = DocJson.Serialize(Populated());

        // The worst of the four: not a bool but an array, recomputed from Kind
        // and Angle on load anyway, written once per guide.
        Assert.DoesNotContain("\"angles\"", json);
    }

    [Fact]
    public void TheOtherTwoDerivedFlagsAreGoneToo()
    {
        var json = DocJson.Serialize(Populated());

        Assert.DoesNotContain("\"hasAlphaTrack\"", json);
        Assert.DoesNotContain("\"hasPivot\"", json);
        // Symbol.FrameCount is derived from Frames.Count, and a stored copy of
        // a list's length is a second source of truth for the same fact.
        Assert.DoesNotContain("\"frameCount\": 1,\n      \"frames\"", json);
    }

    [Fact]
    public void ADerivedPropertyOnTheDocumentModelIsMarkedAsOne()
    {
        // The sweep. Anything on a serialized document type with a getter and
        // no setter either carries [JsonIgnore] or is a key in the file — and
        // the second is almost never what was meant.
        var types = typeof(Doc).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Lightbox.Core.Documents" && t is { IsPublic: true, IsClass: true });

        var leaks = new List<string>();
        foreach (var type in types)
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanWrite || property.GetIndexParameters().Length > 0) continue;
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
                leaks.Add($"{type.Name}.{property.Name}");
            }

        Assert.True(
            leaks.Count == 0,
            "These derive from other fields and will be written into every document that "
            + $"has one: {string.Join(", ", leaks)}. Mark them [JsonIgnore].");
    }
}
