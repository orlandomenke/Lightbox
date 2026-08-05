# Onion skin, references and the camera

## Onion skin

Previous drawings tint red, next drawings blue. The checkbox on the timeline bar
turns it on; the two number fields beside it are how many drawings to show
**before** and **after** the playhead, asked separately because working forwards
usually means two behind and none ahead. Ghosts sit directly under the layer they
belong to, so multi-layer onion reads correctly, and they are off during
playback — the one thing playback has to show is the animation.

Everything else is behind the **⚙** next to those fields:

| Setting | What it does |
| --- | --- |
| Mode | **Frames** ghosts this layer's other drawings. **Light table** instead dims the *other layers* at this frame, as sheets under the one you are drawing on. The paper is left alone. |
| Opacity | How visible the nearest ghost is. |
| Falloff | What each further ghost is worth. At **1** they are all equally visible — right for checking registration across a sequence. At **0.5** each is half the one before — right when you are drawing an inbetween. |
| Keyed drawings only | Step by drawings rather than by frames. On 2s or 3s the frame before is a hold of the drawing already on screen; this walks to the real one. |
| Draw over the current drawing | Ghosts on top instead of underneath. Under is how a lightbox works and is what you want while drawing; over is for checking a line you have just made against the one it should match. |
| Before / After tint | The two ghost colours. Red and blue by convention. |

A ghost counts **drawings, not frames**: on 3s the drawing three frames back is
still the *first* ghost, and gets the strongest opacity.

Per-layer, the **◉** in the Layers panel opts a layer out of ghosting entirely.

**Ghost poses.** *Pin as ghost* keeps the current frame ghosted from everywhere
else in the sequence — the extreme you are animating towards, however far away it
is. Depth cannot express that, because the distance is the point. *Clear pins*
removes them all. Pins are saved with the document; a document that never pins
anything stores nothing for it.

Onion settings belong to you, not to the artwork: they are kept across sessions,
survive rearranging or switching workspaces, and opening another document does
not reset them.

## Animation references

**View → Reference** opens a panel for importing an image of an animation — a
sprite sheet, a strip of frames, a contact sheet, a run cycle you photographed
off paper — and laying it against the timeline. The **＋** in the panel's header
picks the file.

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

The camera is the one transform that is not view-only: it is authored, saved and
exported. It still never touches a stroke — it only decides what part of the
record a render shows.

---
