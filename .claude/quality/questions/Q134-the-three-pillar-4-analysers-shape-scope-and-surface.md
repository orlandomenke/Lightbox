# Q134 · The three Pillar 4 analysers: shape, scope and surface — **answered 2026-08-20**

The spacing assistant, walk cycle analyzer and jump arc analyzer were the last
`[?]` reading-family items Q98's motion trail was built to carry. All three are
arithmetic over the record — none needs a model, so they stay in Pillar 4
rather than AI assistance. Four decisions were prompted together.

**What the spacing assistant adds: flag + one-click nudge** (recommended,
accepted). The graph editor already lays measured spacing over intended
(`SpacingChart.Measure`/`Intended`), so an advisory-only assistant would
re-state a picture that exists. The assistant names the drawings that miss the
intended ease, shows each one's target as a ghost tick on the motion trail,
and offers a command that slides the playhead's drawing along the measured
path to its intended fraction — a whole-frame translate, one undo step,
deterministic. The declined alternatives: advisory-only leaves the artist
doing the tedious part the assistant exists to remove; capture-a-chart-from-
the-drawing is a chart-authoring feature more than a spacing fixer, and can be
its own item later.

**Walk cycle checks: loop closure + contact evenness + bob symmetry**
(recommended, accepted), on the active layer's sheet treated as one cycle,
with feet read as the lowest ink. The cost accepted knowingly: the bob check
assumes a gait that rises between contacts, so a deliberate shuffle can be
flagged — the readout is advisory prose, never a gate. Requiring authored
anchors was declined because the analyser would then say nothing on exactly
the unanchored work that needs it.

**Jump arc scope: the run the playhead is in** — extreme to extreme, the same
closing rule `SpacingChart` and `ExposureSheet.RunAt` share (recommended,
accepted). No new range-selection UI; automatic airborne detection stays with
the separate *automatic contact frame detection* roadmap item. Whole-layer
fitting was declined because a walk and a settle in the same layer make one
parabola meaningless.

**Surface: the trail overlay plus a readout** (recommended, accepted).
Geometry — ghost targets, the fitted arc, offender marks — rides the existing
motion-trail overlay; findings appear as a compact text readout in the onion
bar's ⚙ flyout beside the trail's own settings. A dedicated analysis docker
was declined as a new panel for what is initially three short reports; graph
series were declined because the walk and jump findings are positional on the
canvas, not curves over time.

One consequence worth writing down: the assistant's targets use
`MotionTrail.Locate` (authored pivot, else ink-bounds centre) rather than
`SpacingChart`'s stroke-point centroid, because the ghost ticks are painted
beside the trail's real ticks and the two must not disagree about where a
drawing is. The graph keeps its centroid — it is a legibility measure, not a
position anything is moved to.
