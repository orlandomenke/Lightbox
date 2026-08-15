# Q61 · Resize canvas and resize image: what is allowed to change the grain? — **answered 2026-08-08, three recommendations taken and one overruled**

**What forced the question.** `ROADMAP.md` carried *Resize canvas and resize
image* as `[?]` — three sentences of intent, no evidence anchors. Two of those
sentences turn out to be in tension with the drawing engine, and neither is
obviously the one that should give way.

**The tension, in one paragraph.** Every dab dynamic — scatter, size, flow,
roundness, rotation and all three colour jitters — is seeded from the IEEE-754
bits of a dab's position through `Hash01`. That is invariant 2, and it is what
makes a reload, an undo and an AI inbetween all produce the same mark. The
consequence nobody had written down is that **moving a coordinate changes the
mark that coordinate carries**. Growing the canvas leftward means the artwork
no longer starts at (0,0), so the obvious implementation — shift every stroke
right by 200 px — re-rolls the grain of the entire document. Rescaling the
artwork multiplies every coordinate and does the same thing.

**Four decisions.**

- **The canvas gets an origin; the drawing does not move.** `Scene.OriginX` and
  `OriginY` are nullable and absent from a document that never resized, and
  `Left`/`Top`/`Right`/`Bottom` are what everything reads. Growing leftward
  moves the origin negative and leaves every coordinate exactly as it was, so
  the resize is O(1) whatever the document holds and the render is
  bit-identical outside the new margin. The alternative — translate every
  stroke and accept the re-grain — was rejected because the artist added paper
  and would get back a drawing with different texture. The cheap third option,
  refusing the top and left anchors, was rejected for dropping half of what
  was asked for.
  - **The cost is real and is being paid in the raster path.** The document
    rectangle is no longer `(0, 0, W, H)`, so everything converting a document
    coordinate into a pixel in a layer bitmap has to subtract the origin.
    `InDocumentSpace` already translated by an arbitrary device origin, which
    is what makes this surgery rather than a rewrite — but it is surgery in
    `BrushEngine`, which `HOTSPOTS.md` ranks at the top of the repository.
- **Resize image multiplies the geometry and the brush sizes, and the grain
  re-rolls.** One document space forever: after a 2× resize a 10 px brush still
  makes a 10 px mark. The alternative was a stored document scale applied as a
  canvas transform — invariant 7's own prescription, and it preserves the mark
  bit-for-bit — rejected because it makes authoring space and document space
  diverge permanently for every path that reads pixels, compounds across
  repeated resizes, and turns a 10 px brush into a 20 px mark.
  - **The line between the two operations is the answer's real content**, and
    it reads as inconsistent until it is stated: *when the artist changes the
    art, the mark may come back different; when the artist changes only the
    paper, it must not.* That is Q26's finding — the grain belongs to the
    canvas — applied to the two cases separately rather than to both at once.
  - **Invariant 7 is not what this breaks.** That invariant governs rendering
    the same document larger, and the reason is that a 2× render must be a
    sharper picture of the same mark. An authored rescale is a request for a
    different document.
  - Re-rolled grain at the new resolution arguably reads *better* than the
    alternative would: scaling up, the texture is native rather than
    magnified; scaling down, it is drawn small rather than downsampled.
  - **Two payloads cannot be handled by arithmetic** — a frame's imported
    baseline scan and a smudge stroke's baked sample are pixels, not
    instructions. `IPixelResampler` is declared in Core and implemented in
    Raster so the operation is honest about them; a payload that will not
    decode is dropped rather than left at the old scale.
  - **A non-uniform resize cannot honestly scale a brush**, because a dab has
    one diameter and no axes. Geometry moves exactly and the mark moves by the
    geometric mean, so a 2×1 rescale gives correctly-placed strokes drawn
    about 1.41× wider. Uniform rescales — the default, and what the dialog
    links by default — are unaffected.
- **Pixels are the unit; PPI is a field beside them.** `Scene.Ppi` has existed
  as declared metadata that nothing reads. Resize image can set it, and setting
  it never resamples anything by itself. A full physical-units dialog with a
  resample toggle was rejected for making PPI load-bearing across export and
  print paths that do not read it today.
- **Both operations ship on one branch — the recommendation, overruled.** The
  branch rule says a sentence needing an "and" is two branches, and *resize
  canvas and resize image* has one. The owner's call was one branch: they share
  a dialog and most of the plumbing, so splitting them means building the
  dialog twice or landing a dialog with one button. Recorded as a departure
  because the diff is correspondingly larger in the hottest paths in the
  repository, which is exactly the cost the rule exists to avoid.

---
