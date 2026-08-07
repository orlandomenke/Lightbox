using System.Text.Json.Serialization;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Ai;

/// <summary>
/// Wire DTOs for exchanging strokes with the model, plus mapping to/from the
/// Core model. The wire shape mirrors the document format (camelCase), so the
/// model reads and writes "real" Lightbox strokes — with two adjustments for
/// token economy on the way out: strokes are resampled to at most
/// <see cref="MaxWirePoints"/> points and coordinates are rounded to one
/// decimal. Inbound payloads are validated and clamped, never trusted.
/// </summary>
public static class StrokeWire
{
    public const int MaxWirePoints = 32;

    /// <summary>Model output may overshoot the canvas a little; beyond this margin we clamp.</summary>
    public const double BoundsMarginFactor = 0.25;

    public sealed class PointDto
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("pressure")] public double Pressure { get; set; } = 0.5;
    }

    public sealed class StrokeDto
    {
        [JsonPropertyName("tool")] public string Tool { get; set; } = "brush";
        [JsonPropertyName("color")] public string Color { get; set; } = "#1a1a1a";
        [JsonPropertyName("size")] public double Size { get; set; } = 6;
        [JsonPropertyName("hardness")] public double Hardness { get; set; } = 0.8;
        [JsonPropertyName("opacity")] public double Opacity { get; set; } = 1;
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("points")] public List<PointDto> Points { get; set; } = [];
    }

    public sealed class InbetweenFrameDto
    {
        [JsonPropertyName("t")] public double T { get; set; }
        [JsonPropertyName("strokes")] public List<StrokeDto> Strokes { get; set; } = [];
    }

    public sealed class InbetweenResultDto
    {
        [JsonPropertyName("inbetweens")] public List<InbetweenFrameDto> Inbetweens { get; set; } = [];
    }

    // ---- outbound: Core -> wire --------------------------------------------

    public static StrokeDto ToWire(Stroke s)
    {
        var points = s.Points.Count > MaxWirePoints
            ? GeometryOps.Resample(s.Points, MaxWirePoints)
            : (IReadOnlyList<StrokePoint>)s.Points;
        return new StrokeDto
        {
            Tool = s.Tool == ToolKind.Eraser ? "eraser" : "brush",
            Color = s.Color,
            Size = Math.Round(s.Brush.Size, 1),
            Hardness = Math.Round(s.Brush.Hardness, 2),
            Opacity = Math.Round(s.Brush.Opacity, 2),
            Label = s.Label,
            Points = points.Select(p => new PointDto
            {
                X = Math.Round(p.X, 1),
                Y = Math.Round(p.Y, 1),
                Pressure = Math.Round(p.Pressure, 2),
            }).ToList(),
        };
    }

    // ---- inbound: wire -> Core (validated + clamped) ------------------------

    /// <summary>
    /// Convert a model-produced stroke into a Core stroke, clamping every
    /// channel into its valid range and the coordinates into the scene bounds
    /// (plus a margin). Returns null for unusable strokes (no points, bad color).
    /// </summary>
    public static Stroke? FromWire(StrokeDto dto, SceneInfo scene)
    {
        if (dto.Points.Count == 0) return null;

        string color;
        try
        {
            var (r, g, b) = ColorOps.HexToRgb(dto.Color);
            color = ColorOps.RgbToHex(r, g, b);
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            return null;
        }

        var marginX = scene.Width * BoundsMarginFactor;
        var marginY = scene.Height * BoundsMarginFactor;

        return new Stroke
        {
            Tool = dto.Tool == "eraser" ? ToolKind.Eraser : ToolKind.Brush,
            Color = color,
            Brush = new BrushSettings
            {
                Size = Math.Clamp(dto.Size, 0.5, 200),
                Hardness = Math.Clamp(dto.Hardness, 0, 1),
                Opacity = Math.Clamp(dto.Opacity, 0, 1),
                Spacing = 0.15,
            },
            Label = string.IsNullOrWhiteSpace(dto.Label) ? null : dto.Label,
            Points = dto.Points.Select(p => new StrokePoint(
                Math.Clamp(p.X, -marginX, scene.Width + marginX),
                Math.Clamp(p.Y, -marginY, scene.Height + marginY),
                Math.Clamp(p.Pressure, 0, 1))).ToList(),
        };
    }

    public static List<Stroke> FromWire(IEnumerable<StrokeDto> dtos, SceneInfo scene) =>
        dtos.Select(d => FromWire(d, scene)).Where(s => s is not null).Select(s => s!).ToList();
}
