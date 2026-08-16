using System.Text.Json;
using Lightbox.Ai;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai.Tests;

public class SchemaTests
{
    [Fact]
    public void Schemas_AreValidJson()
    {
        using var doc = JsonDocument.Parse(StrokeSchemas.InbetweenResult);
        Assert.Equal("object", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void Schemas_EveryObjectForbidsAdditionalProperties()
    {
        static void Walk(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    && t.GetString() == "object")
                {
                    Assert.True(el.TryGetProperty("additionalProperties", out var ap)
                                && ap.ValueKind == JsonValueKind.False,
                        "schema object without additionalProperties:false");
                }
                foreach (var prop in el.EnumerateObject()) Walk(prop.Value);
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray()) Walk(item);
            }
        }

        using var doc = JsonDocument.Parse(StrokeSchemas.InbetweenResult);
        Walk(doc.RootElement);
    }

    [Fact]
    public void Schemas_ContainNoNumericRangeConstraints()
    {
        // Structured outputs reject minimum/maximum — ranges are clamped
        // client-side instead.
        Assert.DoesNotContain("minimum", StrokeSchemas.InbetweenResult);
        Assert.DoesNotContain("maximum", StrokeSchemas.InbetweenResult);
    }
}

public class WireMappingTests
{
    private static readonly SceneInfo Scene = new(960, 540, 12);

    [Fact]
    public void ToWire_ResamplesLongStrokesAndRoundsCoords()
    {
        var stroke = new Stroke
        {
            Points = Enumerable.Range(0, 500)
                .Select(i => new StrokePoint(i * 1.23456, i * 0.98765, 0.512345))
                .ToList(),
        };
        var dto = StrokeWire.ToWire(stroke);

        Assert.Equal(StrokeWire.MaxWirePoints, dto.Points.Count);
        Assert.All(dto.Points, p =>
        {
            Assert.Equal(Math.Round(p.X, 1), p.X);
            Assert.Equal(Math.Round(p.Pressure, 2), p.Pressure);
        });
    }

    [Fact]
    public void ToWire_KeepsShortStrokesIntact()
    {
        var stroke = new Stroke { Points = [new(1, 2, 0.5), new(3, 4, 0.6)] };
        Assert.Equal(2, StrokeWire.ToWire(stroke).Points.Count);
    }

    [Fact]
    public void FromWire_ClampsEverything()
    {
        var dto = new StrokeWire.StrokeDto
        {
            Tool = "brush",
            Color = "#ff0000",
            Size = 9999,
            Hardness = 7,
            Opacity = -2,
            Points =
            [
                new() { X = -99999, Y = 99999, Pressure = 42 },
            ],
        };
        var s = StrokeWire.FromWire(dto, Scene)!;

        Assert.Equal(200, s.Brush.Size);
        Assert.Equal(1, s.Brush.Hardness);
        Assert.Equal(0, s.Brush.Opacity);
        Assert.Equal(1, s.Points[0].Pressure);
        Assert.Equal(-Scene.Width * StrokeWire.BoundsMarginFactor, s.Points[0].X);
        Assert.Equal(Scene.Height * (1 + StrokeWire.BoundsMarginFactor), s.Points[0].Y);
    }

    [Fact]
    public void FromWire_RejectsUnusableStrokes()
    {
        Assert.Null(StrokeWire.FromWire(new StrokeWire.StrokeDto { Points = [] }, Scene));
        Assert.Null(StrokeWire.FromWire(
            new StrokeWire.StrokeDto { Color = "not-a-color", Points = [new() { X = 1, Y = 1 }] },
            Scene));
    }

