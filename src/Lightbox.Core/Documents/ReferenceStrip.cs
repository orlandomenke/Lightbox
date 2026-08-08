namespace Lightbox.Core.Documents;

/// <summary>
/// One frame of a reference sheet: a rectangle in the source image, plus the
/// nudge that lines it up with the drawing.
/// </summary>
/// <remarks>
/// <para>
/// The rectangle is a <b>window onto the sheet</b>, not a crop of the artwork
/// inside it. That distinction is the whole feature. Tight-cropping each frame
/// to its own content and then drawing them all at the same place would put
/// the character's bounding box in one spot every frame — which is to say it
/// would delete the animation. The windows tile the sheet, so a runner who
/// travels across their cell still travels when the reference is played back.
/// </para>
/// <para>
/// The nudge lives on the cell rather than on the timeline slot because
/// alignment is a property of the reference drawing, not of where it happens
/// to be exposed. A cell shown at two frames is the same drawing lined up the
/// same way.
/// </para>
/// </remarks>
public sealed class ReferenceCell
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Per-cell alignment nudge, in document pixels.</summary>
    public double Dx { get; set; }

    public double Dy { get; set; }

    /// <summary>
    /// The point in the cell that should sit at the same place every frame —
    /// the contact foot, the hips, whatever the animator is registering on.
    /// Sheet pixels, absolute. Null means "not decided".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Absolute rather than relative to the cell so that nudging or resizing a
    /// cell does not silently move the pivot: the pivot is a mark on the
    /// drawing, and the cell is a window that happens to contain it.
    /// </para>
    /// <para>
    /// Null until set, and written only then — a sheet nobody has registered
    /// carries no pivot keys. <see cref="Pivot"/> is what to use when drawing:
    /// it falls back to the middle of the cell's bottom edge, which is where a
    /// standing figure's contact point usually is.
    /// </para>
    /// </remarks>
    public double? PivotX { get; set; }

    public double? PivotY { get; set; }

    /// <summary>
    /// Where the pivot is, decided or not. Derived; never serialized — a
    /// tuple has public fields rather than properties, so this wrote an empty
    /// <c>"pivot": {}</c> onto every cell, which is the same leak as the
    /// others and harder to spot for looking like nothing.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public (double X, double Y) Pivot =>
        (PivotX ?? X + Width / 2.0, PivotY ?? Y + Height);

    /// <summary>
    /// Whether the pivot was placed by hand rather than assumed. Derived;
    /// never serialized.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPivot => PivotX is not null || PivotY is not null;

    public ReferenceCell Clone() => (ReferenceCell)MemberwiseClone();
}

/// <summary>
/// An imported image of an animation — a run cycle, a shot from a film, a
/// contact sheet — sliced into frames and laid against the timeline.
/// </summary>
/// <remarks>
/// <para>
/// Not artwork. It never exports, never reaches a stroke, and never appears in
/// a flattened document: it is a drawing aid on the view-only side of
/// invariant 5, the same side as onion skin. It lives in the document rather
/// than in settings for the reason ghost pins do — it names frames of
/// <i>this</i> sequence and means nothing anywhere else — and
/// <see cref="Scene.References"/> is null until one is imported, so a document
/// that never uses the feature writes no key for it.
/// </para>
/// <para>
/// The image travels inside the document as base64 PNG, exactly like
/// <see cref="Doc.BrushTips"/>. A reference that lived at a path would break
/// the moment the file moved, and a reference that breaks silently is worse
/// than none: you would not notice until you were drawing against nothing.
/// </para>
/// </remarks>
public sealed class ReferenceStrip
{
    public string Id { get; set; } = Ids.NewId("ref");

    public string Name { get; set; } = "Reference";

    /// <summary>The whole sheet, base64 PNG. Empty for a video reference.</summary>
    public string Png { get; set; } = "";

    /// <summary>
    /// The footage this strip was cut from, or null for an ordinary image
    /// sheet — and null writes no key, the usual rule.
    /// </summary>
    /// <remarks>
    /// Video is the one reference that is referenced by path rather than
    /// embedded (Q56): footage is megabytes per second, and embedding it
    /// would turn every document that draws over a shot into a copy of the
    /// shot. The extracted frames are rebuilt from the file on load; a
    /// missing file degrades to drawing against nothing, exactly as a
    /// corrupt embedded sheet already does. Relative paths resolve against
    /// the document's directory, like <see cref="AudioTrack.Path"/>.
    /// </remarks>
    public string? VideoPath { get; set; }

