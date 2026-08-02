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

- [ ] **B29** `P2` `canvas` A full recomposite costs about 20 ms a layer, so the playhead cannot be dragged `evidence: manual`
  - Repro: a 3-layer scene, drag the playhead. Measured p95 **124 ms** at 720p against a 16 ms interaction budget — the cliff is at **12 frames**, which is well inside the cache, so this is not B28. Scrubbing is simply a full recomposite and a full recomposite is not cheap.
  - The same cost, seen four ways in `PERFORMANCE.md`: recompositing by layer count breaks at **4 layers** at 1080p (476 ms at 24); onion skin breaks with **no ghosts at all**, because drawing one costs a recomposite before a single ghost is blended; playback is over its 83 ms period; scrubbing is over its 16 ms one. One cause, four symptoms — filed once, on purpose. Filing the symptoms separately would have four people fixing the same blend loop.
  - Cause: each visible layer is a full-canvas `DrawBitmap` of a premultiplied 1080p bitmap, so a recomposite is *layers × canvas area* of software src-over. Linear in layers — measured **n^0.99**, so nothing is accidentally quadratic — and the constant is simply large: about 20 ms per 1080p layer on this container.
  - Why drawing does not suffer: `ComposeRing` repaints only the region a stroke touched, which is the whole reason that class exists. The gap is that **a frame change has no equivalent** — it always goes wide.
  - Fix: unmeasured, so this names candidates rather than a plan. A composite cache for the unchanged layers below and above the active one turns N blends into 2 on the common case, and matches how an artist works — one layer moves, the rest are wallpaper. Failing that, the same trick the presenter already uses: composite at display resolution rather than document resolution when the result is only going to be shown. Measure both against the sweep before choosing. Cost: L
  - Found by `tools/Lightbox.Bench`, not by a report. Nobody had measured a frame change because every budget in the charter measures one stroke on one frame.

- [ ] **B30** `P3` `canvas` A busy drawing takes over a second to rebuild from its strokes `evidence: manual`
  - Repro: rasterise a frame of 800 strokes at 1080p. Measured p95 **1594 ms**, and it passes the 100 ms per-action budget at about **100 strokes** — which is not a busy drawing, it is an ordinary one.
  - Paid on every cache miss, every undo past the top of the stack, and every frame of an export. B28 makes it worse by turning misses from occasional into constant, but the two are independent: fixing the eviction policy leaves this exactly where it is.
  - Cause: **n^1.03** — linear in strokes, so the replay itself is honest and the constant is the whole story at roughly 2 ms a stroke. Invariant 1 says the pixels are derived from the record, and this is the price of that; the question is not whether to replay but whether every miss has to replay from nothing.
  - Fix: unmeasured. The obvious candidate is a periodic raster checkpoint on a cel — keep the strokes as the record, keep a rendered snapshot beside it, and replay only what came after. That is a cache with a persistence question attached, so it wants its own design note rather than an afternoon. Cost: L
  - P3 rather than P2 because a miss is currently rare on a scene that fits the cache. Re-rank it upward if B28 is fixed by making misses cheaper rather than rarer.

- [ ] **B28** `P2` `timeline` Past the frame cache's size, playing or scrubbing misses every single time `evidence: manual`
  - Repro: a 3-layer scene at 1280×720. A cel is 3.5 MB, so 48 frames is 506 MB against `FrameBitmapCache.ByteBudget` of 512 MB, and 96 frames is 1 GB. Walk the sheet in order — which is what playback does, and scrubbing, and export.
  - Cause: an LRU against a sequential scan, which is the one access pattern LRU is worst at. Walking 96 frames evicts the frames at the start to make room for the ones at the end, so by the time the playhead comes round again *everything it is about to ask for has just been thrown away*. The hit rate is not degraded, it is **zero** — the cache stops being a cache and every frame is re-rasterised from strokes. Found by the sweep in `tools/Lightbox.Bench` refusing to terminate at 192 frames, which is the same fact stated less politely.
  - It is a cliff and not a slope, which is what makes it worth its own entry: at 48 frames everything is instant and at 96 nothing is, with no warning in between. It also gets *worse* with the canvas — the same scene at 1080p crosses at 21 frames, and at 4K at 5.
  - Fix: not a bigger budget, which only moves the cliff. Two candidates worth measuring against each other. **MRU eviction while playing** is the textbook answer to a sequential scan and costs one flag on the cache. **A resolution ladder** — cache playback frames at display size rather than document size — is what actually matches the use, since nobody inspects 4K detail at 12 fps, and it multiplies capacity rather than shifting it. Both together are probably right, and neither should be built before the sweep can measure whether it worked. Cost: M
  - Charter §3's memory objective (O4) already says caches are sized in bytes rather than item count, and this one is. Being correctly sized is not the same as being correctly *evicted*, which is the gap this fell through.

