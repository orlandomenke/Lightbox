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

Its options bar carries everything the tool can do: which mode you are in,
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
- **Shift-drag a bone's tip** to grow a child out of it, joined to the
  parent — the fastest way to build a limb, one bone at a time. **Add child**
  in the options panel does the same without aiming, and **Length** sets a
  bone's size by number instead of by drag.
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

*Planned:* painting weights under a live pose, IK, spline chains and rig
export (`docs/DESIGN-bones.md` has the whole plan).
