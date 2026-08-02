# Bugs

What is wrong, and what would prove it is not.

**The checkboxes are derived, not typed.** Every entry names the regression
test that must exist for the bug to be closed, and
`python3 scripts/bugs.py sync` resolves that against
`.claude/codemap/map.json` and rewrites the mark.

| | meaning |
| --- | --- |
| `[ ]` | open — the test does not exist, or does not resolve |
| `[x]` | fixed — every named test resolves |

Deleting the test reopens the bug on the next run. That is the property worth
having: a bug marked fixed with nothing guarding it is worse than an open bug,
because it stops anyone looking.

An entry with no `evidence:` is **refused at check time**. If you cannot name
what would prove the fix, you have not finished describing the bug.

`evidence: manual` is the one exception, for bugs no headless test can reach —
synthetic pen and hover input through Xvfb is unreliable here. Those never
auto-close; a human verifies and ticks the box. Reach for it rarely: it is the
only place in this file where a claim is the best evidence available.

```bash
python3 scripts/bugs.py check          # status, exits 1 on drift
python3 scripts/bugs.py sync           # rewrite the marks
python3 scripts/bugs.py next           # highest-priority open bugs
python3 scripts/bugs.py mine timeline  # open bugs in one domain
python3 scripts/bugs.py stats          # counts per priority and domain
```

## Priority — severity × reach

|  | every session | common | occasional | rare |
| --- | --- | --- | --- | --- |
| **blocks work** | P1 | P1 | P2 | P2 |
| **corrupts art** | P1 | P1 | P2 | P3 |
| **wrong output** | P2 | P2 | P3 | P3 |
| **annoyance** | P3 | P3 | P4 | P4 |

*Corrupts art* outranks *wrong output* because a wrong render is visible and
recoverable; a damaged record is neither.

**Cost is not in the matrix, on purpose.** It is recorded on each entry so a
session with ten spare minutes can pick a cheap P2 over an expensive one, but
it never changes the order — folding effort into the rank is how the hard,
important bugs never get picked.

## Domains

One tag per bug, from this list, so an agent about to edit an area can find
what is already known to be wrong in it:

`brush` · `timeline` · `layers` · `canvas` · `transform` · `colour` ·
`export` · `project` · `ui` · `ai`

**The rule for an agent working in a domain:** fix its open **P1 and P2** bugs
alongside whatever you came for, each with its own regression test in the same
commit. Mention P3 and P4 without touching them — a request to change one
thing must not come back as a diff touching six. Anything needing a product
decision goes to `QUESTIONS.md` and is left alone.

---

## Open

- [x] **B1** `P1` `timeline` Onion skin invisible since the document gained a paper layer `evidence: OnionGhostsShowOverThePaper`
  - Repro: open the app (paper layer present), draw on frame 1, add frame 2, turn onion skin on. No ghost.
  - Cause: `MainViewModel.PublishSnapshot` queues **every** onion pass first and then composites every layer over them. The paper is opaque and at the bottom of the stack, so it paints over all the ghosts. Before the paper existed the ghosts showed through a transparent stack.
  - Fix: interleave — for each layer, its own ghosts, then the layer. That is also what makes multi-layer onion read correctly.
  - Regression I introduced with the paper layer. Cost: S

- [x] **B2** `P1` `timeline` Cannot draw on a layer whose cels are all cleared `evidence: PaintingWithNoKeyAtThePlayhead_CreatesOne`
  - Repro: clear every cel on a layer, pick the brush, drag on the canvas. Nothing happens and nothing is said.
  - Cause: `PaintTarget()` returns null when no cel at or before the playhead is keyed, so `BeginStroke` returns silently. Fill and gradient no-op the same way.
  - Fix: painting where there is no key should **create** one, which is what every animation tool does. Silence is the worst part — even refusing would be better than nothing.
  - Cost: S

- [x] **B3** `P1` `timeline` Thumbnail never returns after a cel is cleared and redrawn `evidence: RedrawingAClearedCel_BringsTheThumbnailBack`
  - Repro: clear a cel, draw on it again. The timeline cell stays blank.
  - Cause: likely the same root as B2 — nothing is drawn, so there is nothing to show. If it survives B2, it is a missing `_dirtyThumbIds` entry in `ClearCelAt`.
  - Cost: S

