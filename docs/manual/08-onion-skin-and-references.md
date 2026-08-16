# Onion skin, references and the camera

## Onion skin

Previous drawings tint red, next drawings blue. The checkbox on the timeline bar
turns it on; the two number fields beside it, under **Ghosts**, are how many
drawings to show **before** and **after** the playhead, asked separately because
working forwards usually means two behind and none ahead. They start at 1 and 1,
so onion skin out of the box is one drawing each way — raise them to see further. Ghosts sit directly under the layer they
belong to, so multi-layer onion reads correctly, and they are off during
playback — the one thing playback has to show is the animation.

**The same controls are on both the Timeline and the X-sheet bars**, because
onion skin belongs to you rather than to whichever view of the animation you
have up. They used to be on the X-sheet's bar alone, which made the Timeline tab
look as though one drawing each way was all it could do.

Everything else is behind the **⚙** next to those fields:

| Setting | What it does |
| --- | --- |
| Mode | **Frames** ghosts this layer's other drawings. **Light table** instead dims the *other layers* at this frame, as sheets under the one you are drawing on. The paper is left alone. |
| Opacity | How visible the nearest ghost is. |
| Falloff | What each further ghost is worth, and the setting that decides whether a deeper ghost is visible at all. It compounds, so it bites fast: at **0.5** the third ghost is an eighth of the nearest one's opacity, which against paper is nothing — raise the depth to 3 at that setting and it looks as though nothing happened. The default is **0.75**, which at the default opacity gives 0.35, 0.26, 0.20, 0.15 — four you can read, still clearly ranked by distance. At **1** they are all equally visible, which is right for checking registration across a sequence. |
| Keyed drawings only | Step by drawings rather than by frames. On 2s or 3s the frame before is a hold of the drawing already on screen; this walks to the real one. |
| Draw over the current drawing | Ghosts on top instead of underneath. Under is how a lightbox works and is what you want while drawing; over is for checking a line you have just made against the one it should match. |
| Before / After tint | The two ghost colours. Red and blue by convention. |

A ghost counts **drawings, not frames**: on 3s the drawing three frames back is
still the *first* ghost, and gets the strongest opacity.

Per-layer, the **◉** beside a layer's name in the timeline's layer column opts
that layer out of ghosting entirely — the background you never want ghosted, or
a layer you are not working on. The one on the canvas shortcut bar does the same
for the active layer.

**If you raise the depth and see nothing new appear, the falloff is why**, not
the depth. Every ghost you asked for is being drawn; the ones further out are
simply too faint at a low falloff. Take the falloff up before you take the depth
down.

**Ghost poses.** *Pin as ghost* keeps the current frame ghosted from everywhere
else in the sequence — the extreme you are animating towards, however far away it
is. Depth cannot express that, because the distance is the point. *Clear pins*
removes them all. Pins are saved with the document; a document that never pins
anything stores nothing for it.

Onion settings belong to you, not to the artwork: they are kept across sessions,
survive rearranging or switching workspaces, and opening another document does
not reset them.

## The motion trail

**Motion trail**, beside the onion controls on the timeline bar, draws a line
through where the subject is on each drawing around the playhead, with a tick
per drawing — earlier ticks red, later ones blue, the current drawing ringed in
white. How many drawings it runs through each way is set behind the **⚙**,
under its own heading — separate numbers from the ghost depths, because a
spacing chart wants more drawings than anyone wants ghosted, and starting at
four each way.

**The gaps between the ticks are the spacing.** Even gaps read as even speed;
gaps closing up read as an ease-in — the same chart animators draw on paper
margins, taken off the drawings you already made. A hold is one tick, not a
pile of them, so counting ticks counts drawings.

Where each tick sits comes from the drawing itself: a **pivot anchor** placed
on the drawing (rig edit mode, `Ctrl+K`) if there is one, else the centre of
the drawing's ink. A **filled** tick is an anchor — your own statement of where
the subject is; a **hollow** tick is the derived centre — a guess that wobbles
with the silhouette. If an arc looks wrong on hollow ticks, anchor the drawings
before trusting it. Sockets are ignored: a hand's attachment point is not where
the character is.

