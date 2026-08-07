# Reading the subject before drawing on it

Status: **backlog design, nothing built.** Three features asked for this
independently — AI inbetweening (built, and currently blind), AI inking, and
normal maps — which is the signal that the thing they share should be designed
once rather than three times. Same reasoning the project container got.

## What they each keep asking

| Feature | The question it cannot answer today |
| --- | --- |
| Inbetweening | *That arm swings; what is behind it, and is this a biped or a quadruped?* |
| Inking | *Is this line a jaw or a cast shadow? Should it be heavy?* |
| Normal maps | *Is this region a cheek, a sleeve fold, or the ground?* |

All three are asking **what am I looking at**, and all three currently answer
it from a flat RGBA image and nothing else. An inbetweener that knows the
character is a biped, has an arm here and a torso behind it, and that the arm
occludes the torso in this frame, is doing a different job from one shading
pixels between two bitmaps.

## The reading: taxonomy and placement, kept apart

The single most useful decomposition, because the two halves have different
lifetimes:

**Taxonomy — per character, stable.** *This is a biped. It has a head, a torso,
two arms, two legs. The near arm is normally in front of the torso.* True in
every frame of every animation of that character, so it belongs on the
character in the project, is read once, and is worth an artist reviewing and
correcting by hand.

**Placement — per frame, per layer, disposable.** *In frame 12, the arm's
region is this polygon, it occludes the torso here, and the head is turned
three-quarters.* True of one drawing only. Cheap to recompute, expensive to
keep, and stale the moment somebody redraws.

Splitting them means the expensive, reviewable, worth-storing half is stored
once per character rather than once per frame, and the half that goes stale is
the half nobody is keeping.

## Depth turned out to be load-bearing twice

`SubjectPart.Depth` was added so an inbetween keeps the near arm in front of the
torso. It has a second job nobody designed it for, and it is the more important
one: **it is what makes a reveal checkable.**

A higher-depth part moving off a lower-depth one *vacates a region*. New ink
inside that region is expected — the body behind the swinging arm, the window
behind the drawn curtain — and new ink outside it is not. Without depth, "the
model invented a stroke" and "the model drew what the motion revealed" are the
same observation, and a verifier has to either forbid both or allow both.

`docs/DESIGN-ai-correctness.md` builds on this. It is recorded here because it
changes what the taxonomy is *for*, and a later reader deciding whether depth
earns its place should know it pays twice.

## Where it must not go

**Not into the render path.** Invariant 2 forbids randomness in rendering, and
an AI reading is the least reproducible thing in the building. It is safe
anyway, and the reason is worth stating plainly: **the reading is an input to
*authoring*, never to rendering.** An AI inking pass consumes the reading and
emits ordinary strokes; from then on the document is strokes, replay is
deterministic, and the reading could be deleted without changing a pixel. That
is exactly the shape the existing inbetweener already has, and it is the line
every one of these features has to stay on the right side of.

The test that says so: **delete every reading from a finished document and
re-render — it must be byte-identical.** If that ever fails, something has
started reading the analysis at render time and invariant 2 is gone.

## Where the rig already went

`DESIGN-skeleton.md` has `parts`: named regions with a z-order, bound to bones,
drawn by hand. That is the same concept arrived at from the other direction,
and the reading must not compete with it.

**A hand-made rig wins.** Where an artist has named a part, that is what the
part is; the reading fills in where they have not. This is the derived-not-
asserted rule applied to a subject: a guess is a default, never an override of
something a person stated.

## The light

Asked for by both inking and normal maps, and it means two different things —
worth separating before somebody builds one thing that does both badly.

**A normal map does not need a light.** It encodes surface direction; that is
the whole point of it, and it is why a game can light the sprite at runtime
from any angle. A light in the normal-map tool is a **preview rig** — it exists
so an artist can rock the light around and see whether the map reads. It is
view-only, in invariant 5's sense, and it must never end up baked into the
output.

**Inking and shading do need one.** "Which side of this form is in shadow" and
"which contours are the lit edge and should be thin" are questions with no
answer until a light is placed. Here the light is a **generation input**: it
decides what gets drawn, the strokes are committed, and the light is not
consulted again.

So one record, two uses, and the rule that keeps them apart: **the light never
reaches `StampStroke`.** It is read by generators before there are strokes, and
by a preview after there are pixels. Nothing in between.

Where it lives: on the scene, nullable, absent until placed — the camera's
rule. A document that never lights anything must serialize, render and export
exactly as it does now.

## Inking styles

The request is "flat lines, comic book style", and the useful way to hold that
is: **a style is a brush preset plus a policy for choosing width and placement.**
The preset half already exists and was just given tags, overwrite and revert —
an inking style should be an ordinary brush the artist can open and edit, not a
hidden table of numbers.

