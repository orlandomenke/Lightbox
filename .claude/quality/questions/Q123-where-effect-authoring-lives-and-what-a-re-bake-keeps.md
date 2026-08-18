# Q123 · Where effect authoring lives, and what a re-bake keeps — **answered 2026-08-18**

Step 4 makes an element something an artist can run, which forces four choices
the design had only half-made. The owner raised the first — *"this effect
creation should have a window of its own"* — and it was right for a reason
beyond comfort, so all four were prompted together. All four took the
recommendation.

**1. Its own window, plus on-canvas placement.** The design's landing checklist
said *docker*; a window is better and the reasons are countable. Roughly thirty
fields — eight simulation parameters, the emitters, the band ramp, sixteen
treatment fields — do not fit a docker column, and the cascade needs room to mark
which fields are overridden and offer a per-field revert, which is Q118's one
unavoidable cost. Tuning needs a preview and a scrubber, and a docker has room
for neither, so tuning would be bake-and-squint. And `HOTSPOTS.md` names
`MainWindow.axaml` the hottest file in the repository with no tests at all: a
separate window is the *structural* way to keep the design's "no effect logic on
`MainViewModel`" promise rather than an intention to be careful.

**Placement stays on the canvas**, and that is the half a window-only answer
would have lost: an element is a box at a place and its emitters are points and
segments inside it. Typing coordinates for a flame is not authoring. So the
canvas owns *where*, and the window owns *what it does and how it is drawn*. The
cost accepted is a second place to look, and a window that has to stay in step
with the canvas selection.

**2. The preview shows the baked strokes, on a scrubber** — what you will
actually get, not the field behind it. A heat map is always live and shows the
physics directly, but band levels, line weight and coverage cannot be judged from
it, and those are most of what is being tuned.

The choice is affordable only because of the seam step 4 builds: **solving and
drawing are separate passes.** Changing a line treatment re-traces in ~44 ms and
feels live; changing a simulation parameter costs a ~1.4 s re-solve. Those two
will feel different and the UI should not pretend otherwise — the honest thing is
to show it, not to hide the re-solve behind a spinner that makes every edit feel
slow.

**3. Editing a baked stroke makes it yours.** Touch a stroke carrying a
`SimId` and the id is cleared; re-baking removes only strokes that still carry
it. Hand work survives by construction, with no dialog and no new state.

This replaces what the design assumed — *"re-simulating discards hand edits,
which the UI has to say plainly before it does it"* — with something better than
a warning: nothing to warn about. It also makes "the artist can draw over, erase
into and rig the result" true rather than conditional, which was the whole
argument for baking strokes in the first place (Q116). The cost, accepted: an
artist keeps a stroke they may have wanted regenerated, and needs a way to hand
it back — a *re-attach* that restores the id, which is a small command and is
listed in step 5 rather than forgotten.

Baking onto a locked layer was declined for giving up exactly what baking was
for; warn-and-discard for making a parameter nudge cost a tweaked flame tongue.

**4. An element bakes into its own layer**, created and named on first bake, so
it can be hidden, blended, opacity-tweaked and re-baked as a clean replace
without touching anything else — and so it is the natural home for a later
"omit from export" or a separate effects pass. The cost accepted is that an
element silently adds a layer, and that moving an element between layers becomes
a question needing an answer. Baking into the current layer was declined for the
same reason a forty-eight-frame element in the character layer is unpleasant:
`SimId` would still identify the strokes, so it would *work*, and it would read
as mess.
