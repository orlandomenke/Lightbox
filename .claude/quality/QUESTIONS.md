# Open questions

Decisions the loop could not make for you. Each one blocks something
specific; each can be answered in a line. Answer inline (edit the file) or in
chat — the loop reads this file at the start of every round and treats an
answered question as settled.

Questions are removed once implemented, with the decision recorded in
`LOOP.md`.

---

## Q11 · What a "reusable animation preset" would be that a cycle symbol is not — **answered (b)**

**Answered 2026-08-03: (b), a timing preset, and the other line is struck as (a).**
One item, specified: *save an exposure pattern and apply it to a range of cels*.
It re-exposes drawings that already exist, which is the half of frame-by-frame
work a symbol cannot carry — a symbol carries drawings, a timing preset carries
their spacing.

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

Kept here rather than moved to `LOOP.md` because the decision is settled and
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

## Q15 · Is a mirrored stroke one stroke or two?

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

## Q16 · Is a subject reading stored, and what makes it stale?

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
losing it is free. Not decided.

## Q17 · Does an inking pass replace the pencils or land on its own layer?

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

## Q18 · Do flat point arrays cost schema adherence?

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

## Q20 · When a textured line is re-shaped, may its texture change?

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

## Q21 · How big does a reference the model has to read need to be?

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
