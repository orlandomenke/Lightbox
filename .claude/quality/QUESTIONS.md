# Open questions

Decisions the loop could not make for you. Each one blocks something
specific; each can be answered in a line. Answer inline (edit the file) or in
chat — the loop reads this file at the start of every round and treats an
answered question as settled.

Questions are removed once implemented, with the decision recorded in
`DECISIONS.md`.

---

## Q66 · The bug list is growing faster than it drains — what changes? — **answered 2026-08-12: fix rather than file, and a blocked run asks in a PR**

Raised by the owner: *"I notice a lot more bugs being reported by the agentic
system. Most seemingly not resolved only recorded. Which increases the bug list.
I want the agentic system to auto fix any bugs they encounter and prompt me
questions in this interface whenever a major decision had to be made."*

**The observation is right, and measuring it made it sharper than the
impression.** Share of each block of ids still open:

```
B1–B60      9%
B61–B120   14%
B121–B160  18%
B161–B179  79%
```

That is a regime change rather than a drift, and it is not explained by the
newest entries having had least time: four P1s were open at the time of asking
— B144, B168, B178, B179, all `canvas` — and B144 had been open for
thirty-one merges. The mechanism is visible in the log: recent work had settled
into diagnose-and-file (`B178: file the frame-wait fault the first GPU-on report
exposed`, `B179: report what memory is actually held`), which is exactly right
for a hard performance problem and became the default for everything.

**Two things needed deciding rather than assuming, because each collided with a
rule already in `CLAUDE.md`.** Both were put to the owner with a recommendation
and both recommendations were taken.

**(a) Auto-fixing collides with "a branch is one objective."** That rule is
emphatic — *if the sentence describing the branch needs an "and", it is two
branches* — and "fix everything you encounter" is an instruction to grow an
"and". **Answer: fix it, on its own branch, after finishing the one in hand.**
So the two rules now say the same thing from different directions: finding a
second defect produces another branch, in sequence, each doing one thing. It
costs more pull requests, and that is the price of not accumulating. Severity
sets the bar — P1 and P2 always, P3 when small — and filing instead is an
exception that must name its reason in the entry.

**Rejected: fixing it in the current branch.** Faster to green and it would have
required relaxing the branch rule in the same change, which trades a measurable
problem for the one the repo's own history says causes drift.

**(b) "Prompt me in this interface" is already the rule, and the runs doing the
filing cannot obey it.** `AskUserQuestion` needs an interface; a scheduled or
background run has none, which is precisely why its questions went to a file.
Restating the rule harder would not have changed that. **Answer: such a run
stops, pushes what is finished, and puts the question at the top of the pull
request, titled `[needs a decision]`.** The point is to move unanswered
questions to where the owner already looks — an open PR is visible, and the
evidence of this file is that a line in it is not.

`QUESTIONS.md` still records the answer once it arrives. What changed is where
the *question* waits.

**What would show this worked**, and is worth checking rather than assuming: the
share of open ids in the next block of twenty. If B180–B199 sits near the
historical 9–18% rather than near 79%, the rule took. If it does not, the
constraint is not the instruction and this entry should be reopened rather than
the instruction reworded — which is the failure mode the questions section
already has a paragraph about.

---

## Q65 · Should strokes be merged to shrink a document? — **answered 2026-08-10: no — compress and quantise instead**

Raised by the owner with a detailed proposal: merge expired strokes at the undo
horizon, union paths with CSG, run Ramer–Douglas–Peucker over committed
strokes, chain strokes that meet end-to-end, and fall back to raster caches —
all under the constraint *avoid rasterisation, because the application promises
both raster and vector*.

**The constraint is right and the answer is no anyway**, because the cheap wins
are elsewhere and every merge on that list is either a determinism break or
worth nothing. Measured on a 400-stroke 1920×1080 painting with one brush
preset, which is the ordinary case for a painting:

```
                        raw          gzip
as saved (indented)   9,792,336    1,541,012
compact               3,952,622    1,367,127
points alone          3,675,740    1,353,826   91% of stroke bytes
400 brush blocks        237,601        1,521
points at 2dp         1,920,418      371,661
```

**Three findings, and the third is the one that changes the plan.**

- **The file is uncompressed pretty-printed JSON.** `DocJson.Options` sets
  `WriteIndented = true` and `Save` writes the string straight out. Compressing
  it is 6.4× for no semantic change at all, and it keeps the readable formatting
  the serializer's own comment chose on purpose.
- **Deduplicating brush settings is worth nothing.** This was the most promising
  idea going in — 41 properties inlined on every stroke while `ClipRegion` is
  already a content-hashed registry, so the house pattern existed and the
  retrofit was obvious. 400 identical brush blocks are 238 KB raw and **1.5 KB
  gzipped**: gzip already does the dedupe, and the change would buy ~0.1% of a
  compressed file. Worth recording precisely because it looked like the answer.
- **Coordinate precision is the whole game.** Points are 91% of stroke bytes,
  and storing two decimal places rather than a round-trippable double takes them
  from 1,354 KB to 372 KB gzipped — a 73% cut of the 91%, without touching one
  piece of geometry.

**Why every merge on the list is refused.**

- **RDP is a determinism break, not a compression.** "Reduces coordinates by 70%
  without altering the shape" is true of a plain vector outline and false here:
  every dab dynamic is seeded from the IEEE-754 bits of the dab's position
  through `Hash01`, so moving a control point does not give the same mark with
  fewer points, it gives **a different mark**. That is invariant 2, and invariant
  7 exists because the identical trap bit once already at output scale.
- **Chaining strokes end-to-end breaks the dab walk.** The walk is a fold
  carrying spacing phase, travelled distance and heading; concatenating gives the
  second stroke's dabs the first one's accumulated state, so the dabs move and
  the seeds move with them. B45 is this bug, already paid for once.
- **There is nothing to union.** A stroke is a centreline with width, pressure
  and per-dab dynamics, not an outline. CSG would require outlining it first,
  which destroys the thing that makes the mark.
- **Merging at the undo horizon makes the document depend on session length**
  and on a UI preference — the same drawing saved twice would differ.
- **The raster cache already exists** and is better than the proposal's version:
  `TileStore`, `TilePyramid`, `FramePrewarmer`. Vectors on disk, pixels in
  memory, non-destructive. That part is a no-op.

**And one cost the proposal could not see: stroke identity is an input to the
inbetweener.** It matches strokes between frames. Merging strokes to save bytes
spends the thing the headline feature runs on — which is a worse trade than any
byte count makes it look.

**So the order of work, if this is ever picked up:** compress the container
first; quantise coordinates second, and *at capture, never at save* — rounding a
committed point is RDP's bug wearing a smaller hat, and it has to happen before
the point enters the record so the live preview and the commit see the same
numbers. Flat point arrays third, reusing Q18's answer. Together roughly 10×
before a single stroke is merged.

**Not filed as a bug**, because nothing is broken: a large file is a cost, not a
defect. It belongs on the roadmap when file size actually hurts somebody.

