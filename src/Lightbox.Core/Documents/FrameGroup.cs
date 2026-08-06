namespace Lightbox.Core.Documents;

/// <summary>
/// Marks placements that form an animated cycle created by expanding a multi-frame
/// symbol across the timeline. Allows UI to show, group, and edit frames together.
/// </summary>
public sealed class FrameGroup
{
    /// <summary>Unique identifier for this group.</summary>
    public string Id { get; set; } = Ids.NewId("fgrp");

    /// <summary>The symbol this group was created from.</summary>
    public string SymbolId { get; set; } = "";

    /// <summary>Placement IDs that belong to this group, one per frame.</summary>
    public List<string> PlacementIds { get; set; } = [];

    /// <summary>First frame index this group spans.</summary>
    public int FrameStart { get; set; }

    /// <summary>Number of frames in this animation cycle.</summary>
    public int FrameCount { get; set; }

    /// <summary>Whether this group is expanded in the timeline UI (true = show all frames, false = collapsed preview).</summary>
    public bool Expanded { get; set; } = true;
}
