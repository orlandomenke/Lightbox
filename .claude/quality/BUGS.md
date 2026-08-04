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

- [x] **B48** `P1` `ui` Docking a floating panel crashed the application `evidence: FloatingPanelTests, APanelCanBeTornOutAndDockedAgainWithoutCrashing, ADockedPanelIsInTheStripRatherThanParkedInThePool, FloatingAndDockingRepeatedlyStaysStable`
  - Repro: drag a panel out so it floats in its own window, then drag its header back onto a dock zone. `NullReferenceException`, and the manual promised this worked.
  - **Two bugs on one line, and the crash was the less serious of them.** `CloseFloating` did `Park(window.Release())`. Docking a floating panel reaches it through `ApplyDockLayout`, which detaches every panel it is about to restrip — so the window's `Content` was already gone and `Release` had nothing to give back.
  - `Release` was `(Docker)Content!` and **the cast did not throw**: casting a null reference succeeds and yields null, so the `!` silenced the warning and passed the null along. The exception surfaced two frames later in `MainWindow.Detach`, a long way from the line that was wrong — which is the general lesson worth keeping. A `!` does not assert; it suppresses, and it moves the failure somewhere less informative.
  - The second bug is what a crash fix alone would have left: even without the throw, parking whatever the window handed back would have pulled the freshly docked panel **straight out of the strip it was just dropped into** and stored it in the pool — present in the layout and invisible. Two journeys end in `CloseFloating` and they need opposite things: closing the window must park the panel, docking it must not.
  - Fix: `Release` returns a nullable and `CloseFloating` parks only what the window still holds.
  - **`FloatingPanelWindow` was the one control in the docking system the codemap listed as `[NO TESTS]`**, which is where I looked first and where it was. The round trip is now covered six ways, including three trips in a row — once is a crash, three times is the state machine. Five of the six fail against the old code, and the failure cascades: the crash left the layout half-applied and took nine tests down with it.
  - Cost: S

- [x] **B47** `P1` `project` Save as had no shortcut, and export and status never checked for a file `evidence: SaveRequirement, SaveGate, SaveFirstDialog, SaveFactsFor, SaveRequirementTests, SaveAndStatusGateTests, ShortcutRegistrationTests, AFileThatIsNoLongerThereIsNotTheSameAsNeverSaved, AStatusChangeAsksAboutItsOwnRowRatherThanTheActiveTab`
  - Repro, as reported: Ctrl+Shift+S does nothing. Then, separately: export and auto-export happily run on a drawing that was never saved, and a status can be set on one.
  - Cause of the first half, and it is duller than a broken handler: `Ctrl+S` was an `InputGesture` on the Save menu item and was never in `ShortcutMap`. So it could not be rebound, it did not appear in the shortcut editor — and because nothing enumerated it, nobody ever asked the next question, which is that **Save as had no gesture at all.** It was not a broken shortcut; it was never a shortcut. Found by writing the guard for the process rule rather than by looking for it.
  - Cause of the second half: nothing connected "is this on disk" to the two actions that depend on it. An export claims *this is what the drawing looks like*; a status claims *this is finished, go and use it*, and with auto-export on it immediately writes a sheet for an engine to collect. Both are claims about a file made to somebody else, and both were being made about files that did not exist.
  - Fix: one pure gate over three facts — is there a path, is there a file at it, are there edits the file lacks — shared by export and status so they cannot drift into disagreeing about what "saved" means. A status change is **prohibited** until there is a file, with two answers and no third: *Save file as…* or *Revert status change*. There is deliberately no "do it anyway", because that is offering a mistake.
  - **`SaveInPlaceFirst` is deliberately not blocking.** The artist already said where the file goes, so writing to it needs no permission and a dialog there would be a click between "mark Ready" and it being Ready.
  - **"The file was moved" is kept apart from "never saved"**, because the fix differs and so does the honest wording — writing back to a remembered path would put the file in a folder somebody deliberately emptied.
  - The gate asks about the **row being marked**, not the tab in front. Gating on the active tab would let one row through unsaved or block another for a reason not on screen.
  - Caught a real case immediately: `WorkspaceTests` asserted that clicking Ready on a freshly duplicated animation set the status, and a duplicate is explicitly not written yet — `DuplicateSelected` says "Save to write it to disk". The test was asserting the old behaviour; it now asserts both the refusal and, after a save, the success.

- [x] **B45** `P1` `brush` Every brush changes the moment you let go `evidence: LiveMatchesCommittedTests, IncrementalDabWalkTests, AHardRoundInkStrokeIsTheSameLiveAndCommitted, ASimulatedMediumLooksTheSameLiveAndCommitted, PaintLoadDepletesWhileDrawingAndNotOnlyOnRelease, GrowingAStrokeOnePointAtATimeGivesTheSamePixels`
  - Repro: draw with almost anything and watch the pen lift. Reported from the artist's side as five separate complaints, all one bug: the mark shrinks slightly all round; airbrush and the media lose opacity; the media's paint load is invisible while drawing and then depletes on release (oil worst — filling an area, "only a single stroke survived"); imported `.abr` brushes are much darker live than committed; and the tip texture jumps, sometimes a lot.
  - Cause: `FlushLivePreview` built a `tail` stroke from `points.Skip(lastStamped - 1)` and handed it to the engine as if it were a stroke of its own. The dab walk is a fold with four pieces of carried state and a tail restarts all four — the spacing phase (so the walk emits an extra dab at arc length zero on **every pointer event**, which is why heavily overlapped brushes came out denser live), the distance `travelled` that `LoadAt` depletes against (so paint load never depleted until commit), the `heading` a direction-following tip uses, and the step size. And because a dab that lands elsewhere is *seeded* elsewhere, every `Hash01` dynamic re-rolled on release: that is the texture jump.
  - Second half, found by measurement rather than by reading: a dab's position is not settled when it is generated. `Densify` interpolates a span from its neighbours, so the newest span straightens once the next point arrives. Usually a sub-pixel move — but with scatter 0.35 on a 30 px brush that re-seeds the dab and throws it ten pixels. Renders were identical at 0.85 px spans and 206/255 out at 8.7 px spans, which is what localised it.
  - Fix: the engine walks the whole stroke and the caller says which dabs to *draw* (`WalkDabs` / `StampDabRange`). Settled dabs are stamped permanently; the unsettled tail is stamped so the mark reaches the pen tip, its pixels backed up, and taken back on the next event. Holding the tail back instead was tried and measured — 4% short on a long stroke, 39% short on a six-event flick — and rejected.
  - Three checks, all on the same evidence: pixel numbers, side-by-side images, and a human. Before: ink mean 3.30/255, airbrush 7.88, oil 20.27, scatter 37.25, ink coverage up to 1.053×, paint load 239→239 live against 238→0 committed. After: **mean 0.00 and coverage 1.000 on every one**, load 238→0 in both.
  - Mine, from the incremental-preview work. Cost: L

