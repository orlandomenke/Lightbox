# Keyboard, performance and what is planned

## Keyboard

Every shortcut is editable in **Edit → Configure…**, which is searchable.
Shortcuts are context-aware: the same key can mean different things over the
canvas, the timeline and the Layers panel.

| Key | Action |
| --- | --- |
| B / E / S | Brush, Eraser, Select (press again to cycle variants) |
| A | Arrow — select whole lines, guides, symbols |
| N | White arrow — reshape an isolated line's points |
| P | Pen — draw a line by placing its points |
| W | Width — make a line heavier or lighter |
| Double-click | Go inside a line to reshape it; Esc to come back out |
| Backspace | Take the last point back off, while drawing with the pen |
| Enter / Esc | Finish the pen line (neither discards it — Ctrl+Z does) |
| Delete | Delete the selected lines |
| Arrows | Nudge the selected lines a pixel, ten with Shift (Arrow tool only) |
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
| **Full** | Twice what the screen shows, up to the document's own resolution. Sharpest — the extra detail smooths stroke edges — and it no longer pays for pixels your monitor cannot display: zoomed out on a big document it costs a fraction of what it used to, and at 100% zoom and closer it is the document's own resolution, as it always was. |
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

**Help → Write a render report** measures how drawing is actually working *on
your machine* and writes it beside the other files. This is the one to reach for
when Lightbox feels slow, because the answer is usually a fact nobody can guess
from the outside.

### If playback stutters, read the tile section first

**Play the scene, then write the report** — the section is about what happens
while frames are flipping, so a report written from a still canvas has nothing
in it.

While a sequence plays, a drawing is held as tiles: it costs the ink on it
rather than the whole sheet of paper, which is the difference between about
14 ms a frame and about 137 ms at FullHD. Some drawings cannot be held that
way, and then playback pays the slow price. The report says which, in one line:

| It says | What to do about it |
| --- | --- |
| *the scene has a camera* | A scene with a camera never uses tiles yet. Nothing to fix in your drawing — this one is on us. |
| *frames carry imported or flattened pixels* | Imported artwork and flattened frames are held whole. Keep imported reference on its own layer rather than flattened into the animation. |
| *frames place symbols* | Placed symbols are drawn whole. Fewer placements on the animated layers, or draw them in. |
| *frames contain smudge or blur strokes* | Smudge and blur have to read the pixels around them, which a tile does not hold. Keep them off the layers that animate, or flatten that pass when it is finished. |

The percentage beside it is **layer passes, not frames** — a two-layer drawing
is asked twice for every frame you see. So 50% often means one layer of two,
not half your animation.

If it says *every pass tiled* and playback is still slow, the drawing is not the
cause and the report's other sections are where to look.

### The section below it: drawings prepared ahead of the playhead

While a sequence plays, Lightbox draws the frames that are *about* to come up on
a second processor core, so the frame arrives already made rather than being
made while you wait for it. You never see this working; you would only see it
failing, as the first run through a scene being rougher than the ones after it.

The report says how many were ready in time. **Over half is it working.** Under
half means a single drawing takes longer to build than the gap between frames on
this machine — usually a very dense drawing at a large canvas size — and the
report says so in those words rather than leaving you to do the arithmetic.
Lowering **Canvas quality**, or playing a shorter range, is what helps there.

### And the one below that: was the clock on time

Every other measurement in the report is about how long a frame took to **make**.
This one is about something else entirely — whether the tick that asked for it
turned up when it was supposed to. They are different problems, and only this one
can make an almost empty scene stutter on a fast machine.

The line to read is **mean lateness**. A millisecond or two is your operating
system's normal scheduling and is not a fault. Much more than that and the gap
between frames is wandering however cheap the frames are — and a wandering gap is
what the eye reads as stutter, even when the frame rate averages out correctly.

**If it says the clock is arriving late, capture the report twice**: once with
the pointer sitting still, and once while you keep moving it. A big difference
between the two is itself the finding — it means the playhead is competing with
your pointer rather than with the drawing, which is a different fix from anything
in the sections above.

### And the one after that: did the frames reach the screen

There are two ways playback can be uneven and they need different fixes, so
there are two sections. The one above asks whether the *request* for a frame
arrived on time. This asks whether the frame then got drawn.

**Mean wait to be drawn** is the number. Under about 17 ms — one frame of a
60 Hz screen — the picture is going up as fast as anything can put it there.
Much above that, frames are being made and then sitting, which is a different
fault from a late clock and reads exactly the same from your chair.

