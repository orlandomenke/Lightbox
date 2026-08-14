# Keyboard, performance and what is planned

## Keyboard

Every shortcut is editable in **Edit → Configure…**, which is searchable.

**Where the pointer is decides what a key does.** Most shortcuts are general and
work everywhere — B is the brush whether you are over the canvas, a toolbar or a
panel. A panel may claim a key for itself, and then it wins **in that panel** and
the general one still applies everywhere else: `I` inserts a keyframe over the
timeline and reaches for the eyedropper anywhere else. A panel counts as yours
when it is the **last place you pointed**: hovering it claims it, moving to the
canvas or another panel releases it, and lifting a pen off the tablet changes
nothing — the panel you pointed at keeps answering until you point somewhere
else. A panel that is redrawing while you rest the pointer on it still answers
for its own keys, and keyboard focus is the fallback when there is no pointer
to consult.

A key press looks for its meaning in three places, in this order:

| | Where | Example: `Delete` |
| --- | --- | --- |
| 1 | **The panel under the pointer**, if it claims the key | Over the Layers panel: delete the layer |
| 2 | **The canvas**, if it claims the key | Over the Colour panel, or over the canvas itself: clear the selection's contents |
| 3 | **Everywhere**, the general binding | `B` is the brush from all three |

The practical form of that: **a panel overrides, it never blocks.** A key a panel
has no use for keeps the meaning it has everywhere else, rather than going dead
while your pointer happens to be resting there.

**Tearing a panel off changes none of this.** A panel in its own window keeps its
own shortcuts and the general ones — `I` still inserts a key in a floating
timeline, and `B` still picks up the brush.

Configure groups the list by **where** each binding applies, not by what it does,
so the three rungs above are the headings you scroll through. That is also why a
general key and a panel key sharing a gesture is not a clash to resolve, and
Configure will not offer to resolve one — they can share on purpose. Two
*general* commands on one gesture is the case with no answer, and that is what it
warns about.

| Key | Action |
| --- | --- |
| B / E / S | Brush, Eraser, Select (press again to cycle variants) |
| A | Arrow — select whole lines, guides, symbols |
| N | White arrow — reshape an isolated line's points |
| P | Pen — draw a line by placing its points |
| W | Width — make a line heavier or lighter |
| U | Shape — line, rectangle, ellipse, polygon |
| Double-click | Go inside a line to reshape it; Esc to come back out |
| Backspace | Take the last point back off, while drawing with the pen |
| Enter / Esc | Finish the pen line (neither discards it — Ctrl+Z does) |
| Delete | Delete the selected lines |
| Arrows | Nudge the selected lines a pixel, ten with Shift (Arrow tool only) |
| Ctrl (hold) | Borrow the eyedropper; let go and your tool comes back |
| I | Eyedropper — anywhere except the timeline, where it inserts a key |
| Ctrl+Z / Ctrl+Y | Undo, redo |
| Ctrl+T | Transform |
| Ctrl+E | Merge the active layer into the one below |
| Ctrl+A / Ctrl+D / Ctrl+Shift+I | Select all, deselect, invert |
| Delete / Backspace | Clear the selection's contents / fill it with the background — over the Layers panel, delete the layer / blank it |
| Space | Play / pause |
| ← / → | Previous, next frame |
| X / D | Swap foreground and background / reset to black over white |
| M | Mirror the view |
| 0 | Reset zoom, rotation, mirror and pan |
| Shift + drag | Resize the brush |
| Wheel / Shift+wheel | Zoom / rotate the view |
| Ctrl+Shift+R | Auto-arrange the reference board (in the board window) |
| Ctrl+Shift+↑ / ↓ | Bring the last picture you touched forward / send it behind (in the board window) |

Zoom, rotation, mirror and pan are **view-only**. They never touch the document.

**Each document keeps its own framing.** Zoom into a face on one drawing, switch
tabs, and the other document is where you left it — not at your face zoom. A
document you have not framed yet opens fitted. The same goes for the playhead,
the selected layer, the selected reference and **the selection**: they belong to
the drawing, not to the window.

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

Between about 70% and 100%, Lightbox quietly composites the **whole** document
rather than the slightly smaller size that was asked for. That is not a rounding
slip: compositing at a reduced size has to resample every layer, which costs
about two and a half times as much per pixel as copying it straight. Just below
full size the resampling costs more than the pixels it saves, so the smaller
setting would be slower as well as softer. Further out it pays properly and is
used.

It only changes what you see while working. **The drawing, the exports and the
thumbnails are always full resolution**, whatever this is set to.

### Blending layers on the graphics card

*Experimental, and off, because it is not yet known to be faster.*

**Edit → Configure → Performance → Use the graphics card to blend layers.**

Stacking layers together is arithmetic over every pixel, and a graphics card is
built for exactly that. The catch is that the layer images live in ordinary
memory and have to reach the card before it can do anything with them. On a
laptop with shared graphics memory — which is most laptops — that transfer
competes with the drawing you are already doing, so it can easily cost more
than it saves.

So this is a switch to *try*, not a setting to turn on and forget. The honest
answer for your machine is:

1. Turn it on.
2. Zoom in far enough that the canvas edges are off screen — that is the only
   view it currently applies to. A fit-to-window view composites the old way.
3. Play a scene back for a few seconds.
4. **Help → Write a render report**, and look for *resident layer textures*.

The report says how many layer draws avoided a transfer. It also says when the
answer is "nothing happened", which is the case the checkbox cannot tell you
about on its own — a machine presenting in software has no card to blend on,
and the hint under the checkbox says so before you spend time on it.

Nothing you save is affected either way. Exports, thumbnails and the file on
disk are produced by the processor whatever this is set to, so a picture that
came out of the graphics card is never the picture that gets written down.

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

