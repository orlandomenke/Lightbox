namespace Lightbox.Core.Documents;

/// <summary>
/// An effect tuned once and used again — its <em>parameters</em>, not its
/// drawings.
/// </summary>
/// <remarks>
/// <para>
/// <b>A preset and a symbol answer different questions, and conflating them is
/// the trap</b> (Q129). A symbol stores the baked frames: free to place, and
/// every placement identical — "that explosion again, three times, offset four
/// frames". A preset stores what made them: it costs a re-simulation, and in
/// exchange the new one can be forty frames instead of twenty-four, twice the
/// size, and blowing left. Both are wanted.
/// </para>
/// <para>
/// <b>The unit is a group, not an element</b>, because an explosion is a flash
/// and a fireball and a roll of smoke with four frames between them. Saving one
/// element would save a third of an effect and lose the timing that makes it
/// read as one event.
/// </para>
/// <para>
/// <b>Stored relative, so it can be put somewhere.</b> Capture subtracts the
/// group's own corner and its first frame, so what is kept is the shape of the
/// effect rather than where it happened to be when somebody liked it. The
/// offsets <em>between</em> members survive untouched, since those are the
/// effect.
/// </para>
/// <para>
/// <b>A library is a source to choose from, not a live dependency</b> — the
/// decision <c>SymbolScopes</c> already took and for the same reason. Using a
/// preset copies its parameters into the document, so the document keeps working
/// with the library gone, and editing the preset afterwards does not reach back
/// into effects already made from it.
/// </para>
/// </remarks>
public sealed class EffectPreset
{
    public string Id { get; set; } = Ids.NewId("fxpreset");

    public string Name { get; set; } = "Effect";

    /// <summary>
    /// What the artist filed it under. Absent until they tag one.
    /// </summary>
    /// <remarks>
    /// Free text, on <see cref="Symbol.Tags"/>'s reasoning: the categories that
    /// matter are the ones the work has — "act two", "that dragon", "small" —
    /// and no list written here would hold them.
    /// </remarks>
    public List<string>? Tags { get; set; }

    /// <summary>The elements, in drawing order, with their origins relative to the effect's corner.</summary>
    public List<SimElement> Elements { get; set; } = [];

    /// <summary>
    /// The shared line treatments its elements name, carried along so the look
    /// survives the move.
    /// </summary>
    /// <remarks>
    /// Absent when every element states its own. A preset whose elements named
    /// a treatment the destination document has never heard of would resolve to
    /// the defaults — the same line, plain, which is the one failure that looks
    /// like the preset simply being wrong.
    /// </remarks>
    public Dictionary<string, LineTreatment>? Treatments { get; set; }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public EffectPreset Clone()
    {
        var copy = (EffectPreset)MemberwiseClone();
        copy.Tags = Tags is null ? null : [.. Tags];
        copy.Elements = Elements.Select(e => e.Clone()).ToList();
        copy.Treatments = Treatments?.ToDictionary(t => t.Key, t => t.Value.Clone());
        return copy;
    }
}