**2026-08-13: it hurts somebody, and the first step is built.** The owner
reported large paintings costing minutes to open and slowing the session, and
chose (prompted, two questions): **phased** — container compression now, the
raster checkpoint (Q60/B30) next on its own branch — and **quantisation
deferred** to its own branch, on the recommendation that gzip alone is 6.4× and
a capture-path change deserves its own tests. `DocJson.Save` now writes gzip
(`CompressionLevel.Fastest`, streamed, atomic) and `Load` sniffs the container
so every pre-existing plain-JSON document loads unchanged;
`DocJsonCompressionTests` guards both directions and prints the achieved
sizes. Flat point arrays (Q18's answer) remain third in the order this entry
set. The in-session half of the report was B187 — autosave serializing on the
UI thread — fixed on the same branch.

---

## Q61 · Resize canvas and resize image: what is allowed to change the grain? — **answered 2026-08-08, three recommendations taken and one overruled**

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

## Q54 · Does Lightbox go public, and under what licence? — **answered 2026-08-08: yes, GPL-3.0, history and all**

**What forced the question.** CI stopped allocating runners on 2026-08-08 — run
#488 on `main` passed `docs`, `changes` and `test`, then `publish-win-x64` failed
in two seconds with `runner_id: 0`, no steps and a 404 on its logs, and every run
after it failed the same way on whichever job came first. Not a code fault: the
account had run out of Actions minutes. Measured burn was **9 billed minutes per
run** (GitHub rounds each *job* up to the minute, so `changes` at 8 s and `docs`
at 19 s cost a minute each), and **18 per merged change** — once for the pull
request, again for the push to `main`.

**Public repositories get unlimited free Actions minutes**, so the answer removed
the constraint rather than managing it. The owner intended to open-source
Lightbox anyway; the bill only set the date.

**Three decisions, and what each cost.**

- **Everything is published, history included.** Splitting the planning docs out
  was considered and rejected twice over. Retroactively: `BUGS.md` has 178
  commits, `ROADMAP.md` 105 and `QUESTIONS.md` 47, so purging them would rewrite
  nearly every SHA — including the commit references the ledgers themselves cite.
  Going forward: `bugs.py` and `roadmap.py` derive their checkboxes by resolving
  evidence anchors against the code index **in the same tree**, so a separate
  private repo would turn every derived checkbox back into an assertion. That is
  the precise failure B81 exists to prevent.
- **GPL-3.0.** Checked rather than assumed: every dependency is permissive
  (Avalonia, SkiaSharp, CommunityToolkit, the Anthropic and MCP SDKs all MIT;
  xunit Apache-2.0, which is GPLv3-compatible but *not* GPLv2-compatible — which
  is why the v3 family and not v2), and a scan for copied third-party source
  found only ordinary prose. AGPL was considered for the MCP and IPC surfaces,
  where "someone hosts Lightbox as a service" is not far-fetched, and declined:
  the network clause is a no-op for a desktop application and AGPL is on enough
  corporate blocklists to cost more than it buys.
- **`main` is protected, with admin bypass kept.** A pull request and passing
  checks are required, and `LIGHTBOX_PUSH_TO_MAIN=1` still works when a merge is
  genuinely intended. `.githooks/pre-push` stays the first line; protection is
  the second.

**The cost that is worth naming, because it is permanent.** Publishing is prior
art. It forecloses patenting anything in this tree — immediate in most of Europe
under absolute novelty, with a twelve-month grace period in the US. Nothing here
looks patentable (brush stamping, flood fill, Bézier geometry and layer blending
are decades of prior art), but the deterministic `Hash01` dab seeding and the
inbetweening approach are the two places anyone would look, and that door is now
shut. Accepted knowingly.

**What keeps the commercial option open**, and it is one thing: **sole
copyright**. GPL binds recipients, not the owner, so the same code can be
relicensed later — but only while one person holds all of it. `CONTRIBUTING.md`
therefore declines pull requests during alpha rather than leaving it to silence.
The day that changes, it needs a CLA first.

**Not legal advice, and the owner was told so.** An hour with an IP solicitor
before the switch is cheap if the commercial stakes are real.

---

## Q46 · What colour does the theme's accent take, and how does a tab say it is the one showing? — **answered 2026-08-07: violet, and an underline**

Three questions in one exchange, because they were three faces of the same
finding: **the palette had only ever covered half the application.**

Stage 1 tokenised every view, and every test passed, and the application still
wore two colour systems. Tokenising a view reaches the surfaces somebody aimed
at a token; every stock control — toggle buttons, slider thumbs, checkboxes,
radios, focus rings, list selection — paints from the *theme's* palette, and
Fluent's accent is Windows blue. The proof was one control wearing both at
once: the opacity slider had our coral track and Fluent's `#0078D7` thumb.

Nothing could have caught it from inside. It took a screenshot, which is the
part worth keeping: a colour system is only as wide as the surfaces that
resolve through it, and no assertion about the tokens can tell you which
surfaces those are.

**(a) The interactive accent is violet `#7B61FF`.** Every "this is on" state —
toggles, slider thumbs, checkboxes, selection, focus. Violet rather than coral
because it is *already* the selection colour in the layers list and the cel
vocabulary, so the selected row and the switched-on toggle become one colour
instead of two. It also leaves coral meaning "the primary action" without
competition, which is the rule the button ranks depend on: a screen where every
"on" state is as loud as the one button you want pressed has ranked nothing.

The cost, taken knowingly: the primary button's gradient no longer shares a
colour with any control state, so the loudest thing on screen is deliberately
unrelated to everything around it. That is the point, and it is also the thing
that will look wrong to somebody wanting the app to be "coral".

**(b) The active tab carries a 2 px accent underline.** The first version had no
mark at all, reasoning that the header is already a distinct surface and a
filled tab inside it makes two boxes where the artist needed one word. **The
boxes part still holds; the conclusion did not.** Three words at slightly
different brightnesses read as a row of labels rather than as a control — and a
tab strip that is not legible *as* a tab strip has hidden two panels instead of
offering them, which is the opposite of what tabbing is for.

An underline is the affordance that adds no box and costs no height. A filled
pill and full boxed tabs were both rejected for the reason the original
no-mark version was chosen: they put a second box inside the header, and boxed
tabs would want a row of their own, spending exactly the height tabbing exists
to save.

**(c) Dialogs sit on `SurfaceElevated`, one step above the panels.** They were
painting pure black, which is Fluent's window ground showing through because
nothing had told the theme otherwise. Elevated rather than the panel surface so
a dialog reads as floating over the app rather than as a hole cut in it — the
"anything raised goes one step up" rule the four surfaces already encode.

**What none of this needed deciding about**, so it did not hold the question up:
the theme's palette is written as hex literals in `App.axaml` and cannot be
otherwise. A `ColorPaletteResources` is built before the merged dictionaries it
would look into, so `{StaticResource}` there does not resolve. That is a fact
about Avalonia rather than a preference, and it is guarded by
`TheThemePaletteIsWrittenInHexOnPurpose` asserting the literals equal the tokens
they stand in for.

---

## Q52 · Does the Raster/Vector layer choice survive? — **answered: no, and imports get their own layer**

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

## Q53 · How does an artist get into point editing? — **answered: Illustrator's model in full**

**Answered 2026-08-07: two pointers *and* isolation mode.** A black-arrow
**Select** tool for whole strokes, a white-arrow **Direct select** for nodes, a
**Pen** with modifiers — and double-clicking a stroke isolates it, Esc leaves.

**The property being bought is that geometry editing is a decision, not an
accident**, and the research is one-sided about how you get it. Illustrator's
isolation mode *"automatically locks all other objects so that only the objects
in isolation mode are affected"*; Figma enters vector edit on Enter and leaves on
Esc; Grease Pencil separates Draw, Edit and Sculpt. The tools that feel mushy use
a modifier you have to remember instead — Krita's own vector-tool wiki says
*"Alt+drag allows you to start a rubber band without accidentally selecting and
moving a shape"*, and Inkscape's node tool requires that *"the drag must not
begin on a path unless Shift is used"*. **Modes are safe by default; modifiers
are unsafe by default and ask you to remember the antidote.**

The recommendation was isolation alone; the owner's answer added the two
pointers, on the grounds that Illustrator has both and the black/white
distinction is what makes the split legible at a glance. Illustrator's actual
convention is used — **black selects objects, white edits anchors** — rather than
the reversed pairing in the original note.

**What it costs.** Three tools rather than one mode, so three walks of the tool
registration checklist, and a `Select` that overlaps conceptually with the
existing pixel selection tools. Answered by Q48: they look different and do
visibly different things.

**Blocks:** nothing. `PathEditSession` is a second instance of the transform
tool's modal-session pattern.

## Q55 · Do the derived codemap files stay committed? — **answered: no, gitignored**

**Answered 2026-08-08, asked when the owner reported the treadmill directly:**
*"We keep running into the same problem due to Claude documents: index,
features and bugs. We tried guarding it but with each push main gets ahead and
the next branch always blocks due to merge conflicts on those docs."*

**Decision: stop committing `INDEX.md` and `FEATURES.md`.** They are derived
from the whole solution, so every branch that touches code rewrote them end to
end and any two parallel branches conflicted by construction — and GitHub runs
no merge driver, so every open pull request went red the moment any other one
merged, requiring a hand-merge of `main` into every survivor after every
merge. The files are gitignored beside `HOTSPOTS.md`; the session-start hook
builds them when stale or absent; CI runs `build` instead of `verify`; the
merge driver is retired. `LedgerGateTests.TheDerivedIndexIsNotTracked` pins it.

**What the alternative cost and why it lost:** the committed copy bought a
fresh clone an index without a ten-second build, defended by a local merge
driver plus a CI byte-verify. Both worked as designed and neither ended the
conflicts, because the web UI merge is the one place neither could run.

**Decided in the same exchange: the ledgers stay committed and hand-resolved.**
`BUGS.md` is authored prose no script can reproduce; its collisions are rarer
(two branches must both file bugs) and the pre-push `bugs.py ids` gate already
refuses the silent losses. Sharding it per domain was offered and declined.

## Q47 · Does a node carry Bezier handles? — **answered: yes, on a path beside the points**

**Answered 2026-08-07: handles on every node** — full Illustrator levers,
**against a recommendation of points-only**.

The recommendation was the Curvature-tool model: place points, let the
centripetal Catmull–Rom the renderer already runs infer the curve, Alt for a
corner. It is free — `GeometryOps.Densify` already does the interpolation and
`IsCorner` already exists — and Adobe shipped that tool precisely because the
handle pen is too hard. The owner chose handles anyway, for control and for
transferable muscle memory.

**The cost quoted at the time was the wrong cost, and saying so matters.** The
objection was that `StrokePoint(X, Y, Pressure)` is baked into the record, the AI
wire format, the contour tracer and every geometry op, so handles meant widening
it — a migration and a second curve type in the renderer. **That is avoidable,
because a drawn stroke and an authored path are different things.** A drawn
stroke has hundreds of sampled points and wants no handles; a pen path has a
dozen authored nodes and wants nothing else. So handles go on an **optional
`Stroke.Path`** — a small control net that *generated* the points — and `Points`
stays what renders. `BrushEngine`, `StrokeIndex`, `ContourTracer` and
`StrokeWire` are untouched, a hand-drawn stroke writes no `path` key, and there
is no migration.

**The residual cost is real and is the thing to hold:** a line now has two
representations that can disagree.

> **A stroke's `Path` and `Points` must never disagree.** Any operation that maps
> points maps the path's nodes and handles too, or drops the path.

`TransformOps.TransformStroke` is the first caller that must obey it and
`StrokeInterpolator` the second, and a test asserts it rather than a comment.

**Blocks:** nothing.

## Q48 · Does picking a stroke belong to the existing selection tools? — **answered: a separate line-picker**

**Answered 2026-08-07: a new tool.** The black arrow picks whole strokes — click,
shift-click, drag a box — and the existing marquee, lasso and wand keep selecting
*areas of pixels*. Two tools that look different and do visibly different things.

**The rejected option is the interesting one:** folding both into one tool, so a
click picks a line and a drag on empty canvas picks an area. Fewer tools, and it
reintroduces exactly the ambiguity Q53 exists to remove — the same click meaning
two things depending on what happens to be underneath it.

**What it costs.** One genuinely new primitive: a stroke-under-point query, which
the codebase has never needed. All three pieces exist and are tested and nothing
composes them — `StrokeIndex.Intersecting`, `GeometryOps.DistToSegment`,
`BrushEngine.CommitBounds`. `StrokeIndex`'s contract is *ascending record
position, not speed*, so the picker reverses it for hit order and must say why.

**Blocks:** nothing.

## Q49 · Do shapes become retained objects? — **answered: no, they stay strokes**

**Answered 2026-08-07: a rectangle is still a line painted with your brush** —
now reshapeable like any other line, but the document does not remember it was
ever a rectangle.

This **softens rather than reverses** the shipped manual sentence — *"it is not
re-editable as a shape afterwards"* — which stays true as written: not
re-editable *as a rectangle*, but re-shapeable like everything else. Grabbing its
corners is most of what anyone wanted.

**The reason is Krita, from the other direction.** Retained shapes mean two kinds
of thing in one document and a rule that some tools work on one and not the
other. Krita has that rule and it is the failure: its SVG layers *"don't actually
contain brush strokes, which makes them useless for most line art"*, and the
brush tool is unavailable while one is selected. One `Stroke` record is the
asset, and it is not being spent here.

**What it costs.** No retyping the width of a rectangle you drew last week; you
move its corners instead. Live shapes remain reachable later if an artist asks —
nothing here forecloses them.

**Blocks:** nothing.

## Q50 · What does an artist see on entering edit mode on a hand-drawn line? — **answered: fitted, and it says so**

**Answered 2026-08-07: fit a path and report the count** — "412 points → 12
nodes" — with one undo restoring every original point.

A drawn line has a point every few pixels. Showing all of them is technically
lossless and practically unusable: hundreds of nodes a few pixels apart, where
dragging one moves nothing. Fitting is what Illustrator's Image Trace and CSP's
Simplify both do, and Schneider's least-squares cubic fit is the standard.

**What it costs, and it is the reason this was asked rather than assumed:** the
line moves slightly. A fitted curve is not the wobble you drew. That is
acceptable only because it is *said out loud* and is one keystroke from being
undone — a silent fit would be the app quietly redrawing your work.

Rejected: showing every point (unusable), and asking each time (a dialog in front
of a gesture made hundreds of times, answered the same way every time). A detail
slider was offered and not taken; it stays available later as a tool option.

**Blocks:** nothing.

## Q51 · Do AI inbetweens carry the path? — **answered: only when node counts match**

**Answered 2026-08-07: carry the path through when both keys have the same number
of nodes, plain strokes otherwise** — **against a recommendation of never**.

The recommendation was that generated frames always come out as ordinary strokes
with no path, consistent with `StrokeInterpolator` already dropping `Holes`,
`ClipId`, `GradientId` and `SwatchId`. The owner took the middle: matched counts
are the common case when one key was copied from the other and edited, and
node-level correction of frame 4 is worth having when it is honestly available.

**What it costs, stated when it was chosen: the same command produces two
different results depending on something invisible.** An artist runs *inbetween*
twice and gets editable nodes once and not the other time, with nothing on screen
explaining why.

**So the mitigation is not optional and is part of the decision.** The AI status
line says which happened *and why* — "paths carried" versus "paths not carried:
keys have 12 and 9 nodes" — the same way every bulk edit in the project window
says what it did. **A silent version of this answer is a defect, not a
simplification**, and the test asserts both messages rather than only the
behaviour.

**Blocks:** nothing.

## Q45 · How far does the people model go, with no server? — **answered: a name and an id, forever**

**Answered 2026-08-07: `Person` is a label with a stable id, and it never gains
a role or a rights field.** Recorded as a decision rather than left as a comment
on the type, because the pressure to add one arrives with the first dashboard
filter.

**The reason is that rights inside this application would be theatre.** The
manifest is plain JSON on disk — a stated design commitment, so an agent can
read and write any part of it and so a project diffs in git. A permission a text
editor defeats is not a permission; it is a UI that lies about what it enforces,
which is the same class of defect as a menu entry bound to nothing and worse,
because people plan around it.

An advisory role field was rejected for that exact reason: a role that grants
nothing will be read as granting something, and the first time somebody asks why
a junior could still edit a locked shot, the honest answer is that the field
never meant that — by which point the studio has organised around it.

Designing the client/server split now was rejected as architecture for a product
that does not exist, paid for by the one user who does.

**The two positions this leaves, in order:**

1. **The project file is the shared state and the network is somebody else's** —
   git, a shared drive. The `.lbproj` folder-of-JSON layout was designed for
   this, and assignment and status are fields people edit and merge.
2. **Feed an existing tracker** — ShotGrid, Kitsu, Flow — through an adapter, if
   a studio ever needs one. It needs no new model, because documents already
   have stable ids to match a shot against. Kitsu being open-source is the same
   instinct as bring-your-own-model.

**The accepted cost:** two people editing one manifest can conflict, and nothing
in Lightbox mediates it. The merge is the studio's, the same as for any other
file in their repository.

## Q41 · Where does the project window live? — **answered: its own window**

**Answered 2026-08-07: a top-level window with tabs inside it**, opened like
Configure and Export. Q29's split made literal — it is what you do *between*
drawings, so it can sit on a second monitor while the canvas keeps the first,
and it never competes with drawing space.

A docker was rejected because the whole point is columns — tags, status,
assignee, length — and a docker strip is 200 pixels. It would be the project
docker again with more squeezing, which is the surface that already exists. A
main-window tab was rejected because it makes the tab strip mean two things and
gives up the second monitor.

The accepted cost: another top-level window to keep in step with a project that
changes underneath it, and on one small screen it covers the canvas.

## Q42 · What is in the first cut? — **answered: Structure, Status and Assets**

**Answered 2026-08-07: all three, in one branch,** plus the model gaps they
need — document tags, document-level resources, the user tier. Export follows,
because `ExportPlan.For` and `Describe` already exist and the row menu already
reaches that view.

*"Manage assets on project, folder and file level"* was the explicit ask, so
deferring the Assets tab would have deferred the half that was named. The
accepted cost is a branch wider than "one branch, one objective" likes; it is
taken knowingly because the three tabs share the traversal, the selection model
and the window, and splitting them means building the window twice.

**Export followed on the next branch, as this said it would**, alongside the
three things the first cut shipped read-only or not at all: the Assets tab
writes, a single selected folder gets its facets edited, and a status card drags
between columns. Five tabs now, and the deferral cost nothing — `ExportPlan`
had not moved.

## Q43 · How is "who is working on this" modelled? — **answered: a people list**

**Answered 2026-08-07: named people on the project, assigned by picking.**
Against the recommendation, which was a free-text name per document.

The case for free text was that a name is a label like the folder glyph, and a
registry is a table nobody maintains in a single-user alpha. The case that won
is the feature's own purpose: this is the surface that replaces a spreadsheet,
and **two spellings of one person is exactly the spreadsheet problem.** Grouping
and filtering by assignee have to be exact to be worth having, and a rename has
to fix every row rather than none.

**The costs, recorded because they are real:**

- It is a registry somebody maintains, and in a one-person project it is
  overhead with no payoff until the second person arrives.
- It is the first half of an accounts system with no second half — no auth, no
  sync, no identity. A `Person` here is a name and an id, and must not start
  looking like a login.
- A document can name a person who was deleted. The palette path already has
  this shape and wants the same answer rather than a bespoke one — and deleting
  a person says how many documents name them first, the way Q35's warning does.

## Q44 · Is a bulk edit undoable? — **answered: no, and nothing is destructive**

**Answered 2026-08-07: no undo.** Status, tags and assignee are manifest
metadata rather than artwork — changing one touches no pixel, needs no document
open, and setting it back is the same gesture as setting it. The window says
what it did.

A second undo stack was rejected: `DocumentEditor`'s is per-document and holds
document state, so this would be a whole new system, and it would pre-empt *the
undo record becomes data* — unbuilt roadmap work that would want to own it.
A confirmation on every bulk edit was rejected as the friction that stops people
using bulk edits at all.

The accepted cost: a mis-drag on the status board is corrected by hand, and
Ctrl+Z will feel like it ought to work there.

## Q38 · How does an artist set a folder's glyph? — **answered: a grid plus free entry**

**Answered 2026-08-07: a small grid of common production glyphs with a text box
beside it.** The owner's point, and it is the sharper version of Q35: deriving
the glyph from what a folder carries is a designation smuggled back in. It
forces the code to pick a winner when a folder has several facets, and it will
pick wrong the first time somebody makes a prop folder with a pivot.

> *"I would rather have the glyphs to be selectable so that an artist/director or
> whoever could set the glyphs to a folder. So they can define the folder and
> what content belongs to it."*

**The line that keeps this from becoming a second designation: the glyph is a
label, the facets are the data.** Nothing in the code reads it — it is
`Notes` with one character, absent unless set, falling back to `🗀`. The AI path
asks for *the nearest folder above this with a reading*; export asks for a
pivot; neither asks what the icon is. So the artist names what a folder means
without anything downstream depending on their vocabulary, which is the part
that has to survive a production full of designations nobody wrote down.

Free entry was rejected alone (an empty box gives no hint the feature exists,
and typing an emoji is a coin toss on Linux) and a closed set was rejected
outright — it is a designation list wearing a different hat, and the first need
outside it is a dead end. The grid's cost is accepted: it is curated, so it
reads as opinionated, and somebody maintains it.

## Q39 · Does a folder row show what it carries? — **answered: only in a details panel**

**Answered 2026-08-07: the row stays name, glyph and duration; the facets show
when the folder is selected.** Against the recommendation, which was a dim
summary on the row.

The reason to prefer the row was discovery — otherwise a reading is invisible
until something goes wrong. The reason it lost is that the docker is already
dense and a tree of forty rows has to stay scannable, which is the thing the
panel is *for*.

**The cost, recorded because it is real:** nothing tells you a folder has a
hand-corrected reading until you click it. What keeps that from being a defect
is Q35's warning — it fires at the moment of the destructive act, naming what
goes, which is where the information actually has to be. The row summary would
only have improved discovery, not safety. If an artist ever loses a reading
anyway, this is the entry to revisit.

## Q40 · Do "subject" and "scene" survive as words? — **answered: gone from both**

**Answered 2026-08-07: gone from the code and from the UI.** `IsSubject`,
`IsScene` and `Subjects()` become facet questions — *the nearest folder with a
reading*, *folders with an order* — and the menu says **Read this folder…**.

Q35 dissolved the two records and then immediately collapsed the facets back
into two nouns, a glyph switch and a named collection. That is the same
rigidity one level down: a production has props, environments, effects,
vehicles, layouts and crowds, and under two privileged nouns every one of them
is "just a folder".

The cost is a rename across the call sites and the manual, and that *subject*
is a genuinely useful word which now lives only in
`docs/DESIGN-subject-reading.md` — where it describes the reading rather than a
kind of folder, which is what it always meant.

## Q35 · Do Character and Scene survive as records, or dissolve into folder attributes? — **answered: dissolve**

**Answered 2026-08-07: dissolve entirely.** `Character` and `ProjectScene` go.
A folder carries `Taxonomy`, `Pivot`, `Variants`, `Order` and `Notes`, each
nullable and absent until used. A character *is* a folder with a taxonomy; a
scene *is* a folder with an order. Both derived, neither declared.

**With a condition the owner added, and it closes a hazard the recommendation
missed:** *"but want the user [to know] they're about to do so."* Because
character-ness is now derived, it can be **lost by an action that does not look
like losing it** — clearing a taxonomy, or deleting a folder, silently takes the
pivot, the variants and a hand-corrected reading with it. Under the old model
"delete character" was explicitly destructive; under this one it is a side
effect.

So any action that would end a folder's character-ness or scene-ness **names
what goes before doing it** — *"This folder is Knight. Clearing its reading also
discards the pivot and 2 variants."* The specific list, not a generic "are you
sure", the way the export confirmation already counts what it would write.

**One thing to check before the first line is written:** does anything reference
a character *by id*? The cross-project character library (P1d) is the likely
holder, and if it does, that reference becomes a folder id and a second format
is touched by a change that looks like one.

**Blocks:** nothing. It is the fix for **B114**.

## Q36 · When does an existing project get migrated? — **answered: it does not**

**Answered 2026-08-07: no migration.** *"The application is in alpha, only used
by me, a single user. So no migration is needed. I am currently only testing and
no production whatsoever has been run."*

Writing a migration for zero real projects is cost with no beneficiary, and it
would be the second code path that `DESIGN-project-scoping.md` exists to remove.

**The consequence, recorded so it cannot be a surprise: project files written
before the change will not open.** Acceptable now, not acceptable in a month, so
the change carries its own tombstone — `ProjectManifest.Version` goes to **2**,
and a version-1 manifest is **refused with a sentence** rather than crashed on,
saying that the drawings are intact because documents are their own files in
their own format. Only the index is lost.

**Write the migration the day a second person has a project.** This entry is the
record that the decision was deliberate rather than overlooked.

**Blocks:** nothing.

## Q37 · Are brush presets the ninth scoped kind now, or later? — **answered: now**

**Answered 2026-08-07: now, scoping the preset id only.** `BrushPreset.Id` is
already a stable `Ids.NewId("preset")`, and a `ScopedResource` is a kind plus an
id — so `Lightbox.Core` needs no knowledge of `BrushPreset`, which lives in
`Lightbox.App`. It is the palette pattern with a different string.

Scoping the whole `BrushSettings` record was rejected: large, it would bloat
every manifest using it, and it would put two sources of truth behind one brush
plus a new question about which wins when the preset is edited.

**What it delivers:** *"a project could dictate which brush settings need to be
used"* — and it needs no enforcement concept, because the machinery already
separates the verbs. `Resolve` **offers** a set; `Nearest` **selects** one. A
project-level declaration coming back from `Nearest` *is* the dictate.

**Known cost, inherited rather than new:** a document can reference a preset
that was deleted or never shared. The palette path has the same shape and wants
the same answer, not a bespoke one.

**Blocks:** nothing.

## Q31 · Does a frame remember that a model made it? — **answered (a)**

**Answered 2026-08-07: (a), stored on the frame, absent unless AI touched it.**
A hand-drawn frame writes no key, so a document that never used the AI is
byte-identical to one from before the feature existed — the camera's rule.

**Blocks:** nothing. Phase 0 can proceed.

`docs/DESIGN-ai-correctness.md` puts a verifier and a deterministic fallback
behind every AI inbetween, which means three frames can look alike and have very
different histories: one the model got right, one it got wrong and was repaired,
one that fell back to the deterministic engine entirely.

**(a) Stored on the frame, absent unless AI touched it.** An artist returning
after a month knows which frames to trust and which to look at again. It is a
new key in the document, so hand-drawn frames write nothing — the camera's rule.

**(b) Session-only.** The timeline marks AI frames while the app is open and
forgets on reload. No format change; the information vanishes exactly when it is
most wanted.

**(c) Not tracked.** An inbetween is an inbetween.

**Recommend (a).** The whole feature is a claim about trust, and a claim you
cannot audit a month later is not one. Note the cost honestly: it is a document
format change, and *derived* data in the record is the mistake Q16 avoided for
placement readings — the defence here is that provenance is not derived from
anything, it is a fact about how the frame came to exist.

**Blocks:** phase 0 of the correctness pipeline.

## Q32 · What happens to a frame that fails verification and cannot be repaired? — **answered (c)**

**Answered 2026-08-07: (c), insert nothing and say why.** Against the
recommendation, and **the owner's reasoning defeats the recommendation rather
than merely overriding it**, so it is recorded in their words:

> *"For complex subjects — a human, a dog or something else complex and organic
> — I believe the deterministic inbetweener is prone to make mistakes. I'd
> rather have nothing than a frustration."*

The recommendation assumed the deterministic engine is a **floor**. On a box it
is. On a dog it is not — it is a confident wrong answer, and **B113 is the proof
rather than a worry**: four straight lines making a box, and the matcher crossed
the top and bottom edges over and collapsed the shape mid-motion. Whatever that
does to a quadruped's legs, it does silently.

So substituting it under a failed AI request is not a safe default, it is
swapping one unreliable answer for another and labelling the swap as safety. A
frame that is absent costs the artist a minute; a frame that is subtly wrong on
a dog costs them the time to notice, plus their trust in every other frame.

**What it costs, stated so nobody rediscovers it as a bug.** The artist asked
for four inbetweens and may get three. The status has to name which `t` was
refused and why, or the gap is a puzzle rather than a decision — "frame 3 of 4
was refused: the near arm did not stay between the keys" is the bar.

**Read per frame, not per batch:** the frames that passed are inserted and the
ones that failed are not. All-or-nothing would throw away good work because one
frame was bad. Say so if that reading is wrong — it is the one part of this
answer that was inferred rather than given.

**Blocks:** nothing.

**(a) Insert the deterministic answer, flagged.** Four inbetweens asked for, four
delivered, the fallen-back ones marked. Nothing silently missing, nothing
silently wrong.

**(b) Insert it silently.** No visual noise; the artist is never told the AI
failed, which makes the feature look better than it is.

**(c) Insert nothing and say why.** Strictest reading of "reliable", but a gap
in the timeline is work to find and fill, and the deterministic answer was
available the whole time.

**Recommend (a)**, and it depends on Q31: "flagged" needs somewhere to record
the flag. If Q31 lands on (c), this collapses into (b) whether we like it or not.

**Blocks:** phase 0.

## Q33 · An AI answer nearly identical to the deterministic one — reject or report? — **answered (a)**

**Answered 2026-08-07: (a), report only, never reject.** Distance from the
deterministic answer is a cost signal and a diagnostic, never a veto.

**Blocks:** nothing.

The deterministic engine is both the fallback and the reference, so distance
from it is free. Too far is suspicious. Too close means the model added nothing.

**(a) Report only, never reject.** Agreeing with the cheap engine is not
incorrect. Surface it as a cost signal — *"this model added nothing on 9 of 12
frames"* — and let the artist decide.

**(b) Reject and fall back.** Cleaner cost story, at the risk of throwing away
answers that were right.

**Recommend (a).** Rejecting a correct answer for being unimaginative is
indefensible on correctness grounds, and the cost argument is fully served by
saying so out loud. The threshold for "nearly identical" is also exactly the
sort of number that gets tuned until it passes.

**Blocks:** nothing — this can be added after phase 0.

## Q34 · Does the golden set ship with the app? — **answered (a)**

**Answered 2026-08-07: (a), it ships.** Grading an artist's own model is the
bring-your-own-model story rather than a development convenience, and it is what
separates *connectable* from *usable*. The obligation that comes with it: the
set has to stay honest, so it is committed, reviewed, and changes to it are
changes to a published claim.

**Blocks:** nothing. Phase 2 can proceed.

A committed set of keyframe pairs with known-good answers, scored by the
verifier, is what turns "reliable" into a number per model.

**(a) Ships.** Point Lightbox at any local or hosted model and it reports what
that model can and cannot do. This is the bring-your-own-model onboarding story,
and the difference between *connectable* and *usable*.

**(b) Development artifact only.** Stops regressions in the built-in providers;
an artist with an unusual local model is back to trial and error.

**Recommend (a).** Constraint 2 is that artists bring their own model; shipping
the grader is what makes that a feature rather than a shrug. Cost is install
size and the obligation to keep the set honest.

**Blocks:** phase 2.

## Q25 · Is a character sheet a document, or part of one? — **re-answered (b), inside a project**

**Re-answered 2026-08-12, by the owner, overriding (a):** inside a project a
sheet is **its own file**, filed on a folder the way a document is. The owner's
words: *"every document can create a reference sheet but it's always assigned
to the first top folder by default. So it becomes a file. All documents within
the folder can access the character sheet. We see it in the project docker and
through the project manager we can re-assign if need be."*

What that cost, and where the line was drawn — the four sub-decisions were
prompted and answered the same day:

- **Filed like documents** (`SheetRef` with `FolderId` in the manifest), not a
  scoped-resource declaration. One concept — *where is it filed* — and
  visibility is the folder's subtree. The reference declarations stay for what
  filing cannot say (B133 still owns their unread half).
- **On disk inside the assigned folder's directory** (`<folder>/<slug>.sheet.json`),
  so the tree in a file manager matches the panel; re-assigning therefore
  **moves a file**, disk-first like a document move (B106's order).
- **Standalone documents keep (a)** — sheets stay in `Doc.ReferenceSheets`,
  B66's prompt-to-save unchanged. Two storage shapes exist, switched by
  context; that is the accepted cost of not making loose files travel in pairs.
- **Migration is promote-on-open**: a project document carrying old in-document
  sheets lifts them into the registry (filed on its top folder) the first time
  it is read, and its next save writes both halves. Idempotent because sheets
  keep their ids.

(a)'s reasoning below is kept because most of it still holds — the format-change
cost it predicted is exactly what was paid, and what finally justified paying it
was the sharing argument the last line of (a) anticipated: *"if sheets later
need to be shared between documents, that is the argument for (b)."* B133's
measurement showed sheets could not reach sibling documents at all, and the
docker/window visibility the owner asked for needs a real slot in the manifest.

**The first answer, 2026-08-04: (a), it stays part of a document.** No format
change, no new project-manifest slot, and no new docker row type that is not a
file. The reported pain is losing work — *"character sheets are not saved to
disk"* — and that is fixed by making sure there is a file behind the document
the sheet lives in, which costs one prompt.

The docker-visibility half of the report is answered rather than implemented: a
character sheet **is** visible in the project docker, as the document that
contains it. If sheets later need to be shared between documents, that is the
argument for (b) and it is a better one than this.

**B66 is unblocked** and is now two ordinary pieces: ask for the name before
writing anything (B65's rule on another surface), and prompt to save a document
that has never been saved so the sheet has somewhere to live.

**Blocks:** nothing.

The report says: *"Outside of a project (single file) a character sheet is a
manually saved document. Creating a character sheet should directly prompt
saving. In a project, a character sheet is directly added, similar to how the
project dockers add them directly."*

That describes a character sheet as **a document with its own file**. The code
has it as **part of a document**: a `ReferenceSheet` lives in
`Doc.ReferenceSheets`, so it is saved when its document is saved and has no file
of its own. The project manifest holds `DocumentRef` (animations, shots,
project documents) and `Character` — there is no slot a reference sheet could
occupy, which is why it cannot appear in the project docker today.

So the two halves of the report need different things, and only one is a defect:

**(a) It stays part of a document, and the bug is that an unsaved document loses
it.** Then the fix is the prompt: creating a sheet on a never-saved document
prompts to save, so there is a file behind the work. Nothing in the format
changes, nothing new appears in the project docker, and "not visible in the
project docker" is answered with *it is inside a document, and the document is
listed*. Cheapest by a wide margin.

**(b) It becomes a document in its own right** — its own file, its own
`DocumentRef`, listed in the docker beside animations. Matches the report's
wording most literally and makes "add it directly in a project" fall out for
free. It is a **format change**: sheets move out of `Doc`, existing documents
need migrating, and `CLAUDE.md`'s rule that a proposal requiring a format change
has "drifted into redefining what a document is" applies squarely.

**(c) Both — it stays in the document and the docker learns to show it.** No
format change, and the docker gains a row type that is not a file, which every
path that maps a row to a path (`PathOf`, reveal, copy path, rename in **B64**)
then has to have an answer for.

**Recommend (a)**, because the reported pain is losing work — "character sheets
are not saved to disk" — and (a) fixes exactly that at the cost of one prompt.
The docker visibility that (b) and (c) buy is a smaller complaint, and (b) spends
a format migration on it. If sheets later need to be shared between documents,
that is the argument for (b) and it is a better one than this.

## Q22 · Is a "Document" called a Workfile, and what else is in that menu? — **answered (a)**

**Answered 2026-08-04: (a), *Document* stays.** Fix the grouping and the dead
entries; do not rename. The report says the *menu* is undecipherable rather than
the *word*, and folders being visually indistinguishable from files is the
complaint the fix should answer first. (b) stays available if the confusion
survives that — but two names for one thing is usually the cause of the next
confusion, and `Document` is load-bearing in the manual, the roadmap,
`DocumentRef` and the MCP surface.

**B63 is unblocked entirely**: both halves are now ordinary work.

**Blocks:** nothing.

Raised in a report: the create-in-project menu is "undecipherable — what is a
folder and what is a workfile", with a suggestion to rename *Document* to
*Workfile*.

The defect underneath is filed (**B63**: entries that produce nothing, and no
visual split between folders and files). What cannot be decided from the code is
the vocabulary. *Document* is used throughout the manual, the roadmap and the
serialization (`DocumentRef`, `NewDocumentSettings`), so a rename is not a label
change — it is a rename across the UI, the docs and the artist's mental model.

**(a) Keep *Document*.** Fix only the grouping and the dead entries. Cheapest,
and the word is already established everywhere else in the product.
**(b) Rename to *Workfile* in the UI only.** The record keeps `Document`. Solves
the reported confusion at the cost of two names for one thing, which is the
thing that usually causes the next confusion.
**(c) Rename everywhere.** Consistent, and it touches the manual, the roadmap,
the MCP surface and every serialized name — an expensive change to make for a
menu.

**Recommend (a) plus B63's grouping fix**, on the grounds that the report says
the *menu* is undecipherable rather than the *word* — folders and files being
visually indistinguishable is the complaint the fix should answer first. If the
confusion survives that, (b) is still available.

## Q23 · How does a tab say whether its document belongs to a project? — **answered (a)**

**Answered 2026-08-04: (a), a badge on the tab.** What was asked for, and the
whole of the reported need: self-contained, no OS interaction, sitting exactly
where the ambiguity is. The window title (b) is deliberately not taken now —
Avalonia sets it per window rather than per tab, so with several tabs open it can
only ever describe the active one, and that is a second design rather than a
free addition.

Worth building *after* **B67**, not before: when dockers become document-scoped
the panels visibly change as tabs switch, and the badge is what stops that
reading as a bug. Filed as roadmap work rather than a bug — nothing is broken,
something is absent.

**Blocks:** nothing.

Reported alongside **B67**: "there is no good way to identify open documents
(tabs) as part of a project or not. A small boxed P in the tab would already
help a lot. In the title bar of the OS would be a great additional position."

The reporter has proposed a design, which makes this a question about *how far*
rather than *whether*. It matters more once B67 lands, because when dockers
become document-scoped the panels an artist sees will change as they switch
tabs, and a visible reason for the change is what stops that reading as a bug.

**(a) A badge on the tab.** What was asked for. Self-contained, no OS
interaction, and it sits exactly where the ambiguity is.
**(b) Badge plus the window title.** The title bar is where every other
application says which file is open, and Avalonia sets it per window rather than
per tab — so with multiple tabs it can only describe the active one. That is
probably fine and is worth saying out loud rather than discovering.
**(c) The project name rather than a badge.** More informative and much wider;
tabs are already short on room.

**Recommend (a) first**, because it is the whole of the reported need and is
cheap, with (b) as a follow-up once B67 makes project membership matter visibly.

## Q24 · What is a saved brush setting scoped to, and does saving it need a button? — **answered: automatic**

**Answered 2026-08-04: automatic persistence, no button.** Brush tuning survives
a restart on its own; there is no explicit *save settings* action and therefore
no second mechanism with a different lifetime competing with the first. The
reported pain was losing settings on restart, and that needs no new concept.

The scope question the button would have forced is deferred with it. `BrushScope`
already feeds a new document the project's brush
(`ANewDocumentInTheProjectIsFedThatBrush`), so per-project exists; per-file does
not, and nothing now requires choosing between them. **B71** is therefore the
whole of the work, and it keeps the rule that a brush left at its defaults writes
no keys.

**Blocks:** nothing.

Reported: "individual brush settings need to be cached for the duration of the
session… when brush settings are changed, present the user a save settings
button next to the all brush settings. This is stored per file and/or per
project."

Two decisions are tangled here and only the first is required by the bug.

**Automatic or explicit.** B71 as filed makes tuning survive a restart
automatically. An explicit *save* button is a different promise — it says the
tuning is a named thing an artist commits to, and that unsaved changes are
discardable. Both are defensible; shipping both without deciding gives an artist
two mechanisms with different lifetimes and no way to tell which one is holding
their brush.

**And what the scope is.** `BrushScope`/`BrushScopeDefaults` already exist —
a project feeds a new document its brush, guarded by
`ANewDocumentInTheProjectIsFedThatBrush` — so *per project* is built. *Per file*
is not, and the report asks for "per file and/or per project", which is the part
that needs a person: the two disagree the moment a document in a project is
opened, and something has to win.

**Recommend automatic persistence (B71) first and the button deferred**, because
the reported pain is losing settings on restart and that needs no new concept.
If the button still seems wanted afterwards, it is a small addition to a
mechanism that exists rather than a second one competing with it.

## Q11 · What a "reusable animation preset" would be that a cycle symbol is not — **answered (b)**

**Answered 2026-08-03: (b), a timing preset, and the other line is struck as (a).**
One item, specified: *save an exposure pattern and apply it to a range of cels*.
It re-exposes drawings that already exist, which is the half of frame-by-frame
work a symbol cannot carry — a symbol carries drawings, a timing preset carries
their spacing.

## Q20 · What frame bounds does an Asset project export from an unbounded canvas? — **answered (b), and the question was half wrong** — *superseded by Q71: the infinite canvas was removed 2026-08-12*

**Answered 2026-08-04.** Two corrections, and the second one dissolves most of it.

**The premise was too narrow.** This was framed as a game-animation problem, and
the tool is a drawing, painting *and* animation application. An infinite canvas
belongs to the **Shot** target — a world the camera frames, delivered as video —
not to the Asset one, where the canvas *is* the output. The game export pipeline
is already built and is not what this feature serves.

**And the conflict is not a project-type gate.** An unbounded canvas and a fixed
frame-bounds sprite export are **mutually exclusive by construction**, in every
project type — that is a fact about the two features, not about a manifest. So
nothing is gated: the pair is declared incompatible, the refusal names the fix,
and authoring an export region resolves it. Reach survives untouched, and
*Making reach unconditional* stands as written. Recorded as its own roadmap item.

So the answer is **(b), an authored export region** — arrived at from the other
direction than expected. It is not the rule for deriving bounds from an
unbounded canvas; it is the thing an artist authors to *make* the canvas bounded
where a bounded answer is required. (a) is still the right starting value for
that region — bounds-of-ink as a first guess, then draggable — but it cannot be
the mechanism, because a derived bound changes silently when a stray mark lands
in frame 40, and a game build cannot take that.

**Blocks:** nothing now. `docs/DESIGN-infinite-canvas.md` can be built against
this.

*The analysis below is what the answer was reached from, kept for the reasoning.
Its framing is the one the answer corrects: it treats the asset case as central.*

`CLAUDE.md` makes both of these first-class, and this is the one place they meet
head-on. **Assets** — "the canvas *is* the output. There is no camera, frame
bounds must stay consistent, and every frame is a deliverable." An **infinite
canvas** is defined by not having bounds. A sprite sheet is defined by having
consistent ones. So "the asset workflow loses" is not an available answer.

It cannot be answered from the code, because the code has never had to say what
the edge of a drawing is — `Scene.Width`/`Scene.Height` have always answered it
and an unbounded canvas removes the answer rather than changing it.

**(a) Bounds of ink, per scene.** Export the rectangle that encloses every
stroke in the sequence, identical for every frame. Needs nothing authored and it
is what an artist means by "the drawing". The risk is that it is *derived*: add
one stray mark in frame 40 and every previously exported frame changes size,
silently, which is exactly the kind of thing that breaks a game build.

**(b) An authored export region.** A rectangle the artist places once, saved with
the project. Stable by construction — the property assets need — and it makes
the bounds a thing you can see and drag rather than a consequence. Costs a UI
surface and one more thing to set up before the first export.

**(c) The camera, when one exists; ink otherwise.** Reuses machinery that is
already built, keyframed and exported. But `CLAUDE.md` says a camera is
shot-level machinery that must stay absent from asset work — this would make the
asset target depend on the one thing it was defined as not having.

**Recommend (b)**, on the grounds that consistency is the requirement rather
than convenience, and only (b) gives it by construction. (a) is the better
default *inside* (b) — an authored region that starts at the bounds of ink is
one click rather than a blank rectangle.

## Q21 · Is the infinite canvas a document property or a project-type default? — **answered (c), both, and they are not alternatives** — *superseded by Q71: the infinite canvas was removed 2026-08-12*

**Answered 2026-08-04: both — and the question contained a false choice.**
"Document property *or* project default" reads as two designs; it is one. The
reach rule already says exactly this: a project type decides *what is on, what
is in front of you, and what a new document starts with — never what the
application can do*. So the **property lives on the document** (that is the
capability, available everywhere) and a **project supplies the default** (that
is what a new document starts with). Answering "both" is the rule applied, not
a compromise between two readings.

Both cases the answer came from are real and neither needs a mechanism the
other lacks: somebody making *one* infinite-canvas animation turns the property
on for that document, and somebody producing a run of product or service
animations sets it once on the project so every new document starts that way
rather than switching it on each time.

**The mechanism exists and is proven, which is why this is cheap.** A project
already feeds new documents a default brush — `BrushScope`,
`BrushScopeDefaults`, guarded by `ANewDocumentInTheProjectIsFedThatBrush`, and
by `AProjectThatNeverAsksForThisWritesNoBrushKey` so an unused default writes
nothing. An infinite-canvas default is the same shape against the same
precedent. Note it is the **project** that carries it, not only the project
*type*: a studio's own project can default to unbounded whatever type it is,
which is what the reach rule means by defaults never deciding availability.

**Blocks:** nothing. It was never a blocker — a project type can only default a
property that exists, so the document property comes first under either answer.


*The analysis below is what the answer was reached from, kept for the reasoning
rather than as a live recommendation — (a) and (b) turned out to be one design.*

The reach rule settles the hard half already: every feature is reachable in every
project type, so this is not "who is allowed an infinite canvas". It is what a
new document starts with, and whether turning it on later is a document edit or a
project setting.

**(a) A document property, off by default everywhere.** Matches the camera
exactly — absent from the file until authored, askable for anywhere. Simplest,
and "optional means absent" falls out for free.

**(b) A project-type default.** A storyboard or an illustration starts unbounded,
a sprite project starts fixed. More convenient on day one, and it puts a
behaviour an artist has to reason about into a manifest they rarely open.

**Recommend (a)** until somebody asks for (b), because (a) is a prerequisite for
(b) rather than an alternative to it: a project type can only default a property
that already exists.

## Q19 · Are Linux and macOS shipping targets, or only development ones? — **answered (a)**

**Answered 2026-08-04: (a), development targets only — Windows is what ships.**
The glibc floor is accepted and closes as not-applicable, on this question's own
reasoning rather than in spite of it: `build.yml` publishes exactly one artifact,
`win-x64`, so nothing crosses the floor and a rising one cannot lose a user who
has nothing to download. Anyone on Linux today built from source and therefore
has a .NET SDK, which puts their distro far above either number.

The consequences, so they are not re-derived: the `net10.0` upgrade is unblocked
and has landed; **B32**'s fix points **up** (the solution moved to `net10.0`
rather than the MCP server moving down to `net8.0`); and a `linux-x64` publish
job stays the separate concern `DESIGN-net10-upgrade.md` files it as, rather than
becoming part of the upgrade. Revisit if a Linux or macOS artifact is ever
shipped — that, not the glibc number, is the thing that would make the floor
matter.

**Blocks:** the `net10.0` decision in `docs/DESIGN-net10-upgrade.md`, and
whether **B32**'s fix points up or down. *(Both now resolved by the answer above.)*

The upgrade is otherwise clean. Avalonia 12.1.1 and SkiaSharp 3.119.4 both
publish explicit `net10.0` dependency groups, every .NET 9 and .NET 10 breaking
change on the official lists was checked against real code and none apply, and
.NET 8 leaves support in November 2026 — so standing still is also a decision
with a date on it. One consequence needs a person: a self-contained `net10.0`
Linux build requires **glibc 2.27** (Ubuntu 18.04-class) where `net8.0` needed
**2.23** (Ubuntu 16.04-class). Windows and macOS floors do not move.

**The reason this is a question and not a footnote is that it cannot be
answered from the code.** It depends on who runs this, and nothing in the
repository records that.

What the code *does* say is that the floor is currently theoretical.
`build.yml` publishes exactly one artifact, `win-x64`, cross-compiled from
Ubuntu. **There is no Linux build and no macOS build shipped at all**, so
today a rising Linux floor cannot lose a user who has nothing to download.
Anyone running this on Linux right now built it from source, which means they
have a .NET SDK, which means their distro is far newer than either floor.

So the glibc number is the wrong thing to decide. The thing to decide is
whether the missing Linux and macOS artifacts are an omission or a choice —
because that is what makes the floor matter, and it is also what decides
whether the publish-path half of **B32** should grow a `linux-x64` job.

**(a) Development targets only — Windows is what ships.** Take the floor; it
costs nothing measurable, because nothing crosses it. Linux stays what it is
today, the place the tests run and the Windows bundle is built. The devcontainer
serves that fully and the glibc question closes as not-applicable.

**(b) Shipping targets, not yet built.** Then the floor is real but still
almost certainly fine — Ubuntu 18.04 left standard support in 2023, and an
application that wants a tablet and a GPU is not being run on an eight-year-old
distro. Worth saying out loud rather than assuming, and it makes a `linux-x64`
publish job part of the upgrade rather than the separate concern
`DESIGN-net10-upgrade.md` currently files it as.

**(c) Stay on `net8.0`.** Keeps the floor and keeps the smaller diff, which is
**B32**'s own prescription. It buys three months and pays a migration's
verification cost twice — once to prove a downgrade changed no pixels, again in
November to prove the upgrade did not.

**Recommend (a)**, on the evidence that the only artifact anyone can download
is a Windows one and no issue in the tracker asks for another. It is the one
reading that makes the glibc floor a non-question rather than a small risk
taken quietly — and if (b) turns out to be the truth, the floor is still very
likely fine and the thing that changes is scope, not safety.

**Blocks:** the last `[?]` but one in Pillar 3.

The pillar lists *Reusable animation presets* and *Animation templates* as
separate from the Animation library — but the Animation library shipped, and
what it delivers is a multi-frame symbol placed with a frame offset, which is
already a reusable animation. Two placements of one cycle run the same drawings
out of step. Whatever these two items are for, it is not that.

The reading that survives is that they are about **timing rather than
drawings** — the part of frame-by-frame work that a symbol does not carry:

- **(a)** *Strike it.* The Animation library is the reusable animation, and
  these two lines are a pre-implementation guess that the design outgrew. A
  roadmap that keeps items nothing can distinguish from shipped ones is the
  wish list this file's checkbox rules exist to prevent.
- **(b)** *A timing preset* — a saved exposure pattern (on 1s, on 2s, a
  slow-in of 1-1-2-3-4) applied to a selected range of cels, re-exposing the
  drawings that are already there. This is a real animator's tool, it is
  genuinely absent, and it is nothing a symbol can express, because a symbol
  carries drawings and this carries their spacing.
- **(c)** *A motion preset* — keyframed placement transforms, so a symbol can
  be told to arc across the frame over twelve cels. This is the largest of the
  three and it needs a decision about whether placements become animatable at
  all, which is a pillar-4 question wearing a pillar-3 hat.

**Recommend (b), and strike the other line as (a).** One item, specified:
*"Timing presets — save an exposure pattern and apply it to a range of cels."*
It is the only one of the three that is both absent and unambiguous.

## Q12 · Whether an animation template is a document or a project type — **answered (a)**

**Answered 2026-08-03: (a), a document with a flag.** Designed out in
`docs/DESIGN-templates.md`, because "changeable on the fly" was asked for
explicitly and it is the property that decides the mechanism: a template is
**copied, never referenced**, so editing one is safe precisely because it
cannot reach back into work already started from it. (c) stays available —
a starter pack is (a) plus content, and needs no change to the mechanism.


**Blocks:** the last `[?]` in Pillar 3.

*Animation templates* — starting a new animation from a skeleton rather than an
empty document — is real and absent. What is undecided is where it lives, and
the app already has two mechanisms that overlap it: `NewDocumentSettings`
(size, fps, frame count) and project types (which decide the workspace).

- **(a)** *A document in the project marked as a template.* Copy it, rename it,
  start drawing. Costs nothing new — a template is an ordinary animation with a
  flag — and an artist can make one out of work they have already done, which
  is where real templates come from.
- **(b)** *A built-in list* (walk cycle 8 on 2s, run cycle 6, blink 4, take 12).
  Better on day one, worthless on day two: every studio times its own walk
  differently, and a list nobody can add to becomes a list nobody uses.
- **(c)** *Both* — built-ins that are seeded as project documents on first use,
  so they are editable from the moment they appear.

**Recommend (a).** It is the smallest thing that is not a guess about how other
people animate, and (c) is (a) plus a starter pack, which can be added later
without changing the mechanism.

---

## Q10 · Does wet paint survive between strokes — **answered (c), not yet buildable**

**Answered 2026-08-02: (c), a bounded wet window, with the size of the window a
brush setting.** `0` means the paint is dry the moment the pen lifts — exactly
today's behaviour — and `N` means the next `N` strokes can still pick it up.

Kept here rather than moved to `DECISIONS.md` because the decision is settled and
the *implementation is not startable*: `MediumSimulator` is a static pure
function of (coverage, existing pixels, paper, settings) that builds its
lattice per stroke and discards it. There is no state between strokes for a
window to bound. Adding the setting now would put a control in the brush
options that changes nothing, which charter **O7** exists to stop.

What the answer already constrains, so the fluid pass does not have to
re-litigate it:

- **The window size is stored per stroke** (invariant 4), not read from the
  tool at render time. Changing your brush must never re-wet a painting you
  finished last month.
- **Default 0 keeps every existing document byte-identical.** Absent by
  default, the camera's rule again.
- **A stroke's render depends on the previous N strokes**, which is the real
  cost. Re-rendering a frame already replays in order, so that part is free —
  but editing or undoing a stroke in the middle now invalidates the *next* N
  as well, and the frame cache and invariant 6 have to know it. Bounded by N
  rather than by the whole history is precisely why (c) was chosen over (b).

---

## Q13 · What counts as the same sheet of paper — **answered (c)**

**Answered 2026-08-02: (c).** The wet window is per frame and per layer, and
**generated strokes never carry wetness** — the inbetweener and the MCP surface
write `WetStrokes = 0` whatever the source stroke said.

Per frame and layer because a cel is a separate drawing and a layer is not
paper; it is the same answer Q6 gave for what a smudge samples, and it keeps
the replay trivially bounded. The extra clause is a determinism one: an
inbetween whose appearance depended on how many strokes the generator happened
to emit before it would diverge between runs, which is invariant 2 broken by a
side door.

## Q14 · What an eraser does to wet paint — **answered (a)**

**Answered 2026-08-02: (a).** An eraser is a stroke like any other. It spends
one of the window's `N` and removes pigment; the moisture goes with the pigment
it belonged to.

The physical answer — an eraser that smears wet paint — is a brush somebody can
build later on top of the advection loop, not a property the eraser has to have
from the start. Recorded because it is a real limitation and an artist erasing
into a wash will find this hard-edged: **if that turns out to matter, the fix is
a new brush, not a change to the eraser.**

## Q15 · Is a mirrored stroke one stroke or two? — **answered (c)**

**Answered 2026-08-07: (c), one stroke while drawing with an explicit "break
symmetry" that expands to two.** So `Mirror` lives **on the stroke**, not on the
scene — that is the part this answer actually settles, and the reason it could
not be deferred.

Turning symmetry off is meaningful while the stroke is whole: it removes the
reflection rather than leaving an orphan. Breaking symmetry is a deliberate,
undoable act that writes two ordinary strokes and forgets the pairing, which is
correct — after the break they are two marks and pretending otherwise would owe
the artist a promise nothing keeps.

**Blocks:** nothing. Symmetry can be built.


Symmetry does not exist yet and it should — for character design, which is what
this application is for, a vertical mirror is not a nicety. What has to be
decided before anything is written is what the *record* holds when an artist
paints with a mirror on.

**(a) One stroke, rendered twice.** `Stroke.Mirror` names an axis; the engine
stamps the dabs and their reflections. The record stays the size of what was
drawn, and turning symmetry off afterwards is meaningful — it removes the
reflection rather than leaving an orphaned copy. Invariant 1 pushes here: the
mark is one gesture, so one entry.

**(b) Two strokes, emitted at commit.** Simpler in the engine, and the artist
can then edit, erase or transform the halves independently — which they
frequently want, because symmetry is usually a scaffold rather than a promise.
The cost is that the record has no memory of the pair, so "turn symmetry off"
cannot mean anything and the two halves drift as soon as either is touched.

**(c) Both — (a) while drawing, with an explicit "break symmetry" that expands
to (b).** Probably where this ends up, and worth naming as a target rather than
arriving at by accident, because it decides whether `Mirror` is on the stroke or
on the scene.

The reason it is a question rather than a guess: (a) and (b) are not
interchangeable later. A file written under (b) cannot be read back as (a), so
picking the easy one first forecloses the other.

## Q16 · Is a subject reading stored, and what makes it stale? — **answered (c)**

**Answered 2026-08-07: (c) for placement, on the character for taxonomy.** The
split the design already made decides the storage, so there is one answer per
half rather than one answer:

- **Taxonomy lives on the `Character` in the project manifest.** Durable, small,
  reviewable, and the one thing here an artist may correct by hand — so it goes
  where their correction survives a cache wipe, a reinstall and a clone. It is
  authored data the moment they touch it, and authored data belongs in the
  record.
- **Placement lives in a cache beside the autosave, never in the
  `.lightbox.json`.** Keyed by a content hash of the frame's effective strokes.
  Staleness is then not a problem to solve: a hash that no longer matches is a
  cache miss, and a cache miss costs one call. Losing the whole cache costs
  nothing but time, which is exactly the property that makes it safe to throw
  away whenever anything is uncertain.

**Why not (b) — stored in the document with a hash.** It buys the same cheap
batches and charges the document for them: every frame carries a reading, the
file grows with something no render reads, and a merge between two branches has
to reconcile two models' opinions about the same drawing. The hash makes
staleness detectable, not free.

**The consequence that makes this more than a preference:** invariant 1 says the
stroke record is the document, and a placement reading is *derived from* the
record. Putting derived data in the record is the mistake the codemap merge
driver exists to undo elsewhere in this repo. Taxonomy escapes that test because
it is not derived from any one document — it is a statement about a character,
and once an artist edits it, it is theirs.

**One thing this answer does not decide,** because it is not a preference: the
deletion test still governs both halves. Delete every reading — cache and
taxonomy alike — and a finished document must re-render byte-identical. If that
ever fails, something is reading the analysis at render time and invariant 2 is
gone. It is the reading's first test, before any of the storage above.

**Blocks:** nothing now. The reading is buildable; **Q17** still blocks the
inking half only.

`docs/DESIGN-subject-reading.md` splits the reading into **taxonomy** (per
character, stable) and **placement** (per frame, disposable). The taxonomy is
clearly worth storing — it is reviewable, correctable, and true of every frame.
The placement is the question.

**(a) Never stored.** Every operation reads the frame it is about to work on.
Always fresh, nothing to invalidate, and no new keys in the file. The cost is
that two runs of the same inking pass on the same drawing can differ, and that
a batch across two hundred frames pays for two hundred readings.

**(b) Stored with a content hash of what it read.** Staleness detection is then
free: the hash of the frame's strokes no longer matches, so the reading is
discarded rather than trusted. Batches get cheap. The cost is file size and one
more thing that can be subtly wrong — a reading that matches the hash but was
produced by a model that has since changed its mind.

**(c) Stored, but only as a cache outside the document** — beside the autosave
rather than in the `.lightbox.json`. Keeps the record clean, keeps the batch
cheap, and makes "it went stale" a non-event because losing the cache costs
nothing. The cost is that a reading an artist corrected by hand would live
somewhere that gets deleted, which argues that corrected readings are taxonomy
and belong in (a)'s per-character half anyway.

Leaning (c) for placement and stored-on-the-character for taxonomy, because it
puts the durable half where an artist can edit it and the disposable half where
losing it is free.

## Q17 · Does an inking pass replace the pencils or land on its own layer? — **answered (c)**

**Answered 2026-08-07: (c), one Ink layer for the whole sequence**, its cels
lined up with the pencils'. Non-destructive without the two-hundred-layer
problem, and it uses the layer model as it already stands.

**It carries a UI commitment, and that is the half worth writing down:** an
inking pass runs over a **range**, not a frame. A per-frame gesture would make
one layer per frame by accident, which is the option this answer rejected. So
the surface that starts an inking pass takes a range the way the exposure-sheet
operations already do.

**Blocks:** nothing. Inking is unblocked — it was the last thing waiting on this.


**(a) Its own layer, pencils untouched and hidden.** What an inker does on
paper, non-destructive, and the artist can re-run with a different style
without losing anything. Costs a layer per inked frame, which over two hundred
frames is a layer count nobody wants to scroll.

**(b) Replaces the strokes in place, one undo step.** Matches "the stroke record
is the document" — the inked lines simply *are* the frame now. Cheap, tidy, and
the artist keeps the pencils by duplicating the layer first if they want them,
which is a thing they already know how to do.

**(c) Its own layer, but one layer for the whole sequence** — an "Ink" layer
whose cels line up with the pencils'. This is what the layer model already
supports and it is probably the answer, but it assumes an inking pass is run
over a range rather than a frame, which is a UI decision as much as a record
one.

The reason it cannot be deferred: (a) and (b) produce different documents from
the same gesture, and a file written under one cannot be reinterpreted as the
other. Pick before the first pass ships, not after.

## Q18 · Do flat point arrays cost schema adherence? — **answered (c)**

**Answered 2026-08-07: (c), flat arrays for points only, objects for everything
else.** Points are 99% of the volume and the only part that repeats; `tool`,
`color` and `label` keep their names, so the field whose loss actually costs an
inbetween keeps its key.

**Adopt it with the measurement rather than instead of it.** The adherence
claim in `StrokePayload.cs` was undated and unmeasured, and this answer does not
make it true — it makes the risk small enough to take. The golden set (Q34) is
the natural place to watch it: **label retention** belongs in the scores, so a
regression shows up as a number rather than as a bad inbetween somebody notices
weeks later.

**Blocks:** nothing.


The measurement is settled and the trade is not. `docs/DESIGN-ai-payload.md`
has the numbers: writing a point as `[123.4,567.8,0.55]` instead of
`{"x":123.4,"y":567.8,"pressure":0.55}` takes **57%** off the payload, and at
2560 points in a 40-stroke frame pair that is the largest encoding win
available — 102 KB down to 44 KB, ~26k tokens down to ~11k.

Against it, `StrokePayload.cs` says the wire shape mirrors the document format
because it "measurably improves schema adherence". That claim is undated,
unmeasured anywhere in this repo, and entirely plausible: a model has seen a
great many `{"x": …}` objects and very few positional triples, and positional
encodings invite exactly the failure that matters most here — a transposed
coordinate, or a dropped `label`.

**A lost label is a lost correspondence**, which is a worse inbetween. So this
is not "57% cheaper, ship it"; it is 57% cheaper against a quality risk nobody
has quantified.

What would settle it: the same twenty frame pairs through both encodings on at
least two providers, scoring label retention, point-count fidelity and whether
the inbetween lands between its keys — the check `AiConnectionTester` already
implements. Real API calls, so it is a deliberate spend rather than something
to slip into an unrelated commit.

Three ways it could land:

**(a) Keep objects.** Adherence is worth more than tokens, and the bigger win
is sending fewer strokes anyway — six times bigger, per the same document, and
with no format risk at all.

**(b) Flat arrays everywhere.** If adherence holds across providers, 57% is not
a rounding error and refusing it out of caution is superstition.

**(c) Flat arrays for points only, objects for everything else.** Points are
99% of the volume and the only part that repeats; `tool`, `color` and `label`
stay named, so the field most at risk keeps its key. Probably the answer, and
it is still a guess until somebody runs it.

This is the standing disagreement between **ai-engineer** and **art-director**,
and it is written here rather than settled by whichever of them ran last.

## Q26 · When a textured line is re-shaped, may its texture change? — **answered (a)**

**Answered 2026-08-07: (a), accept it. The grain belongs to the canvas.** A mark
is a function of where it is, the way a real pencil's grain is a function of the
paper's tooth under it.

**This closes the question rather than deferring it, and that is worth the
sentence:** (b), (c) and (d) are now *rejected*, not "later". Nothing needs a
seed origin on the stroke, nothing needs arc-length seeding, and — the one that
matters most — **no tunable radius enters the render path**. Invariant 4's
suspicion of hidden knobs is upheld for free, and invariant 2 stays exactly as
written, with no second costume to check for.

**What it obliges.** Pillar 0's re-shaping item ships with a manual line saying
that moving a textured line changes its grain, and saying *why* — not as an
apology but as the same fact as the paper's tooth. An artist who wants the mark
preserved exactly moves the layer rather than the line, which is a real answer
and should be the one the manual gives.

**Blocks:** nothing. Re-shaping is unblocked.


Invariant 2 seeds every dab dynamic — scatter, size, roundness, rotation, all
three colour jitters — from the dab's position via `Hash01`. That is what makes
a mark reproducible on reload, on undo and through the inbetweener, and it is
not negotiable.

The consequence nobody has had to face yet is that **it also means moving a line
changes what the line is made of.** Drag a control point and the dabs near it
re-seed: the grain shifts, the scatter lands elsewhere, a bristle that was
splitting now does not. Correct by the invariant, and wrong to the artist, who
expects to nudge a line and see *the same line, somewhere else*. Pillar 0's
re-shaping item cannot ship without an answer, and the answer changes the
record, so it cannot be retrofitted.

**(a) Accept it, and say so in the manual.** The mark is a function of where it
is, the way a real pencil's grain is a function of where the paper's tooth was.
Free, honest, and it makes re-shaping feel unreliable for exactly the brushes
people would most want to re-shape.

**(b) A seed origin stored per stroke.** Hash from `position − origin` rather
than from position, and carry the origin through an edit. The texture then
travels with the line. Cheap, and it changes the meaning of every existing
stroke unless the origin defaults to zero — which it can, so old documents
render identically. The catch: two strokes drawn in different places with the
same shape now have the *same* texture, which is the flicker invariant 2 exists
to prevent, in a new costume.

**(c) Seed from arc length along the stroke instead of from position.** The
grain belongs to the stroke rather than to the canvas, so it survives any edit
including a wholesale move. Also kills (b)'s duplication problem, because two
strokes still differ if anything else about them differs. The cost is real: a
dab's seed now depends on every point before it, so an edit near the *start*
re-seeds everything after it — the opposite failure, and arguably worse.

**(d) Re-seed only what moved, and blend.** Keep position seeding, but let dabs
within some radius of an untouched point keep their old values. Preserves the
line away from the edit at the price of a rule with a tunable in it, and a
tunable in the render path is the thing invariant 4 is suspicious of.

Not answerable by measurement, which is why it is here: every option renders
*something* defensible, and the question is which one an animator would call
the same line. **art-director holds the veto on that**, and **ai-engineer holds
it on whether the chosen seeding still reproduces exactly** — an inbetween of a
re-shaped stroke has to land where the record says. (c) is the one I would open
the argument with, on the grounds that grain belonging to the stroke is what it
belongs to on paper, but the start-of-stroke cascade may sink it.

## Q27 · How big does a reference the model has to read need to be? — **answered (d)**

**Answered 2026-08-07: (d), choose per view from what is in it.** Measure thin-
line density and pick the cap, rather than sending a face turnaround and a
walk-cycle sheet at the same size.

**The objection recorded against (d) still stands and has to be answered in the
build, not argued away:** a heuristic that decides what leaves the machine is
unpredictable in the way invariant 4 distrusts, one level up from pixels. It is
answerable, and cheaply — **make the choice visible and overridable**:

- The request shows what cap each view got, so the artist can see that the face
  sheet went at 1024 and the silhouette at 512.
- A view can be pinned to a size, which is (c) surviving inside (d) as the
  escape hatch rather than as the mechanism.
- The heuristic is a pure function of the view, tested on the same fixtures
  `RenderReferenceViewPng` already has, so it is inspectable rather than felt.

**That guard is inferred rather than given** — say so if the intent was the bare
heuristic. Without it this is a number nobody can predict changing what the
model sees, which is the one failure mode the objection named.

**Blocks:** nothing.


B31 capped reference views at **768 px on the long edge** on the way into a
request, and the number is doing real work: providers bill by area regardless of
file size, so 768 is 442 image tokens against 691 at 960, and 7 KB against
115 KB on a 1080p sheet. The cap is on the request, never on the view — the
artist's sheet stays whatever they drew.

**art-director's objection, with pictures.** Rendered through the real
`RenderReferenceViewPng(view, longEdge)` path and compared at authored, 768 and
512: a body silhouette at `Size 3–4` survives even 512, and this is the case the
cap was measured against. A **face close-up** and a **head drawn at natural
scale on a full-body sheet** do not — eyebrows go, the eyes reduce to grey
smudges, cheek hatching disappears. Mipmapped linear minification greys a thin
dark line toward the ground rather than keeping it crisp-and-small, which reads
to a model as *the line is not there* rather than *the line is thin*. The
failure it predicts is the quiet kind: an inbetween that goes subtly off-model
on the face, with nothing in the request or the response saying why.

**The caveat I owe the measurement, because it is this repo's recurring trap.**
The fixture's thin lines were drawn at pressure 0.25–0.5, and pressure drives
both size *and* flow here, so they are already hairline-faint at authored
resolution — visible, but at the edge of it. The cap made a marginal line
invisible; it did not make a solid line marginal. That is still a real cost and
it is a smaller one than the images alone suggest. *The number was real and the
attribution was partial* — same shape as the saturation trap, and worth stating
before anyone re-derives the conclusion from the pictures.

**ai-engineer's position** is that the token cost of a bigger cap is small in
context: 768→1024 is roughly 442→786 image tokens against the ~26k a stroke
payload already spends, so about 1.3%. It does not want the cap removed —
uncapped means a 4K sheet billed as a 4K sheet — and it will not spend the
budget on a number nobody has A/B'd against a real provider's output.

Four ways out:

**(a) Leave 768.** Cheapest, and defensible for pose, silhouette and
proportion — which is most of what a reference is asked for. Accepts the
facial-detail loss.

**(b) Raise it to 1024.** ~1.3% more tokens, and the two failing cases above
come back inside legibility. Still one number for every kind of view, which is
the thing art-director actually objected to.

**(c) Per-view opt-out, absent until asked for.** A view marked *detail* is sent
at its authored size. Fits the house rule exactly — the record grows a key only
for a view that used it — and puts the trade where the artist can see it, since
they know which sheet is the face turnaround. Costs a setting and a UI for it.

**(d) Choose per view from what is in it.** Measure thin-line density and pick
the cap. Best result on paper, and it makes what leaves the machine depend on a
heuristic nobody can predict — the *shape* invariant 4 is suspicious of, one
level up from pixels.

Not answerable by measurement in either direction: art-director can show that
768 loses facial construction and cannot show that 1024 is enough, and
ai-engineer can price a cap and cannot price a worse inbetween. **(c) is the one
I would open the argument with**, because it is the only option that does not
pretend a face turnaround and a walk-cycle sheet want the same number. (b) is
the cheap interim if a setting is too much for now.

---

## Q28 · What is a reference bound to, when a project is a production? — **answered (b)**

**Answered 2026-08-05: (b), a binding list.** A reference names any number of
targets — the project, one or more folders, specific documents — because
"multiple folder" was in the request and no single-scope field can express it
without duplicating the reference.

The reason it beat tags, which is where the wider vision points: a binding list
**grows into** tagging rather than being replaced by it. A tag becomes a fourth
kind of target, so `binds: tag/prop` arrives later without touching the model or
migrating a file. Choosing tags first would have made every reference's reach
depend on a tag somebody else edited — action at a distance, and the shape
invariant 4 is suspicious of.

What still needs deciding when it is built: what happens to a binding whose
folder is deleted while another binding survives. Not a blocker — a reference
with no remaining bindings is simply project-wide or orphaned, and either is a
one-line rule — but it should be chosen deliberately rather than fallen into.

Raised 2026-08-05, alongside Q29 and the scope note in the ledger's project
entries. **Not answerable from the code**, because the code has only ever had
one answer and it was chosen when a project meant one character.

A `ReferenceSheet` lives in `Doc.ReferenceSheets` — it belongs to **one
document**. That was coherent when Pillar 1 said a project *is* a character:
the turnaround belonged to the animation you were drawing.

It stops being coherent the moment a project is a production. The reporter's
own examples are the argument: a character sheet should reach **every animation
of that character**, a level design should reach **the environment it
describes**, and on a film an art-direction board is wanted **project-wide**.
The type is also wider than "character sheet" — level designs, world designs,
environmental sketches are the same kind of thing pointed at something else.

So the question is not *should a reference be shared* but **what does a
reference name as its scope**, and the options differ in what they cost:

**(a) A scope field on the reference: project, folder, or document.** One
nullable key, absent by default, and it reads like the camera does. Cheapest,
and it cannot express "these three folders" — which the reporter asked for by
name ("or multiple folder").

**(b) A list of bindings.** A reference names any number of folders, documents
or the project. Expresses everything asked for. The cost is that a reference
stops being ownable by anything, so deleting a folder has to answer what
happens to a reference that named it and one other.

**(c) Tags on both sides.** A reference carries tags; a folder or document
carries tags; a reference shows where the tags meet. This is where the owner's
"custom tags and be able to tag" points, and it is the only option where
"every character animation" is expressible without listing them. It is also the
one that can surprise: adding a tag to a folder silently changes what a
document sees, which is the *shape* invariant 4 is suspicious of.

**What makes this urgent rather than interesting:** invariant 1. A document
currently re-renders from its own record, and a reference resolved through a
project, a folder or a tag is a document that does not. The camera precedent
says how that is repaid — `ProjectIo.Flatten` inlines everything referenced when
a document leaves the app — so whichever option wins, **Flatten has to inline
resolved references and there must be a pixel-identity test for it**, or the
escape hatch rots silently. That part is not a preference and does not need a
decision.

---

## Q29 · Is the project docker the whole surface, or the quick view of one? — **answered (a)**

**Answered 2026-08-05: (a), settle the split first and build the hierarchy
shared.** The folder hierarchy is Core model code that both surfaces read, not
a docker view model that a window later borrows.

The division: **the docker does what you do while drawing** — find it, open it,
move it, rename it — and **the window does what you do between drawings**:
bulk operations, tagging, reference binding, status across the production.

This is sequencing rather than taste. Hierarchy is the one piece both surfaces
need, so building it into the docker first is how it ends up with two
implementations — and the second one is always written by somebody who cannot
change the first.

Raised 2026-08-05 by the owner: *"the project docker is part of a larger
project window where we can do advanced operations. The docker is the quick
overview and document/hierarchy helper."*

Recorded rather than answered, because it decides where the open project bugs
land and they should not each guess. B86 (drag/drop, subfolders,
collapse/expand), B87 (permanent delete with confirmation), B64 (rename), B63
(the create menu) are all currently filed against *the docker*. If a project
window exists, some of them belong there instead — a delete that prompts about
a folder full of files is a poor fit for a sidebar, and a bulk retag is not a
docker operation at all.

The split that seems to hold, and is offered as a starting point rather than a
conclusion: **the docker does what you do while drawing** — find it, open it,
move it, rename it — and **the window does what you do between drawings**:
bulk operations, tagging, reference binding, status across the production,
whatever an artist would stop drawing to do.

The reason to settle it before B86 rather than after: hierarchy is the one
piece both surfaces need, and building it into the docker first is how it ends
up with two implementations.

---

## Q30 · When do characters and scenes become folders? — **answered (a), 2026-08-06**

Raised 2026-08-05 when `ProjectFolders` landed and parked at the owner's
request. Asked properly on 2026-08-06 with a recommendation, and the answer went
**against** the recommendation on two of three parts — recorded here as given,
with the one consequence of the combination named rather than argued.

### The answer

**Shape: (a), one hierarchy, now.** Character and Scene stop being separate
kinds of thing. The recommendation was to cascade resources first and merge
later; the owner took the merge directly, which is the stronger reading of
*nothing rigid, everything fluid, but with rules*.

Four consequences the owner specified, and they are the actual design:

| | |
| --- | --- |
| **Character library and asset library** | Become **project-based**, not character-based. Creating into them and saving to them is available **project-wide** rather than being a property of a character or of the Asset Library project type |
| **Pivot, colliders and the like** | **Folder-based**, and *enableable on a file*. Absent unless switched on — the camera's rule again, so a folder that never needs a pivot writes no key and shows no control |
| **Character sheets** | **Folder-based**, so a sheet can serve every drawing under a folder rather than one document. The owner made this conditional on it not colliding with the character library |
| **Resolution** | **Accumulate, nearest wins ties.** Every declaration in the chain registers; where two name the same swatch id the nearest one wins |

**The character-sheet condition is met — checked rather than assumed.** There is
no collision, and the reason is that Q25 already put sheets in the right place
*(as of Q25's re-answer on 2026-08-12 that place changed: a project's sheets are
now their own files filed by folder — which is this row's "folder-based" wish
finally built — and the no-collision reasoning below still holds because there
is still no `Character.Sheets` anywhere)*:
a `ReferenceSheet` lives in `Doc.ReferenceSheets`, so it belongs to a *document*
and there is no `Character.Sheets` for a library import to carry.
`CharacterLibrary.Import` copies `entry.Character.Animations` and their
documents, so a document's own sheets travel with it either way. Adding a folder
scope is purely additive: a folder-scoped sheet stays with the folder it was
declared on, which is correct — it belonged to the source project's structure,
not to the character being imported.

**Migration: new projects only.** Existing `.lbproj` files keep character
palettes and the character/scene layout; the new shape applies to projects made
after the change.

### The one thing the combination costs, stated once

*One hierarchy now* and *new projects only* pull against each other, and it is
worth writing down because neither option said it alone: if existing projects
keep characters, the `Character` and `ProjectScene` records — and every code
path that reads them — **can never be removed**. "One hierarchy" is then true of
new projects and false of the codebase, which keeps two of everything, which is
the cost this question was opened to remove.

Recorded as the owner's decision and not re-litigated. The narrow amendment that
would resolve it later, if it becomes annoying rather than theoretical, is to
read the old shape and write the new one — an old project stays readable and
adopts the new layout the first time it is saved. That is a one-line change of
policy in the loader, not a redesign, so deferring it costs nothing except the
duplicate paths in the meantime.

### What this does not change

`ProjectIo.Flatten` must keep inlining everything a document references
regardless of where it is filed, and `AProjectWrittenBeforeFoldersKeepsItsPaths`
must keep passing. Both are invariant 1 at the boundary where a file leaves the
app, and neither was ever a preference.

---

<details>
<summary>The deliberation this replaced, kept because the options were weighed
and (b) and (c) were rejected for reasons worth not re-discovering.</summary>

**The state of things.** The project now has two hierarchies. The folder tree is
arbitrary — any name, any depth — and `ProjectFolder`/`DocumentRef.FolderId`
describe it. Beside it, `Character` and `ProjectScene` still build paths from
fixed words in `ProjectIo`: `characters/<slug>/animations/<slug>.lightbox.json`
and `scenes/<slug>/shots/<slug>.lightbox.json`. Those constants are the last of
the naming convention.

Two hierarchies is a real cost and it is worth naming rather than tolerating
quietly: every surface has to render both, every operation has to ask which
kind of thing it is holding, and "move this into that" has four cases instead
of one.

**What makes it more than a refactor.** A character is not only a folder. It
carries a palette, a pivot, variants that inherit animations, and a
`character.json` that `CharacterLibrary` reads across projects. A scene carries
running order and a running time. Whatever replaces them has to keep all of
that, and has to open every `.lbproj` already written.

The shapes worth weighing when it is time:

**(a) A character *is* a folder with character data.** `ProjectFolder` grows a
nullable `Character` (and `Scene`), so one tree holds everything and a plain
folder simply has neither. One hierarchy, one set of operations, and the
migration is mechanical: read the old lists, emit folders, keep the ids.
Riskiest at the seam — `CharacterLibrary`, variants and `SymbolScopes` all
resolve characters by identity today.

**(b) A character *has* a folder.** The character record keeps its own life and
gains a `FolderId`; the tree is where it appears and the character is what it
is. Cheaper and reversible, and it leaves two records describing one thing —
which is the state that produced this question.

**(c) Leave them.** Characters and scenes stay a fixed top-level convention and
folders are for everything else. Honest for a game project with a flat asset
pile; wrong for a feature, where "Episode 2 / Act 1 / Sc 014" is the structure
and a character is one leaf of it.

**What does not need deciding either way**, so it should not hold the question
up: `ProjectIo.Flatten` must keep inlining everything a document references
regardless of where it is filed, and `AProjectWrittenBeforeFoldersKeepsItsPaths`
must keep passing. Both are invariant 1 at the boundary where a file leaves the
app, and neither is a preference.

</details>

## Q58 · The timeline family: what are the Xsheet, the track Timeline and the Graph Editor in v1? — **answered, all recommendations taken, 2026-08-08**

Raised when the owner asked for the reference's timeline (its strip reads
*Timeline | Xsheet | Dope Sheet | Graph Editor*) plus "2 more dockers
complimenting each other, xsheet (i presume this is what we have today), and
graph editor", with field research requested (TVPaint, Toon Boom, OpenToonz,
and the general dope-sheet/graph-editor vocabulary). Asked with the question
prompt; all four answers took the recommendation.

