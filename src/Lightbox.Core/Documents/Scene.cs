namespace Lightbox.Core.Documents;

public sealed class Scene
{
    public string Id { get; set; } = Ids.NewId("scene");

    public string Name { get; set; } = "Scene 1";

    public int Width { get; set; } = 960;

    public int Height { get; set; } = 540;

    public int Fps { get; set; } = 12;

    public int FrameCount { get; set; } = 1;

    /// <summary>Paper color composited behind all layers.</summary>
    public string BackgroundColor { get; set; } = "#ffffff";

    /// <summary>Render with no paper at all (transparent PNG exports).</summary>
    public bool TransparentBackground { get; set; }

    /// <summary>Pixels per inch — metadata for future print/export work.</summary>
    public int Ppi { get; set; } = 72;

    public List<Layer> Layers { get; set; } = [];
}