/// <summary>
/// Capturing an effect into a preset, and making one again from it.
/// </summary>
/// <remarks>
/// Pure document surgery — no rendering, no view model, no file — so it is
/// testable without a window and reachable from the MCP surface when that
/// arrives, the same shape <see cref="SimGroupOps"/> takes.
/// </remarks>
public static class EffectPresets
{
    /// <summary>
    /// Keep a group's parameters under a name.
    /// </summary>
    /// <remarks>
    /// <b>Layer references do not travel</b>, and that is the one thing about
    /// this worth knowing. <see cref="Emitter.MaskLayerId"/> and
    /// <see cref="SimElement.ObstacleLayerId"/> name layers in <em>this</em>
    /// document; carried into another they would name nothing, and an emitter
    /// pointed at a layer that is not there emits nothing at all — a preset that
    /// looks fine until somebody bakes. So the id is dropped and the layer's
    /// <em>name</em> is kept as a hint, which <see cref="Instantiate"/> tries to
    /// match. A production that calls the cloak layer "cloak" in every scene
    /// gets it reconnected; anything else gets an emitter with no mask and is
    /// told so, which is a visible problem rather than a silent one.
    /// </remarks>
    public static EffectPreset Capture(Doc doc, SimGroup group, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(group);

        var members = SimGroupOps.Members(doc, group);
        var preset = new EffectPreset
        {
            Name = string.IsNullOrWhiteSpace(name) ? group.Name : name.Trim(),
        };
        if (members.Count == 0) return preset;

        var originX = members.Min(e => e.OriginX);
        var originY = members.Min(e => e.OriginY);
        var first = members.Min(e => e.FirstFrame);

        foreach (var member in members)
        {
            var copy = member.Clone();
            copy.OriginX -= originX;
            copy.OriginY -= originY;
            copy.FirstFrame -= first;

            copy.ObstacleLayerId = NameOfLayer(doc, copy.ObstacleLayerId);
            foreach (var emitter in copy.Emitters)
            {
                emitter.MaskLayerId = NameOfLayer(doc, emitter.MaskLayerId);
            }

            if (copy.TreatmentId is { } id && doc.LineTreatments?.TryGetValue(id, out var shared) == true)
            {
                (preset.Treatments ??= [])[id] = shared.Clone();
            }

            preset.Elements.Add(copy);
        }

        return preset;
    }

    /// <summary>
    /// Make the effect again in a document, at a place and a frame.
    /// </summary>
    /// <returns>The new group, or null if the preset holds no elements.</returns>
    public static SimGroup? Instantiate(
        Doc doc, EffectPreset preset, double originX = 0, double originY = 0, int firstFrame = 0)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(preset);
        if (preset.Elements.Count == 0) return null;

        // Shared treatments keep their ids, so two effects made from one preset
        // share the look rather than growing a copy each — SymbolScopes' rule,
        // and the reason a placement made today still resolves.
        foreach (var (id, treatment) in preset.Treatments ?? [])
        {
            (doc.LineTreatments ??= [])[id] = treatment.Clone();
        }

        var made = new List<SimElement>(preset.Elements.Count);
        foreach (var stored in preset.Elements)
        {
            var element = stored.Clone();
            element.Id = Ids.NewId("sim");
            element.OriginX += originX;
            element.OriginY += originY;
            element.FirstFrame += firstFrame;

            element.ObstacleLayerId = LayerNamed(doc, element.ObstacleLayerId);
            foreach (var emitter in element.Emitters)
            {
                emitter.MaskLayerId = LayerNamed(doc, emitter.MaskLayerId);
                // A fresh id per emitter too: two effects from one preset in the
                // same document would otherwise share emitter ids, and the
                // window resolves the selected emitter by id.
                emitter.Id = Ids.NewId("em");
            }

            (doc.Sims ??= [])[element.Id] = element;
            made.Add(element);
        }

        return SimGroupOps.Create(doc, preset.Name, made);
    }

    /// <summary>
    /// Which layer references a preset could not reconnect, by the name it was
    /// looking for.
    /// </summary>
    /// <remarks>
    /// Asked <em>before</em> instantiating, so a window can say what will be
    /// missing rather than reporting it after the fact. Distinct names, in the
    /// order they appear, because "cloak, cloak, cloak" tells nobody anything.
    /// </remarks>
    public static List<string> MissingLayers(Doc doc, EffectPreset preset)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(preset);

        var missing = new List<string>();
        foreach (var element in preset.Elements)
        {
            Want(element.ObstacleLayerId);
            foreach (var emitter in element.Emitters) Want(emitter.MaskLayerId);
        }
        return missing;

        void Want(string? name)
        {
            if (name is null || missing.Contains(name)) return;
            if (LayerNamed(doc, name) is null) missing.Add(name);
        }
    }

    private static string? NameOfLayer(Doc doc, string? id) =>
        id is null ? null : doc.Scene.Layers.FirstOrDefault(l => l.Id == id)?.Name;

    private static string? LayerNamed(Doc doc, string? name) =>
        name is null ? null : doc.Scene.Layers.FirstOrDefault(l => l.Name == name)?.Id;
}