- [ ] **B46** `P3` `brush` The live preview re-densifies and re-walks the whole stroke every pointer event `evidence: IncrementalDensify, APointerEventStaysInBudgetDeepIntoALongStroke`
  - Not a defect an artist can see today; a scaling one, and recorded because it was introduced knowingly. Fixing B45 required the live preview to walk the whole stroke rather than the newest two points, which turned O(1) work per pointer event into O(n).
  - Measured, and attributed rather than guessed: 0.41 ms over the first fifty events of a 600-event stroke and 1.70 ms over the last fifty. `GeometryOps.Densify` is 0.84 ms of the 1.15 ms walk at 600 points, so it is the majority of it and it is pure re-computation — appending one point changes only the last two spans of its output.
  - Fix: densify incrementally. `Densify` already works span by span, and a span's geometry depends only on one point behind and two ahead, so the cached output can have its tail recomputed and everything before it reused. That also makes the walk resumable, since the settled prefix's dabs are already cached.
  - Accepted for now because the budget holds with room: a pointer event 550 events into one stroke costs well under a fifth of a 60 Hz frame, and `APointerEventStaysInBudgetDeepIntoALongStroke` guards that line and prints the growth so this can be measured against something.

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
  - **Verified against the real paint before believing the number.** The suspicion was that the sweep's own `SKPaint` had put it on a slow path, since four measurements this session had the right figure and the wrong attribution. It had not: the sweep's paint is character-for-character what `SceneRenderer.DrawPass` builds. Timed at 1920×1080 on this container — **no paint at all 18.53 ms, an empty paint 18.60, `DrawPass`'s paint 18.80.** Under one and a half per cent between them. The blit itself is the cost, at about 9 ns a pixel, so there is no cheap fix hiding in the paint and the structural ones below are the only ones there are.
  - Two things that fell out of the same measurement and sharpen the picture. A layer at **partial opacity costs 29% more** than an opaque one (23.93 ms), so layer opacity is not free. And an **onion tint costs 59% more** (29.43 ms, the `SrcIn` colour filter), so a ghost is 1.6 plain layers — which is why the onion row is the worst of the four symptoms and not merely the deepest.
  - Fix: unmeasured, so this names candidates rather than a plan. A composite cache for the unchanged layers below and above the active one turns N blends into 2 on the common case, and matches how an artist works — one layer moves, the rest are wallpaper. Failing that, the same trick the presenter already uses: composite at display resolution rather than document resolution when the result is only going to be shown. Measure both against the sweep before choosing. Cost: L
  - Found by `tools/Lightbox.Bench`, not by a report. Nobody had measured a frame change because every budget in the charter measures one stroke on one frame.

- [ ] **B30** `P3` `canvas` A busy drawing takes over a second to rebuild from its strokes `evidence: manual`
  - Repro: rasterise a frame of 800 strokes at 1080p. Measured p95 **1594 ms**, and it passes the 100 ms per-action budget at about **100 strokes** — which is not a busy drawing, it is an ordinary one.
  - Paid on every cache miss, every undo past the top of the stack, and every frame of an export. B28 makes it worse by turning misses from occasional into constant, but the two are independent: fixing the eviction policy leaves this exactly where it is.
  - Cause: **n^1.03** — linear in strokes, so the replay itself is honest and the constant is the whole story at roughly 2 ms a stroke. Invariant 1 says the pixels are derived from the record, and this is the price of that; the question is not whether to replay but whether every miss has to replay from nothing.
  - Fix: unmeasured. The obvious candidate is a periodic raster checkpoint on a cel — keep the strokes as the record, keep a rendered snapshot beside it, and replay only what came after. That is a cache with a persistence question attached, so it wants its own design note rather than an afternoon. Cost: L
  - P3 rather than P2 because a miss is currently rare on a scene that fits the cache. Re-rank it upward if B28 is fixed by making misses cheaper rather than rarer.

- [x] **B28** `P2` `timeline` Past the frame cache's size, playing or scrubbing misses every single time `evidence: EvictionOrder, AnLruScanThrowsAwayEverythingItIsAboutToNeed, EvictingTheMostRecentKeepsHalfTheSheetResident, ScanEvictionIsBetterThanLruOnAScan, TheFrameBeingShownIsNeverTheOneEvicted, PlayingSwitchesTheCacheToScanEviction`
  - Repro: a 3-layer scene at 1280×720. A cel is 3.5 MB, so 48 frames is 506 MB against `FrameBitmapCache.ByteBudget` of 512 MB, and 96 frames is 1 GB. Walk the sheet in order — which is what playback does, and scrubbing, and export.
  - Cause: an LRU against a sequential scan, which is the one access pattern LRU is worst at. Walking 96 frames evicts the frames at the start to make room for the ones at the end, so by the time the playhead comes round again *everything it is about to ask for has just been thrown away*. The hit rate is not degraded, it is **zero** — the cache stops being a cache and every frame is re-rasterised from strokes. Found by the sweep in `tools/Lightbox.Bench` refusing to terminate at 192 frames, which is the same fact stated less politely.
  - It is a cliff and not a slope, which is what makes it worth its own entry: at 48 frames everything is instant and at 96 nothing is, with no warning in between. It also gets *worse* with the canvas — the same scene at 1080p crosses at 21 frames, and at 4K at 5.
  - Fix: `FrameBitmapCache.EvictionOrder`, set to `MostRecent` for the duration of a scan. Evicting the newest stops the scan destroying itself — the frames that arrived first stay resident and the tail of the sheet fights over what is left, so a sheet twice the capacity goes from **every** frame missing to about half hitting. Half a cache beats none. The frame being shown is explicitly never the victim, or the cache would be a no-op on exactly the frame the artist is looking at. LRU stays the default because while drawing, recency does predict reuse.
  - Not a bigger budget, which only moves the cliff. The other candidate — caching playback frames at display resolution rather than document resolution — is still worth doing and multiplies capacity rather than shifting it; it is a bigger change and wants its own measurement, so it is not folded in here.
  - **The test for the defect nearly did not work**, and the reason is worth keeping: counting cached frames and bytes to detect a hit reports every *miss* as a hit, because a miss that inserts one entry and evicts one leaves both totals exactly where they were. It read 24 of 24 hitting on a cache that was thrashing completely. Bitmap identity is the honest signal. Cost: M
  - Charter §3's memory objective (O4) already says caches are sized in bytes rather than item count, and this one is. Being correctly sized is not the same as being correctly *evicted*, which is the gap this fell through.

