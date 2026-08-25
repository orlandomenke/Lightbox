namespace Lightbox.Core.Documents;

/// <summary>
/// A character sheet: reference art that belongs to the document but lives
/// OUTSIDE the timeline. A sheet holds named views of the subject
/// (front/side/three-quarter/…); each view is a full layer stack
/// (lines/fill/shading/detail) of single-frame layers. Views are what the
/// artist consults — and what the AI is shown — to keep frames on-model and
/// to fill regions a pose reveals.
/// </summary>
public sealed class ReferenceSheet
{
    public string Id { get; set; } = Ids.NewId("sheet");

    public string Name { get; set; } = "Character";

    public List<ReferenceView> Views { get; set; } = [];

    /// <summary>A copy holding no reference in common with this one.</summary>
    public ReferenceSheet Clone()
    {
        var copy = (ReferenceSheet)MemberwiseClone();
        copy.Views = Views.Select(v => v.Clone()).ToList();
        return copy;
    }
}

/// <summary>One view of the subject (e.g. "side"), with its own layer stack.</summary>
public sealed class ReferenceView
{
    public string Id { get; set; } = Ids.NewId("view");

    public string Name { get; set; } = "view";

    public int Width { get; set; } = 960;

    public int Height { get; set; } = 540;

    /// <summary>Layer stack; each layer uses a single cel (no timeline).</summary>
    public List<Layer> Layers { get; set; } = [];

    /// <summary>
    /// Guides placed on this view, or null — absent unless used, exactly like
    /// <see cref="Scene.Guides"/>.
    /// </summary>
    /// <remarks>
    /// A sheet view is edited through a wrapper document built fresh on every
    /// open, so anything that should survive has to live on this record. The
    /// layers always did; the guides landed on the wrapper's scene and
    /// evaporated with it — reported as "guides are gone in reference sheet
    /// documents" (B287). A construction rig on a model sheet is exactly the
    /// kind of guide that has to be there tomorrow.
    /// </remarks>
    public List<Guide>? Guides { get; set; }

    /// <summary>A view with one painted layer, ready to draw on.</summary>
    public static ReferenceView Create(string name, int width, int height) => new()
    {
        Name = name,
        Width = width,
        Height = height,
        Layers =
        [
            new Layer
            {
                Name = "Lines",
                Kind = LayerKind.Painted,
                Cels = [new Cel { Frame = new Frame() }],
            },
        ],
    };

    /// <summary>A copy holding no reference in common with this one.</summary>
    public ReferenceView Clone()
    {
        var copy = (ReferenceView)MemberwiseClone();
        copy.Layers = Layers.Select(l => l.Clone()).ToList();
        copy.Guides = Guides?.Select(g => g.Clone()).ToList();
        return copy;
    }
}
