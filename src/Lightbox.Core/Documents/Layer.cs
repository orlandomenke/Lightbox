namespace Lightbox.Core.Documents;

public enum LayerKind
{
    Painted,
    Vector,
}

/// <summary>
/// A cel on the timeline. <c>Frame == null</c> means "hold the previous
/// exposed frame" — the classic exposure-sheet model.
/// </summary>
public sealed class Cel
{
    public Frame? Frame { get; set; }
}

public sealed class Layer
{
    public string Id { get; set; } = Ids.NewId("layer");

    public string Name { get; set; } = "Layer";

    public LayerKind Kind { get; set; } = LayerKind.Painted;

    public bool Visible { get; set; } = true;

    public double Opacity { get; set; } = 1;

    /// <summary>One entry per timeline frame; a null Frame is a hold.</summary>
    public List<Cel> Cels { get; set; } = [];
}