- [x] **B26** `P2` `brush` Paint load thins the whole stroke instead of running out along it `evidence: LoadAt, ALoadedBrushStartsFullAndRunsOut, AFullBrushNeverRunsOut, ItWorksWithoutASimulatedMedium, LoadIsNotAppliedTwiceWhenAMediumIsOn, ABiggerBrushCarriesFurther`
  - Repro: paint with Oil, which ships `PaintLoad = 0.6`. The mark is 40% transparent from the first dab to the last, evenly. Measured interior alpha 0.600 against 1.000 for the same brush with no medium — exactly the load, applied as a flat multiplier.
  - Cause: `PaintLoad` says "paint on the brush at the start of a stroke. Below 1 the brush runs out as the stroke lengthens, which is what dry-brush is", and `MediumSimulator.SeedFromCoverage` reads it as `pigment = density * c * load`. There is no arc length in that expression, so there is nothing for the brush to run out of. It is a transparency slider wearing a dry-brush label.
  - Consequence: oil and gouache are body colour and they render translucent, which is half of "gouache and oil have no height". It also means the one control that would give a stroke a *beginning and an end* — the loaded start, the dragged-out tail — currently gives it neither.
  - Fix: `BrushEngine.LoadAt` — remaining paint decays exponentially with distance travelled, measured between dab centres so it is the path the paint took after spacing and smoothing have had their say. It lives in the dab walk because that is where arc length is; `MediumSimulator` works off the scratch surface's coverage on purpose and never learns how the stroke was drawn. A side effect worth having: it now works for a brush with **no medium at all**, which is what a dry brush actually is.
  - The length scale is in brush diameters, so resizing a brush does not silently re-tune its load. Load 1 returns exactly 1 and is a byte-for-byte no-op, so nothing already drawn moves. And `MediumSimulator` no longer multiplies pigment by it — with depletion in the dab walk that would have applied it twice, and there is a test for exactly that.
  - **Dab overlap is what made this hard to tune**, and it is worth writing down. At the usual spacing a dozen dabs land on any given pixel, so the mark's alpha is `1-(1-a)^12` and saturates: at the first setting tried, a load of 0.35 faded from 1.000 to 0.858 over seven hundred pixels — not a brush running out, a brush thinking about it. The per-dab curve has to fall much further than the visible mark does, which is why the reach constant looks aggressive read on its own. Measured after: 1.000 → 0.214 → 0.000 across the same stroke. Cost: M

- [ ] **B27** `P3` `brush` A wet medium barely bleeds past where the brush went `evidence: BleedReach, AWetMediumBleedsPastTheBrush, AWetterBrushBleedsFurther, ItKeepsSpreadingTheLongerItRuns`
  - Repro: one watercolour stroke at `Wetness = 0.85`. Mark height 40 px with no flow at all, 42 px at 4 flow steps, 46 at 8, 48 at 16, 48 at 24. Twenty per cent, and flat by 16 steps — the artist's "none seem to spread like a moisture-rich medium".
  - Suspected `EntryHead` pinning the front, and **that was wrong**. Measured directly on a disc of radius 12 over 32 steps, the front reaches 17 at water depth 0.5, 19 at 1, 22 at 2 and 26 at 4. The model spreads perfectly well; it was never being asked to.
  - Cause, in two parts. The seeded depth *was* too shallow — `Wetness` mapped straight to 0..1 while `EntryHead` is 0.15 absolute, so the mark's soft fringe sat under the threshold and could not flow, and the lattice's own note says to seed around 0.5–3. Fixing that alone changed nothing, which is what pointed at the real wall: **`MediumSimulator` runs inside the region `DabReach` computed, and `DabReach` is the geometry of the stamps.** It knows nothing about fluid. The lattice edge was two pixels outside the mark and everything downstream faithfully simulated a wash in a box.
  - Fix: `BrushEngine.BleedReach` pads the region by how far the medium can carry, scaled by wetness and flow steps and capped at the brush width — a margin is area, area is the cost of painting, and invariant 6 says painting is bounded work. Zero without a medium, so ordinary drawing pays nothing. `WetnessDepth` maps Wetness onto the depth range the lattice documents.
  - After: a 40 px stroke reaches **58 px** at 24 steps against 40 with no flow — 45%, against 20% before — and it keeps growing with the control (44 → 52 → 62 across 4, 12 and 32 steps) rather than flattening at 48 whatever you asked for. Cost: M
  - The lesson is the one this session keeps relearning: the measurement was real and the attribution was not. Two constants would have been changed on the strength of a plausible story if the front had not been measured first.

- [x] **B35** `P2` `brush` A medium stroke hollows out down its middle `evidence: AWetStrokeKeepsPigmentDownItsMiddle`
  - Repro: one stroke with each of Watercolor, Gouache, Oil. All three left a pale line down the centre with the pigment banked on the flanks.
  - Cause: `EdgePull` moves pigment toward the wet boundary, and `MediumSimulator.Apply` runs **once over the whole stroke** — so the boundary is the mark's own outline, and the centre, being furthest from dry paper, empties. Nothing was wrong with `FluidLattice`; the presets asked it for too much.
  - Measured, mean alpha along the centre against a flank 14 px out, on the shipped watercolour: **centre 3.0 against flank 55.6, a ratio of 0.05.** That is the white line, as a number. The sweep: 0.05 at EdgePull 0.70, 0.18 at 0.35, 0.42 at 0.20, 0.83 at 0.10, **1.11 at 0.06**, 1.74 with no pull at all.
  - Fix: Watercolor 0.70 → **0.06**, Gouache 0.15 → **0.05**, Oil 0.05 → **0.02**. At 0.06 the centre holds 41.6 alpha and the flank sits at 37.4 — above the 35.4 it reaches with no pull at all, so there is still a real rim, with a live centre inside it.
  - The rim was never wrong; it was turned up until it was the only thing left. A confident stroke is not a drying puddle. Cost: S
  - **The test only discriminates on watercolour**, and that is worth saying: Gouache and Oil already sat at 0.15 and 0.05, so their ratios passed before the change. Watercolour is where the bug was, and the threshold fails loudly at anything near 0.70.

