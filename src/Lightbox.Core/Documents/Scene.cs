namespace Lightbox.Core.Documents;

public sealed class Scene
{
    public string Id { get; set; } = Ids.NewId("scene");

    public string Name { get; set; } = "Scene 1";

    public int Width { get; set; } = 960;

    public int Height { get; set; } = 540;

    public int Fps { get; set; } = 12;

    public int FrameCount { get; set; } = 1;

    public List<Layer> Layers { get; set; } = [];
}
