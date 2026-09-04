# Q181 · How do guide sets and rigs travel between documents? — **answered by the owner, 2026-09-04**

Raised by the owner, 2026-09-04: *"When I create a guide set I want the guides
to be opened on the relative position on a new document… This way heights stay
the same throughout a project or multiple files"*, and then the same wish
pointed at armatures: *"the saved bones can be scaled relative to the document
we are opening. Though this should be optional."*

What it blocks: whether `PullGuideSet` transforms what it copies, whether there
is a rig library at all, and what unit either of them travels in.

## Where the two systems were when it was asked

**Guide sets travel badly.** [[Q30]]'s scoped resources gave `GuideSet` a home in
the manifest and `GuideScopes.VisibleTo` a resolver, and `PullGuideSet` copies
the guides **verbatim in document pixels**. Author a six-head chart on 4K, pull
it into 1080p, and it is four times too tall with its anchor off the canvas.

The *preference* half already knew better: `AppSettings.HeightScaleFill` stores
0.7 of the canvas rather than a head height in pixels, so a newly added height
scale lands as a figure on a canvas of any size. The library half never learned
the same trick, and that asymmetry is the whole defect.

**Rigs cannot travel at all.** `Doc.Armature` is one armature per document and
there is no manifest key, no scope kind and nothing to pull from. `ArmatureOps.Solve`
already returns world placements, so *measuring* a rig costs nothing; there is
simply nowhere to keep one.

## The answer: one currency, and the height scale converts into it

Two systems, two notions of "relative" would have been the wrong shape. There is
one:

- A **guide set** travels as a fraction of canvas height, because a guide's job
  is framing.
- A **rig** travels in **heads** — the human is 7.5, the dog is 3, the goblin is
  4.5 — because a rig's job is proportion.
- The **height scale on the receiving document** converts heads into pixels.

`GuideKind.HeightScale` turns out to be built for it: `(X, Y)` is already the
*bottom* — the ground the character stands on — and `Spacing` is already one
head. A rig lands feet-on-anchor at `heads × Spacing` and is correct on any
canvas at any resolution *without being told the resolution*. That is stronger
than "scale to the document", and it is what actually keeps the goblin shorter
than the human across twelve files.

## The four decisions

**1. A rig's default size is its head count, falling back to the canvas.** The
rig records how many heads tall it was when saved, measured against the height
scale present. Landing on a document that has a height scale: `heads × Spacing`,
feet on the anchor. No height scale there: canvas fraction, the guide-set rule.
*Original pixels* stays on the menu and has to, because the goblin being short
is data rather than an accident to normalise away — that is the "optional" the
owner asked for. A rig saved on a document with no height scale has no head
count, writes no key, and can only offer the other two.

**2. Two landings, chosen at pull time.** "Use as armature" becomes
`Doc.Armature` — one only, posable, bindable. "Place as proportion guide" is a
ghost drawn and snapped like a guide, any number of them, not posable and not
bound. One library record with two ways to land, not two ways to save a rig.
The forcing argument is that `Doc.Armature` is singular, so a size-comparison
sheet — human, dog and goblin standing together — is *only* expressible as the
second.

**3. A guide set scoped on a folder applies when a document is created in it.**
"Heights stay the same throughout a project" means the new drawing in the knight
folder opens with the knight's chart already on it, scaled to its canvas. The
cost is accepted knowingly: the document has content before the artist touched
it, so its initial state is not empty.

**4. Sets reach a second project as a file.** Guide sets and rig sets stay in the
project manifest; an export/import pair moves the knight into the sequel or to
another artist. A machine-wide library was rejected for a specific reason — it
would make a project stop describing itself completely, so opening it elsewhere
would quietly lose guides that looked like part of it.

## The two things that would break this

**Non-uniform scale corrupts three guide kinds silently.** Scaling x by the width
ratio and y by the height ratio would tilt every `Line`, stop an `Isometric`
being isometric, and make a `Grid` non-square — to make one kind fit. One
uniform factor, taken from height; positions by fraction of each axis. Where the
aspect matches, which is the owner's case, all of it agrees exactly.

**Rescaling a bound armature boils the character.** `docs/DESIGN-bones.md`'s "one
trap" is that the bind pose is the coordinate space dab dynamics seed from, so
changing it after strokes are bound re-rolls every dab. Scaling at pull time is
an authoring act on a rig nothing is bound to yet and is safe; a rescale after
binding must be refused, or must rebind. Note this is *not* invariant 7 — that
governs render, and forbids multiplying stroke coordinates to render bigger.
Nothing here multiplies a stroke at all.

**Nothing here is blocked.** The rig landed in full — armature, bone tool, pose
keys, IK, constraints, correctives, layer links — so there is something to put
in a library today; only the library is missing.

**Compatibility falls out of absence.** A set saved before this exists carries no
authored-canvas key and pulls exactly as it does today, which is the
`optional-settings` rule doing the migration for free.
