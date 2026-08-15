# Q86 · Bones phase 3: connected bones, the IK target, and how the phase lands — **answered 2026-08-14, all three as recommended**

Prompted before starting phase 3 of `docs/DESIGN-bones.md`, with phases 1
and 2 landed.

1. **Connected bones get a flag.** `Bone.Connected`, nullable so an
   unconnected bone writes no key. An extruded child sets it and the solve
   places it at the parent's tip, so re-lengthening a parent drags the chain
   the way Blender does. The alternative — extrude places the child at the
   tip and nothing glues it — left a gap to close by hand every time a limb
   was re-proportioned, which is exactly the kind of tidying that makes a rig
   feel unfinished. Costs accepted: a record field, a branch in the solve, and
   it has to survive serialization and image resize like every other
   coordinate-adjacent key.
2. **An IK chain aims at a target bone**, Spine's and Blender's model, rather
   than a bare point keyed in the pose track. The decisive argument is that a
   bone already poses, keys and interpolates through machinery that exists:
   no second kind of keyframe, no second overlay, and the handle is visible
   and grabbable like everything else. A keyed point would have been less to
   author for a two-bone limb and could not be parented to anything, which is
   what an animator wants the moment the character walks.
3. **IK lands on its own branch**, before aim/copy constraints and spline
   chains. It is the piece an animator feels immediately, and constraints
   layer onto a solved pose rather than the reverse. Doing all three at once
   would match the doc's phase boundary and produce one diff touching the
   solve three ways, which is where a determinism bug is hardest to localise.