### The answers

- **Xsheet = today's horizontal grid, re-hosted.** One row per layer, one cell
  per frame, holds, timing presets — already an exposure sheet laid sideways,
  and the owner presumed as much. A classic vertical sheet is a later
  orientation toggle, not a second implementation. OpenToonz-style cell marks
  and drag-fill cycles join the queue rather than v1.
- **The track Timeline ships editable.** One track per layer, drawings as
  dots, holds as bars, the camera as its own track, per-track colours as the
  reference draws them — and the dots drag to retime from day one, because a
  timeline you can see and not touch reads as broken.
- **Graph editor v1 = camera curves + hold easing + the spacing graph.** The
  conventional half is what the field has (transform curves with handles and
  interpolation presets). The spacing graph is the differentiator no
  competitor has: because the stroke record is the document, Lightbox can
  MEASURE how far the drawings actually move between frames and plot the true
  spacing of the animation — the pencil-era spacing chart, derived from the
  art. The AI inbetweener fills toward it.
- **Adopt next: audio + timing ladders.** An audio track with a waveform and
  scrubbed playback is the single biggest gap against every competitor;
  timing ladders (the chart on an extreme naming where the inbetweens sit)
  are the classic tool nobody ships as a first-class object, and the natural
  input to the inbetweener. Shift-and-trace and cycle drag-fill stay on the
  list, unscheduled.