- [ ] **B26** `P2` `brush` Paint load thins the whole stroke instead of running out along it `evidence: manual`
  - Repro: paint with Oil, which ships `PaintLoad = 0.6`. The mark is 40% transparent from the first dab to the last, evenly. Measured interior alpha 0.600 against 1.000 for the same brush with no medium — exactly the load, applied as a flat multiplier.
  - Cause: `PaintLoad` says "paint on the brush at the start of a stroke. Below 1 the brush runs out as the stroke lengthens, which is what dry-brush is", and `MediumSimulator.SeedFromCoverage` reads it as `pigment = density * c * load`. There is no arc length in that expression, so there is nothing for the brush to run out of. It is a transparency slider wearing a dry-brush label.
  - Consequence: oil and gouache are body colour and they render translucent, which is half of "gouache and oil have no height". It also means the one control that would give a stroke a *beginning and an end* — the loaded start, the dragged-out tail — currently gives it neither.
  - Fix: depletion needs distance along the stroke, which the simulator does not have: it works off the scratch surface's coverage, deliberately, so it never has to know how the stroke was drawn. Cheapest honest route is for `BrushEngine` to bake remaining load into dab alpha as it stamps, which puts it where arc length already lives and leaves the medium pass alone — and which makes it work for a dry brush with no medium at all. Cost: M

- [ ] **B27** `P3` `brush` A wet medium barely bleeds past where the brush went `evidence: manual`
  - Repro: one watercolour stroke at `Wetness = 0.85`. Mark height 40 px with no flow at all, 42 px at 4 flow steps, 46 at 8, 48 at 16, 48 at 24. Twenty per cent, and flat by 16 steps — the artist's "none seem to spread like a moisture-rich medium".
  - Cause: unconfirmed. Suspect `EntryHead` (0.15), the capillary entry pressure that pins the wet front. It is what stops a wash creeping forever and it is what makes a rim possible at all, so it is not simply too high — but seeded water tops out near 1.0 at full coverage and falls off fast at the fringe, so the front pins about two cells out and then nothing more happens however long the solver runs. Worth checking whether the seeded depth scale, not the threshold, is the wrong half of the comparison.
  - Fix: measure where the front stops and why before changing a constant. The regression test is the height series above: it must keep rising with flow steps rather than flattening. Cost: M

- [ ] **B23** `P2` `brush` BristleDrag and Pickup are read by nothing `evidence: manual`
  - Was four settings: `Body`, `Relief`, `BristleDrag`, `Pickup`, measured at **0 of 41 600 pixels different** between all-0 and all-1. Two of them are now implemented and this entry is what is left.
  - **Done — `Body` and `Relief`.** `Impasto.Shade` takes the paint's own coverage as its height, differences it for a normal, and lights it from the upper left. Measured over the solid body of an oil stroke, luminance range 0.007 flat → **0.535** thick, with the upper flank at 0.465 against the lower at 0.276 — lit from where the light is, not merely noisier. No height buffer, so no per-layer state outside the document and no threat to invariant 1; the price is that two crossing strokes do not build a ridge where they meet, each being shaded from its own coverage. Evidence: `ThickPaintIsModelled_AndFlatPaintIsNot`, `TheRaisedEdgeCatchesTheLight`, `PaintWithNoBodyRendersExactlyAsItAlwaysDid`, `ShadingPaintsNoExtraCoverage`, `ReliefIsAsRepeatableAsEverythingElse`.
  - **Left — `BristleDrag` and `Pickup`.** Both are `DESIGN-fluid-media.md` piece (3), the directional advection loop, which the design deliberately sequences last and behind two open questions. They also need something the medium pass does not have: `MediumSimulator` works off the scratch surface's coverage on purpose, so it never learns which way the stroke was going, and a comb or a drag needs exactly that. The design note's own answer for the *appearance* of `BristleDrag` is a directional tip with `AngleFollowsDirection`, which is the brush-tip-texture work queued next — so the cheap half arrives from there, and only `Pickup` really wants the loop.
  - Until then the honest alternative for the two remaining controls is still to **hide them** — a setting that does nothing is worse than an absent one, charter O7. Cost: M