- [x] **B36** `P3` `brush` The three medium presets have no tip dynamics at all `evidence: EveryMediumPresetHasATipAndSomeVariation`
  - Repro: the same three strokes. The flanks showed regular perpendicular ticks at the dab interval rather than a dragged mark.
  - Cause: none of the three set `TipId`, `AngleFollowsDirection` or any jitter, so every dab was the same soft circle at the same angle and what was left to see was the stamping interval. The artist's own diagnosis, and right about the flanks.
  - Fix, one per medium rather than one for all three, because they are not the same brush:
    - **Oil** — `bristle` + `AngleFollowsDirection`, the pairing `DESIGN-fluid-media.md` names as the cheapest directional-drag answer in the engine, plus a little size and rotation jitter.
    - **Gouache** — `paintbrush` (the chisel) + `AngleFollowsDirection`: a loaded flat brush turned to the stroke.
    - **Watercolor** — `wet-edge` with size and roundness jitter and *no* heading. A wash edge is not directional; what it wants is an irregular boundary.
  - All jitter is seeded from dab position through `Hash01`, so the mark varies and still replays identically — invariant 2 intact, and it is why this is visual variation rather than logical randomness.
  - **Not visually verified.** The tests assert each preset declares a tip and some variation, which is a shape check; whether the mark now *reads* as a dragged brush is an art-direction question. A contact-sheet render was attempted and abandoned when the throwaway project referencing `Lightbox.App` took over ten minutes to build. Worth a look by eye before this is trusted — the bristle tip's starburst bug was found exactly that way and no test saw it. Cost: M

- [x] **B37** `P1` `brush` Blur turns semi-transparent art opaque black `evidence: AnEffectBrushDoesNotMakeAWashMoreOpaqueThanItWas`
  - Repro: medium strokes on a layer, then Blur across them on the same layer. One thin blur came out as a blurred core inside a solid black surround.
  - Cause: `StampBlur` and `StampBlurDraft` drew the blurred snapshot once per dab with no blend mode set — so `SrcOver` — laying a semi-transparent copy over the previous one. A watercolour stroke is black pigment at low alpha, so stacking its alpha a dozen times over pure black goes opaque. Bare canvas beside the stroke gained alpha from the blur's own spread and stacked that too, which is the surround.
  - Fix: `SKBlendMode.Src`. A blur **replaces** its dab with the softened version; there was never a reason to composite a second copy over the first.
  - Measured: peak alpha on a wash of 60 stays **60** after a blur. It used to climb to opaque. Cost: S
  - **Smudge and blender have the same runaway and it is not fixed — that is B38.** Splitting them because the fixes are not the same size: blur replaces a region and `Src` says exactly that, while a smudge has to blend and there is no blend mode for it.

- [x] **B38** `P1` `brush` Smudge and blender turn semi-transparent art opaque black `evidence: AnEffectBrushDoesNotMakeAWashMoreOpaqueThanItWas, ASmudgeStillMovesColourRatherThanDoingNothing, AWholeSmudgeStrokeStaysAffordable`
  - The other half of B37. A smudge deposited the colour it sampled **at the sampled alpha**, over what the last dab left, so the canvas fed its own opacity back in. Measured on a wash of alpha 60: **peak alpha 60 → 249.** That is the black smear, as a number.
  - Fix: the deposit is now an explicit lerp — `dst = lerp(dst, deposit, w)` with `w` the dab's falloff — because **no Skia blend mode expresses it.** `SrcOver` gets the colour right (it is already a lerp toward the source) and the alpha wrong (it adds instead of interpolating), and that difference *is* the bug. Treating alpha as an ordinary channel is what makes both halves work: a dab over a wash converges on the wash's own opacity instead of climbing past it, and a dab reaching off the edge of a mark can still carry pigment into bare canvas, because zero becomes a value to move away from rather than a place that cannot be painted.
  - `SrcATop` was tried first and reverted — it holds the destination alpha but paints nothing where the destination is transparent, which broke smudging onto bare canvas and broke all-layers sampling outright. Nine tests named it. The reason is left in the code so it is not tried again.
  - **The pixel loop needed two corrections, both measured.** `GetPixel`/`SetPixel` are a P/Invoke each and cost **10,810 ms** for one 4K stroke against a 400 ms interaction budget; spans over `PeekPixels` brought it to **273 ms** — 40×, and the same lesson `DESIGN-performance.md` already records from the first time this engine did it. And the lerp is done on **premultiplied** values, which is both the faster and the more correct operation: no unpremultiply per pixel and no divide by zero where alpha is nil.
  - `SmudgeCostTests` now holds a budget for this path, because it is a managed pixel loop and invariant 6 says painting is bounded work: 960×540 at size 40 costs 16 ms, 4K at size 180 costs 273 ms.
  - **It also fixed the flow control.** Carried colour across flow 0.08 → 0.85 used to read 0, 21, 21, 26, 34 — plateauing, because the accumulation dominated. It now reads 0, 17, 34, 47, 64: monotonic and roughly linear. Flow on a smudge is finally a dial rather than a switch, which is the other half of the artist's original complaint.
  - **One thing this broke and the fix for it.** B33 had the draft path read a pristine pre-stroke copy for *every* effect brush, and that is right for blur and wrong for smudge: a blur re-derives its dab from pre-stroke pixels, but a smudge **carries** — each dab has to see what the previous ones left, which is how a smear travels. `TheSmudgePreviewMatchesTheCommit` caught it. The pristine base is now blur-only. Cost: M

- [x] **B33** `P2` `brush` The live blur covers more ground than the mark that commits `evidence: ALiveBlurDoesNotCoverMoreGroundThanTheMarkThatCommits`
  - Reported as "while dragging there is artifacting on the affected dab — it visually enlarges in a bigger radius than just the brush".
  - Cause: `FlushLivePreview` handed `_liveComposite` to `FrameRasterizer.AppendDraft` as **both** the surface to write and the pixels to read. The exact render gives every dab of a stroke the same *pre-stroke* pixels — one effect pass per stroke — but the preview re-snapshotted its own accumulated output on every pointer event, so a pixel under a dozen overlapping dabs was blurred a dozen times. N passes of sigma s reach like s·√N, and each pass drags colour in from three sigma further out, so the mark spread well past the brush as the drag went on.
  - Fix: `_liveEffectBase`, a second copy that is never written to, is what the effect samples; `_liveComposite` is still what it writes into. `AppendDraft` grew a `readFrom` parameter, defaulting to the layer so nothing else changes.
  - Measured over 60 pointer events, 40 px brush, flow 0.6: the preview covered **672 px more** than the commit before, **88 px after** — a 7.6× reduction, and the threshold in the test sits between the two so it discriminates. `maxDiff` fell from 255 to 89.
  - **Not fully closed by this, and the entry says so:** 88 px of over-coverage remain, because the draft path still works from a per-segment cropped snapshot rather than the whole pre-stroke layer. Filed as B34.
  - **Smudge was measured and is not affected** — 453 differing pixels before, 351 after, ink counts already matching. It carries colour through its own state rather than by re-reading pixels, so it never compounded. The fix is a small improvement there and no more; the artist's report was about blur.
  - Three probes were wrong before one was right, and the reason is worth keeping: the first walked sideways along the opaque test bar and measured **the bar**; the second dragged along the bar's middle, where blurring uniform black is a **no-op by construction**; both passed against the unfixed code. Counting inked pixels while dragging along the bar's *edge* is what finally discriminated. The same lesson as the saturation trap — the number was real and the attribution was not. Cost: S

