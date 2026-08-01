namespace Lightbox.Core.Documents;

public static class DocumentFactory
{
    /// <param name="paperColor">
    /// Hex paper colour, or null for a transparent document. When given, the
    /// document opens with a locked Background layer holding that colour as a
    /// full-canvas fill stroke — the paper is content, not a render setting.
    /// </param>
    public static Doc CreateDoc(int width = 960, int height = 540, int fps = 12, string? paperColor = null)
    {
        var layers = new List<Layer>();
        if (paperColor is not null) layers.Add(BackgroundLayer(width, height, paperColor));
        layers.Add(new Layer
        {
            Name = "Paint",
            Kind = LayerKind.Painted,
            Cels = [new Cel { Frame = new PaintedFrame() }],
        });
        return new Doc
        {
            Scene = new Scene
            {
                Width = width,
                Height = height,
                Fps = fps,
                FrameCount = 1,
                Layers = layers,
            },
        };
    }

    /// <summary>
    /// The paper, as a locked layer whose single stroke fills the canvas. A
    /// fill stroke rather than a baked bitmap, so it obeys the same rule as
    /// everything else: the record is the document, and a reload re-renders
    /// it rather than carrying pixels it cannot regenerate.
    /// </summary>
    public static Layer BackgroundLayer(int width, int height, string paperColor) => new()
    {
        Name = "Background",
        Kind = LayerKind.Painted,
        Locked = true,
        IsBackground = true,
        Cels =
        [
            new Cel
            {
                Frame = new PaintedFrame
                {
                    Strokes =
                    [
                        new Stroke
                        {
                            Tool = ToolKind.Fill,
                            Color = paperColor,
                            Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
                            Points =
                            [
                                new StrokePoint(0, 0, 1),
                                new StrokePoint(width, 0, 1),
                                new StrokePoint(width, height, 1),
                                new StrokePoint(0, height, 1),
                            ],
                        },
                    ],
                },
            },
        ],
    };
}