The policy half is what is new, and the axes worth having are the ones that
change what an inker would actually do:

| Axis | What it decides |
| --- | --- |
| **Weight** | Uniform, or heavier where the form turns away from the light |
| **Taper** | Whether a line thins at its ends |
| **Depth cue** | Whether nearer contours are heavier than far ones |
| **Interior detail** | Silhouette only, or folds and creases too |
| **Fills** | Whether solid blacks are laid in as well as lines |

Flat and comic then stop being two hard-coded modes and become two points in
that space, which matters because the third style somebody asks for will not be
either of them.

## Normal maps: Laigter, AI, or both

[Laigter](https://github.com/azagaya/laigter) is the obvious candidate and it
is **GPL-3.0**. That decides the integration shape rather than being a footnote
on it:

- **Linking it in** would put Lightbox under GPL-3.0. That is a project-level
  licensing decision, not a feature decision, and it should not be made by
  accident inside a normal-map task.
- **Running the CLI as a separate process** is the ordinary way to keep the
  licences apart, and it fits how it should behave anyway: an optional external
  tool the artist points at, absent unless they have it, with the app degrading
  to its own generator rather than breaking.

Which suggests three tiers rather than one choice, in increasing order of what
they need to know:

1. **Built in, from the alpha.** Sobel on the silhouette's distance field gives
   a serviceable bevel and needs no dependency and no model. This is the one
   that should exist first, because it makes the panel, the preview light and
   the export path real before anything harder lands.
2. **Laigter**, when the artist has it, for the parameter set they already know.
3. **AI, using the reading**, for the thing neither of the others can do: a
   cheek is round, a sleeve fold is a crease, and hair is not a smooth surface.
   That distinction is the entire argument for spending a model on this, and it
   is why the reading is the prerequisite rather than a nice-to-have.

## What has to be answered before building

In `QUESTIONS.md` rather than guessed here — Q16 (is a reading stored, and what
invalidates it — **answered (c)**), Q17 (does an inking pass replace the pencils or sit on a new
layer). Both change the record, and both are cheaper to decide than to migrate.

## Order

The reading is the prerequisite for the interesting half of two features, and
the built-in normal map is the prerequisite for none of it. So:

1. Built-in normal map, the light record, and the preview — no AI, no
   dependency, and it makes the panel and the export path real.
2. Inking styles against the *existing* brush presets, with weight from the
   light only. Still no reading: a light and a silhouette already give a better
   line than a uniform one.
3. The reading — taxonomy on the character, placement per frame, MCP surface,
   and the deletion test above as its first test.
4. The parts of inking and normal maps that need to know a cheek from a sleeve.

Steps 1 and 2 ship something usable and prove the light. Step 3 is the one that
needs its questions answered first.

---

## The third normal-map tier, and why it comes last

*Added after the question "could AI analyse the subject and colourise a normal map
based on it, after Laigter, so we can leverage both paths?"*

Yes, and the framing matters: **it improves on the two earlier paths rather than
replacing either.** Tier one bevels the silhouette deterministically; tier two runs
Laigter if the artist has it; tier three takes whichever produced the **base map**,
plus the reading above, and *corrects* it.

That ordering is not politeness toward the cheaper tiers. It is what makes the
feature measurable and what makes it safe:

- **Two base paths mean something to compare against.** The same drawing refined
  from the silhouette bevel and from Laigter's output tells you how much the model
  actually contributed. Run this first and that question has no answer.
- **A better base is a smaller job.** Laigter already reads the interior lines a
  silhouette bevel cannot see, so the model is left with the part that genuinely
  needs recognition rather than the whole surface.
- **A failure degrades to a working map.** The base is not a fallback bolted on
  afterwards; it is the input, so it is still there.

### What only a model can do here

The maths knows where the edge is. It cannot know *what the region is* — and the
same silhouette bevel is wrong for all of these in different directions:

| Region | Wants |
| --- | --- |
| A cheek | a dome |
| A sleeve fold | a crease, running along the fold rather than in from the outline |
| Hair | strands, not one mass |
| A pauldron | hard edges and a flat face |
| A cloth hem | soft, and softer than the bevel would make it |

This is the case for spending a request, and it is why the reading is a
**prerequisite rather than a nice-to-have**: the taxonomy names the parts, the
placement says where they are on this frame, and the model's job reduces to
assigning each named part a shape. Without the reading it is guessing at both at
once.

### The rules it inherits, and the one it adds

Everything in `AI assistance` applies — a model never renders, two reviewers, cost
is first-class. The refinement is generated once at authoring time and **stored on
the document**; deleting it must leave the deterministic map exactly as it was.

The rule this feature adds is about the shape of its failure. A bad inbetween reads
as a wrong drawing; **a hallucinated normal reads as damage** — lighting that
contradicts the art, a face that dents under a moving light. So:

- The refinement is **blendable against the base** with a strength, not all-or-nothing.
- It is **reviewable side by side** with the base and with the other tier.
- It is **discardable without regenerating anything**.

### The problem to measure before building any of it

**Cost is per frame, not per character.** A 24-frame cycle is 24 requests unless
the reading lets one answer cover the whole cycle — and whether it can is the
central design question, not a detail. The taxonomy is per character and stable;
if a part's *shape* is also stable, then one refinement per part could be reused
across every frame that part appears in, and the cost collapses to per character.
If it cannot, this feature is expensive in a way no artist will accept on a
sequence, and that is worth knowing before the prompt is written.

And the acceptance bar, stated in advance: judged against the same sprite lit the
same way from all three paths, side by side. **If a person cannot tell the
refinement from tier one, the request was not worth making** — the
medium-simulation rule from `CLAUDE.md` applied to a model.

---

# The reading, designed against inbetweening

*Added 2026-08-07, after Q16 was answered (c) and after the prompt-drawing
feature was removed. Everything above stays true; this section decides the
parts that were open and reorders the work for one destination.*

The doc above designs the reading as a shared prerequisite for three features
and orders the work for the normal-map track. **This section takes the other
destination — a less blind inbetweener — and works out what that specifically
needs**, because the answers differ and the difference was costing an argument
every time it came up.

## The ordering, corrected for this destination

The order above is: built-in normal map and light, then inking styles, then the
reading, then the parts that need to know a cheek from a sleeve. That is right
*if* the target is normal maps and inking, because steps 1 and 2 make the light
and the export panel real.

**It is wrong if the target is inbetweening.** The inbetweener is described
above as "built, and currently blind", and neither the light record nor the
normal-map panel is a prerequisite for un-blinding it. On this track the
reading is step one, and the light is not needed at all — a light decides which
contours are heavy, which is an inking question. So:

1. **Taxonomy.** Per character, from the character sheet. One call, stored,
   editable.
2. **Measure whether it is enough.** Feed the taxonomy to the existing
   inbetweener and compare against today's output on the same keys. This is a
   gate, not a step — see *The measurement that decides the rest* below.
3. **Placement**, only if step 2 says the taxonomy alone did not close the gap.

Two of those three steps are cheap, and the expensive one is behind a gate. That
is the whole reason for splitting it this way.

## Two calls, not one

`IAiArtist` now has exactly one method, and adding to it is a decision rather
than a detail — the interface is what a reader consults to learn what the
application asks a model for. The reading wants **two** more, not one:

```csharp
Task<AiResult<SubjectTaxonomy>> ReadSubjectAsync(
    SubjectRequest request, CancellationToken ct);

Task<AiResult<IReadOnlyList<PartPlacement>>> ReadPlacementAsync(
    PlacementRequest request, CancellationToken ct);
```

They are not two flavours of one call, and folding them together would hide the
one fact the design turns on:

| | `ReadSubjectAsync` | `ReadPlacementAsync` |
| --- | --- | --- |
| Input | Reference images from the character sheet | One frame's effective strokes, plus the taxonomy |
| Cadence | Once per character | Once per frame, cache permitting |
| Output lifetime | Durable, hand-editable | Disposable |
| Where it lands | `Character.Taxonomy` in the manifest | A cache outside the document |
| Failure cost | An artist fixes it by hand | One wasted call |

Both satisfy rule 0 of the roadmap's AI section — neither starts from nothing.
The taxonomy starts from a character sheet the artist drew; the placement starts
from the frame they drew.

## The records

Core, beside the rest of the project model. Nullable everywhere it can be
absent, per *"optional means absent"*: a project with no reading writes no keys,
and `Assert.DoesNotContain("\"taxonomy\"", json)` belongs in the same commit.

```csharp
/// What this character IS. True in every frame of every animation of it.
public sealed class SubjectTaxonomy
{
    public string Kind { get; set; } = "";          // "biped", "quadruped", "prop"
    public List<SubjectPart> Parts { get; set; } = [];

    /// Set when a person edited it. A later re-read must not silently
    /// overwrite an artist's correction — the rig rule, applied to itself.
    public bool Reviewed { get; set; }
}

public sealed class SubjectPart
{
    public string Name { get; set; } = "";          // "near-arm", "torso"
    public string? Parent { get; set; }             // "near-arm" hangs off "torso"

    /// Normal depth order against siblings. The near arm is usually in front.
    public int Depth { get; set; }
}

/// Where a part IS, in one drawing. Derived, cached, never in the document.
public readonly record struct PartPlacement(
    string Part,
    IReadOnlyList<StrokePoint> Region,
    IReadOnlyList<string> Occludes);
```

`SubjectPart` deliberately carries no geometry. Geometry is placement, and a
taxonomy that held a polygon would be stale the first time the character turned
round — the exact confusion the two-halves split exists to prevent.

## Where each half lives, now that Q16 is answered

**Taxonomy — `Character.Taxonomy`, nullable.** It sits beside `Pivot`, which
already establishes the pattern: absent unless set, serialized only when
present. It survives a cache wipe, a clone and a reinstall, because the moment
an artist corrects it, it is authored data and authored data belongs in the
record.

**Placement — a cache beside the autosave, keyed by content hash.**
`%AppData%/Lightbox/readings/`, alongside `AutosaveService.AutosavePath`, keyed
by the hash of the frame's *effective* strokes — `StrokeRecordCleaner.
EffectiveStrokes`, the same view the inbetweener sends, so an erased stroke
cannot change a key without changing the drawing. Staleness then needs no
mechanism at all: a hash that does not match is a miss, and a miss costs one
call.

The reason this is not merely tidier: a placement reading is **derived from the
stroke record**, and invariant 1 says the stroke record is the document. Putting
derived data in the document is the mistake the codemap merge driver exists to
undo elsewhere in this repository. Taxonomy escapes the test because it is not
derived from any one document — it is a statement about a character, and once
edited it is the artist's.

## The measurement that decides the rest

Stated before anything is built, because it is the gate and a gate written
afterwards is a rationalisation:

> **Does the taxonomy alone measurably improve an inbetween?**

The comparison is the same two keys, the same provider, the same seed of a
prompt, run with and without the taxonomy block, judged on the failures the
inbetweener actually has — a limb that swings through the torso, a part that
vanishes because it was hidden in one key, a stroke that loses its label.

Three outcomes and each says what to do next:

- **Taxonomy alone closes most of the gap.** Then placement is a refinement, not
  a requirement, and it waits behind cheaper work. Cost per cycle stays at one
  call for the character plus what the inbetweener already spends.
- **Taxonomy helps only where placement is also present.** Then the two ship
  together, and the cache is load-bearing rather than an optimisation.
- **Neither moves the needle.** Then the blindness was not the problem, the
  finding is worth as much as a feature, and it is written here rather than
  discovered twice.

`art-director` judges "improves", `ai-engineer` judges the cost — the pair that
gate G12 requires, and this is precisely the disagreement they exist to have.

## What it costs, and the one number that matters

From `docs/DESIGN-ai-payload.md`, not re-derived: **images are ~87% of a
request's bytes and ~5% of its tokens; strokes are the reverse.**

The taxonomy request is nearly all image — a few reference views, a short
instruction, a small JSON reply. So it is **cheap in tokens and slow in
bytes**, and it happens once per character. That is the good half.

The placement request is nearly all strokes, which is the expensive half, and it
is the one the cache exists for. The arithmetic the whole design turns on:

| | Without a stored taxonomy | With one |
| --- | --- | --- |
| 24-frame cycle | 24 readings of *what this is* | 1 |
| Second animation, same character | 24 more | 0 |

**Prompt caching compounds it.** A stored taxonomy is a stable prefix, so it
belongs at the *front* of the request where `cache_control` can cover it — about
90% off the tokens it spans. A taxonomy appended after the frame data saves
nothing, and that is a real mistake to make once.

## Tests, in the order they should be written

1. **`DeletingEveryReadingChangesNoPixel`** — first, before any provider work.
   Take a finished document, delete the taxonomy and empty the cache, re-render,
   and assert byte-identical. The day it fails is the day something reads the
   analysis at render time and invariant 2 is gone.
2. **`AHandNamedPartBeatsAGuessedOne`** — a rig's hand-drawn `parts` win where
   they exist. A guess is a default, never an override of something a person
   stated.
3. **`AReviewedTaxonomyIsNotOverwrittenByAReRead`** — the same rule pointed at
   the reading's own history.
4. **`AProjectWithNoReadingWritesNoKeys`** — the absence test the camera
   established, applied here.
5. **`ARedrawnFrameMissesTheCache`** — change one stroke, assert a new key.
6. **`AReadingSurvivesLosingTheCache`** — clear the cache directory mid-session
   and assert the next request simply pays for a call, no error surfaced.

## Still open after this

- ~~**Q17**~~ — **answered (c)**: one Ink layer for the whole sequence, run over a range. Inking is unblocked.
- **What a taxonomy editor looks like.** The reading is hand-correctable by
  design, and "hand-correctable" without a UI means "hand-correctable by editing
  JSON". Not designed here, and it is not a prerequisite for the measurement —
  but it is a prerequisite for calling the feature finished.
- **Whether the MCP surface exposes the reading.** An agent that could ask
  *what am I looking at* would use it, and the tools are already guarded and
  undoable. Deferred with the rest of the MCP scope question rather than
  answered in passing here.