- [x] **B34** `P3` `brush` The live blur over-covered by ~88 px `evidence: ALiveBlurDoesNotCoverMoreGroundThanTheMarkThatCommits`
  - What was left of B33 after the pristine-base fix: the preview still covered 88 px more than the commit, and `maxDiff` was still 89.
  - **Closed by B37, and the attribution here was wrong.** This entry blamed `StampBlurDraft`'s per-segment cropped snapshot, and proposed padding the subset or reading the whole layer. Neither was needed. Re-measured after B37 changed the blur's per-dab draw from `SrcOver` to `Src`: over-coverage is **-2 px**, which is noise. The residual was never the crop — it was the *same accumulation* as B33, seen from the other end, and one fix closed both.
  - The lesson is the one this file keeps recording: two symptoms with one cause look like two bugs until one of them is fixed. Filing the residual separately was right; guessing its cause without measuring was not.
  - `ALiveBlurDoesNotCoverMoreGroundThanTheMarkThatCommits` now asserts a **20 px** ceiling rather than the 200 it was loosened to for this bug's sake — a threshold with 200 px of slack would not notice either bug returning. Cost: none, closed by another fix.

- [x] **B35** `P2` `brush` A medium stroke hollows out down its middle `evidence: AWetStrokeKeepsPigmentDownItsMiddle`
  - Repro: one stroke with each of Watercolor, Gouache, Oil. All three left a pale line down the centre with the pigment banked on the flanks.
  - Cause: `EdgePull` moves pigment toward the wet boundary, and `MediumSimulator.Apply` runs **once over the whole stroke** — so the boundary is the mark's own outline, and the centre, being furthest from dry paper, empties. Nothing was wrong with `FluidLattice`; the presets asked it for too much.
  - Measured, mean alpha along the centre against a flank 14 px out, on the shipped watercolour: **centre 3.0 against flank 55.6, a ratio of 0.05.** That is the white line, as a number. The sweep: 0.05 at EdgePull 0.70, 0.18 at 0.35, 0.42 at 0.20, 0.83 at 0.10, **1.11 at 0.06**, 1.74 with no pull at all.
  - Fix: Watercolor 0.70 → **0.06**, Gouache 0.15 → **0.05**, Oil 0.05 → **0.02**. At 0.06 the centre holds 41.6 alpha and the flank sits at 37.4 — above the 35.4 it reaches with no pull at all, so there is still a real rim, with a live centre inside it.
  - The rim was never wrong; it was turned up until it was the only thing left. A confident stroke is not a drying puddle. Cost: S
  - **The test only discriminates on watercolour**, and that is worth saying: Gouache and Oil already sat at 0.15 and 0.05, so their ratios passed before the change. Watercolour is where the bug was, and the threshold fails loudly at anything near 0.70.

- [x] **B36** `P3` `brush` The three medium presets have no tip dynamics at all `evidence: EveryMediumPresetHasATipAndSomeVariation`
  - Repro: the same three strokes. The flanks showed regular perpendicular ticks at the dab interval rather than a dragged mark.
  - Cause: none of the three set `TipId`, `AngleFollowsDirection` or any jitter, so every dab was the same soft circle at the same angle and what was left to see was the stamping interval. The artist's own diagnosis, and right about the flanks.
  - Fix, one per medium rather than one for all three, because they are not the same brush:
    - **Oil** — `bristle` + `AngleFollowsDirection`, the pairing `DESIGN-fluid-media.md` names as the cheapest directional-drag answer in the engine, plus a little size and rotation jitter.
    - **Gouache** — `paintbrush` (the chisel) + `AngleFollowsDirection`: a loaded flat brush turned to the stroke.
    - **Watercolor** — `wet-edge` with size and roundness jitter and *no* heading. A wash edge is not directional; what it wants is an irregular boundary.
  - All jitter is seeded from dab position through `Hash01`, so the mark varies and still replays identically — invariant 2 intact, and it is why this is visual variation rather than logical randomness.
  - **Not visually verified.** The tests assert each preset declares a tip and some variation, which is a shape check; whether the mark now *reads* as a dragged brush is an art-direction question. A contact-sheet render was attempted and abandoned when the throwaway project referencing `Lightbox.App` took over ten minutes to build. Worth a look by eye before this is trusted — the bristle tip's starburst bug was found exactly that way and no test saw it. Cost: M

- [x] **B37** `P1` `brush` Blur turns semi-transparent art opaque black `evidence: AnEffectBrushDoesNotMakeAWashMoreOpaqueThanItWas`
  - Repro: medium strokes on a layer, then Blur across them on the same layer. One thin blur came out as a blurred core inside a solid black surround.
  - Cause: `StampBlur` and `StampBlurDraft` drew the blurred snapshot once per dab with no blend mode set — so `SrcOver` — laying a semi-transparent copy over the previous one. A watercolour stroke is black pigment at low alpha, so stacking its alpha a dozen times over pure black goes opaque. Bare canvas beside the stroke gained alpha from the blur's own spread and stacked that too, which is the surround.
  - Fix: `SKBlendMode.Src`. A blur **replaces** its dab with the softened version; there was never a reason to composite a second copy over the first.
  - Measured: peak alpha on a wash of 60 stays **60** after a blur. It used to climb to opaque. Cost: S
  - **Smudge and blender have the same runaway and it is not fixed — that is B38.** Splitting them because the fixes are not the same size: blur replaces a region and `Src` says exactly that, while a smudge has to blend and there is no blend mode for it.

