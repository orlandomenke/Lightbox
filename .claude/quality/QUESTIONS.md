# Open questions

Decisions the loop could not make for you. Each one blocks something
specific; each can be answered in a line. Answer inline (edit the file) or in
chat — the loop reads this file at the start of every round and treats an
answered question as settled.

Questions are removed once implemented, with the decision recorded in
`LOOP.md`.

---

## Q9 · Who owns brush settings

**Blocks:** nothing yet. Surfaced by live-preview work, which tripped over it.

Brush settings live on the view model and are persisted to one shared store
(`MainViewModel.BrushStorePath`); every new view model loads from it. So they
are global to the process, not to a document. Two consequences already
visible: switching a brush to watercolour in one test hands watercolour to
every view model constructed afterwards, and two open documents cannot have
different brushes.

- **(a)** Global, as now — one brush state for the app, persisted between
  sessions. Matches Photoshop and Krita: the brush is a property of the tool,
  not the file. Tests must isolate the store, which is what
  `BrushStateIsolated` now does.
- **(b)** Per document. Each open document remembers the brush it was last
  painted with, saved in the file. Nicer for switching between a line-art
  document and a painting one, and it makes a document reproduce exactly on
  another machine — but it is not what artists expect from the tool bar.
- **(c)** Global by default, overridable per document.

**Recommend (a)** — it is the convention, and the only real cost is test
discipline. Worth asking because (b) has a genuine pull for a character-based
workflow, where a character's brush set is part of the character.

**Update, 2026-08-02.** "Test discipline is now in place" was optimistic, and
CI proved it: nine test classes set brush parameters from outside the
`BrushState` collection, and one of them raced `LivePreviewPixelTests` on a
loaded runner. The pixel check went red on CI and green on every local run —
which is the failure mode this whole arrangement exists to prevent, arriving
anyway because the rule is a convention a reviewer has to notice rather than
something the compiler can hold.

They are all in the collection now, but that is a patch on the symptom. The
argument for **(b)** or **(c)** is stronger than it was: process-wide mutable
state that only a naming convention protects will keep leaking, and each leak
looks like a flake until somebody spends an afternoon on it. Still (a) on the
product merits; the test cost is higher than this entry originally claimed.

---

## Q10 · Does wet paint survive between strokes

**Blocks:** the whole fluid-media pass. See `docs/DESIGN-fluid-media.md` for
what that pass is; this is the decision that has to come before any of it.

Real wet media let the *next* stroke pick up what the last one left: that is
what "wet" means to an artist, and it is why a smudge over drying gouache
behaves differently from a smudge over dry gouache. It is also what puts
simulation state into the document, because invariant 1 says a reload must
render the same image. A moisture buffer that persists between strokes and is
not saved makes a reload a different painting.

- **(a)** Paint dries between strokes. Moisture, pigment and height buffers
  live for one stroke and are discarded. Everything in the fluid pass still
  works *within* a stroke — wet edges, advection, pooling, dry-brush tearing —
  and the record stays exactly what it is today. Cheapest by a wide margin, and
  bounded by construction.
- **(b)** Paint stays wet, and the wet state is part of the document. Strokes
  interact the way they do on paper. The record grows a per-frame fluid buffer
  that has to serialize, and every stroke's result now depends on the complete
  history before it — so an edit in the middle of a frame re-renders everything
  after it.
- **(c)** Paint stays wet for a bounded window — the last N strokes, or until
  the frame changes — with the window saved so a reload reproduces it.

**Recommend (a) to start**, because it delivers most of the visible difference
— the flat noise look is a *within-stroke* problem — at none of the record
cost, and because (b) can be built on top of it later without the intermediate
work being wasted. Worth asking rather than assuming: an oil painter would say
(a) is not wet media at all, and this app has digital painting as a first-class
purpose, not a hobby attached to the animation.

---

## Answered

### Q5 · What "animate on 2s" does — **both, as separate commands**, 2026-08-01

Two distinct operations rather than one with a mode:

- **Stretch to Ns** — each drawing is held for N frames, so the range gets
  longer and no drawing is lost. This is what an animator means by "animating
  on 2s".
- **Reduce to Ns** — keep every Nth drawing and discard the rest, so the
  range keeps its length. Destructive, and named so it reads that way.

### Q7 · How much of the Photoshop brush panel — **(b)**, 2026-08-01

Tier 1 plus Texture and Colour Dynamics from tier 2:

1. Size jitter with minimum diameter
2. Angle and roundness jitter
3. Direction-following angle
4. Dual brush
5. Flow jitter
6. Texture — settable paper tile, depth and scale (generalises granulation)
7. Colour Dynamics — foreground/background jitter, hue/saturation/brightness
   jitter

All are per-dab modulations seeded from dab position, so determinism is
unaffected. Colour Dynamics is the one that needs a model change: it wants a
second colour in the record.

Still declined: airbrush build-up timing, bristle qualities, and the full
Mixer Brush reservoir. Each is a simulation rather than a parameter, and none
survives an `.abr` round trip in a form we could honour.

### Q8 · Rename the brush pages to match Photoshop — **(a)**, 2026-08-01

Adopt Photoshop's grouping inside the Effects page: Shape Dynamics,
Scattering, Texture, Dual Brush, Transfer. An imported preset should be
recognisable to someone who knows the panel it came from.

### Q1 · Smudge with no colour of its own — **(a)**, 2026-08-01

The first dab picks up the colour under it and deposits it, then drags from
there. A tap therefore softens a colour boundary slightly and does nothing at
all on flat colour, which is what an artist expects.

Was implemented as (b) — the first dab sampled but returned early, so nothing
appeared until the pointer moved.

### Q2 · What a locked layer blocks — **(a)**, 2026-08-01

Locking blocks everything that changes pixels or geometry: paint, fill,
transform, delete, blank, cel clear/cut/paste, and external writes over
IPC/MCP. Visibility, opacity, blend mode and reordering stay available, so a
locked layer is still useful as reference.

Follow-ups taken with it: locking a group locks every layer inside it, and
the brush cursor shows a blocked state over a locked layer rather than
silently doing nothing. A locked layer still renders and still exports —
locking is about editing, not visibility.

### Q3 · The default background layer — **(a)**, 2026-08-01

A new document with a paper colour gets a locked `Background` layer filled
with it, which can be unlocked and painted like any other layer.

Follow-up taken with it: the checkerboard shows wherever the composed image
is transparent, whether or not a background layer exists.

### Q4 · Cursor ring under pen pressure — **(a)**, 2026-08-01

The ring shows maximum size while hovering and tracks live pressure while the
pen is down. Live tracking is a setting, defaulting **on**.

---

## Q11 · What a "reusable animation preset" would be that a cycle symbol is not

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

## Q12 · Whether an animation template is a document or a project type

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
