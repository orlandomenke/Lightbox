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

Below the switch, the panel lists **every bone in the rig**, children indented
under their parents. Picking one there selects it on the canvas — the same
selection a click on the canvas makes — and the selected bone draws **white**
where the others draw green, the same colour selection wears on every overlay.
The rest of the panel is what you can do to the picked bone (rename, length,
add child, delete, IK, spline, constraints), the weight brush while it is
armed, and the binding actions. The pointer
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
- **Rename** a bone in the options panel — a pair ending `.l` and `.r` is what
  X-symmetry reads. **Delete** (the button, or the **Delete** key while the
  Bone tool is in hand) removes it and re-parents its children to its
  parent, leaving them exactly where they are; strokes bound to it lose that
  binding.

**Every one of these drags shows its result while you drag.** The bone you
are creating, moving, re-aiming or extruding follows the pointer, computed by
the same code the release runs — so where the drag shows it is where letting
go puts it, and the whole gesture is still a single undo step.

## Posing

Switch to **posing** (**Shift+K**) and dragging a bone rotates it instead of
editing it. The pose is keyed **at the playhead automatically** — pose the arm
on frame 8 and a pose key lands on frame 8, interpolating from and to the keys
either side, with the frames between showing the blend. Scrub the timeline and
bound drawings follow the pose live, in playback and in every export.

**The drawing follows the drag, not just the bones.** Bound strokes re-render
through the provisional pose as you drag, exactly — the same render the
release lands, so nothing settles or shifts on pen-up. The one exception is a
stroke whose brush carries the expensive badge (a simulated medium, smudge or
blur, layer sampling): it draws as a thin ghost of its own centreline during
the drag and lands exactly when you release. Nothing is keyed until you let go —
and a drag the window loses (focus stolen mid-gesture) is abandoned, the
drawing snapping back to its keyed pose.

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

## Constraints — one bone following another

A **constraint** makes a bone follow another bone, so you pose one thing and
several move. Eyes that track a target, a head that looks where a hand goes,
a prop that stays in a fist.

Select the bone that should follow, and pick what it follows from
**constrain to…**. That makes it in one go — an **aim**, because an aim reads
instantly: the bone visibly turns to look. Then set what kind of following it
actually is:

- **Aim at** — turn to point at the target.
- **Copy turn of** — take the target's angle, wherever it is.
- **Copy place of** — sit where the target sits, keeping your own angle.

Stack **Copy turn** and **Copy place** on the same bone and it follows the
target completely. There is no tick-box constraint that does everything,
because most of the combinations are never used and every one of them would
sit in your file at *off*.

- **Offset** is degrees added after the constraint resolves — a head that
  should look slightly past what it aims at.
- **Strength** is how far toward the constrained result the bone goes. At
  **100%** the constraint owns the bone's turn, so posing it by hand does
  nothing; lower it and your pose and the constraint blend. At **0%** the
  bone is exactly where you put it.
- Constraints resolve **top to bottom**, after IK. A later one sees what an
  earlier one did, and a constraint can override a bone the IK solver just
  placed.
- A bone cannot follow itself or anything hanging off it — there would be no
  answer — so those bones are simply not in the list.
- Like IK, constraints are **posing**. Switch to **Bind** and the skeleton is
  the one you built.

## Spline chains — tails, hair and capes

Some things are not posed joint by joint. A tail, a plait, a cape, an
antenna: what you want is a *shape*, and the bones should follow it.

Select the bone at the end of the run and press **Add spline**. Three amber
handles appear along it, laid on the shape you already drew — so nothing
moves until you move something. Switch to **Pose** and drag a handle; the run
curves to follow.

- **Spline bones** is how many bones up from the end lie on the curve.
- The curve passes **through** every handle, not near it. Where you put a
  handle is where the tail goes.
- Every bone keeps **its own length**. A spline bends the run; it never
  stretches it, so the drawing bound to it is never scaled and its grain
  never re-rolls.
- If the run is longer than the curve, the rest of it trails straight on
  rather than bunching up at the end.
- Dragging any bone of the run moves the nearest handle, because the run's
  angles belong to the curve.
- **Remove spline** deletes the curve and its handles, unless something else
  is using one.

A tail that whips is three handles keyed over four frames — the handles are
ordinary bones, so they key, parent and drag like everything else in the rig.

## Binding drawings to bones

A stroke follows the rig once it has **weights** — how much each bone moves
each part of it.

- **A whole layer at once** — right-click the layer and pick **Follows the
  rig**. That covers every stroke on it, on every frame, including ones you
  draw later, and it is what you want for a character you are about to animate
  rather than a single illustration. **Link** the character's layers (lines,
  colour, details) and rigging one rigs them all — see *Layers*. Nothing is
  written onto your lines: the layer's binding is read when the drawing is
  drawn, so linking a layer moves the drawings that were already on it.
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

*Planned:* painting weights under a live pose, angle-driven corrective
shapes, secondary motion and rig export (`docs/DESIGN-bones.md` has the
whole plan).