The two sections together settle it:

| Clock | Frames | What it means |
| --- | --- | --- |
| on time | prompt | The front end is fine. If playback still looks uneven, the cost is in *making* the frames — the two sections above this one. |
| late | prompt | The playhead is being held up before it asks for anything. |
| on time | waiting | The frames are made and not shown. Capture it again while moving the pointer: if the wait collapses, the screen is only being refreshed when you move the mouse. |

There is also a line naming the **clock priority** the run used. It normally
reads `Render` and you can ignore it; it exists so that a report captured with
the diagnostic override below can never be mistaken for an ordinary one.

### And below those: where the tick's time went

The two sections above narrow the problem down; this one names it. While a
sequence plays, Lightbox times each part of the work it does per frame and
reports them side by side:

```
scene                     90 frames, 3 layers, 4200 strokes
frame cache               500 MB held of 512 MB
  served from memory      300
  had to render           900  (75%)
  thrown out              850
playback ticks            120
  Thumbnails             never ran
  Highlights                0.2 ms/tick   worst    0.6 ms   (120 of 120 ticks)
  Publish                  15 ms/tick   worst   41.2 ms   (120 of 120 ticks)
```

Two lines to read first:

- **had to render** — a frame that is not in memory has to be rebuilt from every
  stroke on it, which is the most expensive thing that happens per frame. A high
  percentage next to a **frame cache** that is close to full means your scene is
  bigger than the memory set aside for it, and the app is re-drawing frames it
  had already drawn. Fewer frames in the range you are looping, or a smaller
  canvas, is what helps.
- **ms/tick against your frame period** — 83 ms at 12 fps, 42 at 24. Any single
  phase approaching that makes the clock late whatever else is true, and the
  lateness reported above it is the consequence rather than a second fault.

A phase that says **never ran** is doing what it should: some work is deliberately
skipped while frames are flipping, because nobody can read it at speed.

### Trying the fix off and on

If you are chasing this with us, `LIGHTBOX_CLOCK_PRIORITY=Background` starts
Lightbox with the old, slower playhead scheduling deliberately put back. Running
the same build twice — once normally, once with that set — is worth far more
than comparing two different builds, because it changes exactly one thing.
Anything unrecognised, including a typo, is simply the normal setting.

It names which parts of the drawing are done by your graphics card and which by
the processor — and those are not the same question. The status strip says
**GPU** when your card is putting finished frames on screen, which it usually is;
combining the layers is done by the processor either way. The report says both,
so "it says GPU but feels slow" stops being a contradiction.

It also catches a failure that is otherwise invisible: Lightbox asks your card
for a working surface, and if it cannot have one it quietly falls back to the
processor rather than refusing to draw. That is the right thing to do and it
looks like nothing at all — the report says so in as many words, and it is the
most useful line in the file. Large canvases at a high display scale are where
it happens.

Alongside those it records what your session actually cost: how much of each
frame had to be redrawn, how many repaints cost nothing because only the cursor
moved, and how long a frame takes against the 16.7 ms that makes 60 a second. A
short version is written every time Lightbox starts, so the basic facts are
there even for a session that ended badly.

Nothing is sent anywhere, here as everywhere else in this folder. Take two
reports either side of changing **Canvas quality** and the difference between
them is the measurement.

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

**Help → Lightbox 0.1.0-alpha.17+9f3c1ab** is the exact build you are running,
and clicking it copies that text. It is worth putting in any bug report: "the
newest build" is several different programs in a week, and this names one.

It has two parts and both earn their place. In front is the **version** — what
the download called itself, matching the file name on the Releases page. After
the `+` is the **commit**, which is what makes it exact: a version can be shared
by a release and the builds that led up to it, a commit cannot be shared by
anything. An `-alpha.<number>` in the middle means a build made between
releases, straight off a branch; a plain `0.1.0` means a release.

## Planned

Not built. Listed so the gap is visible rather than implied.

**Projects and workflow**
- Scene management
- Project conversion (Illustration → Animation → Game) with no artwork recreated
- Linked characters that update everywhere when edited, rather than copied

**Drawing**
- Pixel-perfect mode
- Brush symmetry
- Reshaping a line after you have drawn it — dragging its individual points, and
  a pen tool. Picking a whole line and moving, deleting or recolouring it is
  built; see [the Arrow](03-tools-and-strokes.md#what-you-can-do-with-what-you-picked)
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
