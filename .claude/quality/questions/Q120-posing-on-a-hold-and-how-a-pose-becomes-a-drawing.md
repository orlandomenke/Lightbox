# Q120 · Posing on a hold, and how a pose becomes a drawing — **answered 2026-08-18: never automatic, and one command that commits what you see**

The other half of the owner's report of 2026-08-18, and the more interesting
half, because the observation was right and the diagnosis it invites is wrong:

> *"Posing the image/armature on a hold frame does not insert a frame … I am
> using the bone system as a guide to create a run animation; although the bones
> do move between all the frames, on playback I only see 2 images that have a
> keyframe. So either posing the armature should insert a keyframe on the layer,
> or the image should respond to the bones being posed on playback."*

**The image already responds** — that is Q81's live rigged render, and it is
keyed per timeline position, so a held drawing on a rigged layer deforms
differently on every frame of the hold (`LiveRigRenderTests` pins exactly that).
It only applies to strokes that are *bound*. The owner was using the rig the
other way, as a construction armature under hand-drawn art, where nothing is
bound and nothing should deform. Two drawings, fifteen poses, two images on
playback: everything working as designed, and the workflow still stuck.

What was missing is the sentence *"and this one is a drawing"*.

| | What it costs |
| --- | --- |
| **Never automatic; an explicit command** (recommended, **chosen**) | One more thing to press. Posing stays free of consequence, which is what makes trying a pose cheap. |
| Posing on a hold inserts a drawing | Makes an exploratory drag destructive, multiplies drawings across a hold being scrubbed through, and contradicts how rig-mark dragging on a hold already behaves (`DraggingWhileParkedOnAHoldEditsTheDrawingBeingHeld`). |

And what the inserted drawing holds:

| | What it costs |
| --- | --- |
| **The posed drawing, baked** (recommended, **chosen**) | For a guide rig it is a copy to erase over rather than a clean sheet. |
| A blank cel | The truest frame-by-frame start, and it throws away the deformation the rig just computed — useless for a bound character. |
| Ask each time | Covers both, at the price of a dialog in the middle of an animation pass, on a command pressed once per drawing across a cycle. |

## Why one command covers two workflows

Because it commits **what is on screen** rather than a category. `Skinning`
already decides per stroke whether the rig moves it; the command bakes through
that same funnel, so:

- **bound art** arrives deformed into its posed position, ready to touch up;
- **a bone guide over hand-drawn art** arrives as a copy of the drawing the cel
  was holding, with the posed skeleton showing through to redraw over.

Neither branch is written down anywhere in the command — it is one call to
`Skinning.BakeFrame`, which returns zero for the second case and changes
nothing. A cel that already has a drawing of its own is not duplicated; there
the command is `BakePoseHere` and says so.

`InsertDrawingFromPose`, one editor step for the key and the bake together —
one press, one undo. Guarded by `PoseToDrawingTests`. Surfaced as *Drawing from
pose* in the bone options and as `armature.insertPoseDrawing` in `ShortcutMap`
with no default gesture, so a cycle can be worked with it under a key.
