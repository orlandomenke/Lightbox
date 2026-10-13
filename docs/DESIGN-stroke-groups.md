# Stroke groups: why a merge should never turn strokes into pixels

**The stroke record is the document — and merge down was the one command that
quietly broke that.** Recorded 2026-10-13, after the owner put real drawings
through the AI inbetweener, found their merged line art had become pictures, and
restated the deal in one sentence: *"working with the stroke record was the
superpower compared to other tools like Krita and Photoshop — editable
everything everywhere, without losing the power of digital painting."*

This note is the design that keeps the deal through a merge. It is written
before the code, records what was decided and by whom (Q265), and is meant to be
argued with: if you are about to add a case where a merge bakes pixels, read
*What still flattens* first, because the list is supposed to get shorter.

## The short answer

A merged drawing keeps the upper layer's strokes in a **group**: one entry in
the drawing's stroke list that holds strokes of its own together with the blend
mode, opacity, clip, mask and effects the layer had. It is drawn by rendering
those strokes on their own and compositing the result onto the drawing — which
is exactly what the merge did once, at merge time, before throwing the strokes
away. A group does it every time the drawing is rendered instead, and keeps
them.

Three things follow, and they are the reason for choosing this shape:

- **A merge is exact, and still strokes.** No approximation: the picture is
  computed the way the two layers computed it.
- **An eraser inside a group can only reach that group's strokes.** The whole
  reason erasing forced pixels disappears, with nothing to cut.
- **Anything drawn after the merge acts on the whole drawing**, because it comes
  after the group in the list. Erasing "the merged layer" erases all of it.

## Why merge flattened at all

A frame's pixels are *baseline, then each stroke in order*. Two layers merge by
appending the upper drawing's strokes after the lower's — when that gives the
same picture. It does not whenever something about the upper layer reads or
removes what is underneath it, or is applied when the layers are combined rather
than when a stroke is stamped:

| Reason a pair flattened | Why appending strokes was a different picture |
| --- | --- |
| eraser, clear-region | removes whatever is under it, which now includes the lower layer's marks |
| alpha-locked stroke | paints only where there is already ink; "already" gains the lower layer |
| smudge, blur, colour mixing | sample the canvas so far |
| blend mode, opacity on the upper layer | applied when layers are combined; a stroke cannot carry them |
| mask, clip to the layer below | carve the upper layer's result |
| effect stack, layer style | a filter over the layer's result, per frame |
| upper drawing has a pixel baseline | a baseline sits under a frame's strokes, and there is one slot |
| lower layer has a mask or effects | they apply to the lower content only, not to what lands on top |
| a folder's shape carves the upper layer | depends on the *other* layers in the folder |

`LayerMerge.PairIsLossless` is that table as code. Everything it refuses goes to
`BakedFrame`, which renders the pair once and stores a PNG.

**Every row but the last is computed from the two drawings alone.** That is the
observation the design rests on: the bake is a pure function of two frames and a
handful of layer attributes, so those can be kept and the function run later.

## The model

```
Frame.Strokes: [ stroke, stroke, GROUP, stroke, … ]

GROUP = a Stroke with Tool = Group and a Group block:
    strokes     its own stroke list (which may contain groups)
    baseline    its own pixel baseline, when the layer it came from had one
    blend       how its result lands on what is under it
    opacity
    clip        land only where there is already paint beneath it
    mask        a frame carved out of (or into) its result, and whether inverted
    effects     the layer's effect stack, applied to its result per frame
```

A group is an entry in the list rather than a new field on the frame because
order is meaning here: strokes before it are under it, strokes after it act on
it. It is a kind of stroke because the list already holds six kinds — brush,
eraser, fill, clear-region, gradient, text — and one place draws all of them.

### What a merge writes

For each pair of drawings the two layers expose:

| The pair | The merged drawing's stroke list |
| --- | --- |
| plain strokes on both | lower's strokes, then upper's — as today |
| the upper needs any of the group reasons above | lower's strokes, then **one group** holding the upper drawing and the upper layer's attributes |
| the lower layer has a mask or effects | **a group** holding the lower drawing with them, then the upper (in a group of its own if it needs one) |
| a folder's shape carves the upper | flattened, as today |

A drawing merged twice nests: the earlier group is simply one of the strokes the
later merge appends or wraps.

## Drawing a group

`BrushEngine.StampStroke` is the only stroke-to-pixel path (verified: every
caller goes through it) and already dispatches by kind. A group is one more
branch, and what it does is the bake's own order, moved from merge time to
render time:

1. render the group's baseline and strokes to a scratch surface, each stroke
   through `StampStroke` — so a nested group, an eraser, a smudge all behave
   inside it exactly as they did on their layer;
2. apply the effect stack's filter for this frame index;
3. carve by the mask;
4. carve by what is already on the target, if it clips;
5. apply the layer style;
6. composite onto the target with the blend mode and opacity.

