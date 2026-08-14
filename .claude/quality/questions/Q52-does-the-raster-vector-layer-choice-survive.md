# Q52 · Does the Raster/Vector layer choice survive? — **answered: no, and imports get their own layer**

**Answered 2026-08-07.** The owner's answer, and it is a better design than the
one recommended:

> *"An imported image is always placed on a separate layer. AI won't read it.
> Merging layers with an image skips this as well but before merging. Prompt the
> user if AI is enabled. Otherwise skip it. Remove the layer designation in the
> UI."*

### Corrected the same day: half of this already exists, and the other half has no caller

**Two things were wrong in the framing this answer was given against, and both
were mine.** The decision stands; its premise does not.

**1. There is no image import into a frame, and never has been.** Three places
write `PaintedFrame.PngBase64` — the transform tool resampling an existing
baseline, frame cloning, and clearing it to empty. Not one is an import. The
field's own doc comment says it "carries imported/flattened pixels", and nothing
has ever put an imported pixel in one. So the rule *"an imported image is always
placed on a separate layer"* guards a path with no caller. It is a **forward
rule** for whenever import is built, which is fine — deciding before building
beats retrofitting — but nothing in the roadmap schedules it.

**2. The reference case is built, and it is better than what was being
designed.** `ReferenceStrip` (`src/Lightbox.Core/Documents/ReferenceStrip.cs`) is
*"an imported image of an animation — a run cycle, a shot from a film, a contact
sheet — sliced into frames and laid against the timeline"*. It already settles
every question that was asked here:

| Asked | Already answered by `ReferenceStrip` |
| --- | --- |
| Is it artwork? | *"**Not artwork.** It never exports, never reaches a stroke, and never appears in a flattened document"* — view-only side of invariant 5, same side as onion skin |
| Embedded or linked? | **Embedded**, base64 in the document, and the reason is written down: *"a reference that lived at a path would break the moment the file moved, and a reference that breaks silently is worse than none"* |
| Can it animate? | Yes. `Slots` maps each timeline index to a cell, and `FollowsTimeline` moves them along when an inbetween is inserted |
| Is it a layer? | No, and deliberately not |
| Absent unless used? | `Scene.References` is null until one is imported |

**Krita reached the same three-way split and Lightbox landed on the better half
of it.** Krita separates a *reference images tool* (not a layer, never exported,
per-image choice of embed or link) from a *file layer* (real artwork, linked) —
and its guidance is to link big files. Lightbox went the other way on storage for
a domain reason Krita does not have: you draw *against* a reference, so one that
breaks silently is worse than one that is large. Photoshop offers the same choice
as Place Embedded / Place Linked and defaulted to embedded for its first two
decades.

**So the gap this question thought it was closing is much narrower than it
looked.** Everything about *looking at* or *tracing over* a picture is built. The
only thing missing is an image that has to appear **in the output** — a
photographic background that exports, or a scanned pencil test kept as the
drawing itself. Nobody has asked for either, neither is on the roadmap, and the
rule above is what will govern them if they arrive.

**What survives unchanged, and is worth doing on its own:** the layer picker
still asks a question nobody can answer at layer-creation time, the V/R badge
still implies a difference in what you can draw when there is none, and B132 is
still a real silent failure. Those were never contingent on import existing.

### Why the choice was questioned

**The question came from noticing the choice does almost nothing.** Two layer
kinds, and everything you can *make* in Lightbox behaves identically on both —
same tools, same engine, same marks, because nothing anywhere gates a tool by
layer kind. The whole difference is two rows: a raster layer can hold **pixels
that came from outside** (an imported photo, a paste of flattened pixels), and it
can hold **symbol placements**. So the picker asks, at the moment a layer is
created, a question about an import that has not happened and probably never
will.

**The recommendation was to convert a frame on demand, and it was worse.** It
kept the awkward part — a drawing frame quietly becoming a pixel frame under the
artist — and paid for it with a prompt. Giving an import its own layer removes
the problem instead of managing it: **a layer is born knowing what it is, and
nothing ever converts.** The two frame classes stay because a baseline genuinely
is different content with different provenance; what goes is the *choice*.

**Where the warning moves, and why that is the good part.** The consequence worth
knowing has never been about layers at all — it is that the inbetweener reads
strokes and cannot read pixels, so imported content is skipped. On its own layer
that is obvious and harmless. It only becomes a loss at the moment somebody
**merges** a drawing layer into an image layer, because the result is pixels and
the drawing's machine-readability is gone. So the warning belongs there, before
the merge, rather than at layer creation where it would be noise.

**And it is conditional: prompt only when AI is enabled.** *Absent unless used*,
applied to a warning. An artist who never touches the AI features is being told
about a capability they do not have, which is the definition of noise.

**What it obliges.**

- **Symbols are a blocker, not a nicety.** Placing a symbol currently refuses any
  layer that is not raster (`activeLayer.Kind != LayerKind.Painted`), and
  `VectorFrame` has no `Placements` field. If new layers stop being raster,
  placing a symbol silently does nothing. Nothing anywhere records a reason for
  that restriction, so it reads as an accident. Filed as **B132**.
- **`Layer.Kind` stays in the record and leaves the UI.** The literal ask was the
  UI, and keeping the field is what makes an imported-image layer describable at
  all. It stops being chosen and starts being a fact about how the layer was
  born. Old documents therefore need nothing — the field still exists and still
  means what it meant, so Q36 does not even come up.
- **The manual's layer section changes**, and the R/V badge goes.

**Blocks:** B132 blocks it. Nothing else.

**The follow-on nobody has to take yet.** If `Placements` belongs on both kinds,
the only remaining difference is `PngBase64` — and then the two classes want to
be one `Frame` with a nullable baseline, which is *absent unless used* stated
properly. That is a serialization-discriminator change and a bigger piece of
work; it is named here so it is a decision later rather than a surprise.

### Taken, 2026-08-08 — and the reason was better than the one written above

**Asked as "which kind should a new layer default to", and the owner's reply
dissolved the question rather than answering it:** *"It is unclear to me why
pixels and vector could not exist on the same frame."* They could, and did — a
`PaintedFrame` had always held pixels *and* strokes *and* placements at once. So
the recommendation two paragraphs up, *"the two frame classes stay because a
baseline genuinely is different content with different provenance"*, was wrong on
its own terms: **provenance is a property of content, and it was being encoded as
a property of the container.** That is what made a class able to be *less* than
another rather than different from it, and what made B132 possible.

The decisions, both prompted and both answered:

| | |
| --- | --- |
| The two classes | **Collapse now, in one go** — not staged behind another branch |
| `Layer.Kind` | **Keep it, as import provenance only** — the field survives, the choice does not |

**What it cost on disc, stated because it is a real format change.** A document
saved by this build carries no frame `kind` and carries `pngBase64` only when
there are imported pixels to carry. Older builds cannot read the result; every
older file still opens here, and `PreMergeDocumentTests` pins that against a
fixture and two render fingerprints generated by the two-class build itself.

**And the merge warning landed keyed on the fact rather than the field.** It asks
the *frame* whether it holds a baseline or a placement, not the *layer* what kind
it is — because every pre-merge layer is `LayerKind.Painted`, hand-drawn ones
included, so a warning keyed on `Kind` would fire on every document that exists.
A warning that appears on every old file teaches an artist to ignore warnings.