    /// <summary>
    /// B233, at the seam that matters: the wire has two tools and the document
    /// has six, so <c>ToWire</c> calls everything that is not an eraser a
    /// brush. For a fill that is lossy and honest; for a cleared region it is a
    /// phantom — a brush stroke tracing the outline of a space the artist
    /// emptied, charged for in tokens and reasoned about as if it were a mark.
    /// </summary>
    /// <remarks>
    /// Composed rather than unit-tested, because the fix is that the two are
    /// always composed: <c>EffectiveStrokes</c> runs first and there is no
    /// cleared region left for <c>ToWire</c> to mis-describe. A test of
    /// <c>ToWire</c> alone would pass on the broken build and on this one.
    /// </remarks>
    [Fact]
    public void AClearedRegionNeverReachesTheWire()
    {
        Stroke Line(double y) => new()
        {
            Brush = new BrushSettings { Size = 6 },
            Points = [new(20, y, 1), new(80, y, 1)],
        };
        var cleared = new Stroke
        {
            Tool = ToolKind.ClearRegion,
            Brush = new BrushSettings { Size = 1 },
            Points = [new(0, 0, 1), new(100, 0, 1), new(100, 100, 1), new(0, 100, 1)],
        };

        var wire = StrokeRecordCleaner
            .EffectiveStrokes([Line(50), Line(300), cleared])
            .Select(StrokeWire.ToWire)
            .ToList();

        // One stroke on the wire: the line outside the cleared square. Not the
        // line it emptied, and not the clear masquerading as a brush.
        var only = Assert.Single(wire);
        Assert.Equal("brush", only.Tool);
        Assert.Equal(300, only.Points[0].Y);
    }

    [Fact]
    public void FromWire_MapsEraserAndLabel()
    {
        var s = StrokeWire.FromWire(new StrokeWire.StrokeDto
        {
            Tool = "eraser",
            Label = "  ",
            Points = [new() { X = 1, Y = 1, Pressure = 0.5 }],
        }, Scene)!;
        Assert.Equal(ToolKind.Eraser, s.Tool);
        Assert.Null(s.Label); // whitespace label normalized away

        var labeled = StrokeWire.FromWire(new StrokeWire.StrokeDto
        {
            Label = "jaw",
            Points = [new() { X = 1, Y = 1, Pressure = 0.5 }],
        }, Scene)!;
        Assert.Equal("jaw", labeled.Label);
    }

    [Fact]
    public void FromWire_List_DropsBadKeepsGood()
    {
        var list = StrokeWire.FromWire(
        [
            new StrokeWire.StrokeDto { Points = [new() { X = 1, Y = 1 }] },
            new StrokeWire.StrokeDto { Points = [] },
        ], Scene);
        Assert.Single(list);
    }
}

public class PromptTests
{
    private static InbetweenRequest Request() => new(
        new SceneInfo(960, 540, 12),
        [new Stroke { Label = "arm", Points = [new(0, 0, 0.5), new(10, 0, 0.5)] }],
        [new Stroke { Label = "arm", Points = [new(0, 50, 0.5), new(10, 50, 0.5)] }],
        [0.25, 0.5, 0.75],
        Easing.EaseInOut);

    [Fact]
    public void InbetweenUser_ContainsSceneBoundsAndAllTs()
    {
        var prompt = Prompts.InbetweenUser(Request());
        Assert.Contains("\"width\":960", prompt);
        Assert.Contains("\"height\":540", prompt);
        Assert.Contains("0.25", prompt);
        Assert.Contains("0.5", prompt);
        Assert.Contains("0.75", prompt);
        Assert.Contains("keyframeA", prompt);
        Assert.Contains("keyframeB", prompt);
        Assert.Contains("easeinout", prompt.ToLowerInvariant());
        Assert.Contains("\"label\":\"arm\"", prompt);
    }

    [Fact]
    public void SystemPrompts_MentionCorePrinciples()
    {
        Assert.Contains("arcs", Prompts.InbetweenSystem);
        Assert.Contains("label", Prompts.InbetweenSystem);
    }
}

public class WireRoundTripTests
{
    [Fact]
    public void InbetweenResultDto_ParsesModelShapedJson()
    {
        var json = """
        {
          "inbetweens": [
            { "t": 0.5, "strokes": [
              { "tool": "brush", "color": "#112233", "size": 6, "hardness": 0.8,
                "opacity": 1, "label": "arm",
                "points": [ { "x": 5, "y": 25, "pressure": 0.5 } ] }
            ] }
          ]
        }
        """;
        var dto = JsonSerializer.Deserialize<StrokeWire.InbetweenResultDto>(json)!;
        var frame = Assert.Single(dto.Inbetweens);
        Assert.Equal(0.5, frame.T);
        var stroke = Assert.Single(frame.Strokes);
        Assert.Equal("#112233", stroke.Color);

        var core = StrokeWire.FromWire(frame.Strokes, new SceneInfo(100, 100, 12));
        Assert.Single(core);
        Assert.Equal("arm", core[0].Label);
    }
}
