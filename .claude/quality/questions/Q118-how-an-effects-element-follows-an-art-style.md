# Q118 · How an effects element follows an art style — **answered 2026-08-18**

Effects have to read as *this show's* effects: line style, width and shape, not
just a correct silhouette. Most of that is already free, because Q116 chose to
bake strokes rather than pixels — the outline is a `ToolKind.Brush` stroke
carrying a full `BrushSettings`, so any brush the artist owns, including one
imported from `.abr`/`.kpp`, draws the contour. What is *not* free is everything
about how the contour becomes a stroke in the first place, and four choices there
were prompted together. Two went against the recommendation.

**1. Where a line treatment lives: a shared reference with local overrides**
(recommended: a scoped resource alone; **owner chose the cascade**). An element
names a treatment by id and may override individual fields on top of it.

The cost stated when it was put, and accepted: two places to look when a line
comes out wrong, and the override set is a third thing to serialize, migrate and
show in the UI. Building it revealed that most of that cost is avoidable and one
part is not:

- **It costs one type, not two.** If `LineTreatment`'s fields are nullable and the
  defaults live in exactly one place, then a shared treatment and an override are
  *the same record*, and resolution is `override.X ?? shared.X ?? default.X`. A
  separate mirror record with parallel nullable fields — the obvious shape — is
  the thing that would have to be kept in step forever, and it is not needed.
  This also lands the *optional means absent* rule for free: an untouched
  treatment writes no keys.
- **The UI cost is real and has to be paid deliberately.** An overridden field
  must be marked as overridden, and there must be a per-field revert to the
  shared value. Without both, "two places to look" is exactly the support burden
  the option warned about, and it will read to an artist as the app being
  inconsistent rather than as a cascade.

The alternatives declined: a scoped resource alone means a show tunes its fire
once and every element follows, but an artist who wants *this* flame slightly
heavier has to fork the whole treatment; inline-only means forty fire elements
carry forty copies of one look, and an art-direction note is manual on each.

**2. Whether the solved fields are kept so restyling is fast: session cache, not
saved** (recommended, accepted). Line weight, band count and smoothing are
applied at *trace* time, so changing them needs a re-trace (~30 ms for 48 frames)
and not a re-solve (~1437 ms). Holding the baked fields in memory for the life of
the session makes style tuning feel live; reopening a document costs one re-solve
before the first restyle. Nothing new goes in the file. Saving the fields instead
was declined at roughly 8 MB per 48-frame element of derived data — the thing the
project already decided not to store for the code index and the ledgers — and
having no cache at all was declined because a 1.4 s wait per slider nudge means
artists stop exploring and take the first look that works.

**3. Whether the outline sits exactly on the band contour: it may depart**
(recommended, accepted). The outline may be offset inward or outward, may cover
only part of the silhouette, and may break into several strokes with gaps. This
is where distinct styles actually live — anime fire commonly outlines only the
upper edge, and a broken line reads as drawn where a closed one reads as traced.
The accepted cost is that treatment has materially more to express and one band
may emit several strokes rather than one, so the tracer cannot assume a
one-contour-one-stroke mapping anywhere.

**4. Whether style-matching from a reference drawing is planned now: design both
together** (recommended: build the knobs first; **owner chose together**). The
treatment vocabulary is therefore chosen for what a model can reliably infer from
an image, not merely for what is convenient as a slider.

That is a real constraint and it improves the record, which is the part worth
writing down rather than the part worth regretting:

- **Every field must be observable in a drawing.** `PressureSizeGamma = 1.4` is
  not something anybody can see; "the line is three times heavier on bends than
  on straights" is. So the vocabulary is ratios, angles and distances rather than
  coefficients — and it is better for the artist for the same reason it is better
  for the model.
- **Distances are in stroke-widths, never pixels.** Invariant 7's argument
  applies directly: a treatment expressed in pixels means something different at
  another element scale or output scale. Stroke-widths is scale-free, and it is
  also how an artist describes a line — "break it about two line-widths".
- **The record must be small and schema-constrained.** A model asked for forty
  opaque floats will do badly; ten named measurable properties it can reason
  about it will do well. This follows the existing `StrokeSchemas` pattern.

The costs, both accepted and neither cheap. The knobs are being fixed before
anybody has tuned a look by hand, so the first vocabulary is a guess informed by
inference rather than by use — and the mitigation is only that step 4 is a real
look and the vocabulary may still move before step 3 is final. And it pulls AI
review into what is otherwise pure arithmetic: **gate G12 now applies** to the
treatment record and its schema, so the ai-engineer and art-director pair review
them, with art-director holding the veto on whether an inferred style reads.

*Building* both together was not what was chosen and is not what this records:
inference needs a look to compare an answer against, so it lands after step 4.
What is fixed now is the vocabulary. The payload shape is the cheap direction for
once — `docs/DESIGN-ai-payload.md` measures images at ~87% of a request's bytes
and ~5% of its tokens, and this sends one image and gets back a small object,
which is the opposite of the inbetweener's problem.
