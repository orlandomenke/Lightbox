# Q108 · Selecting a projected reference on the canvas: which tool, what scaling, and where the lock lives — **answered 2026-08-17, all four as recommended**

Raised by: the owner, from a build — *"whenever we project a reference on the
canvas I still want to be able to select the reference on the canvas and be able
to scale and move them… perhaps we should also have the option to lock it."*

What it blocked: a projected reference could only be moved by turning on
**reference align mode** first, could not be scaled on the canvas at all, and
could be knocked out of register by any drag once it was where it belonged.

1. **The Arrow selects it**, the way it selects lines, guides and symbols.
   Click a projected reference to select it, drag to move, corner grips to
   scale, Escape or a click on empty canvas to let go. The alternatives and what
   they cost: **keeping the align-mode toggle** as the only way is the smallest
   change and leaves a mode an artist has to find and switch on before a
   reference will respond at all, which is what made this read as missing;
   **any tool, when nothing else is hit** needs no new gesture to learn and
   would let a brush stroke that starts on a plate grab it instead of painting —
   the accidental drag the lock exists to prevent, made worse.
   - **The pick order is the part with teeth.** The grips come before the
     artwork, because they are small deliberate targets sitting on top of
     everything; the box itself comes *after* it. A projected plate covers most
     of the canvas, so a box that outranked the drawing would make the art under
     it unclickable — the same argument the chrome-before-artwork rule already
     makes, pointing the other way for the one piece of chrome that is the size
     of the paper.

2. **Align mode stays, narrowed to per-frame registration.** The Arrow owns the
   sheet — move, scale, lock; align mode keeps the job the Arrow cannot express,
   nudging one frame's cell (`ReferenceCell.Dx/Dy`) into register against the
   drawing. Two gestures, two jobs, no overlap. **Retiring it** would have been
   the B133-flavoured answer — one route to one promise — and per-cell
   registration would then need a modifier on the Arrow drag, which is a hidden
   gesture nothing advertises. **Keeping both routes to the sheet** was refused
   outright: that is the shape B133 warns about.

3. **Scaling is uniform, from the corners only.** It writes the `Scale` the
   record already has. **Free scaling** would need `ScaleX`/`ScaleY`, would touch
   every consumer of `Scale`, and would let an artist distort the proportions of
   the thing they are drawing from — the same argument the record already makes
   against per-frame scale, which would put the character at a different size on
   each drawing. **No canvas scaling at all** keeps the docker slider and misses
   the point: scaling by eye against the drawing is why you reach for the
   reference on the canvas rather than the panel.

4. **The lock is per reference, plus a sweep.** `ReferenceStrip.Locked`,
   nullable so an unlocked sheet writes no key, undoable, and reachable from the
   canvas shortcut bar, the Reference sheets docker and `Ctrl+Alt+R` for the
   sweep. The sweep (`Workspace.ReferencesLocked`) is workspace state for the
   reason the guide lock is: it is how your screen is set up, not a property of
   the art. **Per-reference only** costs nothing but leaves no way to pin
   everything at once; **sweep only** is cheapest and cannot express the common
   case — the background plate pinned for good while the pose reference is still
   being nudged.
   - **A locked reference still selects.** The box appears with no grips on it,
     which is what makes the lock legible; a gesture that silently does nothing
     is the worse failure.

**Asked for afterwards, in the same exchange, and built:** the locks belong on
the **canvas shortcut bar** while a reference is selected, not only in the
docker — locking is what an artist reaches for the instant a reference is where
they want it, and the pointer is already on the canvas.

**One thing that was not a preference:** the drag had to become one undo step.
B192 had the align-mode drag going through `PerformDelta` per pointer event, so
a single move left dozens of steps to press back through and re-ran the
document-changed storm on each. A gesture is one act, so it is one step —
mutated live through B191's cheap repaint path while the pointer is down, and
registered on release. Building the new gesture on top of the old shape would
have inherited the bug.