The trail follows the **active layer**, is view-only — it never touches the
document — and like the ghosts it is off during playback, coming back the
moment you stop. Its settings are kept with you across sessions, like onion
skin's. It has no key out of the box; **Configure → Shortcuts → “Show motion
trail”** binds one. A drawing with strokes bound to bones is trailed where it
was drawn, not where the pose moved it — trailing the posed motion is *Planned*.

## Animation references

**View → Reference** opens a panel for importing an image of an animation — a
sprite sheet, a strip of frames, a contact sheet, a run cycle you photographed
off paper — and laying it against the timeline. The **＋** in the panel's header
picks the file — or skip the panel entirely and **drop the file anywhere on
the window**: any image dragged in from outside becomes a reference, the panel
opens to show it, and a dropped video goes through the same import questions
the ＋ asks. What Lightbox keeps is the image, not the path, so a reference
never breaks because the file moved.

That includes pictures that were never saved anywhere: **drag an image
straight out of a browser** and Lightbox fetches it and imports it the same
way, named after the file in its address. Drag the picture itself, not the
page — a link to a page is not an image, and the status line says so when a
drop had nothing readable in it.

Lightbox finds the frames in it by reading the gaps between the drawings, and
puts the first on the frame you are on, the second on the next, and so on. The
timeline grows to fit if it is shorter than the reference. If the document has
no drawing on those frames yet, that is the point: they are what you are about
to draw.

The frames it finds are **windows onto the sheet**, not crops of the drawing
inside each one. A runner who travels across their cell still travels when you
step through the reference — cropping each frame to its own artwork would put
the character in the same place every time and delete the animation.

| Setting | What it does |
| --- | --- |
| Show on canvas | The reference sits over the paper and under every drawing, like a photograph taped to a lightbox. It is never exported, and it is never in the artwork. |
| Follow timeline edits | On, inserting a frame moves the later references along with the animation, and the new frame gets no reference — you are drawing an inbetween, and there is no reference drawing for it. Off pins the reference to absolute timing, for matching a shot frame for frame. |
| Scale | One scale for every frame. Per-frame scale would put the character at a different size on each drawing, which is the one thing a size reference exists to prevent. |
| Opacity | How strongly it reads under your drawing. |

### A character sheet, taped to the canvas

The **⧉** button beside a view in the **Reference sheets** panel tapes a
flattened copy of that view onto the canvas — over the paper, under every
drawing, on **every frame**. It is the same kind of reference as an imported
sheet, so everything in the table above applies: drag it, scale it, set its
opacity with the same slider, and it is never exported and never in the
artwork. Click **⧉** again to take it down.

It stays **live**: draw on the sheet — in its tab, or hide one of its layers —
and the taped copy on the canvas updates by itself. Undoing the sheet edit
updates it back. A view that is later deleted leaves its last picture standing,
the way a missing video file leaves its extracted frames.

### The reference board

**View → Reference board…**, the **Board…** button at the top of the
**Reference sheets** panel, the **🗔** icon beside any view in it, or
`Ctrl+Shift+B` — all open the **reference board**: a wall of reference beside
the easel, instead of one picture opened in the drawing's place. Put it on a
second screen, or beside the canvas, and draw.

It fills itself. Every reference sheet the document can consult is pinned up
automatically, each view **flattened to one picture** rather than a stack of
layers, laid out to fit the window. Sheets belonging to a folder above the
document are in scope, so a knight's animations all open the knight's wall.

| To do this | Do that |
| --- | --- |
| Move a picture | Drag it. Picking it up brings it to the front |
| Resize it | Drag its bottom-right corner. The shape is kept |
| Change the stacking | Right-click ▸ **Bring to front** / **Send to back** |
| Put one on the canvas | Right-click ▸ **Project onto the canvas** — see below |
| Lay one onto the timeline | Right-click ▸ **Lay onto the timeline as frames** — the frame analysis the Reference panel's **＋** does, fed from the wall |
| Take one down | Right-click ▸ **Take off the board** |
| Tidy the whole wall | **Auto-arrange** — every picture fitted into the space there is, in the order they are in |
| Add a picture from disk | **Add image…**, or drag files onto the board |
| Add one from a web page | Drag the image off the page onto the board |
| Put a sheet back up | **Sheets ▾** lists everything in scope that is not on the wall |
| Move around | Wheel zooms, middle-drag or drag the empty background pans |

