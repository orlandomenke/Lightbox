namespace Lightbox.Core.Documents;

/// <summary>
/// Which drawings the rig moves, and which layer's binding moves each one —
/// answered by frame id, in constant time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> "Does the rig move this drawing?" used to be
/// <see cref="Frame.HasBoundStrokes"/>, a property of the frame, and every gate
/// in the render funnel asks it: the bitmap cache's key, the tile fallback, the
/// prewarmer, and the pose resolver itself. A layer-level binding (Q90) makes
/// the same question a property of the <em>layer</em>, and not one of those
/// call sites has a layer in its hand — threading one through every
/// <c>Get</c> in the application would have been a diff nobody could review.
/// </para>
/// <para>
/// <b>Built once, explicitly, never lazily.</b> A map that rebuilt itself when
/// it noticed the document had changed would be a cache with an invalidation
/// problem inside the render path, which is the one place a stale answer shows
/// up as wrong pixels somebody exports. The owner of a document builds one and
/// replaces it when the document changes; nothing here watches anything.
/// </para>
/// <para>
/// <b>An unrigged document pays one comparison.</b> <see cref="For"/> returns
/// <see cref="Empty"/> when no layer in the scene is rigged, and <c>Empty</c>
/// answers every question from <see cref="Frame.HasBoundStrokes"/> alone — so
/// the funnel behaves exactly as it did before layer bindings existed, which
/// is what makes this safe to put in front of every render.
/// </para>
/// </remarks>
public sealed class RigIndex
{
    /// <summary>Nothing is rigged by a layer; the frame's own strokes decide.</summary>
    public static readonly RigIndex Empty = new();

    private readonly Dictionary<string, Layer> _byFrameId = [];

    private RigIndex()
    {
    }

    /// <summary>Whether any layer binding is in play at all.</summary>
    /// <remarks>
    /// <c>[JsonIgnore]</c> on a type that is never serialized, and that is
    /// deliberate: <c>DerivedPropertyTests</c> sweeps every public getter in
    /// this namespace, and the rule is blunt on purpose — carving out an
    /// exception for "types that are not really document model" would weaken
    /// the guard that catches the next <c>BlendOrNormal</c> for everyone. One
    /// attribute is the cheaper end of that trade. Nothing here is written to
    /// a file: an index is rebuilt from the document, never stored beside it.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => _byFrameId.Count == 0;

    /// <summary>
    /// Index a document's rigged layers. Cheap and shared when none are.
    /// </summary>
    public static RigIndex For(Doc doc)
    {
        // The common case, and it must stay the cheap one: a scan of layer
        // flags, no allocation, and the shared instance back.
        var rigged = doc.Scene.Layers.Where(doc.Scene.IsLayerRigged).ToList();
        if (rigged.Count == 0) return Empty;

        var index = new RigIndex();
        foreach (var layer in rigged)
            foreach (var cel in layer.Cels)
                // A held cel is null and a drawing can be exposed many times;
                // the id is what the funnel has, so the id is the key.
                if (cel.Frame is { } frame) index._byFrameId[frame.Id] = layer;
        return index;
    }

    /// <summary>The layer whose binding moves this drawing, or null.</summary>
    public Layer? LayerOf(Frame frame) =>
        _byFrameId.TryGetValue(frame.Id, out var layer) ? layer : null;

    /// <summary>
    /// Whether this drawing's pixels depend on the playhead because of the rig
    /// — the question every gate in the render funnel is really asking.
    /// </summary>
    /// <remarks>
    /// Both halves matter and neither implies the other: a stroke can carry
    /// painted weights on a layer nobody rigged, and a rigged layer moves
    /// strokes that carry no weights of their own.
    /// </remarks>
    public bool IsPosed(Frame frame) => frame.HasBoundStrokes || _byFrameId.ContainsKey(frame.Id);
}
