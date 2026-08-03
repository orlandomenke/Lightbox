using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Projects;

/// <summary>
/// Templates: making one, starting from one, and pulling a changed one forward.
/// </summary>
/// <remarks>
/// <para>
/// Q12, built to <c>docs/DESIGN-templates.md</c>. A template is an ordinary
/// document with <see cref="Doc.IsTemplate"/> set — not a file type, not a
/// folder, not a list in the app — and the rule everything else rests on is
/// that <b>a template is copied, never referenced</b>. That is the whole
/// difference from a symbol, which is a live link.
/// </para>
/// <para>
/// Because the copy has no link back, editing a template can never reach into
/// an animation somebody already started. There is nothing to lock, nothing to
/// version, and no dialog asking whether to propagate. <em>Update from
/// template</em> is the artist reaching the other way, on purpose, one document
/// at a time.
/// </para>
/// </remarks>
public static class Templates
{
    /// <summary>Every template in the project, in tree order.</summary>
    /// <remarks>
    /// Loads each document to read its flag, which is the one place the folder
    /// layout's promise is broken on purpose: the flag lives in the document
    /// because that is what lets an artist mark work they have already done,
    /// and finding the marked ones therefore costs a read. Loads are cached by
    /// <see cref="ProjectIo.LoadDocument"/>, so the price is paid once.
    /// </remarks>
    public static List<DocumentRef> InProject(Project project)
    {
        var found = new List<DocumentRef>();
        foreach (var reference in AllRefs(project))
        {
            if (ProjectIo.LoadDocument(project, reference) is { IsTemplateDocument: true }) found.Add(reference);
        }
        return found;
    }

    private static IEnumerable<DocumentRef> AllRefs(Project project)
    {
        foreach (var character in project.Manifest.Characters)
        {
            foreach (var animation in character.Animations) yield return animation;
        }
        foreach (var document in project.Manifest.Documents) yield return document;
        // Null until the project has a shot, per the manifest's own rule.
        foreach (var scene in project.Manifest.Scenes ?? [])
        {
            foreach (var shot in scene.Shots) yield return shot;
        }
    }

    /// <summary>
    /// A fresh document copied from a template, and the copy is the deliverable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three things happen beyond the copy, and each is load-bearing:
    /// the copy is <b>not</b> a template itself (or every animation started
    /// from one would show up in the template list); it records
    /// <see cref="Doc.TemplateId"/> so it can be asked to pull later; and it
    /// keeps the template's <b>layer ids</b>, which is what makes matching in
    /// <see cref="Preview"/> a fact rather than a name-similarity guess.
    /// </para>
    /// <para>
    /// Everything else about the copy is independent from the first stroke. It
    /// renders with the template deleted, and editing the template afterwards
    /// leaves it alone — the test that guards this design is exactly that.
    /// </para>
    /// </remarks>
    public static Doc NewFromTemplate(Doc template, string? templateId = null)
    {
        var copy = DocJson.Clone(template);
        copy.IsTemplate = null;
        copy.TemplateId = templateId ?? template.TemplateId;
        return copy;
    }

    /// <summary>Mark or unmark a document as a template.</summary>
    /// <remarks>
    /// Clearing sets null rather than false, so an unmarked document carries no
    /// template key at all — the same reason the property is nullable.
    /// </remarks>
    public static void SetTemplate(Doc doc, bool isTemplate) => doc.IsTemplate = isTemplate ? true : null;

    // ---- the pull ------------------------------------------------------------

    /// <summary>One layer the template has and the document does not.</summary>
    /// <param name="Index">Where it sits in the template's stack.</param>
    public sealed record NewLayer(Layer Layer, int Index);

    /// <summary>
    /// A layer both documents have, whose properties the template has changed.
    /// </summary>
    /// <param name="DrawnOnSince">
    /// Whether the artist has drawn on their copy of this layer. The
    /// load-bearing signal: a layer with work on it is skipped unless the
    /// artist ticks it explicitly, so a pull cannot quietly rename or re-blend
    /// a layer somebody has been painting on.
    /// </param>
    public sealed record LayerChange(
        string LayerId,
        string From,
        string To,
        bool NameDiffers,
        bool BlendDiffers,
        bool OpacityDiffers,
        bool LockDiffers,
        bool DrawnOnSince);