The sheets on it stay **live**: draw on a view in its tab, hide one of its
layers, undo, and its picture on the board updates by itself. Drawing still
happens in the tab, not on the board. A view deleted from the project comes off
the wall; imported pictures answer to nothing in the document and stay.

**The arrangement is remembered.** It is saved as you make it, and belongs to
the folder the references belong to — so every animation of the same subject
opens the same wall, where you left it, in the session after this one. A
document that is not in a project keeps its own board inside the file instead.

**Imported pictures are copied into the project**, into a `references` folder
beside your art. The original can be moved, renamed or deleted afterwards and
the board is unaffected — which is the point, since a picture dragged off a web
page has no file to point at in the first place.

Clicking **🗔** again brings the open board forward; there is one board window,
not one per picture, and it closes with the application.

### Projecting a board reference onto the canvas

Right-click any picture on the board and choose **Project onto the canvas** to
tape it over the drawing area — any picture, not only sheet views: a file from
disk or an image dragged off a web page projects the same way. The projection
sits **over the paper and under every drawing layer**, so an opaque background
does not hide it and your lines always draw on top. It is **not a layer**: it
never appears in the layer stack, never reaches an exported pixel, and its
opacity is the reference opacity slider like any taped-up reference.

The projection **keeps its link**. A sheet view stays live — draw on the sheet
in its tab and the canvas copy follows. Asking the same tile again takes the
projection down rather than stacking a second copy, and the board's menu reads
**Take off the canvas** while one is up.

On the canvas itself:

| To do this | Do that |
| --- | --- |
| Select a projected reference | Right-click it, or grab it in align mode (**Align on canvas** in the Reference panel) |
| Move it | **Align on canvas**, then drag |
| Bring it forward or back | Right-click ▸ **Bring forward** / **Send backward** (or **to front** / **to back**) among the other references |
| Take it down | Right-click ▸ **Take off the canvas**, or the board's menu |

Forward and back move a reference among the *other references* — the whole
stack stays beneath the drawing layers, which is what a reference is for.