- [x] **B38** `P1` `brush` Smudge and blender turn semi-transparent art opaque black `evidence: EffectArtefactTests, AnEffectBrushCannotRaiseAlphaAboveWhatItCouldHaveSampled, ADraftEffectCannotRaiseAlphaEither, TheLivePreviewAccumulationCannotRaiseAlphaEither, TheReportedConfigurationCannotRaiseAlphaEither, TheStrokeStillDoesSomething`
  - The other half of B37. A smudge deposits the colour it sampled at *the sampled alpha*, over what the last dab left. So the canvas feeds its own opacity back in: alpha rises, the next sample reads the risen alpha, and ten overlapping dabs at `Spacing = 0.1` take 0.24 to 0.72 over pure black. The sampled colour was right and the accumulation was not.
  - **`SKBlendMode.SrcATop` was tried and is wrong.** It is `Src*Da + Dst*(1-Sa)`, which holds the destination's alpha — exactly what is wanted — but paints *nothing where the destination is transparent*. That breaks smudging onto bare canvas, and it breaks all-layers sampling outright, where the colour comes from the backdrop and the destination layer is empty by definition. Nine tests failed and named them: the four `SampleSourceTests`, `SmudgeBrush_DragsExistingColor_AndReplaysDeterministically`, `TheSmudgePreviewMatchesTheCommit`, `SmudgeShowsMidDrag`, `ThatChangesWhatTheLayerRendersAs`. Reverted, with the reason left in the code so it is not tried twice.
  - What a deposit actually means is *lerp the canvas toward the sampled colour, weighted by the dab falloff*, and Skia has no single blend mode for that. The shape that works in this engine is the paint path's: accumulate into a stroke-local scratch at full strength and composite once (`ComposeDraftRegion`). Effect brushes never got that because they mutate the target directly.
  - A regression test must smear something with **alpha below 1** — every pre-existing smudge test uses an opaque bar, where `SrcOver` and `Src` are indistinguishable, which is why this lived so long. Cost: M
  - **Closed with real evidence rather than a claim.** `EffectArtefactTests` measures the invariant that actually discriminates: *a smudge moves colour, so it cannot produce alpha higher than the highest alpha it could have sampled.* Over a soft radial wash whose peak is 60, the smudge peaks at **49** and the blur at **59**, with **zero** opaque pixels — committed, draft, and accumulated over twenty pointer events. The `evidence: manual` this entry carried was a claim; these are numbers.