    /// <summary>
    /// The footage itself, base64 of the original clip file, or null — the
    /// small-production path (Q57). Where <see cref="VideoPath"/> keeps a
    /// document light and <see cref="Png"/> keeps it merely self-contained,
    /// this keeps the <em>material</em>: full fidelity, re-extractable, and
    /// heavy, which is the artist's explicit choice at import. When set, the
    /// frames are rebuilt from these bytes rather than from any file on disk.
    /// </summary>
    public string? VideoData { get; set; }

    /// <summary>
    /// Whether this strip composites into exports (Q57). False for every
    /// reference — a reference never reaches an exported pixel, and that
    /// promise predates this flag. True only for small-production footage,
    /// where the clip IS part of the shot: it renders beneath the drawing
    /// layers in the PNG sequence and the video export, exactly as it shows
    /// on the canvas.
    /// </summary>
    public bool RendersInExport { get; set; }

    public int SheetWidth { get; set; }

    public int SheetHeight { get; set; }

    /// <summary>The frames found in the sheet, in reading order.</summary>
    public List<ReferenceCell> Cells { get; set; } = [];

    /// <summary>
    /// Which cell each timeline index shows; <c>-1</c> for none.
    /// </summary>
    /// <remarks>
    /// A list rather than "cell i at frame i" because the two drift apart the
    /// moment you work: insert an inbetween and every later reference has to
    /// move up one, which is exactly what an animator means by adding a frame.
    /// Beyond the end of the list there is no reference — deliberately not a
    /// hold, because a held reference on a new frame is a drawing you have
    /// already done, presented as one you have not.
    /// </remarks>
    public List<int> Slots { get; set; } = [];

    /// <summary>
    /// Move the reference along when frames are inserted or deleted.
    /// </summary>
    /// <remarks>
    /// On by default: you insert a frame to draw an inbetween, and the
    /// reference for the next extreme belongs after it, not on it. Off is for
    /// a reference pinned to absolute timing — a soundtrack-driven sheet, or a
    /// shot you are matching frame for frame.
    /// </remarks>
    public bool FollowsTimeline { get; set; } = true;

    public bool Visible { get; set; } = true;

    /// <summary>
    /// One scale for every frame.
    /// </summary>
    /// <remarks>
    /// Per frame would be wrong, not merely unnecessary. A reference whose
    /// scale varied frame to frame would put the character at a different size
    /// on each drawing, which is the one thing a size reference exists to
    /// prevent.
    /// </remarks>
    public double Scale { get; set; } = 1;

    public double Opacity { get; set; } = 0.5;

    /// <summary>Document-space top-left every cell is drawn from, before its own nudge.</summary>
    public double OffsetX { get; set; }

    public double OffsetY { get; set; }

    /// <summary>The cell shown at a timeline index, or null.</summary>
    public ReferenceCell? CellAt(int frame)
    {
        if (frame < 0 || frame >= Slots.Count) return null;
        var index = Slots[frame];
        return index >= 0 && index < Cells.Count ? Cells[index] : null;
    }

    /// <summary>Expose cell <paramref name="cell"/> at <paramref name="frame"/>; -1 clears.</summary>
    public void Assign(int frame, int cell)
    {
        if (frame < 0) return;
        while (Slots.Count <= frame) Slots.Add(-1);
        Slots[frame] = cell;
    }

    /// <summary>
    /// Timeline frames where this clip's bar is cut (Q57), or null — no key
    /// until the artist splits. A split point divides a contiguous run of
    /// assigned slots so each section can slide and trim on its own; points
    /// that stop falling inside a contiguous run (because the sections moved
    /// apart and the gap now divides them) are normalised away.
    /// </summary>
    public List<int>? SplitPoints { get; set; }

    /// <summary>
    /// The clip's sections on the timeline: contiguous runs of assigned
    /// slots, divided further at <see cref="SplitPoints"/>. Inclusive frame
    /// ranges, in timeline order.
    /// </summary>
    public IReadOnlyList<(int Start, int End)> AssignedRuns()
    {
        var runs = new List<(int Start, int End)>();
        int? start = null;
        for (var i = 0; i <= Slots.Count; i++)
        {
            var assigned = i < Slots.Count && Slots[i] >= 0;
            if (assigned && start is null)
            {
                start = i;
            }
            else if (assigned && start is { } s && SplitPoints?.Contains(i) == true)
            {
                runs.Add((s, i - 1));
                start = i;
            }
            else if (!assigned && start is { } open)
            {
                runs.Add((open, i - 1));
                start = null;
            }
        }
        return runs;
    }

    /// <summary>Drop split points that no longer sit inside a contiguous run.</summary>
    public void NormaliseSplitPoints()
    {
        if (SplitPoints is null) return;
        SplitPoints.RemoveAll(p =>
            p <= 0 || p >= Slots.Count || Slots[p] < 0 || Slots[p - 1] < 0);
        if (SplitPoints.Count == 0) SplitPoints = null;
    }