The scratch-then-composite shape already exists three times in the engine
(`StampPaint`'s own scratch, `ComposeDraftRegion`, `LayerMerge.BakedFrame`).
Steps 2–5 are `BakedFrame`'s helpers, which become shared rather than copied.

### The costs of that, stated before they are found

- **A surface per group per render.** Bounded by the window being repainted —
  invariant 6 holds only if a group honours `StampStroke`'s `window`, and that
  is a test, not a hope.
- **A group's reach is the union of its children's.** `CommitRegion` and
  `VisibleDrawingBounds` have to know that, or a bounded repaint skips it.
- **The fast paths have to say no honestly.** Tiling (`CanTile`), stroke
  checkpoints (`CanCheckpoint`) and region repaint (`CanRepaintRegion`) each
  decide eligibility by scanning top-level strokes; a group that contains a
  smudge must count as one. The checkpoint fingerprint already hashes each
  entry's JSON, children included, so it stays sound.
- **Effects are per frame.** A group with a wiggle effect renders differently at
  each cel index; `Materialize` already takes one and must pass it down.

## Everything that reads a stroke list

Forty-six clusters of code read or edit `Frame.Strokes` (inventory taken
2026-10-13). Met with an entry they do not understand, almost all of them do
nothing with it — safe, and also the definition of "not yet editable there".
Six would do something wrong:

| | What goes wrong | Fixed in |
| --- | --- | --- |
| `TransformOps` (move, flip, transform) | moves the group's own empty points; its strokes stay behind | step 1 |
| `ImageResize` | its strokes are not rescaled | step 1 |
| `Stroke.Clone` | a memberwise copy would share the child list between copies and undo snapshots | step 1 |
| `StrokeRecordCleaner.EffectiveStrokes` | erasers never reach inside, so erased ink returns in inbetweens and trails | step 1 |
| `StrokeMatcher`, `InbetweenVerifier` | match and count a group as a brush stroke with no points | step 1, via the cleaner |
| `StrokePayload.ToWire` | sends a group to the model labelled "brush", empty | step 1, via the cleaner |

The AI reads a drawing through the cleaner, so teaching the cleaner to look
through a group — its strokes, with its erasers applied — is what makes a merged
drawing readable by the inbetweener. **As built**, the cleaner applies a group's
erasers the way it applies any: a stroke they covered almost entirely is
dropped, a partly erased one is sent whole. Sending the *cut* pieces instead —
`EraserCut` (Q264) as the reader — is a better description of the page and is
not built; it belongs with the *simplify* command in step 3, because both are
the same cut.

### Three more that turned up while building it

The inventory was of code that reads `Frame.Strokes`. These read a *stroke*, and
judged a group by points it does not have:

| | What went wrong | Fixed |
| --- | --- | --- |
| `TransformErasures.RegionReading` | a selection drawn straight over a merged drawing caught nothing | the group is where the ink inside it is, read as a drawing of its own |
| `VisibleDrawingBounds` | a drawing that is one group measured as empty: *"Nothing to transform in this scope"* | the box wraps what is visible inside its groups |
| `TileFallback` | named a group as a smudge stroke in the render report | its own reason |

The first is the one that would have made keeping strokes a step back: flattened
pixels move under a selection (B433), and a group that did not would have cost
the artist something the pixels could do. A group is therefore cut by a
selection exactly as a single stroke is — two copies under complementary clips,
the entry honouring its own `ClipId` over its finished result.

## The file

- A group is absent unless a merge made one. A document with none serializes
  byte for byte as it does today, and a test dumps the JSON to prove it.
- **An older build cannot read a document that has one** — it meets a stroke
  kind it has no name for and stops. SENSITIVITY W2 asks for exactly that
  ("a file from a newer build is refused honestly … never opened and re-saved
  … in a way that silently drops what the older build did not understand"), so
  the version a document is written with goes up **only when it contains a
  group**. Everything else stays openable everywhere.
- **What an older build actually says, checked rather than assumed:** *"Could
  not open X: The JSON value could not be converted to …ToolKind"*. It is a
  refusal, and it is safe — nothing is opened, so nothing is re-saved without
  the drawing — but it is not *"saved by a newer Lightbox"*, because no build
  before this one looked at the version before parsing. The number cannot fix
  builds that already exist. It is still the right marker to write, and this
  build reads it first from now on, so the next format change is refused in
  plain words.
- A committed fixture guards the format (W8), and loading a pre-group document
  changes no pixel (W5) — there is no migration, because nothing is rewritten.

## What still flattens

- **A folder's shape carving the upper layer.** It depends on the other layers
  in the folder, which a drawing cannot carry with it. Rare, and named.
- **A stroke sampling all layers *live*.** Looked at rather than assumed: the
  frozen kind is exact inside a group, because the stroke carries its own
  sample of what was beneath it, lower layer included. The live kind re-reads
  the stack beneath the *merged* layer, which no longer contains the lower
  drawing — a different picture.

The list grew by two during the build, as this section said it might:

- **A placed symbol on a drawing that needs a group** (or under one). A
  placement lives on the frame and is drawn over every stroke of it; a group has
  no slot for one. Kept on the frame it would escape the blend, mask or effects
  of the layer it was placed on, or change places with the strokes that were
  above it. The cure is a placement that can sit *in* the stroke list, which is
  its own piece of work.
- **A layer clipped onto a layer that has effects.** The compositor carves a
  clipped layer by its base's content and mask, *before* the base's effects. A
  clipped group is carved by what is on the drawing, which is after them. The
  cure is a group that can name what it is clipped by.

And by two more in review, both about cost rather than the picture:

- **A merge that would nest groups more than eight deep.** Each merge that
  needs a group adds a level, and the file format refuses to write past 64
  levels of JSON — a document merged far enough rendered and could not be
  saved (the sensitivity review). `StrokeGroups.MaxDepth` is eight; past it
  that drawing flattens, with the usual warning. Eight is also eight surfaces
  held at once to render one drawing.
- **A lower drawing that samples all layers live, when its layer's mask or
  effects would put it in a group.** Such a drawing is never cached; as a
  picture that was a decode per read, as a group it would be a group render per
  pointer event (the leak-hunter).

Nothing else flattens, and `LayerMerge.CanGroup` is this list as code.

### What review found that the tests had not

Recorded because each was green before it was found.

- **A group merged a second time was appended like a plain stroke** when the
  second merge needed nothing — and a clipped or blended group then read the
  lower layer's ink as well as its own drawing's (the adversary, with a
  repro). A group that is clipped or not Normal is no longer appendable.
- **A group cut by a selection lost its halves' stencils on the next
  whole-layer move** — the entry's own clip did not travel (B340's shape).
- **The spacing nudge moved half a merged drawing**, and trim-to-content cut a
  group's pixels off: `FrameTranslate` and the crop bounds were consumers the
  inventory missed. Symbols, the stroke clipboard and document export collected
  clip regions and swatches from the top of the list only.
- **A constant effect keyed a held drawing's cached picture by cel**, rendering
  a 24-cel hold 24 times. Only an effect that changes over time does now.

### Known, and not fixed here

- **Ink a group partly hides still reaches the inbetweener.** Asked of the
  owner (Q266): a group that shows nothing at all — zero opacity, or a mask
  with nothing painted on it — is left out of what the AI reads; anything else
  is read whole, as an unmerged masked layer's strokes always were. Sending
  only the visible ink means cutting lines by a mask, which is its own branch.
- **A rig does not pose strokes inside a group.** The flattened merge froze the
  rest pose too, so nothing is lost, but nothing is gained either.
- **The cost of a group is a full-surface scratch per render**, and a mask is
  rendered whole each time. Sizing the scratch to the window being repainted is
  the first thing to do if a merged drawing is felt on undo at 4K.

## Steps

Each is its own branch and pull request, in this order (Q265):

1. **Exact merge, whole-group editing.** The model, the file, rendering, the six
   wrong consumers, and merge writing groups. A group moves, transforms,
   resizes, copies and deletes as a unit; the picker selects it; the AI reads
   through it; **Split group** puts it back on a layer of its own. Reshaping one
   stroke *inside* a group is not in this step.

   **Landed in two parts, and the split is recorded because it was not the
   plan.** The first pull request is everything that makes a merge keep strokes
   and lose nothing the pixels could do: model, file, rendering, the consumers
   above, merge writing groups, the AI reading through, and a group moving with
   its drawing — whole, or cut by a selection. What it leaves for the second:
   **Split group**, and the picker selecting, copying and deleting a group on
   its own. Until then undo is the only way back from a merge. They were held
   back because each is a command with a design of its own (a split has to
   rebuild a layer's exposure sheet and decide what happens to marks drawn after
   the merge), and neither changes what a merge writes.
2. **Editing inside a group**, tool by tool: pick, reshape, width, recolour,
   path edit, stroke clipboard, bones. The inventory above is the checklist.
3. **Retire the eraser dialog** (Q264). Merge is exact either way, so it stops
   asking; the cutter stays as the reader and as an optional *simplify to plain
   strokes* command, which is the form the inbetweener matches best.

## Decided, and by whom

Asked of the owner on 2026-10-13 with `AskUserQuestion`; all four as recommended.

| Question | Answer | Against |
| --- | --- | --- |
| Where do groups live? | An entry in the stroke list | Keeping the layers and showing them as one: far less code, but anything drawn or erased after merging lands on one inner layer only — a tidier folder, not a merge |
| What ships first? | Exact merge, whole-group editing | Holding everything until every tool reaches inside; or erasers and blend/opacity only |
| Older builds? | Mark only documents that contain a group as newer | Also offering a "save for older builds" export |
| The eraser cutter (#732)? | Merge it; retire its dialog later | Holding it; closing it |

The larger decision came first, the same day: asked how far to go to keep the
deal, the owner chose **grouped merge, never rasterize** over designing it now
and building it after the AI work, and over stopping at erasers.