### If drawing lags the pen, capture it and read the pen-to-screen section

**Draw first, then write the report.** The *pen to screen while drawing* section
times every stroke event through its whole journey — stamping the dabs,
publishing the frame, drawing it to the screen — and it only has numbers for
strokes made since Lightbox started. A minute of ordinary drawing is enough;
make it a fair minute: one wet brush and one dry one, a big brush and a small
one, so the report can tell a cost that scales from one that is always there.

The section does the diagnosis itself and names the slow step in plain words —
whether the time goes into making the mark, waiting to publish it, or waiting
for the screen to show it — so the useful thing to do with it is attach the
whole file rather than interpret the numbers. Two things worth knowing when
reading it:

- **Wet-media brushes carry an extra line.** A simulated medium re-renders the
  stroke while you draw. That work runs beside the drawing rather than in its
  way, so it cannot lag the pen — but its cost is printed separately because a
  slow pass still shows as the *wet look arriving late*: plain dabs at the pen
  tip that turn into the rim and the pooling a beat afterwards. If that line is
  flagged and the rest is healthy, that settling is the medium's cost — try the
  same preset's fast counterpart to confirm.
- **The measurement starts when the event reaches Lightbox.** Anything the
  tablet driver or the operating system adds before that is invisible to it, so
  a clean section with a lagging hand points outside the application.

### If playback or scrubbing stutters, read the tile section first

**Play the scene, then write the report** — the section is about what happens
while frames are flipping, so a report written from a still canvas has nothing
in it.

While the sequence is moving — playing, or being dragged along the ruler — a
drawing is held as tiles: it costs the ink on it rather than the whole sheet of
paper, which is the difference between about 14 ms a frame and about 137 ms at
FullHD. **Dragging the playhead counts as moving**, so a scrub is as cheap as
playback; the moment you let go, the still picture is drawn the ordinary way
again, which is why a paused frame can look very slightly different from the
same frame flying past.

Some drawings cannot be held that way, and then both pay the slow price. The
report says which, in one line:

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
| on time | waiting | The frames are made and not shown. The split below says why. |

#### The split: what was happening while the frame waited

Under those numbers the same wait appears three more times, sorted by what
arrived while the frame was sitting there: **nothing**, **input somewhere that
is not the canvas**, and **input on the canvas**.

You do not have to interpret it — the report says which of the three findings it
saw. But the reason it is worth capturing properly is worth knowing, because the
three answers point at completely different faults:

| What the rows show | What it means |
| --- | --- |
| Only *on the canvas* is fast | Frames are waiting for the canvas to be repainted, and only your pointer moving over it does that. The playback path is not getting frames drawn on its own. |
| *Any* input is fast | The application is genuinely idle between frames and any event at all revives it — a different fault, and a different fix. |
| All three the same | How long a frame waits has nothing to do with input, so the unevenness is in *making* the frames. Read the tick breakdown below. |

**Capture it with the pointer still, and off the canvas**, for at least a few
seconds of playback — and give each condition a good run. A row with only a
handful of frames in it is one stall rather than a trend, and the report refuses
to draw a conclusion from it rather than pretending to a verdict.

There is also a line naming the **clock priority** the run used. It normally
reads `Input` and you can ignore it; it exists so that a report captured with
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
flattened tiles           64 MB held of 128 MB
  reused a flatten        420  (58%)
  had to flatten          300
  thrown out              12
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

  **The size the cache is allowed to reach depends on your machine**, so the
  number after *of* will not match the example above — Lightbox takes a share of
  the memory you actually have rather than a figure fixed in advance, which is
  why the same scene can play smoothly on one computer and re-render on another
  with no setting different between them. Edit ▸ Configure ▸ Performance ▸
  *Frame cache* overrides it in either direction: raise it if you have memory
  spare and long scenes, lower it if something else on the machine needs the
  room.
- **reused a flatten** — while a sequence plays, most drawings are not changing:
  a layer on 2s shows the same drawing two frames running, and a background may
  not change all scene. Lightbox keeps the assembled picture of a drawing so it
  only has to be put together once, and this is how often that worked. A high
  percentage is the normal, healthy case and needs nothing from you. A low one
  usually means the view is moving — panning or zooming while playing changes
  what has to be assembled every single frame, so nothing can be reused.
- **ms/tick against your frame period** — 83 ms at 12 fps, 42 at 24. Any single
  phase approaching that makes the clock late whatever else is true, and the
  lateness reported above it is the consequence rather than a second fault.

A phase that says **never ran** is doing what it should: some work is deliberately
skipped while frames are flipping, because nobody can read it at speed.

Under the phases are two more lines, and they are the ones to read first:
**drawing it to the screen**, and **tick + draw**. Compositing a frame and
putting it on screen are separate jobs, and only the first happens inside the
tick — so the phases above can all look affordable while the frame period is
already spent. The report adds them for you and says what share of the budget
the total is, because that sum is the number that decides whether playback can
keep time, and neither half means much alone.

### Trying the fix off and on

If you are chasing this with us, `LIGHTBOX_CLOCK_PRIORITY` starts Lightbox with
the playhead scheduled differently, without changing anything else. Running the
same build twice — once normally, once with that set — is worth far more than
comparing two different builds, because it changes exactly one thing. Anything
unrecognised, including a typo, is simply the normal setting, and the report
always names the one it actually used.

| Value | What it does |
| --- | --- |
| `Background` | The original scheduling, deliberately put back. The playhead waits behind everything else the application has queued. |
| `Input` | The normal setting. The playhead waits behind the work that puts frames on screen, which is the point. |
| `Loaded` | A step above normal. Worth trying if playback feels late but steady. |
| `Render` | Level with the work that puts frames on screen. This was the setting before, and it is the one to try if you want to see the stutter come back. |

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