- [x] **B10** `P1` `project` Every swatch link dies when a project is saved and reloaded `evidence: VariantsRoundTripWithTheirPalettes, ASavedProjectKeepsItsSwatchIds`
  - Repro: make a project, paint with a palette swatch, save, reopen. The art no longer follows the palette; a variant resolves to no palette at all.
  - Cause: `ProjectIo` stored shared palettes as GIMP `.gpl`. That format carries names and RGB and **cannot carry ids** — `GimpPalette.Read` mints fresh ones — so every `Stroke.SwatchId` and every `Character.PaletteId` pointed at something that no longer existed.
  - Fix: store project palettes as JSON, ids intact. `.gpl` stays what it is — an interop format for the docker's Import/Export, not a storage format.
  - Mine, from the previous commit. Found by the variant tests rather than by review. Cost: S

- [x] **B4** `P2` `brush` Blur, smudge and blender brushes only update on pen lift `evidence: SmudgeShowsMidDrag`
  - Repro: pick smudge or blur, drag across existing paint. The smear appears only when the pen lifts.
  - Cause: `FlushLivePreview` appends the draft into `_liveComposite`, but `PublishSnapshot`'s overlay branch only ever reads `_liveScratch`. The composite is computed every event and never shown.
  - Fix: for the active layer, a blur/smudge drag **replaces** the layer bitmap rather than overlaying a scratch — these tools modify pixels that are already there, so an overlay is the wrong shape.
  - Same class as the wet-media bug already fixed: brushwork must look while drawing the way it will look afterwards.
  - Cost: M

- [x] **B5** `P2` `transform` Transform shows no live pixels, only the gizmo `evidence: TransformPreviewMovesThePixels`
  - Repro: Ctrl+T, drag a handle. The quad moves; the drawing does not until commit.
  - Cause: the gizmo is view-only chrome and nothing maps the strokes until `CommitTransformAffine`/`Perspective`.
  - Fix: a per-pass matrix on `RenderPass`. The gizmo hands its shape to the view model on every change and the composite draws the moving pixels through it — no geometry is re-mapped until apply, so undo and invariant 1 are untouched. Under a selection the frame splits into the strokes that move and the ones that stay, which is the split the commit makes.
  - Cost: M

- [x] **B6** `P2` `timeline` No way to delete a cel `evidence: DeleteCel_RipplesTheRest`
  - Repro: right-click a cel. There is "Clear cel" (blank it, keep the timing) and "Cut cel", but nothing that removes it and pulls the following cels back.
  - Cause: never built. `DeleteFrame` is a different operation — it deletes across every layer.
  - Fix: a ripple delete on one layer's row, one undo step.
  - Cost: S

- [x] **B9** `P1` `timeline` The paper disappears on every frame but the first `evidence: AddingAFrame_HoldsThePaperRatherThanBlankingIt`
  - Repro: open the app, add a frame. The new frame has no paper — transparent canvas, checkerboard.
  - Cause: `DocumentEditor.AddFrameAfter` inserts `NewEmptyFrame(layer)` on **every** layer, including the Background. A blank key on the paper layer shadows the paper.
  - Fix: the paper holds. A background layer gets `Frame = null` on an added frame, so the exposure sheet resolves it back to the one paper drawing — which is what a paper layer means.
  - Found while writing B1's regression test: the ghost was visible in the test only because the paper had gone missing. Cost: S

- [x] **B11** `P2` `ui` The project panel never appears after New or Open project `evidence: TheProjectPanelAppearsAsSoonAsThereIsAProject`
  - Repro: create or open a project. The project tree is not in the sidebar. Adding a character to it later makes it appear.
  - Cause: `MainViewModel.HasProject` forwards to `ProjectDocker.HasProject` and so has no notification of its own. The relay it depended on was the docker's *change callback*, which fires when the docker edits the project — and adopting one is not an edit.
  - Fix: relay `ProjectViewModel.PropertyChanged` for `HasProject` directly.
  - Reported from a build. Cost: S

