# Q144 · How does an anchor carry direction? — **answered 2026-08-21: a nullable angle on the placement**

Asked alongside Q143, which needs it: an attachment that only knows *where* the
hand is draws the sword at one fixed rotation through the whole swing. A socket
that also says *which way* it points is what lets the armor turn with the limb
— and it is what a game engine wants from an exported socket anyway (Unity's
attachment transforms carry rotation; ours export position only).

| | What it costs |
| --- | --- |
| **An angle on the placement** (recommended, **chosen**) | One nullable field on `AnchorPoint` (degrees, per frame, like the position), a rotation stalk in the rig overlay, a line in the sidecar. Null means "no direction", so a document whose anchors never turn serializes exactly as today — the absent-until-used rule, same as the camera. |
| **A two-point anchor (base + tip)** | More visual, and the length could scale the attachment — but it doubles what an animator places on every frame of every cycle, complicates push-across, and the length-scaling it enables is a feature nobody asked for yet. |

Per-frame like the position, and on the frame rather than against an index, for
the reason `Anchor`'s own remarks give: a hold, a re-time and a cel drag move
drawings around the sheet, and direction stored on the drawing travels with it
for free.

None of this reaches a pixel in the base document — like the anchor itself it
is authored data that leaves through the exporter — until Q143's attachments
arrive, at which point the angle rotates a *symbol placement*, which renders
through the ordinary symbol path. Invariant 2 is safe: an angle is authored,
never rolled.

## What this did not answer

Whether the rig overlay's push-across copies the angle with the position
(almost certainly yes — they are one placement), and whether the exporter emits
degrees or radians (whatever the sidecar's existing conventions say; decide in
the branch that builds it, against `docs/manual/11-exporting-to-a-game-engine.md`).