    /// <summary>
    /// Slide one section (Q57): the assignments in the inclusive range move
    /// by <paramref name="delta"/>. The caller has already clamped the delta
    /// against the neighbouring sections; assignments pushed past frame zero
    /// are dropped, as in <see cref="SlideSlots"/>.
    /// </summary>
    public void SlideRange(int start, int end, int delta)
    {
        if (delta == 0) return;
        var moved = new List<(int At, int Cell)>();
        for (var i = Math.Max(0, start); i <= Math.Min(end, Slots.Count - 1); i++)
        {
            if (Slots[i] < 0) continue;
            moved.Add((i + delta, Slots[i]));
            Slots[i] = -1;
        }
        foreach (var (at, cell) in moved)
        {
            if (at < 0) continue;
            Assign(at, cell);
        }
        if (SplitPoints is not null)
        {
            for (var i = 0; i < SplitPoints.Count; i++)
            {
                if (SplitPoints[i] > start && SplitPoints[i] <= end + 1)
                {
                    SplitPoints[i] += delta;
                }
            }
        }
        NormaliseSplitPoints();
    }

    /// <summary>
    /// Slide every assignment along the timeline by <paramref name="delta"/>
    /// frames (Q57) — the clip bar's body drag. Assignments pushed past
    /// frame zero are dropped off the front, which is what dragging a clip
    /// off the left edge means.
    /// </summary>
    public void SlideSlots(int delta)
    {
        if (delta == 0 || Slots.Count == 0) return;
        var moved = new List<int>(Slots.Count + Math.Max(0, delta));
        for (var i = 0; i < Slots.Count + Math.Max(0, delta); i++) moved.Add(-1);
        for (var i = 0; i < Slots.Count; i++)
        {
            if (Slots[i] < 0) continue;
            var at = i + delta;
            if (at < 0) continue;
            while (moved.Count <= at) moved.Add(-1);
            moved[at] = Slots[i];
        }
        Slots = moved;
    }

    /// <summary>First timeline frame with a cell assigned, or -1 for none.</summary>
    public int FirstAssignedSlot()
    {
        for (var i = 0; i < Slots.Count; i++)
        {
            if (Slots[i] >= 0) return i;
        }
        return -1;
    }

    /// <summary>Last timeline frame with a cell assigned, or -1 for none.</summary>
    public int LastAssignedSlot()
    {
        for (var i = Slots.Count - 1; i >= 0; i--)
        {
            if (Slots[i] >= 0) return i;
        }
        return -1;
    }

    /// <summary>Every frame in order, one cell each, starting at <paramref name="from"/>.</summary>
    public void LayOutFrom(int from)
    {
        Slots.Clear();
        for (var i = 0; i < from; i++) Slots.Add(-1);
        for (var i = 0; i < Cells.Count; i++) Slots.Add(i);
    }

    /// <summary>
    /// Line every cell up on its pivot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registration an animator does on a peg bar: pick the point that
    /// should not move — the contact foot, the hips — and slide each drawing
    /// until it sits at the same place. Everything is measured against the
    /// first cell, so aligning twice changes nothing and the sheet does not
    /// wander off the canvas.
    /// </para>
    /// <para>
    /// This is the opposite of what tiling the sheet does, and both are
    /// wanted. Tiling keeps a runner travelling across their cell, which is
    /// the motion; aligning on the pivot takes the travel out so the animator
    /// can see the pose change underneath it. Which one you want depends on
    /// whether you are matching the walk or the drawing, so it is a button
    /// rather than a rule.
    /// </para>
    /// </remarks>
    public void AlignByPivot()
    {
        if (Cells.Count == 0) return;
        var (originX, originY) = Cells[0].Pivot;
        foreach (var cell in Cells)
        {
            var (x, y) = cell.Pivot;
            cell.Dx = (originX - x) * Scale;
            cell.Dy = (originY - y) * Scale;
        }
    }

    /// <summary>Centre the sheet's first cell on a canvas of this size.</summary>
    public void CentreOn(int width, int height)
    {
        var cell = Cells.Count > 0 ? Cells[0] : new ReferenceCell { Width = SheetWidth, Height = SheetHeight };
        OffsetX = (width - cell.Width * Scale) / 2;
        OffsetY = (height - cell.Height * Scale) / 2;
    }

    public ReferenceStrip Clone()
    {
        var copy = (ReferenceStrip)MemberwiseClone();
        copy.Cells = Cells.ConvertAll(c => c.Clone());
        copy.Slots = [.. Slots];
        return copy;
    }
}