- [x] **B24** `P2` `brush` Watercolour's edge pooling inverts at the value it ships with `evidence: EdgePull_RespondsMonotonically, EdgePull_BuildsARimInsteadOfMottlingTheMiddle`
  - Repro: paint a Watercolor wash and look at the rim. Rim-to-interior density ratio measured down the middle of a stroke: EdgePull 0.0 → 1.22, **0.4 → 1.83** (a darkened rim, which is what pooling looks like), 0.8 → 0.68, 1.0 → 0.65. Above roughly 0.5 the rim comes out *lighter* than the interior, the opposite of a wet edge. **The Watercolor preset ships `EdgePull = 0.7`**, so the flagship wet brush sits on the wrong side of the peak.
  - Re-measured on a dried disc once B25 was in, rim over middle across EdgePull 0 → 1: **0.50, 0.50, 0.49, 0.48, 0.46, 0.44**. Worse than reported, and simpler: the response was not a peak that fell off after 0.5, it fell the whole way, and the rim was never darker than the middle at any setting. EdgePull made the edge *lighter*, monotonically. The number the original repro caught was a different band on a pipeline that was throwing most of the pigment away.
  - Cause: the potential, not the rate. The term climbed the gradient of film thinness, `1/(1+water)` — and inside a wash that field is bumpy, because the paper's tooth is in it and so is every eddy the flow left behind. So it was local gradient ascent on a surface covered in local maxima, and every one is a trap. Pigment walked a cell or two, found a dip, and stopped. Measured: interior roughness (scatter around the radial mean, so a smooth fade does not count) went **0.13 → 2.04** from EdgePull 0 to 1. Sixteen times the mottling and no rim — which is precisely the artist's "it still only looks like a texture created from noise".
  - Fix: make the potential the **distance to dry paper** instead. A chamfer field has no local extrema by construction, so pigment that starts moving keeps moving until it arrives; two raster sweeps, less than the smoothing pass the old comment here feared, and 242 ms against 238 ms on the 400×400×12 budget. Water depth moves from *defining the direction* to *gating the rate*, which is where it belongs: deep water carries pigment, a film about to dry pins it, so the drift stops at the rim on its own. `EdgeRate` 0.25 → 0.9, because a term that no longer traps pigment two cells from where it started needs to advect a whole cell per step to reach a boundary twenty cells away.
  - After: rim/middle **0.50, 0.81, 1.28, 1.97, 2.89, 4.03** — monotone, and a real ring at the top. Roughness 0.13 → 0.25 instead of 0.13 → 2.04. Through the stroke path, wet edge over centre at the shipped `EdgePull = 0.7` is **1.71**, against 0.97 with pooling off. The preset needed no re-tuning after all. Cost: M

- [x] **B25** `P3` `brush` A simulated medium paints three to five times fainter than the same brush without one `evidence: Drying_PutsEveryGrainOnThePaper, HowLongTheSolverRuns_DoesNotDecideHowMuchPaintLands, DryingAMarkThatNeverFlowed_LeavesItExactlyWhereItWasStamped, FlowStepsDecideWhereThePaintGoes_NotHowMuchOfItThereIs, AMediumThatNeverFlows_StillPaintsTheStroke`
  - Repro: one stroke, `MediumKind.None` against `Watercolour`. Interior ink density measured 0.66 flat, 0.13–0.25 simulated. The wash is so pale that granulation and paper texture are most of what survives, which is why it reads as noise rather than as pigment.
  - Also measured: spread is nearly absent — mark height 45 px flat, 42 px at 4 flow steps, 47 at 12, 50 at 24. About 10% at the top of the range, where a moisture-rich medium should visibly bleed past where the brush went.
  - And `FlowSteps = 0` renders **nothing at all** rather than degrading to the stamped dabs, so zero is an accidental off-switch for the paint rather than for the flow.
  - Cause: the lattice conserved pigment and the readback did not. `Deposit` binds a fraction of the suspension per step; `ReadDeposit` reports only what is bound; whatever was still suspended when the loop ended was dropped on the floor. `TotalPigment` counted suspension, so the conservation tests stayed green while most of the paint never reached a pixel — the sum was right and the picture was wrong. The tell was that the *flow* control set the *opacity*: measured interior alpha 0.06 at 2 flow steps, 0.18 at 8, 0.29 at 24, and 0 at 0.
  - Fix: `FluidLattice.Dry()` — the stroke is over, so the wash is over: the water goes and every remaining grain is left where it stands. Bound in place rather than by running the solver down to dryness, because a cell with no water has no velocity, so the extra sweeps would move nothing. Measured after: ink mass 3034 at 0 flow steps and 2785 at 24 (the 8% is faint fringe falling under the visibility threshold), against roughly 5× before. `FlowSteps = 0` now paints the stamped mark at the medium's pigment density. Cost: M
  - Two things this did not fix, both logged rather than folded in: spread is still only +20% (**B27**), and `PaintLoad` still thins the whole stroke instead of running out along it (**B26**), which is the rest of why oil looks translucent.

