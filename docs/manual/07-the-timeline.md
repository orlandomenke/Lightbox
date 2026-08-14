# The timeline


## The timeline family

Three views over the same animation share the bottom panel as tabs, and
nothing you do in one is invisible in another:

| | |
| --- | --- |
| **Timeline** | One coloured track per layer: drawings are dots, holds are the bars behind them, the camera is its own orange track on top, and the scratch track's waveform is its own band underneath. **Drag a dot** to retime that drawing; click anywhere else to scrub. |
| **X-sheet** | The exposure sheet — the grid described below, where cels are edited, exposed, re-timed and annotated. |
| **Graph editor** | Value over time for the things that interpolate: the camera's position, zoom and rotation (drag a key dot — up and down for value, sideways to retime; a chip shows the value as you drag), and the **measured spacing** of your drawings — how far the ink actually moves between poses, the spacing chart read off the art itself. Even spacing is constant speed; widening is an ease; a spike is the drawing that pops. **Double-click** the plot to key the camera's framing at that frame (it keys what is already there, so nothing jumps — then drag it). **Right-click a key** for its easing into the next key, and to remove it. The **legend** on the bar toggles each curve, and its swatch says which colour is whose; the dashed **Spacing (intended)** curve is the same travel redistributed by the easing picked on the X-sheet bar — where the hollow dots and the filled ones disagree is the drawing that misses the ease. Spacing curves read the active layer. With one curve showing, the axis carries its numbers. |

## The X-sheet

One row per layer, one cell per frame. Click a cell to go there; the current one
is highlighted. A **keyed** cell holds a drawing; a **hold** repeats the drawing
before it, which is what animating on 2s and 3s is made of.

**Two kinds of cell look empty, and they are not the same thing.** A plain empty
cell is inside the scene: the playhead goes there, you can select it, and a mark
on it keys a drawing. A **hatched** cell is past the end of the scene — there is
no frame there at all, so it cannot be clicked into or drawn on, and the playhead
never reaches it. Extend the scene and the hatching gives way to real cells.

## The scratch track

**Audio is optional and absent until you add it** — the same rule the camera
follows. **♪ Add audio** on the Timeline docker's bar imports a WAV file, and
the timeline grows an **Audio** band: one bar per frame, tall where the sound
is loud, so a beat or a syllable can be found by eye and a drawing timed onto
it.

On import you choose **where the sound lives**:

- **Reference the file** (the default): the document stores where the file is
  (relative to the document when it lives nearby, so a project folder moves
  as one thing). Keep editing it in your audio tool — the timeline reads the
  file as it is on disk. If the file goes missing the bar shows a *missing*
  badge and the track waits, silently, for it to come back; your timing is
  never lost with it.
- **Embed a copy**: the sound travels inside the document, which then
  survives being shared without the WAV beside it — at the cost of carrying
  those megabytes through every save. The dialog shows the size before you
  choose.

Either way the offset, volume and mute you set are part of the document.

**The clip is a bar you can take hold of.** In the Timeline docker the sound
spans its frames as a bar around the waveform: **drag the body** to slide the
whole clip for timing, **drag either end** to trim its in and out points —
cut the slate off the front, end on the beat — all without touching the
source file. Trimming eats or restores source frames; the trimmed window is
what plays, scrubs and exports. Footage clips get the same bar, one row per
clip, with the same body-slide and edge-trim handles.

**Cut a clip where the playhead is.** Put the playhead inside a bar,
right-click it and choose **Split at frame *n***. The bar becomes two, and
from then on each section slides and trims on its own — move a line of
dialogue three frames later without moving the rest of the take, or hold a
piece of footage while the shot after it stays where it is. Cut again to make
a third; a cut at a section's own edge is refused, because there is nothing
there to divide.

Two things sections will not do. They **never overlap**: drag one into its
neighbour and it stops against it, so the timeline can always say what is
playing at a frame. And splitting **never edits the source** — a section is a
window onto the file and a place to put it, so nothing you do here can lose
sound or frames you have not got a copy of.

A split clip's timing is no longer one offset and one trim, so exports lay
the sections out on the timeline first: gaps come out as silence, and the
sound lands frame-for-frame where the bars say it does.

The bar's controls: **Mute**, **volume**, the **start frame** (negative trims
a lead-in without editing the file), and **✕** to remove the track — the file
itself is untouched.

