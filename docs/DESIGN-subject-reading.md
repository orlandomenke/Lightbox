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
invalidates it), Q17 (does an inking pass replace the pencils or sit on a new
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
