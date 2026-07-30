namespace Lightbox.Core.Documents;

public static class DocumentFactory
{
    public static Doc CreateDoc(int width = 960, int height = 540, int fps = 12)
    {
        var layer = new Layer
        {
            Name = "Paint",
            Kind = LayerKind.Painted,
            Cels = [new Cel { Frame = new PaintedFrame() }],
        };
        return new Doc
        {
            Scene = new Scene
            {
                Width = width,
                Height = height,
                Fps = fps,
                FrameCount = 1,
                Layers = [layer],
            },
        };
    }
}
