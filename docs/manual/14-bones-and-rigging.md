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
switch is how you get to weight painting; the three are exclusive — a
Weights drag paints influence, it never turns a bone.

Below the switch, the panel lists **every bone in the rig**, children indented
under their parents. Picking one there selects it on the canvas — the same
selection a click on the canvas makes — and the selected bone draws **white**
where the others draw green, the same colour selection wears on every overlay.
Every bone and handle sits on a dark rim, so the chrome reads on white paper
and dark canvases alike.
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
- **A dashed line tethers every separated joint to its parent's tip**, so
  hierarchy stays visible when bones stand apart: a glued joint reads as
  touching, a parented-but-offset one reads as tethered, and a bone with no
  line to anywhere is a root. The tether follows live while you drag, and it
  answers what you are looking at — a glued joint keyed apart by a pose
  translation shows the tether too, because right now it *is* apart.
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

Switch to **posing** (**Shift+K**) and the same drags edit the *pose* instead
of the skeleton. **Which part of the bone you take hold of decides what the
drag does, and it means the same thing in both modes:**

| Grab | Posing | Binding |
| --- | --- | --- |
| The **tip** handle | aims the bone | aims it, and sets its length |
| The **shaft** | carries the bone, and everything parented to it | moves it in the skeleton |
| The **joint** handle | puts the joint under the pointer | moves the rest joint |

So **take a root bone by its shaft to move the whole character** — children
ride their parent, so one drag carries the skeleton. The pointer says which of
the two you are about to get before you press: a move cursor on the shaft and
the joint, a turn cursor on the tip.

The pose is keyed **at the playhead automatically** — pose the arm
on frame 8 and a pose key lands on frame 8, interpolating from and to the keys
either side, with the frames between showing the blend. Scrub the timeline and
bound drawings follow the pose live, in playback and in every export.

**The skeleton has its own onion skin.** With onion skin on, posing also
shows outline ghosts of the skeleton at the neighbouring **pose keys** — warm
behind the playhead, cool ahead, the same colours the drawing's ghosts wear —
so an inbetween pose is judged against where it came from and where it goes.
The onion bar's switch and depths drive it, and a ghost is never grabbed: a
press through one lands on the real skeleton or the canvas.

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

### Keeping a pose as a drawing

Posing writes a pose key and nothing else. That is what makes trying a pose
free — but on a long exposure it means the frames you posed still show the one
drawing the cel is holding, and playback shows the drawings you made rather
than the poses you authored.

**Drawing from pose** is the other half. Park on a held frame, pose the rig,
and use it: the hold breaks and that frame becomes a drawing of its own,
holding what you were looking at. Three ways to reach it, all the same command:

- the **bone options**, which acts on the frame you are standing on;
- **right-click a cel in the X-sheet**, which acts on the cel you clicked and
  then takes you to it — the way to work along a row without moving the
  playhead first. It only appears once the document has an armature;
- a key of your own, bound under *Keep this pose as a drawing* — worth doing
  for a cycle, where you press it once per drawing.

- **Bound art** arrives baked into its posed position, ready to touch up.
- **A bone guide over hand-drawn art** — nothing bound — arrives as a copy of
  the drawing the cel was holding, with the posed skeleton showing through it
  to redraw over. This is the frame-by-frame way to use bones: block the
  action out with the skeleton, then commit one drawing per frame you want.

Only the frame you are on becomes a drawing; the cels either side keep
holding what they held. Press it on a frame that already has a drawing of its
own and nothing is inserted — it bakes the pose into that drawing instead,
which is what **Bake pose** does.

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

## Joint fixes — drawing the shape a bend should have

Bend a joint far enough and the skinning goes wrong: the inside of the elbow
collapses, the outside pinches. That is not a weights problem — the correct
shape at 120° is not a blend of anything — so you **draw it**.

Bend the joint to where it looks wrong. Select the bone, and in **Joint fixes**
press **Draw a fix here**. The drawing changes to its posed self, and every
tool works on it normally: move points, reshape lines, whatever fixes it. Then
press **Keep the fix**.

From then on, the fix **eases in** as that joint approaches that angle, and
holds past it. At rest the drawing is exactly as you made it.

