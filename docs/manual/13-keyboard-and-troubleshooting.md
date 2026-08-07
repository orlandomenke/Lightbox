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

## When something goes wrong

If Lightbox closes on its own, it writes down what happened before it goes. You
get a message naming the file and offering to open the folder it is in; if the
failure was bad enough that the message could not be shown, the next start says
so in the status strip instead.

The file lives with your other Lightbox settings, in a `logs` folder inside your
app data folder — the same place the autosave recovery copy goes. It holds the
time, the exact build, your operating system, and what failed. **Attaching it to
a bug report is the single most useful thing you can do**, because it names the
build: "the newest one" is several different programs a week.

Two smaller things end up in the same folder:

- `diagnostics.log` — problems Lightbox survived rather than closed for. The
  canvas is built to keep drawing through a failure rather than take the window
  down with it, and this is where it notes that it did. Each kind is recorded
  **once per session**: a fault in the drawing loop can repeat hundreds of times
  a second, and a log that grows by a megabyte a second is a second problem
  rather than a record of the first.
- The status strip also names the file at the moment such a problem happens, so
  you can tell "that looked wrong" from "something actually broke".

Nothing here is sent anywhere. It is written to your own disk and stays there.

**Help → Open the diagnostics folder** takes you straight there, so you never
have to know the path.

**Help → Show a console while drawing** opens a console window alongside
Lightbox carrying the same information as it happens, rather than after the
fact. It is off, it stays off until you turn it on, and it takes effect the
**next** time Lightbox starts — which is the way it gets used: switch it on,
restart, and make the problem happen again while you watch. Turn it off the
same way.

**Help → Trigger a test failure** appears alongside the console switch, and only
while it is on. It holds a short list of deliberate failures — one the app
survives, one that only writes to the console, and several that end it on
purpose — so you can confirm the crash report, the dialog and the console
actually work on your machine rather than hoping they will the day you need
them. Each entry says what it proves, and the ones that end Lightbox ask first
when the drawing has unsaved edits. Turning the console switch off takes the
whole list away again.

**Help → Lightbox 1.0.0+…** is the exact build you are running, and clicking it
copies that text. It is worth putting in any bug report: "the newest build" is
several different programs in a week, and the part after the `+` says precisely
which one.

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
