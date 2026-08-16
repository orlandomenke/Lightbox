# Q98 · Pillar 4 entry point, and the motion trail's tracked point — **answered 2026-08-16**

Pillar 4 (animation-aware drawing tools) is the thinnest pillar and the owner
asked to start on it. Its fourteen open items fall into three families — tools
that *act* across frames, marks that *survive* the sequence (Q80's brushes),
and tools that *read* the sequence back to the artist — and the reading family
has one shared substrate: "where is the subject on frame N." Two decisions
were prompted together.

**Which slice first: the motion trail** (recommended, accepted) — *motion path
visualization* and *spacing visualization* delivered as one overlay, because
they are one thing: a polyline through the subject's position on each frame of
a range, with a tick per frame. Even ticks are even spacing; bunched ticks are
an ease — the trail *is* the spacing chart animators draw on paper. View-only,
so the invariants are trivially safe, and it is the substrate arcs, arc
prediction, the spacing assistant and the analyzers each become a small
follow-up branch on. The alternatives had named costs: *batch transform* is
the quickest win but a leaf nothing builds on; *animation-aware brushes* is
the heaviest item and grows the seeding story a frame dimension; *draw once,
reuse* needs scoping against the Animation library before a line is written.

**What point represents a drawing: its authored anchor, else the stroke-bounds
centroid** (recommended, accepted). An anchor is the artist's own statement of
where the subject is, already per drawing and already read by the exporters;
the centroid fallback means the trail never goes blank on unanchored work. The
costs declined: centroid-only wobbles with the silhouette rather than the
motion, and a picked tracking point is a new record key plus a placement pass
over every frame before the trail shows anything — right for arcs later,
wrong as the entry price.