**Projecting and laying onto the timeline are different acts.** A projection is
one picture pinned over the paper on every frame. **Lay onto the timeline as
frames** instead runs the picture through the same analysis as the Reference
panel's **＋** import: the frames are found in it (a sprite sheet, a strip, a
run cycle), laid against the timeline from the frame you are on, and the
timeline grows to fit. The Reference panel opens so you can fix the grid
(**Cols**/**Rows** ▸ **Apply grid**), rescale, and align — everything in
*Animation references* above applies. Laying the same picture twice is two
imports, exactly like pressing **＋** twice; taking a projection down never
touches a laid-out animation.

**What detection actually looks for.** Not gaps in the whole image — the
drawings themselves. It finds every connected mark on the sheet and then throws
away what is not a drawing: specks and watermarks go because they are tiny next
to a figure, and a title banner goes because a line of text is a fraction of the
height of a row of figures. What survives is grouped into rows, and each row is
cut into cells of equal width. So a page with a heading and a signature on it
works, not only a clean sprite atlas.

**When detection cannot find the frames.** Two rows that touch have no boundary
in the pixels, and no amount of looking finds one that is not there. Set **Cols**
and **Rows** and press **Apply grid** and the image is not consulted at all.
**Detect** goes back to reading it. Or edit the boxes by hand — see below.

### Video to draw against

The same **＋** also takes a **video clip** (MP4, MOV, AVI, MKV, WebM) — the
drawn-over-live-footage workflow. The clip's frames are extracted at the
scene's own fps and laid against the timeline like any reference: frame 1
under frame 1, the timeline growing to fit, your drawing layers on top.

On import you choose **what the clip is for**, and each purpose has its own
storage:

- **Reference — keep by path** (the default): the document stays light and
  the frames rebuild from the file when the document opens. If the file has
  gone, you are drawing against nothing until it is back — the drawing
  itself is never touched.
- **Reference — embed the frames**: the extracted frames travel inside the
  document at reference quality, so it reopens self-contained, with no
  FFmpeg and no file beside it. The original footage is not recoverable from
  the document — it is a reference, not an asset.
- **Production — embed the clip**: for the small production whose whole
  pipeline is Lightbox. The original footage travels in the document at full
  fidelity, shows at full strength, and — unlike every reference —
  **composites into video and PNG exports**, over the paper and under your
  drawings, exactly as the canvas shows it.

**There is a budget** on the extracted frames either way: reference quality
(up to 480 px wide) and at most 240 of them — twenty seconds at 12 fps. A
reference is for registration and timing, not for grading; production
footage keeps its full-fidelity original alongside for the render.

Extraction runs through the same bundled FFmpeg the video export uses; on a
machine without it, the import says so instead of failing quietly.

## Editing the grid by hand

The **⊞** in the shortcut bar turns on grid editing. Every box on the sheet
appears at once, the canvas stops being a place to draw, and **Esc** leaves.
The mode is only there when a reference has been imported.

- **Drag inside a box** to move it.
- **Drag a corner** of the selected box to resize it. The box is a window onto
  the sheet, so growing it shows more of the drawing rather than scaling it.
- **Drag on empty canvas** to draw a box of your own — the way out when
  detection cannot see a boundary.
- **Delete** removes the selected box. The sheet is untouched; only the window
  goes.

Every box carries a **pivot**, drawn as a red cross: the point that should sit
still from frame to frame — the contact foot, the hips. Until you place one it
sits at the middle of the box's foot, which is where a standing figure's contact
point usually is. It is recorded on the sheet, so nudging or rescaling the
reference afterwards leaves it on the same part of the drawing.

**Generate keyframes** does two things at once, because they are one intention:
the timeline grows until every box on the sheet has a frame, and the boxes are
registered so their pivots land at the same place. An eight-frame reference on a
one-frame document is not a state anybody asked for, and neither is a run cycle
that has to be nudged into place eight times. Pressing it twice changes nothing —
everything is measured against the first box, so the sheet cannot wander off the
canvas one press at a time.

Registering on the pivot takes the travel *out* of a cycle, which is the
opposite of what tiling the sheet does, and both are wanted: tiling keeps a
runner travelling, and aligning lets you see the pose change underneath. Which
you want depends on whether you are matching the walk or the drawing, so it is a
button rather than a rule.

**Alignment.** **Align on canvas** turns dragging into lining the reference up
rather than drawing: drag to move *this frame's* reference, hold **Shift** to
move the whole sheet. The **This frame X / Y** fields do the same numerically,
and they drag horizontally like every other numeric field. Alignment is per
reference frame, because it is a property of that drawing — a frame shown twice
is lined up the same way both times. **Clear all alignment** undoes every nudge
on the sheet. Nudges are undoable.

Boxes can also be nudged **several at a time**: select them with the Select tool
and the Move tool drags the whole selection, as one undo step. This moves where
the boxes sit, never which part of the sheet they show — resizing a box is how
you change that, and the two stay separate so lining up a pose cannot quietly
recrop it.

Each reference is saved inside the document, image and all, so it cannot break
by having a file move out from under it.

## Camera

**A camera is optional and absent until you add it.** A document that never
adds one shows no camera UI, serializes no camera keys and pays nothing for it.
That matters because Lightbox has two output targets and neither is the default:

- **Assets** — sprite sheets and cycles. The canvas *is* the output. No camera.
- **Shots** — the canvas is a world and the camera frames part of it. The
  deliverable is what the camera saw.

Add one from the timeline bar. Pan, zoom and roll are keyframed and interpolated;
set a key at the playhead, or clear it. **Through camera** previews the framing.

With a camera, the canvas shows its frame in orange with everything outside
dimmed — and the frame is a gizmo. Drag the square on its top edge to pan, a
corner square to zoom (drag inward to push in, outward to pull back), and the
circle floating above the top edge to roll. Each drag writes to the framing at
the playhead, keying it there exactly as the numeric fields do — adjusting the
camera *is* keying it. The handles are small on purpose: a press anywhere else
on the canvas still paints, camera or no camera.

The camera is the one transform that is not view-only: it is authored, saved and
exported. It still never touches a stroke — it only decides what part of the
record a render shows.

---