    /// <summary>Everything a pull would change, before any of it is applied.</summary>
    public sealed record PullPreview(
        IReadOnlyList<NewLayer> NewLayers,
        IReadOnlyList<LayerChange> LayerChanges,
        bool GuidesDiffer,
        bool FpsDiffers,
        int TemplateFps,
        bool CameraAvailable)
    {
        /// <summary>Whether there is anything to pull at all.</summary>
        public bool Any =>
            NewLayers.Count > 0 || LayerChanges.Count > 0 || GuidesDiffer || FpsDiffers || CameraAvailable;
    }

    /// <summary>What the artist ticked.</summary>
    /// <param name="DrawnOnLayerIds">
    /// Layers with work on them that the artist ticked anyway. Empty means the
    /// safe default: skip every layer that has been drawn on.
    /// </param>
    public sealed record PullOptions(
        bool NewLayers = true,
        bool LayerProperties = true,
        bool Guides = true,
        bool Fps = true,
        bool Camera = true,
        IReadOnlySet<string>? DrawnOnLayerIds = null);

    /// <summary>
    /// What <see cref="Apply"/> would do, so the artist sees it before it happens.
    /// </summary>
    public static PullPreview Preview(Doc doc, Doc template)
    {
        var mine = doc.Scene.Layers.ToDictionary(l => l.Id, StringComparer.Ordinal);

        var newLayers = new List<NewLayer>();
        var changes = new List<LayerChange>();
        for (var i = 0; i < template.Scene.Layers.Count; i++)
        {
            var theirs = template.Scene.Layers[i];
            if (!mine.TryGetValue(theirs.Id, out var ours))
            {
                newLayers.Add(new NewLayer(theirs, i));
                continue;
            }

            var name = !string.Equals(ours.Name, theirs.Name, StringComparison.Ordinal);
            var blend = ours.BlendMode != theirs.BlendMode;
            // Opacity is a double: compare with a tolerance rather than for
            // equality, or a value that round-tripped through JSON would report
            // as a change forever and the preview would never be empty.
            var opacity = Math.Abs(ours.Opacity - theirs.Opacity) > 1e-9;
            var locked = ours.Locked != theirs.Locked;
            if (!name && !blend && !opacity && !locked) continue;

            changes.Add(new LayerChange(
                theirs.Id, ours.Name, theirs.Name, name, blend, opacity, locked, DrawnOn(ours, theirs)));
        }

        return new PullPreview(
            newLayers,
            changes,
            GuidesDiffer: !SameGuides(doc.Scene.Guides, template.Scene.Guides),
            FpsDiffers: doc.Scene.Fps != template.Scene.Fps,
            TemplateFps: template.Scene.Fps,
            // Never overwritten, so there is only something to offer when the
            // document has no camera and the template does.
            CameraAvailable: doc.Scene.Camera is null && template.Scene.Camera is not null);
    }