### What did not need deciding

The dope sheet. The reference names one, but a dope sheet is keyframes by row
with timing and no values — between our Xsheet and the track view there is no
job left for it to do. If one earns its way in later it is a view over the
same records, not a fourth store.

## Q59 · The audio track: which output backend, and where does the sound live? — **answered, both recommendations taken, 2026-08-08**

Raised when the audio track (Q58's first "adopt next") came up in the queue.
Playback needs a native audio output — .NET has none built in and Avalonia
does not either — and a native dependency is exactly the kind of decision that
goes to the owner before code. Asked with the question prompt.

### The answers

- **Output: OpenAL-soft through Silk.NET.OpenAL.** One small, LGPL,
  ships-everywhere native library, bound by a .NET Foundation-maintained
  wrapper. Decoding stays managed — WAV read by our own code, OGG via NVorbis
  and MP3 via NLayer when they arrive — so the native surface is output-only.
  The alternatives were waveform-without-playback (cheapest, but a silent
  audio track misses the point: you animate to the sound, not the picture of
  it) and SDL2 (battle-tested but a windowing/input/audio kitchen sink linked
  for one function).
- **Storage: reference by path, never embed.** The document stores a relative
  path plus offset/volume/mute; waveform peaks cache separately. Documents
  stay small, the source file stays editable in a DAW, and a missing file
  degrades to a silent badge rather than an error. TVPaint and OpenToonz do
  the same. Embedding would make the file self-contained at the cost of
  megabytes per document and autosave churn on a blob that never changes.

### What did not need deciding

Optionality. Whichever way both questions went, the audio block is nullable
and absent-until-used — a document without audio writes no keys, shows no
audio UI, and pays nothing. That is the same rule the camera already proves.

## Q56 · Video in and out: which engine, which formats, and what shape does footage take? — **answered, all recommendations taken, 2026-08-08**

Raised when the owner asked for a render pipeline ("export our animations to
(professional) video files") and, in the same breath, video to draw against
("like gumball" — drawn characters over live footage). Both need a codec
engine .NET does not have, so the dependency went to the owner before code,
the same way Q59's audio backend did. Asked with the question prompt.

### The answers

- **Engine: a bundled FFmpeg binary, driven as a subprocess.** Frames pipe
  in, the file comes out; an encoder crash cannot take the app down, the
  LGPL boundary stays clean (a separate executable, not linked code), and
  the same binary decodes footage for references. The installer pays ~25 MB.
  The alternatives were system-FFmpeg-on-PATH (every artist pays a setup
  step, support inherits every version) and FFmpeg.AutoGen bindings (fastest,
  and a codec bug crashes the application in-process).
- **Export v1: H.264 MP4, ProRes 422 MOV, and a numbered PNG sequence with a
  WAV.** Review, editorial handoff and comp pipelines respectively, one
  dialog. The scratch track muxes into all of them. DNxHR and WebM are
  argument sets away when asked for.
- **Footage: a reference layer, never a drawing layer.** Imported the way
  references work today — under the drawing layers, mapped to the timeline
  (video time follows scene fps, with an offset), referenced by path like
  audio (Q59), and never exported. Extracting frames onto a raster layer was
  rejected: it bloats the document with footage bytes and the frames would
  export unless remembered and excluded.

### Also decided in the same exchange (not video)

The tool rail rearrange: buttons flow into **1–3 columns adaptively by
window height**, horizontally centred — 2 columns the comfortable default,
1 when the window is tall enough for a single column, 3 when it gets short.
Every tool always visible, never scrolled.

## Q57 · Clips get a storage choice and timing handles — **answered 2026-08-08, queued behind the design-round-3 PR**

Raised when the owner queued the follow-up to Q56's video work: a choice
between referencing footage and storing it in the file ("the former requires
the user to have compositing software... I want to enable other users to also
use this rudimentary"), the imported video visible in the timeline with
handlers for timing, and — perhaps — cutting and rearranging sections of both
audio and video. Asked with the question prompt.

### The answers

- **Two purposes, each with its own storage.** The owner's own words: "2
  paths. One reference 2 small production." A **reference** import may embed
  the extracted contact-sheet frames (reference quality, capped — the same
  240-frame/480px extraction, stored the way image references already store),
  or stay by-path as shipped. A **small-production** import embeds the
  **original video bytes** — full fidelity, re-extractable, for the user whose
  whole pipeline is Lightbox. The cost asymmetry is deliberate: a reference is
  a drawing aid and pays reference prices; production footage is material and
  pays material prices.
- **Audio gets the same reference-or-embed choice** (recommendation taken).
  Same rationale: a self-contained file survives being shared without the WAV
  beside it. Reference stays the default; embedding warns past ~10 MB.
- **Timing handles live in the Timeline docker** (recommendation taken).
  Audio and video each get a clip bar in the track timeline: drag the body to
  slide, drag an end to trim in/out. The X-sheet stays a drawing grid.
- **Slide + trim this round; split-and-rearrange next** (recommendation
  taken). The model is a segment list from day one so "split at playhead" and
  reordering land later without a migration.

### Answered in the same exchange

**Small-production footage exports.** Asked whether embedded production
footage stays draw-against-only, the owner answered "yeah also export for
small production" — so the production path composites into the render
pipeline's output, unlike references, which never reach an exported pixel.
That difference is the line between the two paths: a reference is a drawing
aid, production footage is material.

## Q60 · How does a painting stay cheap to reopen? — **answered: an in-document checkpoint, taken on save, off-thread**

**Answered 2026-08-08**, five decisions in two prompted pairs, after B30 was
measured against a painting rather than a frame of line art and turned out to be
27× over budget at the *smallest* sample anyone had tried. Design:
`docs/DESIGN-raster-checkpoint.md`.

The question only became askable once the numbers existed. B30 had sat at P3 for
weeks on the reasoning that *"a miss is currently rare on a scene that fits the
cache"* — true of one frame in a sequence, false for a single painting, where
there is one cel and the document *is* the frame you keep missing on. The
assumption was excluding one of the two first-class purposes in `CLAUDE.md`, and
nothing measured it because the sweep stopped at 800 strokes with a 9 px line-art
brush: the animation half's shape on both axes.

| | Decided | Instead of |
| --- | --- | --- |
| Where the pixels live | **In the document**, a new nullable field beside the strokes | a sidecar cache file — which was the recommendation until the prior art was read |
| When one is taken | **On save, rendered on a background thread** | before the save completes (would stall Ctrl+S for a minute), or on idle (needs an idle notion that does not exist) |
| What invalidates it | **Any edit it covers drops it**; next save makes a fresh one | several checkpoints at different depths — more of the fast path, much subtler invalidation |
| The undo limit | **A memory budget with a step ceiling, not a flat count** | a flat 250 or 500 |
| The clone stall found while measuring | **Filed as B142**, fixed on its own branch | folding it into B30, or fixing it here |

### The owner's constraints, which decided three of the five

> *"The goal is to not let anything block the artist's options in a reasonable
> sense (10000 undo steps is unreasonable) and to not interrupt the artist where
> it shouldn't or isn't expected (saving a document should not stall for too
> long)."*

Saving off-thread follows directly. So does refusing the multi-depth checkpoint:
it buys fast-path coverage with a failure mode that shows **stale art**, and being
slow is a lesser harm than being quietly wrong. And the undo answer was not a
pick at all —

> *"This should be tested what is the limit of fast and between usable. I would
> now say somewhere between 250 and 500."*

— so it was measured. **Depth turned out not to be the cost.** 500 delta steps
push in 1 ms and hold 433 KB; a brush stroke goes through `PerformDelta`, not
`Perform`. 500 *snapshots* would hold 1.4 GB. A step count therefore prices a
cost that is not there while missing the one that is, which is why the answer is
bytes with a step ceiling rather than a number in the middle of the range asked
about.

### Where the recommendation was wrong, and what changed it

The first recommendation was a **sidecar** cache, reasoning by analogy from Q55:
*"with nothing committed there is nothing to drift, nothing to merge and nothing
to verify"*. That argument is sound about a repository and weaker about a
document, and the prior art is close to unanimous the other way — a `.psd` has
carried a pre-composited flattened image beside its layer data since 1990, and a
`.kra` is a zip of per-layer images. Both chose portability over size. A document
that arrives on another machine without its checkpoint opens in 106 seconds, and
"it is slow on my colleague's laptop" is a worse bug than "the file is large".

The general shape, which is the part worth keeping: **every application that
offers geometry-as-truth restricts mark quality on those layers** — Krita's vector
layers do not get its brush engine, Illustrator rasterizes its expensive effects —
**and every application that keeps full mark quality stores the pixels.** Nobody
makes replay fast; they stop replaying. Lightbox is unusual only in not having
stored them yet.

### The one thing that was not a preference

Whichever way the questions went, `Frame.PngBase64` could not be the field. It is
read by `Materialize`, which draws it and *then* replays the strokes (the art would
paint twice); by `CanTileFrame`, which requires `!HasBaseline` (tiling would switch
itself off on exactly the documents needing it); and by `UnseenByTheModel`, which
reads it as *"imported pixels the model cannot see"* (a checkpoint is a rendering
of strokes the model reads fine). Derived state and content-with-provenance are the
same bytes with opposite meanings, and conflating them was a category error the
merge warning written that same morning is what made visible.

## Q62 · Playback is 7 fps at 1080p and 3 fps at 4K — which half gets fixed? — **answered 2026-08-08: compositing, and ahead of the vector work**

Asked after an artist reported playback as unusable at 1080p and 4K, and a new
bench scenario reproduced it. Filed as **B144**, with the measured split added to
**B29** and **B125**.

### What the measurement found, because it decided the question

`AnimationSweeps.PlaybackCanvasSize` — 3 layers, 24 frames, second pass round the
loop, so no first-pass rasterization is included:

| canvas | playback p50 | of which compositing | of which re-rasterizing | fps |
| --- | ---: | ---: | ---: | ---: |
| 720p | 24.3 ms | 13.4 | ~11 | 41 |
| 1080p | 132.6 ms | 55.7 | **~81** | 7 |
| 4K | 303.0 ms | 215.0 | ~100 | 3 |

The compositing column is `AnimationSweeps.CanvasSize`, which holds one frame and
three layers so every access is a cache hit — compositing with no rasterization in
it. Subtracting gives the rest.

**Two causes, and which dominates flips with resolution.** At 1080p the frame cache
is the majority (B144: a fixed 512 MB budget holds 64 of the 72 bitmaps the scene
needs). At 4K compositing is the majority (B29/B125: full-canvas CPU blits, `n^1.03`
in area, already 2.6× over the playback budget on pure cache hits).

### The answer, which went against the recommendation

**Go at compositing**, and **before the vector phases**.

The recommendation was the cache budget first: it is a literal replaced by a
reading of installed memory, it should take 1080p from 133 ms to about 56 ms — just
inside the 83 ms budget — and it could ship in an afternoon. It was not chosen, and
the reasoning against it is sound: **it buys one resolution.** 56 ms of an 83 ms
budget leaves no room for onion skin (`n^0.84`, and already 885% of budget at one
ghost each side), and 4K is untouched at 215 ms. A fix that makes 1080p *just*
work, while the number that actually scales with the document goes unaddressed,
spends the session and moves the ceiling by one step.

**What the choice costs, recorded because it is real.** Compositing is the largest
piece of work in the performance area — tiling un-gated for bounded documents plus
culling, or GPU compositing through a `GRContext`, and B125 exists because the CPU
path is a ceiling rather than a bug. It is multi-session. Until it lands, **1080p
playback stays at 7 fps** even though a one-line budget change would have made it
usable, and that is the trade being accepted knowingly: no half-fix, and the
interim is worse than it needed to be.

The cache half is not cancelled, only reordered — B144 stays open at P1, and it
gets cheaper to justify once compositing is not the dominant term.

### The measurement gap, which is the part to carry forward

`AnimationSweeps.Playback` has existed all along, times the identical operation,
and reported every row inside budget while the application ran at 7 fps on an
ordinary document. It sweeps **frame count at 720p** — the one axis playback is
nearly flat on (`n^0.83`) — and never varied the one it is quadratic in (`n^2.25`).

**A sweep is evidence about the axis it sweeps and about nothing else.** The bug
was reachable from the existing scenario's own numbers by nobody, because the
scenario asked the wrong question competently. When a report disagrees with a
person using the application, the report is measuring something adjacent.

## Q63 · GPU compositing is at its measurement gate — measure, build past it, or switch axes? — **answered 2026-08-10: switch to the layer axis (B165)**

B125 stages 1 through 4 have landed: the lifetime protocol, the pixel-identity
harness, the pass list crossing to the render thread, the culled composite moving
into the draw op, and a GPU surface behind `LIGHTBOX_GPU_COMPOSITE=1`. Stage 4 is
deliberately a **gate** rather than a feature — it uploads every layer every frame,
which is the worst case by construction, and the number that decides whether that
is a 20× win or a 3× one can only come from real hardware. There is no graphics
context in this repository, which is the same reason B122 shipped as an inference
and the render report exists at all.

So the question was genuinely the owner's to answer, and three options were put:
run the measurement first, build stage 5's residency blind, or leave GPU work at
the gate and attack the other axis.

**The recommendation was to measure first.** Stage 5's design is what has to carry
the whole win if the upload dominates, and building it before knowing that means
committing to an invalidation strategy on an assumption.

**The answer was to switch to B165**, and it is a better call than the
recommendation for a reason the recommendation underweighted: B165 is **fully
testable in this repository**, and stage 5 is not. Every line of a resident-texture
cache would land unguarded here, which is the opposite of what the last six pull
requests have been about — and B165 attacks the axis GPU compositing does nothing
whatever for. Ten layers at 4K is 224% of the playback budget *after* a 20× GPU
win. The two axes multiply, so the second one has to be answered regardless of what
the first measures.

**What the choice costs, stated so it is a decision rather than a drift.** Stage 4
remains unmeasured, so `LIGHTBOX_GPU_COMPOSITE` stays an opt-in nobody has taken
and B125's checkbox stays open on a stage that is *built but unproven*. That is a
real hazard: code that exists and has never run on the hardware it was written for
rots quietly, and the longer the gap the more likely the first real run finds
something the CPU fallback was hiding. The mitigation is that the measurement is
one render report whenever the owner wants to spend five minutes on it — it does
not need a session, and it does not block B165.

## Q64 · What is the minimum spec, and should budgets be derived from it? — **answered 2026-08-10: indie 2D game work on 8 GB with integrated graphics; budgets derived, not chosen**

Raised by the owner while reviewing the GPU and cache work, and it is a
correction rather than a question: **every performance decision in this session
had been reasoned from one laptop.** A Ryzen 7 PRO 5850U with 32 GB and shared
graphics memory is not the machine this has to run on — it is one sample, and
the conclusions drawn from it were being treated as facts about the product.

Two specific consequences the owner named, both correct:

- **A path that is bad on that machine is not necessarily bad in general.** The
  5850U shares memory between processor and card, so an upload competes with the
  compositing beside it; a discrete card crosses PCIe once and then blends from
  dedicated memory. Residency (B167 phase 5) helps the second case *more*, so
  killing it on the first machine's number would repeat B125's mis-aim exactly.
- **Budgets tuned to 32 GB are wrong in both directions.** `FrameBitmapCache`
  held 512 MB and `LayerTextureCache` 192 MB, both constants chosen while looking
  at that machine. On a 64 GB workstation they leave performance unclaimed; on a
  minimum-spec laptop they are more than can be spared.

### The minimum spec, from the production flow rather than from a spec sheet

**Indie 2D game work.** The output is often 4K, but the *documents* are sprite
sheets and character cycles, which are typically well under it — so the floor
has to make a sprite document comfortable rather than make a 4K film document
possible. The machine: an ordinary laptop, integrated graphics, **8 GB of RAM**.

That is what every floor in `MemoryBudget` is chosen against, and it is what
makes "works on minimum specs" checkable instead of a hope.

### The rule: derive, clamp, allow an override

A fraction of what the machine actually has, floored so the minimum spec works
and ceilinged so a large machine is not handed more than it can usefully spend.
The artist's setting stays the final word; this fixes the *default*, which is the
thing that was wrong. Frame cache takes an eighth (1 GB on the minimum spec,
4 GB ceiling); layer textures a sixteenth, deliberately meaner because on
integrated graphics they are the same memory the compositor is competing for;
tiles a thirty-second, both because a tiled frame holds only the tiles a stroke
touched and so buys far more frames per byte, and because **the three budgets are
additive in the worst case** — an eighth plus two sixteenths is a full quarter of
an 8 GB laptop, which is a machine that swaps rather than one with a fast cache.
`MemoryBudgetTests.TheFloorsAreAffordableOnTheMinimumSpec` is where that sum is
checked, and it is the test that caught it.

**The artist's floor is allowed below the derived floor, and that is deliberate
rather than an inconsistency the clamps failed to catch.** The derived floor is
what a minimum-spec machine needs for the cache to be worth having at all; the
setting's floor is how far somebody may go when they have decided they would
rather have the memory back — which is exactly what the Configure page offers in
its own words. The *ceiling* is shared, because past it the cache holds bytes it
will never spend no matter who asked for them.

**What it cannot see is VRAM**, and there is no portable way to ask. System
memory is the proxy: exact on integrated graphics, an underestimate on a discrete
card — which errs toward not exhausting it, and a refused allocation falls back
to the processor rather than failing.

### The cost of generalising, so it is a decision rather than a reflex

Every alternative path is one more thing that can rot, and **none of the GPU
paths can be exercised in this repository at all**. The mitigation is to
parameterise one implementation rather than branch into two — which is what the
composite already does, taking a surface whose provenance is the only difference.
Generalising by *guessing* at machines nobody has measured would be the same
error in a new direction; the render report is what turns guesses into data over
time.

## Q67 · Should strong anticipation be licensable when it reads exactly like a copied key? — **answered 2026-08-12: (a) keep the band; anticipation is authored, in a breakdown**

Raised by art-director in the G12 review of the Phase 0 verifier (2026-08-12),
prompted in-conversation, and answered the same day on the `[needs a decision]`
PR (#179): **(a)**, the recommendation.

The betweenness band refuses a matched stroke sitting more than ~40% of its own
travel from where interpolation puts it. That number was calibrated so a copied
key refuses — the failure a small model most often produces — and it does its
job. The cost the review measured: **a strong anticipation pose drawn into an
inbetween is geometrically the same signature** — deviation opposite the
travel, similar magnitude — so anticipation past roughly a third of the travel
is refused, and the verifier cannot tell a directorial choice from the failure
it exists to catch. On an 80px swing, 30px of wind-back passes and 55px is
refused as "did not stay between the keys".

The decision: **the band stays as calibrated, and anticipation is routed
through authorship.** An artist who wants a strong anticipation draws it as a
breakdown, which Phase 1 makes a hard constraint the arc must pass through;
the copied-key refusal — the commonest small-model failure — stays intact.
The accepted cost, recorded so it is not rediscovered as a bug: **the AI
cannot invent strong anticipation mid-run**, only follow one the artist
stated, and a model that tries will see "did not stay between the keys".

The options not taken: (b) widening `TravelSlack` admits anticipation
everywhere and reopens the copied-key hole the band was tuned against;
(c) a shape signal — a copied key matches the key's *shape* near-exactly,
real anticipation redraws it — stays the upgrade to prototype **if (a)
pinches in practice**, and (b) stays rejected even then.

## Q68 · Does the distorted-silhouette smear deserve a licence? — **answered 2026-08-12: (a) no — the 2× band stays, that frame is hand-drawn**

Same review, same day, answered alongside Q67 on PR #179: **(a)**, the
recommendation. `AreaSlack = 2.0` refuses a
closed shape whose area moves past 2× the interpolated expectation in either
direction. Area-conserving squash and stretch passes — a 10:1 streak that keeps
its area is fine — and a collapse to an eighth refuses, both as designed. The
edge the review named: **a smear style that deliberately draws the silhouette
larger than the character** (≈3.5× area, to sell a fast whip in some 2D/cutout
styles) is refused as a volume gain.

The decision: **the 2× band stays the documented line.** Most smears are drawn
as separate streak strokes, which drag and interpretation already license; the
distorted-silhouette variant is rare and stays a frame the artist draws by
hand. The accepted cost: one stylised technique the AI cannot propose.

The options not taken: (b) an asymmetric band (collapse strict at 0.5×, gain
loose to ~4×) lets a model that balloons a shape mid-motion — a real
small-model failure — pass as a "smear" nobody asked for; (c) tying the band
to the latitude dial makes one dial move two unrelated tolerances, so turning
it up for looser new ink would also weaken the collapse check.

## Q69 · Expanding what a character sheet is for: window, canvas, and how live — **answered 2026-08-12**

The owner asked for two things while another thread moves sheet storage: a
sheet view you can look at *beside* the art, and a sheet view you can see
*under* the art. Four decisions, prompted and answered together.

### The answers

1. **Build on `main`, small surface.** The storage-moving branch could not be
   found on the remote, so both features read views through the existing model
   and touch storage not at all — whatever lands under them merges cleanly.
2. **The floating window is a read-only live viewer first**, shaped so an
   editable canvas can replace its content pane later; the editable step is
   roadmap material, not this build. Full editing in a second window means
   input routing and split brush state — a design doc, not a feature branch.
3. **On the canvas, a sheet view is a `ReferenceStrip`, not a layer.** The
   strip already renders over paper and under drawings, carries opacity, scale
   and offset, and holds the promise that a reference never reaches an exported
   pixel. A "temporary layer" would re-answer export, AI payload, undo and the
   layer docker — four hard questions for the same picture. One addition was
   needed: `Pinned`, because a strip is otherwise only visible on frames with
   assigned slots, and a taped-up sheet must show on all of them.
4. **The taped copy is live, not a snapshot** — *against the recommendation*,
   and recorded with its cost. Editing the sheet re-flattens the strip on the
   edit funnel: one PNG encode at the view's authored size per edit, per taped
   view, paid while a sheet is on canvas. The recommendation was a snapshot
   plus a refresh button — cheaper, more predictable, and rejected because a
   reference that shows yesterday's drawing is worse than one that costs a few
   milliseconds on commit. The string compare in
   `RefreshLinkedReferenceStrips` keeps no-op edits from re-registering
   identical bytes, and the refresh is deliberately not an undo step: the
   drawing's own undo re-runs the funnel and the copy follows.

### What was deliberately not decided

Whether the strip's `Pinned` flag should grow into per-strip slot policies
(hold ranges, per-scene pins) — nothing asked for it, and the flag is one
boolean that a richer policy could replace without a migration.

## Q70 · What is the bar above the canvas, now that tool options have a docker? — **answered 2026-08-12**

The docker work (#195) gave every tool's full vocabulary a panel, which left
the old tool-options bar ambiguous: a second copy of the panel, or something
else? Asked with three shapes — mirror of the docker, quick-access strip, or
status-only — recommendation on the strip.

**Answer: the quick-access strip, and the owner sharpened it in three ways
the question did not ask.** Near-verbatim: *"Quick option. But designed to
the workflow. Should still be customisable but at least a first quick option.
For example; the select has the marquee function, put that there for
illustration. Like a smart bar per workspace, with the exception of size and
transparency. Also Transform should have it's own tool option and be removed
from the quickbar and into the tool options docker."*

Read out as rules:

1. **The bar is the Quick options bar** — per-tool quick controls, not the
   full vocabulary. The docker owns depth; the bar owns reach.
2. **Size and opacity are pinned**, outside the overflow, for every painting
   tool — the same argument that pinned the colour swatches (B77): the two
   things a hand reaches for mid-stroke must never fold into a "More" menu.
3. **Customisable per workspace, later.** Drag-and-drop of which options sit
   on the bar, saved with the workspace, is stage 2 on its own branch — with
   size and opacity explicitly non-removable. Stage 1 ships the fixed layout
   so the bar is immediately useful.
4. **Transform leaves the bar.** A transform session's controls live on a
   page in the Tool options docker, and `BeginTransform` opens that docker so
   Ctrl+T never strands the artist without Apply/Cancel in sight.

### What stage 1 deliberately does not build

The per-workspace smartness and the drag-and-drop customisation are one
feature (customisation *is* the per-workspace part — a default layout that
differs by workspace with no way to change it would be a guess about
workflows), so both land together in stage 2, with the registry of offerable
options built then, when something exists to enumerate it.

## Q71 · Remove the infinite canvas? — **answered 2026-08-12: yes, capability only — the engine stays**

**Asked and answered in conversation, owner's call.** The owner cut the
infinite canvas to focus on a different direction: a simplified 3D environment
to draw in — 2D line data placed in a space that can be rotated and zoomed, no
meshes. That feature has its own design work (asked as its own questions, not
settled here).

**The scope question was the real decision**, because by removal day the
"infinite canvas machinery" was load-bearing for bounded documents. Three
options were put up:

- **(a) Capability only** *(recommended, and chosen)*: remove what an artist
  can reach — `FeatureKey.UnboundedCanvas`, its project defaults, the Configure
  toggle, the `FeatureConflict` registry whose only conflict was
  unbounded-vs-sprite-export, the exporter refusal — and keep the tile engine,
  `StrokeIndex` and B82's viewport culling, which now serve playback
  (`tileModeOn = IsPlaying`), stroke picking/selection, and every zoomed-in
  publish respectively.
- **(b) Full rip-out**: also delete the tile engine and the culling. Costs
  playback its compositor (B144/Q62 measured 145 → 14 ms a frame at 1080p),
  costs picking its index, and undoes B82.
- **(c) Hide the toggle, keep everything**: cheapest and dishonest — dead
  capability, dead tests and a maintained design doc for a feature nobody can
  reach.

**Costs of (a), recorded so they are not rediscovered as bugs:** the tiled
compositor is now reachable *only* while playing, so its live-drawing pixel
tests (`UnboundedCanvasPixelTests`) went with the feature — their regressions
that still have a reachable path were re-pinned through playback
(`ASecondPlayingPublishShowsTheSamePictureAsTheFirst`, the flatten-cache and
bake tests, all converted to toggle playback). The renames follow the same
logic: `ComposeRoute.Unbounded` → `Tiled`, `ComposeUnbounded` → `ComposeTiled`,
because a route named after a removed feature reads as dead code when it is
playback's hot path. `docs/DESIGN-infinite-canvas.md` is deleted with this
entry; Q20 and Q21 above are its decision record and are marked superseded.

## Q72 · The 3D drawing space: what carries the art, what the view is, and how much ships first — **answered 2026-08-12**

**Asked with the question prompt, answered by the owner in one pass.** This is
the feature the infinite canvas was removed for (Q71): a simplified 3D
environment to draw in — rotate, zoom — carrying 2D line data, no meshes.
Design in `docs/DESIGN-3d-space.md`; roadmap items under *Camera and scene*.

Four questions, four answers:

1. **What carries the drawings? — (a) planes in space, as recommended.** Each
   drawing is a flat canvas with a 3D placement; strokes stay today's 2D
   records in plane-local coordinates, and the brush engine, replay, undo and
   the inbetweener never learn 3D exists. True 3D stroke points were priced —
   a rewrite of the stroke record, `BrushEngine`, hit-testing and the AI
   payload — and declined.
2. **View vs camera? — (a) orbit is view-only, as recommended.** Navigation
   while working is never serialised and never exported (invariant 5); what
   renders is the authored camera, extended to a 3D pose in stage 2. A
   document with no camera shows planes head-on and behaves exactly like
   today.
3. **How much first? — (b) multiplane first, against the recommendation.**
   The recommendation was free planes + orbit in the first version, because
   "rotate around the scene" was the stated wish and multiplane cannot do it.
   The owner chose the smaller ship: stage 1 is depth-stacked parallel planes
   with parallax under the existing 2D camera; free orientation and orbit are
   stage 2. **What that choice costs:** no orbiting until stage 2 — stage 1
   delivers depth and parallax, not the rotatable space. What it buys: a
   ship that touches only per-layer matrices, and a record (`depth`) designed
   as the degenerate case of stage 2's pose so nothing is thrown away.
4. **Deliverable now? — (a) design doc + roadmap, as recommended.**
   Implementation starts as its own branches per the one-objective rule.

## Q73 · Docking: the slot cap, the colour family, and where a reopened panel lands — **answered 2026-08-12**

The owner asked for three rules — at most four stacked dockers per side,
default stack groups ("for example Color, Palette and channel — latter not
implemented, go ahead though"), and closed tabs reopening into their tab
group unless the session or the saved workspace placed them elsewhere — and
asked to be prompted for edge cases. Four were prompted, each with a
recommendation, and all four recommendations were taken:

- **A fifth slot never opens.** A drop (or programmatic show) that would
  exceed the cap tabs into the nearest slot instead — nothing is refused,
  and the panel lands where the artist can see it. `DockLayout.MaxSlotsPerSide`.
- **Channels ships minimal but real**: red, green, blue and alpha of the
  composited frame as grayscale thumbnails, click to solo one on the canvas,
  click again for all. The alternative — registering an empty panel marked
  *Planned* — puts dead weight in the default group, and the manual rule
  about documenting what nobody can use applies to panels too. The solo is
  view-only (invariant 5): an `SKColorFilter` on the artwork draw, the
  record untouched.
- **All four colour panels in one group** — Color | Palette | Gradient |
  Channels — rather than the literal "Color, Palette and channel" with
  Gradient evicted. Four tabs still cost one strip, and Gradient keeps the
  home it had just been given.
- **An orphan reopens alone.** A panel whose whole family is closed opens in
  its own slot; the family finds it as members reopen (each closed member
  remembers its slot-mates in `DockPlacement.LastGroupedWith`). The
  alternative — reopening the whole family group — opens three panels
  nobody asked for.

One rule sharpened during the work: the family default applies only to a
panel that has **never been placed** (`HomeSide == Hidden`). A panel the
artist parked somewhere on purpose — solo included — goes back exactly
there, which is what keeps "unless in current session grouped with other
dockers, or if workspace is saved like that" true rather than approximate.

## Q74 · The quick bar belongs to the workspace, not the tool — **answered 2026-08-13**

Q70 stage 1 shipped the bar's frame — the tool icon, the pinned Size/Opacity
pair, transform out to the docker — but left the contents untouched: the full
per-tool vocabulary, folding into ▾ only when width forces it. On a wide
monitor nothing folds, so the "Quick options bar" read as the old tool-options
bar wearing a new name, and the owner reported exactly that. Asked whether to
curate the per-tool sets now, propose them first, or leave it to stage 2.

**Answer: none of the three — the owner reframed the axis.** Near-verbatim:
*"The quick bar should be determined by workspace options not necessarily tool
options. So the options in the quick bar can be customized by except for size
and opacity are fixed per workspace. For example in animation it could get the
play/pause button or add keyframe button. For illustration it could set the
marquee option etc."* Reading Q70's original answer back, "like a smart bar
per workspace" was already there — it had been read out as a per-tool rule,
and this is the correction.

What landed, same day:

- **`QuickBarCatalog`** — the registry of everything the bar can offer, the
  same reason `ShortcutMap` exists one level up: the customize flyout needs
  something to enumerate. Ten entries: the eight tool groups the bar already
  had, plus **Play/pause** and **Add frame** mirroring the timeline's own
  buttons.
- **`DockLayout.QuickBar`** — the workspace's choice, nullable and absent
  from `workspaces.json` until a choice is made; null resolves to the bar as
  it always was, so a store written before the property existed changes
  nothing. Living on the layout buys the whole existing machinery for free:
  dirty until saved, undone by reset, switching with the workspace.
- **Built-in defaults chosen by the work**: Animation, Game art and
  Storyboard carry the transport and Add frame; Illustration and Comic carry
  the paint kit with the marquee; Asset library is minimal; Default keeps
  the resolve-to-everything null.
- **Tool gating stays**: a workspace decides what the bar *offers*; the tool
  in hand still decides which of those offers is relevant right now, so
  carrying "Fill options" shows them with the fill held rather than as a
  dead strip all day. The two gates AND together in the XAML.
- **The ⋮ flyout beside the workspace picker** is the customization — 
  checkboxes over the catalogue, saved with the workspace. Size and opacity
  are not in the catalogue at all, which is what "fixed" means mechanically
  (`SizeAndOpacityAreNotOnOffer` keeps it true).

Q70's stage 2 (drag-and-drop rearrangement) remains open and unchanged; this
delivers the "which options" half of customization without it.

## Q75 · Version control for project files: scope, storage, capture, surface — **answered 2026-08-13**

The `VersionEntry`/`VersionHistoryManager` framework had sat in Core since
M-series work with tests and no store, no content and no UI — recorded as
FEAT-002 "framework-only" in `docs/development/PROJECT-STATUS.md`. Building it
out needed four decisions, prompted and answered in one exchange:

- **What gets versioned first?** — *documents and character sheets.* Both are
  single files with stable manifest ids, so one file-copy mechanism serves
  both; brushes and palettes wait until wanting history is demonstrated rather
  than assumed.
- **Where does history live?** — *in the project folder*, `versions/<resourceId>/`,
  keyed by id so B188-style re-filing moves nothing. History travels with the
  project over git or a drive, which is how projects are already shared (Q43's
  boundary: no accounts, no sync). Registered in `SystemFolders` so B83 does
  not report it.
- **When is a version captured?** — *authored plus milestones.* "Save
  version…" with a label and notes, and an automatic milestone-tagged version
  when a document is promoted to Review or Ready — the moment a studio wants
  the frozen copy, and the reading `VersionEntry.MilestoneStatus` was built
  for. Not every save (unbounded, meaningless labels, duplicates autosave);
  rolling last-N deferred as an addition that needs a retention preference.
- **Where is the UI?** — *File menu plus one shared history window*, with the
  same window reachable from the project docker's row menu. Menu rather than
  project-window-only because solo painters never open the project window.

Costs accepted with the answers: a promotion versions the file **as saved on
disk** (status is set between sessions; reaching into open editors from the
project window would couple them), and reverting an open character sheet
closes its view tabs rather than rebinding them (B98's registration dance,
backwards, is where that path leads). `CreateBranch` stays framework-only —
history is one line per file until that proves insufficient.


## Q76 · Decomposing the two big view files: which tool, which file first, and whether to cap growth — **answered 2026-08-13**

**Asked with the question prompt after a review of `MainViewModel.cs`,
`MainWindow.axaml.cs` and `docs/DESIGN-mainviewmodel-decomposition.md`; all three
answers took the recommendation.** The review's own finding is why it was asked
at all: the design document was written against a 10,098-line file, merged when
the file was 12,001, and its every line anchor was off by 1,200–2,000 lines.

What survived the review, and is worth recording because it was measured twice:
re-deriving the document's coupling analysis against the now 13,110-line file
reproduces it almost exactly — **53% of fields touched by exactly one section**
(it measured 54%), **nine fields crossing five or more** (it measured nine), the
shape tool still widest at **33** (it measured 32). The file is not getting more
tangled as it grows; it is getting longer at constant shallowness. The
section → hub diagnosis stands.

Three questions, three answers:

1. **Which tool? — (a) split by mechanism, as recommended.** The document
   rejected more partial files outright, on the grounds that they buy
   navigability with zero decoupling because every section keeps its licence to
   touch every field. True in the language, false here: the nine partials that
   exist use 0–13 distinct fields each and declare most of them locally —
   `StrokeSelection` touches none, `Momentary` declares 4 of 4, `Audio` 10 of 13.
   3,527 lines left the file that way and stayed loose, because **giving a
   section its own file creates the pressure to declare its state there.** So
   partials for a section that owns its state and touches ≤5 hub fields;
   extracted collaborators, in the manner of `SelectionManager`, for the hub and
   the genuinely shared clusters. **What that choice costs:** two routes to
   explain and a judgement per section about which applies — mitigated by
   `scripts/monolith.py`, which answers it from the field counts. The document's
   real point is kept: a partial for a *hub* would look solved and decouple
   nothing.
2. **Which file first? — (a) split the view first, as recommended.** The same
   analysis run on `MainWindow.axaml.cs` comes back inverted: **79%** of fields
   single-section, and exactly **one** field crossing five or more — `_vm`, used
   in 35 of 37 sections. There is no hub to name and no shared mutable state; it
   is 37 near-independent handler groups over one view-model reference. So the
   view needs *splitting*, not decomposing, and it is the cheap safe proof of the
   pattern before the expensive file. Two things the review turned up alongside:
   the render and publish core is not where the markers say it is — it sits from
   roughly `:11857` under a marker reading *video clip bars (Q57)* — and
   `MainWindow.axaml`, 4,188 lines of XAML with **no test file**, is above both
   C# files on `HOTSPOTS.md`'s risk table.
3. **Cap the growth? — (a) yes, a size ratchet, as recommended.** Since the
   document was merged: **94 commits to `MainViewModel.cs`, +2,793/−965 lines,
   zero leaves extracted.** The file gained more lines while the plan sat
   unstarted than the plan proposed to remove, and an extraction costing a branch
   plus a full suite run per leaf cannot outrun that. `MonolithRatchetTests` now
   holds a line budget for the four oversized files, seeded at current length:
   they may shrink and may not grow, a budget comes down with the extraction that
   earns it, and a second test caps the slack so a stale budget cannot become room
   to regrow. **What that choice costs:** an occasional forced decision mid-feature
   about where new code goes. There is deliberately no environment-variable escape
   hatch — raising the number in a diff is the visible form of the same decision,
   which is the reasoning behind `LIGHTBOX_PUSH_TO_MAIN` applied to a line count.

## Q77 · Naming Tier 0: which cluster, in how many steps, who owns the state, and where B73 lives — **answered 2026-08-13**

**Asked with the question prompt before any code was written; all four answers
took the recommendation.** Step 3 of
`docs/DESIGN-mainviewmodel-decomposition.md`. Two findings reframed the questions
before they were put, and both are why the answers came out as they did:

- **The two Tier 0 clusters are in opposite states.** The render core's *state* is
  already owned — `_composeRing`, `_cache`, `_tileFlats`, `_stackBake`, `_prewarm`
  and `_tileFallbacks` are all collaborators declared at the top of the class. What
  is missing there is sequencing, so it wants an orchestrator rather than a new
  owner of state. The live-paint machine is the opposite: 24 raw SkiaSharp fields
  with no owner at all.
- **A second marker was lying, worse than the one found in the review.** The
  section headed *the shape tool* ran 804 lines, of which only ~180 were the shape
  tool. The rest — `MoveStroke`, `FlushLivePreview`, `StampLiveDabs`,
  `StampLiveSmudge`, `EndStroke`, and `RequestSnapshot` — was the live-paint
  engine, 800 lines away from the state it mutates. That is the entire reason the
  shape tool measured 30 foreign field touches and read as a tool tangled into the
  paint path, when in truth it *was* the paint path with a tool on top.

Four questions, four answers:

1. **Which cluster? — (a) live-paint, as recommended.** It is the one with
   genuinely unowned state, and it is the knot the shape and gradient tools are
   caught in. The render core is smaller work than the design document assumed for
   the reason above, so it can wait; both in one branch was declined as the
   one-objective rule broken on the riskiest change in the plan.
2. **One step or two? — (a) re-mark first, extract second, as recommended.** This
   branch is pure code motion: the engine moved next to its state, the live-post
   methods and the gradient methods went back under their own headings, and the
   render core got a marker. **No line of code changed** — verified the way the
   view split was, by showing the file identical as a multiset of lines. The
   extraction is its own branch. The alternative put a 580-line move and a
   state-ownership change in one diff on the hottest path in the application,
   where nobody could tell which lines changed behaviour.
3. **What owns the state? — (a) a `LivePaintSession` collaborator, as
   recommended**, in the manner of `SelectionManager`: one long-lived object, no
   per-event allocation, so the paint path pays nothing for it. **What that choice
   costs:** its public surface has to be wide enough for the shape and gradient
   tools, which is the thing to watch when the extraction lands. Keeping the fields
   and extracting only methods was declined as navigability without decoupling —
   the exact thing the document was right to refuse about partials-for-hubs.
4. **Where does `RequestSnapshot` live? — (a) stays in the view model, as
   recommended.** It schedules a publish, so it belongs beside `PublishSnapshot`,
   and it moved there in this branch rather than travelling with the paint path
   that calls it. Its `DispatcherPriority.Input` is B73 and does not fail loudly,
   so the live-paint extraction now does not touch it at all.

**What the re-mark bought, measured:** the shape tool went from 804 lines and 30
foreign field touches to 184 and 5 — from the widest section in the file to an
ordinary Tier 1 leaf. `painting` went from 195 lines to 605 and now holds the
engine beside the 19 fields it mutates. The render core went from anonymous to 785
named lines. Nothing was extracted and nothing executes differently.

**Answer 3 has since been carried out, and the numbers are worth keeping here.**
`ViewModels/LivePaintSession.cs` took 22 fields and four lifecycle methods:
`MainViewModel.cs` 13,141 → 12,919, private fields 143 → 122, fields touched by
exactly one section 53% → **63%**, and *live post-processing* went from reaching 19
foreign fields to 6. The full suite, the performance-tagged budgets and
`StrokeLatencyTests` are all green, and no per-event allocation was added — the
session is one long-lived object and the properties are auto-properties the JIT
inlines.

**What it did not buy, stated plainly:** `_live` now crosses seven sections, so the
coupling did not disappear — it became one typed reference in place of 22 raw
fields. The session is not an encapsulation boundary either; the engine mutates its
properties directly. What is genuinely better is that `ClearEffectState` cannot be
got half-right any more, which is B39's exact failure mode.

**Two mistakes were made writing it, both silent, and both are now pinned by
`LivePaintSessionTests`.** The first draft of `ResetPostProcess` disposed the pooled
`PostScratch` instead of wiping the region the last stroke used — a 33 MB
allocation on every pen-down at 4K, with the whole suite green. The second replaced
Skia's mutating `SKRectI.Union` with hand-rolled min/max that skipped empty rects;
measured, `(0,0,0,0) ∪ (5,5,9,9)` is `(0,0,9,9)` in Skia and `(5,5,9,9)` under the
rewrite, because Skia's union is a plain min/max over corners and a default
`SKRectI` is empty *at the origin*. Both were caught by reading the originals rather
than by any test, which is the argument for the tests that now exist: this class's
job is to make expensive things cheap by keeping them alive, and a correctness test
cannot see the difference between keeping a buffer and reallocating it.

**One thing the naming step cost, recorded because the mechanism is one commit old:**
the ratchet budget for `MainViewModel.cs` went *up*, 13,110 → 13,141. The motion
shrank the file by five lines; the 38 lines of comment explaining why the old map
was wrong took it over. It was raised rather than absorbed by trimming that
comment, on the grounds that the budget exists to stop feature code accumulating
in a file nobody can read, not to price the documentation that makes it readable.
That is the only legitimate reason to raise one — the file got more legible and
slightly longer. "A feature needed the room" is not on the list.

It came back down to 12,919 in the branch after, when `LivePaintSession` landed. A
budget that rises once for documentation and falls by 222 for an extraction is doing
its job; one that only ever rises is a comment.

**Answer 1's second half — the render core — has since been carried out, and it
contradicted the plan recorded above.** Q77 said that cluster wanted "an
orchestrator holding those six collaborators, not a new owner of state". Reading
`PublishSnapshot` end to end says otherwise on two counts, and both are worth
keeping because the mistake is a reusable one:

- **The state was not all owned.** The six collaborators own the *caches*. The
  *bookkeeping* — `_pendingDirty`, `_dirtyIsWholeCanvas`, `_pendingViewport`,
  `_publishSeq`, `_lastPublished`, `LastPublishClip`, `FramesReused` — was seven raw
  fields belonging to nothing. So "its state is already owned" was a claim read off a
  collaborator list rather than checked against the code.
- **An orchestrator is the wrong shape.** `PublishSnapshot` reads about fifteen
  pieces of view-model state, so an orchestrator must be handed them per call or hold
  a reference back. The second is a second view model with circular coupling; the
  first allocates a request per publish, and the code next door already refuses that
  trade — the transform-split delegate is cached in a field rather than written as a
  lambda, because "a lambda capturing `this` allocates a closure and a delegate on
  every publish, and a publish happens per pointer event while drawing".

So `ViewModels/PublishState.cs` took the bookkeeping and the sequencing stayed in
`PublishSnapshot`, reading the view model directly and allocating nothing.
`MainViewModel.cs` 12,919 → 12,878, private fields 122 → 118.

**`TakeDirty` is why this is a class rather than seven fields moved sideways.**
Reading the dirty region and clearing it is three statements that must happen
together, and both ways of splitting them are silent: clear without reading and the
next publish repaints nothing that changed; read without clearing and the dirty rect
grows forever, so painting stops being bounded work. Invariant 6 rests on that one
method. `PublishStateTests` sabotages it both ways, and also pins the one-line
difference between `InvalidateWholeCanvas` and `RepaintEverythingThisPublish` — the
fold transition needs the flag without losing the fingerprint, which is "equivalent
today" only because no early return sits between the two points in `PublishSnapshot`.

## Q78 · The leaf plan tops out near 9,700 lines — extract, partial-split, or accept? — **answered 2026-08-13: finish the Tier 1 leaves, against the recommendation**

**Asked after five steps of decomposition moved `MainViewModel.cs` from 13,110 to
12,878 — 1.8% — and the owner asked whether the file staying humongous is a
problem.** It is, and the measurement is what makes it a real question rather than
a mood:

- The file is **7,492 code lines**, 4,140 comment, 1,247 blank. At 32% comment it is
  *below* the repository's 40% average, so "it is heavily documented" is not
  available as an explanation.
- **Every Tier 1 leaf extracted would leave 9,676 lines.** All ten, each its own
  branch with tests and a `leak-hunter` pass.
- The reason is structural: it is not one monolith but **61 sections sharing a
  scope**. The largest is 764 lines and there is a tail of 43 sections totalling
  4,743. Leaves come out at 150–590 lines each, which cannot outrun the total.

**The recommendation was to partial-split the view model now**, applying the half of
Q76 that has only ever been used on the view: measured, **52 of the 61 sections
(9,089 lines) move with no grouping at all**, 36 fields stay in the root, and 9
sections need a sibling. That is the same shape as `MainWindow.axaml.cs`, which went
5,544 → 429 in one branch with the class body proven byte-identical. Tier 0 is what
made it cheap — the 22 `_live*` fields and 7 publish fields are now behind two root
references instead of spread across those sections.

**The owner chose to finish the Tier 1 leaves first instead.** What that buys:
genuine decoupling rather than file boundaries, each leaf landing as a real
collaborator with its own guard, in the manner of `SelectionManager`,
`LivePaintSession` and `PublishState`. Splitting a section into a partial moves it
without giving it an owner; extracting it gives it one, and the three Tier 0
extractions are the evidence that the owner is where the value is.

**What that choice costs**, recorded because it should not have to be rediscovered:

- **Ten more branches, and the file is still ~9,700 lines at the end of them.** The
  answer to "is it small enough now" will be no.
- **Every one of those ten is authored inside a 12,000-line file**, which is the
  condition the split would have removed first. The partial split would have made
  each subsequent leaf a change to an 800-line file instead.
- **The order is not reversible for free.** Extracting a leaf and then partialling
  what remains is fine; partialling first would have made each extraction smaller to
  review. Doing it second means the ten reviews are the expensive kind.

The partial split is not refused, only deferred — it remains the move that answers
the size question, and this entry is what stops it being re-litigated from scratch.

**Q78's leaf pass finished at two AI-path extractions, reviewed by the G12 pair.**
`ConfiguredArtist` took the four provider fields (`_artist`, the two labels, the
enabled flag) and the one operation that sets them together; `ReferenceViewImages`
took the reference-view PNG cache, the render, the downscale and the 768 px request
cap. `MainViewModel.cs` 12,852 → 12,736.

**`ai-engineer`: CLEAN.** Verified member-by-member against `HEAD` — same disposal
order, cap applied at exactly one site, invalidation still inside `MarkDocumentEdited`
rather than `OnDocumentChanged`, no seed/clock/ordering introduced, and the in-flight
`CancellationTokenSource` correctly left on the view model so a provider swap
mid-request cannot inherit the previous request's cancellation.

**`art-director`: ACCEPTABLE, with one finding that was right and is now fixed.** The
extraction copied the sentence "Line art survives the downscale" into the new class's
header — a claim `docs/DESIGN-ai-payload.md` already contradicts: face close-ups
rendered through this exact path at 768 lose eyebrows and turn eyes to grey smudges,
because mipmapped minification greys a thin dark line toward the ground. **Q27 is
answered (d) — choose the cap per view — and this refactor had quietly given a flat
768 a more authoritative-looking home than it had before.** The remarks now carry the
failure mode, name Q27 as the settled answer, and record its three conditions (the
cap is shown per view, a view can be pinned, the heuristic is a pure function of the
view). Q27's heuristic is still unbuilt; this is the placeholder saying so.

**The lesson worth keeping:** a pure code move can still make a claim worse, because
moving prose into a smaller, better-named file makes it read as more settled than it
was. Neither the compiler nor the suite can see that, and it is what the pair is for.

**One pre-existing issue surfaced and deliberately not fixed here.** `ai-engineer`
noted that `_ai.Artist` is dereferenced inside the request lambdas without
re-narrowing after the `is null` guard, so a `ReloadAiProvider()` landing between the
guard and the lambda's invocation would throw. Identical in shape to the code before
this refactor, so not introduced. **Not fixed because it needs a decision, not a
patch:** capturing the artist at the guard makes a request that started before a
provider swap finish on the old provider, while dereferencing late makes it finish on
the new one, and which is correct is a question about what a provider swap means
mid-request rather than about null-safety. That is the "needs a decision" row of the
fix-rather-than-file rule, and it belongs in its own branch with its own question.

**The deferred half of Q78 was then done, and it is what answered the size question.**
`MainViewModel.cs` 12,749 → **655** lines across 19 partials, in two separately
verified steps.

**Step A hoisted 33 shared fields to the root**, giving the split its one rule: a
section's own state travels with it, shared state does not move. **Step B split 61
sections into 19 files** — with the shared state hoisted, union-find over what remained
returned 61 *independent* groups, so the grouping was chosen by concern rather than
forced by coupling.

**The threshold was the whole difficulty.** At "a field crossing three or more sections
stays in root", union-find chained 16 sections into one 4,500-line group, because a
field shared by exactly two sections links them and the links form chains. Lowering it
to "more than one" moved 37 → 54 fields into the root and broke every chain. **That is
the trade the split makes visible rather than removes:** 54 of 114 fields are read from
two or more places. They are now in one marked block instead of scattered through
12,000 lines, which is the honest measure of how coupled this class still is.

Verified as the view split was: coverage with no gaps or overlaps, every marker at
brace depth 1 so no member was cut in half, and the class body **identical as a
multiset of lines** against HEAD — 11,454 non-blank before and after, the only
additions being ten comment lines.

**The nineteen partials are deliberately not given ratchet budgets, and the objection
to that is recorded in the test.** Growth will now land in whichever partial owns the
feature, so the mechanism that capped it has nothing to cap. Kept anyway because that
destination is the split working rather than leaking, and because the largest partial
is 1,310 lines — a file a person can read. Pre-emptively budgeting nineteen readable
files looks like discipline and is noise. Add one when a file stops being readable,
with the number that made it necessary.

**What the whole exercise cost and bought**, since the leaf-versus-split ordering was
argued twice: the leaf pass produced three collaborators (`GuideSnap`,
`ConfiguredArtist`, `ReferenceViewImages`) and moved the file 12,878 → 12,852 → 12,736
— about 0.9%. The split moved it 12,749 → 655 in one branch. Both were worth doing and
the order was wrong: had the split come first, each of the three leaf extractions would
have been a change to an 800-line file instead of a 12,000-line one. That cost was
stated when the ordering was chosen and is recorded here as having been real.

**All five collaborators were re-applied on top of main (52 commits, PR222 included),
and two of them are better for it.** PR222 rewrote the live-post pipeline and added
publish pacing — the code Tier 0 had extracted — so `RenderLivePostProcess` no longer
exists upstream. Rather than route 41 hunks (two of them rewrites, +195/−39 and +112)
into nineteen partials, the merge took main's files verbatim and the restructure was
re-derived on top: PR222's behaviour is intact by construction, which is the only claim
that is cheap to check.

`PublishState` absorbed `_presentedSeq`, `_publishWhenPresented`, `_lastPublishTicks` and
`_damTimerArmed`. They belong with `_publishSeq` rather than beside it — `CanvasIsBehind`
compares three at once, and a deferral released twice puts a second frame in flight,
which is what the pacing exists to prevent. `NotePresented` and `TakeDeferral` clear the
flag inside the state so "released" and "flag down" cannot come apart.

`LivePaintSession` absorbed `_livePostGeneration`, and the bump moved inside
`ResetPostProcess` where PR222 had it. The only thing that invalidates in-flight work is
this state being reset, so the two must not be separable.

**The split went first this time**, which is the Q78 lesson applied: each extraction was
a change to an 800-to-1,800-line file rather than a 13,000-line one, and the difference
was obvious in how quickly each one landed.

Final: `MainViewModel.cs` 13,628 → 692 across 18 partials; `MainWindow.axaml.cs`
5,706 → 455 across 15. 4,191 tests green, PR222's own guards included.

## Q79 · Construction guides: which of the eight wishes is buildable now, and as what — **answered 2026-08-13**

The roadmap's *Construction guides* section held eight `[?]` wishes that were
really two features wearing one name. Four questions, prompted and answered in
one exchange, once the guide-set work gave them their prerequisite:

- **Scope** — *authored drawing aids first* (recommended, accepted). Marks an
  artist places ride the existing guide machinery and are deterministic and
  cheap; the computed analysis overlays (volume, center of mass, perspective
  consistency) each need machinery that reads the drawing, and they wait until
  somebody misses one specifically.
- **The character height guide** — *a dedicated kind* (owner's choice,
  explicitly past the label-only cheaper option). `GuideKind.HeightScale`: one
  object that is "6 heads", anchored at the ground, top-dragged to resize with
  the divisions following, division lines that snap across the canvas. The
  isometric guide's argument, re-applied: labelled lines would be the same
  picture and seven times the housekeeping.
- **Eye-line and horizon** — *fold into `Line` plus label rendering*
  (recommended, accepted). They are horizontal lines; the rendered name was
  the whole missing part, and the height scale needed label painting anyway.
  The costlier alternative — a `Horizon` kind that vanishing points snap onto —
  is recorded in the roadmap entry as deferred, not refused.
- **The analysis trio (plus limb length)** — *stays `[?]`, with the reason on
  each line* (answered "?", read as the recommendation: leave them open).
  Each roadmap line now says what it actually requires — region definitions on
  freehand ink, B58's rigs to name what is measured — so the wish is honest
  about being a research item rather than a backlog item.

Costs accepted: a height scale never constrains a stroke's direction (only
point-snapping), or every horizontal stroke would belong to it; and guides
still cannot be renamed in the UI, so the named lines are named at creation —
a rename surface is a separate, unfiled wish until somebody wants it.

## Q80 · Animation-aware brushes: is authored line boil in scope — **answered 2026-08-13**

Scoping the Pillar 4 `[?]` item *Animation-aware brushes* (defined in the
roadmap entry: grain anchoring, inbetweenable dynamics, frame-context
response, sequence-scale cost review) surfaced one genuine decision: whether
**deliberate, deterministic line boil** — per-frame variation so a hold can
"breathe" on 2s, the TVPaint aesthetic — belongs in scope at all, given that
geometry-seeded determinism makes holds dead-still by construction.

**In scope, opt-in** (recommended, accepted). A per-stroke, off-by-default
effect with an authored per-frame phase stored in the record: deterministic,
so invariant 2 holds and re-renders are identical; absent from the file
unless used, so the optional-means-absent rule holds too. Costs accepted:
the stroke record grows a per-frame dimension, and this is the first effect
whose seed varies by frame — a real extension of the `Hash01` seeding story
that needs its own re-render and hold-stability tests when it is built.

The alternatives both had a named price. *Out of scope* keeps the aesthetic
impossible, and artists fake it with redrawn holds the exposure sheet can no
longer represent as holds. *Post effect over finished frames* is cheaper but
cannot respect per-stroke intent (ink boils, fill does not) and breaks the
rule that pixels derive from the stroke record alone.

## Q81 · Bones phase 2 UI: six approach decisions — **answered 2026-08-14, all six as recommended**

Prompted before building the UI half of phase 2 (`docs/DESIGN-bones.md`),
with the core — weights record, LBS, rest-pose seeding, bake — already
landed and green.

1. **Rigged-ness is record-driven.** A stroke with `Weights` poses; there is
   no layer flag. Cost accepted: render caches must derive pose-dependence
   from content, and invalidation on pose edits is built for that, not read
   from a bit that could go stale.
2. **Auto-key at the playhead.** Dragging a bone at frame N writes or
   updates the pose key at N — posing is the high-frequency act and gets the
   one-step gesture. The camera keeps its explicit keys; the two differ on
   purpose. Accident risk is handled by armature onion-skin and key pips.
3. **One Bone tool, then a rig mode.** The Bone tool lives in the palette
   always (every feature reachable — first drag creates the armature); bone
   list, heat overlay, weight brush and pose/bind toggle appear only once an
   armature exists (unused shows no controls).
4. **Weight painting at rest first.** Heat overlay and brush operate on the
   bind pose; scrub to check, return to correct. Painting under a live pose
   is sequenced later, with the caching that makes per-brush-event re-posing
   affordable — a sequencing call, not a scope cut.
5. **Pose-drag preview: exact, degrading only over budget.** Affected
   strokes re-render exactly per pointer event, region-bounded; strokes over
   the frame budget (already badged by `BrushCostOf`) ghost as their posed
   centreline during the drag and land exactly on release. Invariant 6 is
   the tiebreaker.
6. **X-symmetry mirrors by paired bone names** (`hip.l` ↔ `hip.r`, axis from
   the paired rest placements), Blender's convention — a fixed canvas axis
   breaks on any character not drawn dead-centre. The bone tool's
   auto-naming supplies the discipline.

---

## Q82 · Docker-dependent shortcuts: what falls back over a docker, and how the editor shows it — **answered 2026-08-14, both against the recommendation**

Prompted while closing the gaps in hover-scoped shortcuts. The model
(`ShortcutScope`, panel-scoped bindings, `I` = insert-key over the timeline)
was already built and tested; what was missing was everything between the
pointer and the resolver. Two forks came out of that, one behavioural and one
about the screen an artist reads the rule from.

1. **The fallback chain over a docker is panel → canvas → general.**
   *Recommended: keep panel → general.* The chain used to stop at the docker's
   edge, so `Delete` over the Colour docker did nothing at all. The argument
   for stopping there was that a destructive key firing on the canvas from a
   place that gives no hint the canvas is the target is a bad surprise with
   nothing on screen explaining it. The argument that won: an artist's model is
   *the docker overrides, everything else still works*, and a key that goes
   dead over eleven of twelve dockers contradicts it — "a panel overrides, it
   never blocks" is now the sentence in the manual.

   **What it costs, accepted knowingly.** `Delete` and `Backspace` over any
   docker that does not claim them now reach the canvas and change the
   *drawing* — the selection's contents cleared, or flooded with the
   background — while the pointer is over a swatch or a layer list. Arrow keys
   likewise nudge from any unclaimed docker. The mitigation is that the dockers
   where this would be most surprising already claim those keys (Layers owns
   Delete, Backspace and the up/down arrows; the timeline owns left/right), so
   the exposure is the dockers with no key bindings at all. `ShortcutFallbackChainTests`
   pins all three rungs and the order between them.

   **One thing this does not do:** the general scope (`ShortcutScope.General`,
   "nowhere in particular") does *not* pick up the canvas rung. It is
   deliberately the narrowest scope there is, and a marquee-clearing `Delete`
   reachable from it would be the bad surprise the recommendation was worried
   about, with none of the compensating model.

2. **The Configure window groups shortcuts by scope, not by category.**
   *Recommended: a scope badge per row, keeping the category grouping.* The
   defect either option fixes: two rows bound to `I` — "Color picker tool"
   under Tools, "Insert keyframe at playhead" under Timeline — read as the same
   key bound twice, with nothing saying one of them only answers over the
   timeline. The feature worked and the one screen that explains it described
   something else.

   **What it costs.** "Everywhere" is now much the largest group — most
   bindings are general — and commands an artist thinks of together are split
   across headings when they happen to differ in scope. Two things buy that
   back, both under test rather than asserted in a comment: category still
   *orders* within a group, so the wide one is not a wall
   (`TheWidestGroupIsStillOrderedByCategory`), and every heading carries a line
   stating its rule, so the chain is read rather than inferred. Groups are
   ordered widest-first, so the page reads top to bottom in the order the
   resolver actually walks. The scope is searchable too — it became a heading,
   and typing the name of a group was otherwise the one query that emptied the
   list.

**The third thing found on the way, which needed no decision.** A docker torn
out into its own window answered *no shortcuts at all* — not its own, not the
general ones — because `FloatingPanelWindow` wired no key handling and the main
window never saw the press. Docking it again brought them back, which is what
made it read as a mystery rather than as a missing handler. Every test of this
feature called `IdFor` directly, so the resolver was correct, tested, and
unreachable from half the places a docker can be; `HoverShortcutScopeTests`
drives the real window instead and fails on all three floating cases without
the fix.
## Q83 · What span does the inbetween command fill when a breakdown sits inside it? — **answered 2026-08-14**

**Asked with the question prompt, three questions in one pass, all three
recommendations taken.** Phase 1's second half in
`docs/DESIGN-ai-correctness.md` asked for "`FrameRole.Breakdown` as a hard
constraint — the arc must pass through it", and measuring first found the
premise already met for the wrong reason: `ExposureSheet.NextKeyIndex` finds
the next *drawing* whatever its role, so a breakdown was already the interval's
endpoint and the arc passed through it trivially. There was no constraint to
add.

What the measurement did find was a **disagreement between two notions of a
span** living in the same timeline:

| | closes a span at |
| --- | --- |
| the inbetween command | the next **drawing** |
| `SpacingChart.Intended` | the next **extreme**, or the end of the sheet |

On key(0) · breakdown(1) · key(2) the chart overlay saw one run of 0→2 while
the command saw an interval of 0→1. Two consequences: the easing restarted at
the breakdown, so one slow-out/slow-in across a run came out as two with a
stutter in the middle; and a Q58 timing chart authored on the opening key meant
different things in the two places.

1. **The span is the run** — extreme to extreme, through breakdowns, matching
   `SpacingChart`. One action fills every gap, one undo step. It is what an
   animator means by "inbetween this span", and it makes the two notions agree.
   The cost is a behaviour change for anyone already using breakdowns, accepted
   because the old behaviour was the stutter.
2. **A timing chart spans the run**, the traditional ladder, which is what
   `SpacingChart` already read it as. A rung is a position across the run and
   lands in whichever gap contains it; a rung that merely describes a drawing
   already there asks for nothing.
3. **The AI path does not follow, for now.** `✦ AI Inbetween` keeps asking one
   gap at a time. A third drawing in the request is roughly +50% strokes and
   strokes are the dominant token cost (`docs/DESIGN-ai-payload.md`), and
   `InbetweenVerifier`'s betweenness check would have to become piecewise —
   which is Phase 3/4 work. The breakdown remains a hard constraint for the AI
   regardless, because it is still that gap's endpoint.

**The one thing (3) costs, recorded so it is not rediscovered as a bug:** the
two producers now disagree about the span, which is exactly the disagreement (1)
existed to remove — just moved. It is bounded (the AI's frames are still
correct, just spaced per gap) and it is written into `docs/manual/12-ai-assistance.md`
rather than left for an artist to notice.

**Nothing is ever moved.** Each waypoint keeps its frame and its pose, and a
renormalized local fraction cannot leave its own gap, so no inbetween between
the key and the breakdown can show a pose from beyond it. A breakdown that
disagrees with the easing is not corrected here — `SpacingChart` exists to
*show* that disagreement, and silently re-spacing a drawing the artist placed
would be the application arguing with them.
## Q84 · Camera and scene: the four remaining wishes, and where animation pegs belong — **answered 2026-08-14**

The *Camera and scene* section held four `[?]` wishes — safe area guides, zoom
preview, camera shake preview, scene preview — and the owner asked, reading
them, whether **animation pegs** fit here as well. They do, and the pegs
question turned out to be the largest of the five and the only one that touches
work already in flight. Four questions, prompted and answered in one exchange.

- **Pegs and bones** — *separate record, shared ops* (recommended, accepted).
  A peg hierarchy and a bone hierarchy are the same data structure — named
  nodes, a parent, a keyed and interpolated transform — and `Doc.Armature`,
  `Scene.PoseTrack` and `ArmatureOps`' FK solve already exist. So this was a
  real risk of building one thing twice, the mistake Q11's "reusable animation
  presets" and the parallel `ReferenceAnimation` record were struck for.
  Decided as Toon Boom arranges it: distinct node types over shared transform
  machinery. A `Peg`/`PegKey` record reuses the interpolation shape
  `CameraOps` already has, and `Armature.PegId?` hangs a rig off a master peg.
  Costs accepted, and sharpened by Q81's UI landing the same day: **coarse
  assignment already ships**, so a rigged character's rigid part movement is
  covered and the peg must not become a second way to do it. The peg's
  territory is the layer with *no* armature and no weights — a background pan,
  which today would mean creating an armature and binding strokes just to
  slide a painting. The two hierarchies must also keep sharing one graph
  editor or they will drift apart. One question is deliberately left for when
  pegs start: whether a peg auto-keys at the playhead like a bone or takes
  explicit keys like the camera. Q81 decision 2 made those two different on
  purpose, and a peg sits between them — a pan is authored as deliberately as
  a camera move, but dragging one is as frequent as posing.
  The alternative — one `TransformNode` type — is cleaner on paper and was
  declined for timing: merging the two records while the bone system is
  mid-flight buys a unification that shared ops already deliver, and it would
  put peg-shaped nullables into `Armature`'s bind-pose semantics. Waiting for
  bones entirely was declined outright: a shot cannot pan a background until
  cost-L skinning lands, and rigging a skeleton to slide a background layer is
  the wrong shape of work for the commonest camera-department job there is.
- **Zoom preview and scene preview** — *scene panel, and strike zoom*
  (recommended, accepted). *Zoom preview* is struck as a duplicate of the
  shipped `Camera preview / view through camera`, on the Frame-tagging and
  Timeline-bookmarks precedent: a wish indistinguishable from a built feature
  is the wish list the checkbox rules exist to prevent. *Scene preview* is
  absorbed into **Multiplane parallax** as the authoring surface stage 1
  otherwise lacks — the "Scene panel" `docs/DESIGN-3d-space.md` already names,
  a schematic of the layer stack with depths and the camera's path. Net: one
  item struck, one absorbed, no new item. The competing reading — that both
  were delivery-quality *preview render* (playblast) features — was considered
  and declined; ordinary playback plus view-through-camera covers what it was
  for, and a cached render tier is its own design.
- **Camera shake** — *a nullable modifier on the camera* (recommended,
  accepted). `Camera.Shake?` (amplitude, frequency, decay) evaluated inside
  `CameraOps.At`, its offset seeded from the frame through `Hash01`. Invariant
  2 is what makes this **better** than the field's version rather than a tax on
  it: a shake nobody can reproduce cannot be re-rendered at 4K or handed to
  anyone, and this one is identical every time. Seeding from the frame index is
  legitimate here where it would not be for a dab, because there is exactly one
  camera — nothing can flicker relative to a sibling. The *preview* half of the
  wish's name then costs nothing: a render-time modifier is visible in ordinary
  playback. Baking to keys was declined as the primary shape — 24 keys a second
  floods the graph editor and makes the underlying move unrecoverable, so
  re-tuning amplitude would mean undo and re-apply. A bake command remains
  available later as an addition, not a replacement.
- **Safe areas** — *nullable percentages on the camera* (recommended,
  accepted), with visibility as a view toggle. A delivery spec travels with the
  shot — broadcast and a web short want different safes — and `Camera` already
  carries `OutputWidth`/`OutputHeight`, which is the same kind of fact. A pure
  view preference was declined because the spec is then lost on handoff. Real
  `Guide` objects were declined as an outright defect: safes must follow the
  camera through a pan, push and roll, and guides snap strokes, so a
  compositional boundary would start grabbing linework.

Two consequences recorded because they reach past this section:

- **Invariant 5 will name two transforms, not one.** `CLAUDE.md` says the
  camera "is the one transform that is not" view-only. A peg is authored,
  keyframed, saved and exported on exactly the same terms, so that sentence is
  reworded as part of landing pegs rather than afterwards.
- **Pegs break this section's free-for-assets pattern, once.** Depth without a
  camera does nothing, which is why multiplane never taxes an asset document. A
  peg without a camera *does* something — it moves content on the canvas, so it
  exports. That is correct (an artist who pegs a layer meant to) and it means
  pegs are the one item here that is not free for the asset target.

## Q85 · The repair loop: how many re-asks, what they carry, and whether they are the default — **answered 2026-08-14, one against the recommendation**

Phase 3 of `docs/DESIGN-ai-correctness.md` is the repair loop: stage 4 of the
pipeline, sitting between *verify* and *refuse*. The design already stated the
principle — *"repair with the fault, not a blind retry"* — and left four things
open that decide what it costs and how it feels. Prompted as one batch, four
answers, three as recommended.

1. **Two re-asks, then refuse** — *against the recommendation of one.* The
   argument for one was that a model which has already ignored a named fault is
   mostly going to ignore it again, and that three calls is 90 to 360 seconds of
   an artist waiting for a frame they may not get. The owner's choice is the
   other reading, and it is a real one: the common failure is not a model that
   cannot draw the frame, it is a model that fixes the fault it was told about
   and trips a different check. One re-ask cannot see that shape at all; two
   can.

   **What it costs, measured rather than guessed** (`ARepairReAskCostsAboutHalfAgain_NotAWholeSecondRequest`,
   on the 40-stroke pair `DESIGN-ai-payload.md` uses throughout): a first ask is
   102.1 KB, a repair carrying one rejected frame is 153.3 KB — **1.50×** — and
   the worst case, two re-asks on a run where everything fails, is **4.01× a
   single ask across three calls**. That is the bill this answer accepts. The
   ratio is bounded below 2 by a budget test, because a repair costing more than
   a whole second request would mean the block had stopped being a correction.

   **What it costs in time, recorded so it is not rediscovered as a bug:** the worst
   case is three full calls to produce nothing, and the artist's only feedback
   during it is the status line. So the loop reports the attempt it is on while
   it runs, cancellation is honoured between rounds, and the refusal names how
   many attempts were spent — *"Nothing was inserted after 3 attempts"* — because
   three calls and one call are very different bills for the same empty result.
   `InbetweenRepair.MaxReasks` is the constant, and it is one number to change if
   the wait turns out to be worse than the frames are worth.

2. **The re-ask carries the fault *and* the model's own rejected drawing.** The
   refusal already reads as a sentence a person could act on — *"the ‘near-arm’
   did not stay between the keys — it sits 60px from where the motion puts
   it"* — but it names a stroke in a drawing the model can no longer see, and a
   model asked to fix a stroke it cannot see has to redraw the frame from the
   keys with a hint. Sending its own answer back turns the re-ask into an edit.
   It costs roughly one extra frame's strokes beside the two keys, on the repair
   call only.

   Declined: **also** sending the deterministic answer as a reference. Q32 and
   Q33 together say the free engine is weakest on exactly the complex organic
   subjects that get refused, so that would risk teaching the model to copy a
   bad reference precisely where the reference cannot be trusted.

3. **On by default**, unlike best-of-N. The brush rule — an expensive option is
   opt-in, deliberate, and never the default — does not cover this case, and the
   distinction is worth keeping: **best-of-N buys a better frame; repair buys a
   frame at all.** The alternative to spending the call is an empty slot. Also
   declined: on for local models and off for metered ones, which makes the same
   document behave differently depending on a setting in another window.

4. **A repaired frame records how many asks it took**, absent unless more than
   one — `AiProvenance.Attempts`, under the same optional-means-absent rule the
   record itself follows. It is the only durable trace: the status line saying so
   is gone by the next action, and *"how often does my model need a second go"*
   is the number that tells an artist whether the model they brought is
   borderline. It is also what the capability profile can eventually report.
   Declined for now: a marker on the cel in the timeline. Useful, and it is
   timeline UI on an AI branch, and the AI-provenance badge does not exist there
   either.

**The guarantee that fell out of building it, and was not in the question.** A
repair can never cost a frame that was already accepted. Accepted frames are
carried into the next round untouched, so the only way one can newly fail is the
coherence check — a repaired neighbour that makes it jitter. A round is therefore
adopted only when every frame accepted before is still accepted *and* at least
one more is; a round that gains two and loses one is dropped whole. Counting
accepted frames instead would take a frame the artist had already been given,
which reads as a bug however the totals came out.
`ARepairThatWouldCostAnAlreadyAcceptedFrameIsNotAdoptedEvenWhenItGainsTwo` is
the test, and it is the one test in the file that fails against the counting
rule.

**And one thing the answer to (2) turned out not to cover, found by the G12 pair
and reported by both reviewers independently.** "Fault plus the rejected
drawing" is enough for every refusal *except one*. `InbetweenFault.Incoherent` —
*"it jitters against the frames beside it"* — is defined against frames the
re-ask does not otherwise carry, so a model told that and shown nothing can only
guess. The first cut made it worse by *saying* "keep your corrections consistent
with the frames that were accepted" while sending none of them.

The extension, decided on the measurement rather than referred back: a coherence
repair also ships the **immediate accepted neighbours**, and no other fault does.
That is 2.50× a first ask against the ordinary 1.50×
(`AJitterRepairCostsMoreBecauseItShipsTheNeighbours`), paid only by the fault
that cannot be stated without it. The general rule it leaves behind is worth more
than the fix: **a fault can only be repaired if the re-ask carries what the fault
is measured against** — a constraint on any check added later, not a bug in this
one.

## Q86 · Bones phase 3: connected bones, the IK target, and how the phase lands — **answered 2026-08-14, all three as recommended**

Prompted before starting phase 3 of `docs/DESIGN-bones.md`, with phases 1
and 2 landed.

1. **Connected bones get a flag.** `Bone.Connected`, nullable so an
   unconnected bone writes no key. An extruded child sets it and the solve
   places it at the parent's tip, so re-lengthening a parent drags the chain
   the way Blender does. The alternative — extrude places the child at the
   tip and nothing glues it — left a gap to close by hand every time a limb
   was re-proportioned, which is exactly the kind of tidying that makes a rig
   feel unfinished. Costs accepted: a record field, a branch in the solve, and
   it has to survive serialization and image resize like every other
   coordinate-adjacent key.
2. **An IK chain aims at a target bone**, Spine's and Blender's model, rather
   than a bare point keyed in the pose track. The decisive argument is that a
   bone already poses, keys and interpolates through machinery that exists:
   no second kind of keyframe, no second overlay, and the handle is visible
   and grabbable like everything else. A keyed point would have been less to
   author for a two-bone limb and could not be parented to anything, which is
   what an animator wants the moment the character walks.
3. **IK lands on its own branch**, before aim/copy constraints and spline
   chains. It is the piece an animator feels immediately, and constraints
   layer onto a solved pose rather than the reverse. Doing all three at once
   would match the doc's phase boundary and produce one diff touching the
   solve three ways, which is where a determinism bug is hardest to localise.

## Q87 · How a drawing knows which bone moves it — **answered 2026-08-14**

Prompted by the owner's question, which exposed more than it asked: "is it
bound to the layer? To the layer group? Is it assignable?" The honest answer
was **none of those** — `Stroke.Weights` is per stroke, `Assign` acts on the
current stroke selection on the current frame, and layers know nothing about
the rig. Fine for one illustration; for a two-layer character over 200 frames
it is 400 manual binds, and nothing binds a stroke drawn after rigging. For an
application whose stated unit of work is a sequence, that is a hole rather
than a missing convenience.

The owner's own proposal was a layer *lock* to the layer above or below,
carrying alpha lock, "bone lock" and more. Answered:

1. **Layers link, and the link holds across frames** — the owner's shape,
   sharpened by their own words: *"link layer across frames … so we can paint
   lines, colors, details, effects and only the linked layers move along."*
   Recommended layer-level binding was declined, and rightly: it makes each
   layer state its own relationship to the rig, where what an artist has is a
   **set of layers that are one drawing**. Linking says that once. Because the
   link is a property of the layer structure rather than of a drawing, it
   applies to every frame at no extra cost — which is the half that closes the
   400-binds hole.
2. **A general link, several properties travel it** — recommended
   bone-specific was declined. Cost accepted knowingly: every property that
   travels has to answer what inheriting it *means*, and some have no sensible
   answer. The mitigation is that travelling is **opt-in per property on the
   link**, so a link made for bones does not silently start sharing alpha lock.
3. **The bone is named on the layer**, not inferred from adjacency.
   Layer-above/below addressing was declined for the reason it keeps being
   declined here: reordering layers would silently retarget the link, and a
   silent retarget is the invisible-failure shape this tool has already been
   reported for three times. The owner's ctrl/alt+RMB gesture survives as a
   fast way to *join a link*, which is stable under reorder in a way that
   "follow whatever is above me" is not.
4. **A stroke drawn on a linked-and-bound layer is bound.**

One interpretation made rather than asked, recorded because it departs from
the letter of (4) while serving its intent: the layer's binding is resolved at
solve time for any stroke that has no weights of its own, instead of writing
`Weights` into each stroke as it is created. It delivers the same thing an
artist sees — draw on a rigged layer and the line is rigged — and it is
better on three counts. It binds strokes drawn *before* the layer was linked,
so linking is retroactive rather than only forward. It writes no per-stroke
key on 200 frames of drawing, which is the camera's rule. And there is one
source of truth rather than a copy in every stroke that can drift from the
layer that made it. Per-stroke weights stay exactly as they are: the override
an artist paints when a shoulder needs two bones, and they win over the
layer's binding wherever they exist.

**The gestures, added 2026-08-14 by the owner.** Adjacency returns as the
*gesture* rather than as the addressing, which is exactly the distinction (3)
preserved — the gesture reads the neighbour once, at the moment it is made,
and what it writes is a link membership by id. Reordering afterwards is
therefore safe, where "follow whatever is above me" would not be.

- **Ctrl+Shift+RMB** — link to the layer above. **Ctrl+Alt+RMB** — the layer
  below. **Shift+RMB** — remove the link.
- **RMB** — the docker's own context menu, unchanged, with **Linking** and
  **Follows the rig** added as flyouts: the same options, named, plus the
  per-property switches, so the gestures are discoverable rather than
  folklore. A *bare* right-click deliberately does nothing new — that menu
  already carries rename, reorder, merge and folders, and opening a link menu
  over it would shadow the lot.

**Every link gesture is on the right button, and that is the correction that
produced the mapping.** The first spec put the menu on Ctrl+click, which is
the docker's multi-layer toggle. Raised, and the owner moved the gestures
rather than the selection — *"You are right. Lets move it to ctrl + shift and
ctrl + alt to link layers. Shift click to remove the link."* Taken as
right-button throughout, because moving off Ctrl only helps if Shift is
cleared too: Shift+click is the docker's range select, so a left-button
unlink would have traded one collision for another. Keeping the whole left
button for selection is what makes "Ctrl+click still toggles" and "Shift+click
still ranges" both true at once.

**The docker draws it, added 2026-08-14.** A link nobody can see is a link an
artist has to remember, so a linked row is indented and carries an elbow down
its left edge in the link's colour. The owner described two cases — an elbow
one way up for a layer linked above, the other way for below — and it is
generalised to a **bracket**: corner at the top of a run, line with a tick
through the middle, corner at the bottom. The two cases are what that reduces
to for a pair, and it keeps reading with three or more, which their two-case
form does not cover.

The mark is asked of a row's **neighbours** rather than of the link's
membership order, and that is the part worth keeping: a link's members need
not be adjacent, so a bracket drawn from membership order would run a
continuous line down the side of a row that is not in the link.
`LayerLinkMark.Detached` is the honest answer for a member with neither
neighbour in the link — a tick, joining nothing.