- [x] **B22** `P2` `colour` A duplicated cel loses its link to the palette `evidence: ACloneKeepsItsLinkToThePalette, ADuplicatedCelStillPaintsFromTheSameSwatch`
  - Repro: paint with a palette swatch, duplicate the cel along the timeline (or generate an inbetween), then recolour the swatch. The original changes and the copy does not. Same for a gradient.
  - Cause: `Stroke.Clone` copied nine properties and not `SwatchId` or `GradientId`, so a cloned stroke fell back to its literal colour. It is what `DocumentEditor.CloneFrame` and the inbetweener both use, so it reached cel copy, cel duplication, drag-with-copy and every AI inbetween.
  - Fix: copy them. The list is exhaustive now and says so, because a field added to a stroke and missed here does not fail — it goes quiet. Found while writing break-link, which needed the same copy. Cost: S

- [x] **B17** `P2` `canvas` Guides are invisible over the drawing `evidence: GuidePainter, GuidePainterTests, AGuideIsVisibleOverAnOpaqueDrawing, TheArtStillReadsThroughIt`
  - Repro: place any guide on a new document. It shows on the grey surround and vanishes the moment it crosses the canvas.
  - Cause: mine, and the comment I wrote made it sound deliberate. `DrawGuides` ran before the artwork on the reasoning that "a ruler on paper is something you draw over" — but a new document opens with an opaque background layer, so under the drawing means under a sheet of white. The analogy does not survive an opaque bottom layer.
  - Fix: draw guides over the artwork, translucent. The thing the old order was protecting — not hiding the drawing — is paid for with alpha instead.
  - Was `evidence: manual`, and no longer is. The obstacle was real — the rig is painted inside a Skia lease the headless platform never grants — but the answer was to move the painting somewhere a test can call rather than to give up: `GuidePainter` is pure Skia and takes a canvas, and `PaintDocument` owns the checkerboard/artwork/guides order, because splitting those three apart is exactly how the bug happened. Putting the guides back underneath fails five of the seven tests. Cost: S

- [x] **B8** `P3` `ui` Timeline context submenu flickers under a pen `evidence: CelDragGesture, CelDragGestureTests, OpeningAContextMenuCancelsThePendingDrag, LettingGoDisarmsTheGesture`
  - Repro: right-click a timeline cel with a pen and hover "Insert frame". The submenu flickers and will not stay open. A mouse is fine.
  - Cause: not a spurious leave, which was the guess. A pen right-click is a press-and-hold, so the press armed the cel drag, the hold opened the menu, and moving towards the submenu crossed the six-pixel threshold and started a drag — which seized the pointer and shut the menu. "A mouse is fine" is the detail that pins it: a mouse right-click never passes the left-button guard, so it never arms anything.
  - Fix: a context menu and a drag are two readings of one press and only one can win, so opening the menu cancels the gesture. Releasing cancels it too — the arming press used to be cleared only by a move that found the button up, so lifting without moving left it armed for any later movement. Both rules live in `CelDragGesture` rather than in a handler, which is also what made them testable. Cost: S

- [x] **B7** `P3` `transform` Transform does not affect gradients `evidence: TransformingAGradient_MovesItsAxis`
  - Repro: lay a gradient, Ctrl+T, move it. The ramp does not follow.
  - Cause: reproduced — it is the region filter, and only with a selection. Without one the ramp does follow. `MajorityInside` counts a stroke's points, and a gradient's two points are the ends of its axis, not a centreline; the ramp colours the whole layer regardless of where they sit. A marquee drawn straight over a visible gradient reported "nothing to transform in this scope".
  - Fix: judge a gradient by what it covers. It joins any region-limited transform and moves whole, which is the rule the filter already followed for everything else.
  - Cost: M

## Fixed

Entries move here when `sync` closes them; the evidence stays so a deleted
test reopens the bug.