- [x] **B12** `P1` `ui` The canvas is not rendered at all `evidence: TheCanvasGetsTheRoomLeftOverByTheStrips`
  - Repro: open the app, or make a new file. The canvas area is empty and hovering it does not show the brush ring, even in brush mode.
  - Cause: `CanvasHost` was still on `Grid.Column="2"` after the docking rework renumbered the work area's columns to seven. Column 2 is the *left dock strip's* cell — `Auto`, and empty by default — so it sized itself to the zoom bar floating on top of the canvas and the canvas got no width. Column 4 (`*`, `MinWidth=240`) held the space open and empty, which is why it read as a dead renderer rather than a missing control.
  - Fix: `Grid.Column="4"`. The regression test asserts the canvas has real bounds and shares a column with neither strip.
  - Reported from a build after I dismissed the same symptom in a screenshot as an Xvfb artifact. It was not. Cost: S

- [x] **B13** `P2` `ui` Neither half of the foreground/background pair opens anything `evidence: EachHalfOfThePairCarriesItsOwnPicker, ClickingASwatchOpensItsPicker`
  - Repro: click either swatch in the tool options bar. Nothing happens — no picker, no drag. The pair is a read-only display of two colours you can only change from elsewhere.
  - Cause: two handlers, both dead in different ways. The foreground swatch's `OnColorSwatchPressed` subscribed its move/release handlers to the Color *panel's* named `ColorSwatch` control rather than to the swatch that was pressed, so the gesture was watched on a control nobody had touched. The background swatch's handler called `FlyoutBase.ShowAttachedFlyout` on a `Border` that had no `FlyoutBase.AttachedFlyout` — a no-op that is indistinguishable from a click that did nothing.
  - Fix: one handler for every swatch, operating on the sender; an attached flyout on each, pointing at its own picker; and a `▾` dropdown beside the pair, because a press-and-maybe-drag gesture is a poor place to hang the only route to the picker.
  - Found while reading for the picker-flyout request, which turned out to be a fix rather than an enhancement. Neither failure was catchable from a view model — the view models were correct and the wiring was not — so the guards are window tests. Cost: S

- [x] **B14** `P2` `ui` Deleting the paper leaves an opaque white canvas `evidence: DeletingThePaperLeavesTransparencyRatherThanWhite`
  - Repro: unlock the Background layer and delete it. The canvas stays white. There is no checkerboard, and nothing tells you the paper has gone.
  - Cause: `SceneRenderer.BackgroundOf` clears to transparent when a background layer *exists*, and falls back to the scene colour when none does — a rule written for documents saved before background layers, which is exactly the state a deletion produces. So deleting the paper put the document back into the legacy branch and the paper came back as a clear colour.
  - Fix: deleting the last background layer sets `Scene.TransparentBackground`. Deleting the paper means there is no paper, and undo restores both together.
  - Reported from a build. Cost: S

- [x] **B15** `P3` `ui` The tool options bar's columns do not line up `evidence: EveryValueFieldInTheBarIsTheSameWidth, NoValueFieldInTheBarSetsAWidthOfItsOwn`
  - Repro: switch between brush, gradient and selection. The label, slider and value box start and end somewhere different each time.
  - Cause: every group sized its own parts — value boxes of 64, 68 and 72 for three numbers of the same shape, sliders of 80, 90 and 110. Nothing was wrong individually, which is why it survived a density pass.
  - Fix: `Slider.param` and `NumericUpDown.value` in `Density.axaml` decide once. The guard is that no control in the bar declares a width of its own, which is the failure rather than any particular number.
  - Reported from a build, with a screenshot. Added to the ui-critic's checklist. Cost: S

- [x] **B16** `P3` `ui` The brush parameter flyout scrolls when it should grow `evidence: TheBrushParameterFlyoutIsNotPinnedToOneHeight`
  - Repro: open the ⚙ flyout and switch category. Short pages have dead space; long ones get a vertical scrollbar.
  - Cause: the flyout's grid declared `Height="430"` for five pages of different lengths.
  - Fix: size to the page, with a `MaxHeight` so a very long one cannot run off the screen.
  - Reported from a build. Cost: S

- [x] **B20** `P2` `canvas` Shrink does nothing to a selection touching the canvas edge `evidence: ShrinkingTheWholeCanvasPullsInFromAllFourEdges, AnEdgeTouchingSelectionShrinksOnTheEdgeItTouches`
  - Repro: magic-wand the whole document, or drag a marquee off the left edge, then Shrink. Select All then Shrink moved the selection one pixel and no more; a marquee touching one edge shrank on three sides.
  - Cause: erosion is a dilation of the complement, and `Dilate1` treated everything beyond the bitmap as empty. For a full-canvas selection the complement is empty, so there was nothing anywhere to grow inward from. The one pixel that did move was the round-trip artifact of B21, not erosion.
  - Fix: the complement includes what is off the edge. `Dilate` takes what lies outside, false for a real dilate and true for the inverted pass inside `Erode`. Cost: S

