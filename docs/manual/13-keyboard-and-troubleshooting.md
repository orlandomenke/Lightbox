# Keyboard, performance and what is planned

## Keyboard

Every shortcut is editable in **Edit → Configure…**, which is searchable.
Shortcuts are context-aware: the same key can mean different things over the
canvas, the timeline and the Layers panel.

| Key | Action |
| --- | --- |
| B / E / S | Brush, Eraser, Select (press again to cycle variants) |
| Ctrl (hold) | Pick a colour |
| Ctrl+Z / Ctrl+Y | Undo, redo |
| Ctrl+T | Transform |
| Ctrl+A / Ctrl+D / Ctrl+Shift+I | Select all, deselect, invert |
| Space | Play / pause |
| ← / → | Previous, next frame |
| X / D | Swap foreground and background / reset to black over white |
| M | Mirror the view |
| 0 | Reset zoom, rotation, mirror and pan |
| Shift + drag | Resize the brush |
| Wheel / Shift+wheel | Zoom / rotate the view |

Zoom, rotation, mirror and pan are **view-only**. They never touch the document.

**Each document keeps its own framing.** Zoom into a face on one drawing, switch
tabs, and the other document is where you left it — not at your face zoom. A
document you have not framed yet opens fitted. The same goes for the playhead,
the selected layer and the selected reference: they belong to the drawing, not to
the window.

Framing is remembered for the session, not saved into the file — reopening
tomorrow opens fitted. The brush is the deliberate exception and works the other
way: it follows the tool, or the project, never the individual document — see
[Where the brush lives](04-brushes.md#where-the-brush-lives).

---

## When the canvas feels slow

The info strip along the bottom reports what the app is actually doing:
how much memory the rendered frames are using, and whether the canvas is being
put on screen by the **GPU** or by the **CPU (software)**.

Software means no graphics context was available — usually an out-of-date
driver, a virtual machine, or a remote desktop session. It is not a small
difference. Showing the canvas means rescaling the whole document for every
frame, and in software that becomes the most expensive thing the app does,
outweighing the drawing itself.

**Canvas quality** is the lever, in **Edit → Configure → Performance**:

| | |
| --- | --- |
| **Display** | Matches the screen — full detail zoomed in, less zoomed out. The default. |
| **Full** | Always the document's own resolution. Sharpest, slowest. |
| **Half** | Half of what the screen shows. Softer while you work, fastest. |

It only changes what you see while working. **The drawing, the exports and the
thumbnails are always full resolution**, whatever this is set to.

### When the app changes it for you

If the canvas cannot keep up, Lightbox turns the quality down to Half once and
says so in the status line. That happens when the graphics backend comes back
as software, and also when the measured frame time says the canvas is
struggling — which catches a machine that has a GPU and is still too slow, on
a big canvas with a lot of onion skin.

It will not do this if you have set a canvas quality yourself. Choosing one —
including choosing Display, the default — settles the matter, and the app will
not overrule it however slow the machine gets. You can change it back at any
time in Configure, and it will stay changed.

**Measured now**, at the bottom of that page, shows the current cost per
repaint and per frame, the headroom left, and what is worth changing about the
document if there is none.

## Planned

Not built. Listed so the gap is visible rather than implied.

**Projects and workflow**
- Scene management
- Project conversion (Illustration → Animation → Game) with no artwork recreated
- Linked characters that update everywhere when edited, rather than copied

**Drawing**
- Pixel-perfect mode
- Brush symmetry
- Perspective rulers, vanishing points, grid and snapping
- Shape tools and vector guides
- Pattern fills

**Layers**
- Layer masks, clipping masks, adjustment layers
- Non-destructive filters

**Editing**
- Liquify, clone stamp, healing brush

**Version control**
- Seeing who has a file checked out, and taking the lock before you paint. Aimed at
  the systems game studios actually run — Unity Version Control and Perforce — where
  a lock, not a merge, is how two artists stay out of each other's way. Git status
  on a row is planned alongside it.

**Animation**
- A pose library, and reusable animation cycles

**Interop**
- PSD import and export

**The application**
- This manual, in the app
