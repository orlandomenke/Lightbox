# Roadmap

What Lightbox is for, in the order that matters. Six pillars carry the
identity; a seventh carries the floor everything stands on.

**The checkboxes are generated, not asserted.** Every item names the types,
tests or files that would exist if it were built, and
`python3 scripts/roadmap.py sync` resolves those against
`.claude/codemap/map.json` and rewrites the mark:

| | meaning |
| --- | --- |
| `[x]` | every anchor resolves — built |
| `[~]` | some resolve — started, or the anchors have gone stale |
| `[ ]` | none resolve — not started |
| `[?]` | no evidence declared — **unverifiable**, add anchors or admit it is a wish |

Deleting a feature un-ticks its box on the next run. A roadmap that only ever
moves forward is a wish list.

```bash
python3 scripts/roadmap.py check   # status, exits 1 if the file has drifted
python3 scripts/roadmap.py sync    # rewrite the marks
python3 scripts/roadmap.py next    # what to pick up
python3 scripts/roadmap.py stats   # one line per pillar
```

Adding an item: write the line, then add `evidence:` naming what will prove
it. If you cannot name the proof, the item is not specified yet — that is
what `[?]` is telling you.

---

## Pillar 0 — The drawing floor

Not a differentiator, and the reason it comes first anyway: nobody switches to
a tool whose brushes are worse, however good the pipeline is. The bar here is
*credible*, not *best-in-class* — the identity is built in pillars 1–6. Every
item that a painting app is simply expected to have lives here.

### Brush engine

- [x] High-performance brush engine `evidence: BrushEngine, StampStroke, LargeCanvasPerformanceTests, PerformanceTests`
- [x] **What a swept soft mark's ceiling should be** `evidence: docs/DESIGN-swept-ceiling.md, SweptCeilingTests, CeilingReachPx`
  - Q157 holds a soft mark down to the brush's own footprint, which fixed an edge that came out half as wide as a single dab. **B349 is what that ceiling prints**: the footprint is a maximum of dab shapes, and where the mark saturates it *becomes* that surface — ridges and all — which an artist reads as lines down an airbrushed patch.
  - The cheap repairs are ruled out by measurement rather than by taste. A ripple is peak-to-trough, so an operator that never lowers the ceiling cannot flatten one; an operator that lowers it clips a lone dab against its own profile. Four were built and each fails one of the two. Sampling the footprint four times more finely does not help either — the ridges are the real shape of the maximum along the path, not a discretisation of it.
  - So this is a question about the model: **is a ceiling per-point the right idea at all**, or should it build with the passes the way paint does? What comes out has to keep Q157's edge, keep B27's measured wash spread, and re-render every existing document the same way or say plainly that it does not. **A design note before any code.**
  - **Answered and landed 2026-09-02** (`docs/DESIGN-swept-ceiling.md`, B349): the ceiling stays per-point, and what changes is what the point is measured against — the brush's falloff applied to the pixel's distance from the edge of everything the stroke reached, instead of the best any single dab does there. It is a relaxation of the old ceiling by construction, so it keeps Q157's edge to the byte and cannot clip a lone dab; it is flat between passes where the maximum dipped; and the distance transform that computes it costs a live event a tenth of a millisecond fit-to-window. Existing documents re-render with fewer ridges and no other difference, and the seven fingerprints that say so are re-recorded with the reason beside each. Media stay outside it, as Q157 measured they must.
  - Cost: **L**. B349 carries the measurements this starts from.
- [~] **The swept ceiling's timing guard says what it costs relative to something, not in milliseconds** `evidence: SweptCeilingTests, TheDistanceTermIsBoundedAgainstTheCapItReplaces, TheUnboundedTransformStillFailsTheGuard`
  - B363. `TheDistanceTermCostsOneLiveEventLessThanItsBudget` has now failed twice on trees that never touched the brush — on `main` at its original 2 ms, and on PR #526 at 6.287 ms against the 5 ms it was raised to for the runner. It also fails both parameters on the owner's own machine on unmodified `main`. A guard in that state does not report a regression; it reports whatever else was running.
  - The test already takes paired minima of `plain` and `swept` five times over and then compares their difference to a wall-clock constant, which is where the pairing is discarded. B347 and B339 both landed on the same answer for the same symptom: assert the relationship, print the absolutes.
  - What needs deciding first is what the guard should promise — the term bounded against the cap it replaces, or against a frame with the measurement moved off a shared runner, or split into a correctness assertion plus an owner-machine number. The three are different promises and the current one is *a live event stays under a frame*, which is the one a hosted runner cannot keep.
  - Whatever ships must include the half no raise has supplied: proof that the unbounded exact transform this guard exists to catch — 13 and 34 ms on the runner — still fails it. A limit the correct version clears easily is decoration.
  - Cost: **S**, and it is small only once the promise above is chosen.

- [x] Custom brush editor `evidence: BrushPageGeneral, BrushPageEffects, BrushPagePressure`
- [x] Brush presets and tagging `evidence: BrushPreset, PresetStore, BuiltInPresets, BrushPresetList, BrushTagChoices, BrushPresetEditingTests, TagsPersistAndFeedTheFilterList, ABrushNobodyFiledWritesNoTagsKey`
  - "and tagging" was ticked against `BrushCategoryList`, which is the ⚙ window's page list and has nothing to do with tags. Tags are now free text on the preset, absent until one is filed, and the picker collects whatever exists rather than offering a vocabulary written here — the categories worth having are the ones an artist's work has.
- [x] A brush can be edited, updated and saved as a copy `evidence: BrushComparison, SameMark, UpdateSelectedPreset, RevertBrushPreset, BrushPresetEditingTests, BrushComparisonTests, NudgingAnythingLightsTheIndicator, PuttingASettingBackClearsTheIndicator, EverySettingThatReachesPixelsIsCompared`
  - The dot compares values rather than tracking a "touched" flag, so putting a setting back clears it — one that stayed lit would train people to ignore it. The comparison serializes rather than listing properties: a hand-written comparer over forty fields is a list somebody forgets to extend, and the failure is silent.
- [x] The brushes that ship can be overwritten and reverted `evidence: BuiltInPresets, Merge, BuiltInPresetMergeTests, UpdatingAShippedBrushSurvivesARestart, AShadowedBuiltInAppearsOnceAndKeepsItsPlace, RevertingAShippedBrushGivesBackTheOriginal`
  - By shadowing — a user preset reusing the built-in's id, which the merge prefers and which keeps its place in the list. Deleting the shadow uncovers the original, so "revert" is a deletion rather than a stored copy of what shipped.
- [x] Brushes are searched and filtered, not scrolled `evidence: BrushFilter, BrushFilterTests, SearchMatchesATagAsWellAsAName, TwoTagsMeanEitherNotBoth, SearchAndTagsNarrowTogether`
  - A flyout rather than a dropdown. Tags OR between themselves: asking for a brush that is both inking and roughs is almost always an empty list, which reads as the filter being broken. The rules are a pure function rather than a method on the window, because none of them are testable through a flyout.
- [x] A brush is chosen by its mark, not by its name `evidence: BrushPreviewRenderer, BrushChoice, Fingerprint, BrushPreviewRendererTests, BrushPickerTests, EveryBuiltInBrushLeavesAMarkOnItsTile, EditingABrushChangesItsPicture, AnEffectBrushGetsSomethingToWorkOnRatherThanBlankPaper, ABiggerBrushAlwaysReadsBiggerRightAcrossTheRange`
  - Tiles carrying a real swash through `BrushEngine.StampStroke`, so a wet brush shows its rim and a scattered one shows scatter — a hand-drawn swatch would be a second answer to "what does this brush look like" and would start lying the first time the engine changed. Cached on the preset's id **and** its settings, because an id alone would keep showing the mark a brush used to make.
  - **The tile is a portrait, not a measurement**, and that is the one place it departs from what the canvas would get: a 300 px brush at true size shows the flat middle of a mark, so size is mapped logarithmically onto 2.5–24 px and the real number goes in the corner. Clamping to a single ceiling was the first attempt and it made 40 px and 300 px draw the identical picture — found by rendering a contact sheet and looking at it, not by a test.
  - Effect brushes are the exception twice over: they are drawn over a test pattern, because a smudge on clean paper is a blank tile, and they keep their real size, because a blur's character is its absolute-pixel reach. Both were found the same way, and the second turned up **B49** — a shipped brush whose flow had been set on the assumption that per-dab blur accumulates, which it does not — and **B50**.
- [x] A collection can be brought in, tidied and thrown away again `evidence: BrushLibraryWindow, BrushImportJob, BrushImportProgress, DeletePresets, BrushLibraryTests, RemovingACollectionSavesOnceRatherThanOncePerBrush, GivingUpKeepsWhatWasAlreadyRead, ABadFileIsNamedRatherThanCountedAndDoesNotStopTheRest`
  - Importing worked and was a one-way door: fifty-six brushes in, no way to rename or remove them, and the reading ran on the drawing thread for about sixteen seconds (B52). The reading is now a pure function with per-file progress and a cancel; the window is called a **library** rather than "imports" because it manages your own brushes too, and because *library* is what this app already calls a collection of reusable things.
  - **Bulk is a separate method, not a loop.** One save for the whole removal rather than one per brush — the same class of mistake as the import itself, and invisible in the result.
- [x] Brush stabilization (lazy mouse, weighted, predictive) `evidence: SmoothingMode, StrokeFilters, SmoothingTests`
- [x] A fast curve is stamped along the curve, not the chords between pen samples `evidence: Densify, DensifyTests, StampingArcTests, ACurveIsFollowedRatherThanCutAcross, AFastCurveIsInkedRightOutToItsEdge, ADrawnCornerStaysSharp`
- [x] Texture brushes `evidence: PaperField, PaperKind, TexturedBrushTests`
- [x] Smudge, blend and mixer brushes `evidence: SmudgeMode, SmudgeFirstDabTests, MediumSettingsTests`
- [x] Smudge and blur sample all layers, live or frozen `evidence: SampleSource, BakedSample, BakeSample, SampleSourceTests, LiveSampleRebakeTests, ALiveSmudgeFollowsAnEditToTheLayerUnderIt, ABakedStrokeIgnoresABackdropThatChangedUnderIt, AHandDrawnBakedSmudgeFreezesWhatWasUnderIt`
- [x] A project remembers the brush its work is painted with `evidence: BrushScope, BrushScopeDefaults, BrushScopeTests, BrushMemoryTests, ANewDocumentInTheProjectIsFedThatBrush, AProjectThatNeverAsksForThisWritesNoBrushKey`
- [x] Eraser variants `evidence: ToolKind, BrushKind, EraserResurrectionTests`
- [?] Pixel-perfect mode
- [x] Pressure curve editor `evidence: CurveEditor, ResponseCurve, PressureResponse, ResponseCurveTests, PressureCurveTests, BrushCurveUiTests, AnArtistDrawnCurveDoesWhatNoGammaCould, ACurveNeverLeavesTheUnitSquare, TheEditorHandsBackAWholeCurveRatherThanMutatingOne`
  - Was ticked for three gamma sliders, which is an exponent rather than a curve — and `p^γ` is monotone for every γ, so it could not express a brush that widens and then floods. Now a drawn curve per dynamic, monotone cubic so it cannot overshoot past the unit square, seven targets rather than three. The gamma stays as the curve's default shape: every file, preset and imported `.abr` is written in one, so a target without a curve renders exactly as it always did and there is nothing to migrate.
- [x] Pressure drives more than size, flow and hardness `evidence: BrushDynamic, PressureCurveTests, PressureCanOpenTheScatterWithoutReshufflingIt, ADynamicWithNoGammaOfItsOwnIsTurnedOnAsALine`
  - Scatter, roundness, and a smudge's colour rate and drag length. Scatter keeps the hash's direction and scales only the distance, so a harder press spreads the same pattern rather than reshuffling it — invariant 2 from the artist's side.
- [x] A brush has a blend mode `evidence: BlendModes, ForStroke, PressureCurveTests, ABrushBlendModeChangesHowTheStrokeMeetsWhatIsUnderIt, AnEraserIgnoresTheBlendModeEntirely, ABrushThatSetsNoBlendModePaintsExactlyAsItAlwaysDid`
  - Applied once where the stroke's surface meets the layer, not per dab, so a Multiply brush does not go black where it crosses itself. Nullable, so a document that never used one grows no key. The Skia mapping moved down into Raster: a brush's Multiply and a layer's Multiply have to be the same operation.
- [x] The tip is chosen from the brush options, as pictures `evidence: TipChoice, TipPickerList, BrushCurveUiTests, ThePickerOffersRoundFirstAndThenEveryTipThatExists, ChoosingATipCopiesItsPixelsIntoTheDrawing, EveryBuiltInTipHasAThumbnailToShowInThePicker`
  - Two columns of thumbnails in a flyout. A dropdown of names is unusable for shapes — nobody knows what a "Cut nib" looks like until they have seen one, and having seen it they never read the name again.
- [x] Brush rotation, base and per-dab `evidence: BrushSettings, BrushDynamicsTests, AngleFollowsDirection, TipRotationDeg, RotationJitter`
- [ ] Tilt and speed reach the stroke record `evidence: StrokePointTiltTests, AMouseStrokeStoresNoTilt, AnOldFileWithoutTiltStillLoads, TiltIsReplayedNotResampled`
  - Was ticked as part of rotation and is not built: `StrokePoint` is `(X, Y, Pressure)`, no device reads tilt, and nothing stores time — so speed can only be inferred from point spacing, which after `Densify` is a resampling artifact rather than a speed. Optional, absent by default: a mouse has no tilt and 0 means perpendicular, so the two must not be the same value. See `docs/DESIGN-brush-tips.md`.
- [ ] Symmetry and mirrored painting `evidence: SymmetryTests, AMirroredStrokeIsOneRecordNotTwo, SymmetryIsViewOnlyUntilTheStrokeLands`
  - Nothing exists. Krita and Photoshop both have it, and for character design — the thing this application is for — a vertical mirror is not a nicety. The design question was whether a mirrored mark is *one stroke rendered twice* or *two strokes*, and **Q15 answered (c)**: one stroke while drawing, with an explicit "break symmetry" that expands it to two. So `Mirror` lives **on the stroke** rather than on the scene, which is the part that could not be deferred. Unblocked.
- [x] Smoothing is a brush setting, not a global `evidence: BrushStabilisation, BrushStabilisationTests, APresetCarriesItsOwnStabilisation, TwoBrushesCanSteadyTheHandDifferently, ABrushThatFollowsTheApplicationWritesNoStabilisationKey`
  - Nullable, so absent means today's behaviour exactly: one setting for the whole app. A brush that says otherwise overrides it. Not a pixel setting and invariant 4 does not reach it — smoothing filters the pointer samples *before* they become the stroke's points, so the mark already carries the result and there is nothing left to re-run.
- [x] Texture from an image, not only the built-in papers `evidence: TextureRegistry, ImportedTextureTests, AnImportedPaperBitesIntoTheStroke, ThePaperIsAnchoredToTheDocumentRatherThanToTheStroke, AHugeScanIsReducedRatherThanHeldWhole, ATextureThatIsNotRegisteredIsIgnoredRatherThanFatal`
  - The same treatment tips got: an asset with an id, in the document rather than on somebody's disk, absent when unused. Held as a height field rather than as pixels — luminance, so a scan slightly warm or cool reads as the tooth it looks like — and downscaled on import, because a 2400px scan is 144 MB of float for a grain that repeats every few hundred pixels. Anchored to the document, which is what makes two strokes crossing the same patch sit on the same tooth.
- [x] Brush scripting/API `evidence: LightboxTools, IpcDocumentApi`
- [x] Brush importers — .abr / .gbr / .gih / .kpp `evidence: AbrReader, GbrReader, GihReader, KppReader, BrushImportTests`
- [x] Physical media simulation (watercolour, gouache, oil, ink) `evidence: MediumSimulator, FluidLattice, Pigment, MediumRenderingTests`
- [x] A performance map, not only a ratchet — scaling curves, cliffs and a ranking `evidence: Lightbox.Bench, Harness, AnimationSweeps.cs, DrawingSweeps.cs, Cadence, Curve, Runner`
  - The `Category=Performance` tests answer "did this diff break a path we know about". They cannot answer "where does this stop being usable" or "what should we fix first", and the unit of work here is a sequence, which no budget grows. `tools/Lightbox.Bench` sweeps a dimension, fits the exponent, finds the cliff where p95 misses the budget, and ranks by pressure. Minutes to run, so it is deliberate rather than per-commit. Design: `docs/DESIGN-performance.md`; output: `.claude/quality/PERFORMANCE.md`.
- [x] The simulated media are measured and bounded by the stroke `evidence: MediumPerformanceTests, TheMediumsCostGrowsNoFasterWithTheCanvasThanPlainCompositingDoes, AMediumStrokeDoesNotAllocateALatticeEachTime, AReusedLatticeRendersExactlyWhatAFreshOneWould`
- [x] Every brush feature is absent from the file until it is used `evidence: MediumOnDisk, IsUntouched, BrushDynamicsSerializationTests, ABrushThatUsesNeitherWritesNeitherKey`
  - The simulation was behaviourally optional and written anyway: twenty-one medium keys on every stroke of every document, a third of the brush record, for a pass nobody switched on. `BlendOrNormal` was worse — a convenience getter beside the nullable field, serializing the exact key that making it nullable had removed. Any accessor added beside a nullable setting needs `[JsonIgnore]`.
- [x] Expensive brushes are marked as such before you pick one `evidence: BrushCost, BrushCostOf, BrushCostTests, BrushCatalogueTests, JitterAndScatterAndTextureAreNotExpensive, EverySimulatedMediumHasAFastCounterpart`

#### Brush tips — a generated library

Design: `docs/DESIGN-brush-tips.md`. A tip is an asset, generated once and then
only looked up; nothing about one is computed while a stroke is being drawn.
The runtime half — caching, bilinear sampling, mipmaps, rotation, dual texture,
the art-tool importers — is already built and the design doc says which parts
not to rebuild. What is missing is everything that *makes* a tip.

- [x] Tips are cached as rasters and sampled properly, never rebuilt per dab `evidence: BrushTipRegistry, BrushTipSamplingTests, AnEnlargedTipIsSmoothedRatherThanBlocky, AMinifiedTipIsAveraged_NotPointSampled`
- [x] A tip library, scoped like palettes: project, user, and inlined on export `evidence: BrushTip, TipStore, TipLibraryTests, AProjectTipComesBeforeAUserTip, DeletingFromTheLibraryCannotChangeADrawing, AProjectThatNeverMadeATipWritesNoTipsKey`
  - **"Scoped like palettes" finally means it (Q30) `evidence: TipScopes, WithNothingDeclaredEveryTipIsStillOffered, ATipDeclaredOnAFolderIsOfferedThereAndNotElsewhere, DeclaringABrushTipNarrowsTheProjectsOwnAndNotTheUserLibrary`** — *Share a brush tip here* on a folder. The line above said scoped-like-palettes before the scoping mechanism existed; it meant project-versus-user, and the folder axis is what makes the phrase true. Like symbols and unlike palettes it **narrows**, so a project declaring none keeps offering everything. The user library and the built-in catalogue are never narrowed — they follow the artist rather than the project, and painting with either copies the raster into the document anyway.
  - The record adds `Pivot` — where the pen touches, which is not the centre of an angled mark. It is what stops multi-capture blending from ghosting, and making it a field rather than a cropping discipline is the point.
- [x] Procedural tip generator — circle, soft circle, ring, chisel, hatch `evidence: TipGenerator, TipRecipe, TipGeneratorTests, AGeneratedEdgeIsCoverageNotAStaircase, HatchRulesAreDrawnAsWidthNotAsSinglePixels, TheSameRecipeBakesTheSameTipEveryTime`
  - Every threshold is anti-aliased as pixel coverage. A binary `d <= Radius` stair-steps, and a stair that shifts phase between frames boils at 12 fps.
- [x] Five shapes a circle and a chisel cannot reach — bristle, superellipse, polygon, spatter, halo `evidence: TipShape, TipGeneratorTests, ABristleTipIsCombedAtTheRimAndSolidInTheMiddle, OneExponentWalksTheWholeSuperellipseFamily, APolygonHasCornersThatReachFurtherThanItsFlats, SpatterGrainsHaveASizeRatherThanBeingFog, AHaloIsDenserAtItsRimThanInItsMiddle`
  - Standard curves rather than invented ones: a raised-cosine comb, Lamé's superellipse, the polar form of a regular polygon, Worley cellular noise and a Gaussian rim band. Spatter hashes its cell index the way the dab dynamics hash a position — an RNG here would make the same document render differently on two machines.
- [x] Eight built-in tips, so the library is not empty on day one `evidence: TipCatalogue, TipCatalogueTests, ThereAreEightBuiltInsAndEveryOneBakes, TheIdsAreFrozen, ThePixelsBehindABuiltInDoNotMoveBetweenRuns`
  - Recipes, not shipped PNGs, and every id is frozen: a document that painted with one copied the raster in under that id, so renaming one is a compatibility break and re-tuning one silently repaints old drawings. Adjusting a built-in means adding a new one.
- [x] Scans become tips: levels, invert, square, centre, edge mask `evidence: TipFromImage, TipFromImageTests, OneSetOfLevelsAppliedToABatchGivesMatchingTips, AMarkTouchingTheCropIsRejectedRatherThanFaded, TheCropFollowsTheMarkRatherThanThePage`
  - The edge mask is enforced, not requested: a tip whose mark was cut by the crop stamps box edges down the whole stroke, and it is trivially detectable.
- [x] The tip workshop, off the main menu like Configure `evidence: BrushTipsWindow, BrushTipsWindowTests, GeneratingATipPutsItInTheLibraryAsPixels, OnlyTheControlsTheShapeActuallyReadsAreShown, ThePreviewBakesSmallHoweverBigTheOutputIs`
- [ ] Multi-capture tip sets blended by tilt and speed `evidence: BrushTipSet, TipSetBlend, TipSetTests, BlendingHappensAtTheSizeBeingDrawn, ASteadyTiltBlendsOnceAndReusesIt`
  - Needs tilt and speed in the record first. Blend at the mipmap level the dab samples, quantise the blend factor and cache by it — otherwise it is a million lerps per dab, and a tilt that wobbles a degree makes the mark shimmer.

### Canvas

- [x] Large canvas optimization `evidence: ComposeRing, FrameBitmapCache, CanvasQuality, LargeCanvasPerformanceTests`
- **Infinite canvas — removed on purpose (2026-08-12).** The owner cut the feature to make room for a different direction (a simplified 3D drawing space; see Q71). What was removed is the *capability*: `FeatureKey.UnboundedCanvas`, its project defaults and Configure toggle, the `FeatureConflict` machinery whose only declared conflict was `UnboundedCanvasExcludesFixedFrameBoundsExport`, and the sprite exporter's refusal. What deliberately stayed is everything the feature left behind that bounded documents now depend on: the tile engine (`TileStore`, `TileGrid`, `TiledRasterizer`, `TileCompositor`, `TilePyramid`, `TileFrameCache`) is playback's compositor — `tileModeOn = IsPlaying` — `StrokeIndex` serves stroke picking and selection, and B82's viewport culling serves every zoomed-in publish. The full design, its measurements and its history are in git (`docs/DESIGN-infinite-canvas.md`, deleted with this entry) and in Q20/Q21, both marked superseded.
- [x] Canvas rotation `evidence: CanvasViewTests, CanvasControl`
- [x] Canvas mirroring `evidence: MirrorButton, IsMirrored, CanvasViewTests`
- [x] Render at any output scale without changing the mark `evidence: OutputScaleTests, AHigherOutputScale_RendersTheSameMark, ScalingTheCoordinatesInstead_ProducesADifferentMark`
- [x] Reference image panel `evidence: ReferenceSheet, ReferenceView, ReferenceTabTests`
- [x] The cursor says what the tool will do, and whether it can `evidence: CanvasCursor, CanvasCursorTests, ADisallowedActionShowsWhyRatherThanDoingNothing, TheViewModelReportsTheIntentForTheActiveLayer, HoldingControlOverAPaintToolShowsTheEyedropper, MovingOutsideTheSelectionRefusesAndMovingBackDoesNot, AlphaLockRefusesOverBareCanvasAndAllowsOverPaint, TheStatusLineOnlyAppearsWhenThereIsSomethingToSay, PickRing, PickRingTests, TheSampledColourIsOnTopAndTheColourInHandBelow, TheMiddleIsAHoleTheArtworkShowsThrough, ThePreviewIsExactlyWhatTheClickWillTake, CanvasCursorChoice, ResizeAt, CanvasCursorChoiceTests, ACornerHandleScalesAlongItsOwnDiagonal, AnEdgeHandleScalesAcrossItselfRatherThanAlongItself, ARotatedViewSwingsTheHandleCursorsWithIt, TheCursorHoldsForTheWholeDragRatherThanFollowingThePointer, AKeyOrAToolChangeReDecidesTheCursorWithNoPointerEventToHangItOn`
  - Today the canvas shows a brush-size ring and little else. The eyedropper, the fill, the move tool and the shape tools all present the same pointer, so the only way to know which one is armed is to look away from the drawing at the toolbar — which is exactly the moment an artist does not want to spend.
  - **The half that matters more is the refusal.** Painting on a hidden layer, a locked layer, an alpha-locked layer with nothing under the brush, filling outside a selection: these currently do nothing and say nothing, and silence is indistinguishable from a broken app. A forbidden cursor turns "it is not working" into "it will not do that here", which the artist can act on. B2's lesson written down as a rule — *even refusing would be better than nothing*.
  - Needs one place that maps (tool, modifiers, what is under the pointer) to a cursor, so the answer cannot disagree between the canvas control and the view model. Testable without a window if the mapping is pure, which is the shape `RigOverlay.CursorFor` already uses.
  - Depends on the icon set below for the artwork, but not for the mechanism: it can ship with system cursors and get custom ones later.
  - **Built: the mapping, and it is wired.** `CanvasCursor.For(tool, target)` is pure — a tool and five booleans in, a cursor kind out — so it is tested with no window, which matters because synthetic input through Xvfb is unreliable here and a decision buried in the canvas control would have shipped unguarded. `MainViewModel.PointerIntent` binds onto `CanvasControl.PointerIntent`, the only writer of `Cursor` outside the guide hover, and `RefreshPointerIntent` exists because `Layer.Visible` and `Layer.Locked` are plain properties on a model that notifies nothing — hiding the layer you are drawing on would otherwise leave the pointer still promising a stroke.
  - **`CanvasTarget` names every field for the obstacle** (`LayerHidden`, not `LayerVisible`) so its zero value is an ordinary paintable place. That is correctness, not style: a record struct's primary-constructor defaults do not run for `new CanvasTarget()`, so the permissive spelling refused every tool everywhere while reading, in source, as though it allowed them. Caught by its own test rather than by review, which is why the test is still there.
  - **Built: modifiers, the two positional facts, and somewhere for the sentence to go** — the three gaps this item carried.
    - **Ctrl over a paint or fill tool is a held eyedropper**, and the pointer says so. Not invented: the canvas branches on exactly this before it looks at the tool, on the grounds that *"the colour you want is almost always already on the canvas."* Reading it has a real consequence rather than a cosmetic one — holding Ctrl over a **locked** layer stops forbidding, because picking a colour off a locked layer is allowed and always was. **Only this modifier.** Shift and Alt change what several tools *do* — a fill inverts, a wand adds or subtracts — but not which *kind* of action it is, and a cursor that changed for each would be flicker rather than information. `NoOtherModifierChangesTheTool` is what stops the mapping growing past what the canvas actually branches on.
    - **The two positional facts are answered from a hover report**, so *outside the selection* and *nothing under an alpha-locked brush* are true rather than assumed. Bounded as invariant 6 requires: the selection test is point-in-polygon over the outline rather than `SelectionMask`'s full `w × h` rasterisation, the alpha read is one pixel and only happens when the layer is alpha-locked, and the report is coalesced by pixel and suppressed entirely while a gesture is in progress. With the pointer off the canvas there is no place, so both stay permissive — the cursor under-reports rather than inventing a refusal.
    - **The sentence goes beside the AI status line, not into it.** That strip already carries messages of exactly this class ("Nothing fillable at that spot"), but one follows the pointer and the other records what a command did, so sharing a slot would have each wiping the other out the moment the artist moved the mouse. `HasPointerRefusal` keeps it absent in the ordinary case, because a refusal that is always present is furniture and stops being read.
  - **Built: the eyedropper says what it would take, not only that it is armed.** `PickRing` draws a split ring around the pointer — the colour under it on top, the colour in hand below, and a hole in the middle so the pixel being aimed at is never covered. The cursor was answering *which tool is this*; picking a colour is a **comparison**, and the other half of it lived in a docker on the far side of the window, which is exactly where an artist is not looking while aiming at a drawing.
    - **The ring and the click come out of one function**, `PickedColorHexAt`. A preview computed separately from the thing it previews breaks quietly in the case nobody tested, and the paper fallback is such a case: transparent pixels resolve to the paper colour, so a preview that had not been told would read blank over most of a fresh drawing.
    - **Bounded, as invariant 6 requires.** The click can afford `CompositeVisibleLayers` and a `GetPixel`; a hover cannot, and that is the shape of every performance regression the invariant names. `SampleVisibleComposite` moves the sample point into the transform and composes into a **1×1 surface** instead — one clipped draw per layer, proportional to the layer count rather than the canvas — and it composes rather than blending by hand, so layer opacity and blend modes stay known to the compositor alone. The hover report was already coalesced to one per document pixel crossed, which is what makes even that affordable.
    - **Keyed on the intent, so the borrowed eyedropper gets it too.** Ctrl over a brush is where it earns the most: the borrow exists because breaking a stroke to fetch a colour is the cost being avoided, and comparing without breaking it is the same argument one step further. The brush's size ring gives way to it while picking — a gizmo describing a tool that is not in hand is worse than none.
    - **The drawing is a static function over an `SKCanvas`**, for the reason the mapping is pure: synthetic input is unreliable here, so a gizmo drawn inside `CanvasControl.Render` ships unguarded. `PickRingTests` renders onto a bare surface and reads pixels — the halves the right way round, the middle still showing the artwork — and each was mutated back in turn to prove it fails.
  - **Built: the cursor also says what is *under* it, not only what is in hand (B241).** The item above answers "which tool is armed"; twelve grabbable things on the canvas answered nothing at all — the gizmo's six gestures inside one box, the camera's three grips, a reference box corner, a guide, a height scale's top, and the canvas itself. `CanvasCursorNow` is the single writer, and it asks the live gesture before the hit test, which is what makes the cursor **hold for a whole drag** and **change the instant a key goes down** rather than needing machinery for either. Resize arrows are drawn at the handle's real screen angle (Q107), measured through `DocToView` so a rotated or mirrored view moves them with it — invariant 5 from the pointer's side. The decision is separated from the bitmap (`CanvasCursorChoice`) because rendering one needs a surface a headless run has not got, so a test asking for the `Cursor` would be asserting against the fallback.
- [x] Resize canvas and resize image `evidence: CanvasResize, ImageResize, ResizeAnchor, IPixelResampler, CanvasResizeTests, ImageResizeTests, NotOneCoordinateInTheDocumentMoves, GrowingToTheLeftMovesTheOriginRatherThanTheDrawing, GrowingAndCroppingByTheSameOddAmountReturnsToWhereItStarted, ADocumentThatNeverResizedWritesNoOriginKeys, DoublingTheImageDoublesTheGeometryAndTheBrush, EveryCoordinateOnTheSceneIsAccountedFor, DocumentOriginTests, GrowingThePaperMovesNoInkAndReRollsNoJitter, AStrokeInTheNewPaperIsDrawnRatherThanClippedAway, ResizeDialog, ResizeDialogTests`
  - Resize canvas expands the image with the value added to the x or y. It keeps the DPI and all other canvas related configurations. The content on the canvas stays put. The user wants to be able to select; all direction, down, to either side or up. There should be a preview.
  - Resize image scales the entire image and changes the dpi of the docment if any is given. and we can optionally constrain the proportions.
  - For both we can We can chose to resize only x, y or by default link the two so it scales uniformly. And after confirming resize or rescale the canvas is resets to the viewport.
  - **The two operations differ in what they are allowed to do to the mark, and that is the whole design** (Q61). Every dab dynamic is seeded from the bits of a dab's position through `Hash01`, so moving a coordinate changes the mark it carries. Resizing the *paper* therefore moves nothing: `Scene.OriginX`/`OriginY` shift instead, the document rectangle becomes `[Left, Right) × [Top, Bottom)`, and the render is bit-identical outside the new margin. Rescaling the *artwork* multiplies every coordinate and the brush sizes with them, and the grain re-rolls — which is allowed, because the artist asked for different art. *Changes the art, the mark may change; changes the paper, it must not.*
  - **Built: the two operations and the record.** `CanvasResize` is anchor arithmetic over three fields (and `Half` truncating rather than flooring, so growing and cropping by the same odd amount are exact inverses rather than drifting a pixel a round trip). `ImageResize` is an exhaustive visitor over every coordinate in the document — strokes, holes, brush size and texture scale, clip contours and feather, guides and their spacing, camera keys, pivot, symbol placements, per-frame anchors and collision boxes, reference offsets — with `EveryCoordinateOnTheSceneIsAccountedFor` reflecting over `Scene` so a coordinate added later and not handled fails a test instead of landing in the wrong place. `IPixelResampler` is how it stays honest about the two payloads that are pixels rather than instructions.
  - **Built: the origin through the raster path.** `BrushEngine.StampStroke` and `FrameRasterizer.Rasterize` take an `origin`, defaulting to `(0,0)` so every caller that has never resized is untouched — and `RuntimeDeterminismTests` still matches its .NET 8 fingerprint, which is what proves that claim rather than asserting it. Two coordinate spaces are now named on `ToSurface`: **document**, which a stroke records and every `Hash01` seed must receive, and **surface**, a pixel in a bitmap whose (0,0) is the document's top-left. The canvas transform converts for everything that draws; raw pixel access converts by hand.
    - **The clamp was the sharp edge, exactly as predicted.** `SegmentBounds` and `RangeBounds` now clamp to `[Left, Right) × [Top, Bottom)`. Clamping to the surface instead is a bug that ships green — it discards only the half of a stroke sitting in newly-added paper, which no comparison of the region two documents *share* can see. Hence `AStrokeInTheNewPaperIsDrawnRatherThanClippedAway`, which is the one test that looks at the margin instead.
    - **The finding that was not in the plan: the effects that seed from a rect, not from a dab.** `ApplyGranulation` anchors a tiled noise shader to the stroke's rect and `ApplyTexture` passes its corner into `PaperField.Fill`. So `SegmentBounds` has to keep returning *document* coordinates — handing those two a surface rect leaves the mark where it is and slides the paper underneath it. That is invariant 2 broken in the one way that looks like nothing, and no per-dab assertion would catch it.
    - **`LerpDab` split as expected, and `DabShape` was the part underneath it.** The span indexing is a surface coordinate, the `Hash01` seed is a document one. `DabShape` was seeding rotation jitter from its *device* centre, so it now takes the document position separately — which also fixes a latent invariant-7 break: a tipped effect dab re-rolled its rotation at any output scale other than 1, since device and document only coincide there.
    - **The guard is `DocumentOriginTests`**: the same strokes rendered into flush paper and into paper grown left and up, with the shared region required to be bit-identical. Its fixture is chosen to reach the sites that differ — jittery paint, granulation, paper texture, a **tipped** smudge, a blur, a fill. Tipped on purpose: `DabShape` is inert without one, so a tipless smudge silently guarded nothing. Each of the three sites above was mutated back in turn and the test caught all three.
  - **Built: the view and the pixel tools.** `CanvasControl.ViewToDoc` now answers in **stroke** coordinates rather than surface pixels, which is what fixes the tools for nothing — picking a line, dragging a guide, placing a symbol and starting a stroke all want the space the record is written in, so one conversion corrects all of them. `DocumentOrigin` is a styled property bound from the view model rather than a field on `RenderSnapshot`, because nothing about *drawing* the composited bitmap changes when the paper is renamed; only the question of which stroke coordinate is under the pointer does.
  - **Three tools index pixels instead and convert back** — flood fill, the wand, the colour picker — and **two results convert forward again**, which is the half that would have shipped wrong. `FloodFill` reports its contour in the bitmap's own pixels; handed straight to a stroke that is a fill offset by the whole margin, with the record and the pixels disagreeing. The selection stays a surface mask, because a `w × h` array of booleans is what it is, and crosses into stroke coordinates at exactly one place: `PrepareClipForSelection`, where it becomes part of the record (invariant 3).
  - **`DocumentOriginInViewTests` grows the canvas before asking anything**, and that is the point rather than a detail: the two spaces coincide until an origin goes non-zero, so every one of these tests passes on an unresized document whether the code is right or wrong — which is why the existing suite could not see any of it. Each of the three conversions was mutated back in turn and caught.
  - What remains is `TiledRasterizer`, which still refuses effect brushes (B60 — the mechanism it needs now exists, the tiling itself is not switched on).
  - **Built: the dialog, and every surface it shows up on.** `ResizeDialogViewModel` owns the arithmetic and the window owns the window, so the cases nobody clicks through by hand — a link that would divide by zero, a crop to one pixel, a PPI change that is not a resize, a link that oscillates between its two fields — are asserted without one. The anchor grid is built from `ResizeAnchor` itself rather than declared nine times in XAML, so the two cannot disagree and a tenth anchor would appear for free.
  - **The link starts on for an image and off for a canvas**, because they are different questions: rescaling artwork non-uniformly distorts it and almost nobody means to, while adding paper to one side is the ordinary reason to open the canvas mode at all.
  - **The preview is a sentence rather than a picture**, and it names the one consequence that cannot be undone by eye — whether the marks are about to change. `TheTwoModesLeaveDifferentTracks` is the test that stops the two blurring: paper writes an origin and moves no coordinate, artwork moves every coordinate and writes no origin.
  - One undo step through `DocumentEditor.Perform` (an image resize walks every stroke, clip contour, guide, camera key, placement and collision box — undo per stroke would lose the drawing), fit-to-view after confirming, an **Image** menu of its own, and both commands in `ShortcutMap` on Photoshop's `Ctrl+Alt+C` and `Ctrl+Alt+I` so they can be found and rebound. `TheDialogOpens` constructs the window in both modes, which is B163's lesson applied before it costs anything.

### Film-scale line quality

**The one place Pillar 0's bar is deliberately raised above "credible".** Everything
else here is what a painting app is expected to have; these three are what a
drawing has to survive being projected. The deliverable is a theatre screen, and
a line that reads fine at 100% on a monitor is being judged at forty feet.

Per the rule in *Reach and configuration* below, none of this is film-only: it is
available in every project, defaulted for the ones that need it.

- [ ] Zooming in re-stamps the line rather than magnifying it `evidence: ViewRenderScale, ZoomFidelityTests, AZoomedLineIsReStampedRatherThanMagnified, ZoomingPastDocumentResolutionIsBoundedWork`
  - **The record is already resolution-independent and the screen is not.** A stroke is coordinates plus a brush, invariant 7 makes output scale a canvas transform, and `OutputScaleTests` proves a 2× render is the same mark rendered sharper. But `CanvasQuality.Full` is documented as *"always composite at document resolution"* — so that is the ceiling on screen, and zooming to 400% on a 4K canvas magnifies finished pixels instead of re-stamping the dabs. The information to draw a crisp line at that magnification is sitting in the document, unused.
  - The reason this is a real feature and not a one-line change is invariant 6: painting is bounded work. Re-stamping at 4× the device resolution over a viewport is affordable; doing it per pointer event over a whole 8K canvas is not. So it needs a render scale derived from the *view*, applied to the *visible region*, with the existing `FrameBitmapCache`/`ComposeRing` machinery taught the difference between "document pixels" and "device pixels" — which today it conflates.
  - **The trap is invariant 7 the other way round.** Rendering the visible region at 4× must scale the surface, never the coordinates: doubling a coordinate re-rolls every `Hash01` dab dynamic and gives a *different mark*, which at a zoom boundary would show as a visible seam where the texture changes. `OutputScaleTests` already renders it the wrong way round on purpose; a zoom-fidelity test needs the same guard.
- [x] A whole line can be picked and then moved, deleted or recoloured `evidence: StrokePicker, StrokeActionTests, MovingTheSelectionShiftsEveryPoint, UndoingAMoveRestoresTheExactCoordinates, UndoingADeletePutsTheLineBackInItsOldPlaceInTheOrder, RecolouringALineFromAPaletteCutsItLooseFromTheSwatch, MovingAFillTakesItsHolesWithIt, TheActionsAreRegisteredWhereTheConfigurationWindowCanSeeThem`
  - The step before point editing, and a separable one: the Arrow tool picks whole strokes, the actions rewrite them, and none of it needs a path record. `StrokePicker` answers *which line*, `SelectionManager` holds ids rather than list positions because positions shift on delete, and the three actions go through `DocumentEditor.PerformDelta` for one undo step each.
  - **Two failures worth keeping written down, because both look right.** Undo of a move restores the snapshotted points rather than subtracting the offset — `a + d - d` is not `a` in IEEE-754 and every dab dynamic is seeded from the *bits* of a coordinate, so an inexact undo returns the line visibly home with a different grain. And a recolour must clear `SwatchId`, which wins at render time: writing `Color` alone changes a field nothing reads and the line stays the colour it was.
  - Q26 is what makes moving a line this cheap — the grain is allowed to change, so there is no seed origin to carry and no new field on the record.
  - **Not this item:** dragging a stroke's individual points, and scaling or rotating a selection. The latter wants a `TransformScope` meaning "these strokes inside this cel"; every scope today is a set of cels, so the transform session cannot express it yet.
  - **That diagnosis was wrong and B223 landed the feature (2026-08-16).** Scope was never the obstacle: `TransformSession` has carried a `Func<Stroke, bool>` filter since the marquee shipped, `TransformOps.TransformFrame` takes it, and `PartsFor` already renders the moving/static split it implies. *Which frames are in scope* and *which strokes within them move* are orthogonal, and the code knew it while this bullet did not — which is what stopped anybody trying. What was actually missing was a filter built from the line selection, plus a scope that stops offering cels the picked lines are not on. Kept above rather than deleted, because a wrong diagnosis that cost two releases is worth more written down than tidied away.
- [x] A drawn line can be re-shaped and keeps the mark it was drawn with `evidence: PathEditSession, PathEditingTests, ReshapingKeepsTheWeightTheLineWasDrawnWith, AfterACommitThePathAndThePointsStillAgree, WithALineIsolatedAClickOnAnotherChangesNothing`
  - Vector manipulation with the texture of charcoal, pencil or paint. **Half of this already exists and is worth saying so:** a stroke on a `VectorFrame` is the same `Stroke` record with the same `BrushSettings` as a raster one, stamped by the same engine — so a vector line already carries real media rather than a flat outline. Nothing needs a second engine.
  - What is missing is the editing: there is no tool that takes a finished stroke's points and lets an artist drag them. `VectorFrame` holds `List<Stroke>` and nothing reaches into one after it is drawn.
  - **The design question this raises was genuinely hard, and it is answered.** Every dab dynamic — scatter, size, roundness, rotation, all three colour jitters — is seeded from dab position via `Hash01`. Move a control point and the dabs near it re-seed, so *the texture changes where the line moves*. That is correct under invariant 2 and wrong to an artist, who expects to nudge a line and see the same line somewhere else. **Q26 answered (a): accept it — "the grain belongs to the canvas."** A per-stroke seed origin, arc-length seeding and a blended re-seed radius are all *rejected*, not deferred, so nothing here needs a new field and no tunable enters the render path. What it obliges is a manual line saying the grain shifts and why, with "move the layer rather than the line" as the answer for an artist who needs the mark preserved exactly.
  - **Unblocked, and the rest is decided too.** Q47–Q53 settle the tooling: two pointers plus isolation mode, Bezier handles carried on an optional `Stroke.Path` beside `Points` rather than widened into `StrokePoint`, shapes staying ordinary strokes, one frame first. `docs/DESIGN-vector-tooling.md`.
  - **Phase 2 landed 2026-08-08: the tool.** Double-click a line to isolate it, drag its points and handles, Esc to leave. `PathEditSession` owns the geometry and the view model owns the history, so a drag is one undo step however many pointer moves it took. Isolation is enforced in the picking path rather than drawn over the top — `WithALineIsolatedAClickOnAnotherChangesNothing` — because a mode that only *looks* isolating is worse than none. The white arrow is `N`: `A` is this application's black arrow, which the design's Illustrator-shaped key table did not account for.
  - **And it needed something the design did not predict: `PressureProfile`.** A fit keeps pressure only where its nodes landed, so re-flattening flattens the taper with it — measured at a peak of 1.00 dropping to **0.89** on the first node drag. The weight is now re-applied by normalised arc length, so it stretches with the edit instead of being resampled away. This item's own wording is what made it non-optional: *keeps the mark it was drawn with*.
  - **Phase 1 landed 2026-08-08: the record and the two functions between it and the points.** `StrokePath`/`PathNode` on an optional `Stroke.Path`, `PathFlattener` one way and `CurveFitter` (Schneider) the other, and the agreement invariant obeyed at all three existing callers that map points — `TransformOps.TransformStroke`, the arrow's `Offset`, and `StrokeInterpolator`, which carries no path by design. Measured: a 121-point drawn arc fits to **4 nodes** and flattens back within **1.2 px**; a hand-drawn stroke still serializes no `path` key. This box stays open because it names the *tool*, which is phase 2 — what landed is everything underneath it.
- [x] Draw a line by placing its points, with a pen `evidence: PenSession, PenToolTests, AClickAndDragPlacesASmoothNodeWithMirroredHandles, FinishingWritesOneOrdinaryStrokeWithPathAndPointsAgreeing, AWholePathIsOneUndoStep, TheWhiteArrowOpensThePenSOwnNodesRatherThanAFit`
  - **Phase 3 of `docs/DESIGN-vector-tooling.md`, and the first tool here that authors a path rather than finding one.** Both arrows need a line to already exist; this one starts from nothing. Click for a corner, click and drag for a curve, click the first node to close, `P` to reach it.
  - **What it writes is an ordinary stroke** — same record, same `BrushSettings`, same engine, with `Points` flattened from the path and the path kept beside them. So a pen line erases, fills against, exports and inbetweens like a drawn one, and it is the one stroke in the document that has never needed a fit: the white arrow opens the nodes the pen authored rather than an approximation of them.
  - **The preview is chrome rather than paint, and that is invariant 6 rather than a shortcut.** The shape tool stamps its live preview with the real brush, which is right for one drag; a pen session lasts as long as it takes to place a dozen nodes, and re-stamping the whole path into a full-canvas scratch on every pointer move is the cost the invariant exists to refuse. The canvas traces the flattened path and the brush is stamped once, at the commit.
  - **Enter and Escape both finish and neither discards**, which is where this departs from the polygon selection beside it. A selection in progress is not artwork; a dozen placed nodes are. What is written is one undo step, so `Ctrl+Z` is the way to lose it.
  - **Not this item:** the reshaping set — pinch a segment, width along the line, simplify, cut and join. That is phase 4, and CSP's `Correct line` list is the proven minimum it is scoped to.
- [x] Pull a line's curve to where you want it, without picking a point `evidence: SegmentDrag, SegmentDragTests, TheCurveEndsUpWhereItWasPulledAndTheNodesDoNotMove, TheWorkIsSplitInProportionToWhereTheCurveWasGrabbed, PullingTheCurveMovesItWithoutMovingTheNodes, APinchIsOneUndoStepAndUndoingItRestoresTheLineExactly`
  - **Phase 4a of `docs/DESIGN-vector-tooling.md`, and the design calls it the one artists reach for most.** Every other way to change a curve asks you to find the node that governs it and reason about its handles; this asks you to put the line where you want it. Clip Studio's *Correct line* list opens with it and Illustrator, Inkscape and Figma all have it.
  - **The nodes do not move, and that is the whole distinction.** Only the two handles governing the segment change, so the shape either side of the bit being pulled stays exactly where it was put. A smooth endpoint is the one apparent exception and is not really one: its far handle swings to stay in line, because that is what smooth means, and a node that kinked the moment you pulled the line beside it would be worse.
  - **The interesting decision is which answer to pick, because there are infinitely many.** Holding the endpoints fixed leaves `b1·ΔP1 + b2·ΔP2 = d` — one equation, two unknowns. The least-squares member moves the control points as little as possible and splits the work in proportion to each one's influence where you grabbed, which is what makes the gesture feel like pulling a wire. The cheap alternative — give it all to the nearer control point — reads as the curve lurching, because the answer changes discontinuously as you drag past the midpoint.
  - **Segments are tested last, and that is load-bearing.** Every node and handle sits *on* the curve, so a segment that won the hit test would make them unclickable.
  - **Phase 4 turned out to be four objectives wearing one number**, and the design records the split: this, then width along the line, then simplify, then cut and join. They share a session and share nothing else.
- [x] Change a line's weight after it is drawn `evidence: PressureProfile, PressureProfileEditTests, AWidthEditIsLocalAndLeavesTheEndsAlone, TheEdgeOfTheEditIsSmoothRatherThanAKink, DraggingOffTheLineMakesItHeavierAndTowardsItLighter, AWidthDragIsOneUndoStepAndRestoresTheOriginalWeight, AnAuthoredLineWithNoPressureCanStillBeGivenWidth`
  - **Phase 4b, and it needed no new field.** Illustrator's Width tool with its "width points" is, in this record, the `Pressure` array under another name — a Lightbox stroke has been a centreline with a width at every point since the first one was drawn. So this edits a number the format already carries rather than adding a parallel one, which is the same reason the vector work needs no vector layer.
  - **It edits the weight, never the points, and that is what makes it hold.** The flatten regenerates the points from the nodes on every commit and re-applies the profile afterwards, so an edit written into the points would be thrown away by the next reshape — the trap `PressureProfile` was built to close one operation earlier, arriving again from the other side.
  - **Resampled up and never down.** A straight authored segment flattens to two points and a local change has nowhere to land between them; a drawn line already has hundreds, and passing those through a fixed resample would quietly coarsen the taper the artist drew. The tool must not take something away as the price of being picked up.
  - **A raised cosine rather than a triangle**, because a triangle is continuous and its slope is not, and a brush whose taper changes abruptly reads as a nick in the line.
  - **Distance from the line, not a signed side.** There is no left and right to weight differently — one centreline, one width — and offering the control anyway would be a handle on something the record cannot hold.
- [x] Refit a line through fewer points `evidence: PathEditSession, SimplifyLine, SimplifyingLeavesFewerPointsAndSaysHowMany, SimplifyingKeepsTheWeightTheLineWasDrawnWith, SimplifyingRefitsTheLineAsItIsNowRatherThanAsItWasDrawn, EachSimplifyIsItsOwnUndoStep`
  - **Phase 4c, and it is `CurveFitter` with its tolerance turned up.** The fitter's own summary said so before this existed; what needed deciding was *what* gets refitted.
  - **The flattened current path, not the drawn points.** By the time somebody simplifies, the line may have been reshaped, pinched and re-weighted — going back to the drawn points would silently undo all of it under a button labelled *simplify*, which is the worst kind of surprise a command can spring.
  - **The count is the feature as much as the refit is.** *Simplify* with no number is a button an artist presses and then squints at the canvas to find out what it did. Each press loosens further and is its own undo step, so one too many costs a single Ctrl+Z.
  - **The weight survives**, because it is a function of arc length rather than of node count — fewer points describing the same line read the same taper. Without that, *simplify* would also quietly mean *flatten the pressure*.
  - **Not built: the live slider** the design's wording suggests. It sits on this same primitive (`Simplify(tolerance)` already takes one) and wants a tool-options control with a preview that does not commit; stepping is the honest first cut rather than a stand-in for it.
- [ ] Sub-pixel stroke precision that holds up at 8K `evidence: ChordTolerance, SubPixelPrecisionTests, TheChordToleranceFollowsTheOutputScale, ALongCurveHasNoFlatSpotsAtEightK`
  - The geometry is already in doubles and there are six stabilisation modes, so this is not "add smoothing" — it is one number. `GeometryOps.Densify` defaults to `maxChord = 2.0` **document pixels**, and that tolerance does not know the output scale: render or zoom at 4× and every chord becomes 8 device pixels, so a curve that is smooth in the file has visible flat spots on screen and in the export. The same 2 px is simultaneously too coarse at 8K and wasteful on a thumbnail.
  - So the tolerance wants to be derived from the scale the render is happening at, and stored per stroke where it reaches pixels (invariant 4) rather than read from global state. The cost is real and bounded — halving the chord roughly doubles the dab count — which makes it a per-preset trade with a badge, like the other expensive options.
  - Anti-aliasing is a `bool` today. A theatre-screen line probably wants more than one answer there too, but that is a second measurement and not this item.

### Colour

- [x] Color picker with history `evidence: ColorPickerViewModel, ColorSwatch`
- [x] Advanced color wheel `evidence: ColorWheelFidelityTests, ColorSpace, ColorOps`
- [x] Palette management `evidence: PaletteDockerViewModel, GimpPalette, PaletteTests, PaletteDockerTests`
- [x] Live palettes — recolour a swatch, the art follows `evidence: PaletteRegistry, StrokeColor, LivePaletteTests, RecolouringASwatchRepaintsTheArtThatUsedIt`
- [x] Gradient editor `evidence: GradientDockerViewModel, GradientOps, GradientTests, GradientToolTests`
- [x] Gradient tool `evidence: GradientDragStarted, BeginGradient, EndGradient, TheRampRunsAlongTheDrag, TheRampIsVisibleWhileDragging_AndSurvivesThePenLift`
- [?] Pattern fills
- [ ] Regrade the painting by editing the palette `evidence: PaletteOps, RegradeSwatches, PaletteRegradeTests, RotatingThePalettesHueRepaintsEveryStrokeThatUsedIt, ARegradeIsOneUndoStepAndPutsEverySwatchBackExactly, AStrokeCutLooseFromItsSwatchDoesNotMove, SwappingInAnotherPaletteKeepsSwatchIdentityAndMovesOnlyTheColours`
  - One step past live palettes, which already prove the mechanism: recolour a
    swatch and the art that used it follows. This lifts the same edit to the
    whole palette — rotate its hue, shift its temperature, compress its values,
    or swap another palette in wholesale — and the painting regrades itself.
    Because things that belong together share one palette (Pillar 1), the same
    gesture regrades every frame of a sequence that painted from it: a
    colour-script experiment across a shot for the cost of one undoable edit.
    Nobody else has this, because nobody else's colour lives in the record.
  - **A swap rewrites colours, never identities.** Swapping palette B in means
    writing B's colours into the existing swatches (matched by position), so
    every `SwatchId` in the record keeps meaning something and undo is exact.
    Rebinding strokes to a different palette's ids would touch the record
    everywhere to say the same thing.
  - **It only reaches art painted from the palette**, and that is stated
    rather than hidden: a stroke whose colour was picked loose carries no
    `SwatchId` and does not move. That limitation is also this feature's
    argument for painting from palettes in the first place. Bulk recolour of
    loose strokes by similarity is a real want and a separate item — folding
    it in here would put a heuristic inside an operation whose whole value is
    being exact.
  - Invariant 4 is not in play and it is worth saying why: a palette is part
    of the work, not a preference. A regrade is an authored document edit
    travelling the exact channel a single-swatch recolour already uses — the
    swatch wins at render time by design, and this changes the swatch.
  - Effort: low-to-medium. The registry, the swatch reference and the repaint
    path all exist; what is new is a set of whole-palette transforms, one undo
    step across them, and the docker surface to reach them.
- [ ] Ink-and-paint flatting, as fills in the record `evidence: GapAwareFill, FlattingPass, FlattingTests, AGapSmallerThanTheToleranceDoesNotLeakTheFill, EveryFlatIsAnOrdinaryFillStrokeWithContours, ReflattingAfterALineEditKeepsEachRegionsColour, TheFlatColoursAreSeededFromRegionGeometryNotFromAnIndex, AFlattingPassIsOneUndoStep`
  - Laying flat colour under lineart is one of the most-hated jobs in comics
    and animation ink-and-paint, and invariant 3 is what makes this version
    different from everyone else's: a flatting pass emits **one ordinary
    `ToolKind.Fill` stroke per enclosed region**, on a layer of its own, so
    the result is auditable, editable per region, and replays like anything
    else — not a bitmap somebody has to lasso apart to correct.
  - Two halves, separable and in this order. **Gap-aware fill** first: a fill
    that closes leaks up to a tolerance (Clip Studio's *close gap*), stored on
    the fill's stroke like the rest of its record (invariant 4) so the same
    fill re-renders the same way forever. **The flatting pass** second: find
    every enclosed region of the lineart at once and fill each — the gap-aware
    fill run everywhere, plus distinct colours.
  - **Re-flow is the point of living in the record.** Lines change after
    flatting — that is the whole misery of the job — so re-running the pass on
    edited lineart must keep each region's colour, matched by spatial overlap
    with the fills already there. Deterministic throughout: regions come from
    geometry, and a fresh region's placeholder colour is seeded from its own
    geometry through `Hash01`, never from an RNG or a running index
    (invariant 2), so the same document flats the same way on any machine.
  - The tier that *names* regions — this one is skin, bind it to the palette's
    skin swatch — needs a model and therefore belongs to `## AI assistance`,
    where it is listed as speculative. Same shape as the normal-map tiers: the
    deterministic pass is the model's input, not its fallback.
  - Pairs with the regrade item above on purpose: flats bound to swatches are
    what let one palette edit regrade the whole ink-and-paint layer.
  - Effort: medium-to-high. The fill machinery, contours and clip regions
    exist; gap closing is a change inside one algorithm, the pass is new work
    (region decomposition, overlap matching, one `PerformDelta` for the lot).

### Guides and shapes

- [x] Perspective rulers `evidence: Snapper, DirectionAt, AStrokeIsHeldOnTheRayFromTheVanishingPoint, ARulerStraightensTheStrokeDrawnAlongIt`
- [x] Vanishing point tools `evidence: GuideKind, AVanishingPointsDirectionDependsOnWhereYouAreStanding, AVanishingPointPullsToItself`
- [x] Grid and snapping `evidence: Snapper, AGridPullsToItsIntersections, ATiltedGridStillSnaps, AStrokeOnAGridRecordsTheSnappedPoints`
- [x] Shape tools `evidence: ShapeBuilder, ShapeKind, AShapeIsAnOrdinaryStroke, AnEllipseFitsItsBoxAndCloses, ShiftSquaresItAndAltGrowsItFromTheCentre`
- [x] Vector guides `evidence: Guide, GuidesSurviveASaveAndReload, ADocumentWithNoGuidesWritesNoGuideKey, AHiddenGuideStillSnaps`
- [x] Rulers and guide editing `evidence: RulerStrip, TickStep, DraggingOutOfTheTopRulerLeavesAHorizontalGuide, LettingGoBackOnTheRulerThrowsTheGuideAway, AGuideIsMovedByGrabbingItOnTheCanvas, TheRulersAreAbsentUntilAskedFor`
- [x] Text `evidence: TextElement, TextBaker, GlyphOutline, FontLibrary, EachGlyphIsOneContourFillCarryingItsElement, TypeRendersFromTheRecordWithNoFontAnywhere, ADocumentNobodyHasTypedInWritesNoTextKeys, AnOpenLicensedFontIsCarriedInTheDocumentThatUsesIt, ClickingTypeAlreadySetPicksItUpToRetype`
- [ ] A paragraph box that reflows `evidence: ParagraphWidth, TextWrap, ParagraphTextTests, ADocumentThatNeverSetAWidthWritesNoWidthKey, DraggingTheBoxEdgeRewrapsTheWords, ACaretLandsWhereAWrappedLineBrokeIt`
  - **Q172, the owner's call against the recommendation.** Type today is *point*
    text: `X`, `Y`, alignment, and lines that break where somebody pressed
    Enter. A column of type has to be broken by hand, which is exactly the case
    — a title block, a caption column — where "Lightbox does not do that" sends
    the work to another application.
  - What it needs: an authored `ParagraphWidth` on `TextElement`, a line-breaking pass in
    `TextLayout.Of`, and a resize gizmo on the box that rewraps as it is
    dragged. Dragging the edge of point text is what turns it into paragraph
    text, so there is no mode to choose.
  - **`optional-settings` governs the field.** A width has to be absent from the
    JSON of every document that never set one — the anchor above names the test
    that says so — or every existing document grows a key for a feature it does
    not use.
  - **B347 is the half that is built**, and it is underneath this rather than
    beside it: entering the box, placing the caret where you clicked and
    selecting inside it are written against `TextLayout`, which does not care
    *why* a line broke. Wrapping simply produces more lines. Cost: M, and the
    risk is in the wrapping — where a line breaks decides where the caret goes,
    so getting it wrong shows up as the caret landing in the wrong place rather
    than as text merely looking odd.
  - Q149's question — *what carries the text: a stroke kind, a placement, or a
    vector-layer object* — resolved to **none of the three separately**
    (Q186–Q189, `docs/DESIGN-text.md`). A `TextElement` holds what was typed;
    committing shapes it and records one `ToolKind.Text` contour stroke per
    glyph carrying `TextId`. The strokes are the drawing and the element is only
    what lets the words be typed again — so it did not wait on vector richness
    after all, because it needed none: a glyph is a filled contour, which the
    fill tool already writes.
  - **Fonts are for editing, never for rendering**, which is what makes the
    licence question answerable rather than a lawyer's problem. Google's
    families publish a licence permitting redistribution, so a document carries
    one and stays retypable anywhere; an installed font's terms cannot be read,
    so it is named and never copied. Neither choice moves a pixel.
  - Point text only, deliberately: no wrapping box, no text on a path, one style
    per block. Those three are the items below.
  - The other half of the smart-objects request needed no item at all:
    re-editability is invariant 1, and one-drawing-placed-many-times is
    Pillar 3's symbols, already shipped for the flat case.

- [?] Box text — a dragged rectangle the words wrap inside, re-flowing when it
  is resized. Real work rather than a flag: line breaking, and a decision about
  what happens to type that no longer fits. Effort: medium.

- [?] Text on a path — flow a baseline along a drawn line, which is the
  Lightbox-flavoured half of typography and wants the placement/offset design
  settled first. Effort: medium.

- [?] Mixed styles in one block — a word bold inside a sentence. The record
  already allows it in principle (a block could carry runs), and the editing
  surface for it is the actual cost. Effort: medium.

- [?] Widths beyond normal in the font list — `FontRef` records weight and
  slant, so a condensed cut is only reachable when the foundry ships it as its
  own family name. Widening it to width is the fix, and it is small.

### Layers and compositing

**Compositing cost has two axes and they multiply: canvas *area* and *layer
count*.** Worth stating here rather than only in the ledger, because it decides
which performance work is on the critical path for everything above — 4K and
8K, and a character rig with ten layers are the same question asked two ways.

The measurement, from `AnimationSweeps.CanvasSize` with every access a cache hit
so it prices compositing alone: **linear in area (`n^1.03`), and 1344% of the
playback budget at 8K for a *three*-layer frame.** Both axes therefore have to be
answered, and answering one does not soften the other:

| Axis | Answer | State |
| --- | --- | --- |
| Canvas area | GPU compositing, display-only | B125, not started — **mandatory** |
| Layer count | Do not recomposite unchanged layers | B165, not started — **mandatory** |
| Layer count × memory | Held side composites instead of every cel resident | B198 — measured, carried by B29's candidate |
| Pixels actually served | Tiles, and the compose-scale clamp | B144, B160 — built |
| Pixels served *while painting* | A culled ring: viewport-sized *and* dirty-region-aware | B291 — **built** |

**The fifth row was not what it was first filed as, and the correction is the
useful part.** It was written up as painting getting worse the closer the artist
works; measured, it was a **flat ~4× penalty** at every zoom — an incremental
stroke publish cost 5.7–6.0 ms while a whole-canvas publish of the same document
cost 1.4–1.5 ms, because the second was culled to the viewport and the first was
not. The interactive path cost four times the path it exists to optimise.

B121's condition turned out to be about the **fresh surface**, not about culling:
the culled route builds a new surface every publish and must fill all of it,
which is where the 109× came from. `ComposeRing` keeps its buffers and already
repaints only what went stale, so it could take a smaller surface all along — it
only lacked an origin. Giving it one took the stroke publish to 1.81 ms at 100%
and **0.24 ms at 800%**, so the cost now falls with zoom instead of being flat.

The layer axis is swept to 100 as of 2026-08-14, and it added a fourth row: past
about 64 layers at 1080p a single frame's cels (~830 MB) exceed the 512 MB frame
cache, so a recomposite re-rasterizes what it just evicted — 1.04 s at 64 layers
becomes **7.1 s at 100**. The side-composite measurement (`SideCompositeHit`)
says what fixes it: two held surfaces are flat at **35 ms from 1 to 100 layers**,
and holding them also removes the residency demand that builds the wall.

**Why both.** A 20× GPU win takes 8K/three-layer from 1344% to 67% of budget:
viable, barely. The same frame at ten layers is **224% after the GPU**, because
the GPU divides the area term and leaves the layer term alone. Ten layers is an
ordinary rig, not a stress test.

**Parallel CPU compositing is deliberately not on that list.** Banding the
surface across a laptop's sixteen threads is a genuine 3–4×, and it is the same
axis as the GPU with a worse constant — so every line of it is deleted when B125
lands. It is the reserve if GPU compositing is ever ruled out, and nothing else.

That is a rejection of parallel *compositing*, not of parallelism. Wherever work
is CPU-bound, staying there, and genuinely independent, threads remain the
answer — rebuilding a document from its stroke record (B30) and export, whose
frames are independent by construction, are both open candidates. Stroke replay
is sequential *within* a frame because each mark blends onto the last, so the
axis there is frames rather than strokes.

**GPU compositing is display-only, and export stays on the CPU.** That is what
makes it safe rather than a rewrite of the renderer: the stroke record is the
document (invariant 1) and export runs through `FrameRasterizer`, so GPU blend
rounding cannot reach saved art. The two paths staying separate *is* the
constraint — `RuntimeDeterminismTests` going red means it was broken, not that
the test needs relaxing.

- [x] Layer masks `evidence: LayerMask, LayerShapes, PassShape, LayerMaskRecordTests, MaskAndClipCompositingTests, MaskEditingTests, AShapeCarvesThePassToItsCoverage, AStrokeLandsOnTheMaskAndUndoTakesItBackOffIt, TheLivePreviewShowsTheCarveBeforeThePenLifts, AMaskedLayerRefusesToFoldAndStillRenders, AMaskedUpperLayerBakesWithItsMaskApplied`
  - **A mask is strokes, like everything else (Q147)** — one `Frame` rendered
    to alpha through the one pixel path, so it is deterministic, undoable and
    inbetweenable with no second representation anywhere. Coverage is opacity:
    painting shows, erasing hides, and inverting is a flag. One drawing held
    across the whole timeline (Q148); the animated case is a clipping
    arrangement, which reuses every cel mechanism instead of duplicating them
    inside the mask.
  - **One seam for every compositor**: `LayerShapes` describes what carves a
    layer, and the canvas publish, both exporters, the MCP render, the fill's
    sampling composite, the smudge backdrop, the navigator and reference views
    all ask it — a masked layer cannot look different in two of them. Shaped
    passes refuse the fold and the tile path; merge-down bakes the mask in and
    clears it. The one known preview gap is B280 (transform drag).
- [x] Clipping masks `evidence: LayerShapes.BaseOf, AClippedLayerDescribesItsBaseAndTheBasesMask, ConsecutiveClippedLayersShareTheFirstUnclippedBase, AClippedLayerAtTheBottomRendersUnclipped, TheDescribedListSkipsAClippedLayerOverNothing, ClippingIsUndoableAndAbsentWhenReleased, AClippedUpperLayerBakesCarvedToTheLowersContent`
  - Positional, Photoshop's rule: the base is the first unclipped layer
    beneath, consecutive clipped layers share it, and the base's own mask
    carves what clips to it. A flag rather than a base id, so reordering
    means what the artist's drag means. Ctrl+Alt+G, the convention.
- [x] Adjustment layers `evidence: Layer.Adjusts, EffectsViewModel, EffectsDockerTests, AnAdjustmentPassFiltersTheBackdrop, AnAdjustmentIsCarvedByItsShapesAndFadedByItsOpacity, AnAdjustmentLayerLandsAboveTheActiveOneCarryingItsEffect, AnAdjustmentLayerChangesThePublishedComposite, AnAdjustmentLayerDescribesOneBackdropPassAndNoCelFetch`
  - **Q151 held: an effect-carrying layer, not a new mechanism.** It rides
    the effects record below, applied to the composite beneath it and scoped
    by the ordinary layer machinery — its mask carves where it applies, its
    clip grades one silhouette, its opacity is strength, its eye switches it
    off. Its cels stay empty and nothing renders them; a document without
    one writes no key.
- [x] Photoshop-style filters `evidence: SharpenSteepensAnEdgeAndAmountZeroIsExactlyIdentity, FindEdgesKeepsTheEdgeAndDropsTheFlats, ThresholdIsTwoTonedThroughLuminanceNotPerChannel, PosterizeBandsTheRangeAndKeepsBothEnds, InvertIsItsOwnUndo, AGradientMapCarriesToneToItsTwoColours, EveryPhotoshopFilterWorksOnALayersOwnStackAndOnTheBackdrop, ThePhotoshopFiltersAreOfferedEverywhere`
  - **Six, chosen for being native (Q160)**: sharpen (an unsharp mask, with
    a radius), find edges, invert, threshold, posterize and gradient map.
    Native is what lets them work on a layer's own stack as well as on
    adjustment layers and the scene — unlike Hue/Saturation and grain, which
    are CPU passes and therefore backdrop-only.
  - **The convolution primitive was measured and rejected**: one 3×3 matrix
    convolution expresses sharpen and find edges directly and costs ~1270 ms
    per 960×540 compose, twenty times an 8 px blur, because Skia's CPU
    convolution has no fast path. Rebuilt from blur, offset and arithmetic
    blend the pair measures 173 ms. Both carry a radius floor of 2, because
    Skia's raster blur is a no-op below sigma 1 and a shorter radius returns
    the picture untouched.
  - **Emboss is deliberately not here.** Relief needs a constant added to
    colour only, and the arithmetic filter adds `k4` to alpha as well, so mid
    grey arrives as half-transparent white; doing it natively takes a
    seven-node graph referencing the input four times. It is a five-line CPU
    pass once per-pixel passes can run on a layer's own stack, which is its
    own branch — so it waits rather than shipping badly.
- [x] Effects that vary by frame `evidence: DefaultOf, EffectShelf, AWiggleMovesTheMarkAndStaysPutForTheLengthOfItsHold, AFlickerDipsOutOfFullStrengthAndNeverAboveIt, TwoWigglesDoNotMoveInLockstep, ATimeSeededStackRebuildsPerFrameAndAStaticOneDoesNot, ATiledRepaintGrainsExactlyAsAWholeOneDoes, GrainDoesNotReRollWhenTheSurfaceScales, AWiggleBoilsWhileTheDrawingHolds`
  - **The animation shelf's first inhabitants (Q159)**, and the design's
    step 4: wiggle and flicker (native, either path — a wiggle over the whole
    composite is a camera shake) and film grain (a `DeterministicHash` CPU
    pass, so backdrop-only like HSL — the noise has to be ours, not Skia's,
    or a library upgrade re-renders a finished film). Frequency is a **hold**
    in frames rather than a rate, and the effect reads the playhead rather
    than the drawing, so a cel held for three frames still boils.
  - **Two traps the design named in advance, both real and both now pinned**:
    the filter cache fingerprints on parameters *evaluated at the frame*, so
    a frame-seeded effect was served frame 0's chain forever until
    `TimeSeeded` put the frame in the fingerprint; and the CPU pass runs on a
    clip-bounded readback in device pixels, so grain re-rolled on a bounded
    repaint and again at 2× until the readback's origin and the device scale
    travelled with it.
- [x] Layer styles `evidence: EffectColorSpec, StyleFor, SelfStyle, ADropShadowFallsAwayFromTheLight, AnOuterGlowHalosTheSilhouetteAndAnInnerGlowStaysInside, AStrokeOutlinesWhereItsPositionSays, ABevelLightsTheEdgeFacingTheLight, AStyleDecoratesTheCarvedSilhouetteNotTheUnmaskedContent, AStyleIsOfferedOnlyWhereItHasASilhouette, TheMasterSwitchMutesTheStackWithoutTouchingItsUses, TheStackMasterSwitchSilencesEveryChainAndTheCacheFollows`
  - **Effect kinds on the layer's own stack (Q153), not a second record**:
    drop shadow, outer glow, inner glow, stroke, and the smooth bevel
    (Q154 — contour and gloss wait for the curve editor). All native filter
    graphs reading the pass's silhouette, so the one-filtered-redraw fast
    path holds; self-only, the mirror of the CPU grades' backdrop-only.
    Styles decorate the *carved* silhouette (Q155): content → filters →
    mask carve → styles, so a glow hugs what the mask leaves. Colours are
    an optional `Colors` map on the use — absent until authored, not
    keyable until colour curves are worth keying.
- [x] Blend modes `evidence: LayerBlendMode, BlendComposeTests`
- [x] Layer folders `evidence: LayerGroup, LayerFolderTests`
- [x] Layer and alpha locking `evidence: LayerLockTests, AlphaLockTests`
- [x] Non-destructive filters `evidence: EffectUse, EffectStack, EffectRegistry, EffectPasses, EffectRecordTests, EffectRegistryTests, EffectPassTests, EffectComposeCostTests, ASelfEffectFiltersOnlyItsOwnPass, AnUnknownKindIsPreservedNotDropped, AKeyedRadiusEvaluatesPerFrame, AFilteredLayerRefusesToFoldAndStillRenders, TheSceneStackDescribesALastPass`
  - **Built to `docs/DESIGN-effects.md`, steps 1–3 of its own build order**:
    the record (on Q122's shared `EffectParam`, so wind, camera and blur key
    in one vocabulary), the Raster registry with the first three of the v1
    catalogue (levels, HSL, Gaussian blur — one of each seam the catalogue
    names except the seeded one), the seam through every compositor, and the
    effects docker with its own view model, the decoupling the design made
    the review bar. Unknown kinds are preserved and rendered as identity;
    stacks and params are absent until authored at every level.
  - **Deliberately still open, from the design's own list**: keying UI on the
    timeline (the record already carries keys and evaluates them — see
    `AKeyedRadiusEvaluatesPerFrame` — the editor for placing them arrives
    with the curve editor); film grain and vignette (grain is the invariant-2
    seeded case and wants its `Hash01` test alongside); the layer-effect
    output cache (today a static blur re-runs per recomposite; the fold and
    tile refusals bound the cost, `EffectComposeCostTests` budgets it, and
    its blur ratio is the number the cache should visibly move); presets as
    project files (design step 5); and MCP `effects.*` operations, deferred
    with the payload questions G12's pair review owns.

### Editing

- [x] Selection tools `evidence: SelectVariant, ClipRegion, SelectionTests, ClipRegionRegistry, BeginSelectionMove, SelectionCtrlMoveTests, CtrlInsideAMarqueeMeansMoveRatherThanPick, CtrlOutsideTheMarqueeStillPicksAColour, WithNoSelectionCtrlIsTheEyedropperExactlyAsBefore, ThePointerSaysMoveWhileCtrlIsHeldOverTheSelection, TheDragMovesWhatIsInsideAndLeavesWhatIsNot, APressAsksAboutTheSelectionBeforeItFetchesAColour, TransformClipsToTheSelectionTests, HalfSelectedIsHalfMoved, PolygonSelectionPreviewTests, TheFirstVertexIsDrawnBeforeThereIsASecond`
  - **A region means the same thing to every tool that reads one** (B319, Q166): paint is clipped to it, a copy takes what it covers, and a transform moves what it covers and leaves the rest. The transform was the odd one out — it shared the filter with the clipboard and never got the clip, so it moved whole strokes chosen by a majority vote and refused outright when the marquee caught less than half of one.
  - The tools that make a region are also the ones that show it being made: the polygon rings its first vertex and bands to the pointer (B315), a half-drawn one goes when you reach for another shape (B316), and the rubber band sits under the hand on a cropped page (B317).
  - **Built: Ctrl inside a selection drags what is in it (Q104).** Requested as *"when selecting and pressing ctrl and hovering on a selected area (and during) enable moving"*, and the whole question was where the boundary sits, because Ctrl was already the held eyedropper. The narrower claim wins: the move needs a selection *and* the pointer inside it, so the picker keeps the rest of the canvas. That is only defensible because the cursor says which one is armed before the press — the item above, one commit earlier.
  - **It reuses rather than adds**, which is the point: `BeginSelectionMove` is `BeginLineMove` with a different refusal, the filter is `DerivedTransformFilter`, the press rides the line drag's own move/commit/discard channel, and the marching ants already follow a session's preview matrix. Nothing new had to be built for the undo step, the axis lock, the guides or the outline.
- [x] Undo restores a mark's pixels instead of rebuilding them `evidence: MarkSnapshot, UndoMarkSnapshotTests, RestoringAMarkCostsItsAreaRatherThanTheDrawing, ASmudgeUndoesWithoutFallingBackToTheWholeRender, ARebuiltDrawingForgetsItsSavedPixelsSoTheRedoIsNotStale, ARenderingThatArrivedAfterTheMarkSendsUndoToTheReplay, PixelsAreNotKeptForStepsPastTheUndoDepth`
  - **Q167, answered 2026-08-26.** B327 stopped undo re-stamping every stroke on a drawing and made it replay only the reverted mark's footprint — a scattered 800-stroke drawing went from ~1 498 ms per Ctrl+Z to 6.7 ms. It still rebuilds by **replaying the record**, which leaves two holes: a mark covering most of the drawing replays most of the drawing, and a smudge or blur anywhere in the patch forces a full re-render because an effect brush would sample pixels from the wrong moment.
  - **The answer is Photoshop's and Krita's: copy the tiles under a mark before stamping it, and swap them back on undo.** Their undo costs the area changed and nothing else. Measured on a 960×540 document before deciding, because *"or test it"* was the instruction:

    | drawing | patch | snapshot on commit | restore on undo | replay today |
    | --- | --- | --- | --- | --- |
    | scattered, 800 strokes | 6 KB | 0.007 ms | 0.016 ms | 39.4 ms |
    | hatched band, 800 strokes | 279 KB | 0.039 ms | 0.045 ms | **7 497.3 ms** |
    | canvas-crossing, 50 strokes | 1 975 KB | 0.695 ms | 0.937 ms | 1 600.6 ms |

  - **The commit-cost worry was tested and did not survive.** The objection to snapshotting was that drawing happens a thousand times an hour and undo does not, so a slower pen lift would be a bad trade. The copy costs 0.039 ms on a 279 KB patch against the ~7.5 ms a commit already pays. It only approaches 9% of a commit on a full-canvas mark.
  - **So the budget is a guard, not a design principle.** It exists because a canvas-crossing mark is ~126 MB across a 64-step history at 960×540 and over 2 GB at 4K — not because of commit time. On ordinary drawings a mark is 6 KB and it never engages. Express it as a total the way `FrameBitmapCache.ByteBudget` is, so trimming is an eviction policy rather than a refusal at commit time.
  - **It does not touch invariant 1.** The stroke record stays the document; the snapshot is a cache of a state the record can already describe, and a snapshot that disagreed with the record would be a bug rather than a second source of truth. B327's replay stays for steps that name no footprint and for snapshots trimmed out of the budget, and structural undo is untouched — a `SnapshotStep` has no mark to name. Reuse `TileGrid`, `TiledRasterizer` and `_tileFrames` rather than inventing a second set of squares. Cost: L

  - **Built 2026-08-31.** `MarkSnapshot` copies the pixels under a mark aside in `AppendToFrameRender` — the last moment they still exist — and `ApplyEditScope` swaps them back. Reported again by the owner as a delay undoing in a reference view, which is the same shape B327 was reported as and the residual that entry names as its honest limit.
  - **Rectangles rather than tiles, deliberately, and it is a deviation from the answer above.** Q167 said to reuse `TileGrid` on Krita's model, whose canvas *is* tiles. Lightbox's is not — `FrameBitmapCache` holds one flat bitmap per frame — so rounding a 42×34 mark out to 256-pixel tiles would store 256 KB–1 MB where the exact rectangle stores 6 KB, with no copy-on-write sharing to win it back. That would have contradicted the 6 KB figure this item's own budget argument rests on. Put to the owner before building and chosen. `AMarksPatchIsItsOwnAreaRatherThanAGridOfTiles` is what stops it drifting back.
  - **An exchange, not a restore, which is what makes redo free too.** The patch holds whichever side of the step the drawing is not on, so undo and redo are the same call. That invariant only survives while every transition goes through it, so any path that rebuilds a drawing another way forgets its patches — the one failure mode here that is wrong rather than slow, and `ARebuiltDrawingForgetsItsSavedPixelsSoTheRedoIsNotStale` is the test that holds it.
  - Measured on this branch, 960×540, minimum of eight, both arms in the same process on the same drawing — and every restore arm ran 16 restores with **zero fallbacks**, which is what says the fast path is the one being timed:

    | drawing | replay (B327) | restore | |
    | --- | --- | --- | --- |
    | scattered, 200 strokes | 4.9 ms | 3.6 ms | 1.4× |
    | scattered, 800 strokes | 6.0 ms | 4.0 ms | 1.5× |
    | scattered, 2 400 strokes | 21.6 ms | 10.8 ms | 2.0× |
    | hatched band, 200 strokes | 73.7 ms | 4.0 ms | **18.6×** |
    | hatched band, 800 strokes | 323.9 ms | 3.2 ms | **102.7×** |
    | hatched band, 2 400 strokes | 526.6 ms | 4.3 ms | **122.7×** |

  - **Restore is flat at 3–4 ms down the whole column**, which is the property worth having rather than any one ratio: it tracks the mark's area and has stopped tracking the drawing. The scattered rows are small gains because they are the case B327 already handled well — the hatched rows are the case Q167 exists for, and they are where an artist on a model sheet actually lives.
  - **A 64-step history of ordinary marks costs 713 KB**, three orders of magnitude under the budget, so the guard never engages in normal work.
  - **The byte budget was not enough on its own, and measuring is what said so.** `MaxUndo` trims the undo stack but nothing told the store, so a hatched 2 400-stroke drawing held **194 MB of patches for 64 reachable steps** — correct, and memory the artist pays for and cannot spend. `MarkSnapshot.MaxSteps` follows the undo-depth preference, and `PixelsAreNotKeptForStepsPastTheUndoDepth` holds it.
  - **Q167's second hole is closed as a side effect.** A smudge or blur forced B327 to re-render the whole drawing, because replaying an effect brush inside a region would sample ink painted after it. Saved pixels re-stamp nothing and so have no sampling problem — `ASmudgeUndoesWithoutFallingBackToTheWholeRender` against B327's `ASmudgeOnTheDrawingSendsUndoBackToTheWholeRender`, the pair asserting opposite outcomes on the same drawing.
  - **What still takes the rebuild**, all of it correct and slower rather than wrong: a step naming no footprint (the text commit), a history jump across several steps, a mark whose patch outgrew the budget, a rendering that arrived after the mark was saved (an export at 2×), and **a mark off the edge of the paper** — `CommitBounds` clamps to the surface, so an off-canvas stroke has no footprint for either path to use. The last one is pre-existing B327 behaviour and was found by a probe that drew off the bottom of the document and reported no gain at all.
  - **The three fallbacks are counted, not assumed**: `MainViewModel.FrameRegionRestores` against `FrameRegionRepaints` and `FrameRenderDrops`, plus `MarkSnapshot.Fallbacks`, which is what answers "how often does the fast path fire in real work" — the question this item left open.
  - Detection power proved with three mutants: writing the patch without taking the old pixels back kills two tests, skipping a rendering the snapshot never saw kills one, and leaving the patches in place across a rebuild kills one. B327's own suite keeps testing the replay, because `UndoRegionRepaintTests` now clears the snapshots in its warm-up.
- [x] Warp transform `evidence: TransformToolTests, TransformBegun`
- [?] Liquify
- [?] Clone stamp
- [?] Healing brush
- [~] Vector selection that matches the hand it was learned with `evidence: PathHoverPreview, PathEditSession, CloseIndicator, PenActive, PathEditingTests, HoveringALinePreviewsItsPointsAndHandles, ClickingALineWithTheWhiteArrowSelectsAllOfItLikeTheArrow, PickingOnePointKeepsTheOthersVisible, TheHeldModifierEntersDirectSelectFromThePen, ClickingTheCloseIndicatorStrokesTheWholeShape`
  - **The two arrows exist and do not yet behave like the tools they are named after.** Requested against Illustrator, which is the vocabulary anyone doing this work already has, and the pieces are specific enough to build without further design:
    - **Hover previews the geometry.** Moving over a line shows its points and handles before anything is clicked, so an artist knows what they are about to grab. Nothing shows geometry on hover today.
    - **Clicking the line selects the whole path**, exactly as the black Arrow does — the white arrow's difference is what it can then do to it, not what a click means.
    - **Clicking a point or a handle picks that one and leaves the rest visible.** Selection narrows; the drawing does not disappear around it. `PathEditSession` already tracks node selection, so this is the presentation half.
    - **A modifier held over the pen enters direct select**, the way Illustrator's Ctrl does, so a path can be corrected mid-draw without putting the tool down. This is the same registry problem as B176 and should reuse whatever momentary machinery that lands — two independent hold implementations is how one of them becomes unrebindable.
    - **The pen shows an on-canvas close indicator**, and clicking it strokes the whole shape. Closing a path is currently something you find out worked afterwards.
    - **A modifier over the white arrow widens the stroke.** Line weight after the fact already exists as the Width tool; this is reaching it without a tool change.
  - **The one part that is a defect and not a feature is filed separately as B172**: the white arrow cannot enter a path at all today — `_enterPathEdit` is wired only to the black Arrow's double-click — so the tool is inert on its own. That is a bug with a repro and it should not wait for this item; this item is what the tool does *once it works*.
  - Ordering: hover preview first. It is the cheapest of the six, it is the one that makes the others discoverable, and until geometry is visible before a click every other gesture here is guesswork with a mouse.
  - **Built: the hover preview, and the click that B172 was about.** Moving over a line with the white arrow shows its points and handles; clicking it enters and takes hold in one gesture. The two shipped together because they are the same discovery — seeing the points is what tells an artist the click is worth making.
  - **The preview goes through isolation's own overlay channel**, as the pen already does, because only one of the three can be live and two node lists would be a question with no answer that shows up on screen as both. Isolation wins outright when it is active; nothing is drawn selected on a hover, because a preview says what is *there*, not what is picked.
  - **It fits once per line, not once per pointer move.** A hover fires continuously and `PathEditSession.Open` runs a curve fit over every point of the stroke, so refitting per event would put work proportional to a stroke's length in a per-event path — the shape invariant 6 rules out. `HoverPathAt` returns whether the answer moved, so the canvas repaints on the frames that matter, and `HoveringAlongTheSameLineDoesNotRefitIt` is what keeps that true.
  - Still to build: clicking the line selecting all of it, the pen's held modifier, the close indicator, and the widen modifier. The four remaining anchors do not resolve, which is what keeps this item honestly in flight rather than green.

### Replay

**The stroke record is already a recording of its own making** — invariant 1
means every saved document carries, in order, every mark that survived into the
final image. Procreate and Clip Studio bolt a screen recorder onto the app to
get a timelapse; here the timelapse is structural: it was never switched on, it
exists for every document ever saved, and it can come out at any size because
it is re-rendered rather than recorded. What is missing is only presentation.

- [ ] Scrub a drawing back through its own strokes `evidence: DocumentReplay, ReplayCursor, DocumentReplayTests, ScrubbingToAStrokeShowsExactlyTheDocumentAsOfThatStroke, ScrubbingNeverWritesToTheDocument, SteppingForwardStampsOneStrokeRatherThanRebuildingTheFrame`
  - A replay position is *a prefix of the record, rendered* — which is what
    loading a document already does, stopped early. Strictly a view: the
    scrubber never mutates the document, and leaving replay returns to the
    live drawing untouched. Scoped to the frame in view, so it costs a
    sequence nothing and needs no new concept to explain there.
  - **Stepping forward is one stamp, not a rebuild.** Replaying stroke k+1
    onto the surface that showed stroke k is how the renderer works anyway; a
    per-position rebuild is quadratic over the document and is the shape of
    mistake invariant 6 names. Scrubbing *backwards* is the expensive
    direction, and it wants the answer the frame cache already embodies:
    checkpoint surfaces every k strokes, re-stamp from the nearest one.
  - **Order is the record's order** — within a layer as authored, across
    layers bottom-up, composited as of each position. The record stores no
    wall-clock time, so a true interleaved chronology across layers does not
    exist; if one is ever wanted it is an optional per-stroke timestamp,
    absent until recorded, never required. Worth noticing what the record's
    order buys instead: undone strokes are not in it, so a replay shows the
    drawing's decisions rather than its hesitations — tighter than a screen
    recording, and honest about being so.
  - Effort: medium. The rendering primitive exists (it is loading); the work
    is the checkpointing, the scrubber surface, and keeping replay legibly
    read-only in the UI.
- [ ] Export the replay as a timelapse `evidence: TimelapseExporter, TimelapseExportTests, ATimelapseIsOneForwardPassHoweverManyFramesItEmits, TheFramesComeOutAtTheAskedSizeViaTheSurfaceNeverTheGeometry, AThousandStrokesEmitAsManyFramesAsAskedNotAThousand`
  - One forward replay pass, emitting a composited frame every k strokes, k
    derived from the asked length — a sketch and a two-hundred-hour painting
    both come out at thirty seconds. Frames leave through the existing
    sequence-export machinery rather than a second encoder.
  - Output size is a surface scale, never a coordinate multiply — invariant 7
    verbatim, and the reason a timelapse can be exported at 4K from a document
    painted at screen size. It is also the test that this is a replay rather
    than a recording: a screen recorder could never answer for pixels it
    never showed.
  - Effort: low once scrubbing exists — it is the same pass with a frame sink
    attached, and the export plumbing already handles sequences.

### Interop

- [~] PSD import/export `evidence: PsdReader, PsdDocumentImport, PsdBlendMap, PsdReadTests, PsdImportTests, PsdFixture, ChannelsBecomeRgbaAtTheLayersOwnOffset, EveryCompressionSchemeDecodesToTheSamePixels, EveryReasonIsCollectedBeforeRefusing_NotJustTheFirst, ALayersPixelsLandOnTheBaselineAtTheirCanvasPosition, APhotoshopFolderBecomesALayerFolder, PsdWriter, APsdRoundTripsThroughPhotoshopWithItsLayers`
  - **Built: import.** RGB and greyscale, 8 and 16 bits, PSD and PSB, raw / RLE /
    ZIP channels, folders, and layer name, visibility, opacity, blend mode and
    locking. `.psd` and `.psb` open through **File ▸ Open…** rather than a
    separate Import item, because "open this drawing" is the same intent whoever
    made the file.
  - **Imported pixels land on `Frame.PngBase64`**, the baseline that has been in
    the model since the two frame classes merged for exactly this — "pixels with
    no stroke provenance" — and whose own comment recorded that nothing in the
    application had ever written one. Invariant 1 is untouched: a frame is
    `baseline + strokes stamped on top`, so a PSD layer is a drawing to paint
    over and every mark added afterwards is still a stroke.
  - **Masks and clipping are imported**, and they were refused for one day before
    they were not. `main` landed layer masks and clipping (Q147/Q148) between this
    branch starting and merging, and `LayerMask` holds an ordinary `Frame` — so a
    PSD mask arrives exactly as a layer does, as baseline coverage, and
    `ClipToBelow` already implements Photoshop's consecutive-clipped-layers rule.
    A mask's rectangle is its own rather than its layer's, and what lies outside
    it is a byte in the file rather than a convention; both matter, because
    guessing either hides or reveals three quarters of somebody's drawing. This
    is the single biggest reduction in what the refusal costs.
  - **The decision that shapes it (2026-08-24): a PSD using features Lightbox has
    no model for is refused, by name, all at once.** Adjustment and fill layers,
    text, smart objects, layer effects, vector masks and a folder that blends as
    a group all change what the pixels beneath them look like. The
    alternative on the table was to take Photoshop's own flattened composite for
    those layers, which always *looks* right and silently discards the stack; the
    owner chose refusal instead. **The cost is real and was accepted knowingly**:
    plenty of production files have an adjustment layer or a mask somewhere and
    will not open until it is flattened. What makes it defensible is that the
    refusal is a list — every feature, the layer carrying it, and the Photoshop
    menu path that fixes it — so one trip back should be enough. The mask work
    above is also the pattern for shrinking it further: every refusal here is a
    missing *model*, so each one Lightbox grows turns a refusal into an import
    rather than needing the reader rewritten.
  - **Not built: export.** Declined for this pass in the same exchange, which
    leaves Lightbox able to read a Photoshop file and not hand one back — the
    half most artists will notice. Writing a PSD is markedly easier than reading
    one, because the writer chooses the compression (RLE) and never meets a
    feature it cannot represent, so this is a small item rather than a research
    project. `PsdWriter` and `APsdRoundTripsThroughPhotoshopWithItsLayers` are
    the anchors that will resolve when it lands, and they deliberately do not
    resolve today.
  - **Baselines are canvas-sized**, because `FrameRasterizer.Materialize` draws a
    baseline stretched over the whole canvas, so a layer stored at its own
    smaller bounds would be scaled up to fill the frame. A nullable rect beside
    the baseline is the better answer and changes a serialized type that
    `ImageResize`, `Crop`, `Transform` and `LayerMerge` all read, so it was kept
    as a follow-up — **B304**, cost M.
  - **What that costs was measured rather than asserted, and half the assertion
    was wrong.** The claim written down at the time was "only decode time and
    memory pay". The file-size half held up completely: PNG and gzip crush the
    transparent margin, so a 12-layer 4K import is 12 KB on disk. The time half
    did not — reading the PSD is 1–3% of the work and building the baselines is
    the rest, which is about four seconds for that file. PNG compression level
    is the obvious lever and is not one. `PsdImportCostTests` holds the numbers
    and a loose budget so the attribution cannot quietly invert.
  - **The reader's safety was claimed, then refuted four ways** by an adversarial
    pass on the same day, and all four are worth knowing because the existing
    thirty-five tests — a byte-by-byte truncation fuzz among them — caught none
    of them. Three were one shape: an attacker-controlled 64-bit PSB length
    surviving a bounds check and truncating to a negative `int`, after which
    `Pos + count <= End` is true for every count and the "bounds check" is not
    one. `PsdCursor.Has` now compares by subtraction, `PsdCursor.Pos` refuses to
    leave its section at all, and `PackBits` accumulates its scanline cursor in
    64 bits — three fixes rather than one, because each closes the hole at a
    different distance from the caller.
  - **The fourth was the interesting one, and it was a refusal bypass rather than
    a crash.** A layer mask is announced *twice* in a PSD — a length field in the
    layer's extra data, and a channel id in its channel table. The reader
    believed only the first, so a layer carrying real mask pixels with
    `maskLength = 0` imported as a plain opaque layer and silently threw the mask
    away: exactly the failure refusing exists to prevent, reached from the side
    nobody was watching. `PsdHostileInputTests` is the regression suite, and the
    lesson generalises — a fuzz that only truncates well-formed files never
    corrupts a length into a value that survives the check.
  - **A per-layer memory ceiling turned out not to be a ceiling.** Layer bounds
    are independent of the canvas, so a 4×4 document declared four 10,000×7,000
    layers — each under any generous per-layer cap — and asked for about 3 GB
    from an 800 KB file. The bound that actually prices the suspicious thing is
    the *ratio*: content past the canvas edge is ordinary in Photoshop and
    ordinary by a small margin, so a layer is capped at four times the canvas
    area with a floor for small documents, and a running total backs it up.
  - **The fixtures are the part worth copying.** There is no Photoshop here, so
    the test PSDs are built in C# as the brush-format tests already build `.abr`
    and `.gbr` — and then cross-checked against `psd_tools`, an independent
    implementation, in both directions. That check found a real defect on its
    first run: every fixture omitted the trailing image data section, which
    `psd_tools` rejects as corrupt, so the reader had been green against files no
    other application would open.
- [x] Tablet optimization `evidence: PressureTests, PressureVmTests, PenDiagnostic`
- [~] Save as an ordinary image format — PNG, JPEG, SVG `evidence: ImageSaveFormat, SaveAsImage, ImageSaveTests, ASvgSaveKeepsVectorLayersAsPaths`
  - Export writes sheets and sequences for engines; there is no plain "save this as a picture". PNG and JPEG are small and mostly plumbing. **SVG is the interesting one and should not be faked**: a raster document cannot become an SVG except as an embedded bitmap, which is a lie in a vector wrapper. It is only honest for the vector layers, and it needs the vector side to be richer first — which is what makes it the same item as the one below.
  - JPEG needs a quality control and a warning that it has no alpha, or somebody exports a character on a white box and finds out later.
  - **Built: PNG, JPEG and WebP**, through `File ▸ Save as image…`
    (Ctrl+Alt+Shift+S). One image by default — the missing verb this item names —
    with an opt-in *every frame* that writes numbered files, which exists because
    `ExportPngSequence` is PNG-only so a JPEG or WebP sequence had no route at
    all. It renders through `SequenceExporter.RenderFrame`, so a saved PNG and
    that frame from an exported sequence are the same bytes by construction; a
    test asserts exactly that, because two compositing paths would be free to
    drift.
  - **The alpha warning arrives before the save, not after.** The dialog says so
    when JPEG is picked on a document that has transparency, and the result
    reports what actually happened — measured from the rendered pixels, so a
    fully painted canvas saved as JPEG warns about nothing. Where transparency
    *is* lost it is filled with white rather than left to darken toward black,
    which is what handing a premultiplied image to the JPEG encoder does.
  - **Three formats and not more, because that is what Skia here can encode.**
    Measured rather than assumed: of the fourteen `SKEncodedImageFormat` values,
    eleven — BMP, GIF, ICO, WBMP, PKM, KTX, ASTC, DNG, HEIF, AVIF and JPEG XL —
    return null from `Encode`. TIFF and PSD are the two absences an artist will
    look for and both would be ours to write.
  - **SVG is still not built and the box stays open for it**, on this item's own
    reasoning above. `ASvgSaveKeepsVectorLayersAsPaths` does not resolve, which
    is what keeps the item honestly in flight rather than green.
- [~] Lightbox draws its own icons `evidence: IconSet, IconSetTests, EveryToolbarButtonResolvesAnIcon, NoButtonAnywhereWearsAGlyphInsteadOfAnIcon, EveryIconIsAuthoredOnTheSameGrid, ASelectionVariantIsNeverTheShapeToolsOutline, IconSourceDocument`
  - Every icon in the app should be one set, made deliberately rather than assembled. The interesting part is *how*: **the app should draw them itself**. That needs vector tooling good enough to author a 16 px glyph and an SVG save that emits real paths, which is the honest dependency chain — icons wait on the vector side, and the vector side is worth having anyway.
  - Generating the SVGs directly is the fallback and is fine as a first pass, but it is a worse test of the product: a drawing application that cannot make its own icons is telling you something about its vector tooling. Dogfooding here is a feature, not a vanity.
  - The mechanical half is separable and can land first: one place that names every icon, so a missing one fails a test instead of showing a blank button, and so a redraw is a single swap. That also settles the cut-off-icon complaints, which are a sizing question the current pile of assets cannot answer consistently.
  - **Built: the registry, and the twelve icons that were still characters.** `IconSet` names all 33 and is walked from both ends — every name resolves to a geometry, and every geometry drawn in `Icons.axaml` is named. Four tool buttons were drawing Unicode from the system font: `➤` and `➢` for the two arrows, four box-drawing characters for the shape variants, and five for the select variants including **U+1FA84, an emoji** — full colour beside monoline glyphs where the platform had that character, a blank box where it did not. `NoToolButtonDrawsAGlyphFromASystemFont` is written against the *shape* of that mistake rather than against those eleven characters, so the next one fails too.
  - **The select variants are a system rather than five drawings**: the *dash* means selection, the shape means which one. That is what lets a sixth variant be drawn later without inventing a visual language for it, and it is why the box and the ellipse are not the shape tool's outlines reused — a solid outline is a shape the tool will draw, a dashed one is a region it will select, and those two tools sit next to each other in the rail.
  - **Extended to the whole application** (2026-08-16): the other ~50 glyph
    buttons — the transport, the view and shortcut bars, every docker's verbs,
    the select-variant radios, the docker chrome (float/dock, collapse, grip)
    and the stateful pairs behind bound properties (play/pause, the collapse
    chevrons, the publish arrows) — now draw from the same set, which grew to
    ~75 geometries. `NoButtonAnywhereWearsAGlyphInsteadOfAnIcon` widens the
    tool-rail guard to every Button, ToggleButton and RadioButton in the app;
    labels with words in them ("＋ Swatch") stay typography on purpose. This
    unblocks the 26→20 tile question Density.axaml records, which is its own
    change because the rows resize with it.
  - **Still the fallback this item warns about**: these were authored as SVG path data by hand, not drawn in Lightbox. `IconSourceDocument` is the anchor that will resolve when the set has a `.lbx` source and an SVG export behind it, and it deliberately does not resolve today. What landed is the registry that makes that redraw a single swap, which is the order the item asked for.

---

## Pillar 1 — Scope-based projects, not file-based

The switch that reframes everything else: **the unit of work is bigger than a
file, and things that belong together share one palette, one brush set, one set
of references, one export configuration.** This is also where the project-type
and workspace split lands — see "Project architecture" below.

**Renamed from "Character-based projects" on 2026-08-06, and the rename is the
point rather than tidying.** The pillar's claim was always about *grouping* —
that a file is too small a unit and the things around a drawing should be shared
by whatever the drawing belongs to. "Character" was the example that made it
concrete, and it hardened into the mechanism: a palette could be shared by a
character and by nothing else. Q30 separates the two again. A character is now
a folder that carries character data, resources are declared on any scope and
accumulate down the tree with the nearest winning ties, and the pillar says what
it always meant.

Read the completed items below as **history, not as the design**. They were
built and they work; several are character-shaped because that was the only
scope available when they landed, and Q30 is what widens them:

| Landed as | Becomes, under Q30 |
| --- | --- |
| Shared palette across *a character's* animations | A palette on any folder, resolved by walking up from the document |
| Character library, gated to the Asset Library type | Project-based, available project-wide — which is the same conclusion *Making reach unconditional* reached from the other direction |
| Character workspace — animations, assets, references, palette in one place | The same set, declared on whichever scope owns them |
| Project browser — characters and their animations | One tree, which B85/B86 already built beside the old two axes |

None of that unbuilds anything. It is the difference between *the only place a
palette can live* and *one of the places a palette can live*.

- [x] Project type recorded, absent by default `evidence: ProjectType, AProjectWithNoTypeWritesNoTypeKey, ADeclaredTypeSurvives`
- [x] Project types at creation (Illustration / Animation / Game Art / Storyboard / Comic / Asset Library / Empty) `evidence: NewProjectDialog, NewProjectSettings, NewDocumentSettings`
- [x] Project as a container above the document `evidence: ProjectManifest, ProjectIo, Project, ProjectTests, AProjectRoundTripsThroughTheFolder`
- [x] Character workspace — animations, assets, references, palette in one place `evidence: ReferenceSheet, ReferenceSheetModelTests, ReferenceTabTests`
- [x] Character library `evidence: CharacterLibrary, LibraryEntry, ImportingASubjectBringsItsDocumentsAndPalette, AnImportedSubjectStillPaintsFromItsPalette, AnImportSurvivesSavingAndReopeningTheProject, ImportOrigin, ImportResult, LibraryViewModel, LibraryWindow, ReImportReplacesByProvenanceAndNeverTouchesLocalWork, AnEditedCopyIsKeptAndNamedBeforeItIsReplaced, ImportLandsInTheOpenProjectTheDockerAndTheDisk, TheImportCommandIsRegisteredSoItCanBeFoundAndRebound`
  - **Slice 2 landed 2026-08-20: the way in, and the merge.** `ImportOrigin`
    stamps every copied folder and document with its library source — ids,
    never paths, so the stamp survives the library moving and is the edge a
    future dependency graph reads — plus a content hash, which is what makes
    "edited since import" answerable without the library present. Re-import
    merges (Q138): it replaces exactly the provenance-matched copies, adds
    what the library gained, never touches local work, and the one
    destructive act — replacing an edited copy — asks first with the names,
    Q35-style, keeping as the default. `ImportingTwiceGivesTwoDistinctFolders`
    was rewritten to `ImportingTwiceMergesIntoOneFolder`: the old behaviour
    was the declined numbered-beside option, recorded before Q138 decided.
  - **One view model, two surfaces**: the picker on the project panel (a
    flyout; four lines in the ratcheted axaml because the flyout is built in
    a partial) and `LibraryWindow`, the browsing home with the roots editor —
    both read the same `LibraryViewModel`, so they cannot drift. Roots are
    app-level settings, empty by default, scanned when a surface opens and
    never at startup. Palettes and variants arrive with a first import; a
    library's later recolour does not propagate into a folder that already
    has its own — that is Pillar 3's job, and it starts from these stamps.
  - **Slice 3 landed 2026-08-21: the registries**, which is what closes the
    item — the walk through "land the places it shows up", each surface to
    its registry. `project.libraryWindow` in `ShortcutMap` (searched,
    rebindable, no default key; the picker's Browse item and the command
    share one opener). A **Library page in Configure** whose roots list *is*
    the `LibraryViewModel`'s collection — one owner, so Configure, the window
    and the picker cannot hold three answers about where libraries live. The
    **MCP op `import_character`** rides the same scan, merge and after-path
    the UI uses, with the edited-copy gate reshaped for a caller with no
    dialog: edited copies are kept and reported unless `replaceEdited` says
    otherwise, so an agent destroys nothing it did not name. And the manual's
    character-library section says what ships rather than what was planned.
  - **The engine is proven (Q138 slice 1, 2026-08-20), and most of it already
    was** — the two anchors this item carried for weeks named tests that
    existed under other names: B114's subject rename moved
    `…Character…Animations…` to `…Subject…Documents…` in
    `CharacterVariantTests` and the ledger kept pointing at the old words, so
    the item under-reported itself. The anchors now name the real tests, plus
    the one proof that was genuinely missing and landed with them:
    **an import survives saving and reopening the project** — documents from
    their own files, the palette declaration, the variant with its recolour
    and rebased override, the reading. Everything before it proved the import
    in memory; a library that works until the artist quits is not a library.
  - **What keeps this `[~]`** are Q138's remaining slices, named as anchors so
    the box cannot lie: the way in (picker + window over one view model,
    roots in Configure, the registered import command) and the provenance
    merge — `Import` stamps each copy with its library source id so re-import
    replaces exactly what came from the library, adds what it gained, and
    never touches work the artist made locally, warning Q35-style before
    replacing an edited copy.
- [x] Character variants that inherit animations (Default / Winter Armor / Damaged) `evidence: SubjectVariant, DocumentsFor, PaletteStandInsFor, AVariantInheritsEveryDocumentItDoesNotOverride, AnOverriddenDocumentReplacesOnlyItself, VariantViewingTests, SwitchingTheVariantRepaintsTheSharedDrawing, GivingTheVariantItsOwnVersionIsADuplicateThatStandsIn, RecolouringTheVariantThroughThePanelLeavesTheBaseAlone`
  - **The same under-reporting the character library had, fixed the same day
    it was noticed**: the anchors named `CharacterVariant` and
    `AnimationsFor`, which B114's subject rename had made `SubjectVariant`
    and `DocumentsFor` — the engine was proven all along and the box could
    not say so.
  - **What actually landed with the rename of the anchors (Q143's
    prerequisite, 2026-08-21) is the way in.** The model shipped whole and
    unreachable: `ActiveVariant` was written by nothing, `OverrideDocument`
    had no caller, and a variant made in the project window could never be
    *looked at*. Now the docker's folder rows carry the picker (right-click ▸
    Variant), the viewed variant's name on the row, and the two override
    gestures; `PaletteStandInsFor` is what makes the swap live — strokes name
    the base palette, and the registry never answers a named palette from a
    different one (Q30), so the variant's copy must be registered *as* the
    base id rather than merely existing.
  - Viewing is view state, like the playhead: never serialized, never dirties
    a document, and a switch is one deliberate gesture so the full repaint it
    triggers is bounded by that gesture, not by pointer events.
- [x] Variant attachments — armor drawn once, riding an anchor through every animation (Q143) `evidence: VariantAttachment, VariantAttachments, AttachmentOverlay, VariantAttachmentTests, VariantAttachmentViewTests, AnAttachmentRidesTheAnchorByName, TwoDocumentsWithDifferentAnchorIdsBothDressTheSameAttachment, FollowingTheAimTurnsThePlacementAndItsOffset, AbsenceIsTheOffState, AVariantThatWearsNothingWritesNoAttachmentsKey, TheCanvasShowsTheArmorOnlyWhileTheVariantIsViewed, MovingTheAnchorMovesTheArmor, TheExportKeepsThePromiseTheCanvasMade, TheEditorDressesAndUndressesAVariant`
  - The assembly Q143 chose: anchors supply the animated position and, via
    Q144, the direction; a symbol supplies the drawn-once add-on with its
    own pivot; and the variant owns the attachment record — nullable, absent
    until used, bound to the anchor **by name** because ids are per document
    and names are already the sidecar's cross-document contract.
  - **Built (2026-08-22): the record, the resolution, the overlay pass and
    the editor.** Resolution produces <em>ephemeral</em> `SymbolPlacement`s —
    nothing touches a frame, so invariant 1 holds without a flatten step —
    and both compose paths (the view's publish, the exporter's frame
    composition) append the same overlay, so the sheet shows what the canvas
    showed, the palette's contract extended to worn pixels. The overlay
    bitmaps are cached per timeline index, keyed by editor revision so undo
    invalidates them for free, and retire through the frame cache's
    pin-aware deferral (B130's lesson). The per-document and per-frame
    override levels collapsed onto the anchor itself: nudge, aim or clear it
    per drawing and the armor follows — no second store.
  - **Open on purpose, recorded in Q143:** draw order (the overlay sits
    above the whole stack; behind-a-limb needs an attachment to name its
    layer) and which folder's armor dresses a multi-folder export (the
    resolver is ambient per active document, like the palettes).
- [~] Scene management `evidence: ProjectScene, AddScene, AddShot, SceneDuration, AFilmSurvivesASaveAndReload, AShotIsADocumentLikeAnyOther, ShotsAreIndentedUnderTheirScene`
- [x] Project conversion (Illustration → Animation → Game) with no artwork recreated `evidence: Convert, ConversionReport, ConvertingRecreatesNoArtwork, ConvertingAwayFromAnimationKeepsTheCameraAndTheScenes, ConvertingDoesNotRearrangeTheScreenByItself`
- [x] Workspace layouts, decoupled from project type `evidence: WorkspaceStore, WorkspaceViewModel, EveryProjectTypeHasABuiltInWorkspace, TakingAProjectTypesDefaultsSwitchesWorkspace`
- [x] Dockable panels `evidence: DockLayout, DockStrip, DockZones, PanelsLandInTheStripTheLayoutNames, AnEmptyEdgeCollapsesAndAFilledOneOpens`
- [x] Rearrange the tabs in a group — drag a tab along its own header, and land a joining panel at the position aimed at rather than at the end `evidence: MoveTabTo, TabRects, DockerTabRearrangeTests, ATabMovesToThePositionItWasDroppedAt, ATabDroppedInItsOwnHeaderMovesToThatPosition, TheCaretMarksTheGapUnderThePointerRatherThanTheCorrectedIndex, ATabDraggedToTheEndOfItsOwnHeaderEndsUpThere, ALayoutSavedBeforeTabsCouldBeRearrangedKeepsItsOrder`
  - **A group had an order nobody chose**: the members of a slot came back sorted by `DockPanelId`, so the strip read Colour | Palette | Gradient | Channels because that is the order the enum declares them in, and no gesture could change it. `DockPlacement.TabOrder` is the position, ties break on the id, and a layout written before the field existed has every placement at zero — which is exactly the old sort, so nothing an artist saved moves.
  - **The drop resolution needed geometry it did not have.** A tab is as wide as its title, so "which gap is the pointer between" cannot be derived from a count; `PanelSlot.Tabs` carries the measured rectangles the way `HeaderHeight` already carried the measured band, and a header that reports none falls back to the join it always meant.
  - **Two indices that are not the same number**, and the reason is written into `JoinAtTab`: the caret is drawn between the tabs as they stand, while the index handed to the layout describes the strip with the dragged tab already lifted out of it. Drawing the corrected one puts the mark a whole tab from the pointer; applying the visual one walks a tab forward a place on every drag that lets go where it started.
  - **Its own header is the one place a panel can drop on itself.** Everywhere else that resolves to no target, and no target means the release floats the panel — so the rearrangement had to be a real target or a drag that let go over its own name would tear the panel out of the window.
- [~] Project browser — characters and their animations `evidence: ProjectViewModel, ProjectRow, TheDockerListsCharactersWithTheirAnimationsUnderThem`
- [x] Reach the files from the browser — reveal, open externally, duplicate `evidence: FileReveal, FileRevealTests, EveryRowKnowsWhereItIsOnDisk, DuplicatingAnAnimationCopiesItsArtIntoTheSameCharacter`
- [x] Movable canvas overlay bars — view controls and view shortcuts, on any edge `evidence: CanvasOverlayLayout, CanvasOverlayBar, CanvasOverlayGeometryTests, CanvasOverlayTests`
- [x] Shared palette across a character's animations `evidence: TwoAnimationsUnderOneCharacterPaintFromOnePalette, RefreshProjectResources`
- [x] Standalone export from inside a project `evidence: ProjectFlattenTests, AFlattenedDocumentRendersIdenticallyWithTheProjectGone`
- [x] Open an existing loose document without a project `evidence: TheAppOpensWithNoProject, WithNoProjectADocumentSavesAndLoadsExactlyAsBefore`
- [x] Per-workspace panel sets (Illustration / Animation / Game) `evidence: TheBuiltInsDifferFromEachOther, OnlySavedWorkspacesOfferABin`
- [x] Auto save - configurable in time if a file is already present. `evidence: AppSettings, AutosaveService, TheDefaultIsEveryMinuteToTheRecoveryCopyOnly, ZeroTurnsAutosaveOff`
- [x] Quick options bar — pinned Size and Opacity, the active tool's icon, per-tool quick controls; the transform session moves to its own Tool options page (Q70, stage 1) `evidence: QuickOptionsBarTests, MakesSizedMarks, TheOverflowCarriesNoSecondCopyOfSizeOrOpacity, BeginningATransformOpensTheToolOptionsDocker`
- [x] Quick bar contents per workspace — the bar is the workspace's smart bar, not the tool's (Q74): a registry of offerable options, per-workspace built-in defaults (Animation gets the transport, Illustration the marquee), and the ⋮ flyout beside the picker choosing contents saved with the workspace; Size and Opacity are not on offer at all `evidence: QuickBarCatalog, QuickBarWorkspaceTests, SavingTheWorkspaceKeepsTheChoice, SizeAndOpacityAreNotOnOffer`
- [ ] Quick options bar rearrangement — drag options along, onto and off the bar rather than ticking them in a flyout (Q70, stage 2's remaining half; the registry it needed now exists) `evidence: QuickBarDragTests, DraggingAnOptionOffTheBarRemovesItFromTheWorkspace`

## Pillar 2 — Persistent, customizable onion skinning

Cheap to state, and the thing frame-by-frame animators evaluate a tool on
within five minutes.

- [x] Onion skin, on/off `evidence: OnionSkin, OnionPrevTint, OnionNextTint`
- [x] Adjustable depth `evidence: OnionDepth, OnionBefore, OnionAfter, OnionSkin`
- [x] Per-layer onion skin `evidence: OnionEnabled, PerLayerOnionTests`
- [x] Colour-coded onion skin `evidence: OnionPrevTint, OnionNextTint`
- [x] Persistent onion-skin settings across sessions `evidence: OnionSettings, AppSettings, OnionTests`
- [x] Onion skin from keyframes only `evidence: OnionSkin, OnionKeysOnly, OnionSkinTests`
- [x] Per-frame opacity falloff curve `evidence: OnionSkin, OnionFalloff, OnionSkinTests`
- [x] Light table mode `evidence: OnionMode, IsLightTable, OnionTests`
- [x] Draw-over mode `evidence: OnionSettings, OnionDrawOver, OnionTests`
- [x] Ghost poses `evidence: GhostFrames, HasGhostFrames, OnionTests`
- [x] Onion skin that survives a workspace switch `evidence: AppSettings, OnionSettings, OnionTests`

## Pillar 3 — Pose, expression and animation libraries

Reusable assets with real identity: edit the sword once, every animation
holding it updates.

**Designed; the record has landed.** `docs/DESIGN-symbols.md` settles the one
decision the whole pillar rests on — an asset is a *live symbol referenced by
id*, not a copy with a link back — and breaks it into seven commits. Most of
the items below are one step of that design rather than separate features: the
six libraries are `SymbolKind` values plus a browser filter and land together.

S1 and S2 are in: the record (`Symbol`, `SymbolPlacement`, `SymbolKind`, a
nullable `Placements` on `PaintedFrame`, project-scoped storage in
`assets/symbols.json`) and the render pass that resolves a placement by id and
draws it. A placement is rasterised in symbol space and transformed onto the
canvas, so the same symbol placed twice is the same mark twice rather than two
rolls of the same dice — the property invariant 2 makes non-negotiable.

S3 is in too: an exported document carries the symbols it places, so it renders
identically with the project gone. The design said flatten should dissolve a
placement into ordinary strokes; it cannot, because baking the transform into
coordinates re-rolls every `Hash01`-seeded dynamic and the export would be a
different drawing. `docs/DESIGN-symbols.md` records the change and the reason.

S4 is in: placing, moving, removing and breaking the link, each one undo step.
The Move tool grabs a placed symbol before it grabs the drawing — moving a
placement edits two numbers on the placement, while moving a drawing rewrites
stroke coordinates, and which you get is decided by what you grabbed.
Break-link is the one place in the application where a mark is allowed to
change, and it is written down where it happens.

**Corrected 2026-08-28: "S4 is in" was half true for two years of merges.**
`BreakLink` was built, correct and covered by four tests — and took a
`SymbolPlacement` that nothing in the application ever handed it. No button, no
`ShortcutMap` entry, no MCP verb, while `docs/manual/09-symbols.md` described it
as a thing an artist does. It is CLAUDE.md's registry rule failing in its usual
shape: the feature worked, nothing was red, and there was no address for it. Now
`BreakSelectedLink` carries the selection lookup, **Edit ▸ Break symbol link**
is the address, `edit.breakSymbolLink` is the registration, and
`TheBreakLinkCommandActsOnTheSelectedPlacement` asserts on the half that was
missing rather than the half that was never broken.

S5 is in: the browser panel, absent unless a project is open. Make a symbol
from the current drawing, find one by kind or by name or by tag, place it,
delete it. The six libraries below are the kind filter, which is why they all
tick together.

S6 and S7 are in, which closes the first cut. A symbol opens in a tab of its
own and is drawn on with the ordinary tools; every edit lands in the symbol as
it is made and bumps its version, so every placement of it is already showing
the new drawing when you switch back. Placements made before an edit are
reported as outdated and can be acknowledged — reported, never repaired, since
they already show the current symbol and the fix for an unwanted edit is to
undo it once in the symbol.

Dragging a tile onto the canvas drops it where the pointer is; Place is the
keyboard route and puts it in the middle. The staleness report and its
Acknowledge live in the panel's footer, absent when there is nothing to say.

The dependency graph closes the pillar. It answers "where is this placed?"
across every document in the project — which is the one question the folder
layout is arranged to avoid asking, so it is an explicit action and never runs
to refresh a panel. It is what makes deleting a symbol a decision: placements
are still left alone on purpose, because a delete that quietly edited forty
animations is worse than one that leaves marks that stop drawing and say so,
but that is only defensible now the artist is told how many there are. Scope is
symbol → document; symbol → symbol edges do not exist while nesting is refused,
and the note saying the graph "needs nesting first" was true only of the half
nobody was asking for.

**Designed 2026-08-28, not yet built: a symbol owns a layer stack (Q171).**
`docs/DESIGN-symbol-layers.md` settles it in seven steps — the record
(`Symbol.Layers` reusing the document's own `Layer`, not a narrower one), **the
compositor moving down into `Lightbox.Raster`**, the symbol render pass where
only `Render` changes, capture from a `LayerLink`, and a detach that rebuilds
the stack. Effort L.
  - **The move was costed by compiling it, not by reading it**, after the first
    draft guessed the opposite way round. All 2,856 lines of the compositor
    build inside Raster with three errors — two memory budgets that are already
    settable properties and one diagnostic note, none of them rendering.
    `GpuComposite` depends on SkiaSharp alone; the `MainViewModel` reference in
    `SceneRenderer` is a doc comment. It is in App because that is where it was
    written, not because it belongs there.
  - The payoff is wider than symbols: `SpriteSheetExporter.ComposeFrame` stops
    being a private reimplementation of compositing, and the canvas, the export
    and the symbol pass all reach one.
The owner went looking for the Animate workflow — a head is a lines layer, a
colour layer and two effect layers, and all four are one reusable thing — and
found three independent reasons it cannot be said. `MakeSymbolFromDrawing` takes
the active layer alone; `OpenSymbol` builds the editing tab with exactly one
layer; and `SyncEditedSymbol` does `Layers.SelectMany(l => l.Cels)`. That third
one is a **defect, not a limit**: nothing guards `AddLayer` in a symbol tab, so
a lines layer and a colour layer are folded into the frame list and become
frames 1 and 2 of an animation. Measured at `afba7436` — one frame and one
layer in, two frames out. Q171 takes the Flash model (a symbol carries its own
layers, detaching rebuilds the stack) over flattening-with-a-warning.

**The guard landed 2026-08-28, ahead of the stack.** Two halves, because
refusing the gesture is not the same as making the fold impossible: `AddLayer`
refuses in a symbol tab and says why, and `SyncEditedSymbol` reads **one layer
by id** — `DocumentTab.SymbolLayerId` — instead of `SelectMany`-ing over all of
them. The id rather than index 0 is the part worth keeping: a paste inserts at
the active index, so index 0 would have made the pasted work the symbol and the
artist's drawing an extra frame of it, which is the same corruption wearing a
different hat. A layer arriving by any other door is now reported and left out
rather than folded in. Both halves come out when the stack lands; the import
path is untouched and keeps its own test
(`ImportingACycleStillLandsItAcrossTheTimeline`), because expanding the
timeline is the other axis and is where the stack will eventually arrive.

Still open, and deliberately: symbols containing symbols. The two items below
are **not** unstarted — they are undecided, and the decision is in
`QUESTIONS.md` as Q11 and Q12. The short of it: the Animation library already
ships the reusable animation (a cycle symbol placed with a frame offset), so
whatever "reusable animation presets" meant, it was not that.

- [x] Shared symbols — the record (design S1–S2) `evidence: Symbol, SymbolPlacement, SymbolRegistry, SymbolRasterizer, SymbolRecordTests, SymbolRenderTests`
- [x] Linked assets — edit once, update everywhere (S6) `evidence: OpenSymbol, EditingASymbolChangesEveryPlacementOfIt, AnEditBumpsTheVersion`
- [x] Symbol editing (S6) `evidence: OpenSymbol, SymbolEditingTests, TheTabEditsTheSymbolsOwnFramesRatherThanCopies`
- [x] Asset versioning (S7) `evidence: OutdatedPlacements, StalePlacementReport, APlacementMadeBeforeAnEditIsReportedAsOutdated`
- [x] Asset browser (S5) `evidence: SymbolBrowserViewModel, SymbolBrowserTests, TheKindFilterIsWhatTheSixLibrariesAre`
- [x] Asset tagging (S5) `evidence: SymbolRow, SearchMatchesANameOrATag, TagsEditAsOneLine`
- [x] Smart asset search (S5) `evidence: SymbolBrowserViewModel, SearchMatchesANameOrATag, SearchAndKindNarrowTogether`
- [x] Pose library (S5 — a `SymbolKind` and a browser filter) `evidence: SymbolKind, TheKindFilterIsWhatTheSixLibrariesAre`
- [x] Expression library (S5) `evidence: SymbolKind, TheKindFilterIsWhatTheSixLibrariesAre`
- [x] Hand library (S5) `evidence: SymbolKind, TheKindFilterIsWhatTheSixLibrariesAre`
- [x] Face library (S5) `evidence: SymbolKind, TheKindFilterIsWhatTheSixLibrariesAre`
- [x] Prop library (S5) `evidence: SymbolKind, TheKindFilterIsWhatTheSixLibrariesAre`
- [x] FX library (S5) `evidence: SymbolKind, TheKindFilterIsWhatTheSixLibrariesAre`
- [x] Reusable backgrounds (S5) `evidence: SymbolKind, TheKindFilterIsWhatTheSixLibrariesAre`
- [x] Animation library (S5) `evidence: SymbolKind, ACycleOpensWithACelPerFrame, AnOffsetPlacementRunsTheSameCycleOutOfStep`
- [x] Dependency graph — where a symbol is placed, project-wide `evidence: SymbolGraph, SymbolUsage, SymbolUse, SymbolGraphTests, ASymbolKnowsHowManyPlacementsItHasAndWhere, APlacementLeftBehindByADeleteIsStillReported`
- [x] Global and project symbol scopes — a personal library beside the project's `evidence: SymbolScope, SymbolScopes, SymbolLibrary, SymbolScopeTests, SymbolLibraryTests, PlacingAGlobalSymbolCopiesItIntoTheProject, TheProjectRendersWithTheLibraryGone, PromotingCopiesUpAndKeepsTheId, EditingALibrarySymbolDoesNotReachIntoAProjectThatPlacedIt, DraggingALibrarySymbolOntoTheCanvasCopiesItToo`
  - **Not a gap in Pillar 3 — an extension of a finished one.** Nothing above promised it; it is the split `TipStore` already settled for brush tips, applied to symbols: global in `symbols.json` beside the brushes, project in `assets/symbols.json`, document in `Doc.Symbols` as the flattening target.
  - **The decision, and it costs something.** A symbol is a live link by id, but the project is what re-renders — a `.lbproj` that resolved a placement out of an application folder would lose art the moment it moved machine. So **placing a global symbol copies it into the project**, under the same id. The library is a source to choose from, never a live dependency.
  - The price is stated rather than discovered: editing a library symbol does **not** reach into projects that already placed it. Rolling a fix forward is a **pull** — *Update from library* — the same direction as the template pull, the project reaching out, never the library reaching in. Placements need no touching, because replacing what an id resolves to *is* the update.
  - **The folder axis beneath it (Q30 step 5) `evidence: SymbolScopes.VisibleTo, SymbolScopes.CanPlace, WithNothingDeclaredEverySymbolIsStillOffered, ASymbolDeclaredOnAFolderIsOfferedThereAndNotElsewhere, APublishedSymbolReachesTheWholeProject, ScopingNarrowsThePickerAndNeverWhatIsAlreadyDrawn, DeclaringASymbolNarrowsTheGridForOtherFolders`** — *Share a symbol here* on a folder, so the knight's props are the knight's. Last of Q30's five because it is the only one that **narrows**: the other four went from nothing to somewhere, and a symbol is project-wide already. A project that declares none keeps meaning *all of them*, which is what every project in existence means. It governs the picker and never the renderer — a placement resolves by id, so moving a document cannot change a pixel.
  - **Reachable with no project open.** The library is the artist's own, so it is there when they open the app to draw one picture; placing one into a loose document copies it into `Doc.Symbols`, which the registry already reads and which is the same key `ProjectIo.Flatten` writes — the self-containment rule one level down. The *project* tree stays gated, because without a project it has nothing at all to show, and making a project symbol still needs a project to put it in.
  - Adoption is keyed on the **id inside `PlaceSymbol`**, not per route. Doing it per route looked simpler and was wrong: drag-and-drop carries only an id, so a row-based version would have worked for the Place button and left a dragged library symbol failing to resolve — the harder bug to find, because the two routes are indistinguishable from the panel.
- [x] Timing presets — save an exposure pattern and apply it to a range of cels `evidence: TimingPreset, TimingPresetStore, ApplyTiming, TimingPresetTests, TimingPresetUiTests, ApplyingAPatternReExposesTheDrawingsThatAreThere, ThePatternDecidesTheLength_NotTheSelection, ItNeverCreatesOrDestroysADrawing, ApplyingToASelectedRangeRetimesTheWholeRange, ASavedPatternPersistsAndComesBackOnTheNextLaunch`
  - **Q11 answered (b).** *Reusable animation presets* is struck: the Animation library already is the reusable animation — a multi-frame symbol placed with a frame offset — and a roadmap item nothing can distinguish from a shipped one is the wish list the checkbox rules exist to prevent. What is genuinely absent is **timing**, which is the half a symbol cannot carry: a symbol carries drawings, this carries their spacing.
  - On 1s, on 2s, a slow-in of 1-1-2-3-4. Applied to a selected range, it **re-exposes the drawings that are already there** rather than making any — which is why it is nothing a symbol can express, and why it composes with everything else instead of competing.
  - **Landed whole.** `ExposureSheet.ApplyTiming` re-times a range, `TimingPreset.BuiltIns` carries the six patterns worth having on day one, `TimingPresetStore` keeps an artist's own beside their brushes, and the picker plus **Re-time** sit on the timeline bar with a cel-menu item beside them. One undo step.
  - **One correction made when the UI went on.** The engine first held the *selection's* length and dropped whatever no longer fit, which would have meant "on 2s" silently discarding half an artist's drawings. **The pattern decides the length**: twelve drawings on 2s occupy twenty-four cels and the row grows. Thinning a range on purpose is `ReduceToStep`, which is a separate command precisely because it is destructive. `ThePatternDecidesTheLength_NotTheSelection` holds the line.

- [x] Bones and spline deformers — a full 2D rig that re-draws marks instead of warping them `evidence: Armature, Bone, PoseTrack, BoneBinding, ArmatureTests, SkinningTests, PosingTheRigMovesTheStrokePointsNotTheRecord, ADabsDynamicsSeedFromItsBindPoseSoAPoseCannotBoil, AFixedIterationIkSolveIsBitIdenticalOnReload, BakingAPoseWritesOrdinaryStrokes, IkChain, BoneConstraint, SplineChain, LayerLink, RigIndex, Corrective, CorrectiveOps, CorrectiveTests, CorrectiveToolTests, TheFixEasesInWithTheAngle, ADragInPosedSpaceComesBackAsTheRestOffsetThatProducesIt, LayerLinkTests, LayerLinkToolTests, LayerRigRenderTests, ADrawingOnARiggedLayerRendersPosedWithNoWeightsOnIt, ABakedDrawingIsNotSwungAgainByTheLayerItSitsOn, ABoneNamedOnOneLayerReachesEveryLayerInTheLink, ALinkThatDoesNotCarryBonesRigsNothing, InverseKinematicsTests, IkToolTests, BoneConstraintTests, ConstraintToolTests, SplineChainTests, SplineToolTests, BonesFollowTheCurveAndKeepTheirOwnLength, AnAimConstraintTurnsTheBoneToPointAtItsTarget, BindModeNeverSeesConstraints, TheChainReachesATargetItCanReach, BindModeNeverSeesIk, APoseDragOnADrivenBoneMovesTheTargetInsteadOfDoingNothing, PoseGrabTests, PoseToDrawingTests, PoseDrag, PoseMoveBy, InsertDrawingFromPose, InsertDrawingFromPoseAt, ACarriedPoseIsAKeyAtThePlayheadAndLeavesTheRestAlone, KeepingAPoseAsADrawingBreaksTheHold, TheSheetsRightClickAimsAtTheCelThatWasClicked, PoseKeyEditTests, PoseTrackRowTests, MovePoseKey, DeletePoseKey, MoveBoneKey, TheArmatureRowMarksEveryFrameAnyBoneIsKeyedOn, DraggingABonesKeyMovesOnlyThatBone`
  - **Designed 2026-08-13, not scheduled: `docs/DESIGN-bones.md`.** The feature bar (hierarchy, FK, IK, constraints, spline chains, skinning, angle-driven correctives, secondary motion, export), the record shapes, six shippable phases, and the licensing wall — algorithms from papers, never Spine's or Live2D's code or formats.
  - **The core is Lightbox-native and no big package can copy it:** bones deform *stroke control points* and `BrushEngine` re-stamps the mark — a bent arm is re-drawn, not rubber-sheeted. The one trap is invariant 7's shape, and its rule is in the doc: dynamics seed from bind-pose coordinates, the transform moves only placement, so a rigged character cannot boil.
  - **Owner's decisions, taken with the design:** live posing and bake are both first-class (re-baking per tweak would concede the iteration speed puppet tools win on); rig export is own JSON + Godot + DragonBones converters, chosen over the cheaper own+Godot knowing it is more surface.
  - Phase 1 (armature, FK posing, no deformation — M) already earns its keep: anchors ride bones, and the armature gives the inbetweener the intent it lacks. Total XL.
  - **Phase 1's record layer landed 2026-08-13:** `Doc.Armature` and `Scene.PoseTrack` (optional-absent, the camera's rule — `AnUnriggedDocumentWritesNoRigKeys` is the guard), the FK solve and sparse-key pose interpolation in `ArmatureOps` (`TheSolveIsBitIdenticalAcrossAReload` pins determinism), and image resize scaling the rig with everything else. Still to come in phase 1: the bone tool, posing UI, armature onion-skin, anchors riding bones, inbetweener conditioning (G12 applies to that last one).
    - **Armature onion-skin landed 2026-08-16**, the last of phase 1's visual items: in Pose mode with onion skin on, the skeleton at the neighbouring pose *keys* draws as tinted outline ghosts — keys rather than frames, because a ghost one frame away on an interpolated track is a near-copy that says nothing. The onion bar's own switch and depths drive it, the tints are the drawing ghosts' (warm behind, cool ahead), and a ghost is never hit by a press. Rides `BoneChromes` as a `BoneGhost` field so the ratcheted draw-op gained no lines. `BoneChromeVisualTests` guards the ghosts, the gating, the hit-transparency and the tints.
  - **Phase 2's core landed 2026-08-13:** `Stroke.Weights` (`BoneBinding` — null point-weights is the coarse 100% assignment, so cutout rigs stay one small key) and `Stroke.RestPoints`, LBS on stroke control points with rest-remainder blending, distance-falloff auto-bind (BBW can replace it behind the same call), bake-to-ordinary-strokes, and the no-boil mechanism itself: a posed stroke's dabs are walked on the bind-pose path and stamped on the posed one, so `ADabsDynamicsSeedFromItsBindPoseSoAPoseCannotBoil` and `AnIdentityPoseRendersByteForByteAsTheUnposedStroke` both hold. Live and bake share one construction (`PoseStroke`), which is what makes them bit-identical. Still to come in phase 2: weight painting UI (heat overlay, weight brush, X-symmetry), the live rigged render path in the app, and the bind/pose gestures.
  - **The live rigged render and the weight brush's arithmetic landed 2026-08-14.** One funnel: `FrameBitmapCache.PoseResolver` poses bound frames on the miss path (`Skinning.PoseFrameForRender` — the same construction bake writes, so `TheLiveRenderIsByteForByteTheBakedRender` holds through the real cache), keyed per timeline position the way placed symbols already are; the scene view, onion ghosts (now posed at their own position via `OnionGhost.Index`), playback and all four exporters ride it. Bound frames refuse the tile path (`TileFallbackReason.BoundStrokes`) and the prewarmer skips them — a detached render cannot pose. `WeightPaint` carries the brush rules (Blender's normalization with per-bone locks, rest-hold deficits, prune-to-absent, name-paired mirroring across the pair's own axis) as pure record edits, `WeightPaintTests` guarded.
  - **Painting weights under a live pose landed 2026-08-16** (Q101, sequenced after the rest-pose loop by Q81 decision 4). The brush hit-tests and the heat dots sit at the posed positions at the playhead — `Skinning.PoseControlPoints`, the render's own construction, correctives in force — while the weights stay rest-pose facts on the same indices; the mirrored dab rides its pair's own rigid delta out (`WeightPaint.MirrorPosed`, exact where the painted bone owns the dab's neighbourhood). `LivePoseWeightTests` guards the surface end to end and the scrub refresh; the rest of `WeightPaintTests` pins the arithmetic, `ADabHitsThePointWhereItIsDrawnNotWhereItRests` the reason it exists.
  - **The pose-drag live preview landed 2026-08-15 (Q81 decision 5), riding B219's gesture preview.** The provisional pose reaches the same funnel — `PoseFrameForRender` takes a `poseOverride`, so the drag's pixels are the release's by construction (`ThePreviewPoseRendersTheReleasePixels`). Region-bounded per invariant 6: the publish patches old ∪ new posed reach, measured off the posed control points (`PosedReach`), and the request rides the same publish dam every live preview does. The degrade reads the badge as written: `BrushCost.Expressive` strokes ghost as thin centrelines during the drag (`GhostCentreline`, `AnExpensiveBrushGhostsDuringTheDragAndTheRecordNeverLearns`) and land exactly on release; `Textured` strokes render exactly throughout, because their pen-lift passes are stroke-bounded rather than region-scaled. `APoseDragMovesThePublishedPixelsBeforeRelease` drives the whole path through the view model, cancel included.
  - **The weight brush's gesture landed 2026-08-14**: Bone tool + Ctrl+Shift+K, dabs stream live onto the record for immediate heat feedback, the whole stroke lands as ONE PerformDelta step (idempotent apply — the record already holds the after-state), pressure drives strength through the same hand as every brush, and X-symmetry paints the named pair across its own axis. `AWeightStrokeIsOneUndoStepAndMirrorsAcrossTheNamedPair` guards the loop end to end.
  - **Layer rig links landed 2026-08-14** (Q90), from the owner's question "how do bones know what to move". The honest answer was per stroke, on the current frame, by hand — fine for one illustration, four hundred manual binds for a two-layer character over two hundred frames, and nothing at all for a stroke drawn after rigging. `LayerLink` is a set of layers declared to be **one drawing**, and what travels it is **opt-in per property** — a link can carry several, one, or none, so linking to rig a character never starts sharing alpha lock behind the artist's back. `Layer.BoneId` names the bone (empty string = the whole skeleton, three states of one question rather than a bool beside a string); `Scene.RiggedBoneOf` resolves own-before-link, so a link is a default and never overwrites authored work. Because a link is a property of the layer structure rather than of a drawing, it holds on every frame for free — which is the half that closes the hole. **Adjacency is the gesture, never the addressing**: Ctrl+Shift/Ctrl+Alt/Shift+right-click read the neighbour once and write a membership by id, so reordering afterwards cannot retarget anything. Every link gesture is on the **right** button, which is the correction that produced the mapping — the first spec put the menu on Ctrl+click, the docker's multi-layer toggle, and moving off Ctrl only helps if Shift is cleared too. The docker draws a **bracket** down a linked run, decided by a row's neighbours rather than the link's membership order, so a non-adjacent member says `Detached` instead of drawing a line past a row that is not in the link. `LayerLinkTests` and `LayerLinkToolTests` guard the record and the surface; `LinkBracket.axaml` keeps the chrome off the monolith budget, which rose by exactly the 26 menu lines that had nowhere else to go. **It reaches pixels as of 2026-08-14**, on its own branch as promised. `RigIndex` is the piece that made it reviewable: "does the rig move this drawing?" was `Frame.HasBoundStrokes`, a property of the frame, and a layer binding makes it a property of the *layer* — which none of the five gates has in its hand. Threading a `Layer` through every `Get` in the application would have been a diff nobody could review; a frame-id → layer map built once and handed to the cache, the prewarmer and the resolver together is one object and one ordering rule. It is **built explicitly, never lazily** (a self-refreshing map inside the render path is a cache with an invalidation problem, and a stale answer there is wrong pixels somebody exports), and an unrigged document gets the shared `RigIndex.Empty`, which answers from the frame alone — so the funnel behaves exactly as it did before any of this existed. `PoseStroke` gained a `fallback` it **reads and never writes**, which is what makes linking retroactive and free of a per-stroke key on two hundred frames. **One real defect the tests caught before review could**: a baked stroke has no weights either, so on a rigged layer it looked exactly like an unbaked one and was swung again on the next render — the drawing walking further from the rig every time somebody froze it. `Stroke.RestPoints` is set by `PoseStroke` and by nothing else, so "has a rest path" and "has been posed" are the same statement, and that is the guard. `LayerRigRenderTests` covers the index, the posed render, the per-position key, the untouched record, live-equals-baked, the double-bake trap, whole-skeleton binding and painted weights still winning.
  - **Phase 4 landed 2026-08-16: angle-driven correctives** (Q100), Moho's "smart bones" done stroke-native. Linear-blend skinning collapses the inside of a sharply bent joint, and no weight painting fixes it because the right shape at 120° is not a blend of anything — so the artist draws it. `Frame.Correctives` is nullable-absent; a corrective names a driver bone and carries **stops on a ramp**, each holding per-stroke point offsets. Two properties are load-bearing. The offsets are **rest space, applied before skinning**, so a fix composes with IK, splines, constraints and the pose for nothing and none of them know correctives exist — posed-space offsets would have been trivial and wrong the moment a parent moved. And **rest is an implicit stop at 0°**, materialised into the list rather than special-cased, so an unbent joint corrects nothing without a stop full of zeroes in the file; written as branches it needed four of them and each was a place to be subtly wrong about which side of zero an angle fell. Authoring is **bake, edit, diff**: entering capture replaces the drawing with its posed self so the pen, transform and point editing all work on the shape that is wrong, and capture converts the difference back to rest space through `RestOffsetsFor` — which **probes** the blend's linear part with two unit offsets rather than reimplementing it, the same reason live and bake share one construction. A round-trip test is the guard: offsets fed back through the pose land the point exactly where it was dragged. `CorrectiveTests` (20) and `CorrectiveToolTests` (10) cover the ramp, the rest-space claim, the capture and the ways out of it. **One defect found on the way, and a guard for its whole class**: `Frame` is the one document type that does not go through reflection — `FrameConverter` names its properties — so `Correctives` compiled, worked all session and was gone on reload. `FrameConverterCoverageTests` sweeps every settable property on `Frame` through a save and load, and was mutation-checked to prove it bites.
  - **Phase 5 landed 2026-08-16: deterministic secondary motion.** `Bone.Jiggle` (nullable-absent — `AnUnjiggledRigWritesNoJiggleKeyAndTheEffectivePoseIsTheBasePose`) carries two artist-facing numbers, catch-up and settle; `ArmatureOps.EffectivePoseAt` folds the spring into the pose every *render* uses — one fixed integration step per frame index from the first key, bones in authoring order, state seeded settled from the solved geometry, so the same document renders the same swing forever (`TheSpringIsBitIdenticalAcrossEvaluationsAndAReload`). The render/authoring split is the design's line drawn again: `PoseFrameForRender`, the chrome, the heat, the dab, correctives capture and bake all see the spring; keys and drag measurements never do, or a key would bake the lag in and replay it on top of itself. Overshoot is asserted, not hoped for (`LowDampingOvershootsLikeATailShould`) — a filter that only lags reads as drag, not life. `BoneJiggleTests` and `BoneJiggleAppTests` guard it; a solver-driven bone's swing is overwritten by the very next solve, the same rule an FK drag on one has, and the manual says so.
  - **Phase 6 landed 2026-08-16: rig export — the last phase.** `RigExport` (Core) is the owner-decided *own JSON source of truth*: the skeleton and jiggle settings, the authored keys verbatim, and a **baked block** — per-frame solved bone-local transforms with IK, constraints, splines and springs folded in, because no engine can be asked to replay Lightbox's solvers and determinism is what makes baking honest. `TheBakedBlockReplaysTheSolveExactly` composes the exported locals down the hierarchy and matches the solver to six decimals. `DragonBonesConvert` renders the schema as a DragonBones 5.5 skeleton file (BSD — never Spine's or Live2D's formats, the licensing wall) writing only the format's stable core: bones and sampled motion, no slots or skins, because the design doc's own warning is that DragonBones importers vary in quality. The Godot export gained the rig beside the sheet plus `lightbox_import_rig.gd`, which builds the `Skeleton2D` and its `AnimationPlayer` through Godot's own API — the corrected `.tres` rule applied to rigs. `ExportTarget.DragonBones` is the reachable surface; a document with no rig writes nothing on Godot and is refused with a sentence on DragonBones. `RigExportTests`, `DragonBonesConvertTests`, `RigExportAppTests` guard it.
  - **Spline chains landed 2026-08-14, finishing phase 3.** `Armature.Splines` is nullable-absent; a chain names a run of bones and the handle bones the curve passes through. **Catmull-Rom, not Bézier** — it goes through every handle, which is what an artist expects of something they placed, where a Bézier's interior points only pull. The load-bearing property is that a spline **bends** a run and never **stretches** it: bones are stepped along the curve by arc length, each keeping its own length, because a scaled bone would scale the drawing bound to it and re-roll every dab dynamic off the moved coordinates (invariant 7 restated for the rig). Arc length comes off a **fixed** 32 samples per span for `IkIterations`' reason. Past the end of the curve the run trails straight along the last tangent rather than knotting at the final point. Handles are bones for the same Q86 reason IK's target is — a tail that whips is three handles keyed over four frames and nothing new had to learn to be keyed. Evaluation order is now FK → IK → splines → constraints, so constraints still win. On the surface: **Add spline** lays three handles along the run as it already stands, so pressing it moves nothing; handles draw amber with IK's; a pose drag on a curve-laid bone moves the *nearest* handle rather than being a dead gesture, and deleting one handle thins the curve instead of killing it. `SplineChainTests` and `SplineToolTests` guard the solve and the surface.
  - **Constraints landed 2026-08-14**, the second slice of phase 3. `Armature.Constraints` is nullable-absent, and so are a constraint's offset and influence — the ordinary one (all the way on, no offset) writes neither key. **Three narrow kinds rather than one wide transform-copy**: aim, copy-rotation, copy-position, each doing a thing an artist can name, stacked when the combination is wanted. The wide form is one constraint with a row of tick boxes most of which sit at false in every file that ever used it. Evaluation order is FK → IK → constraints, each in list order, so a later constraint sees an earlier one and a constraint can override a bone IK just placed; a circle (following yourself or your own descendant) is refused rather than iterated, as a circular chain is. On the surface, **picking the target is the add** — one gesture instead of three for the constraint wanted nine times in ten — and the target list omits the bones the solve would refuse, so no choice in it produces a constraint that does nothing. Deleting a bone now takes the chains and constraints that named it, which the IK slice should have done: the solve already refused them, so what was left was worse than misbehaviour — an entry the panel lists and the artist adjusts with nothing ever happening. **One real fix fell out of it**: a pose drag measured its key against the *constrained* angle rather than against FK, so at half strength a drag straight right landed the bone straight left. `BoneConstraintTests` and `ConstraintToolTests` guard the solve and the surface. Still to come in phase 3: spline chains.
  - **IK landed 2026-08-14** under Q86, sliced alone (aim/copy constraints and spline chains are their own branches). `Armature.Chains` is nullable-absent — a rig with no IK writes no `chains` key — and one chain names its tip bone, how many bones up it may turn, a **target bone** and an optional pole. The target being a bone rather than an xy pair is the decision that pays: it keys on the ordinary pose track, parents to a prop, and drags with the gesture that already exists, where a bare point would have needed all three built again. `ArmatureOps.Solve` runs FABRIK at a **fixed** twenty passes rather than converging to a tolerance, because a tolerance loop runs a different number of passes on inputs differing in the last bit and a reload could then disagree with itself. **A null pose is the rest and never runs IK** — that one line is what keeps bind-mode drags from fighting the solver, and it is the bind/pose line rather than a flag to remember, since every posed caller passes a dictionary. Q86's other half landed with it: `Bone.Connected` glues an extruded child to its parent's tip, so re-proportioning a limb drags the chain instead of leaving a gap, and any bind drag on the joint unglues it rather than being silently ignored. On the surface: one **Add IK** button makes chain, handle and selection together, the handle draws amber so it is not mistaken for a bone, and a pose drag on any chain bone moves the handle — because an FK key on a solver-driven bone is a gesture that visibly does nothing, which is the exact defect this tool has been reported for three times. `InverseKinematicsTests` and `IkToolTests` guard the solve and the surface respectively. Still to come in phase 3: aim and transform-copy constraints, and spline chains.
  - **Posing learnt to move and to commit, 2026-08-18** (Q119, Q120), from the owner using the rig as a construction guide for a run cycle. Two things were missing and neither was visible from inside the code. **Every pose drag rotated**, whatever it had hold of, so there was no gesture that translated an ordinary bone — a character could be posed and never moved. Pose mode now reads a grab exactly as bind mode does (tip aims, shaft and joint carry), which makes *move the whole skeleton* a drag on the root and nothing more, because children ride their parent through FK; `CanvasCursor.ForBone` lost a branch, since with the modes agreeing it had nothing left to say about the mode. IK and spline handles keep their place-at-the-pointer answer for every grab: the bone a drag moves is often not the bone under the pointer, so a delta has nothing to be a delta of. And **posing on a hold authored no drawing** — correctly, that is what makes trying a pose free, but it left the guide workflow with no way to say *and this one is a drawing*. `InsertDrawingFromPose` breaks the hold at the playhead and keeps what is on screen, one editor step for the key and the bake together: bound art arrives baked into its posed position, a bone guide over hand-drawn art arrives copied through to redraw over. One command rather than two, because it commits what is on screen rather than a category — the second case is `BakeFrame` returning zero and changing nothing. **Three surfaces, one command** (the owner asked for the sheet on 2026-08-18): the bone options acts on the playhead, the X-sheet's right-click acts on the cel that was clicked and then goes there — the pose baked in has to be the pose at the frame under the cursor, so it is a cel-targeted overload rather than a playhead move — and `armature.insertPoseDrawing` carries no default gesture so a cycle can be worked with it under a key. The menu item is absent rather than disabled without a rig, the camera's rule. Q121 recorded the third part of the report and answered it by *not* building it: the Transform tool still moves strokes rather than bones, because the complaint dissolved once the carry existed.
  - **The pose track became visible and editable, 2026-08-18**, from the owner asking for the armature to be keyframeable in the timeline. It already was — poses have auto-keyed at the playhead since the Bone tool landed, and `PoseKey.Bones` has always been per bone — but **nothing on screen ever said so**, which is how a key that failed to survive a reload stayed invisible until somebody scrubbed onto its frame and found the rig at rest. The track timeline now grows an **Armature** row marking every frame any bone is keyed on, expandable to one row per bone (off by default: a twenty-bone character would otherwise cost twenty rows). Keys drag to retime and right-click to remove, on the summary row for the whole pose and on a bone's row for that bone alone; `ArmatureOps.MoveKey`/`MoveBoneKey`/`RemoveKey`/`RemoveBoneKey` are the record edits, in Core because retiming a key is a pure edit of the track and wants testing without a window. A key that gains a bone is **seeded from the interpolated pose** first, `KeyPose`'s rule one operation along — a bone absent from a key is at rest on it, so a key holding one bone would snap every other bone to rest — and a key left holding nothing is removed rather than kept as a marker nothing can read. `TrackRow` gained a `TrackKind` in place of its `IsCamera` bool, Q90's three-states-of-one-question; only the painter and the host's routing branch on it.
  - **The Bone tool landed 2026-08-14** under Q81: one tool in the palette (K — the rig's always-reachable door; its first drag creates the armature), the mode is the tool, posing toggles with Shift+K and keys at the playhead (a new key copies the interpolated pose first, so keying one bone cannot snap its neighbours). `ArmatureOverlay`/`ArmatureOverlayPainter` follow `RigOverlay`'s discipline — pure hit-testing and bitmap-testable chrome, heat view included — and `ArmatureToolTests` covers hits, gestures, auto-key, undo and pixels. Coarse assignment and auto-bind ride the stroke selection. The overlay surfaces went into `CanvasControl.Overlays.cs`/`MainWindow.Overlays.cs` and the ratchet budgets came DOWN for both files; the toolbar button raised the axaml budget by its exact twelve lines. Manual section 14 documents it, weight brush marked *Planned*.
- [x] Stepped bone timing — a held key and pose on 2s, opt-in (Q152) `evidence: SampleFrame, PoseSteppingTests, AHoldKeyFreezesThePoseUntilTheNextKey, SteppingSamplesTheRenderPoseAndLeavesAuthoringFluid, AJigglingRigHoldsDeadStillInsideAStep, AnUnsteppedTrackWritesNoStepKeyAndAHoldRoundTrips, SetPoseKeyEase, SetPoseStep, SettingAPoseKeysEaseIsOneUndoStep, SteppingTheTrackIsUndoableAndAbsentWhenCleared, ClearingTheStepRemovesATrackNothingElseAuthored`
  - **Landed 2026-08-23**, the owner's ask: fluid auto-tween stays the default, and drawn timing is opt-in at two grains — per key (`Easing.Hold`, freeze until the next key, on the same ease menu the camera's keys use) and per track (`PoseTrack.Step`, the *render* pose sampled on Ns and held between, anchored at frame 0 so held poses sit on the exposure sheet's grid beside drawings on 2s).
  - **The step quantizes `EffectivePoseAt` and never `PoseAt`** — the authoring/render split jiggle already made. Keys still seed from and drags still measure against the fluid tween, retiming the step re-poses nothing, and the jiggle walk holds dead-still inside a step instead of wobbling over a held pose. Bake and export ride `EffectivePoseAt`, so a stepped rig bakes to held drawings ready for the timing presets.
  - **Optional means absent, both halves:** an unstepped track writes no `step` key, and clearing the step removes a track nothing else authored.
- [x] Animation templates — a document in the project marked as a template `evidence: IsTemplate, TemplateId, Templates, NewFromTemplate, TemplateTests, TemplateUiTests, ANewDocumentFromATemplateIsACopyNotALink, EditingATemplateLeavesEarlierCopiesAlone, AnOrdinaryDocumentCarriesNoTemplateKeys, ALayerTheArtistHasDrawnOnIsSkippedUnlessTicked, APullNeverTouchesTheExposureSheet`
  - **Q12 answered (a), with the design written out in `docs/DESIGN-templates.md`.** A template is an ordinary animation with a flag, not a new kind of file — so an artist can make one out of work they have already done, which is where real templates come from, and editing one is just drawing.
  - **The rule that makes it safe: a template is copied, never referenced.** That is the whole difference from a symbol, which *is* a live link. If templates were references, editing one would silently rewrite every animation ever started from it — the opposite of what a starting point means.
  - **Update from template is the pull, and the direction is the safety property.** The copy stays static; the document reaches out to the template when the artist says so, one document at a time, as one undoable step. It brings new layers, layer properties matched by id, guides, frame rate and an absent camera — never drawings on a layer that exists, and never the exposure sheet. `Doc.TemplateId` is the only link in the design and it points **document → template**, so a template has no idea who copied it and deleting one cannot break anything.
  - **The load-bearing clause, and it has a real signal rather than a guess.** *Skipped for any layer you have drawn on* compares **stroke ids**, so it catches the artist who deleted one stroke and drew another, where a count would call that unchanged. Imported pixels with no stroke provenance count as work too.

## Pillar 4 — Animation-aware drawing tools

Tools that know they are operating on a sequence, not a picture. This is the
pillar the determinism invariant exists to make possible: an effect that
varies between similar strokes is fine on one image and boils at 12 fps.

Everything here is arithmetic — geometry and timing, no model. The AI half of
this pillar (inbetweening, inking, reading the subject) lives under
**AI assistance** below, because its cost and its review process are shared with
every other AI feature and are only legible together.

- [x] Deterministic marks across frames (no boiling) `evidence: OutputScaleTests, BrushDynamicsTests, ScalingTheCoordinatesInstead_ProducesADifferentMark`
- [x] Batch frame editing `evidence: CelRangeTests, CelRangeSelectionTests`
- [?] Batch transform across frames
- [x] Frame hold tools `evidence: ExposureSheet, ExposureEditingTests, RetimingTests, ExposureStep`
- [?] Animation-aware brushes
  - **Scoped (Q80): a brush whose mark still makes sense when there are two
    hundred of them, played at 12 fps, some drawn by the inbetweener.** Not a
    brush category — every brush passes through this, with project type
    setting defaults, never availability. The floor is already built: `Hash01`
    seeding is what stops similar strokes on neighbouring frames shimmering,
    and it is the `[x]` deterministic-marks item above. This item is the four
    deltas on top of that floor, each per stroke (invariant 4) and arithmetic
    (no model — the AI reading of a neighbouring drawing is inking, under AI
    assistance):
  - **Grain anchoring as a per-stroke choice.** Canvas-locked paper texture is
    right for a painting and wrong for a sequence, where a moving drawing
    swims through fixed grain like a screen door; mark-locked grain travels
    with the drawing. The brush carries the anchoring, the project type
    defaults it — canvas for illustration, mark for a cycle.
  - **Inbetweenable dynamics.** The inbetweener interpolates geometry today;
    the brush is animation-aware when pressure profile, taper and flow along
    the stroke interpolate too, so a generated inbetween reads as the same
    tool making the middle drawing rather than an interpolated skeleton with
    re-rolled character.
  - **Authored boil, held holds (Q80: in scope, opt-in).** Geometry seeding
    makes a hold dead-still for free, which is the right default — and removes
    the choice. Deliberate line boil is an off-by-default per-stroke effect
    with an authored per-frame phase stored in the record: deterministic
    (invariant 2 holds), absent from the file unless used, so a hold can
    breathe on 2s because the artist asked. First effect whose seed varies by
    frame — the cost accepted in Q80 is that the seeding story grows a frame
    dimension and needs its own re-render and hold tests.
  - **Frame-context response, arithmetically.** Dynamics that read the
    previous frame's stroke record as geometry — a cleanup brush weighting
    toward the prior drawing's nearby line, or a mark that dries out as its
    stroke moves further from its predecessor.
  - Sequence-scale cost is the review stance over all four: `BrushCostOf`
    badges are read against replay across a whole sequence, not one image.
- [?] Draw once, reuse across animations
- [~] Fluid effects elements — fire, smoke and water as drawn line and fill `evidence: FluidSolver, MarchingSquares, FieldTracer, SimBaker, SimBakeOps, SimElement, LineTreatment, EffectParam, FluidEffectsViewModel, FluidEffectsWindow, EffectFieldRow, FluidSolverTests, MarchingSquaresTests, FieldTracerTests, SimBakerTests, SimElementTests, FluidEffectsViewModelTests, FluidEffectsWindowTests, A_New_Element_Arrives_Already_Burning, Every_Solver_Parameter_Has_A_Row, A_Style_Edit_Previews_And_A_Fluid_Edit_Waits, The_Fingerprint_Notices_Physics_And_Ignores_Style, Opening_The_Window_Is_A_Registered_Command, Smoke_Rises, Smoke_Arrives_Lit_And_Fire_Does_Not, AddExpansion, Expansion_Grows_A_Blob_And_Thins_It, Expansion_Makes_No_Matter, A_Radial_Push_Hollows_The_Middle_Where_Expansion_Keeps_It, A_Burst_Expands_The_Front, A_Timed_Emitter_Stops_Feeding, SimGroup, SimGroupOps, SimGroupTests, FluidEffectsGroupTests, Retiming_Shifts_Everything_And_Keeps_The_Internal_Timing, Folding_Puts_The_Baked_Layers_In_One_Folder_In_Order, A_Group_Stores_No_Geometry_Of_Its_Own, EmitterScatter, EmitterScatterTests, Scatter_Breaks_A_Burning_Edge_Into_Separate_Flames, Scattered_Flames_Differ_In_Height_Without_Being_Told_To, A_Longer_Surface_Gets_More_Flames_From_The_Same_Settings, EffectPreset, EffectPresets, EffectPresetStore, EffectPresetTests, FluidEffectsPresetTests, An_Effect_Made_From_A_Preset_Draws_Exactly_What_The_Original_Drew, Layers_Reconnect_By_Name_And_What_Cannot_Is_Reported, Shading_Slides_The_Inner_Bands_Toward_The_Light_And_Leaves_The_Silhouette, A_Highlight_Cannot_Be_Lit_Out_Of_Its_Own_Volume, FreeSurfaceSource, MetaballSource, ObstacleBoundaryTests, Combustion, CombustionTests, A_Detached_Piece_Of_Flame_Survives_More_Frames_When_It_Can_Burn, A_Parcel_That_Is_Burning_Stays_Hot_Far_Longer_Than_One_That_Is_Only_Cooling, Burning_Spends_Its_Fuel_So_It_Puts_Itself_Out, Fuel_Below_The_Ignition_Point_Does_Not_Burn, Fire_Arrives_Burning_And_Smoke_Does_Not, The_Burning_Controls_Appear_Only_When_It_Is_On`
  - Designed in `docs/DESIGN-fluid-effects.md` (2026-08-18), answering "is a
    performant 2D fluid simulation possible, with the outline in the artist's
    line style and the shape filled with colour". It is Pillar 4 rather than an
    entry under *Non-destructive filters* because it **authors drawings** rather
    than transforming pixels: an effects element runs a deterministic solver
    over a frame range and writes ordinary `ToolKind.Fill` bands plus a
    `ToolKind.Brush` outline into each frame. So the outline is a real stroke
    carrying a real brush, and playback, export and the per-pointer budget pay
    nothing for a baked element.
  - **Measured before it was designed.** The existing `FluidLattice` — a
    watercolour solver doing considerably more than a plume needs — runs a
    192×108 grid at 1.7 ms/step in Release, so 24 frames on 8 substeps is
    ~330 ms; contour trace and Douglas-Peucker simplify is 0.64 ms and yields
    37 points for an annulus. Fluid is low-frequency, so the sim runs coarse and
    the *contour* is traced into document coordinates. The budget is a
    cancellable bake, not a frame: ≤ 2 s for 48 frames of fire.
  - **The risk is boil, not cost, and invariant 2 does not cover it.** A
    bit-reproducible sim still yields contours whose point count and
    parameterisation jump every frame. Q116 chose per-frame tracing anyway —
    right for fire, and its costs (no dial-down, no `StrokeMatcher`
    correspondence inside an element, N independent polylines) are written down
    there against the day water arrives.
  - **One pipeline, three sources of field.** `field → iso-contour → strokes` is
    the stage that makes a drawing, and it does not care where the field came
    from — which is what decides how far the feature reaches. A *solved grid*
    gives the gaseous family (smoke, fire, steam, dust, ash, ink), and those
    genuinely are one engine with different numbers. *Splatted particles* —
    metaballs — give goo and blobs that merge, and are the cheap unbuilt source.
    A *free surface* gives water and is the hard one: there the contour **is**
    the simulation rather than a threshold of it, so no parameter reaches it from
    smoke. Step 2's tracer therefore takes a field, not a solver.
  - **Q116 settles the four pivotal choices**: bake to strokes with the
    parameters kept in a `Doc.Sims` registry absent until authored; bands *and*
    particles from the first slice; per-frame tracing; fire first. Three went
    against the recommendation and each records what it costs.
  - **Following an art style is mostly free, and Q118 settles the rest.** Because
    the outline is a real `ToolKind.Brush` stroke, every brush the artist owns —
    imported `.abr`/`.kpp` included — already draws it. What a brush cannot say is
    how the contour becomes a stroke, so a **line treatment** carries that: where
    the line sits relative to the band (offset, partial, broken), what varies its
    weight along the way (curvature, flow speed, field gradient, light direction,
    band depth, taper — blended, written as per-point pressure so no new
    rendering is needed), and how smooth or jagged it is. Named by id with
    per-field overrides, in ratios, angles and **stroke-widths rather than
    pixels** — invariant 7's argument, and the same property that makes each field
    observable in a drawing, which is what Q118's choice to design for style
    inference now requires. It applies at *trace* time, so restyling costs ~30 ms
    against ~1437 ms to re-simulate; the fields are cached for the session so
    tuning a look is live. Gate G12 applies from the record onward.
  - Build order is six branches, one objective each — solver, field → strokes,
    record, fire end to end, docker, then smoke, goo and water — plus style
    inference last, which needs a look to be judged against. Stays `[?]` until
    step 4 gives it evidence anchors to name: anchoring it earlier would make the
    box read `[x]` — *built* — for a feature no artist can reach.
  - **Three pieces are specified and waiting, in this order** (2026-08-18). Each
    is one branch and none blocks the others:
    **(1) Emission flicker** — an area mask refuels itself every frame, so it
    reads as a burning edge rather than flames; modulating emission per cell by
    `Hash01` over position *and frame* makes burning points wander. It is the
    first effect parameter whose seed varies by frame, which is Q80's ground for
    brushes, so it needs its own re-render and hold tests.
    **(2) Drawn art as an obstacle** (Q125) — the real solver work: interior
    Neumann boundaries in the pressure solve, flux transport that will not carry
    mass into an obstacle cell, and conservation tests that learn mass may be
    held against an obstacle and not only against a wall. `SimElement.ObstacleLayerId`
    and the `ISimMasks` seam are already in place for it.
    **(3) Anchor attachment** (Q122) — an element bound to a drawing's anchor, so
    it follows a character with no keying. The coupling it introduces is that a
    bake starts depending on another layer's drawings.
  - **Painted emission landed 2026-08-18** (Q124, refined by Q125): an emitter
    names a mask layer and emits where that layer has ink, with a keyable origin
    as the only thing that moves it — so a travelling emitter lays a trail, which
    is smoke behind flying debris as much as fire on a hem. **Q125 corrects
    Q124**: alpha lock belongs to the painter, not the bake, because an
    intersection at bake time would let a hem swinging away silently extinguish
    the fire on it. The render then said something no test did — a mask that
    emits over an area every frame refuels itself and reads as a *glowing shape*
    rather than flames, because no tongue can detach. Flames need emission sparse
    in space (paint a broken mask — already works) or in time (a flicker seeded
    from position and frame, which is a decision rather than a tweak and is not
    in this branch).
  - **Wind landed 2026-08-18** (Q122), with `EffectParam`/`EffectKey` — the key
    vocabulary `DESIGN-effects.md` specified — built as its first user rather
    than a second one invented. Wind is a *relaxation toward the wind's speed
    weighted by how much fluid is there*, so still air stays still and only the
    plume is blown; a uniform push in a closed box is divergence the projection
    removes on the same step. The inertia that makes a simulation beat several
    bakes is measured: two frames after a reversal the risen smoke leans −24.6
    cells while fresh smoke leans +4.0. Also a pre-roll, so an element opens on
    an established plume rather than on still air (16.0 → 40.9 on the first
    frame).
  - **Step 4 landed 2026-08-18**: fire end to end — emitters, the temperature
    field, the heat ramp, embers, and a re-bake that replaces only what the
    element still owns. `SimBaker` splits solving from drawing, measured **40×
    apart** (1756 ms against 43 ms for 48 frames), which is what makes Q123's
    live preview affordable. **The picture disagreed with every green test**:
    turbulence acting as wind, a plume that stalled, nothing damping the flow, and
    bands landing entirely inside the core because a plume's field is steeply
    peaked. `SimParams.Drag` was added and measured (7.0 → 3.6 → 0.9 cells of
    drift at 0 / 0.05 / 0.12), band levels became fractions of the element's own
    peak, and two of the findings are now tests. Looking also caught a defect
    nothing else would: `PeakBand` sampled only the frames an element kept, so
    exposing on 2s shifted every band level.
  - **Step 6g landed 2026-08-19**: presets — an effect tuned once and used
    again. `EffectPreset` keeps a group's *parameters* where a symbol would keep
    its frames (Q129): using one re-simulates, and in exchange gives a real
    effect that can then be retuned. Verified by rendering rather than asserted:
    an effect made from a preset draws **exactly** what the original drew,
    translated — compared relative to each element's origin, because a preset is
    stored relative and an absolute comparison reports a difference that is the
    feature working. **Layer references travel by name**, which is the one real
    decision: an id names a layer in *this* document and nothing anywhere else,
    and an emitter pointed at a missing layer emits nothing at all — so capture
    keeps the name, instantiate matches it, and `MissingLayers` reports what
    could not be reconnected *before* it happens rather than after.
  - **Step 6f landed 2026-08-19**: scatter — an emitter feeds every cell it
    covers, so nothing can detach from an area and a painted hem reads as one
    continuous burning edge. `Emitter.Scatter` picks discrete sites instead:
    measured, a continuous hem is 99% alight in one run and the same hem
    scattered is 46% alight in eight flames. **It supersedes 5c-i (emission
    flicker) as the answer to that problem** — flicker was temporal, scatter is
    spatial and stable, so the gaps are in the same place every frame and what
    rises off a flame actually leaves. **Two corrections the measurements
    forced.** The design said `HeatVariation` would give tall flames beside
    short ones; it does not, because height is roughly logarithmic in heat and
    the spread is already there at zero (10, 16, 24, 30, 38, 42, 44 cells) —
    the fluid makes it, since a site with neighbours is fed by their rising
    column. Heat varies *fierceness* instead, which for fire is which colour
    bands each flame reaches. And a site is half a *spacing* across rather than
    the emitter's radius: for a disc that radius is the extent of the shape the
    sites scatter over, so deriving from it made every site as big as the disc
    and a scattered disc came out as one blob.
  - **Step 6d landed 2026-08-19**: effects — several elements that are one
    thing, answering "like Unity's particle system, could we combine and layer
    these?" Layering already worked (an element bakes to a layer each, so they
    composite and z-order like any drawing); what did not exist was a way to say
    three of them belong together. **A `SimGroup` carries no geometry**: the
    obvious design is a group origin and frame offset applied at bake, and it
    was rejected after counting the call sites — placement is read in the solve,
    the trace, the mask rasteriser, the bake and the preview's frame range, and
    an offset missed at any one is a bake landing somewhere other than the
    preview showed. `SimGroupOps` writes the members' own records instead, so
    the bake path never learns groups exist and ungrouping is lossless. The
    additive form is already provided one level up by `SymbolPlacement`, which
    is what settles it. Retiming *shifts* rather than aligns, because the smoke
    starting four frames after the flash is the effect's timing.
  - **Step 6b landed 2026-08-19**: explosions — `Emitter.EmitFrom`/`EmitUntil`
    bound emission to a frame or two, and `Emitter.Burst` expands the front as a
    volume source in the pressure solve (`FluidSolver.AddExpansion`), not as an
    outward velocity. **The reasoning behind that choice was too strong and the
    test caught it**: the claim written first was that a radial push achieves
    nothing because the projection removes divergence, and the test written to
    pin it failed. A push moves the front perfectly well; what the projection
    forbids is the fluid occupying more *room*, so a push is served by
    displacement and evacuates the middle where an expansion keeps it filled —
    0.45 against 0.50 at matched reach, a fireball with a hole in it versus one
    that fills out. Real, visible across a sequence, and far less than the
    argument promised. The first evidence for the overclaim was itself
    confounded — a burst measured against a plume whose buoyancy swamped it — so
    "it did nothing" was read off a test where nothing could have shown.
  - **A one-frame element, found the same day and shipped three days earlier.**
    `NewElement` sized an element to `Doc.Scene.FrameCount`, and a fresh
    document has **one frame** — so pressing Fire made a one-frame effect whose
    preview showed the same drawing whatever the scrubber said. It was mistaken
    for the plume reaching a steady state *twice*, in two separate renders,
    before a direct measurement of the solver caught it. Every earlier claim in
    this feature about how frames evolve was made against that. New elements are
    24 frames now, and baking grows the timeline to hold them.
  - **Combustion landed 2026-08-20 (Q132), and the measurement corrected the
    complaint before it corrected the code.** The owner noticed real flames shed
    their tips and ours did not; the reading that suggests — *the fluid never
    breaks up* — is false. It breaks up on 22 of 40 frames and the tracer draws
    every piece. What was missing was **survival**: six sheds of five cells or
    more over forty frames, **every one lasting exactly one frame**, median piece
    one cell. Heat was only ever stamped at an emitter and then decayed, so a
    detached parcel had nothing to live on. Slowing `Cooling` does keep one alive
    — 12 and 26 frames at 0.01 against one at 0.06 — and doubles the flame's
    height doing it, because **one number was setting both a flame's length and
    its tip's survival**. `SimParams.Burning` is that second job moved somewhere
    it can be set alone: density is the fuel, a fraction burns per step above an
    ignition point, and the reaction is self-limiting because burning spends the
    fuel — which is also how `Burst` gets *fireball becomes smoke* for free. A
    value type, because `SimParams` is a record cloned with `with { }` and a class
    would leave a duplicated effect editing the original. The longest-lived piece
    goes 2 → 8 frames on the flame that prompted it, and 1 → 4 on a new element
    with nothing else touched — the expected `Vorticity` compensation turned out
    to be grid-dependent and unnecessary at the size a new element starts.
  - **Step 6 landed 2026-08-19**: smoke, and two things that only a render
    said. **A smoke emitter has to be warm** — buoyancy reads temperature and
    `Weight` reads density, so an emitter at zero heat is pushed down by its own
    mass and spreads on the floor as a pancake, which is what the first smoke
    render was: four identical frames of a flat blob. Measured, 0 heat reaches
    7.2 cells above the emitter and 0.4 reaches 26.6. And **bands are concentric
    by construction, which a lit volume is not** — they are iso-contours, so
    unshaded smoke is an onion however it is coloured. `LineTreatment.ShadeOffset`
    slides band *b* by *b*/(bands−1) of itself toward `LightAngleDeg`, leaving
    band 0 where it is because that one is the silhouette. It lives on the
    treatment rather than on the element because it is a style decision, and it
    shares its angle with the line-weight driver so one light serves both halves
    of lighting. Clamped to the silhouette's box, which was also found by
    rendering: at the slider's end the highlight otherwise pokes out past the
    outline and reads as a second paler shape sitting on top.
  - **Step 5 landed 2026-08-19**: the effects window (`Ctrl+Shift+E`), and with
    it the first version an artist can actually use — make a flame, tune it,
    watch it, bake it. The window holds no effects logic: it is bindings over
    `FluidEffectsViewModel`, and `MainViewModel` gains one mutation seam
    (`MainViewModel.Effects.cs`, which exists so `InvalidateFrameRender` can stay
    private) while `MainWindow` gains a menu item, a shortcut case and one field.
    Three things it settled that the design had not.
    **Simulate and Bake are separate buttons**, because the two costs differ by
    about forty times: a style edit redraws from the solve in hand and previews
    as the slider moves, a fluid edit marks the picture stale and waits to be
    asked. Hiding that would make every edit feel like the slow one, and would
    make an artist afraid to touch the cheap half. **The outline pen belongs to
    the element** rather than being read from the toolbar at bake time — the
    obvious wiring makes a bake unreproducible, so the same element re-baked
    after picking up a marker comes back inked with a marker. And **rows hold ids
    rather than elements**, because undo swaps a whole `Doc` back in: a row
    holding the object would go on editing a document nobody is looking at, with
    every slider still appearing to work. The tunables are *data* rather than
    controls, so `Every_Solver_Parameter_Has_A_Row` can walk `SimParams` by
    reflection and fail when a parameter is added to the record and never
    surfaced.
  - **Step 3 landed 2026-08-18**: the record. `Doc.Sims` and `Doc.LineTreatments`
    are absent until authored, `Stroke.SimId` is absent on every hand-drawn
    stroke, and an override that states nothing serializes as `{}` rather than as
    sixteen nulls — the medium block's lesson paid up front rather than found in
    the JSON later. `Doc.TreatmentFor` is the one place the cascade resolves.
    `SimParams` moved into Core so the solver reads the document's own numbers,
    the way `BrushEngine` reads a `BrushSettings`; the cost, stated rather than
    hidden, is that a solver tuning change is now a file-format change.
  - **Step 2 landed 2026-08-18**: `MarchingSquares` and `FieldTracer` turn a
    field into filled bands and treated outlines, guarded by 43 tests on a static
    field so the tracer is judged without the solver in the reading. The
    interpolated crossing lands within 2e-7 of a cell where the field really
    crosses, against the half-cell a mask tracer would give — ten document pixels
    of staircase at a coarse grid. Re-tracing a 48-frame element measures
    **44 ms** against ~1437 ms to re-simulate, which is what makes tuning a style
    live. `LineTreatment`'s cascade resolves `override ?? shared ?? default` out
    of one record rather than two.
  - **Step 1 landed 2026-08-18**: `FluidSolver`, an incompressible MAC-grid
    solver with buoyancy, vorticity confinement and seeded curl-noise
    turbulence, guarded by eighteen tests. Two findings worth carrying forward.
    The measured cost is **3.74 ms/step at 192×108** — more than twice
    `FluidLattice`, where the design predicted half, because an incompressible
    solver pays for a pressure projection a shallow-water one does not; the
    1437 ms bake still meets the two-second target, but the same wrong reasoning
    would have called a 512×288 element affordable. And the textbook scalar
    advection **invented up to 193% of its own density**, which Q117 settled by
    moving density and temperature onto conservative face fluxes.
- [x] Motion path visualization `evidence: MotionTrail, MotionTrailPainter, MotionTrailTests, MotionTrailOverlayTests, TheTrailRunsInTimelineOrderWithTheCurrentDrawingMarked, TheViewModelHandsTheWindowTheTrailAndKeepsItCurrent, TheToggleIsRegisteredSoItCanBeFoundAndRebound`
  - **Landed 2026-08-16 with spacing visualization as one overlay — the motion
    trail (Q98)** — because they are one thing: a polyline through the
    subject's position on each drawing around the playhead, a tick per
    drawing, and the gaps between ticks *are* the spacing. Earlier red, later
    blue (the onion tints, one convention not two), current ringed white.
  - **The tracked point is the drawing's authored pivot anchor, else its ink
    bounds' centre** (Q98's second half). Filled tick = authored, hollow =
    derived, so an artist knows which ticks to trust before fixing an arc off
    them. Sockets are ignored — a hand's attachment point is not the subject —
    and `Scene.Pivot` is deliberately no fallback: one point for the whole
    document is a dot, not a motion.
  - **View-only and record-clean**: `MotionTrail.PointsAround` is pure Core
    arithmetic riding `OnionSkin.Ghosts` (holds resolve to one tick, depth
    counts drawings), the painter follows `RigOverlayPainter`'s
    bitmap-testable discipline, and the toggle is a registered shortcut plus
    the timeline-bar checkbox beside onion skin. Settings persist app-side
    like onion's.
  - **Known edge, accepted for the slice**: a rig-bound drawing is trailed
    where it was drawn, not where the pose moved it — the manual marks posed
    trails *Planned*, and it belongs to the arcs/analyzer follow-ups this
    substrate exists for.
- [x] Motion arcs `evidence: MotionArc, MotionArcOverlay, MotionArcPainter, MotionArcTests, MotionArcOverlayTests, TheFitRecoversTheCircleTheTicksSitOn, CollinearTicksFitALineNotANonsenseCircle, AnOffArcTickIsFlaggedWithItsFootOnTheArc, TheArcTogglesAreRegisteredSoTheyCanBeFoundAndRebound`
  - **Landed 2026-08-20 with arc prediction as one overlay** — the first of
    Q98's follow-ups on the trail substrate. A least-squares circle degrading
    to a line (Q133: the other models are wanted *eventually* — the parabola
    with the jump arc analyzer, a model picker when a second model exists),
    drawn in gold under the trail's ticks: one added colour for one added
    concept, the arc's opinion against the ticks' facts.
  - **Off-arc is a leave-worst-out judgement, not a raw residual.** A
    least-squares fit pulls toward an outlier and spreads the blame — one
    drawing 14 px off put every tick past tolerance in the first version, and
    the largest deviator was an innocent edge tick. The tick whose *absence*
    most improves the fit leaves until the rest agree, then everything is
    judged against that arc; the flagged tick carries its **foot**, so the
    overlay says "move it about here" rather than just "wrong". Tolerance is
    read off the ticks' own polyline — a tolerance that moved with the curve
    would judge a drawing by how much it dragged the judge.
- [x] Arc prediction `evidence: ThePredictedNextContinuesTheSpacingTrend, AnEmptyCurrentCelGetsAPredictionBetweenItsNeighbours, AStalledSubjectPredictsNothing, ARealNextDrawingSuppressesThePrediction, TheArcPaintsAndThePredictionsAreDashed`
  - The overlay's other half: a dashed tick where the drawing after the last
    one should land — spacing carried on from the artist's own, an ease kept
    easing, clamped so a spike does not launch it off canvas — and a dashed
    tick on an empty cel between drawings, the inbetweening moment. Dashed
    everywhere because a suggestion drawn like a fact gets treated as one.
  - **The refusals are the design**: no prediction on top of a real drawing
    (the view model probes one drawing past the window, and the probe tick is
    never displayed), none for a stalled subject (a hold's exit is a timing
    decision, not geometry), none from two ticks (a chord agrees with every
    arc through its ends). Timeline-proportional spacing for the empty cel is
    deliberate for this slice — obeying an authored timing chart belongs to
    the spacing assistant below.
- [x] Spacing visualization `evidence: TheTrailPaintsTicksAndTheLineBetweenThem, AHoldIsOneTickNotTwo, AnAuthoredPivotBeatsTheDerivedCentre, ErasingSaysNothingAboutWhereTheSubjectIs`
  - The motion trail's other half — see the item above; one overlay, two
    promises. The hold rule is the load-bearing one for spacing: a tick per
    cel would pile invisible coincident dots on every hold and make tick
    counting lie about drawing counts, so ticks count drawings and a hold on
    2s is one tick standing still.
- [x] Spacing assistant `evidence: SpacingAssistant, TargetsForRun, FrameTranslate, AnalysisOverlayPainter, SpacingAssistantTests, FrameTranslateTests, ABunchedDrawingIsFlaggedAndItsTargetSitsOnTheMeasuredPath, TheExtremesOwnChartBeatsTheGlobalEasing, TheNudgeMovesTheDrawingAndUndoRestoresTheExactBits, ANudgeRefusesADrawingWithAPixelBaseline, ANudgeRefusesAClipLimitedStroke, EvenWorldSpacingCanMissThroughAZoomingCamera`
  - **The acting half of the spacing chart (Q134)**: ghost ticks on the trail
    where the intended spacing (the extreme's chart, else the easing) wants
    each inbetween, and a one-click nudge that slides the playhead's drawing
    along the measured path onto its target — a whole-frame translate
    (`FrameTranslate`, everything `ImageResize` visits), one undo step, exact
    on undo. Targets use the trail's subject (`MotionTrail.Locate`), not the
    graph's centroid, so the ghost and the real tick cannot disagree.
  - Extremes are never targets; a drawing with a pixel baseline or a
    selection-clipped stroke is refused whole rather than torn from its
    pixels or its mask (the clip lives on the document, content-hashed and
    shareable, so it cannot travel with one drawing).
- [x] Timing charts `evidence: TimingChart, TimingChartView, TimingChartTests, TimingChartVmTests, TheChartPlacesTheInbetweensExactlyOnItsRungs, TheInbetweenerObeysTheChartOverTheBar`
  - The ladder on the extreme (Q58): `Frame.Chart` holds the rungs, the cel
    menu's editor writes them, and both inbetweeners and the intended-spacing
    curve read the same list — one authored object, every consumer.
- [x] Automatic contact frame detection `evidence: ContactFrames, ContactReading, ContactFramesTests, AFootfallStartsWhereAPlantedDrawingFollowsAnAirborneOne, AShotIsNotACycleSoNothingWraps, DetectContactsMarksTheFootfallsOnceAndUndoes, DetectContactsLeavesTheArtistsOwnMarkerAlone, ContactMarkersTrimTheFitToTheAirborneStretch`
  - **Detection reads; the marker is the artist's** (Q135): "Detect contacts"
    reads footfalls off the lowest ink — the walk analyser's ground-band rule,
    shared through `ContactFrames.Planted` so the two can never disagree —
    and writes named "contact" markers as one undo step, on request only.
    Frames already marked are left alone, whatever they say.
  - Linear where the walk analyser wraps: a shot is not a cycle, so frame 0
    planted IS a footfall and the landing is not the takeoff.
  - The jump arc analyser trims its fit to the airborne stretch between
    contact markers — authored record, not re-detection, so correcting a
    wrong split is editing a marker rather than arguing with a heuristic.
- [?] Perspective consistency checker
- [?] Silhouette readability preview
- [x] Walk cycle analyzer `evidence: WalkCycleAnalyser, WalkCycleReport, WalkFinding, Ground, WalkCycleAnalyserTests, ShotAnalyserTests, ACleanCycleReportsNoFindings, ASeamThatJumpsNamesTheLoop, UnevenContactsNameTheStride, ALopsidedBobNamesBothSteps, AWalkUpASlopeReadsItsContactsAlongTheSlope, ATravellingWalksPlantedFootMustHoldItsPlace, AWalkTowardCameraIsHedgedNotFlagged`
  - Reads the active layer's sheet as one cycle (Q134): loop closure, contact
    evenness, bob symmetry — all off the record, feet as the lowest ink,
    tolerances as fractions of ink height. Prose in the trail's flyout
    readout, advisory by design: a deliberate shuffle is allowed to trip it.
  - **The loop check is a seam check, not an equality check** — a correct
    cycle's last drawing differs from its first by one step, so the seam is
    judged against the steps *away* from it (a wrong endpoint corrupts the
    step beside it too, and must not set its own yardstick).
  - **Shot terms since Q137**: the range is the tag under the playhead (else
    the whole sheet), the ground is the artist's "ground" line guide at any
    angle (else horizontal lowest ink), the seam checks gate on the tag's own
    `Loop` flag (else standing in place), a travelling walk gains foot-slide
    detection — the planted foot holds still, or treads at a constant rate —
    and size drift past the depth band is hedged as depth motion instead of
    flagged. Always world-space: a camera move cannot make a foot slide.
- [x] Jump arc analyzer `evidence: JumpArcAnalyser, JumpArcFit, FitRun, CameraView, JumpArcAnalyserTests, ShotAnalyserTests, PointsOnAParabolaFitWithNoOffenders, ADrawingOffTheArcIsNamed, AnArcThatNeverComesDownIsNotBallistic, TheFitIsTheRunAtThePlayheadNotTheWholeLayer, AJumpOnASlopeFitsTheParabolaItActuallyDescribes, ThroughTheCameraTheJumpVerdictTravelsWithoutACurve`
  - Fits x-linear/y-quadratic to the playhead's run (Q134) — cel-index time,
    so 2s carry their real timing — draws the arc dashed on the trail and
    rings the drawings off it. Closed-form least squares run twice: once over
    everything, once without the single worst drawing, because a plain fit is
    dragged toward the bump and smears blame onto its neighbours (measured:
    one 40 px bump flagged four of six drawings before the trim, one after).
  - Under four located drawings it returns nothing: three points fit any
    parabola. Contact markers trim the fit to the airborne stretch (Q135).
  - **Shot terms since Q137**: the fit's axes are the ground guide's, so a
    jump on a slope fits the parabola it actually describes; size drift past
    the depth band suppresses flags and hedges instead; and "through the
    camera" refits on each frame's projected position — does it read as an
    arc where the audience looks — with the verdict hedged as being about the
    read, and no curve drawn (a between-frames sample has no framing).
- [?] Timing diagnostics

## Pillar 5 — One-click export to game engines

Atlases, metadata, pivots, events and hitboxes, in one action. Half of this
exists; the half that does not is what makes it *one* click.

- [x] Sprite sheet generation `evidence: SpriteSheetExporter, SpriteSheetExportTests`
- [x] Consistent trimmed bounds across a sequence `evidence: SpriteTrim, SpriteSheetOptions, TrimmingDefaultsToTheUnion_SoEveryCellIsTheSameSizeAndNothingJitters`
- [~] Normal maps for the sprites `evidence: NormalMapGenerator, NormalMapOptions, NormalGreen, NormalMapWriter, DistanceInside, NormalMapTests, ABevelFromTheSilhouetteNeedsNoDependency, ThePreviewLightIsNotBakedIntoTheMap, GreenIsBrightAtTheTopUnderOpenGlAndAtTheBottomUnderDirectX, NormalMapPanel`
  - Three tiers, and the cheapest one first because it makes the panel, the preview light and the export path real: a Sobel over the silhouette's distance field needs no dependency and no model. Then Laigter for artists who have it. **Tier three is AI, and it lives under AI assistance** — it refines whichever base map the first two produced rather than replacing either, so it is filed with the features whose cost is per request.
  - **Laigter is GPL-3.0.** Linking it in would put Lightbox under GPL-3.0, which is a project-level licensing decision and must not be made by accident inside a normal-map task. Running its CLI as a separate optional tool keeps the licences apart and is how it should behave anyway — absent unless the artist has it, degrading to the built-in generator rather than breaking. See `docs/DESIGN-subject-reading.md`.
  - **Tier one is built and exported; the panel is not.** `NormalMapGenerator` takes an alpha channel and returns RGBA, `NormalMapWriter` writes `<name>_normal.png` beside a sheet, and the export window has the checkbox. What is missing is the interactive panel with a draggable preview light — so the item is `[~]` rather than `[x]`, and the anchor for the panel is left in place unresolved rather than quietly dropped.
  - **Chosen as the next Pillar 5 item precisely because it can be verified here.** The alternative was another engine exporter, and the Unity defect had just shown what an unverifiable write-only integration costs. This is pure arithmetic over an array: no dependency, no model, no external API to get wrong.
  - **The green channel is the pivot bug again, and it was treated as such before it could bite.** OpenGL (+Y up) is Unity and Godot; DirectX (−Y) is Unreal. Get it backwards and a character lit from above reads as lit from below, every bevel reads as a groove, and nothing points at a channel convention. So it is a named parameter with a test on each value, a mnemonic written down (*on an OpenGL map of a sphere, green is bright at the top*), and a discriminating test that the two conventions differ in **green only** — a version that flipped red as well, or flipped nothing, passes a green-only check on a symmetric shape.
  - **The Unity target deliberately does not override it.** A flipped green is something an artist should see and set, not something that changes silently under them when they pick an engine.
  - **Deterministic by construction** — no RNG, no sampling, a pure function of alpha and options. Invariant 2 is trivially satisfied here, which is a third reason this tier comes first.
  - **A rounded profile is the default because a chamfer creases.** A linear ramp's derivative jumps where it meets the flat interior, and a jump in the derivative is a visible line under a moving light that no artist drew. The quarter-sine has zero derivative at the top; a test measures the largest step across the bevel and shows the dome's is smaller.
  - **Derived from the finished sheet, not re-composed.** A map generated from a second render could disagree with the sheet by a pixel about where an edge is, and a normal map one pixel out of register with its albedo puts a bright rim on every silhouette. Reading back also means trim, padding and packing are already applied, so the two line up cell for cell with nothing to keep in step.
  - **Distance is a two-pass chamfer, not exact Euclidean**, and that is the medium-simulation rule applied to a filter: the difference is smaller than the antialiasing on the edge it measures from, so the cheap one is correct. Both sweeps are needed and a test proves the second one runs, by asserting the field is symmetric about a box's centre.
  - **Outside the silhouette is the flat normal, not black.** `(0,0,0)` decodes to a normal pointing away from the surface and lights as a black hole wherever an engine samples the edge.
  - One test measured strength at a single pixel against a threshold picked by guesswork, and failed at 123 against a limit of 120. The number was real and the assertion was not: the claim is about how *far* the tilt reaches, so it now measures the width of the tilted band.
- [x] Automatic packing (rect/skyline, tighter than a grid) `evidence: SkylinePacker, PackedRect, PackResult, SpritePack, SkylinePackerTests, NothingOverlapsAndNothingLeavesTheSheet, TheSameInputPacksIdentically, PackingBeatsAGridOnRaggedInput, APackedSheetIsSmallerThanTheGridOnRaggedFrames, TheGridIsStillTheDefaultAndItsBytesAreUnchanged`
  - **Measured, not claimed.** Eight ragged frames at per-frame trim: **12 625 px packed (101x125) against 16 200 px on the grid (90x180) — 22% smaller**, at 80.8% occupancy. The exporter's own note said packing "needs per-sprite metadata to be usable at all", so the metadata landed with it.
  - **Grid stays the default and its bytes are unchanged**, asserted as a byte comparison rather than a shape check — "the same layout" and "the same file" are different claims and only the second keeps somebody's importer working.
  - **Deterministic**: same sizes in, byte-identical sheet out. Not for invariant 2's sake, but because a re-export that reshuffles the atlas makes every downstream diff meaningless. The sort is total — height, then width, then **input index**, which never ties; stopping at height would have been stable by luck.
  - A packed sheet reports `columns` and `rows` as **0** and `pack: "skyline"`, because an importer that divides by a plausible-looking column count would be silently wrong.
  - **No UI yet** — `SpriteSheetOptions.Pack` is reachable from code and the MCP surface. The picker belongs with the export preset in the one-click item below, and putting it anywhere else first would mean moving it.
- [x] Atlas optimization `evidence: SpriteSheetResult, PackResult, APackedSheetIsSmallerThanTheGridOnRaggedFrames`
  - It **is** the packer, reported. `SpriteSheetResult.UsedArea` and `Occupancy` put a number on the result, because "atlas optimisation" with nothing measured is a feeling. Occupancy is deliberately not compared *between* modes: a grid with no padding is 100% cell-occupied by construction however empty those cells are, so total sheet area is the honest comparison.
- [~] Sprite atlas generation across characters `evidence: SheetFrameOwner, SeveralDocumentsConcatenateInTheOrderTheyAreGiven, EachDocumentBecomesATagSoAnEngineCanTellTheClipsApart, EveryFrameKeepsItsOwnDocumentsPivot, RunningAGroupedPlanWritesOneSheetHoldingEveryDocument`
  - **"Across characters" was a scope question, and Q30 answered it.** A folder declared as `OneArtifact` is the boundary; everything under it packs into one sheet, with a frame tag per document so an engine can still tell the walk from the run.
  - The parts that are not obvious and are therefore tested: the untrimmed cell takes the **largest** canvas so a bigger character is not cropped by a smaller one that came first; pivot, fps, anchors and colliders are all **per owning document**, because a sheet-wide answer puts every character after the first in the wrong place; and `SheetFrameOwner` is what lets the engine exporters ask which document a frame came from.
  - **A GameMaker sprite and a PNG sequence refuse.** One animation with one origin and one image speed is what those formats *are*, so several documents is not one artifact — they say so and write nothing rather than exporting the first and looking successful.
- [x] Generic JSON exporter `evidence: SheetDocument, SheetMeta, SheetFrame`
- [x] Export frame durations `evidence: SheetFrame, TheSidecarIsAsepriteShaped`
- [x] Export metadata `evidence: SheetMeta, SpriteSheetResult`
- [x] Pivot editor `evidence: Pivot, ThePivotIsRecordedPerCell_SoTrimmingCannotShiftTheCharacter`
- [x] Multi-frame pivot editing `evidence: Anchors, SetAcross, ClearAcross, AnchorTests, SettingAcrossARangeTouchesEveryDrawingInIt, AHeldDrawingIsVisitedOnceRatherThanTwice`
  - Works on **drawings**, not cels: a range on 2s holds each drawing twice, and resolving through the exposure means a hold is visited once rather than edited twice. One position for the range rather than an interpolation — interpolating a socket needs two authored ends and a curve, and guessing it would make the simple case unpredictable.
- [x] Named pivot points `evidence: Anchor, AnchorKind, AnchorPoint, Declare, AnAnchorRoundTripsThroughTheFile, ADocumentWithNoAnchorsCarriesNoAnchorKeys`
  - `Scene.Pivot` stays the document's single unnamed pivot and is what an engine reads when nothing else is declared; a named anchor of kind `Pivot` is for a *second* one.
- [x] Socket system `evidence: AnchorKind, ResolvedAt, AnAnchorIsExportedPerFrameByNameAndInsideTheCell, AnAnchorOnAHeldDrawingIsExportedOnEveryFrameItShows, PackingDoesNotMoveAnAnchorRelativeToItsCell`
  - **The declaration and the positions live in different places, and that is the load-bearing choice.** `Scene.Anchors` holds the names, because a name is a property of the rig and renaming "left hand" must not touch a drawing. `Frame.Anchors` holds where the point *is*, because a hold, a re-time, a cel drag and a timing preset all move drawings around the sheet — an index-keyed table would silently point at the wrong drawing after any of them, and it would look like an animation bug. On the frame the anchor travels with its drawing for free, and a test re-times a range to prove it.
  - Exported per frame, keyed by **name** rather than id, measured **inside the cell** like the pivot so trimming cannot move where a weapon attaches. Positions are stored in document pixels; normalising them into the record would bake the trim in and make a re-export at a different trim wrong.
  - Exported and nothing more: parenting a GameObject to a socket is the engine's job. Lightbox owes the position.
- [x] An anchor carries a direction — a nullable angle per placement (Q144) `evidence: AngleDeg, StalkTipOf, AnchorDirectionTests, AnAnchorWithNoAngleWritesNoAngleKey, AnAimedAnchorRoundTripsItsAngle, TheAngleTravelsWithTheDrawingThroughARetime, TheSidecarCarriesTheSocketsAngle, TheStalkDragWritesTheAngleToTheDrawing, MovingAnAimedSocketKeepsItsAim, ClearDirectionTakesTheAngleAndOnlyTheAngle`
  - What Q143's attachments need to turn the sword with the hand, and what an
    engine wants from a socket anyway. Per frame like the position and on the
    drawing for the same reason; null means no direction, so a document whose
    anchors never turn serializes exactly as before the field existed.
  - **Built (2026-08-22): the field, the stalk, and the sidecar.** Degrees,
    matching every exported `RotationDeg`. The rig overlay's selected anchor
    grows a stalk whose tip is the rotation grip — a ghost stub authors the
    first angle, because an affordance that only exists once used is not one
    — an aimed anchor shows its stalk unselected, and *Clear direction here*
    is the way back to null. Every write path re-records the whole point, so
    a move, a push-across and a re-time all carry the aim; under a resize the
    angle is carried, never scaled — rotation is not a length, the limit
    `ImageResize` already records for guides and bones. The generic sidecar
    gains `angle` beside the position, absent when unaimed. The Unity payload
    deliberately does not carry it yet: its anchors are fixed `[x, y]`
    arrays, and reshaping a contract importers already parse is a decision
    for whoever needs the angle there.
  - **`FrameConverter` names every property it writes, so a field added to `Frame` is silently dropped.** Cost one round-trip test to find. `WriteShared` now exists so the next base-class field is added in one place rather than two, with the hazard written down where somebody will hit it.
  - **The canvas overlay's decisions are built; the overlay itself is not** — the gesture set is designed to place anchors and shapes together, and is recorded on the hitbox/hurtbox editor item below, since it is one piece of work serving both. Nothing paints a socket or reaches the mode yet (**B58**), so an anchor is authored through the API and the file rather than on the canvas.
- [x] Collision shapes `evidence: CollisionShape, ShapeRole, ShapeBox, CollisionShapes, CollisionShapeTests, AShapeRoundTripsThroughTheFile, ADocumentWithNoShapesCarriesNoShapeKeys, AHitboxIsActiveOnlyWhereItIsPlaced`
  - **Rectangles only, and that is the scoping decision rather than a gap.** A rect is what every 2D engine takes directly — `BoxCollider2D`, `RectangleShape2D`, GameMaker's bbox — so it exports without a conversion that could change what the artist meant. A polygon needs a real editor (add, move, delete vertices, over the canvas, per frame) *and* a per-engine decomposition, and half a polygon editor is worse than a whole rect one. Polygons arrive later as a second kind beside this, and every rect authored today keeps working.
  - **Built as a copy of the anchor design on purpose**, down to the six operations: declaration and role on `Scene.Shapes`, rectangle per drawing on `Frame.Shapes`. Same reasoning — a name belongs to the rig, a rectangle belongs to a drawing and must travel with it through a hold, a re-time, a cel drag or a timing preset. A test re-times a range to prove it. The payoff is that the one canvas overlay still to be built places both.
  - `ShapeBox.CentreX`/`CentreY` are `[JsonIgnore]`, because a read-only property serializes like any other one and two derived keys on every shape of every frame is exactly what `BlendOrNormal` did.
- [x] Hitbox and hurtbox editor — one canvas overlay, shared with the anchors `evidence: RigOverlay, RigMark, RigMarkKind, RigCorner, RigHit, RigMarks, DragRig, AddAnchorAt, AddShapeAt, PushRigAcross, RigOverlayTests, RigEditingTests, ADragLandsOnTheDrawingRatherThanOnTheFrameIndex, ASelectedShapesCornerBeatsItsOwnBody, TheSmallerShapeWinsWhenOneIsInsideAnother, DraggingWhileParkedOnAHoldEditsTheDrawingBeingHeld, RigOverlayPainter, TheRigOverlayReachesTheCanvas, RigEditModeIsBindable`
  - **The decisions are built; the overlay is not, and this box was `[x]` for a while saying otherwise.** `RigOverlay` answers what a press hit and what a drag produces, `MainViewModel.Rig.cs` turns that into document edits, and thirty tests cover both — all of that is real. What does not exist is anything that *paints* a mark or *reaches* the gesture: `RigMarks`, `RigEditMode`, `SelectedRigMarkId`, `AddAnchorAt`, `AddShapeAt` and `HasRig` have **no consumer outside the view model and its own tests** — no XAML binding, nothing in `CanvasControl`, no menu item and no `ShortcutMap` entry. An artist cannot turn the mode on, so an artist cannot reach any of it. **B58.**
  - Three anchors are left deliberately unresolved rather than dropped, the way `NormalMapPanel` is on the normal-map item: `RigOverlayPainter` (the painting, pulled out into pure Skia the way `GuidePainter` was, for the reason the next bullet gives), `TheRigOverlayReachesTheCanvas` (a published-snapshot test — the pixels, per charter **O7**, because a binding that is never read is exactly the shape this box already got wrong once), and `RigEditModeIsBindable` (the mode in `ShortcutMap`, per `CLAUDE.md`'s "land the places it shows up").
  - **Designed as one overlay for anchors and shapes together**, which is the whole reason the two records were made the same shape. An anchor is a zero-sized rectangle, so there is one hit-test, one drag and one set of handles rather than two of each — which is what lets the two "no canvas overlay yet" caveats on the anchor items close as one piece of work rather than two.
  - **No sweep, and measured rather than assumed.** A performance pass flagged rig-mark count as an unswept dimension on a `WhileDrawing`-cadence path. It is not one yet — nothing draws per frame — and it will not be a cliff when it is: driving the real view model at 2 / 10 / 50 / 200 / 600 marks, `PressRig` costs 0.0025 / 0.0078 / 0.15 / 0.05 / 0.17 ms against a 16 ms budget, so a rig ten times larger than any real character sits at ~1% of one frame. The number that does grow cleanly is allocation — ~0.26 KB per mark per press, 11.6 KB at 50 marks and 156 KB at 600 — which is worth knowing when the overlay is wired to hover, and is still nothing beside the 0.3 MB over 60 events the charter already accepts. Re-measure when `RigOverlayPainter` exists, because painting is the part that could actually scale.
  - **The gesture's arithmetic lives outside `CanvasControl`, and that was the heatmap's doing.** `CanvasControl.cs` is 2 500 lines with five fix commits behind it, and the real risk of adding a gesture there is not breaking it — it is that the gesture's arithmetic becomes untestable without a window. `RigOverlay` is pure: sixteen tests cover hit order, screen-sized targets and drag maths with no Avalonia at all.
  - **Hit order is the design, and each step earns its place.** A selected shape's corners beat its own body, or resizing is impossible because the body sits under every corner. Anchors beat shape bodies, or a socket on the character inside a hurtbox is unreachable — the ordinary arrangement, not an edge case. The smallest shape wins among overlapping bodies, because a hitbox drawn inside a hurtbox is how a character is built.
  - **Targets are screen-sized rather than document-sized**: a document-space handle is unclickable at 25% and covers the drawing at 800%. An anchor gets a more forgiving radius than a corner handle, because it is a point with no body to grab.
  - **Dragging a corner past its opposite flips the rectangle rather than going negative, and a shape cannot be collapsed to nothing.** A negative width is not a shape an engine can read, and refusing the drag at the crossover makes the handle feel broken; a zero-sized shape is invisible, unhittable and still exported, which is the worst of the three.
  - **Edits land on the drawing, never on the frame index.** Every write goes through the `SetAcross` that already existed, with a range of one — so dragging while parked on a hold moves the drawing being held rather than turning the hold into a drawing. A test re-times the range afterwards and finds the anchor still on its own drawing, which is the property the record was shaped for.
  - What the record already settled, so the overlay did not have to decide it: **absence is the off state**. A sword connects on two frames of a six-frame swing, so the rectangle exists on those two drawings and nowhere else, and there is no active flag for a re-time to desynchronise. `ClearRigHere` and `DeleteSelectedRigMark` are therefore separate gestures — clearing this frame is how a hitbox stops being active partway through a swing; deleting takes the declaration and every placement.
  - `PushRigAcross` is the bridge to the multi-frame operations: place a physics body once and push it across the cycle rather than dragging it twenty-four times, with a hold visited once.
- [x] Physics shapes `evidence: ShapeRole, ARoleDefaultsToHurtboxBecauseThatIsWhatAnArtistDrawsFirst, AShapeIsExportedWithItsRoleAndInsideTheCell`
  - Three roles rather than one flag, because an engine puts them on different layers and the artist is authoring that distinction rather than a detail of it. A physics body is usually a stable capsule that does not follow the drawing, which is precisely why it is not a hurtbox.
- [x] Export collision data `evidence: SheetShape, UnityCollider, Collider, AShapeIsExportedWithItsRoleAndInsideTheCell, PackingDoesNotMoveAShapeRelativeToItsCell, AHurtboxBelowTheFeetPivotArrivesWithANegativeYOffset, TrimmingCannotMoveACollider`
  - Generic sidecar: a `shapes` **list** per frame rather than a map, unlike `anchors`, because each entry carries a role an importer filters on and declaration order is meaningful to a person reading the file. Pixels, in the cell's own coordinates, like the pivot and the anchors — a collider that shifted because one frame's ink was tighter is a gameplay bug with no visible cause.
  - Unity: the same rectangle also arrives as a collider's `offset` and `size`, in world units measured from the sprite's pivot — the two numbers `BoxCollider2D` takes, so the shipped script assigns two `Vector2`s and computes nothing. **A fourth convention on top of the pivot's three**, and the memorable failure is the sign of Y: get it wrong and every hurtbox sits above the character's head. `UnityConvertTests` asserts both directions in one test, because a version that flipped everything passes a single-axis check.
  - **The cell rect does not appear in the collider arithmetic at all, and that is a result rather than an omission**: the rectangle and the pivot are both in document pixels, so the cell's origin cancels in the subtraction and trimming cannot move a collider by construction. Asserted anyway — union, per-frame, none and skyline all give the same offset — because "by construction" is a claim about the code. Where a document declares no pivot the exporter passes the cell's centre, since that is Unity's default sprite origin and it *is* cell-dependent.
  - **Colliders are handed over rather than attached.** A `Sprite` is an asset and a collider is a component, so there is nothing on the sliced sprite to set; the importer exposes `CollidersOf` and stays away from Unity's editor-internal sprite physics-shape provider, which is version-coupled and not what this data is.
- [x] Frame events `evidence: FrameMarker, IsEvent, ExportsAsEvent, AnUntouchedMarkerWritesNoEventKey, OnlyMarkersMarkedAsEventsAreExported`
  - **Built by not building it twice.** `Scene.Markers` already *is* a named point on a frame, so a frame event is a nullable flag on a marker rather than a parallel `FrameEvent` record — which would have been exactly the mistake Q11's "reusable animation presets" was struck for: a feature nothing can distinguish from a shipped one.
  - Opt-in, because the two uses genuinely differ: most markers are notes to the animator ("contact", "check the hand") and exporting those would fill an `AnimationClip` with callbacks nothing handles. Ticking it says *this one is for the game*.
- [x] Animation events `evidence: IsEvent, ExportsAsEvent, AnEventPastTheEndIsNotExported`
  - The same record. An "animation event" and a "frame event" were two names for one thing, and only one of them needed building.
- [x] Export animation tags `evidence: AnimationTag, TagDirection, ATagIsExportedAsAClipInTheEstablishedShape, ATagThatRanPastTheEndIsShortenedRatherThanLost, ATagRoundTripsWithItsDirectionAndLoop`
  - **A tag is genuinely new, and the reason is the one thing a marker cannot be: a marker is a point, a tag is a range.** Written in Aseprite's own `frameTags` key and field names, because every engine importer that reads a sprite-sheet sidecar already looks there. Direction and a loop flag come with it — Aseprite has no loop field and engines need one.
  - Frame **indices** rather than frame ids, unlike an anchor, and the difference is deliberate: an anchor belongs to a drawing, a tag names a stretch of the timeline. Re-timing is *meant* to move a tag's boundaries, because a clip is however long the animator made it.
  - A tag that ran past the end is **shortened rather than dropped** — it still names a real range, and losing the clip would be the worse answer. One entirely past the end names nothing and goes.
- [x] Export animation clips `evidence: AnimationTag, ATagIsExportedAsAClipInTheEstablishedShape, ATagEntirelyPastTheEndIsDropped`
  - There is no clip record to build: everything an engine calls a clip is a named frame range and nothing more, so tags *are* clips. This is also what makes one sheet holding several animations usable at all, which is why grouped export depends on tags and not the reverse.
- [x] Unity exporter — walking skeleton: sliced sprites, pivots, clips and events `evidence: UnityConvert, UnityExporter, UnityExportOptions, UnityConvertTests, UnityExportTests, AFeetPivotArrivesAsBottomCentreNormalised, ItNormalisesWithinTheTrimmedCellRatherThanTheCanvas, AnEventIsTimedFromItsOwnClipRatherThanFromTheSheet, NoMetaFileIsEverWritten, AnEditedImporterIsNotOverwritten`
  - **The decision that keeps it safe: Lightbox never writes or edits a Unity `.meta` file.** Unity owns those — GUIDs, version-specific YAML, rewritten by Unity — and hand-writing one is how asset importers corrupt projects. So Lightbox writes files and a small editor script does the Unity-side work through `TextureImporter` the way Unity intends. That rule has a test rather than a comment.
  - **The arithmetic is on our side, and the script computes nothing.** Three conventions differ at once — origin, direction, and normalised-within-the-sprite's-own-rect — and a flipped pivot looks like an animation problem and gets debugged as one. `UnityConvert` is pure and tested with worked examples, including the discriminating case: flipping before normalising agrees on a square cell and disagrees on every other one, which is exactly why that bug ships. Nothing is clamped to 0..1, because a character whose ground point sits below their lowest ink legitimately has a pivot below the rect.
  - **The `unity` block is additive.** The generic sidecar keeps every key it had, so Godot and Unreal read the same file, and an ordinary export still has no Unity block at all.
  - **Frame durations come from fps, not from the sidecar's rounded milliseconds.** At 12 fps the file says 83 ms and the truth is 83.33; over a hundred frames that is a third of a frame adrift, which desyncs against audio. Measured in the test rather than asserted — and the first version of that assertion compared the two per-frame numbers at three decimals, where they agree, and passed while proving nothing.
  - **An event's time is seconds from its own clip's start**, not the sheet's. Getting that wrong puts every event in a later clip out by the clip's offset, which reads as "events fire early" and is horrible to chase.
  - The importer ships as **source**, so it has no version coupling to Unity and can be read by whoever lives with it — and it is not overwritten once it exists, because somebody may well have adjusted it.
  - **Not verified inside Unity.** It references `UnityEditor` and cannot be compiled here, which is exactly why every number it consumes is computed and tested on this side. A real import is the remaining check.
  - **And the first version of it would have sliced nothing.** `TextureImporter.spritesheet` was removed in Unity 2022.2 and inert from 2021.2 — silently, which is the worst available failure. Now branches on `UNITY_2021_2_OR_NEWER` to `ISpriteEditorDataProvider` (`SpriteDataProviderFactories` → `InitSpriteEditorDataProvider` → `SetSpriteRects` → `Apply`), with a `spriteID` per rect that the old API had no field for. Needs the 2D Sprite package and says so rather than null-referencing. See the API-checking item below: this is what a write-only integration failing invisibly looks like.
- [x] MonoGame exporter — nothing to build, and that is the finding `evidence: SheetDocument, SheetFrame, SheetMeta, SpriteSheetExporter, TheSidecarIsAsepriteShaped`
  - **MonoGame has no sprite-sheet asset format.** You call `Texture2D` on a PNG and hand `Draw` a source rectangle you supply yourself, usually parsed from a JSON beside the texture. The generic exporter already writes exactly that pair, so a "MonoGame exporter" would be a file that renames our sidecar.
  - Marked built rather than left `[?]`, because `[?]` means *nobody has decided* and this has been decided: the answer is that the generic path is the whole feature. Its anchors are the sidecar's own, which is honest — if the sidecar changes shape, this box goes with it.
  - The Content Pipeline (`.mgcb`) is about *building* content, not about describing sprites, so it is not the missing half.
- [x] Raylib exporter — nothing to build, same reason `evidence: SheetDocument, SheetFrame, SpriteSheetExporter, TheSidecarIsAsepriteShaped`
  - `LoadTexture` plus `DrawTextureRec`, and no asset format whatsoever. A PNG and a rectangle list is the entire contract, and it is what we write.
- [x] Godot exporter — the sheet, the sidecar, and a GDScript importer `evidence: GodotConvert, GodotExporter, GodotExportOptions, FrameDuration, SpriteOffset, GodotConvertTests, GodotExportTests, NoTresFileIsEverWritten, RoundingRecoversTheValueTheMillisecondsWereRoundedFrom, TheScriptBuildsTheResourceThroughGodotsOwnApi, TheScriptNeverTouchesProjectGodotOrTheCache`
  - **Lightbox writes no `.tres`, and that reverses this item's own earlier reasoning.** It argued Godot was "the one engine whose asset format we can legitimately write, because `.tres` is plain text". That does not hold: **a format being text is not the same as knowing it.** The exact serialisation of a `SpriteFrames` resource — how the animations array is written, how a `StringName` is quoted, what `load_steps` must be, whether a `uid://` is now required — could not be verified here, because the network policy blocks the Godot documentation. Hand-writing a format from partial knowledge is precisely what produced the Unity importer that sliced nothing.
  - **So the pattern generalises: we write files and data; the engine's own API builds its asset.** The shipped GDScript calls `AtlasTexture`, `SpriteFrames` and `ResourceSaver`, so the `.tres` that lands is one Godot wrote — the format is Godot's business and stays correct across versions for free. This is now the rule for every engine, not a Godot workaround.
  - **The sidecar needs almost no Godot block**, unlike Unity's: regions, tags and fps are already in the generic file and are not duplicated, because two copies of a rect are two chances to disagree. Two things are converted on this side, where they can be tested.
  - **A Godot frame duration is a multiplier of the animation's speed, not a time.** Passing the sidecar's milliseconds through would run one frame for nearly seven seconds, which gets reported as "the importer hangs" rather than as a unit mistake.
  - **And it is rounded to a whole number of ticks, which was a real finding rather than a tidy-up.** Two of its own tests failed at 0.996 and 1.008: the sidecar stores integer milliseconds, so at 12 fps a one-tick frame is written as 83 where the truth is 83.33. A hold is by definition an integer number of frames, so rounding *recovers* the value the milliseconds were rounded from instead of carrying a 0.4% error into every frame — the Unity `SecondsPerFrame` lesson applied in the one place it still could be.
  - `Sprite2D.offset` is measured from the region's centre and Godot's 2D Y runs down like ours, so there is **no flip here** — which makes it exactly the conversion somebody later "simplifies" into a sign error. It has a discriminating test for that reason.
  - **It never touches `project.godot` or the `.godot/` cache**, which Godot owns the way Unity owns `.meta`, and the script skips every hidden folder while scanning. Rules with tests rather than comments.
  - An `EditorScript` rather than a plugin: a plugin needs a `plugin.cfg`, a directory layout and enabling in project settings, all of which would be things put into somebody's project uninvited.
  - **Not verified inside Godot.** Targets Godot 4 and says so in the script, because `SpriteFrames.add_frame` took no duration before 4.0 and the timing would be silently dropped on 3.x — the same shape of silent failure the API-checking item exists for. A real import is the remaining check.
- [x] GameMaker exporter — `_stripN` strips, which make `.yy` unnecessary `evidence: GameMakerConvert, GameMakerExporter, GameMakerConvertTests, GameMakerExportTests, TheNumberInTheNameIsTheNumberOfCellsInTheImage, ATagsStripHoldsTheFramesThatTagCoversAndNotTheOnesBeforeIt, APingPongTagIsReportedRatherThanQuietlyFlattened, NoProjectFileIsEverWritten`
  - **The question this was blocked on turned out to be the wrong question.** It asked which GameMaker versions to support when writing `.yy` — JSON, so writable in principle, but the schema moves between releases and carries IDs the IDE owns, and a file written for the wrong one gives a project that will not open. **The answer is not to write one.** GameMaker slices a strip whose filename ends `_strip8` into eight frames on import — honoured on drag into the IDE, through the sprite editor, and by `sprite_add()` at runtime. A naming rule has no schema to guess and no version to be wrong about.
  - So all five engines now land on the same principle from three different directions: **we do not write formats we cannot verify.** Unity, Godot and Unreal hand the work to an engine-side script; GameMaker is the one engine with no import-time scripting at all, and needs none.
  - **The layout is forced rather than offered, and that is a consequence.** The cell width is derived *by division*, so the strip must be one row of uniform cells with no padding. Packing, columns and padding are overridden and the export window hides them (`UsesStripLayout`) — showing a control the exporter then ignores is how an artist learns the controls lie. A per-frame trim is refused with a note; a `None` trim is still honoured, because it also gives uniform cells.
  - **Checked in pixels, not in options.** "We passed `Padding = 0`" is a different claim from "the width divides cleanly by the number in the filename", and only the second is the thing GameMaker relies on. The naming contract also has an inverse (`ReadStripName`) so it round-trips — a forward-only test agrees with an off-by-one.
  - **One strip per tag, because a GameMaker sprite is one animation** with no clip list to hold several. The strips are cut out of the single-row sheet rather than re-exported per tag: on one row a tag is a contiguous horizontal band, so a crop is exact, and it keeps this target out of the shared sheet exporter that four other targets depend on. The crop's off-by-one was verified by mutating it and watching the test fail.
  - **The single animation speed costs nothing, which is worth writing down** because it looks like it should. The sheet already writes one cell per timeline frame, so a drawing held on 2s is two identical cells — which is exactly how a strip expresses a hold. What GameMaker genuinely cannot do — reverse, ping-pong, not looping — is reported in the status line with what to do about it in code, rather than exported as though it worked.
  - **Not verified inside GameMaker.** The convention is documented behaviour rather than something we implement, and both halves of it (`_stripN` slicing, width by division) were confirmed before building. A real import is the remaining check.
- [x] Unreal Paper2D exporter — a sheet, a lean block, and an in-editor Python importer `evidence: UnrealConvert, UnrealExporter, UnrealConvertTests, UnrealExportTests, UnrealsFigureIsAHundredTimesUnitysBecauseAUnitIsACentimetre, TruncatingRatherThanRoundingWouldDeleteTheFrameEntirely, EveryPropertyWriteGoesThroughTheHelperThatCannotFailSilently, AndItActuallyCatchesEachOfThoseMistakes`
  - **The decision this item was waiting on was taken: an Unreal-side script is in scope.** `.uasset` is a binary serialized format and cannot be written from outside the editor, so there was never an alternative here — and by the time this was built, Godot had already arrived at the same shape for a different reason. *We write files and data; the engine's own API builds its asset* is now simply how engine export works, and Unreal is the case where it was forced rather than chosen.
  - **A world unit is a centimetre in Unreal and a metre in Unity, and that is the finding.** The same "how tall should this be" figure that gives a correct `pixelsPerUnit` for Unity is a hundred times wrong for Unreal — and wrong in the direction that produces a 1.8 cm character rather than an error. So `PixelsPerUnrealUnit` exists beside `UnityConvert.PixelsPerUnit` with a test asserting the factor of a hundred *between them*, so the two cannot quietly converge. The export window's field now says **metres** rather than "world units", because that is the only label that is unambiguous across both engines.
  - **`frame_run` is an integer count of frames, which makes the rounding worse than Godot's.** At 12 fps the sidecar writes 83 ms where a tick is 83.33, so the quotient is 0.996 — and truncating that gives **zero**, which is a drawing that never appears in the animation. Godot's equivalent mistake was a wrong duration; this one is a missing frame.
  - **`custom_pivot_point` is the plainest of the three engines' pivots and therefore the most dangerous**: rectangle-relative texture pixels, Y down — neither Unity's normalised pivot nor Godot's offset-from-centre. It has a test that computes *both* wrong answers and asserts they differ, because either one applied here compiles, exports, and puts every character's feet somewhere else.
  - **The script treats every property name as fallible, and that is its load-bearing design.** Paper2D's scripting surface is experimental and property names have moved between engine versions; a `set_editor_property` against a renamed name is exactly the failure that shipped a Unity importer which sliced nothing. Every write goes through one `_apply` helper that records what it could not set, and the run ends with `log_error` naming each one and saying the assets are incomplete. A test counts the bare writes so adding one later fails.
  - **Asset names are sanitised on the Lightbox side, not in the script.** Unreal object names reject the spaces and brackets an artist puts in a sheet name, and the first version implemented those rules twice — tested in C#, duplicated untested in Python. The sidecar now carries `assetBase` and a de-duplicated `flipbookNames` list, with one entry even for an untagged sheet so the script has no branch in which it invents a name.
  - **Mips off and nearest filtering**, which is Unreal's version of Godot's `filter_clip`: a lower mip averages across region boundaries, so a sliver of the neighbouring sprite lands on this one.
  - **The shipped Python is checked structurally on every build, and the check proves it discriminates.** It is not a parser and CI has no Python to be one, so it checks tabs, statement indentation at bracket depth zero, block colons and bracket balance — and a companion theory mutates the real script four ways, one per class of mistake, and asserts each is caught. A structural check that can only pass is worth nothing.
  - **Not verified inside Unreal.** The property surface was confirmed against the Python API documentation before a line was written — `source_texture`, `source_uv`, `source_dimension`, `pixels_per_unreal_unit`, `pivot_mode`, `custom_pivot_point`, `key_frames`, `frame_run`, `create_asset`, `AssetImportTask` — and `PaperSpriteFactory` was confirmed to expose *no* texture property, which is why the region is set after creation. What could not be confirmed is why `_apply` exists rather than being defensive style. A real import is the remaining check.
- [~] A project holds a game's assets, and export follows the artist's own folders `evidence: AssetFolder, ExportScope, ExportGrouping, AssetStatus, ProjectExportTests, AFolderTreeIsAbsentUntilOneIsMade, ExportingAFolderTakesEverythingUnderIt, OnlyReadyAssetsAreExportedWhenFiltered, AGroupedSheetSplitsRatherThanExceedingTheTextureLimit`
  - **Designed in `docs/DESIGN-project-assets.md`.** A project has one grouping axis today — `Character`, which earns its type by carrying a palette — so environments, props and UI have nowhere to go but a flat list. The answer is a **second, generic axis**: nestable `AssetFolder` with a nullable `Kind` hint, additive and absent until one is made.
  - **The exporter reads the tree rather than a fixed structure**, which is the whole of "dynamic rather than static": make a folder and the exporter can already export it, with no per-kind change ever. Scope is document / folder / selection / project; grouping is per-animation (default), per-folder, or one atlas; a path template gives a studio its own layout.
  - **Per animation is the default on the honest reasoning, not on file size.** More sheets is usually *more* total bytes, not fewer — each pays its own padding and rounding, and each is a separate texture bind at runtime. Per-animation wins because it is the simplest import, has no texture-size cliff, and Unity's own Sprite Atlas re-packs it at build time anyway. The real hazard of grouping is **exceeding max texture size** (8192 or 16384), so the grouped modes take a `MaxSheetSize` and split into numbered sheets rather than writing a file no engine will read.
  - **`DocumentRef.Status`** — Design, Draft, InDevelopment, Review, Ready, Reopened — on the *manifest*, not in the document, so marking something Ready cannot dirty the artwork or need the file open. The payoff is the export filter: *everything that is Ready* is what lets work-in-progress live in the same project as shipped art without shipping it by accident, and today the only way to get that is a second project. `Reopened` is kept distinct from `InDevelopment` on purpose — "this was Ready and is not any more" is the state a linear pipeline cannot express.
  - **Version control: be friendly to it, do not reimplement it.** The folder-of-JSON layout is already diffable and was chosen partly for that, with one real hole — `PaintedFrame.PngBase64` is a single enormous line, so a document carrying an imported baseline diffs unreadably. Measure that before promising anything; if it is as bad as it looks, baselines belong in sidecar PNGs and that is its own decision. Beyond that: **read-only git status per row** is cheap and safe, and stage/commit/push is deliberately last and optional, because every git edge case would become a support question in a drawing application and no panel can resolve a merge conflict in art.
- [ ] Version control the way game studios actually run it — locks, not merges `evidence: IVersionControl, VersionControlStatus, FileLock, PerforceClient, UnityVersionControlClient, GitClient, VersionControlTests, ALockedFileNamesWhoHasIt, AMissingClientIsAbsentRatherThanBroken, SavingToAReadOnlyFileAsksForACheckoutRatherThanThrowing`
  - **Designed in `docs/DESIGN-project-assets.md`, prompted by the question "could we connect to Unity's version control?".** Yes, and it fits better than git does.
  - **Three backends, not five.** Unity ships Unity Version Control (formerly Plastic SCM, driven by `cm`); Perforce/Helix Core (`p4`) is the industry default for game art; git is everywhere. **Unreal ships no VCS of its own** — its Revision Control integration talks to Perforce, Git, Subversion or Plastic — and Godot and GameMaker likewise use git through their own plugins. So one `IVersionControl` with one implementation per CLI covers every engine.
  - **Shell out to a client that is absent unless installed**, exactly the rule Laigter follows, and for a second reason as well: Perforce and UVCS are proprietary, so running their client as a separate process keeps the licences apart. Never link, never vendor, never ship a client.
  - **The realisation that reorders the whole item: locking is the feature, not history.** Git cannot say "I have this open, do not touch it". Perforce marks binary assets `+l` through a typemap — its own documentation names digital assets as the typical case and recommends that typemap when configuring against Unity, Unreal or Godot — and UVCS adds Smart Locks that check you are on the latest version first. Two artists opening the same walk cycle is the failure that costs a day, and a lock is the one thing a panel can prevent outright rather than help resolve.
  - **This retires a worry.** Under a locking workflow nobody merges a drawing, so `PngBase64`'s unreadable diff is a nuisance in git and nearly irrelevant in Perforce. Still worth measuring; no longer a reason to change the on-disk layout.
  - Order, and it is not the git order: **show lock state per row** (unlocked / yours / someone else's, with the name), then **take and release a lock plus check out**, then **submit** last and optional. Check-out is closer to essential than convenient: on Perforce a file is read-only until you have it, so an artist literally cannot save without it.
  - **The invasive part is the save path, not the panel.** Save, autosave and auto-export must expect a read-only target and say "check this out first" rather than throwing an IO error at an artist.
  - **Gluon is the mode these users are in** — UVCS's artist client checks out part of a tree rather than switching branches. An integration built around branch switching would be modelling the programmer's workflow, not the animator's.
  - **Lightbox does not own the workspace root**: a `.lbproj` normally sits inside or beside an engine project already under version control, so the enclosing workspace has to be discovered rather than assumed.
  - **Unverifiable here**, and the same shape as the Unity importer defect: a write-only integration against an external tool cannot fail visibly on this side. Each backend records its CLI, minimum version and commands, and gets one real run against a live server before its box is ticked — the second customer for the API-checking item above.
  - **Background handling is per folder, with a per-file override**, and the folder tree is what makes that expressible: characters never need a background, environments and backdrops often are one, props do not. The mechanism already exists — `BackgroundHandling` on the export and a three-state pin per layer — so what this adds is a default on `AssetFolder` that the export inherits, with the nearest ancestor winning. Not guessed ahead of the tree, because "which folder does this file belong to" is the question the tree answers and there is no point answering it twice.
  - Explicitly **not** a production tracker: no assignees, dates, comments or notifications. Status exists because the exporter needs it; the producer-facing view is a side effect.
- [x] The background stays out of the sheet, and says so `evidence: BackgroundHandling, BackgroundSignal, BackgroundRules, OmittedLayer, SuspectedBackground, OmitFromExport, SetLayerExportPin, BackgroundRulesTests, ExportPinTests, TheDefaultExportIsByteIdenticalToBeforeBackgroundHandlingExisted, AFloodedLayerIsOmittedUnderDetectionAndKeptWithoutIt, ALayerThatFillsTheCanvasOnOneFrameOnlyIsNotABackground, ALayerNamedLikeABackgroundIsReportedRatherThanRemoved`
  - **The gap was narrower and worse than it looked.** `ComposeFrame` already skipped the flagged paper layer, so a sprite sheet never carried the document's own background — which made the feature look done. What it did not skip is the layer an artist *makes*: add a layer, flood it grey so the line reads, and it exports. Every character sheet in the project.
  - **Detection reads pixels, not names.** A layer counts as a background when every drawing it shows covers ≥ 99.9% of the canvas. Not 1.0, because a flood on an antialiased canvas leaves soft edge pixels; not lower, because 0.99 of a 1920×1080 canvas is 20 000 pixels and that is a whole small drawing.
  - **Every drawing, not the first**, and that rule is the false positive it exists to prevent: a flash, a whip pan or an impact frame goes full-bleed for two frames and is art. A test animates one frame of a flooded layer and asserts the layer survives.
  - **The strong signal acts; the weak signal advises.** A name that reads like a background is a guess — "Sky" is scenery most days and the asset on others — so a name never removes anything. It raises a `SuspectedBackground` on a *kept* layer instead. The failure being designed against is not "the background got exported", which is visible in the sheet; it is "a layer the artist wanted is missing", which is invisible here and surfaces in the engine on a build.
  - **Nothing is dropped silently**: `SpriteSheetResult.OmittedLayers` names every layer and its reason, hidden layers included, so "why is my shadow missing" has a one-word answer.
  - **Three states per layer, not a checkbox** (`Layer.OmitFromExport`, nullable so an untouched document writes no key). *Never export* covers the reference photo and the colour check; *always export* is the escape hatch for the asset whose background **is** the point — a backdrop, a sky, a tiling floor — which was not exportable at all before, because the paper layer was unconditionally dropped. Reachable from the Layers docker context menu, one undo step, and it dirties the document because what leaves the app is document state.
  - **`PaperOnly` stays the default and its bytes are unchanged**, asserted as a byte comparison. A mode that started quietly removing layers from sheets that were fine yesterday would be the same defect from the other direction.
  - Per-folder defaults — characters never need one, environments might — belong with the asset folders below and are deliberately not guessed here. **Stray strokes are explicitly not solved**: a mark on the wrong layer is a drawing, and nothing can distinguish it from one that was meant. The report is the honest answer.
- [~] Every engine exporter is checked against that engine's current API `evidence: EngineApiNotes, EngineApiNote, EngineTarget, ImportMechanism, EngineApiTests, EverySymbolTheRecordNamesIsActuallyCalledByThatImporter, EveryEngineTargetHasARecord, NoImporterHasBeenRunAgainstTheRealEngineAndTheRecordSaysSo, EngineImportLog`
  - **Filed because it already caught a shipped defect.** The Unity importer used `TextureImporter.spritesheet`, which **stopped working in Unity 2021.2 and was removed in 2022.2**: assigning it throws nothing and slices nothing, so the import logs success and produces one sprite. Every Unity anyone is running would have silently ignored the export. Fixed by branching on `UNITY_2021_2_OR_NEWER` to `ISpriteEditorDataProvider` — `SpriteDataProviderFactories` → `InitSpriteEditorDataProvider` → `SetSpriteRects` → `Apply`, with a per-rect `spriteID = GUID.Generate()` that the old `SpriteMetaData` had no equivalent for and a mechanical port would drop.
  - **The lesson is about the shape of the mistake, not about Unity.** A write-only integration cannot fail visibly here: we produce a file, hand it over, and never see the other side. So "it compiles and the tests pass" says nothing about whether anything reads it. The standing rule is that every engine target names the API version it was written against and what it would do on a newer one.
  - Worse, a test of mine was *enforcing* the bug: it asserted the importer did **not** mention `SpriteDataProviderFactories`, on the assumption that was the physics-shape API. Asserting the absence of an API is only safe when you know what that API is for.
  - **Half of it has landed, and it is the half that can be automated.** `EngineApiNotes` records, per engine, the API surface the importer calls, the minimum version, what happens on an older one, and the deprecation risk — **as data rather than as prose**, because a note in a markdown file drifts from the code the first time somebody changes a call and nobody notices. `EngineApiTests` asserts every symbol named appears in the importer it belongs to, so the record and the code cannot disagree in either direction; the check was verified by adding a symbol nothing calls and watching it fail. It also asserts every engine target has a note, so a sixth engine cannot land without one.
  - **`VerifiedByRealImport` is a field on the record and is false on all four**, which is the honest state and is stated where the code can see it rather than as a caveat in a document. `UnverifiedIn` takes the set rather than only reading the static list, so a partially verified state is expressible and testable — over a static list, "the unverified ones" and "all of them" are the same collection, and an assertion that cannot tell them apart would pass on a query that ignored the flag.
  - **What is left is the part no amount of testing here can do: one real import per engine, by hand, recorded** — and `EngineImportLog` is named above as its evidence anchor precisely so this item cannot report itself finished on the strength of the half that was automatable. A green box here would mean the thing that would actually have caught the original defect had been done. That needs Unity, Godot, Unreal and GameMaker installed, and it is the only thing that would have caught the original defect. The item stays `[~]` until it is done.
  - The four notes as they stand: Unity 2021.2 with a compiled `#if` branch to the pre-2021.2 API and the highest risk after Unreal, because `ISpriteEditorDataProvider` lives in a package rather than the engine; Godot 4, low risk by construction since no `.tres` is written; Unreal 5.0, the highest risk of the four, handled by the script reporting every property it could not set; GameMaker 2.3, the lowest by a wide margin because there is no API at all, only a filename rule.
- [x] Asset status, and exporting because something was marked done `evidence: AssetStatus, AssetStatuses, AutoExport, AutoExportSettings, AutoExportOutcome, AutoExportReport, SetProjectStatus, AutoExportTests, AStatusRoundTripsAndAnUnsetOneWritesNoKey, MarkingSomethingReadyDoesNotTouchTheArtwork, ReSelectingTheStatusSomethingAlreadyHasDoesNotExport, AFailedExportIsAMessageRatherThanAnException`
  - **The workflow shortcut through the whole pillar.** Finish an asset, set it Ready, and the sheet and its sidecar land where the engine is already looking. The failure it removes is not a slow export — it is the export that *did not happen*, which makes a designer think the artist has not started.
  - **`DocumentRef.Status` on the manifest, never in the document**, and that is the load-bearing choice: marking something Ready must not dirty the artwork file, must not touch a pixel, and must not need the file open. A test asserts the document's bytes are unchanged across a status change. Nullable, so a project that never uses statuses writes no key and "nobody has said" stays distinct from Design — a project imported from loose files has no statuses, and claiming every file is at the start of a pipeline it was never in is a guess.
  - **The status change is authoritative; the export is a consequence.** Ready is written and saved first, then the export is attempted. A missing folder, a file the engine has locked, an unmounted drive — the artist keeps their status and gets a message. Refusing the status change because a file could not be written would make a production field hostage to a network share. `Run` never throws, for that reason alone.
  - **Off by default, and that is consent rather than timidity**: it writes files into somebody else's project on a UI click.
  - **Re-selecting the status something already has does nothing.** Opening the menu to check what a document is set to must not re-export — which is why `Decide` takes the previous value rather than only the new one.
  - The trigger status is configurable rather than hard-wired to Ready, because a studio that reviews in engine wants Review. When the engine should see an asset is a question about their pipeline, not ours.
  - `Decide` is pure and takes no disk, so every branch — disabled, unchanged, wrong status, no destination, relative-with-no-project — is tested without a project on disk or an engine to write into. A relative output folder resolves against the project root and is *refused* with no project rather than resolved against the working directory, which would write files somewhere nobody chose.
  - A configured preset that has since been deleted falls back to a working default rather than refusing to ship.
  - Explicitly **not** a production tracker: no assignees, dates or notifications. Status exists because the exporter needs it; the producer-facing colour dot is a side effect.
- [?] Modular exporter plugin system
- [x] One-click game-ready export `evidence: ExportPreset, ExportTarget, ExportPresetStore, ExportRunner, ExportRun, ExportWindow, DescribeExport, ExportWindowTests, APresetRoundTripsThroughTheFile, TheBuiltInsAreNeverWrittenToTheFile, AUnityPresetAlsoWritesTheImporterAndKeepsItsBlock, TheStatusLineNamesTheLayersItLeftOut, WhatDoesNotApplyIsHiddenRatherThanDisabled`
  - **The finding that made this the right item: none of Pillar 5 was reachable.** `SpriteSheetExporter` was called by `UnityExporter` and by tests, and by *nothing* in the interface. Five sessions of sheet writing, packing, sidecar, anchors, collision rectangles, tags, events and a Unity importer, all callable from an agent over MCP and from no menu an artist could press. The manual meanwhile listed "Export sprite sheet" in its command table, which is the worst version of the problem — a promise with nothing behind it.
  - **Presets are the feature, not a convenience.** "One click" is a claim about the *second* export: the first is six decisions, and with nowhere to keep them it is six decisions every time. Three built-ins so nobody has to derive "union trim, grid, detect the background" from first principles — each one a position on an argument this pillar already settled, asserted as such rather than left as taste.
  - **Built-ins are never written to the settings file**, the same rule `TimingPresetStore` follows: freezing them in on first launch would mean a later correction to "Character sprites" reached nobody.
  - **Controls that do not apply are hidden, not disabled** — the camera's rule applied to a dialog. A PNG sequence has no cells, no atlas and no sidecar, and offering it a layout picker teaches an artist that the controls lie. The window asks the *record* whether a group applies (`UsesSheetSettings`, `UsesEngineSettings`) so the two cannot disagree.
  - **The report is in the status line, by name.** "2 layers left out" is exactly as unhelpful as silence when the question is *which*, so `DescribeExport` names each layer and its reason. That is what turns "a layer I wanted is missing" from something discovered in the engine on a build into something noticed on export.
  - **A bug caught while writing the runner**: reading the omitted-layer report for a Unity export by exporting a second time would have rewritten the sidecar and stripped the `unity` block back off it. `UnityExportResult` now carries the sheet result so there is one export and one account of it.
  - Settings first, path second, because asking for a filename before a format is how somebody ends up with a `.png` holding a PNG sequence's folder name. A garbled number falls back rather than refusing the export — with a test that types text rather than setting the value, since setting it and reading it back would pass on a window that cannot parse at all.
  - **Project- and folder-scope export is not here**: scope needs the asset folder tree, and the document is the primitive until it exists. Everything in this cut is one document.

### Game-specific optimization

- [ ] Procedural directional generation — automatically create 4-dir / 8-dir variants from a base cycle `evidence: DirectionalVariantGenerator, RotationCompositor, DirectionalExportTests, ABaseWalkCanBeRotatedIntoFourDirectionalVariants, LightingAdjustsPerDirection, ConsistencyIsMaintainedAcrossDirections`
  - **HIGH-VALUE, MARKET-VALIDATED.** Game dev research (2026) shows directional variants consume 70% of sprite production time: manually creating 8 views × 24 frames = 192 manual frames per character cycle. Auto-generation via rotation + perspective/lighting adjustment would transform this bottleneck.
  - **Effort:** Medium (~600 LOC + art direction)
  - **Impact:** Eliminates the single largest sprite bottleneck in 2D game dev
  - **How it works:** Take one cycle (walk forward), rotate character 45°/90°/135°/180°, re-light per direction, export 4-/8-directional sheets
  - **Blocker:** None — independent, builds on existing export system
  - **Note:** This is unique to game animation; no competitor offers it

- [ ] Live engine preview (hot-reload) — push changes to running game without rebuild cycle `evidence: EngineHotReload, WebSocketBridge, UnityWatcher, GodotWatcher, HotReloadTests, ExportedAssetsReflectInRunningGame, ChangesAppearWithin2Seconds`
  - **HIGH-VALUE, MARKET-VALIDATED.** Studios cite "export/reimport cycle kills animation rhythm" as a core pain point. Animators iterate on their own art; breaking focus for a 30s rebuild per change is expensive.
  - **Effort:** High (~800 LOC + engine plugin per target)
  - **Impact:** Transforms iteration speed; competitive differentiator
  - **How it works:** WebSocket connection from Lightbox to running Unity/Godot editor. On sprite export, push directly to asset instead of writing file. Running game receives asset reload message and updates display.
  - **Blocker:** None — independent
  - **Security:** Only active in development; disabled in release builds
  - **Test:** Asset changes appear in running game within 2 seconds of export

## Pillar 6 — Production-focused workflow

Assets, animations and scenes as first-class citizens rather than layers on a
canvas. Also the home for everything that keeps a long project alive:
timeline, review, versioning, collaboration.

**Strategic gap noted:** Request analysis identified that production review and
collaboration features (comments, version history, storyboard workflow) are
largely unbuilt `[?]` and represent a gap between the game-export focus of
Pillar 5 and studio workflows. High-value items for prioritization: **Undo
history browser** and **Version snapshots** (project plumbing), **Frame
comments** (collaboration), and **Storyboard view with beat-to-timeline
conversion** (missing entirely). See `scripts/bugs.py` B83-B87 for related
project structure bugs and `.claude/quality/comparison.md` for full analysis.

### Timeline and exposure

- [x] Multi-layer timeline `evidence: LayerRow, FrameCell, TimelineExpansionTests`
- [x] Exposure sheets (X-sheet) `evidence: ExposureSheet, ExposureTests`
- [x] Timeline scrubbing `evidence: TimelineExpansionTests, CurrentFrameIndex`
- [x] Playback speed control `evidence: PlaybackSpeedTests, PlaybackClock`
- [x] Loop regions `evidence: TimelineRuler, DraggingTheStartHandleSetsTheStartFrame, DraggingTheEndHandleSetsTheEndFrame, AltClickingBetweenThemResetsItToo, WithLoopingOffItStopsOnTheLastFrame`
- [x] Frame markers `evidence: FrameMarker`
- [x] Automatic frame numbering `evidence: FrameLabel, FrameCell`
- [x] ~~Frame tagging~~ — this is **frame markers**, shipped above, plus P5d's event flag `evidence: FrameMarker, IsEvent, ExportsAsEvent, AMarkerWithNoNoteWritesNoNoteKey`
  - Struck rather than built. A "frame tag" is a named point on a frame, which `FrameMarker` has been since M9c; the only thing it was missing was a way to say *this one is for the game*, and P5d added that as a flag. An item nothing can distinguish from a shipped feature is the wish list the checkbox rules exist to prevent — the same reason Q11's "reusable animation presets" went.
- [x] ~~Animation tagging~~ — this is **`AnimationTag`**, built in P5d `evidence: AnimationTag, TagDirection, ATagCarriesProseToo`
  - Also struck. Pillar 5 needed named frame ranges to make a multi-animation atlas usable, so it built them; this item and that requirement were the same feature approached from the timeline's side and the exporter's.
- [x] ~~Timeline bookmarks~~ — a bookmark **is** a marker, and what was missing was navigation `evidence: GoToNextMarker, GoToPreviousMarker, TheMarkersCanBeWalkedForwardsAndBackwards, WalkingPastTheLastMarkerStaysPutRatherThanWrapping`
  - The useful half was hiding inside a duplicate. Markers have existed since M9c with **no way to reach one**, so on a long sheet they were labels you hunted for by eye. Next/previous marker is what "bookmarks" actually wanted, and it is now there; walking past the last one stays put rather than wrapping, because wrapping moves the playhead somewhere the artist did not ask for and they would not see where it went.
- [x] Animation notes `evidence: Note, HasNote, SetMarkerNoteAt, Notes, WritingANoteOnAnUnmarkedFrameMakesTheMarker, ANoteSurvivesRenamingTheMarker, ANoteIsNotAnEvent, ATagCarriesProseToo`
  - **The one of the four that was genuinely different**, and the difference is precise: a `Label` is a chip on the ruler and has to stay short to fit, a note is "the hand pops here, fix it on 2s" — several lines if it wants, shown in a list and on hover, never drawn as a chip and **never exported as an event**.
  - Built as a nullable `Note` on `FrameMarker` *and* on `AnimationTag`, so prose can attach to a point or to a range, with no new record either time. A separate note type would have made an artist choose between two near-identical features before knowing the difference, and forced the UI to explain both. **Discoverability is solved by presenting one mechanism well, not by shipping two.**
  - **A latent defect found on the way in.** `SetMarkerAt` replaces the marker at a frame, so renaming one would have silently thrown away its note *and* un-exported its engine event — a deletion disguised as an edit, and the kind only noticed much later. Both are carried across now, with a test that fails if they stop being.
- [?] Timeline scrubbing with audio
- [?] Audio waveform

### Comparison and review

- [?] Flipbook comparison
- [?] Split-screen frame comparison
- [?] Side-by-side animation comparison
- [?] Pin any frame as reference
- [x] Floating reference windows `evidence: ReferenceBoardWindow, ReferenceBoardWindowTests`
  - Answered as **one board rather than many windows** — see *the reference
    board* under the reference section below (Q87).
- [?] Pose references
- [x] Imported animation reference, sliced into frames and laid against the timeline `evidence: ReferenceStrip, StripSlicer, StripSlicerTests, ReferenceStripTests`
- [x] An animated symbol — a stored cycle, not a single drawing `evidence: Symbol, SymbolPlacement, FrameIndexAt, FrameOffset, SymbolRecordTests, SymbolRenderTests`
  - **Already built, and worth recording as built rather than re-asked.** `Symbol.Frames` is a list, `Symbol.Fps` sets its rate, a placement advances with the timeline, and `FrameOffset` shifts where in the cycle it starts — so one stored walk carries two characters half a stride apart. Editing one opens a cel per frame. "An animation symbol" is a symbol with more than one frame in it, not a feature to add.
- [ ] Storyboard view with beat-to-timeline conversion — sketch and layout sequences visually, then convert to timeline frames `evidence: StoryboardPanel, StoryboardLayout, StoryboardThumbnails, BeatToFrameConversion, StoryboardTests, AStoryboardCanBeSketchedWithOneCelPerBeat, ConvertingABeatToATimelineFramePreservesTheDrawings`
  - **High-value, high-effort.** Explicitly requested in Request 1 feature analysis as a missing production workflow. Acts as a director-friendly rough-layout view before committing to full timeline: one cel per beat/moment, laid out visually, with thumbnails for pacing. The conversion step takes a storyboard and produces corresponding timeline frames with matching content and exposure. Distinct from the timeline — the storyboard is ephemeral, the timeline is the record. Foundational for studio and film workflows where storyboards precede animatic assembly.
- [ ] A reference layer: draw over a reusable animation that never exports `evidence: LayerRole, ReferenceLayerTests, AReferenceLayerIsNeverExported, AReferenceLayerRendersAsAGhostThroughTheOnionPath`
  - **The mechanism is already assembled; what is missing is presentation.** An animated symbol placed on a layer with `OmitFromExport = true` is *today* a reusable, live-updating, non-exporting animated underlay: edit the base run cycle once and every shot drawn over it follows, export and it is not there. Three existing parts — `Symbol` with several frames, `Layer.OmitFromExport`, and the onion-skin render path — cover it.
  - So this item is deliberately **not** a new record. It is a nullable `Layer.Role` (absent until used) with one value, `Reference`, that renders through the onion path, implies the export pin without replacing it, and shows a badge in the Layers docker. The three failures it fixes are that an underlay currently reads as artwork, that nothing points at the workflow so an artist would have to invent the recipe, and that "this layer is a guide" and "do not export this layer" are one setting wearing the wrong label.
  - **Not locked.** Roughing on the reference before committing on a clean layer is a real way to work, and locking it would make the feature narrower than the manual underlay it replaces.
  - **Ghost rendering reuses the onion-skin path** rather than a second tint implementation — there is one way to draw "this is not the current drawing" and two would drift.
  - The payoff is the base-character workflow: an unstyled, line-only rig whose cycles are drawn over per shot and updated centrally. One global symbol; every animation that uses it is a layer pointing at it. Designed in `docs/DESIGN-symbols.md`.
  - Explicitly **not** a parallel `ReferenceAnimation` record — that is the mistake Q11's "reusable animation presets" was struck for. Nor onion skin *of* a reference layer, which is a ghost of a ghost.

### Camera and scene

- [x] Camera, optional and absent by default `evidence: Camera, CameraOps, CameraTests`
- [x] Keyframed pan, zoom and roll `evidence: CameraKey, CameraOps, CameraCompositingTests`
- [x] Camera preview / view through camera `evidence: CameraViewModelTests, CameraTransform, ViewThroughCamera`
- [x] Export through the camera `evidence: CameraExportTests, SequenceExporter`
Scoped by Q84 (2026-08-14): the four wishes here were one real feature, one
duplicate of something shipped, one wish whose name hid a design decision, and
one authoring surface belonging to multiplane. The owner's question on reading
them — *would animation pegs fit here as well?* — turned out to be the largest
item in the section, and the only one touching work in flight.

- [ ] Safe area guides — action-safe and title-safe as nullable percentages on the camera, painted with the frame overlay `evidence: SafeArea, SafeAreaPainter, SafeAreaTests, SafeAreasFollowTheCameraThroughAPushAndARoll, ACameraWithoutSafeAreasWritesNoKey, ASafeAreaNeverGrabsAStrokesDirection`
  - **Not a guide, despite the name** — the trap worth recording, because the wish's own wording points at the wrong mechanism. A `Guide` lives in document coordinates and snaps strokes; a safe area is a *fraction of the camera's output rect* that has to follow the camera through a pan, a push and a roll. Built as `Line` guides it would sit still while the camera moved, and it would start grabbing linework — a compositional boundary for the eye that constrains input is a defect, not a feature.
  - The numbers live on the **camera** (Q84), nullable so a shot that never asks writes no key, because a delivery spec travels with the shot — broadcast and a web short want different safes — and `Camera` already carries `OutputWidth`/`OutputHeight`, which is the same kind of fact. Whether they are *shown* is a view toggle. A pure view preference was declined: the spec is then lost the moment the shot is handed to somebody else. HD convention is 93% action / 90% title; the legacy 4:3 pair is 90/80.
  - Cost S.
- [x] ~~Zoom preview~~ — this is **camera preview / view through camera**, shipped above `evidence: CameraViewModelTests, CameraTransform, ViewThroughCamera`
  - Struck rather than built (Q84), on the Frame-tagging and Timeline-bookmarks precedent: an item nothing can distinguish from a shipped feature is the wish list the checkbox rules exist to prevent. The competing reading — that this and *scene preview* were both delivery-quality preview render, a playblast — was considered and declined, because ordinary playback plus view-through-camera covers what it was for and a cached render tier is its own design.
- [ ] Camera shake — a deterministic modifier on the camera, not baked keys `evidence: CameraShake, CameraShakeTests, ShakeIsIdenticalOnEveryRender, ACameraWithoutShakeWritesNoKey, ShakeLeavesTheAuthoredMoveEditable`
  - **Invariant 2 makes this better than the field's version rather than taxing it.** Shake is canonically an RNG, which is forbidden here — and a shake nobody can reproduce cannot be re-rendered at 4K, cannot be handed to anyone, and diverges between a preview and a delivery. `Camera.Shake?` (amplitude, frequency, decay) is evaluated inside `CameraOps.At` with its offset seeded from the frame through `Hash01`, so it is identical every time. Seeding from the frame index is legitimate *here* where it would not be for a dab: there is exactly one camera, so nothing can flicker relative to a sibling.
  - A **render-time modifier**, not baked keys (Q84) — which is also why the wish's *preview* half costs nothing, since ordinary playback shows it. Baking would put 24 keys a second into the graph editor and make the underlying move unrecoverable, so re-tuning amplitude would mean undo and re-apply. A bake command stays available later as an addition rather than a replacement.
  - Cost S–M.
- [x] ~~Scene preview~~ — this is the **Scene panel**, absorbed into *multiplane parallax* below as the authoring surface stage 1 otherwise lacks `evidence: ScenePanel, LayerDepthRow, CameraPathPlot`
  - `docs/DESIGN-3d-space.md` already names "the Scene panel" as the thing shot-flavoured project types default visible, and stage 1 has nowhere to author a per-layer depth or see the camera's path. A schematic of the stack with depths, plus the path, is that surface — so this is one item's missing half rather than an item of its own (Q84).
- [ ] Animation pegs — a keyframed transform layers attach to, sharing the camera's interpolation `evidence: Peg, PegKey, PegOps, PegTests, APegMovesTheLayerSurfaceNotTheGeometry, ALayerWithoutAPegWritesNoKey, SeveralLayersOnOnePegMoveAsOne, ARigHangsOffItsMasterPeg, PeggedMotionIsIdenticalOnEveryRender`
  - **The camera's counterpart, and that is why it belongs in this section rather than beside the guides.** The camera is already the proof that an authored, keyframed, interpolated transform can live in this document without touching a stroke (invariant 5's second half); a peg is that same record pointed at layers instead of at the view. The traditional origin makes the pairing literal — sliding the peg bar under the camera is how a pan was done — and camera-moves-view / peg-moves-artwork are the two halves of one idea. `CameraOps.At` already supplies the three things the solve needs: hold outside the authored range rather than extrapolate, easing from the vocabulary the inbetweener shares, and geometric interpolation for scale because scale is a ratio.
  - **A separate record over shared ops** (Q84), Toon Boom's arrangement: `Peg`/`PegKey` reusing the interpolation shape `CameraOps` has, with `Armature.PegId?` hanging a rig off a master peg. A peg hierarchy and a bone hierarchy *are* the same data structure — named nodes, a parent, a keyed transform — and `Doc.Armature`, `Scene.PoseTrack` and `ArmatureOps`' FK solve already exist, so this was a real risk of building the cutout workflow twice. One `TransformNode` type is cleaner on paper and was declined for timing: it buys a unification shared ops already deliver, at the price of merging two records while the bone system is mid-flight. Waiting for bones entirely was declined outright — a shot cannot pan a background until cost-L skinning lands, and rigging a skeleton to slide a background layer is the wrong shape of work for the commonest camera-department job there is. The standing obligation, sharpened by phase 2's UI landing the same day (Q81): **coarse assignment already ships**, so a rigged character's rigid part movement is covered and the peg must not become a second way to do it. The peg's territory is the layer with *no* armature and no weights — a background pan, which today would mean creating an armature and binding strokes to slide a painting — and where a character *is* rigged, the master peg carries the whole rig via `Armature.PegId?` rather than strokes being re-bound. Both hierarchies keep sharing one graph editor or they drift.
  - **One question to ask when this starts rather than guess now:** does a peg auto-key at the playhead like a bone, or take explicit keys like the camera? Q81 decision 2 made those two deliberately different — posing is the high-frequency act and got the one-step gesture, the camera kept its explicit keys — and a peg sits between them: a pan is authored deliberately like a camera move, but dragging one is as frequent as posing. Not guessed here.
  - **Its own record because the point of a peg is that layers move together.** `Layer.PegId?` (nullable, absent until used) rather than a transform track per layer — a background and its overlay pan as one thing, and five parallel tracks would drift.
  - **Invariant 7 is absolute here.** A pegged layer is rasterised and then drawn through a matrix; its stroke coordinates are never multiplied. `Hash01` seeds every dab dynamic from the IEEE-754 bits of a position, so moving geometry would re-roll scatter, size, flow and the colour jitters every frame and the layer would **boil** as it slid. This is the same trap `DESIGN-bones.md` names and the same one `OutputScaleTests` keeps written down.
  - **One implementation point shared with multiplane** — an optional matrix on the layer's rasterised pass. Pegs are cheaper and useful with no camera at all, so pegs build that slot and multiplane composes into it rather than the reverse.
  - **The one place this breaks the section's free-for-assets pattern.** Depth without a camera does nothing, which is why multiplane never taxes an asset document; a peg without a camera *does* something, because it moves content on the canvas, so it exports. That is correct — an artist who pegs a layer meant to — and it is recorded because every other item here is free for the asset target and this one is not.
  - **Invariant 5 needs rewording when this lands.** `CLAUDE.md` says the camera "is the one transform that is not" view-only; a peg is authored, keyframed, saved and exported on exactly the same terms, so that sentence becomes two transforms in the same commit rather than afterwards.
  - Where it shows up, per the registries: peg curves in the **graph editor** (which already plots camera position, zoom and rotation with a legend), a peg tool in **`ShortcutMap`**, and the peg as a document-level capability on the **MCP surface**. Keying a peg independently of the exposure sheet is the point rather than a conflict — a drawing on 2s under a peg moving on 1s is a moving hold.
  - Cost M: the record and ops are S given `CameraOps`; the gizmo, the graph-editor curves, layer attachment and the compositor's matrix slot are the bulk.
- [x] Multiplane parallax (per-layer depth) — stage 1 of the 3D drawing space, with the Scene panel it is authored in `evidence: LayerDepth, ParallaxTransform, MultiplaneParallaxTests, ScenePanel, LayerDepthRow, CameraPathPlot, ADepthLeftAtItsDefaultWritesNoKey, ADocumentWithoutACameraIgnoresDepth, PanningTheCameraMovesADeepLayerLess, ExportThroughTheCameraCarriesParallax`
  - Specified in `docs/DESIGN-3d-space.md` (Q72, 2026-08-12 — the direction the infinite canvas was removed for). Layers gain a depth; the camera stays today's 2D record; parallax is the depth-dependent response `f/(f+depth)` to camera moves. Depth without a camera does nothing, so assets are untouched by construction and no feature conflict exists.
  - Carries the **Scene panel** (Q84, absorbing the *scene preview* wish above): a schematic of the layer stack with its depths and the camera's path, which is where a depth is authored at all. Composes into the per-layer matrix slot *animation pegs* builds.
  - The record is designed as the degenerate case of stage 2's pose, so a stage-1 document opens unchanged when free planes land. Parallax changes per-layer *matrices*, never allocation — the performance shape of a plain camera pan.
- [ ] Free planes and orbit — stage 2 of the 3D drawing space `evidence: PlanePose, OrbitNavigation, PlaneProjection, PlaneProjectionTests, DrawingThroughAnOrbitedViewLandsOnThePlane, AnOrbitIsNeverSerialized, APoseLeftAtItsDefaultWritesNoKey`
  - Depth generalises to a pose (position, orientation, scale); the working view gains a view-only orbit (invariant 5 — never serialised, never exported); the camera gains an authored 3D pose. Strokes never learn 3D exists: drawing through a tilted view unprojects the pointer to the active plane and re-enters today's input pipeline in plane-local 2D.
  - Planes are rasterised flat (invariant 7 — surface scale, not geometry) and drawn through a homography; painter's-algorithm by camera distance; **intersecting planes are permanently out of scope**. Stage-2 open questions (pose interpolation space, grazing-angle UX, orbit shortcut) are asked when it starts — `docs/DESIGN-3d-space.md` lists them.

### Construction guides

Scoped by Q79 (2026-08-13): the eight wishes here were two different features
wearing one name. The **authored drawing aids** — marks an artist places, which
ride the guide machinery and the guide-set rails above — are built. The
**computed analysis overlays** — checkers that read the drawing and report on
it — stay open wishes, each carrying its design note so the next reader does
not re-derive it.

- [x] Character height guide — a `GuideKind.HeightScale` of its own: one object that is "6 heads", top-dragged to resize with the divisions following, division lines that snap, edited exactly in Configure ▸ Guides and grid `evidence: HeightScaleRow, AHeightScalePullsToItsDivisionLines, AHeightScaleNeverGrabsAStrokesDirection, OnlyAGuideThatCountsSomethingWritesADivisionsKey, PullingAHeightScalesTopResizesTheHeadsAsOneUndoStep, AHeightScaleDrawsItsPostAndItsRungs, AHeightScaleSaysHowManyHeadsItIs`
  - A kind rather than labelled lines for the isometric guide's reason: the artist reaches for "six heads" and expects the divisions to follow the top; seven hand-kept lines is the same picture and seven times the housekeeping. The cheaper route — named `Line` guides ("crown", "eyeline", "ground") in a `GuideSet` on the character's folder, performed rather than built — was a parallel design note here and is what Q79 explicitly declined: it leaves resizing a character a seven-line chore and nothing knowing to label the chart. Deliberately *not* a stroke constraint — it pulls points onto its division lines but offers no directions, or every horizontal stroke on the canvas would belong to it.
- [x] Adjustable guides — the Move tool selects a guide, and the tool-options bar and docker carry its numbers: X/Y for any guide, a grid's cell size, a height scale's head height and head count, a vanishing point's ray count, and one "Set as default" that writes those back as what the next guide of that kind is made from `evidence: GuideOptionsBar, GuideOptionsPanel, GuideOptionsTests, SelectingAGuideSaysWhichNumbersItHas, TypingAPositionMovesTheGuideAndUndoPutsItBack, AGridsCellSizeIsTheDocumentsAndNotThePreference, SettingAGridAsDefaultChangesWhatTheNextGridIsMadeFrom, AHeightScaleSavesAProportionRatherThanAPixelHeight, EachStepOfEmphasisReadsStrongerThanTheOneBelowIt`
  - **The half the guide machinery shipped without.** A guide could be dragged and not adjusted: a grid's pitch and a height scale's proportions were two menus away in Configure, and a vanishing point's fan was hard-coded at twenty-four and reachable from nowhere. The Move tool is the way in because it already picks guides up and had no options of its own, which makes "move it" and "change it" one reach instead of two.
  - **Per guide, per document, undoable — the default is a separate button.** Every field edits the selected guide alone; nothing here reaches a preference until "Set as default" is pressed, because a default that rewrote itself on every nudge would not be one. A height scale saves a *proportion* of the canvas rather than a pixel height, so the same default still lands as a figure on a scene of another size.
  - **The ray count rides `Guide.Divisions` rather than a key of its own** (owner, 2026-08-14): the field already means "how many of them", it is already nullable, and a vanishing point nobody has dialled still writes nothing. It is a drawing property — the point constrains every direction through it whatever the fan shows — which `TheRayCountChangesNothingAboutWhatAVanishingPointConstrains` pins so "show me fewer lines" can never quietly change where a stroke lands.
  - **Three levels of emphasis, because they answer three questions**: can anything here be picked up, would *this* one come up, which one am I changing. A grid and an isometric rig also grow an anchor handle while the Move tool is in hand — they are grabbed at a single invisible point, so without it the affordance was a sentence in the manual. The grid's lattice deliberately does not brighten at the ambient level: a whole-canvas wash is not a hint.
- [x] Eye-line guide — a horizontal ruler that wears its name on the canvas, placed pre-named from View ▸ Guides `evidence: OnAddEyeLine, ANamedGuideWearsItsName`
- [x] Horizon guide — the same mechanism, at the height the vanishing points already assume `evidence: OnAddHorizonLine, ANamedGuideWearsItsName`
  - Both folded into `Line` plus label rendering (Q79) rather than kinds of their own: they *are* horizontal lines, and the label — which the height scale needed anyway — was the whole missing part. The label paints for any named guide, so a rig pulled from a guide set reads at a glance. The deferred growth, noted on both sides of the fork that merged here: a `Horizon` kind the perspective rig takes its vanishing points from — real machinery, costed M, waiting for somebody to miss it.
- [x] Automatic volume guides — the volume checker, built to the design note: alpha-weighted ink area per frame (the 0th moment), a bar band on the timeline, frames flagged when they drift past the tolerance from the shot's median `evidence: InkMoments, InkMomentsTests, SquashPreservesVolumeAndTheMeasurementAgrees, TheDrawingThatLostItsMassIsTheOneFlagged, AHoldReadsExactlyAsTheDrawingItShows, ATighterToleranceFlagsWhatALooserOneForgives`
  - The segmentation problem resolved as the note proposed, one notch simpler: v1 measures the **active layer** — the strongest selection statement the UI has today — rather than a multi-select it doesn't. Holds are measured once by `Frame.Id`, the measuring render is capped at 512 px on its long side (drift ratios are scale-free), and the recompute is posted at background priority off the commit hook, so it never adds to pen-lift latency. Tolerance is a preference (`Configure ▸ Timeline`), default 10%.
- [x] Center of mass visualization — the 1st moment of the same pass: a dot per frame with its arc, onion-skin style, the current frame's dot emphasised, flagged frames warm `evidence: BalanceOverlayPainter, BalanceOverlayPainterTests, TheCentroidLandsOnTheMark, TheCentroidFollowsTheMassNotTheCanvas, EveryFrameGetsADotAndTheArcConnectsThem, TheCurrentFrameIsTheEmphaticOne, TheReadingsReachBothSurfaces`
  - The uniform-density caveat lives in the menu item's tooltip and the manual, as the design note said it should — not in a density model.
- [?] Perspective consistency guide — checking strokes against the VP rig means deciding which strokes *claim* to be perspective lines, which is a labelling problem before it is geometry.
- [?] Limb length guide — an animation checker, not a guide: per-frame distances between named points, compared across the sheet, flagging segments that drift. **Waits for the bone system, deliberately** (owner, 2026-08-13): `docs/DESIGN-bones.md` has anchors riding bones, which makes the per-frame annotation labour — the real cost this entry always named — free once a character is rigged. Measuring hand-placed `Anchor` pairs today would build a surface the bones work immediately obsoletes.

### Project plumbing

- [x] Autosave `evidence: AutosaveService`
- [x] Custom shortcuts `evidence: ShortcutMap, ShortcutMapTests, ConfigureWindow`
- [x] Context-aware shortcuts `evidence: ShortcutContext, ContextShortcutTests`
- [x] Undo history browser — the History docker: every step named, current state marked, double-click to jump `evidence: UndoHistoryViewModel, UndoHistoryRow, UndoHistoryTests, UndoHistoryPanelTests, NavigatingTheHistoryRestoresTheState, UndoneStepsStayInTheHistoryMarkedAsAhead, ATrimmedHistorySaysSoAndJumpStopsAtTheOldestStep, JumpReportsOneFrameWhenEveryStepAgreed`
  - Landed 2026-08-13, and **smaller than this item planned, on purpose.** The plan said "each snapshot captures document bytes … with state preview" — and the item below it measured what that costs: a 64-deep byte stack is a 64× memory multiplier. So the panel shows *names*, not previews: every step arrives labelled via `CallerMemberName` (humanized, ~60 call sites untouched; the handful whose method name reads badly pass an explicit label), `DocumentEditor.History` exposes both stacks as one chronological list, and `JumpTo` walks undo/redo to a clicked row through the same cache-invalidation scopes a single Ctrl+Z uses.
  - Honest at the edges: once `MaxUndo` trims, the "As opened" row is withdrawn rather than promising a state undo cannot reach, and a jump merges the frames the walked steps touched so a two-stroke hop invalidates one frame, not the canvas.
  - Session-only. Persisting the record is B100, blocked on *the undo record becomes data* below — the panel neither waits for that nor changes it.
- [ ] The undo record becomes data instead of lambdas `evidence: EditStep, StrokeEditStep, SerializableEditStep, EditStepTests, ADeltaStepRoundTripsThroughJson, ReplayingAStepMatchesTheOriginalEdit, TheHotPathStillDoesNotSnapshot`
  - **The prerequisite three separate features turn out to share, found by measuring B100 rather than by design.** `DocumentEditor` keeps two kinds of step: `SnapshotStep`, holding a whole `Doc` clone, and `DeltaStep`, holding a pair of `Action<Doc>` delegates. Neither can be written to a file — the first because of what it costs, the second because a closure has no data form.
  - **Measured 2026-08-06.** A 20-stroke document is 45.9 KB and a 64-deep snapshot stack is **2.8 MB, a 64× multiplier**; a 60-stroke document puts it near 8 MB. That cost is paid in *memory* today whether or not anything is ever persisted, which is why this is worth doing on its own terms.
  - **The delegates are the harder half and the reason B100 is blocked.** A stroke commit is a delta — deliberately, because snapshotting per pen lift caused the pause the hot path exists to avoid — so *undo my last stroke* is precisely the step that cannot be recorded. Each of the 16 delta sites has an apply/revert pair that is representable as data; the work is a discriminated set of step records, not a serializer.
  - **What it unblocks:** B100 (persisting undo history, which the owner asked for), the *undo history browser* below, and *version snapshots* beside it — all three currently assume a record that can be read back. `DocumentEditor`'s own doc comment already anticipates it: *"to be replaced by command deltas when heavy raster editing arrives."*
  - Non-negotiable while doing it: the stroke path must not start snapshotting. `TheHotPathStillDoesNotSnapshot` is the guard, and the performance budgets are the backstop.
- [x] Version snapshots — authored version history for documents and character sheets, with milestone capture and revert (Q75) `evidence: FileVersionHistoryStore, ProjectVersions, VersionHistoryViewModel, FileVersionHistoryStoreTests, ProjectVersionsTests, VersionHistoryTests, RevertRestoresTheBytesAndVersionsTheCurrentStateFirst, PromotingToReadyKeepsAMilestoneVersion, AProjectThatNeverVersionsWritesNoVersionsFolder, ARecordOnlyEntryCannotBeRevertedTo`
  - **Lighter than undo history browser, complementary not competing.** Undo is automatic per keystroke; a version is *authored* ("roughs approved") and spans sessions. Landed 2026-08-13 on the `VersionEntry`/`VersionHistoryManager` framework that had waited in Core without a store: `FileVersionHistoryStore` persists each resource's history to `versions/<resourceId>/history.json` in the project folder, and `ProjectVersions` keeps a **byte-for-byte copy of the saved file** beside it — one mechanism for documents and sheets alike, and gzip keeps the copies cheap (~KB-scale for typical documents).
  - Three capture points: **File ▸ Save version…** (label + notes), **promotion to Review/Ready** in the project window (tagged with the milestone, so "which bytes were Ready" survives further drawing — the export-filter story's missing half), and the **safety copy every revert takes first**, which is what makes revert non-destructive by construction.
  - Project-scoped on purpose: a loose file has no `versions/` folder to write into, and the menu says so rather than hiding. `CreateBranch` remains framework-only — no UI until linear history proves itself (Q75's deferral).
  - **The project window shows what it makes (2026-08-22).** Q75 put milestone capture in the window and every history surface elsewhere, so promoting to Ready kept a version the promoting surface could not show. Structure's VERSIONS column now carries `VersionFacts` — count, newest milestone in the board's colour, ✎ for a file that drifted past its kept bytes — the footer counts the drift ("2 changed since approval"), and both row menus open the shared history window through the owner-supplied `HistoryFor` seam. One directory listing plus one history read per versioned resource, cached per (modal) window.
- [ ] Export the approved bytes — an export preset flag "use the Ready version where one is kept", shown per row in the plan before anything is written (Q146) `evidence: ExportApprovedBytesTests, AnExportWithTheFlagShipsTheMilestoneBytes, APresetThatNeverSetsTheFlagWritesNoKey`
  - Decided opt-in **per preset**, not per run (Q146): the studio that wants approved-only exports wants them on every run of that preset, which is what presets are for. The plan view must say per row which bytes ship — kept or current — because a divergence between canvas and output that only shows in the output is how exports lose trust. `VersionFacts.ChangedSinceMilestone` is the per-row fact it leans on.
- [ ] Delete permanently asks about kept versions — a checkbox on the confirmation, "also delete its N kept versions", shown only when any exist (Q150) `evidence: DeletePermanentlyAsksAboutKeptVersions, ALoneVersionedDocumentAsksBeforeDeleting, TheCheckboxCountMatchesTheHistory`
  - The standing answer, chosen over clear-with-the-gesture (Q150, and Q150 records why the question was answered twice): the artist decides with the number in front of them. Today's unconditional clearing (landed with Q150's second, superseded answer) is interim behaviour this replaces. The implementing branch settles the checkbox's default and makes `DeleteNeedsConfirmation` answer true for a lone versioned document — only folders with contents ask today.
- [x] The project window — structure, status, tags, assets and people across a production `evidence: ProjectWindow, ProjectWindowViewModel, ProjectBoard, ProjectWindowTests, ProjectBoardTests, TheWindowAndTheDockerListTheSameDocuments, TheFooterCountsWhatIsTrue, TheAssetsTabShowsAllThreeLevelsAtOnce, TheChainIsFourDeepAndNearestWins, SharingSomethingWithAScopeDeclaresItThere, TheFirstDeclarationOfAKindSaysThatScopingIsNowOn, TheFacetEditorAppearsForExactlyOneFolder, TheReviewedFlagCanFinallyBeSet, TheExportTabShowsWhatWouldBeWritten`
  - **HIGH-VALUE, MARKET-VALIDATED.** Studios manage projects in ShotGrid/Airtable because Lightbox has no dashboard. Current workaround: maintain separate spreadsheets tracking shot status, artist assignments, blocked items.
  - **Q29's second surface, in its own window by Q41.** The docker does what you do while drawing — find it, open it, move it, rename it. This does what you do between drawings: bulk edits, tagging, assignment and status across a production, none of which fits in 200 pixels beside a canvas. Five tabs: Structure, Status, Assets, Export, People. `docs/DESIGN-studio-dashboard.md`.
  - **The first cut named four things it did not do, and all four have landed.** The Assets tab writes as well as reads; a single selected folder gets a facet editor, which is also the only place `SubjectTaxonomy.Reviewed` can be set — the flag shipped in PR #48 with nothing that could write it; a status card drags between columns; and the Export tab shows `ExportPlan` standing still, read-only, because running an export is the export window's job.
  - **Not read-only, which the old entry assumed.** "Manage assets on project, folder and file level" was the explicit ask (Q42), and read-only was written when there was no reason to open a second surface. Q44 answers the undo question: status, tags and assignment are manifest metadata rather than artwork, so nothing here is destructive and there is no undo stack — each bulk edit says what it did instead.
  - **Blocker: cleared 2026-08-07.** B114's one tree is what made it cheap. A dashboard written against `Project.Characters` and `Project.Scenes` would have been rewritten by the change it was waiting on; against one list and one traversal it is a second reading of a tree that already exists.
  - **Two anchors came out and it is worth saying why.** `BlockedShotsAreHighlighted` needs a dependency model — what blocks what — which nothing in Lightbox has and this design does not propose. `ArtistWorkloadIsBalanced` is a claim about balance, needing estimates and capacity, which is the project-manager line this deliberately does not cross. Shipping green against tests that assert something else is the one thing the derived checkbox cannot represent.
  - **Q45 draws the boundary this feature would otherwise drift across.** `Person` is a name and an id and never gains a role or rights: the manifest is plain JSON on disk, so a permission here is one a text editor defeats. Sharing is the project file over git; a tracker adapter (ShotGrid, Kitsu, Flow) is the seam if a studio needs one, and it needs no new model because documents already have stable ids.

- [x] Guide sets exist to be made — a named set of guides, creatable and shared onto folders `evidence: GuideSetEditor, GuideSetTests, AGuideSetCanBeMadeAndNamed, ADeclaredGuideSetReachesTheDocumentsUnderItsFolder, ADocumentPullsASharedGuideSetIntoItsOwnGuides`
  - **The resolver exists and nothing feeds it** (owner's report, 2026-08-13: "guides we are not even able to create or assign"). `GuideSet` is a record, `GuideScopes.VisibleTo` resolves declarations, `ProjectBoard.Offers` lists `Manifest.GuideSets` — and no UI anywhere creates a guide set, so the whole chain is reachable only from tests. The character height guide the scoped-resources design was written around cannot be performed.
  - The missing half is authoring: a way to name a set and put guides in it (plausibly "save these document guides as a set", the way a template is work you already did), after which the existing declaration surface shares it like any other kind. Consuming needs the document side to offer visible sets — the `TipStore` pattern.
  - Deliberately not squeezed into the pills round that found it: creation UI, an editing story and the pull-into-document semantics are a feature, not a registration. Cost: M.
  - **Built 2026-08-13, the shape the entry predicted.** Authoring is "save these document guides as a set": `GuideSetEditor` (View ▸ Guides ▸ Guide sets…) names, refreshes, renames and deletes sets — the canvas stays the guide editor, so the window has no add-a-guide button. Pulling is View ▸ Guides ▸ Add from set, offering `GuideScopes.VisibleTo` (or every set while the project is unscoped, Q30's migration), and lands copies with fresh ids as one undoable step — the set is a library, so a pulled guide moved afterwards edits only its own document. Deleting a set retracts its declarations, because a declaration pointing at nothing would scope the kind and offer air. The manual's guides section carries the workflow; the construction-guide wishes below it now have their prerequisite.

- [x] A guide set lands on the paper it arrives at — authored on 4K, pulled into 1080p, the character is still the same height in frame (Q181) `evidence: AuthoredCanvas, GuideSetFit, GuideSetFitTests, AGuideSetRemembersTheCanvasItWasAuthoredOn, PullingA4kSetInto1080pKeepsTheHeightScaleTheSameFractionOfFrame, AGuideSetScalesUniformlySoALinesAngleAndAGridsSquarenessSurvive, ASetWithNoAuthoredCanvasLandsExactlyWhereItWasAuthored, FittingMeasuresFromThePaperRatherThanFromTheCoordinateOrigin, AGuideSetThatNeverRecordedItsPaperWritesNoCanvasKey, FittingASetOntoThreeDocumentsNeverEditsTheLibrary`
  - **Cost S, and it is one transform in one method.** `PullGuideSet` copies guides verbatim in document pixels, which is the whole defect: the *preference* half already stores a proportion (`AppSettings.HeightScaleFill` is 0.7 of the canvas, not a head height), and the library half never learned it. The set records the canvas it was authored on; the pull scales by one uniform factor taken from **height** and places anchors by fraction of each axis. Uniform is not fussiness — scaling the axes separately to make a height scale fit would tilt every `Line`, stop an `Isometric` being isometric and make a `Grid` non-square.
  - **Absence does the migration.** A set saved before this has no authored-canvas key and pulls exactly as it does today.
  - **Built 2026-09-04.** `GuideSetCanvas` records the paper (shaped like `Scene`'s own four numbers, origin included, so a fraction is measured from the paper's corner rather than from zero on a canvas somebody grew leftward); `GuideSetFit.Onto` carries the set; `SaveGuidesAsSet` records the paper on every save, a refresh included. The uniform half is the one with teeth and `GuideSetFitTests` pins it at 4:3 → 16:9, where the two candidate rules disagree by a third — **three of its seven fail if the fit is skipped**, and the other four are the compatibility and absence guards, which must pass either way.
  - **Still open, and split out below:** applying a folder's set when a document is created under it.
- [x] A folder-scoped guide set applies when a document is created under it (Q181, decision 3) `evidence: ANewDocumentInAScopedFolderOpensWithItsGuides, AGuideSetAppliedOnCreateIsFittedLikeAnyOtherPull, ADocumentInAnUnscopedProjectOpensWithNoGuides, TheNearestDeclarationDecidesWhichGuidesADocumentOpensWith, TheGuidesADocumentOpensWithAreNotAnEditToIt`
  - **Cost S, and it rides the fit above** — the pull already lands on the right paper, so this is only about *when* it happens. "Heights stay the same throughout a project" is not a menu item you remember to use; the new drawing in the knight folder should open with the knight's chart on it.
  - **Built 2026-09-04.** `ApplyScopedGuides` runs after adoption, because adoption is what decides which folder the document is in — there is no scope to resolve until it has a home. **Nearest wins and only the nearest lands**, exactly as a palette resolves: stacking a project rig on top of a character rig would be a defect wearing a feature's clothes. An unscoped project declares nothing, so nothing is applied — auto-apply follows a deliberate share and never a default.
  - **The accepted cost, and how it was paid** (Q181): the document has content before the artist touched it. Written straight onto the scene rather than performed as an edit, so the guides are revision 0 — the drawing does not read as unsaved work nobody did, and there is no undo before the first stroke whose meaning is "take away the guides I opened with". `TheGuidesADocumentOpensWithAreNotAnEditToIt` holds both halves.
  - **Not covered, and deliberately:** the start screen's reuse-the-blank-document path, which never adopts into a project and therefore has no folder to resolve against.
- [x] Rig library — armatures saved, scoped and pulled, sized in head units off the document's height scale (Q181) `evidence: RigSet, RigScopes, ArmatureFit, RigFit, RigSetEditor, ArmatureFitTests, RigSetTests, ARigSetCanBeMadeAndNamed, ARigRemembersHowManyHeadsTallItWas, PullingARigAgainstAHeightScaleStandsItOnTheAnchorAtItsHeadCount, TwoRigsSavedAgainstOneChartKeepTheirRelativeHeights, ARigWithNoHeadCountFallsBackToTheCanvasFraction, OriginalSizeLandsTheBindPoseUntouched, ScalingARigChangesLengthsAndNeverAngles, PullingOntoASkeletonSomethingIsBoundToIsRefused, PullingOntoAPosedSkeletonIsRefused, ADeclaredRigSetReachesTheDocumentsUnderItsFolder, ARigSetThatMeasuredNoHeadsWritesNoHeadsKey`
  - **Cost M, and it is the marriage with the height guide rather than a second copy of it.** The library mirrors `GuideSet` exactly — manifest list, scope kind, copies in with fresh ids. What is new is the unit: a rig travels in **heads**, so the human at 7.5 and the goblin at 4.5 keep their relationship on any canvas without being told the resolution. `GuideKind.HeightScale` was already shaped for it — `(X, Y)` is the ground and `Spacing` is one head — so a rig lands feet-on-anchor at `heads × Spacing`. *Original size* stays on the menu because the goblin being short is data, not an accident to normalise away.
  - **Two landings, one record.** "Use as armature" becomes `Doc.Armature`; "place as proportion guide" is a ghost drawn like a guide, any number per document, not posable and not bound. `Doc.Armature` is singular, so a human-dog-goblin comparison sheet is only expressible as the second — that is what forces the split, not taste.
  - **The hazard, written down before it is built:** scaling a bind pose is safe at pull time and never after. `docs/DESIGN-bones.md`'s "one trap" is that the bind pose is the space dab dynamics seed from, so rescaling an armature with strokes already bound re-rolls every dab and boils the character. Refuse it, or rebind. It is not invariant 7 — nothing here multiplies a stroke coordinate.
  - **Built 2026-09-04.** `RigSet` in the manifest (absent until one is saved), `RigScopes` on the palette pattern, `ArmatureFit` for the arithmetic, and the Skeleton menu plus `RigSetEditor` so the chain is reachable by an artist — the guide-set lesson applied in the same commit as the record rather than a release later. The pull is one undoable step and becomes `Doc.Armature`.
  - **Only three numbers per bone carry a length** — `Length`, and the origin offset `X`/`Y`. Rotations are dimensionless, IK and spline chains name bones rather than points, and a constraint's influence and offset are an amount and an angle. That is why scaling a rig is a function rather than a traversal full of special cases, and why `ScalingARigChangesLengthsAndNeverAngles` is a one-line guard.
  - **The trap is closed by refusing, and the refusal is tested twice.** `docs/DESIGN-bones.md`'s "one trap" is that the bind pose is the space dab dynamics seed from, so swapping a skeleton under bound art re-rolls every dab and the character boils. `ArmatureIsBound` and a posed track both block the pull. Nothing here multiplies a stroke coordinate, so invariant 7 is not in play.
  - **A rig saved with no height scale has no head count** and writes no key — inventing one from the canvas would be a guess dressed as a proportion. The pull falls back to the canvas rule and says which rule it used.
  - **Still open, and split out below:** the proportion-ghost landing, which is what a size-comparison sheet needs.
- [~] A document holds many rigs, all of them posable (Q182, superseding Q181's decision 2) `evidence: ManyRigsTests, OnePoseTrackCarriesTwoRigsWithoutAmbiguity, ADocumentWrittenBeforeManyRigsOpensWithItsRigIntact, ADocumentThatHasRigsWritesThemOnceAndNotUnderTheOldKey, AssigningTheFirstRigLeavesTheOtherCharactersAlone, RigOfBoneFindsTheCharacterAPoseKeyIsTalkingAbout, TheRigYouAreEditingTests, RigRow, TheOtherCharactersAreDrawnAndMarkedAsNotInHand, TheOtherCharactersArePosedAtThePlayheadRatherThanLeftAtRest, PressingAnotherCharacterTakesItInHandAndGrabsNothing, AnEditUndoneAfterSwitchingCharactersStillEditsTheOneItWasMadeOn, DrawingABoneAddsItToTheCharacterInHandRatherThanStartingANewOne, ALayerFollowsABoneOfAnyRig, PlacingASecondRigGivesItFreshBoneIds`
  - **Q181 planned a proportion *ghost* and the owner corrected it**: *"the rigs might still want to be animated. So I can draw over them without losing the reference"*. A posable mannequin you rough out across frames and draw over is a second character, not a drawing aid — and two characters interacting in one shot are two rigs with art bound to both. The reference model was refused for that reason: it costs M and then L, where this costs L once.
  - **The animation half needed no new record.** `PoseKey.Bones` is keyed by bone id and is sparse, so one `PoseTrack` has always been able to carry several rigs' poses without ambiguity — a property phase 1 had and nobody had reason to notice. The whole cost was in the singular `Doc.Armature`: 90 call sites, 35 of them in `MainViewModel.Armature.cs`, where they say *the rig* and come to say *the rig I am editing*.
  - **Step 1, the record, built 2026-09-04.** `Armature` gains an `Id` and a `Name`; `Doc.Armatures` is the list; `Doc.Armature` survives as a `[JsonIgnore]` accessor for the first rig, which is what kept all ninety sites compiling and meaning something true. Documents written before it read their `armature` key and are folded into the list, and no new document writes that key again. An unrigged document still writes neither. Assigning the first rig leaves the other characters alone — a setter that cleared the list would turn "give this document a skeleton" into "delete the dog".
  - **The clone guard grew a rule rather than an exemption:** a zero-length array is skipped wherever it appears, because `[]` on a derived getter is `Array.Empty<T>()` and cannot gain an element. `Doc.Rigs` was the first to hit it; the rule is the value-type skip's argument one shape along.
  - **Step 2, the rig in hand, built 2026-09-04.** `EditingRigId` is view state for the layer index's reason — which character you are working on is not a fact about the drawing, and saving it would put you in somebody else's rig on opening their file. Every editing verb in `MainViewModel.Armature.cs` now means the rig in hand, and every edit **captures** the rig id rather than reading it at replay time, so an undo or redo after switching characters edits the rig the step was made against. The other characters draw dimmed and posed at the playhead — posed is the point, since an unposable reference is the ghost the owner rejected. A press on another character takes it in hand and grabs nothing: two clicks rather than one, because a mannequin stands on the drawing you are making and a stray drag there is expensive.
  - Still to come: **step 3**, a layer's `BoneId` and a stroke's `Weights` resolving against whichever rig owns that bone; **step 4**, placing a library rig as an additional rig with fresh bone ids — Q181's refusals guard *replacing* a bound skeleton, and adding one is not replacing.
- [ ] A set travels to another project as a file — export and import for guide sets and rig sets (Q181) `evidence: SetFileCodec, ExportSetCommand, ImportSetCommand, AGuideSetRoundTripsThroughAFile, ARigSetRoundTripsThroughAFile, AnImportedSetGetsFreshIdsSoItCannotCollide`
  - **Cost S.** Sets live in the manifest and stay there. A machine-wide library was considered and refused (Q181, decision 4): it would make a project stop describing itself completely, so opening it on another machine would quietly lose guides that looked like part of it. A file is a copy and behaves like one — a fix in the sequel does not reach the original, which is the honest cost of the cheap answer.

- [ ] Animatic preview export — one-click movie render with timing and placeholder SFX `evidence: AnimaticExporter, AudioTimelineSync, AnimaticTests, ExportedMovieHasCorrectFrameTiming, PlaceholderBeepsMarkKeyFrames`
  - **HIGH-VALUE, MARKET-VALIDATED.** Every studio manually exports to video for director review; no animation tool offers one-click animatic with timing beeps.
  - **Effort:** Low-Medium (~300 LOC)
  - **What it does:** Export timeline to H.264/ProRes movie file, preserve frame timing, optionally add placeholder beeps at keyframes and sound effects
  - **Impact:** Fast director feedback loop; currently requires external video editor
  - **Blocker:** None — builds on existing export infrastructure

- [?] Backup manager
- [?] Command palette
- [?] Macro recording
- [?] Favorites
- [?] Asset collections
- [?] Smart search
- [?] Batch export
- [?] Render queue
- [?] Package projects
- [?] Missing asset detection
- [?] Dependency warnings

### Collaboration

- [ ] Comments on frames — annotations on specific frames for review and feedback `evidence: FrameComment, FrameComments, FrameCommentViewModel, FrameCommentTests, ACommentIsAttachedToAFrameNotADrawing, CommentsRoundTripThroughTheFile, DeletingAFrameDeletesItsComments, CommentsAreBrowsable`
  - **High-value, medium-effort.** Unblocks Pillar 6 review workflows and is explicitly requested in Request 1 feature analysis. Distinct from marker notes: a `FrameMarker.Note` is internal (hand pointers), a frame comment is external (director feedback, revisions). Stored as `FrameComment` records on `Frame`, serialized with timestamp and optional reviewer. Displayed in the timeline footer or a dedicated Comments docker, with export-optional visibility. Complements the snapshot system for managing revisions.
- [ ] Comments on layers — feedback and annotations per-layer, distinct from frame-level review `evidence: LayerComment, LayerComments`
  - **Unverifiable until design clarified.** Distinct from frame comments — layer-specific feedback — but the design question is whether these are per-frame-per-layer, project-wide per-layer, or something else. Lower priority than frame comments; defer until frame comments implementation clarifies the pattern. Request 1 feature analysis identified as important for studio review workflows, but less frequently referenced than frame comments.
- [?] Task assignments
- [?] Review mode
- [?] Version comparison
- [?] Change history
- [?] Asset locking
- [?] Cloud libraries
- [?] Team asset sharing

## AI assistance

The third of the three purposes in `CLAUDE.md`, gathered here rather than
scattered through the pillars it serves. It is **not a seventh pillar** — the six
are the app's identity and AI cuts across all of them — but it is the one area
where the cost, the failure modes and the review process are shared, and reading
them together is the only way to see the whole bill.

**What belongs in this section:** a feature that *needs a model to be possible at
all*. Things that measure geometry or timing stay with the pillar they serve, even
when the word "assistant" is in the name — arcs, spacing, timing charts and
contact-frame detection are arithmetic, and filing them here would make this
section look like the whole roadmap.

Four rules govern everything below, and they are not negotiable per feature:

0. **The AI never starts from nothing.** Every feature here takes something the
   artist authored and does the tedious part of it — two keys and it fills the
   gap, pencils and it inks them, a pose and it fleshes it out. There is no
   entry point that turns an idea into a drawing, and that is a statement about
   what this application is rather than a feature nobody has built yet.
   - **This rule was written after breaking it.** `IAiArtist` carried a
     `DrawAsync` from M2 — a text prompt in, a drawing out — with a prompt box
     in the AI bar to match. It worked, it was tested, it was documented, and no
     roadmap item ever claimed it, which is how a capability nobody decided on
     survived for eleven milestones. It was removed rather than left unused,
     because a control that is present makes a promise whether or not anybody
     presses it, and the promise a prompt box makes is the wrong one.
   - The test that keeps it out is reflection over `IAiArtist`, not a missing
     button: the button was the symptom, and the interface is where it comes
     back from.
1. **A model never renders.** Every AI feature produces an *authored artifact* —
   strokes, a reading, a normal map — which is then stored and replayed by the
   ordinary deterministic path. Invariant 2 is not a constraint AI works around;
   it is the line that decides whether a proposal is buildable. The test shape is
   always the same: delete the AI's output and the render must be byte-identical
   to what the record alone produces.
2. **Two reviewers, and they are meant to disagree.** `ai-engineer` owns the
   machinery, the cost and the determinism line; `art-director` owns whether the
   result reads at 12 fps and is on-model. **art-director has a veto on
   expression, ai-engineer has a veto on determinism**, and where they disagree
   and cannot measure it goes to `QUESTIONS.md`. Gate G12 makes this mandatory for
   any diff touching `src/Lightbox.Ai`, the MCP surface, a prompt, or an AI path in
   the view model.
3. **Cost is a first-class property.** `docs/DESIGN-ai-payload.md` has the measured
   numbers and they are not to be re-derived. The one that settles most arguments:
   **images are ~87% of a request's bytes and ~5% of its tokens; strokes are the
   reverse** — so "make the payload smaller" is two goals recommending opposite
   changes, and a proposal that has not said which it means is not ready.

### The machinery

- [x] Any model, over an API or MCP `evidence: AiProviders, AiConnection, AiArtistFactory, OpenAiArtist, McpArtist, AiProviderTests, EachProviderShowsItsOwnFieldsAndNobodyElses, AStoredValueBeatsTheEnvironmentWhichBeatsTheDefault`
  - Six providers behind one `IAiArtist`, chosen in Edit ▸ Configure ▸ AI: Claude, GPT, OpenRouter, Ollama, any OpenAI-compatible endpoint, and an MCP server the user supplies. The page is **generated from the catalogue**, so adding a service is a catalogue entry and a factory case — a page that hard-coded Claude's fields would pass a test that only checked Claude and then show an API key box for a local server.
- [x] AI assistance can be switched off entirely `evidence: TurningItOffPersistsAndTakesTheArtistWithIt, TheProviderFieldsStayUsableWhileAssistanceIsOff, AiEnabled`
  - On by default, and off removes the AI bar rather than greying it — the camera's rule, for a studio that wants AI nowhere near a shot. The switch beats a complete connection, and the provider fields stay usable while it is off so a provider can be configured and proven before it is turned on.
- [x] A connection test that checks the output, not just the reply `evidence: AiConnectionTester, AiTestDepth, AiConnectionTesterTests, AThoroughTestFailsWhenTheModelCopiedAKeyInstead, AQuickTestMakesOneCall, EveryArtistMethodStartsFromSomethingTheArtistDrew`
  - **It asks for real work rather than pinging.** The ways this fails are mostly not reachability: a key with no credit, a model name off by a version, an endpoint that answers but cannot honour a JSON schema, an MCP server whose tool is spelled differently, a small model that returns valid JSON full of nonsense. A ping says "connected" to every one.
  - Two depths, and **both ask for an inbetween** — it is the only thing the application asks a model for, so a test that exercised anything else could pass on a provider that cannot do the job. Quick takes a two-point line and checks only that what comes back would mark; thorough adds a real inbetween and checks it lands **between** the two keys — the one assertion that separates a working connection from a working inbetweener, and the one a parse check can never make. Three verdicts rather than two, because "unreachable" and "reachable but drawing nonsense" need different fixes.
- [x] A budget on what a request costs `evidence: AiPayloadBudgetTests, AnInbetweenRequestStaysWithinItsBudget, CostScalesWithStrokeCount_WhichIsWhySendingFewerIsTheRealLever, ResamplingIsWhatKeepsALongStrokeAffordable`
  - The one cost in this app that is invisible locally: a change that doubles a payload shows up on somebody's bill a month later and nothing in the suite says a word. Measured in `docs/DESIGN-ai-payload.md` — a 40-stroke frame pair is 102 KB and at least 26k tokens; `MaxWirePoints` is the constant carrying it, and deleting it would fail no other test.
  - The finding worth keeping: **images are ~87% of a request's bytes and ~5% of its tokens, and strokes are the reverse.** So "make the payload smaller" is two goals recommending opposite changes, and any optimisation has to say which it means. Compression is off the table for the same reason — it takes 82% off the bytes, touches no tokens, and 0.3 s of upload is invisible beside 30–120 s of generation.
- [ ] Send the strokes that need judgement, not the whole frame `evidence: StrokeTriage, StrokeTriageTests, OnlyStrokesThatMoveAreSent, TheContextIsEnoughToPlaceThem`
  - Six times bigger than any encoding trick, and the only lever with no format risk. A 120-stroke frame is ~79k tokens and most of those strokes barely move; the deterministic inbetweener already handles a matched stroke correctly, and the AI is needed where straight interpolation fails — arcs, rotation, overlap. Halving the stroke count halves the cost exactly.
  - The hard half is knowing *which* strokes need judgement, which is `DESIGN-subject-reading.md`'s question approached from the other side.
  - **Nothing here is built, and for a while the file could not say so.** The anchor was `StrokeSelectionTests`, which resolves against `tests/Lightbox.App.Tests/StrokeSelectionTests.cs` — picking whole lines with the black arrow, nothing to do with pruning a payload. So an item with no code behind it showed one of four anchors satisfied, which is exactly the false green the derived checkbox exists to refuse; it arrived through a name collision rather than through a claim anybody made. Renamed to `StrokeTriage` because *selection* is already taken by the canvas and means something an artist does with a mouse — **triage** is the AI-side question of which strokes need a model's judgement, and no UI concept competes for the word.
- [x] An agent can read a drawing without paying for all of it `evidence: ListFrameStrokes, McpReadBudgetTests, ListingADrawingCostsAFractionOfReadingIt, AnIndexFromTheListingFetchesTheStrokeTheListingNamed, AStrokeLabelThatIsNotThereIsRefusedAndTheRealOnesAreNamed, RenderFrame_IsCappedOnTheLongEdge, RenderFrame_AtLongEdgeZero_StillGivesTheAuthoredCanvas, ACappedRenderSaysHowMuchOfTheCanvasYouAreSeeing, NamingStrokesWithoutNamingTheLayerIsRefused, TheReplySaysWhichPositionsItIsAnswering`
  - **The cost the item above cannot see.** `AiPayloadBudgetTests` measures a *request* — built, sent, paid once. An MCP reply is a different thing: it lands in the agent's context and is re-read on every turn for the rest of the session, so a fat reply is a standing charge rather than a one-off. Nothing in the suite said a word about it, which is the gap *An agent can time a sequence* named as "the coverage gate that would make the next such gap fail a test instead of needing an audit".
  - Measured on a 120-stroke, 90-point frame: `get_frame_strokes` is **147.4 KB, ~37,700 tokens**; `list_frame_strokes` answers the same drawing in **10.1 KB, ~2,594** — index, label, colour, point count and a box per stroke, no geometry. Naming three strokes out of it costs 2.5% of the frame. In most tasks the 37,700 bought only the knowledge of which strokes exist.
  - `render_frame` is capped at 768 px on the long edge (~442 image tokens against ~2,764 at 1080p), with `longEdge: 0` for a caller that wants the canvas — **and a reduced render says what fraction of the canvas it is**, because G12's art-director measured that the cap keeps a frame's pose and strips 84% of the fine dark pixels off a 1080p face, eyebrows and eyes entirely at 4K. The cap survived that; its silence did not (Q178, amended). Its sibling `render_reference_view` stays uncapped, on B31's reasoning — an agent asking for a view should get the view — and the two constants are deliberately *not* shared, so Q27's per-view heuristic cannot drag `render_frame` along with it. Q177 and Q178 record both decisions and what the alternatives cost.
  - **The unfiltered read is still the default, and that is a named cost rather than an oversight** (Q177): making the listing the default would save more and would silently change what every existing agent gets back from a call it already makes. The descriptions carry the weight instead. If agents ignore them, that answer is the escalation.
- [ ] An agent can write a frame back without hitting a ceiling `evidence: StrokeDelta, McpWriteBudgetTests, ATranslatedStrokeCostsItsTransformNotItsPoints, ARedrawnStrokeStillCarriesItsGeometry`
  - **B359, and it is a ceiling rather than a cost.** `insert_inbetweens` takes full geometry, so three inbetweens of a 120-stroke frame is **~442 KB, ~113,200 output tokens** — past any single response. The task cannot be completed over MCP at all, and the failure is silent: a truncated answer looks like a model that drew badly.
  - **Blocked on Q179, deliberately.** The fix is a delta encoding and it is cheap to build; the objection is that a transform-only inbetween *is* the deterministic answer, and a wire format whose cheap path is "translate these strokes" will get the cheap path from a model minimising effort. ai-engineer's ceiling against art-director's expression, neither measurable from the other, so it is gate G12's pair and not this branch's guess.
  - Cost M — the format and the app-side expansion are the small half; deciding whether it can be shaped so redrawing stays the default is the expensive one.
- [x] An MCP surface, so an agent can work the document directly `evidence: IpcServer, IpcDocumentApi, IpcTests, InsertInbetweens_ValidatesAndInserts_Undoable, DrawStrokes_AppendsToExposedKey, BadRequests_FailCleanly, PipeRoundTrip_GetScene`
  - **The other direction, and it was missing from this file entirely** until the AI section was gathered — which is its own small argument for the section. `CLAUDE.md` names it as one of the three purposes and the code has shipped it since M4a, but no roadmap item claimed it, so nothing was deriving its status from the code.
  - Independent of the provider list above, and that independence is the point: there, Lightbox calls out to a model; here, an agent the artist already runs calls **in** and edits the document. Configuring a provider is not a prerequisite for either.
  - Every tool goes through the same document editor a menu item uses, marshalled onto the UI thread — so an agent's edit is one undo step, dirties the tab, and cannot bypass `BrushEngine.StampStroke`. An MCP surface that wrote pixels directly would break invariant 1 for the one caller least able to notice.
  - The anchors are named tests rather than a project name, and the first attempt at them was wrong: `McpToolTests` does not exist and `roadmap.py` demoted the item within seconds of it being written. That is the file working as designed — a green box asserted from memory is exactly what the derived checkbox exists to refuse.
- [x] An agent can time a sequence, not only draw on it `evidence: IpcExposureTests, SetKey_MakesADrawingOnAHoldAndIsOneUndoStep, ACreatedKeyIsTheAgentsAndAReMarkedOneStaysTheArtists, ReduceExposure_RefusesRatherThanSilentlyDoingNothing, SetExposureStep_PutsARangeOnTwosAndStaysThereWhenRepeated, NoTimingOpEverRemovesADrawing, ALockedLayerRefusesEveryTimingOp`
  - **Found by asking what the surface above actually reaches.** `get_scene` has
    reported `keyedFrames` since the surface existed and no op could make one, so
    an agent could draw on a frame and could not time anything. On an application
    whose stated unit of work is a sequence, that is the half that matters — and
    it read as a complete `[x]` because one item covered a read-rich, three-verb
    write surface. Four ops close it: `set_key`, `extend_exposure`,
    `reduce_exposure`, `set_exposure_step`.
  - **Nothing new in the record.** Every op is `DocumentEditor` work the menus
    already do — `SetKeyAt`, `ExtendExposure`, `ReduceExposure`,
    `StretchExposure` — so an agent's retime is one undo step and invariant 1
    holds for the caller least able to notice breaking it.
  - **Non-destructive by construction, and that is the boundary.** `SetKeyAt`
    only adds a drawing, `ReduceExposure` refuses to remove one, and
    `StretchExposure` absorbs existing holds rather than multiplying them, so
    asking for 2s twice stays on 2s. `ReduceToStep` — the one that discards
    drawings — is deliberately **not** exposed: a destructive agent op wants the
    explicit-flag treatment `import_character` has, and that is its own decision.
  - **A refusal beats a silent no-op here**, because an agent cannot see the
    timeline. `reduce_exposure` on an unheld frame errors rather than succeeding
    without effect, and an unknown role name fails rather than quietly landing a
    key — the reply is the only feedback the caller gets.
  - **Q31 at its narrowest:** a key the agent *created* carries its provenance; a
    frame it only *re-labelled* stays the artist's. So the stamp is a parameter
    on `SetKeyAt` rather than a write afterwards — it has to land inside the one
    undo step, and a caller setting `frame.Ai` after the fact would make two.
  - Still ahead: the rest of the ~38 document-level commands an agent cannot
    reach — layers, camera, effects, export, selection — and the coverage gate
    that would make the next such gap fail a test instead of needing an audit.

- [x] The AI never inserts a frame it cannot defend `evidence: InbetweenVerifier, InbetweenVerifierTests, ARubbishAnswerInsertsNothingAndSaysWhy, ARefusedFrameKeepsItsSlotAsAHold, TooCloseToTheDeterministicAnswerIsANoteNeverAVeto, PerFrameJitterIsRefusedAsIncoherent, RevealedInkBehindTheMoverIsLicensed`
  - Phase 0 of `docs/DESIGN-ai-correctness.md`: every frame a model returns is verified against the keys — betweenness, dropped strokes, licensed new ink, area-conserved volume, and temporal coherence over the *run*, which is the only check that catches boiling and the reason the verifier sees a sequence rather than a frame. A frame that fails is **refused, per frame and with the reason naming which t** (Q32) — never swapped for the deterministic answer, which stays its own command. Its slot stays a hold, so partial acceptance never shifts a surviving frame off its own timing.
  - The checks are deliberately wide — they reject "not between the keys at all", never "not where I would have put it" — and the deterministic answer passing every check is itself a pinned test. "Too close to deterministic" is a note, never a veto (Q33). The connection tester now judges with the same verifier, so a model it certifies is one the pipeline will accept.
  - Still ahead, by phase: adaptive request shaping, and the piecewise betweenness Q83.3 needs before the AI path can fill a whole run rather than one gap.

- [x] A refused frame is asked again with the fault named, not retried blindly `evidence: InbetweenRepair, RefusedFrame, InbetweenRepairTests, TheReAskCarriesTheFaultAndTheDrawingThatEarnedIt, AModelThatNeverImprovesIsRefusedAfterThreeCalls, ARepairThatWouldCostAnAlreadyAcceptedFrameIsNotAdoptedEvenWhenItGainsTwo, AFailedReAskKeepsTheFramesTheFirstCallEarned, ARefusedFrameIsAskedAgainAndTheFrameItProducesSaysSo`
  - Phase 3 of `docs/DESIGN-ai-correctness.md`, and stage 4 of its pipeline — the step between *verify* and *refuse*. A blind retry is a second roll of the same dice; this hands the model the sentence the verifier wrote (*"the ‘near-arm’ did not stay between the keys — it sits 60px from where the motion puts it"*) **together with its own rejected drawing**, which is what turns a re-ask into an edit rather than a redraw-with-a-hint. The refusals were already written as forwardable sentences naming a stroke and a distance; this is the first thing to spend them on.
  - **Bounded at two re-asks and on by default (Q85).** Two rather than one against the recommendation, because the common failure is a model that fixes the fault it was told about and trips a different check — one re-ask cannot see that shape. On by default unlike best-of-N, and the distinction is the reason: best-of-N buys a *better* frame, repair buys a frame *at all*, so the alternative to spending the call is an empty slot. The cost is a worst case of three calls to produce nothing, which is why the status reports the attempt while it runs and names the count when it fails.
  - **A repair can never cost a frame that was already accepted**, and that guarantee is set-inclusion rather than arithmetic. Accepted frames carry into the next round untouched, so the only way one newly fails is coherence — a repaired neighbour that makes it jitter. A round that gains two and loses one is dropped whole, because a frame given and then taken away reads as a bug however the totals came out. Q32 is untouched throughout: nothing here relaxes a check, and a repaired frame clears the same bar as a first-attempt one.
  - `AiProvenance.Attempts` is the durable trace, absent unless it took more than one — the status line is gone by the next action, and "how often does my model need a second go" is what tells an artist whether the model they brought is borderline.

- [x] Grade the model you brought, before you depend on it `evidence: GoldenSet, CapabilityProfile, CapabilityProfiler, FreeEngineArtist, GoldenSetTests, TheFreeEngineClearsEveryConstructedPair, TheLadderFindsWhereAModelStopsCoping, TheOrganicCategoryReportsItselfUnmeasuredRatherThanVanishing, TheArcRowTellsAnArcApartFromAChord, AiCapabilityPageTests, TheCostOfARunIsShownBeforeItIsSpent, AReadingTakenOnAnotherModelSaysSoRatherThanPassingAsThisOne, AnUnprofiledConnectionWritesNoProfileKey`
  - Phase 2 of `docs/DESIGN-ai-correctness.md`, and Q34's answer: the golden set **ships**, because grading an artist's own model is the bring-your-own-model story rather than a development convenience. A committed set of keyframe pairs, scored by the same `InbetweenVerifier` the pipeline uses, produces a profile per model: schema adherence, label retention (Q18's unmeasured claim, now measured), betweenness per category, and **where the model stops coping with stroke count** — the number the design calls the most valuable and nobody measures.
  - **"Model" here is the AI engine, never the character.** Grading asks whether Claude, or the local install somebody brought, can inbetween at all. Staying *on-model* is the character sheet and `SubjectTaxonomy` — a different mechanism, in a different part of the request, going stale for different reasons. The two senses have genuinely misled a reader of this section, so `docs/DESIGN-ai-correctness.md` now opens by separating them: a golden pair's answer is hidden from the AI, a model sheet is sent to it on every request.
  - **Numbers and a per-category headline, no overall pass or fail** (Q83's sibling decision). A model weak on arcs is still worth using where the free engine is weak, and one that degrades past twelve strokes is fine if it is sent ten. The output is a plan for phase 4's request shaping, and a boolean cannot carry it. This phase measures only: nothing here changes what is sent.
  - **Two rules the set has to keep, both learned by breaking them.** A category with no pairs *reports itself unmeasured* rather than vanishing — `Organic` ships empty, because its answers must be drawn rather than computed, and a silently missing row would read as a passed one. And a category has to be able to *fail*: the `Arc` pair passed everything until it carried departure from the free engine's answer, because a chord interpolation is exactly between the keys and betweenness cannot tell the two apart.
  - `FreeEngineArtist` is what keeps the set honest — the deterministic engine as a subject, costing no tokens and running in CI, so a constructed pair it cannot clear is a bug in the pair. It is never wired into the factory; Q32 stands.
  - **The surface is Configure ▸ AI ▸ Grade this model**, a section beside the provider picker and the connection test rather than a page of its own — grading a model belongs next to choosing and testing one. It shows what a run will send *before* it sends it (the full run is ~5× the short one, almost all of it the long ladder), keeps the reading between sessions, and labels it with the model it was taken on: point the connection elsewhere and the page says so in amber rather than letting an old reading pass as a new one. A cancelled run records nothing, because half a ladder reports a limit that is really where somebody clicked.
  - `AiConnection.LastProfile` is **absent unless somebody measured it** — which needed `AiSettings`'s serializer to ignore nulls, since a nullable property alone would have written `"lastProfile": null` into every `ai.json` in existence. That is the half of "optional" `CLAUDE.md` warns is easy to miss, and `AnUnprofiledConnectionWritesNoProfileKey` reads the file rather than the model.
  - Still open: the hand-drawn `Organic` pairs, and the command that captures them from a document. Until then every profile prints that row as *not measured*.
- [x] A frame remembers when an AI drew it `evidence: AiProvenance, AiProvenanceTests, ADocumentThatNeverUsedAiWritesNoAiKey, AnInsertedAiFrameCarriesItsProvenance, ADeterministicInbetweenCarriesNoProvenance`
  - Q31: provenance on the frame — provider, and the model name when there is one — **absent unless AI touched it**, so a document that never used the AI is byte-identical to one from before the feature existed. Frames inserted by an MCP agent carry it too; deterministic inbetweens do not, because the free engine is not an AI. A record, never behaviour: rendering never reads it.

### Reading the drawing

The prerequisite half. Both of these exist to be *inputs to authoring* and neither
may reach a pixel at render time.

- [?] The AI reads the subject before it draws — **split in two, because one half is built and one is gated**
- [x] …the taxonomy half: what a character IS `evidence: SubjectTaxonomy, SubjectPart, SubjectRequest, ReadSubjectAsync, SubjectReadingTests, SubjectReadingWiringTests, DeletingEveryReadingChangesNoPixel, AReadingSomebodyEditedIsNotOverwrittenByAReRead, AFolderThatWasNeverReadWritesNoKey, TheTaxonomyGoesAtTheFrontWhereACachePrefixCanCoverIt`
  - Once per subject, from the sheets the artist drew, kept on the folder that holds them (`ProjectFolder.Taxonomy`) — nullable and absent until read, so a project that never asks writes no key. Reached from the Project panel, because a reading belongs to the folder the drawings live in.
  - **It was `Character.Taxonomy` when Q16 decided this, and that noun is gone.** B114 dissolved `Character` and `ProjectScene` into the folder tree, and **Q40** settled that what is left is a *facet* rather than a kind: a folder with a reading is a folder with a reading, and whether an artist calls it a character, a prop or a crowd is theirs to say. So the guard is `AFolderThatWasNeverReadWritesNoKey` and the menu says *Read this folder…*. Q16's record is left as it was written — it says what was decided then, and this is where the supersession belongs.
  - **434 B, and 0.6% of a realistic 40-stroke request.** Printed against a two-stroke pair as well, where the same block reads as 43% and means nothing — the denominator is two two-point strokes. Both numbers are in the test output on purpose.
  - At the **front** of the request, where prompt caching covers a prefix. After the frame data it would save nothing, and that is a mistake worth only making once.
- [ ] …the placement half: where each part is in one frame `evidence: PartPlacement, PlacementCache, PlacementCacheTests, ARedrawnFrameMissesTheCache`
  - **Deliberately unbuilt, and gated rather than scheduled.** `TaxonomyMeasurementTests` is the gate: does the taxonomy alone measurably improve an inbetween, same keys, same provider, with against without. The harness exists and the two arms are proven to differ in exactly the taxonomy prefix; the live run needs `LIGHTBOX_MEASURE_KEY` and **has not been run**. Until it is, whether this half is needed at all is unknown.
  - Q16 already decided where it would live if it is built: a content-hashed cache beside the autosave, never in the document, because a placement is *derived from* the stroke record and invariant 1 says the record is the document.
  - Inbetweening, inking and normal maps each asked for this separately, which is the signal to design it once — see `docs/DESIGN-subject-reading.md`. Split in two because the halves have different lifetimes: **taxonomy** (this is a biped with these parts) is per character and worth reviewing by hand; **placement** (where each part is in frame 12, what occludes what) is per frame and disposable. The rig's hand-drawn `parts` win wherever they exist — a guess is a default, never an override of something a person stated.
  - The line it must stay on: the reading is an input to *authoring*, never to rendering. Its first test deletes every reading from a finished document and asserts the render is byte-identical, because the day that fails is the day invariant 2 is gone.
  - **Unblocked.** Q16 is answered (c): taxonomy on `Character` in the manifest, placement in a content-hashed cache beside the autosave and never in the document. A placement reading is *derived from* the stroke record, and invariant 1 says the record is the document — so it does not belong in it. Taxonomy escapes that test because it describes a character rather than a drawing, and once an artist corrects it, it is theirs.
  - **Two methods on `IAiArtist`, not one** — different inputs, cadence, lifetime and storage, and the interface is what a reader consults to learn what the app asks a model for. Both satisfy rule 0: the taxonomy starts from a character sheet, the placement from a frame.
  - **Behind a measurement, not a plan.** *Does the taxonomy alone measurably improve an inbetween?* Same keys, same provider, with and against without. If it does, placement is a refinement rather than a requirement; if neither moves the needle, the blindness was not the problem and that finding is worth as much as the feature. `art-director` judges "improves", `ai-engineer` judges the cost — the disagreement G12's pair exists to have. The design pass is the second half of `docs/DESIGN-subject-reading.md`.
- [ ] A light source, for the tools that need to know where the shadows are `evidence: SceneLight, SceneLightTests, ALightNeverReachesStampStroke, ADocumentWithNoLightExportsExactlyAsItDid`
  - On the scene, nullable, absent until placed — the camera's rule. Two uses that must not be conflated: for inking it is a **generation input** (which contours are heavy, which side is in shadow) consumed before there are strokes; for a normal map it is a **preview rig** and must never be baked into the output, because a normal map that carried a light would defeat the reason for having one.

### What it does for the artist

- [x] AI inbetweening `evidence: Inbetweener, InbetweenerTests, IAiArtist`
- [ ] AI-assisted inking, with styles `evidence: InkingStyle, InkingPass, InkingStyleTests, AnInkedPassIsOrdinaryStrokes, WeightFollowsTheLightRatherThanTheStrokeOrder`
  - A style is **a brush preset plus a policy** — weight, taper, depth cue, interior detail, fills — rather than a hard-coded "flat" and "comic". Two modes would be two modes; the axes are what makes the third style somebody asks for reachable. The preset half already exists, so an inking style is an ordinary brush an artist can open and edit.
  - Output is ordinary strokes through `BrushEngine.StampStroke`, so an inked frame replays, undoes and inbetweens like anything else. **Q17 is answered (c):** one **Ink layer for the whole sequence**, its cels lined up with the pencils'. Non-destructive without a layer per frame. It commits the UI too — an inking pass runs over a **range**, not a frame, or the rejected per-frame option arrives by accident.

- [ ] AI dialogue breakdown assistant — auto-generate exposure sheet from audio `evidence: DialogueAnalyzer, PhonemeDetection, DialogueBreakdownTests, AudioFileProducesExposureSheet, PhonemeTimingsAreAccurate, BreakdownIncludesEmotionalBeats`
  - **MARKET-VALIDATED.** Studios cite dialogue sync as consistent bottleneck; timing is currently hand-roughed by animators. Input: voice recording. Output: exposure sheet with phoneme timings (A, E, M, O, U), mouth shape keys, and optional emotional beat markers.
  - **Effort:** Medium (~400 LOC + audio library)
  - **Impact:** Eliminates manual dialogue timing; jump-starts lip-sync workflow
  - **Design decision:** Phoneme-only (A/E/M/O/U) not full Viseme system, because visemes vary per character and artist override matters more than perfection
  - **Blocker:** None — independent; could integrate with timeline or export as separate document
### Normal maps, tier three

- [ ] AI normal maps — improving on the maths and on Laigter, not replacing them `evidence: AiNormalPass, NormalRefinement, AiNormalMapTests, TheBaseMapSurvivesAFailedRefinement, ARefinementIsStoredNotRegenerated, DeletingTheRefinementLeavesTheDeterministicMap`
  - **Deliberately third, and deliberately a refinement rather than a generator.** Tier one bevels the silhouette (built); tier two runs Laigter if the artist has it; this takes whichever of those produced the **base map**, plus the subject reading, and *corrects* it. So the two earlier paths are not a fallback for when this fails — they are its input, and the better the base, the smaller the model's job.
  - **After Laigter for a concrete reason, not politeness.** Two base paths mean two things to improve and something to compare against: the same drawing refined from the silhouette bevel and from Laigter's output tells you how much the model is actually contributing, which is the measurement that decides whether it is worth the request at all. Running it before Laigter would leave that unanswerable.
  - **What only a model can do, stated precisely.** The maths knows where the edge is; it cannot know *what the region is*. A cheek is a dome, a sleeve fold is a crease, hair is stranded, a pauldron is hard-edged and a cloth hem is soft — and the same silhouette bevel is wrong for all five in different directions. This is exactly the case for spending a request: the subject reading names the parts, and the model assigns each part a shape, so the output is the normal that part *should* have rather than the normal its outline implies. That is why the subject reading is its prerequisite and not merely useful.
  - **Determinism, and how this stays inside invariant 2.** The refinement is generated once, at authoring time, and **stored as an artifact on the document** — not re-run at export and never at render. A test deletes it and asserts the deterministic map is what comes back, which is the same test shape the subject reading uses and for the same reason.
  - **The failure mode is worse than a bad inbetween, so the controls are stricter.** A hallucinated shape does not read as a wrong drawing, it reads as *damage*: lighting that contradicts the art, a face that dents. So the refinement is **blendable against the base** with a strength, reviewable side by side with the base, and discardable without regenerating anything. An artist must always be able to get back to the map the maths produced.
  - **Where the two reviewers land, and it is not obvious.** `art-director` judges whether the lit sprite reads and holds the veto on whether a dome is a dome. `ai-engineer` holds the line that this is an authored artifact rather than a render-time pass, and owns the question this feature makes unavoidable: **the cost is per frame, not per character.** A 24-frame cycle is 24 requests unless the reading lets one answer cover the whole cycle, and that is the design's central problem rather than a detail of it — the first thing to measure, before any of the rest is built.
  - **A tier that improves on both must be able to prove it.** Judged against the same sprite lit the same way from all three paths, side by side. If a person cannot tell the refinement from tier one, the request was not worth making and the item stops there — the medium-simulation rule applied to a model.

### Speculative — needs a model, not yet designed

Each of these needs to recognise *what is drawn* rather than measure how it moves,
which is what puts them here rather than with Pillar 4's arithmetic. None has a
design, and several may turn out to be one feature seen from different sides —
the way inbetweening, inking and normal maps each independently asked for the
subject reading.

- [ ] Consistency checker (AI-powered) — flag proportion drift, off-model frames, style inconsistencies `evidence: ConsistencyChecker, ProportionAnalysis, StyleComparison, ConsistencyTests, DriftDetectedOnFrame43, OffModelFramesFlagged, LineWeightInconsistenciesFlagged`
  - **MARKET-VALIDATED, HIGH-IMPACT.** Every studio hand-checks consistency; currently done by eye + director review. Auto-flag saves review cycles and catches missed frames.
  - **Effort:** Medium-High (~500 LOC, depends on subject reading)
  - **What it checks:** Proportions (arms 8% smaller than other frames?), line weight drift, color palette adherence, pose silhouette consistency
  - **Input:** Sequence of frames + reference character model
  - **Output:** Frame-by-frame warnings ("frame 43: right arm 12% smaller") with severity levels
  - **Blocker:** Depends on subject reading being built (uses part placements to measure proportions)
  - **Adoption:** Directors + lead animators use daily; prevents surprises on export

- [?] Inbetween guide generation
- [?] Secondary motion assistant
- [?] Smear frame assistant
- [?] Smart line cleanup suggestions
- [?] Volume consistency checker
- [?] Motion readability analysis
- [?] Flatting assistant — name the regions the geometric flatting pass found (Pillar 0 → Colour), so flats arrive bound to the palette's swatches rather than wearing placeholder colours

---

## Project architecture — reconciling the design with the code

**This section was a list of things the code did not have yet, and it has since
built all of them.** Rewritten 2026-08-06 rather than left standing: a table
saying "no project layer at all" next to `ProjectManifest`, `ProjectIo` and a
nine-panel project docker is not a caution, it is a lie that costs somebody an
afternoon. What follows is what is true now.

| The design says | The code has, 2026-08-06 |
| --- | --- |
| Project containing scenes/characters/assets | `ProjectManifest` + `ProjectIo` + `Project`, a `.lbproj` folder of plain JSON. **Built** |
| Project type chosen at creation | `ProjectType`, nullable, seven-way picker on New project. **Built**, and absent from the file until set |
| Workspace controls which panels are visible | `WorkspaceStore` with per-project-type defaults, saved and persisted. **Built** |
| Character as a reusable asset with animations under it | `Character` with animations, a palette, variants and `CharacterLibrary`. **Built** — and Q30 makes it a folder that carries this data rather than a second kind of container |
| Scene-based / character-based / asset-based organization | `ProjectFolder` — arbitrary names, any depth, tags — beside `Character` and `ProjectScene`. **Built**; Q30 collapses the three into one tree |

**What is left is not a container, it is the collapse.** The gap this section
was written to name is closed; the gap that replaced it is that there are now
*three* ways to group drawings where there should be one, which is Q30 and is
answered rather than open.

Three consequences worth stating before anyone builds against the design:

1. **There are two hierarchies until Q30 lands, and code has to know which it
   holds.** The folder tree is arbitrary and id-based; `Character` and
   `ProjectScene` still build paths from fixed words in `ProjectIo`
   (`characters/<slug>/animations/`, `scenes/<slug>/shots/`). Anything written
   now against `Project.Characters` or `Project.Scenes` is written against the
   half that is going away — prefer the folder tree and `DocumentRef` where
   there is a choice.
2. **Optional must stay absent.** `Scene.Camera` and `Scene.Pivot` are
   nullable and serialize to nothing when unset, and the same discipline has
   to hold for project type and workspace. An illustration document must not
   start carrying game-art keys because the feature exists.
3. **The shared floor is already shared.** Brush engine, rendering, layers,
   colour, file format and selection are common today, which is exactly what
   the design assumes. No restructuring is needed there — only above it.

### Reach and configuration — the split nobody has made yet

Features have arrived here from two directions: some from a frustration somebody
actually had, some from research into what a production needs. Nobody has yet
said **which features belong to which kinds of work**, and the absence shows up
as a question that gets answered differently each time it comes up.

The decision, and it applies to everything from here on:

> **Every feature is reachable in every project type. A project type sets
> defaults, never availability.**

An artist doing a comic who wants an exposure sheet gets one. Somebody drawing a
single illustration who wants the camera can have it. What a project type decides
is what is *on*, what is *in front of you*, and what a new document starts with —
not what the application is capable of.

**How this composes with "optional means absent", which it does not contradict:**

| Rule | What it governs |
| --- | --- |
| *Optional means absent, not disabled* | The **record**. An unused feature writes no keys, ever. |
| *Every feature is reachable* | The **capability**. No feature is locked behind a project type. |

Both hold at once, and the camera is already the proof: absent from the file
until authored, absent from the UI until asked for, and available to ask for in
any document. What must not happen is the third thing — a capability that cannot
be reached because of a value in a manifest.

**One place already breaks this rule, and it is worth naming rather than
quietly fixing.** `CharacterLibrary.Scan` returns nothing unless
`Manifest.Type == AssetLibrary`, and the test guarding it says that is
"what makes the project type mean something rather than being a label on an
enum." That was a reasonable reading of the design then and it is the opposite of
the rule above. Under the rule, a project's characters are offerable from any
project and the Asset Library type *defaults to publishing them* — which is what
somebody actually wants when they file a character in a game project and then
need it in a short.

### The container and the types

- [x] `Project` container above `Doc` (scenes, characters, assets) `evidence: ProjectManifest, Character, DocumentRef, ProjectTests`
- [x] Project type recorded on the document, absent by default `evidence: ProjectType, AProjectWithNoTypeWritesNoTypeKey`
- [?] Named workspaces, persisted
- [?] Storyboard organization (scenes → shots)
- [?] Comic organization (pages → panels)
- [?] Panel tools and speech balloons
- [?] Print workflow (CMYK, bleed, DPI targets)
- [~] Asset Library project type `evidence: CharacterLibrary, OnlyAssetLibraryProjectsOfferTheirCharacters`

### Making reach unconditional

- [x] One registry of features, their defaults per project type, and nothing gated `evidence: FeatureDefaults, FeatureKey, FeatureDefaultsTests, EveryFeatureIsReachableInEveryProjectType, AProjectTypeSetsDefaultsRatherThanAvailability, AFeatureLeftAtItsDefaultWritesNoKey`
  - The registry is the point, and it is the same argument as `ShortcutMap`: the reason to have one is that something else enumerates it. The Configure window needs to list what can be turned on, the new-document path needs the defaults for a type, and the manual needs to say which is which. Three places deriving that from one table cannot disagree; three places each deciding for themselves already have.
  - Derived defaults, not copied ones. A document stores only what its artist changed, so a default that moves in a later version moves for every document that never overrode it — the same reason `BrushCostOf` computes rather than stores.
- **Feature conflicts — built, then removed with their only tenant (2026-08-12).** `FeatureConflict`/`FeatureConflicts` landed 2026-08-08 declaring exactly one incompatibility, unbounded canvas against fixed frame-bounds sprite export, and the infinite canvas's removal left the registry empty — so it went too, rather than surviving as machinery nobody consults. The *shape* remains the decided design for the next real incompatibility: declared **between features, never on a project type**; refused **with its reason, never hidden**; and **resolvable** by authoring the missing thing, which is what separates a conflict from a gate. The implementation is one `git log -S FeatureConflict` away.
- [ ] Changing a project's type changes defaults and never removes work `evidence: ProjectTypeChangeTests, ChangingProjectTypeKeepsEverythingAlreadyAuthored, ChangingProjectTypeMovesOnlyUntouchedDefaults`
  - The consequence of the rule that costs the most to get wrong. If availability were gated, switching a project from Shot to Asset would have to decide what happens to the camera somebody keyframed. With reach unconditional there is nothing to decide: the camera stays, it is simply no longer part of what a new document in that project starts with.
- [ ] A project's characters are offerable from any project type `evidence: CharacterLibraryReachTests, AnyProjectCanPublishItsCharacters, TheAssetLibraryTypeDefaultsToPublishing`
  - The one existing violation, fixed rather than left as a footnote. `CharacterLibrary.Scan` currently returns nothing unless the manifest says `AssetLibrary`, so a character filed in a game project is unreachable from the short that needs it — and the only way out is to change the project's type, which is a decision about the whole project made to solve one lookup.
  - `OnlyAssetLibraryProjectsOfferTheirCharacters` is the test that pins the old behaviour, and it should be **rewritten rather than deleted**: the thing worth guarding is that the Asset Library type still *defaults* to publishing, which is the part that made the type mean something.
  - **Q30 reached the same conclusion from the other direction, which is the strongest evidence either had.** This item argued it from the reach rule — nothing locked behind a manifest value. Q30 answered it from the workflow — the character library and the asset library become project-based, with creating into them and saving to them available project-wide. Two independent routes to one answer, so this is settled rather than merely proposed. Q30 is the wider change and this item is a subset of it; build it here if it lands first, and delete it as done if Q30 does.

---

## Market-Validated Priorities (2026 Studio Research)

Based on research into professional animation studios, competitor tools, and animator pain points, the following items are market-validated and should be prioritized for adoption advantage.

### **Tier 1: Immediate Market Advantage** (Do these first)

These items are requested by studios, missing from competitors, and unblock other work:

| Item | Pillar | Why Market Needs It | Effort | Impact | Blocker |
|------|--------|-------------------|--------|--------|---------|
| **Version Control (Perforce/UVCS)** | 6 | Without locking, two artists = merge conflict on binary art. Industry standard workflow | High | CRITICAL | Real Perforce test |
| **Frame Comments** | 6 | Teams need non-destructive review. Every studio uses Frame.io for this; Lightbox has no equivalent | Medium | High | None |
| **Undo History Browser** | 6 | Long projects = 50+ undos; artists cannot navigate. 78% of animators cite this frustration | Medium | High | None |
| **Procedural Directional Generation** | 5 | **70% of sprite time** spent on 8-directional variants (192 manual frames per character). Unique to Lightbox | Medium | CRITICAL | None |
| **Hot-Reload to Game Engine** | 5 | Export/reimport cycle kills iteration rhythm. Differentiates Lightbox from Adobe/Toon Boom | High | High | None |
| **Consistency Checker (AI)** | AI | Every studio hand-checks frames; no tool automates this. Prevents surprises on export | Medium | High | Subject reading |
| **Dialogue Breakdown Assistant** | AI | Dialogue sync is consistent bottleneck; currently all manual. Market moving toward automation | Medium | High | None |

### **Tier 2: Studio Production Features** (Build after Tier 1)

| Item | Why Market Needs It | Effort | Dependency |
|------|-------------------|--------|-----------|
| **The project window** | ShotGrid replacement for small studios; eliminates spreadsheet maintenance | Medium | B114 one tree (landed) |
| **Animatic Preview Export** | One-click timing render saves manual video editing cycle | Low-Medium | None |
| **Version Snapshots** | Hand-offs between artists; manual checkpoints of document state | Medium | Undo browser |
| **Subject Reading** | Prerequisite for inking, normal maps, consistency checking; unlocks 3 features | Medium | None — Q16 and Q17 both answered |

### **Tier 3: Competitive Differentiation** (Polish phase)

- Clip library with timing variants
- Collaborative palette sync
- Style guide enforcement (AI-powered)
- Animated reference layer workflow

### **Market Positioning vs. Competitors (2026)**

**Lightbox's unique strengths:**
- ✓ Game asset pipeline (sprite sheets → collision data → engine exports)
- ✓ Procedural directional generation (unique in market)
- ✓ Hot-reload to running game (unique in market)
- ✓ Frame-by-frame + symbol hybrid (only real alternative to abandoned Adobe Animate)

**Lightbox's gaps (competitors ahead):**
- Rigging UI (Harmony, Moho have this; Lightbox treats rigs as symbol collections)
- Real-time collaboration (Figma has set expectations; animation tools lag)
- Production tracking (studios use ShotGrid; Lightbox has no dashboard)

**Market opportunity:**
Lightbox can own the **affordable, game-focused, modern hand-drawn animation space**. Position as:
- "The modern Animate replacement" (Adobe discontinued; Photoshop is inadequate)
- "The game dev animation tool" (Sprite sheets + directional generation + hot-reload)
- "The studio-friendly frame-by-frame tool" (Harmony costs $1000+/year; Lightbox can be 10× cheaper)

### **Why These Research Items Exist**

Two feature request sets (Request 1: Production/Studio, Request 2: Technical/Animation Architecture) were compared against the roadmap in July 2026. Analysis revealed:
1. **Strategic gap:** Production review and collaboration features are unbuilt [?] while game export is mature [x]
2. **Market demand:** Studios explicitly need version control, frame comments, undo navigation, directional generation
3. **Competitive gap:** No animation software offers procedural directional generation or hot-reload; these are unique opportunities
4. **Urgency:** Hand-drawn animation market is consolidating around Harmony (expensive) and OpenToonz (dated, free). Lightbox has a window to own the modern affordable space.

---

## Market-Validated Priorities: Brush Engine & Vector Tools (2026 Market Research)

Based on competitive analysis of TVPaint, Clip Studio Paint, Krita, Procreate, Harmony, Linearity, and Affinity Designer, Lightbox has **exceptional raster brush capabilities** that meet or exceed industry standards. However, critical gaps in vector tooling and market-validated brush features create friction for professional workflows.

### **Key Finding: Lightbox's Raster Brush Advantage**

Lightbox matches or beats competitors in:
- ✓ Pressure curves (drawn curves, not gamma lookup tables)
- ✓ Shape dynamics (size, roundness, rotation jitter)
- ✓ Color dynamics (HSV jitter, secondary colors)
- ✓ Texture brushes (paper, canvas, imported)
- ✓ Medium simulation (watercolor, oils, gouache)
- ✓ Procedural tip generation (circle, soft, ring, chisel, hatch)
- ✓ **Deterministic rendering (Lightbox only)** — strokes render identically on reload, AI inbetween, undo
- ✓ **Brush cost badging** (marks expensive brushes before selection)

Lightbox differs from competitors in determinism + medium simulation, a combination no other animation tool offers.

### **Vector Tooling Gaps (Critical)**

| Feature | Lightbox | Harmony | Linearity | Affinity | Status |
|---------|----------|---------|-----------|----------|--------|
| **Stroke reshaping (path editing)** | [ ] | [x] | [x] | [x] | Missing |
| **Bezier curve handles** | [ ] | [x] | [x] | [x] | Missing |
| **Adaptive/variable-width strokes** | [ ] | [x] | [x] | [x] | Missing |
| **SVG export (real paths)** | [ ] | [x] | [x] | [x] | Missing |
| **Vector + raster same model** | [x] | [ ] | [ ] | [ ] | **Unique** |
| **Textured vector strokes** | [x] | [ ] | [ ] | [ ] | **Unique** |

**The gap**: VectorFrame exists but is read-only. An artist draws a vector stroke but cannot reshape it afterward.

### **Tier 1: Vector & Brush Market Priorities** (Unblock vector workflow)

| Item | Pillar | Market Gap | Effort | Impact | Blocker |
|------|--------|-----------|--------|--------|---------|
| ~~**Resolve Q19: Seed origin for path editing**~~ | 0 | **Done, and the number was wrong.** The question is **Q26**, not Q19 (Q19 is the Linux/macOS shipping question). Answered 2026-08-07 (a): accept the grain shift. Arc-length seeding — which this table recommended — is *rejected*, not chosen | — | — | — |
| **Stroke path reshaping (+ Bezier editing)** | 0 | **100% of vector tools have this.** Professional illustrators cannot work without stroke editing. **Unblocked since Q26**; design in `docs/DESIGN-vector-tooling.md`, and handles ride on an optional `Stroke.Path` rather than a widened `StrokePoint`, so there is no migration | High | CRITICAL | None |
| **Per-layer onion skin control** | 4 | Show layer history independently. Rare feature; unique to Lightbox. Unblocks animation workflow refinements. | Medium (300 LOC) | Medium | None |
| **Pressure curve standardization** | 4 | Unsolved workflow gap: artists re-calibrate pressure in Clip Studio vs Procreate vs Adobe. First standardized import/export. | Low (150 LOC) | Medium | None |
| **Tilt & velocity recording** | 4 | High-end tablet support. Clip Studio, Procreate, Corel all have this. Medium artist request. **Phase 1 landed 2026-08-18**: the record carries optional per-point tilt and speed, captured behind an opt-in preference that is saved, with the axes absent from the file unless recorded. Phases 2–3 — brushes that *use* them, and the preset/importer surfaces — remain, per `docs/DESIGN-pen-dynamics.md` | Medium (600 LOC) | Medium | None — phased in the design doc |
| **Symmetry & mirrored painting** | 0 | Essential for character design. Every professional tool has it. **Unblocked since Q15** — one stroke while drawing, with an explicit "break symmetry" that expands to two, and `Mirror` on the stroke rather than the scene | Medium | High | None |
| **SVG export with real paths** | 4 | Asset interoperability. Illustrators expect SVG export. Currently only raster-painted SVG (dishonest). | Medium (300 LOC) | Medium | Stroke reshaping |

### **Why These Matter Competitively**

**Market Positioning:**

1. **Vector strokes that stay textured when edited (Lightbox only)**
   - Every vector tool (Harmony, Linearity, Affinity) exports flat outlines
   - Lightbox VectorFrame uses Stroke record → strokes are textured marks
   - Once reshaping is built, Lightbox can claim "Vector editing with real media feel"
   - Market gap: **zero competitors** position this way

2. **Deterministic rendering + AI inbetweening reliability (Lightbox only)**
   - Professional complaint: "Cascadeur 2025.1 AI inbetweening is new; we don't trust it yet"
   - Lightbox: Invariant 2 guarantees input → output reproducibility
   - Market opportunity: "AI inbetweening you can trust" positioning

3. **Pressure curve standardization (first in market)**
   - Pain point: Artists re-calibrate curves for each tool
   - Lightbox could export ResponseCurve as JSON, importable into Clip Studio via `.abr`
   - Market differentiation: "First tool that treats curves as portable assets"

4. **Per-layer onion skin (Lightbox only)**
   - Current tools: binary on/off per axis (show all past / show all future)
   - Lightbox opportunity: Show only this layer's history, not scene history
   - Niche feature but powerful for animation rhythm discovery

### **Professional Pain Points Lightbox Addresses**

**Pain Point 1: "Vector editing is separate from raster"**
- Complaint: Illustrators switch engines (Illustrator → Procreate) for raster, back for vector
- Lightbox solution: Single document with raster + vector, same stroke model
- Gaps: Only raster painting exists; vector editing is missing

**Pain Point 2: "Pressure response is inconsistent across tools"**
- Complaint: Pressure curves don't transfer; artists re-configure per tool
- Lightbox solution: Export curves as JSON, importable into other tools
- Market gap: No tool currently offers this

**Pain Point 3: "AI inbetweening can't be trusted"**
- Complaint: Some tools render output differently on replay (stochastic rendering)
- Lightbox solution: Invariant 2 guarantees reproducibility
- Positioning: "Inbetweening you can audit"

**Pain Point 4: "Brush texture is flat" (Adobe Animate)**
- Complaint: Adobe Animate brushes read as "extremely fake" with no variation
- Lightbox advantage: Medium simulation, scatter, wet edge → textured brushes
- Market gap: **Zero vector tools have medium simulation**

### **Roadmap Impact: Items to Upgrade/Add**

**Pillar 4 (Animation-aware drawing tools) — Raster Brushes**

1. **Upgrade**: "Tilt and speed reach the stroke record" [ ]
   - Add market validation: Clip Studio, Procreate, Corel all have this
   - Evidence: TiltTests, SpeedTests, StrokePointDensityTests
   - Blocker: StrokePoint record change (migration required)

2. **New Item**: "Pressure curve export/import — portable across tools" [ ]
   - Unique market position: First standardized curves
   - Effort: 150–200 LOC (ResponseCurve → JSON export, import from clipboard)
   - Evidence: PressureCurveExportTests, InteropTests

**Pillar 0 (The drawing floor) — Vector Tools.** *Filed under Pillar 0, where the
roadmap body files it. Earlier revisions of this appendix said Pillar 4 and even
wrote "Pillar 4 (Drawing floor)", conflating the two.*

3. ~~**New Item**: "Resolve design question Q19 — seed origin for path editing"~~ **[done, and misnumbered]**
   - The question is **Q26**, not Q19. Q19 is *"Are Linux and macOS shipping targets?"*, answered (a). The duplicate-numbering that caused this is filed as **B81**.
   - **Answered 2026-08-07 (a): accept the grain shift** — "the grain belongs to the canvas". A per-stroke seed origin, arc-length seeding and a blended re-seed radius are all *rejected*, so nothing here needs a new field and no tunable enters the render path.
   - Reshaping, symmetry and multi-capture tips are therefore **not blocked** and have not been since that date.

4. **Upgrade**: "A drawn line can be re-shaped and keeps the mark it was drawn with" [ ]
   - Current status: [unbuilt] with design question unresolved
   - Market: 100% of vector tools have this; professional requirement
   - Effort: 800–1200 LOC (PathEditSession pattern, undo integration, render preview)
   - Evidence: PathEditSession, StrokeReshapeTests, TextureConsistencyTests
   - Blocking: SVG export, Bezier editing, sub-pixel precision

5. **New Item**: "Stroke shapes — Bezier curve handles for precision editing" [ ]
   - Dependency: Stroke reshaping (above)
   - Market: Harmony, Affinity, Illustrator all have this
   - Effort: 400–600 LOC
   - Evidence: BezierHandleTests, CurveEditorIntegrationTests

6. **New Item**: "Per-layer onion skin configuration" [ ]
   - Dependency: None (UI + cache filtering only)
   - Market: Rare feature; Lightbox could be first
   - Effort: 300–400 LOC
   - Evidence: OnionSkinLayerTests, CacheFilteringTests, LayerGhostingTests

7. **Upgrade**: "Symmetry and mirrored painting" [ ]
   - Current status: [unbuilt] with design question around dab re-seeding
   - Market: Essential for character animation; every tool has it
   - Effort: 400–600 LOC (SymmetryAxis, mirror stroke generation, cache invalidation)
   - Evidence: SymmetryTests, MirroredStrokeTests, DeterministicMirrorTests

**Pillar 4 (Drawing floor) — Vector Export**

8. **Upgrade**: "Save as SVG — honest vector export" [ ]
   - Current status: [unbuilt] with note "honest for vector layers only"
   - Market: Asset interoperability; studios expect SVG
   - Dependency: Stroke reshaping needs to exist first
   - Effort: 300–500 LOC (VectorFrame → SVG serializer)
   - Evidence: SvgExportTests, PathSerializationTests, RoundTripTests

### **Why This Order Matters**

1. ~~**Resolve Q19 first**~~ — **done; and it was Q26.** Answered 2026-08-07 (a). Nothing is blocked on it
2. **Path editing first, then** — Unblocks SVG export and Bezier editing; makes the vector layer usable. Phased in `docs/DESIGN-vector-tooling.md`: pick a line, then the path record, then isolation mode, then the pen
3. **Pressure curves third** — Quick win, market differentiation, no blocking
4. **Tilt/velocity fourth** — Important for pros, but non-blocking; medium effort

---

## Market-Validated Priorities: Character Sheets & Reference Integration (2026 Market Research)

Based on professional animation studio workflows and competitor analysis, Lightbox has **excellent foundational character management** but critical gaps in reference usability that force animators to manage references outside the tool. Context switching costs ~4 hours per week per animator (1,200 app toggles daily, 20–23 min recovery per switch).

> **Read this whole section against Q30, which was answered after it was written (2026-08-06).**
>
> The research below is sound and the pain points are real; what has moved underneath it is **where the data hangs**. Q30 settled that a character stops being a separate kind of thing: one hierarchy, resources declared on folders and accumulating down the tree with the nearest declaration winning ties. Three consequences change how these items are built, none of them changes whether they are worth building:
>
> | This section says | Q30 says |
> | --- | --- |
> | Serialize reference position "to **character metadata**" | There is no character metadata as a distinct record. It hangs on the scope that owns the reference — a folder, or the document |
> | **Character sheet** version tagging, character sheet versioning for teams | Sheets are **folder-scoped**. "Character sheet" is a reference sheet declared at some scope, and versioning tags that scope |
> | **Character library** and asset library as character-centric | Both become **project-based**: creating into them and saving to them is available project-wide, not as a property of a character or of the Asset Library project type |
>
> Read every "character" below as "the scope this belongs to" and the items survive intact. Build them against `Project.Characters` and they will be rewritten by Q30. The one thing genuinely settled and safe to rely on: **a `ReferenceSheet` already lives in `Doc.ReferenceSheets`** (Q25), so it belongs to a document rather than to a character today — folder scope is additive to that, not a migration of it.

### **Lightbox's Character Management: Strong Foundation**

Already built ✅:
- Character workspace (animations, assets, references, palette unified)
- Character library with import/export
- Character variants (different palette/animation overrides)
- Reference sheets (multi-view layer stacks: Front, Side, Back, Expressions)
- Reference strips (imported animation cycles with per-frame alignment)
- Shared palette across character animations
- **Deterministic rendering** (enables reference-aware brushes — unique capability)

- A sheet view taped onto the canvas — flattened into a `ReferenceStrip`,
  pinned to every frame, live (Q69)
- [x] **A projected reference is an object on the canvas: select it, move it, scale it, lock it** `evidence: ReferenceBoxPainter, SelectReferenceOnCanvasAt, BeginReferenceGesture, ScaleReferenceAbout, ReferenceCanvasSelectionTests, ClickingAProjectedReferenceSelectsIt, ACornerScalesUniformlyAboutTheOppositeCorner, ALockedReferenceStillSelectsAndRefusesToMove`
  - **What it fixes: a reference on the canvas answered to a mode, not to the
    pointer.** Moving one meant finding *Align on canvas* and switching it on
    first; scaling one meant leaving the canvas for a slider in the docker; and
    once a plate was registered against the drawing, nothing stopped the next
    drag knocking it out. The Arrow now picks a projected reference up the way it
    picks up a line, a guide or a symbol (Q108).
  - **Uniform scaling from the corners, and that is a rule rather than a
    shortcut.** A sheet carries one scale for every frame because a reference
    whose size varied frame to frame is the one thing a size reference exists to
    prevent — and the same argument refuses a non-uniform drag: an artist working
    from a plate stretched on one axis is drawing the wrong proportions.
  - **The lock is the guide lock, applied to reference**: per sheet and
    undoable, plus a workspace-wide sweep on `Ctrl+Alt+R`. A locked reference
    still selects and draws its box without grips, so the refusal is visible
    rather than mysterious. Both locks are on the canvas shortcut bar while a
    reference is selected, which is where the hand already is.
  - Closes **B192** on the way: a drag is one undo step now, and repaints through
    the cheap path B191 opened rather than the document-changed storm.
- [x] **The reference board — a whiteboard of reference beside the art** `evidence: ReferenceBoard, BoardTile, BoardLayout, ProjectBoards, ReferenceBoardViewModel, ReferenceBoardWindow, ReferenceBoardTests, ReferenceBoardWindowTests`
  - **What it replaced, and why the replacement is not a superset.** Q69 shipped
    one live window per reference view, which was right about *live* and wrong
    about *one*. An artist works from several references at once, so that meant
    several windows, each framing one picture and none of them arrangeable
    against another — the arrangement, which is the actual work, had nowhere to
    live. The board is the same liveness with the arrangement as the feature, and
    the single-view window is deleted rather than kept beside it: two windows both
    claiming to show a reference view is how B133 started.
  - **Every sheet in scope, flattened, laid out to fit** — a view is one picture
    on the wall rather than a layer stack, through the same
    `RenderReferenceViewPng` the AI payloads and the taped strip use, so the wall
    follows an edit without a re-import. Imported files and pictures dragged off a
    web page sit beside them. Move by dragging, resize from a corner, raise by
    picking up, **Auto-arrange** to tidy, right-click to send behind or take down.
  - **The arrangement persists, filed on the scope that owns the references**
    (Q87) — one wall per subject, shared by every animation under it, which is the
    "reference positioning persists" gap this section calls the highest friction
    in the list. A scope with no board writes no file; a loose document keeps its
    own board inside itself, because it has no project directory to copy an
    imported picture into.
  - **Imports are copied into the project, never linked.** A path into somebody's
    downloads folder breaks silently, and a picture dragged out of a browser has
    no durable path at all.

- [x] **Project a board reference onto the canvas** `evidence: ToggleTileOnCanvas, BoardTileId, MoveReferenceInStack, ReferenceStripAt, BoardProjectionTests, ProjectingAnImageTile_TapesItsPictureToTheCanvas_Linked, TheStackMenuMovesAReferenceForwardAndBack, RightClickFindsTheTopmostReferenceUnderThePoint`
  - **Any tile, not only sheet views** — right-click a picture on the wall and
    it is taped onto the canvas as a pinned `ReferenceStrip`: over the paper
    (so an opaque background layer does not hide it), under every drawing
    layer, never a layer of its own, never in an export. A view tile's
    projection *is* the taped view — one toggle, shared with the docker's
    button — and an image tile keeps its link through `BoardTileId`, which is
    what makes projecting a toggle instead of an accumulation.
  - **The stack is workable from the canvas.** Right-click a projected
    reference to select it and bring it forward or back among the other
    references, or take it down; grabbing in align mode picks the reference
    under the pointer before dragging it. List order is z-order, the same
    back-to-front reading the compositor and the board itself use.

- [x] **Lay a board reference onto the timeline as frames** `evidence: ImportBoardTileAsAnimation, LayingATileOntoTheTimeline_SlicesFramesFromThePlayhead, LayingATileWithNoPicture_ImportsNothing`
  - **The Reference docker's analysis, fed from the wall.** The docker's ＋
    import — slice the sheet, lay the cells from the playhead, grow the
    timeline to fit — was reachable only through its own file picker and the
    window drop, while the board became where references actually arrive. One
    context-menu item closes the gap by calling the same `ImportReference`
    path, and the docker is revealed on success because the grid, scale and
    alignment adjustments live there.
  - **An import, not a projection** — no `BoardTileId`, no pin: laying twice
    is two imports like pressing ＋ twice, and taking a projection down must
    never delete an animation somebody laid out.

Next for the board, deliberately not in the first cut (Q69, Q87):
- [ ] **An editable canvas on the reference board** `evidence: ReferenceViewCanvasTests`
  — draw on a sheet where it hangs, instead of switching to its tab. Needs input
  routing and a decision about shared-versus-split brush state, so it starts as a
  design note, not a feature branch. A tile is one `Image` control precisely so
  this replaces one control when it comes.
- [ ] **Annotating the board non-destructively** `evidence: BoardAnnotation, BoardAnnotationTests`
  — the market gap named below, and the board is where it now belongs: marks over
  the wall rather than over one view, kept apart from the art they describe.

### **Critical Usability Gaps: Reference Management** ❌

| Feature | Impact | Status | Competitors | Gap |
|---------|--------|--------|-------------|-----|
| **Reference positioning persists** | HIGH | [x] | Harmony, Clip Studio, Aseprite save this | Closed by the reference board (Q87): the wall is filed on the scope that owns the references, so it opens where it was left |
| **Non-destructive annotation layer** | MEDIUM | [ ] | Zero competitors | **Pure market gap** |
| **Character version tagging** | CRITICAL | [ ] | Enterprise tools only | Indie teams: "final_v7_REAL.psd" chaos |
| **Expression/pose metadata** | MEDIUM | [ ] | No animation tools | Expressions scattered in files |
| **Real-time consistency checking** | MEDIUM | [ ] | Emerging (ModelSheetAI) | Not mainstream |

### **Tier 1: High-Impact Pain Relief** (Low Effort, Unblocked)

| Item | Pillar | Why | Effort | Impact | Blocker |
|------|--------|-----|--------|--------|---------|
| ~~**Reference positioning persists**~~ — done | 1 | Repositioned every session — highest friction | Low (100 LOC) | HIGH | Closed by the reference board |
| **Character version tagging** | 1 | Out-of-sync versions mid-project cause rework | Low (100 LOC) | CRITICAL | None |
| **Non-destructive annotation layer** | 1 | Artists mark up reference (proportions, anatomy); zero tools let them do this non-destructively | Medium (300 LOC) | Medium | None |
| **Expression/pose frame metadata** | 1 | Scattered files (happy.png, sad.png); no query capability | Medium (200 LOC) | Medium | None |

### **Tier 2: Differentiation** (Medium-High Effort, Blocks Next)

| Item | Why | Effort | Blocker |
|------|-----|--------|---------|
| **Lightweight character versioning** | Indie alternative to $10k enterprise tools (Toon Boom Server, Perforce) | Medium (600 LOC) | Tier 1 version tagging |
| **Deterministic reference-aware brushes** | Lightbox-only: brush responds to reference geometry, reproducibly (invariant 2) | High (600 LOC) | Reference geometry API |
| **Character semantic database** | Store character as queryable data; export as FSM for game engines and AI agents | High (800 LOC) | Metadata structure |
| **AI consistency checking** | Real-time "is this frame on-model?" verification | High (800 LOC) | Subject reading |

### **Tier 3: Emerging (2026+)**
- AI pose estimation overlay (sketch → skeleton detection → pose transfer)
- Multi-device reference sync (desktop + tablet)
- MCP surface for character data (agents reason about character intent)

### **Market Positioning: Where Lightbox Wins**

1. **First with non-destructive reference annotation** (zero competitors)
   - Locked layer for construction lines, proportions, anatomy notes
   - Cannot be painted; toggled independently
   - Not exported to sprite sheet

2. **Reference positioning that persists** (matches competitors, quick win)
   - Harmony, Clip Studio, Aseprite all save position
   - Lightbox should too (100 LOC)

3. **Indie-friendly character versioning** (market gap between freelance chaos and enterprise $10k)
   - Lightweight Git-like tagging for character sheets
   - No Perforce/Toon Boom Server needed

4. **Deterministic reference-aware brushes** (Lightbox-only capability)
   - Brush behavior responds to reference geometry
   - Same reference + same stroke → same output (reproducible)
   - No other tool can do this without breaking invariant 2

### **Roadmap Items to Add**

**Pillar 1 (Scope-based projects) — Reference Usability**

1. **New Item**: "Reference positioning and scale persist across sessions" [ ]
   - Serialize reference position/scale/rotation/opacity to character metadata
   - Restore automatically on file open
   - Effort: 100–150 LOC
   - Evidence: ReferencePositionTests, PersistentStateTests, RestoringReferenceRecoversState

2. **New Item**: "Non-destructive locked annotation layer on reference" [ ]
   - Layer on top of reference for construction lines, proportions, notes
   - Locked against painting but editable for annotations
   - Toggle visibility independently
   - Not exported to sprite sheet or game engine
   - Effort: 300–400 LOC
   - Evidence: AnnotationLayerTests, LockedLayerTests, AnnotationNotExportedToSpriteSheet

3. **New Item**: "Character sheet version tagging and frame-to-version linking" [ ]
   - Tag character sheets with version (v1, v2, v3)
   - Link animation frames to character version ("use v2 for frames 1–50")
   - Export warning if frame uses outdated version
   - Effort: 150–200 LOC
   - Evidence: CharacterVersionTaggingTests, FrameVersionLinkTests, OutdatedVersionWarnings

4. **New Item**: "Expression and pose metadata tagging on animation frames" [ ]
   - Tag frames with expression/emotion/action (happy, running, idle, etc.)
   - Query frames by expression type
   - Export as structured data for game FSM and AI agents
   - Effort: 200–300 LOC
   - Evidence: ExpressionTaggingTests, FrameQueryTests, ExpressionMetadataExport

**Pillar 1 (Scope-based projects) — Team Collaboration**

5. **New Item**: "Lightweight character sheet versioning for team distribution" [ ]
   - Simple Git-like tracking for character versions
   - Tag, compare versions, distribute updates to team
   - Per-animator pull of latest version with warning on outdated
   - Effort: 600–800 LOC
   - Evidence: CharacterVersioningTests, TeamDistributionTests, VersionComparisonTests
   - Blocker: Character version tagging (item #3 above)

**Pillar 4 (Animation-aware drawing tools) — Reference-Aware Rendering**

6. **New Item**: "Reference geometry influences brush behavior deterministically" [ ]
   - Brush responds to reference position/geometry
   - Stroke spacing adjusts based on distance from reference feature
   - Stroke rotation aligns with reference angle
   - Same reference + same stroke input → same output (reproducible)
   - Effort: 600–800 LOC
   - Evidence: ReferenceAwareBrushTests, DeterministicReferenceTests, SpatialSeedingTests
   - Unique to Lightbox (depends on invariant 2: no randomness)

**AI Assistance — Consistency & Semantics**

7. **New Item**: "AI-powered on-model consistency checking" [ ]
   - Background process compares current frame against character master sheet
   - Flags inconsistencies (color drift, proportion deviation, missing features)
   - Shows consistency score per frame (0–100%)
   - Highlights problem regions for artist correction
   - Effort: 800–1200 LOC
   - Evidence: ConsistencyCheckTests, FrameComparisonTests, ScoreCalibrationTests
   - Blocker: Subject reading

8. **New Item**: "Character as semantic database (queryable, exportable)" [ ]
   - Store character metadata as structured data (poses, expressions, proportions, versions)
   - Query: "all frames where character is happy" or "all walk cycle frames"
   - Export as data structure (JSON, FSM format) for game engines
   - Enable MCP surface for agents to reason about character intent
   - Effort: 800–1200 LOC
   - Evidence: SemanticDatabaseTests, QueryTests, ExportFormatTests, MCPSurfaceTests

### **Why This Order Matters**

1. **Tier 1 first** — Four items, 550–700 LOC total, unblocked, address highest pain (reference repositioning)
2. **Tier 2 second** — Differentiation and market positioning; some Tier 1 dependencies (version tagging blocks versioning)
3. **Tier 3 future** — Emerging AI technologies; research-stage tooling (Sketch2PoseNet 2025)

---

## How this file stays true

- `scripts/roadmap.py check` runs in the improvement loop's verify step and in
  the Stop hook. Drift between the marks and the code fails it.
- Landing a feature means adding its evidence anchors in the same commit.
  A commit that ships a feature and leaves its box unticked is incomplete in
  the same way a commit without a test is.
- `[?]` items are a backlog of their own: each one is a feature nobody has
  said how they would recognise.