**It plays.** Press play and the sound runs in sync with the frames, looping
when playback loops and following the Speed % control (faster playback raises
the pitch, the way scrubbing tape does). Dragging the playhead plays the
slice of sound under each frame — the track read a syllable at a time, which
is how a mouth is matched to a line. Playing backwards is silent on purpose;
reversed audio is noise, not information. On a machine with no sound device
the track simply stays quiet — nothing breaks.

## Markers, notes and events

A **marker** is a named point on a frame, shown as a coloured chip on the ruler.
One thing, three uses, and which one you get depends on what you fill in:

| | |
| --- | --- |
| A **label** | The chip on the ruler — "contact", "passing". Keep it short; it has to fit. |
| A **note** | Prose about the frame, as long as it needs to be: *the hand pops here, fix on 2s*. It is not drawn on the ruler; it appears in the notes list and on hover. Writing a note on an unmarked frame creates the marker for you. |
| An **event** | Tick it and the marker is exported to your game engine as an animation event — `OnFootstep` at frame 7. Off by default, because most markers are notes to yourself and a game has nothing to do with them. |

Renaming a marker keeps its note and its event tick. **Next / previous marker**
walks between them, which is what makes them useful on a long sheet rather than
labels you have to find by eye; walking past the last one stays where it is rather
than jumping back to the first.

A **tag** is the same idea over a *range* rather than a point: a name, a start and
an end — "walk", "run", "idle". That is what an engine calls an animation clip, and
it is what lets one sprite sheet hold several animations. A tag can carry a note
too, for when the remark is about the whole cycle rather than one drawing.

Right-click a cel for: insert frame, extend or reduce exposure, clear, delete
(which pulls the rest of the row back), copy, cut, paste, markers, and the
playback range.

Drag a cel along its row to move it. Shift-click for a range, then apply
exposure changes to all of it at once.

**Ctrl+click picks cels one at a time**, including ones that are not next to
each other and ones on other layers — every third cel of a cycle, or the same
two cels across four layers. Ctrl+click a picked cel again to drop it. Shift
still ranges from the last cel you clicked, and re-ranges rather than adding a
second run, so it is the way to correct an overshoot.

**Clear** and **delete** cover every cel you picked, on every layer, and the
cels you left out are left alone — a delete works from the end of the row
backwards, so the gaps it makes never shift the cels you picked next. **Copy**,
**cut** and **paste** work on one row, because a cel clipboard is a sequence of
drawings on one layer; copying a picked-out set takes the cels you chose in
order and pastes them consecutively, holes closed up. If the selection reached
other layers, the status line says which were not copied rather than pretending
otherwise.

## Timing presets

A **timing preset** is a pattern of hold lengths — how long each drawing in a
run is held. `2` is on 2s. `1, 1, 2, 3, 4` is a slow-in: two snappy frames, then
progressively longer holds.

The picker and **Re-time** are on the timeline bar. Shift-click a range first,
or use **Re-time to …** in a cel's right-click menu to do one cel.

Applying one **re-spaces the drawings that are already there**. It never makes a
drawing and never deletes one — the worst it can do to your art is change its
timing, and one undo puts that back.

**The pattern decides the length, not your selection.** Twelve drawings put on
2s take twenty-four frames, so the row gets longer and the rest of it moves
down; the same twelve put back on 1s take twelve, and it gets shorter. The
status line says which way it went. If you want to *thin* a range — keep every
second drawing and discard the rest — that is a different, deliberately
destructive operation.

Six patterns are built in: on 1s, 2s, 3s, 4s, and a slow-in and slow-out. Behind
the **⚙** beside the picker you can save your own: type a name and a pattern
(commas or spaces, both work) and it is there next time you open the app. Your
own patterns can be deleted; the built-ins cannot.

| Compared with | |
| --- | --- |
| A **symbol** | carries *drawings*. A timing preset carries their *spacing* — the half a symbol cannot express. |
| A **template** | gives a new document its shape, once, at creation. A preset re-times drawings you already made, any time, as often as you like. |

## Drawing on a hold

A cel that holds an earlier drawing is not a drawing of its own, so a mark on
one has two honest readings — and which you mean depends on how you work.

