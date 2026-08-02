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
            Palettes = [DefaultPalette()],
        };
    }

    /// <summary>Ids for the two swatches every document starts with.</summary>
    public const string BlackSwatchId = "swatch_black";

    public const string WhiteSwatchId = "swatch_white";

    /// <summary>
    /// Black and white, in a palette, from the first moment.
    /// </summary>
    /// <remarks>
    /// This is the one place the "absent unless asked for" rule does not
    /// apply, and deliberately. A palette swatch is not a feature you opt
    /// into — it is the difference between a stroke that carries a colour and
    /// a stroke that carries a <i>reference</i>, and only the second one can
    /// be recoloured later. Starting with an empty palette means the first
    /// hour of work is painted in literals that can never follow a palette
    /// edit, and there is no way to fix that afterwards short of repainting.
    ///
    /// So the floor is the two colours every drawing already uses. They cost
    /// two entries in the file and they mean the live-palette machinery is on
    /// from the first stroke.
    /// </remarks>
    public static Palette DefaultPalette() => new()
    {
        Name = "Default",
        Swatches =
        [
            new Swatch { Id = BlackSwatchId, Name = "Black", Color = "#000000" },
            new Swatch { Id = WhiteSwatchId, Name = "White", Color = "#ffffff" },
        ],
    };

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
