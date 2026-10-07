# Q197 · Drawing on a hold: blank page by default, copy as an option — **answered 2026-10-06: blank by default, the copy kept as a choice**

**Answered:** a mark on a held cel keys a **blank** drawing by default — the
next sheet of paper, with the onion skin as the light table showing the drawing
before. *Start from a copy* (what the default used to do) stays available as a
third choice of the same *Drawing on a held cel* setting, beside *Edit the held
drawing*. The recommendation was taken.

**The owner's request:** *"A hold or blank frame, if drawn on it it removes the
hold and draws a fresh frame."*

**The cost, accepted by the owner:** with onion skin off, the held drawing
disappears from that frame under the first mark, because the frame no longer
holds it. The copy was the default precisely because "a cel that went blank
under the first touch read as the app losing the drawing"; that reading is now
the onion skin's job, and an artist who works by altering the last drawing
picks the copy.

**What it reverses:** B204, which made the copy the default on an earlier
request ("keying must not change the picture"). B204's fix is kept whole as the
*Start from a copy* choice and its evidence test now runs under that choice;
the entry in `BUGS.md` says so. Drawing past the end of the scene (Q103) keys a
hold too, so it now starts blank — which matches what the canvas shows out
there, so the mark is still the only visible difference.

## What "a mark" covers, and what it does not

The setting governs **marks** — brush, eraser, shape, gradient, pen, new
type, filling a selection (and Backspace's background fill), placing a
symbol. An **edit of what the hold shows** keys a copy whatever the
setting says, because on a blank page it would have nothing to act on:

| Path | Keys | Why |
| --- | --- | --- |
| Selected-line edits (B207, `KeyHeldCelForStrokeEdit`) — move, nudge, recolour, delete, path reshape | copy | the ids being edited only exist on the copy |
| Move / transform (B206, `KeyHeldCelForCommit`) | copy | moving nothing is not a move |
| Placement drag (`Symbols` drag) | copy | placement ids survive the copy, which is how the drag keeps its grip |
| Cut / copy lines, make symbol from selection (`SelectedStrokesForAnOperation`) | copy | takes strokes from what is shown |
| Delete on a marquee (`ClearRegion`) | copy | the same act as a cut; on a blank page it would clear nothing and empty the frame |
| Clicking into held type to retype it | copy | otherwise there is no type under the click |
| The bucket (`FillAtInternal`) | copy | **author's call, needs the owner's confirmation** — see below |
| Blur and smudge brushes | copy | they rework the pixels already there; on a blank page they do nothing and the key stays |
| Any brush on an alpha-locked layer | copy | alpha lock paints only over existing paint |
| Posing a rig on a hold (Q120, `Armature`) | copy | already used `KeyedCopyOf` directly, never the setting — unchanged |
| *Edit the held drawing* | no key | unchanged, and still governs the editing tools too |

## The bucket: an implementation call the owner should confirm

Not raised with the owner — found in review after the decision. A flood fill
takes its boundaries from the drawing it lands on, so on a blank page a click
inside a held character's shirt floods the whole frame; sampling the held
drawing for the boundaries and writing onto the blank page instead fills the
right shape but leaves a coloured silhouette with no lines on that frame. The
bucket's ordinary use on a hold is colouring held line art, which wants the
lines and the colour on this frame, so it **keys a copy** like the other edits
of what the hold shows. The alternative (boundaries from the hold, fill onto
blank) is a few lines if the owner prefers it. Filling a *selection* is not a
flood and stays a mark.

## The pen keys at the finish, not the press

The pen used to key on its first node, though the nodes are an overlay and not
the record. A path parked by a tool switch, abandoned, or left at one node kept
that key — a stray copy before, an emptied frame under the blank default — so
the pen now keys only when `FinishPen` writes the line.

## Two reads that used to key, and now do not

`GetCurrentFramePlacements` (asked by the canvas while drawing selection
chrome and hit-testing a press) and Select All with the Arrow both went through
`PaintTargetOrKey`, so they keyed a held cel. Under a copy that was a stray
drawing on the timeline; under a blank default it would have emptied the frame
on screen just for having a selection. Both now read `PaintTarget` — picking
authors nothing, B207's rule.

## Gestures that key and then decline hand the key back

B236 already took the key back for an eraser that erased nothing, which is
exactly what an eraser on a blank page does, so **an eraser on a hold under the
default records nothing and leaves the hold**. The same rule now covers every
tool that keys at the press and can then decline: a shape or gradient click with
no drag, a cancelled gradient, a fill that found nothing, type with no letters,
a stroke cut off by playback. A pending palette edit is now committed *before*
the key rather than after it, because `DiscardStep` only takes the newest step
and a swatch edit landing on top of the key would have pinned it there. With a copy those left an invisible
stray drawing; with a blank page they would have emptied the frame.

## The stored setting

`AppSettings` serializes the whole object, defaults included, so every settings
file written before this says `"DrawingOnAHold": "StartANewDrawing"` — and a
choice cannot be told from a default. It was the default, so it is read as **no
choice made** and migrated on load to `StartABlankDrawing`, the behaviour the
owner asked for. The cost lands on an artist who had deliberately chosen the old
copy behaviour (impossible to distinguish); the choice is one combo box away,
renamed *Start from a copy*, and the manual says so. `EditTheHeldDrawing` loads
as itself. Parsing is by exact name; anything else (a typo, a number, a later
version's value) reads as the default.

**Blocks:** nothing.
