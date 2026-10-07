namespace Lightbox.App.Services;

/// <summary>
/// The drawings whose thumbnails are out of date — and the one place that says
/// so to the background thumbnail renderer (B397).
/// </summary>
/// <remarks>
/// Marking a thumbnail dirty is how every edit path, twenty of them, already
/// said "this drawing's picture changed". The worker needs exactly that fact to
/// refuse a render begun before the change, and the adversarial review found
/// paths (a symbol placement, a stroke appended in place) that marked the
/// thumbnail dirty without reaching the render-invalidation funnel. Hanging the
/// worker's generation bump on the mark itself means a path cannot do one
/// without the other.
/// </remarks>
internal sealed class DirtyThumbs
{
    private readonly HashSet<string> _ids = [];

    /// <summary>Told about every id added — the worker's generation bump.</summary>
    public Action<string>? Added { get; set; }

    public bool Add(string id)
    {
        Added?.Invoke(id);
        return _ids.Add(id);
    }

    public bool Contains(string id) => _ids.Contains(id);

    public bool Remove(string id) => _ids.Remove(id);

    public void Clear() => _ids.Clear();
}
