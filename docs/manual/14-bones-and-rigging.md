# Bones and rigging

A **rig** is a skeleton under your drawing: bones you pose instead of redrawing
a limb from scratch. Lightbox's rig is different from the puppet tools' in one
way that matters — a posed line is **re-drawn along its bent path**, not
warped like a picture on a rubber sheet, so line weight and brush character
survive any pose. And because every dab's scatter and jitter are seeded from
the drawing's rest position, a rigged character **does not boil** as it moves.

A document that never rigs pays nothing: no keys in the file, no rig controls
in the way.

## The Bone tool

Its panel in **Tool options** carries everything the tool can do, starting
with one switch of three positions — **Bind**, **Pose**, **Weights**. That
switch is how you get to weight painting; the three are exclusive, because
weights are painted against the rest pose, so arming the brush leaves posing.

The panel also carries: which mode you are in,
every bone in the rig, and the weight brush with its settings. The pointer
says what a press would do before you make it — a **move** cursor over a
bone you can shift, a **turn** cursor where a drag would rotate it, and a
**crosshair** on empty canvas where a drag would start a new bone.


The Bone tool (**K**) is always in the toolbar, even before a rig exists —
because its first drag is what *creates* the rig.

- **Drag on empty canvas** to add a bone, from where you press to where you
  release. If a bone is selected, the new one is its child; the joint follows
  the parent from then on.
- **Click** a bone to select it. Tips beat origins, and the selected bone wins
  a contested press, so you can work a joint without the neighbour stealing it.
- **Drag a bone's shaft** to move it bodily — children come with it. **Drag
  a tip** to re-aim and re-length it; **drag an origin** to move just that
  joint. All three edit the skeleton's rest, and each is one undo step.
- **Shift-drag a bone's tip** to grow a child out of it, **glued** to the
  parent's tip — the fastest way to build a limb, one bone at a time. Glued
  means the joint follows: re-length the upper arm and the forearm comes with
  it instead of leaving a gap. Dragging the child's own origin away unglues it
  again, and it stays where you put it from then on. **Add child** in the
  options panel does the same without aiming, and **Length** sets a bone's
  size by number instead of by drag.
- **Rename** a bone in the options bar — a pair ending `.l` and `.r` is what
  X-symmetry reads. **Delete** removes it and re-parents its children to its
  parent, leaving them exactly where they are; strokes bound to it lose that
  binding.

## Posing

Switch to **posing** (**Shift+K**) and dragging a bone rotates it instead of
editing it. The pose is keyed **at the playhead automatically** — pose the arm
on frame 8 and a pose key lands on frame 8, interpolating from and to the keys
either side, with the frames between showing the blend. Scrub the timeline and
bound drawings follow the pose live, in playback and in every export.

A pose never touches your lines. It decides where they are *drawn*; the record
keeps them at rest, so un-keying a pose returns exactly the drawing you made.

**Baking** writes the pose into the drawing: bound strokes become ordinary
posed strokes, cut loose from the rig. What you bake is what you saw — the
live view and the baked result are pixel-identical.

## IK — reaching instead of turning

Turning a shoulder and then an elbow to put a hand somewhere is arithmetic an
artist should not have to do. **IK** inverts it: you place the hand and the
arm works out its own angles.

Select the bone at the end of the limb and press **Add IK**. That makes the
whole thing in one go — a **handle**, drawn amber so it is not mistaken for a
bone, sitting at the limb's tip. Switch to **Pose**, drag the handle, and the
limb reaches after it. The handle is an ordinary bone, so it keys on the
playhead like everything else, and parenting it to a prop or a ground bone is
how a foot stays planted or a hand stays on a sword.

- **IK bones** is how far up the limb the reach goes. Two is an arm or a leg;
  more is a tail or a neck.
- **Pole** picks a bone that says which way the elbow or knee bends. Without
  one the limb keeps whichever way it is already bent, which is usually what
  you want and occasionally is not.
- Dragging *any* bone of the limb in Pose moves the handle, because the limb's
  angles belong to the solver — you are always really moving the hand.
- **Remove IK** deletes the chain and its handle. The bones stay exactly where
  they are and go back to being turned by hand.
- Out of reach, the limb points straight at the handle and stops — it stretches
  no further than it is long.

IK is **posing**, never the rest: switch back to **Bind** and the skeleton is
the one you built, so re-lengthening a bone under a chain does what you asked
rather than being pulled back by the solver.

## Binding drawings to bones

A stroke follows the rig once it has **weights** — how much each bone moves
each part of it.

- **Assign to bone**: select strokes, and assign them wholly to the selected
  bone. This is the cutout workflow — each body part follows one bone — and it
  covers most rigs.
- **Auto-bind**: weight the selected strokes against the whole skeleton by
  distance. It gets a character most of the way; the last stretch — armpits,
  hips, anywhere two bones share one drawing — is what the heat view and the
  weight brush are for.
- The **heat view** shows the selected bone's influence over the current
  drawing, blue (none) through red (owned), while the Bone tool is active.

- The **weight brush** (**Ctrl+Shift+K** while the Bone tool is active)
  paints influence for the selected bone directly on the canvas: pressure
  drives strength, weights normalise themselves (painting one bone up takes
  the others down, a locked bone holds), and a whole brush stroke is one
  undo step. With **X-symmetry** on, painting one side of a named pair
  (`hip.l` / `hip.r`) paints the other side too, mirrored across the pair's
  own axis — the character's spine, wherever it sits on the paper. Painting
  happens against the rest pose; scrub a pose to check, come back to
  correct.

*Planned:* painting weights under a live pose, aim and copy constraints,
spline chains and rig export (`docs/DESIGN-bones.md` has the whole plan).
