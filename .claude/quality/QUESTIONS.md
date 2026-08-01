# Open questions

Decisions the loop could not make for you. Each one blocks something
specific; each can be answered in a line. Answer inline (edit the file) or in
chat — the loop reads this file at the start of every round and treats an
answered question as settled.

Questions are removed once implemented, with the decision recorded in
`LOOP.md`.

---

## Q6 · What a sampled smudge re-reads on reload

**Blocks:** Smudge (all layers) and Blur (all layers) — everything else
about them is plumbing.

A layer's rendered bitmap is currently a function of that layer alone, which
is what makes the frame cache simple and per-layer. A brush that samples the
whole composite breaks that: the result depends on the layers underneath.

- **(a)** Live. A sampled stroke re-samples whatever is beneath it at render
  time, so editing a lower layer updates the smudge above it. The cache key
  gains the backdrop's identity and invalidation cascades up the stack.
  *(recommended: it is what "sample all layers" means, it keeps the stroke
  record the single source of truth, and a reload always agrees with what is
  on screen)*
- **(b)** Baked. The sampled pixels are captured into the stroke when it is
  committed, so the mark never changes afterwards. Simple caching, but the
  document now carries pixel data that the record cannot regenerate — which
  cuts against "the stroke record is the document".

**Recommend (a).** It costs cache work; (b) costs an invariant.

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