    /// <summary>
    /// Pull the ticked changes in. Returns how many things changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never touches a drawing on a layer the document already has, and never
    /// touches the exposure sheet.</b> Those are the work. A template's frames
    /// were superseded the moment somebody drew, and its timing is what a timing
    /// preset is for — which is why Q11 and Q12 stayed separate mechanisms.
    /// </para>
    /// <para>
    /// One exception, and it is a refinement of that rule rather than a hole in
    /// it: a layer the template has and the document does <em>not</em> arrives
    /// with its drawings. Nothing can be overwritten by a layer that is not
    /// there, and the case the design names — construction guides on their own
    /// locked layer — is worthless as an empty layer. The rule exists to protect
    /// an artist's work; a new layer cannot threaten any.
    /// </para>
    /// <para>
    /// Caller's job to wrap this in one undo step. It mutates
    /// <paramref name="doc"/> in place and deep-copies everything it takes, so
    /// the template cannot be reached through the result.
    /// </para>
    /// </remarks>
    public static int Apply(Doc doc, Doc template, PullOptions options)
    {
        var preview = Preview(doc, template);
        var ticked = options.DrawnOnLayerIds ?? new HashSet<string>(StringComparer.Ordinal);
        var changed = 0;

        if (options.NewLayers)
        {
            // Ascending, so each insert lands at the template's index with the
            // earlier ones already in place. Clamped because the document's
            // stack is shorter until they are all in.
            foreach (var addition in preview.NewLayers.OrderBy(n => n.Index))
            {
                doc.Scene.Layers.Insert(
                    Math.Clamp(addition.Index, 0, doc.Scene.Layers.Count),
                    CloneLayer(addition.Layer));
                changed++;
            }
        }

        if (options.LayerProperties)
        {
            var theirs = template.Scene.Layers.ToDictionary(l => l.Id, StringComparer.Ordinal);
            foreach (var change in preview.LayerChanges)
            {
                if (change.DrawnOnSince && !ticked.Contains(change.LayerId)) continue;
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == change.LayerId) is not { } ours) continue;
                if (!theirs.TryGetValue(change.LayerId, out var from)) continue;

                ours.Name = from.Name;
                ours.BlendMode = from.BlendMode;
                ours.Opacity = from.Opacity;
                ours.Locked = from.Locked;
                changed++;
            }
        }

        if (options.Guides && preview.GuidesDiffer)
        {
            // Replaced wholesale: guides are aids, not art. Null stays null so
            // a template without guides does not give the document an empty
            // list where it had none.
            doc.Scene.Guides = template.Scene.Guides is null
                ? null
                : template.Scene.Guides.Select(CloneGuide).ToList();
            changed++;
        }

        if (options.Fps && preview.FpsDiffers)
        {
            // Fps yes, size no. Changing a canvas under finished drawings is a
            // different operation with its own questions.
            doc.Scene.Fps = template.Scene.Fps;
            changed++;
        }

        if (options.Camera && preview.CameraAvailable)
        {
            doc.Scene.Camera = CloneCamera(template.Scene.Camera!);
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// Whether the artist has drawn on their copy of a layer since it arrived.
    /// </summary>
    /// <remarks>
    /// Compares <b>stroke ids</b>, so it is exact in both directions: a stroke
    /// the document has and the template does not is new work, and one the
    /// template has and the document does not was erased. Either way the layer
    /// has been worked on and a pull leaves its properties alone. Counting
    /// strokes would miss the artist who deleted one and drew one.
    /// </remarks>
    private static bool DrawnOn(Layer ours, Layer theirs)
    {
        var before = StrokeIds(theirs);
        var now = StrokeIds(ours);
        return !now.SetEquals(before);
    }

    private static HashSet<string> StrokeIds(Layer layer)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cel in layer.Cels)
        {
            switch (cel.Frame)
            {
                case VectorFrame v:
                    foreach (var s in v.Strokes) ids.Add(s.Id);
                    break;
                case PaintedFrame p:
                    foreach (var s in p.Strokes) ids.Add(s.Id);
                    // Imported pixels have no stroke provenance, so they cannot
                    // be compared by id. Their presence is itself content, and a
                    // synthetic marker is what keeps "drawn on" honest for a
                    // layer whose art arrived as a baseline PNG.
                    if (p.PngBase64.Length > 0) ids.Add($"baseline:{p.Id}:{p.PngBase64.Length}");
                    break;
            }
        }
        return ids;
    }

    /// <summary>
    /// Whether two guide sets are the same rig — grids included, since a grid is
    /// a <see cref="GuideKind.Grid"/> guide rather than a thing of its own.
    /// </summary>
    /// <remarks>
    /// Compared as serialized text rather than field by field. Guides are few,
    /// so the cost is nothing, and a field added to <see cref="Guide"/> is
    /// covered without anybody remembering this method exists — where a
    /// hand-written comparison would quietly start reporting "no change" for
    /// whatever it had not heard of.
    /// </remarks>
    private static bool SameGuides(List<Guide>? a, List<Guide>? b)
    {
        if ((a is null || a.Count == 0) && (b is null || b.Count == 0)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        return string.Equals(
            System.Text.Json.JsonSerializer.Serialize(a, DocJson.Options),
            System.Text.Json.JsonSerializer.Serialize(b, DocJson.Options),
            StringComparison.Ordinal);
    }

    // Round-tripping through JSON is the project's one deep-copy path already.
    // A layer needs it rather than a memberwise copy: its cels hold polymorphic
    // frames, and only the document's own converter can rebuild those.
    private static Layer CloneLayer(Layer layer) => DocJson.CloneValue(layer);

    private static Guide CloneGuide(Guide guide) => guide.Clone();

    private static Camera CloneCamera(Camera camera) => DocJson.CloneValue(camera);
}
