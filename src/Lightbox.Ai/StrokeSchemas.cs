namespace Lightbox.Ai;

/// <summary>
/// JSON Schemas for structured outputs. Constraints the API enforces are in
/// the schema; numeric ranges are NOT expressible here (structured-outputs
/// limitation), so <see cref="StrokeWire.FromWire(StrokeWire.StrokeDto, SceneInfo)"/>
/// clamps values after parse. Every object carries
/// <c>additionalProperties: false</c> as the API requires.
/// </summary>
public static class StrokeSchemas
{
    private const string StrokeSchema = """
        {
          "type": "object",
          "properties": {
            "tool": { "type": "string", "enum": ["brush", "eraser"] },
            "color": { "type": "string" },
            "size": { "type": "number" },
            "hardness": { "type": "number" },
            "opacity": { "type": "number" },
            "label": { "type": ["string", "null"] },
            "points": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "x": { "type": "number" },
                  "y": { "type": "number" },
                  "pressure": { "type": "number" }
                },
                "required": ["x", "y", "pressure"],
                "additionalProperties": false
              }
            }
          },
          "required": ["tool", "color", "size", "hardness", "opacity", "label", "points"],
          "additionalProperties": false
        }
        """;

    public static readonly string InbetweenResult = $$"""
        {
          "type": "object",
          "properties": {
            "inbetweens": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "t": { "type": "number" },
                  "strokes": { "type": "array", "items": {{StrokeSchema}} }
                },
                "required": ["t", "strokes"],
                "additionalProperties": false
              }
            }
          },
          "required": ["inbetweens"],
          "additionalProperties": false
        }
        """;
}