By default the cel **becomes a drawing of its own** and the mark lands on it.
That is what every animation tool does, and it is what makes the timeline show
a drawing where you made one. The alternative silently edits the frame being
held, so your stroke turns up on the earlier frame too and the cel you drew on
stays empty and dark.

**Edit → Configure → Timeline** switches it to *Edit the held drawing*, which
is right when the hold is deliberate and you are still working on that one
pose — touching it up without breaking the hold.

Keying is a separate undo step from the mark that prompted it: one undo takes
the stroke back and leaves the new drawing, a second takes the drawing away and
restores the hold.

## The volume and balance check

**View → Volume and balance check** reads the drawing and reports — it never
touches your input the way a guide does. Per frame, on the layer you are
standing on, it measures two things about the ink:

- **Volume** — the ink's area, shown as a band of bars under the timeline's
  tracks. Squash and stretch preserves volume: a ball that squashes must
  widen, so a level skyline is on-model and a step in it is a drawing that
  swelled or shrank — the drift no single frame can show you. Frames that
  drift further than the tolerance from the shot's median turn warm.
- **Balance** — the ink's centre of mass, drawn on the canvas as a dot per
  frame with the arc they trace, onion-skin style: the current frame's dot is
  the solid, ringed one. A walk reads wrong when this arc jitters, and a pose
  reads off-balance when the dot is not over the support foot.

The centre of mass is honestly the *centroid of the ink*: it equals the
physical centre of mass only if the character has uniform density, which is
the right lie for a drawing aid. And "the character" is whatever is on the
**active layer** — effects, props or a second character on other layers are
deliberately not counted, so standing on the character's layer is how you say
what to measure.

Holds are measured once — a scene on 2s costs half its frame count — and the
readings refresh a beat after each edit rather than during it, so the checker
never adds to the pen's own latency. The allowed drift is **Edit → Configure →
Timeline → Volume check**, ten percent unless you say otherwise.

## The timeline's size

**Frames** on the timeline's own bar sets how wide a frame cell is. Narrow
enough to see the shape of the timing on a two-hundred-frame scene, wide enough
to read the thumbnails on a twelve-drawing cycle — it depends entirely on what
you are doing, so it is a slider rather than a constant. The same number is in
Edit → Configure → Timeline.

## The playback range

The scrub bar carries the loop bounds. **Hover it** and two grips appear — one
at each end of the range, with a bar between them showing what will play.
**Drag either** to move that bound; it settles onto whole frames as you go, so
what you see while dragging is what you get. The two cannot cross: push one
past the other and it pins a frame away.

With no range set yet the grips sit on the ends of the whole timeline, so the
first drag narrows it rather than starting from one frame somewhere arbitrary.

**Alt-click** a grip, or anywhere between the two, to give the range back —
between them is the bigger target and the region the range is actually about.
**Right-click** the scrub bar for the same thing as a menu item.

Grabbing a bound and moving the playhead are nearly the same gesture in nearly
the same place, so the grips win where they are and clicking anywhere else on
the bar still scrubs.

The **Set playback start / end** items on a cel's context menu still work and
do the same thing; the grips are the version you can aim.

## Looping

Playback loops, because a cycle is usually what you are watching and stopping
after one pass means reaching for the button every time. **🔁** on the
timeline bar turns it off, and it then plays the range once and stops on its
last frame.

## Playback

Space plays and pauses. Transport buttons step, jump and loop. Set fps and a
speed percentage. Holds resolve properly, so a drawing exposed on 3s stays up
for its full three frames.

**Playback keeps time rather than counting frames.** A scene set to 12 fps runs
at twelve frames a second of real time, and each frame is held the same length
as every other — which matters more than it sounds like it should, because an
uneven frame length is what the eye reads as stutter even when the average rate
looks right. It also keeps the picture with the scratch track instead of
drifting away from it over a long take.

If the machine cannot keep up, frames are dropped so the timing stays true
rather than the animation running slow. After a real interruption — a dialog, a
laptop lid, a breakpoint — playback picks up from where it is rather than racing
to catch up on what it missed.

**While it plays, the frames just ahead of the playhead are being built on
another processor core**, so a drawing you have not reached yet is usually ready
before you get to it. The first run through a scene used to be the rough one for
exactly that reason — every drawing was built at the moment it was needed. There
is nothing to switch on and nothing to wait for; if you want to know whether it
is keeping up on your machine, **Help ▸ Write a render report** says so.

---