- Bend further and draw again for a **second stop**; the shape ramps between
  them. Drawing at an angle you have already used replaces that stop.
- The second capture starts from the first fix already applied, so you are
  always correcting what you can see.
- **Discard** puts the drawing back and keeps nothing. Leaving Pose mode does
  the same.
- Your lines are never changed. A fix decides where marks are *drawn*, the
  same way a pose does, so removing it returns the drawing untouched.
- Add or delete lines during a capture and those lines are skipped — the fix
  can only describe lines that were there when it started.

A fix belongs to **the drawing**, so it works for the cutout way of animating,
where a limb is one drawing you reuse across the whole sequence. It is not a
tool for correcting two hundred hand-drawn frames.

## Jiggle — secondary motion

Some motion nobody should have to key: the tip of a tail arriving late, ears
that carry on after the head stops, an antenna that will not sit still. Tick
**Jiggle** on a bone and it follows the motion driving it through a spring —
lagging behind, swinging past, settling.

- **Catch up** is how hard the bone is pulled toward where the pose wants
  it. Low is a whippy antenna; high follows almost immediately.
- **Settle** is how quickly the swing dies. Low keeps it bouncing; high is a
  heavy tail that lands at once.
- The swing is computed **from the pose track, one step per frame** — never
  from the clock — so the same document renders the same swing on every
  machine, every export, forever. Scrubbing backwards replays it exactly.
- Children ride the swing, so one jiggled bone at the base of a chain moves
  everything after it. Jiggle belongs on bones you turn by hand — a bone
  driven by IK, a spline or a constraint gets its swing overwritten by the
  solver, the same way hand-posing one does nothing.
- Keys never record the swing: what you author is the pose, and the jiggle
  is how the frames between and after breathe. **Baking** writes what you
  see, swing included.

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
  drawing, blue (none) through red (owned), while the **weight brush is
  armed** — in Bind and Pose the ink stays clean. The dots sit on the
  drawing **as it is posed at the playhead**, so what you see is what you
  would paint.

- The **weight brush** (**Ctrl+Shift+K** while the Bone tool is active)
  paints influence for the selected bone directly on the canvas: pressure
  drives strength (a mouse paints at full strength, like every brush),
  weights normalise themselves (painting one bone up takes the others down,
  a locked bone holds), and a whole brush stroke is one undo step.
  **Red means the selected bone owns those points** — it is not a warning
  colour. To *exclude* a region from moving, do the opposite of painting it
  red: select the bone that currently owns it and paint there with
  **Subtract**. What no bone claims holds still at rest.
  The brush ring wears a **+** or **−** beside it saying which way the next
  stroke will paint, and the modes have their own keys: **Shift+1** arms
  Add, **Shift+2** Subtract, **Shift+3** Smooth — from any tool, in one
  press. The ring is the weight brush's own radius, whatever the paint
  brush is set to. Points painted
  part-way (purple, between blue and red) land between their rest and posed
  positions, which is also the answer to a line that seems to twist in
  depth as a bone turns: push its weights to fully red or fully blue and it
  moves rigidly or not at all. With **X-symmetry** on, painting one side of a named pair
  (`hip.l` / `hip.r`) paints the other side too, mirrored across the pair's
  own axis — the character's spine, wherever it sits on the paper, and the
  mirrored dab lands on the other limb **wherever its own pose put it**.
  You can paint at any frame: the brush works on the drawing you are
  looking at — pose the arm to where the armpit goes wrong, and fix the
  weights right there. The weights themselves are still stored against the
  rest drawing, so nothing about a pose changes what a weight means.
  **While the brush is armed, nothing moves**: the dots change colour dab
  by dab, and the drawing holds still — painting edits how much a bone
  influences a line, it never pushes the line around under your hand. The
  drawing takes up its corrected deformation the moment you switch back to
  Bind or Pose.

**Exporting the rig**: the Godot export writes a `Skeleton2D` importer beside
the sheet when the document is rigged, and the **Rig + DragonBones** format
exports the skeleton file for engines with a DragonBones importer — see
*Exporting to a game engine*. All six phases of `docs/DESIGN-bones.md` are
built.