- [ ] **B39** `P1` `brush` Effect brushes still leave hard black artefacts mid-stroke, some surviving the release `evidence: manual`
  - Reported after B37 and B38 landed: "smudge, blur and blender now work, but while painting there is still artefacting, sometimes persisting after mouse release." The screenshot shows a **hard-edged opaque black band** along part of a smudge stroke, plus a smaller black dash, inside an otherwise correct soft grey smear on a transparent layer over white paper.
  - Hard edges and a straight-sided band are the shape of **whole dab patch rects** going opaque, not of a falloff going wrong: `LerpDab` writes an axis-aligned patch per dab, and a chain of them along a diagonal reads as exactly this band.
  - **Two hypotheses eliminated, so nobody re-runs them.** (1) *`ColorRate` mixing the foreground colour in*: `BrushSettings.ColorRate` defaults to **0** and the Smudge preset does not set it, so `Mix(deposit, ownColor, 0)` is a no-op and the black is not the palette's black arriving that way. (2) *`SampleAverage` reading premultiplied bytes*: it uses `GetPixel`, which unpremultiplies, and `LerpDab` re-premultiplies before lerping — consistent.
  - **Still to check, in order of suspicion.** `LerpDab` indexes the read bitmap with `x * 4` and `y * srcRow` using the **target's** device-clip coordinates while assuming the read bitmap is `Rgba8888` and the same extent; a read surface with a different colour type, row stride or size would make `si` point at the wrong bytes. Separately, `StampSmudge` applies `target.Scale(outputScale)` before the dab loop while `LerpDab` computes device-space rects and then calls `DrawImage(image, left, top)` through that same transform — at `outputScale != 1` the patch is placed and scaled wrongly, which is a real export-path defect whether or not it is this one.
  - The regression test has to smear over a **partially transparent** region and count pixels that came out opaque where the source was not, because every pre-existing smudge test uses an opaque bar where the failure cannot appear. Cost: M
  - **Eight measurements, all clean — so it is not `BrushEngine`'s pixel maths.** `EffectArtefactTests` was built for this and closed B38 instead. Over a soft radial wash whose peak alpha is 60, on a diagonal drag across the blob's *edge* into bare canvas: committed smudge peaks at **49** at both flow 0.08 and 0.5, blur at **59**, the draft path at the same, an accumulation over twenty pointer events at **49**, and the reported configuration — **147 px** brush on **1920x1080** with the preset's shipped defaults — at **52**. Zero opaque pixels in every case, and 2435 px genuinely changed, so none of it is vacuous.
  - **What that eliminates.** The deposit maths, the falloff, the premultiplication, the draft path, per-event accumulation, and brush size. Together with the two hypotheses ruled out earlier (`ColorRate`, the sample's premultiplication), the artefact is not reachable from `StampStroke` with a translucent source on one layer.
  - **So the search moves up a layer**, and the remaining candidates are all in the App: `_liveComposite`'s creation (an `SKAlphaType.Opaque` surface would render transparent-black as **opaque black**, which is exactly the reported colour and would explain hard edges at a region boundary), `FrameRasterizer.AppendDraft`'s region bookkeeping, and the publish/compose path that puts the live surface on screen. The `outputScale` defect noted above is still real and still worth fixing on its own.
  - **What would narrow it fastest**, since it does not reproduce headlessly: whether it still happens on a *fresh single-layer document* with the Background deleted, and whether the Paint layer's blend mode was Normal. If it needs the second layer, it is the compositing path and not the brush at all.

- [x] **B40** `P2` `ui` Ticking View > Symbols does nothing `evidence: manual`
  - Reported as "Symbols / asset browser seems to be a docker, but selecting it never opens it."
  - Not the docking machinery: `IsPanelUsable` gates both Symbols and Project on `HasProject`, deliberately, because a symbol lives on the project above the animations that place it. With no project open the panel is correctly absent — and the **menu item was not**, so it could be ticked and nothing happened.
  - Fix: both menu items get `IsVisible="{Binding HasProject}"`, so the item is absent exactly when the panel would be. "Optional means absent" applies to the control that reaches a feature as much as to the feature.

- [x] **B41** `P3` `ui` Tool-options sliders and checkboxes sit above their labels `evidence: manual`
  - The bar is a fixed 30 px row and its children default to `VerticalAlignment=Stretch`; a stretched `Slider` draws its track at the top of the height it is given and a stretched `CheckBox` does the same with its box — so Size, Hardness, Opacity and "Per brush" all sat high against the text beside them.
  - Fix: styles on `OverflowBar`'s descendants rather than an alignment attribute on some forty individual controls, so a control added later is centred without anybody remembering to.

- [x] **B42** `P3` `ui` Icon glyphs are clipped instead of fitting their tile `evidence: manual`
  - The `icon` tiles were sized for a 13 px label and several glyphs are 15-16 px with ascenders, so they were cut rather than shrunk.
  - Fix: a minimum tile of 26x24 with centred content and no clipping, on every `Button.icon` and `ToggleButton.icon`, so a row of them still reads as a row.

- [x] **B43** `P3` `ui` The onion toggle in a timeline layer row is off-centre `evidence: manual`
  - The two toggles sat in identical tiles relying on the default content alignment, and a geometric glyph and an emoji do not centre the same way inside one.
  - Fix: explicit `HorizontalContentAlignment`/`VerticalContentAlignment` on both, zero padding, and a matched font size.

- [x] **B44** `P3` `ui` The tool-options overflow control reads as part of the workspace picker `evidence: manual`
  - `OverflowBar`'s chevron lands at the bar's right edge, immediately left of the workspace combo — two adjacent chevrons that read as one group belonging to whichever is on the right.
  - Fix: the button says **More tool options** with the chevron rather than a bare chevron, and a vertical rule separates the bar from the picker.

- [x] **B33** `P2` `brush` The live blur covers more ground than the mark that commits `evidence: ALiveBlurDoesNotCoverMoreGroundThanTheMarkThatCommits`
  - Reported as "while dragging there is artifacting on the affected dab — it visually enlarges in a bigger radius than just the brush".
  - Cause: `FlushLivePreview` handed `_liveComposite` to `FrameRasterizer.AppendDraft` as **both** the surface to write and the pixels to read. The exact render gives every dab of a stroke the same *pre-stroke* pixels — one effect pass per stroke — but the preview re-snapshotted its own accumulated output on every pointer event, so a pixel under a dozen overlapping dabs was blurred a dozen times. N passes of sigma s reach like s·√N, and each pass drags colour in from three sigma further out, so the mark spread well past the brush as the drag went on.
  - Fix: `_liveEffectBase`, a second copy that is never written to, is what the effect samples; `_liveComposite` is still what it writes into. `AppendDraft` grew a `readFrom` parameter, defaulting to the layer so nothing else changes.
  - Measured over 60 pointer events, 40 px brush, flow 0.6: the preview covered **672 px more** than the commit before, **88 px after** — a 7.6× reduction, and the threshold in the test sits between the two so it discriminates. `maxDiff` fell from 255 to 89.
  - **Not fully closed by this, and the entry says so:** 88 px of over-coverage remain, because the draft path still works from a per-segment cropped snapshot rather than the whole pre-stroke layer. Filed as B34.
  - **Smudge was measured and is not affected** — 453 differing pixels before, 351 after, ink counts already matching. It carries colour through its own state rather than by re-reading pixels, so it never compounded. The fix is a small improvement there and no more; the artist's report was about blur.
  - Three probes were wrong before one was right, and the reason is worth keeping: the first walked sideways along the opaque test bar and measured **the bar**; the second dragged along the bar's middle, where blurring uniform black is a **no-op by construction**; both passed against the unfixed code. Counting inked pixels while dragging along the bar's *edge* is what finally discriminated. The same lesson as the saturation trap — the number was real and the attribution was not. Cost: S

- [ ] **B34** `P3` `brush` The live blur still over-covers by ~88 px because the draft snapshot is cropped per segment `evidence: manual`
  - What is left of B33. `StampBlurDraft` extracts a subset of the pre-stroke pixels padded by `DabReach + sigma*4` and blurs *that*, so a dab near the crop's edge samples the crop boundary instead of the real neighbourhood — a slightly different mark from the committed one, which blurs the whole layer.
  - Measured: 88 px of over-coverage and `maxDiff` 89 remain after B33, against 672 px and 255 before it.
  - Fix: either pad the subset by enough that no dab can see its edge, or let the draft path read the full pre-stroke layer and accept the copy. The second is simpler and the cost is one full-canvas snapshot per stroke, which the effect path already pays twice — worth measuring before choosing. Cost: S
  - P3 because 88 px on a 40 px brush is not what the artist reported; 672 px was.

- [ ] **B32** `P3` `ai` A third of the Windows download is a second copy of .NET, because the MCP server targets net10.0 `evidence: manual`
  - Repro: publish both projects as CI does and measure. **286 MB on disk, 105 MB zipped** — of which the app is 69.5 MB and `mcp/` is **35.3 MB**, a full second self-contained runtime.
  - It is not accidental duplication that a shared output folder would fix. `Lightbox.App` targets `net8.0` and `Lightbox.Mcp` targets **`net10.0`** (with `Microsoft.Extensions.Hosting 10.0.10`), so of the 224 files in `mcp/`, **182 have the same name as one in the parent and different contents** and only 2 are byte-identical. They are genuinely two runtimes.
  - Nothing chose this: `CLAUDE.md` says the project is .NET 8, and the TFM has read `net10.0` since the commit that created the project. Every other project in the solution is `net8.0`.
  - Fix: move `Lightbox.Mcp` to `net8.0` and publish it into the *same* folder as the app, so the runtime is shared. Saves roughly a third of every download and a third of every CI artifact. Needs checking that `ModelContextProtocol` 1.4.1 supports `net8.0` and that the two `runtimeconfig.json`/`deps.json` pairs coexist — they have distinct names, so they should. Cost: S
  - P3 because it costs bandwidth and storage rather than correctness. It moved up the list when the artifact quota filled: at 105 MB a build a free account's whole 500 MB allowance is four builds, and this is 35 MB of each one.
  - **The fix above is now one of two, pointing opposite ways, and the choice is not this entry's to make.** Moving the whole solution *up* to `net10.0` removes the duplication from the other end — one runtime because everything targets it — and buys the support window with it, .NET 8 leaving support in November 2026. Assessed in `docs/DESIGN-net10-upgrade.md`; the part that needs a human is **Q19**, because a `net10.0` self-contained Linux build raises the minimum glibc from 2.23 to 2.27 and nobody has written down who runs this. Downgrading the MCP server stays the right *interim* move if the upgrade grows a test-stack migration — the measured 35 MB a build is real either way, and this entry should not sit open waiting on a decision it does not depend on.

- [ ] **B31** `P2` `ai` Every AI call re-renders and re-encodes the reference views on the UI thread `evidence: ReferenceViewsAreEncodedOnceNotPerCall, TheEncodedViewIsReusedWhileTheSheetIsUnchanged`
  - Repro: a document with two character-sheet views, then **✦ AI Inbetween** twice. `CollectReferenceImages` runs synchronously in `AiInbetweenAsync` before the request is built, and it composes, PNG-encodes and base64s each visible view every time.
  - Measured: **52 ms** to encode and base64 one 960×540 view (38 ms at 768×432), so the default two views cost about **100 ms of UI-thread stall before every AI call**. Timed on dense synthetic line art, which is the pessimistic end; a sparse sheet is cheaper but not free.
  - It is pure waste, not merely slow: the sheet has not changed between calls, so the bytes produced are identical every time. `_cache.Get` already memoises the *layer* render — what is uncached is `Compose` + PNG + base64, which is the expensive half.
  - Fix: memoise the encoded string per view, keyed on something that actually changes when the drawing does. Invalidate where `OnDocumentChanged` already funnels every edit. Cost: S
  - Two things fall out of the same measurement and belong in whatever commit closes this. **Cap the long edge** on the request rather than on the view — providers bill by area, so 768 px is 442 image tokens against 691 and 244 KB against 333 KB, and the artist's sheet stays whatever size they drew it. And **WebP halves the bytes but doubles the encode** (106 ms against 52); on the UI thread that is a bad trade, and once the cache exists it is free to reconsider.
  - Context and the rest of the payload arithmetic: `docs/DESIGN-ai-payload.md`. The images are ~87% of a request's bytes and ~5% of its tokens, which is why this is a latency bug and not a cost one.

- [ ] **B23** `P2` `brush` BristleDrag and Pickup are read by nothing `evidence: manual`
  - Was four settings: `Body`, `Relief`, `BristleDrag`, `Pickup`, measured at **0 of 41 600 pixels different** between all-0 and all-1. Two of them are now implemented and this entry is what is left.
  - **Done — `Body` and `Relief`.** `Impasto.Shade` takes the paint's own coverage as its height, differences it for a normal, and lights it from the upper left. Measured over the solid body of an oil stroke, luminance range 0.007 flat → **0.535** thick, with the upper flank at 0.465 against the lower at 0.276 — lit from where the light is, not merely noisier. No height buffer, so no per-layer state outside the document and no threat to invariant 1; the price is that two crossing strokes do not build a ridge where they meet, each being shaded from its own coverage. Evidence: `ThickPaintIsModelled_AndFlatPaintIsNot`, `TheRaisedEdgeCatchesTheLight`, `PaintWithNoBodyRendersExactlyAsItAlwaysDid`, `ShadingPaintsNoExtraCoverage`, `ReliefIsAsRepeatableAsEverythingElse`.
  - **Left — `BristleDrag` and `Pickup`.** Both are `DESIGN-fluid-media.md` piece (3), the directional advection loop, which the design deliberately sequences last and behind two open questions. They also need something the medium pass does not have: `MediumSimulator` works off the scratch surface's coverage on purpose, so it never learns which way the stroke was going, and a comb or a drag needs exactly that. The design note's own answer for the *appearance* of `BristleDrag` is a directional tip with `AngleFollowsDirection`, which is the brush-tip-texture work queued next — so the cheap half arrives from there, and only `Pickup` really wants the loop.
  - **The cheap half has arrived.** `TipShape.Bristle` bakes a solid disc with narrow scratches subtracted from it, and with `AngleFollowsDirection` on it is the directional-tip answer the design note pointed at — so an artist who wants the *look* of a dragged bristle now has it, without the advection loop and without a setting that lies. That does not close this entry: it is a different control in a different place, and `BristleDrag` on `MediumSettings` still reads by nothing.
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

- [ ] **B39** `P2` `project` CI runs the tests on a .NET 8 runtime it never asks for `evidence: manual`
  - Repro: provision a Linux container with only the SDK `build.yml` names — `10.0.x` — and run `dotnet test Lightbox.sln`. The whole solution compiles and not one test runs. The error names a missing framework, so it reads as a broken test project rather than as the SDK choice that caused it.
  - The .NET 10 SDK builds a `net8.0` target and cannot run one. No `runtimeconfig.template.json` exists anywhere in the repo and nothing sets `RollForward`, so the default `Minor` applies — it does not cross a major version, and a `net8.0` test assembly will not fall back to the 10.0 runtime.
  - Cause: `ubuntu-latest` preinstalls .NET 8, so `actions/setup-dotnet` naming only `10.0.x` lands on a machine that already had the runtime. The dependency is inherited from the runner image rather than declared in the workflow, which is why nothing in `build.yml` mentions it and why it has never failed.
  - It has an expiry date. When GitHub drops .NET 8 from the runner image after .NET 8 leaves support in November 2026, this workflow starts failing for a reason its own YAML does not contain.
  - Fix: name both runtimes in `build.yml`'s `setup-dotnet` step (a multi-line `dotnet-version` of `8.0.x` and `10.0.x`) so the requirement is declared where it is used. `.devcontainer/devcontainer.json` already installs both and records why. The whole entry disappears if the solution moves to `net10.0` — one runtime, and it is the SDK's own. Cost: S
  - P2 because it blocks work rather than corrupting art, and today it blocks it only for someone setting up a fresh Linux environment — which is exactly what `.devcontainer` exists for, and exactly the person least able to diagnose a missing-framework error in an unfamiliar repo. See `docs/DESIGN-net10-upgrade.md`.

- [ ] **B40** `P2` `brush` No determinism evidence survives leaving the process `evidence: manual`
  - Repro: not a failure to observe, an absence to check. `EffectBrushes_AreDeterministic_AcrossRerenders`, `FillStroke_SurvivesDocumentSerialization_PixelForPixel`, `ClippedStroke_ReRendersIdenticallyFromJsonAlone`, `WithoutACamera_TheExportIsByteForByteWhatItAlwaysWas` and `OutputScaleTests` all compare exactly — and both sides of every one of them are computed in the same process. Change the render by one bit between runtimes and all of them still pass.
  - What they prove is worth having and is not this: that the engine is a function, consulting no clock, no RNG and no uninitialised memory. Invariant 2 asks for that. It does not follow that the function is the same function it was on another runtime, because nothing compares against a value produced by one.
  - `Hash01` is not the exposure, which is the part that misdirects. All three copies — `BrushEngine`, `PaperField`, `TipGenerator` — are FNV-then-avalanche over `uint`/`int` seeded through `BitConverter.SingleToInt32Bits`, so the language specifies every bit and a runtime cannot change what they return. The exposure is the float arithmetic around them: scatter turns a hash into an angle and moves the dab by `Math.Cos`/`Math.Sin`, and transcendental results are not guaranteed bit-identical across runtime versions. One ULP moves a dab a fraction of a pixel, which changes an antialiased edge, which changes the file — and looks like nothing at all until two machines disagree about one document.
  - Fix: `tests/Lightbox.Raster.Tests/RuntimeDeterminismTests.cs` is the instrument and it is already in the tree; what closes this is its `Baseline` constant being filled in from a real run. Deliberately `evidence: manual` rather than naming the test — `bugs.py sync` can see that a file exists and cannot see whether a constant inside it is still empty, so naming it would tick this box while the guard was inert. Cost: S
  - P2, and it is the ordering that earns it rather than the severity. The baseline has to be recorded on the runtime being left behind; recorded after a TFM bump it captures the new runtime as the reference and destroys the only evidence that would have shown a change. So this is cheap now and impossible later. See `docs/DESIGN-net10-upgrade.md`.

## Fixed

Entries move here when `sync` closes them; the evidence stays so a deleted
test reopens the bug.