- [x] **B21** `P2` `canvas` Shrink then Grow walks a selection into its top-left corner `evidence: ShrinkAndGrowLeaveTheSelectionWhereItWas, ACircleShrinksByTheSameAmountOnEverySide`
  - Repro: draw a circular marquee, then Shrink and Grow a few times. The top and left creep in about two pixels a cycle; the right and bottom do not move. Reported as "circle shrinks from the top left".
  - Cause: `TraceBoundary` walks pixel centres, so the polygon it returns runs down the middle of the boundary ring rather than around the outside of it. Filling it back keeps only the pixels whose centres are strictly inside, and Skia resolves the exact-half case towards the bottom right — so the top and left rings are lost on every round trip and the bottom and right survive.
  - Fix: when the contours being rasterised came from a trace, stroke the path as well as filling it, which puts the ring back and makes the round trip stable. Scoped to the selection adjust, because a contour drawn by hand is a geometric outline and filling it is already right. The tracer itself still reports centres — the honest fix there is a corner-lattice trace, and it is shared with every flood fill, so it is not something to change inside an unrelated commit. Cost: M

- [x] **B22** `P2` `colour` A duplicated cel loses its link to the palette `evidence: ACloneKeepsItsLinkToThePalette, ADuplicatedCelStillPaintsFromTheSameSwatch`
  - Repro: paint with a palette swatch, duplicate the cel along the timeline (or generate an inbetween), then recolour the swatch. The original changes and the copy does not. Same for a gradient.
  - Cause: `Stroke.Clone` copied nine properties and not `SwatchId` or `GradientId`, so a cloned stroke fell back to its literal colour. It is what `DocumentEditor.CloneFrame` and the inbetweener both use, so it reached cel copy, cel duplication, drag-with-copy and every AI inbetween.
  - Fix: copy them. The list is exhaustive now and says so, because a field added to a stroke and missed here does not fail — it goes quiet. Found while writing break-link, which needed the same copy. Cost: S

- [ ] **B17** `P2` `canvas` Guides are invisible over the drawing `evidence: manual`
  - Repro: place any guide on a new document. It shows on the grey surround and vanishes the moment it crosses the canvas.
  - Cause: mine, and the comment I wrote made it sound deliberate. `DrawGuides` ran before the artwork on the reasoning that "a ruler on paper is something you draw over" — but a new document opens with an opaque background layer, so under the drawing means under a sheet of white. The analogy does not survive an opaque bottom layer.
  - Fix: draw guides over the artwork, translucent. The thing the old order was protecting — not hiding the drawing — is paid for with alpha instead.
  - `evidence: manual` because the chrome is drawn by a Skia custom draw op that the headless test platform never leases; the composited snapshot the pixel tests read does not contain it. Verify by eye: a guide must be visible across the canvas, and the art must still read through it. Cost: S

- [ ] **B8** `P3` `ui` Timeline context submenu flickers under a pen `evidence: manual`
  - Repro: right-click a timeline cel with a pen and hover "Insert frame". The submenu flickers and will not stay open. A mouse is fine.
  - Cause: not investigated. Pen hover events arrive as a different device with its own enter/leave pattern; the submenu almost certainly closes on a spurious leave.
  - **Recorded, not being worked on** at the user's request. Cost: ?

- [x] **B7** `P3` `transform` Transform does not affect gradients `evidence: TransformingAGradient_MovesItsAxis`
  - Repro: lay a gradient, Ctrl+T, move it. The ramp does not follow.
  - Cause: reproduced — it is the region filter, and only with a selection. Without one the ramp does follow. `MajorityInside` counts a stroke's points, and a gradient's two points are the ends of its axis, not a centreline; the ramp colours the whole layer regardless of where they sit. A marquee drawn straight over a visible gradient reported "nothing to transform in this scope".
  - Fix: judge a gradient by what it covers. It joins any region-limited transform and moves whole, which is the rule the filter already followed for everything else.
  - Cost: M

## Fixed

Entries move here when `sync` closes them; the evidence stays so a deleted
test reopens the bug.
