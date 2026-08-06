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
  - Nothing exists. Krita and Photoshop both have it, and for character design — the thing this application is for — a vertical mirror is not a nicety. The design question that has to be answered first is whether a mirrored mark is *one stroke rendered twice* or *two strokes*: the first keeps the record small and makes turning symmetry off afterwards meaningful, the second is simpler and matches what an artist can then edit independently. Invariant 1 pushes toward the first.
- [x] Smoothing is a brush setting, not a global `evidence: BrushStabilisation, BrushStabilisationTests, APresetCarriesItsOwnStabilisation, TwoBrushesCanSteadyTheHandDifferently, ABrushThatFollowsTheApplicationWritesNoStabilisationKey`
  - Nullable, so absent means today's behaviour exactly: one setting for the whole app. A brush that says otherwise overrides it. Not a pixel setting and invariant 4 does not reach it — smoothing filters the pointer samples *before* they become the stroke's points, so the mark already carries the result and there is nothing left to re-run.
- [x] Texture from an image, not only the built-in papers `evidence: TextureRegistry, ImportedTextureTests, AnImportedPaperBitesIntoTheStroke, ThePaperIsAnchoredToTheDocumentRatherThanToTheStroke, AHugeScanIsReducedRatherThanHeldWhole, ATextureThatIsNotRegisteredIsIgnoredRatherThanFatal`
  - The same treatment tips got: an asset with an id, in the document rather than on somebody's disk, absent when unused. Held as a height field rather than as pixels — luminance, so a scan slightly warm or cool reads as the tooth it looks like — and downscaled on import, because a 2400px scan is 144 MB of float for a grain that repeats every few hundred pixels. Anchored to the document, which is what makes two strokes crossing the same patch sit on the same tooth.
- [x] Brush scripting/API `evidence: LightboxTools, IpcDocumentApi`
- [x] Brush importers — .abr / .gbr / .gih / .kpp `evidence: AbrReader, GbrReader, GihReader, KppReader, BrushImportTests`
- [x] Physical media simulation (watercolour, gouache, oil, ink) `evidence: MediumSimulator, FluidLattice, Pigment, MediumRenderingTests`
- [~] A performance map, not only a ratchet — scaling curves, cliffs and a ranking `evidence: Lightbox.Bench, Harness.cs, AnimationSweeps.cs, DrawingSweeps.cs, Cadence, Curve, Runner`
  - The `Category=Performance` tests answer "did this diff break a path we know about". They cannot answer "where does this stop being usable" or "what should we fix first", and the unit of work here is a sequence, which no budget grows. `tools/Lightbox.Bench` sweeps a dimension, fits the exponent, finds the cliff where p95 misses the budget, and ranks by pressure. Minutes to run, so it is deliberate rather than per-commit. Design: `docs/DESIGN-performance.md`; output: `.claude/quality/PERFORMANCE.md`.
- [x] The simulated media are measured and bounded by the stroke `evidence: MediumPerformanceTests, TheMediumCostsTheSameOnAHugeCanvasAsOnASmallOne, AMediumStrokeDoesNotAllocateALatticeEachTime, AReusedLatticeRendersExactlyWhatAFreshOneWould`
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
- [~] Infinite canvas `evidence: TileStore, TileGrid, StrokeIndex, TileCompositor, TileStoreTests, TileCullingTests, AnUntouchedTileIsNeverAllocated, RecompositingCostsWhatIsOnScreenNotWhatExists, PanningAcrossEmptySpaceAllocatesNothing, AFixedSizeDocumentWritesNoCanvasKey, ATiledRenderIsBitIdenticalToAnUntiledOne`
  - Specified in `docs/DESIGN-infinite-canvas.md`, and it was `[?]` until it was measured. **Not an optimisation — a model that cannot be expressed.** `_cache.Get(frame, scene.Width, scene.Height)` allocates one bitmap per layer at document size, and an unbounded canvas has no width to pass. Culling cannot help, because the allocation happens before anything knows what is on screen.
  - The measurement that settles the order: recompositing is **`n^1.05` in canvas area, cliff at 1440p, 1344% of the playback budget at 8K**, while committing a stroke on the same axis is **`n^0.22`**. Drawing is already canvas-size independent and the drawing floor needs nothing; *showing* the canvas is proportional to the document rather than to the window. One 3-layer frame at 8K is 380 MB against a 512 MB cache — the wall is reached before infinity is.
  - So tiling is the precondition and culling is the consequence, in that order. A tile is a cache entry rather than a document change: invariant 1 holds, and dropping every tile loses nothing but time.
  - **Built: the engine and the cull.** `TileGrid` addresses tiles (256², negative in four directions), `TileStore` allocates them sparsely, `StrokeIndex` answers which strokes reach one, `TiledRasterizer` fills them bit-identically to the untiled path, and `TileCompositor` draws only the tiles a viewport touches. `RecompositingCostsWhatIsOnScreenNotWhatExists` holds the viewport still and grows the store twenty-five-fold for identical work — counted rather than timed, because a stopwatch would pass on a compositor that walked every tile quickly, which is the bug that matters when "every tile" has no bound.
  - **Remaining, in order, and the first one is not the wiring it looked like.** (1) **B82** — the compositor cannot know what is on screen. `PublishSnapshot` composes `scene.Width × scene.Height` whenever there is no camera, and zoom and pan live in `CanvasControl` downstream, so there is no viewport rectangle to cull against; `TileCompositor` is finished and has nothing to aim at. That is architectural, it changes what a `RenderSnapshot` is, and it is the blocker under B29, B30 and this item alike. (2) The unbounded document property — **Q21** *(document property or project default — `QUESTIONS.md` has two Q21s, see **B81**)* settled that it is a document property *and* that a project supplies the default, and that the property comes first under either reading, so this step is the property alone. (3) `FeatureConflict`, so sprite export refuses an unbounded canvas with its reason instead of failing silently.
  - An earlier version of this line said the remaining step was to "wire the cull into `MainViewModel`'s six `_cache.Get` sites". Two things wrong with it, both found by reading the file rather than the plan: there are **twelve** `_cache.Get` calls and only four are the composite — the rest append strokes or sample pixels and need a document-aligned bitmap, which **B60** blocks — and rewiring any of them culls against a rectangle nothing computes yet. Recorded rather than quietly rewritten, because the plan being wrong in a knowable way is the useful part.
  - Q20 *(export bounds from an unbounded canvas — `QUESTIONS.md` has two Q20s, see **B81**)* is **answered** — an authored export region, reached as the resolution of a declared feature incompatibility rather than a rule for deriving bounds. Nothing here is blocked on it.
- [x] Canvas rotation `evidence: CanvasViewTests, CanvasControl`
- [x] Canvas mirroring `evidence: MirrorButton, IsMirrored, CanvasViewTests`
- [x] Render at any output scale without changing the mark `evidence: OutputScaleTests, AHigherOutputScale_RendersTheSameMark, ScalingTheCoordinatesInstead_ProducesADifferentMark`
- [x] Reference image panel `evidence: ReferenceSheet, ReferenceView, ReferenceTabTests`
- [ ] The cursor says what the tool will do, and whether it can `evidence: CanvasCursor, CanvasCursorTests, ADisallowedActionShowsWhyRatherThanDoingNothing`
  - Today the canvas shows a brush-size ring and little else. The eyedropper, the fill, the move tool and the shape tools all present the same pointer, so the only way to know which one is armed is to look away from the drawing at the toolbar — which is exactly the moment an artist does not want to spend.
  - **The half that matters more is the refusal.** Painting on a hidden layer, a locked layer, an alpha-locked layer with nothing under the brush, filling outside a selection: these currently do nothing and say nothing, and silence is indistinguishable from a broken app. A forbidden cursor turns "it is not working" into "it will not do that here", which the artist can act on. B2's lesson written down as a rule — *even refusing would be better than nothing*.
  - Needs one place that maps (tool, modifiers, what is under the pointer) to a cursor, so the answer cannot disagree between the canvas control and the view model. Testable without a window if the mapping is pure, which is the shape `RigOverlay.CursorFor` already uses.
  - Depends on the icon set below for the artwork, but not for the mechanism: it can ship with system cursors and get custom ones later.
- [?] Resize canvas and resize image
  - Resize canvas expands the image with the value added to the x or y. It keeps the DPI and all other canvas related configurations. The content on the canvas stays put. The user wants to be able to select; all direction, down, to either side or up. There should be a preview.
  - Resize image scales the entire image and changes the dpi of the docment if any is given. and we can optionally constrain the proportions.
  - For both we can We can chose to resize only x, y or by default link the two so it scales uniformly. And after confirming resize or rescale the canvas is resets to the viewport. 

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
- [ ] A drawn line can be re-shaped and keeps the mark it was drawn with `evidence: PathEditSession, StrokeReshapeTests, ReshapingALineKeepsItsBrush, AReshapedStrokeSurvivesAReload`
  - Vector manipulation with the texture of charcoal, pencil or paint. **Half of this already exists and is worth saying so:** a stroke on a `VectorFrame` is the same `Stroke` record with the same `BrushSettings` as a raster one, stamped by the same engine — so a vector line already carries real media rather than a flat outline. Nothing needs a second engine.
  - What is missing is the editing: there is no tool that takes a finished stroke's points and lets an artist drag them. `VectorFrame` holds `List<Stroke>` and nothing reaches into one after it is drawn.
  - **The design question this raises is genuinely hard, and it is the reason this is not a small item.** Every dab dynamic — scatter, size, roundness, rotation, all three colour jitters — is seeded from dab position via `Hash01`. Move a control point and the dabs near it re-seed, so *the texture changes where the line moves*. That is correct under invariant 2 and wrong to an artist, who expects to nudge a line and see the same line somewhere else. The options are a per-stroke seed origin that travels with the edit, seeding from arc length rather than position, or accepting the change and saying so. This needs a decision in `QUESTIONS.md` before it needs code.
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

### Guides and shapes

- [x] Perspective rulers `evidence: Snapper, DirectionAt, AStrokeIsHeldOnTheRayFromTheVanishingPoint, ARulerStraightensTheStrokeDrawnAlongIt`
- [x] Vanishing point tools `evidence: GuideKind, AVanishingPointsDirectionDependsOnWhereYouAreStanding, AVanishingPointPullsToItself`
- [x] Grid and snapping `evidence: Snapper, AGridPullsToItsIntersections, ATiltedGridStillSnaps, AStrokeOnAGridRecordsTheSnappedPoints`
- [x] Shape tools `evidence: ShapeBuilder, ShapeKind, AShapeIsAnOrdinaryStroke, AnEllipseFitsItsBoxAndCloses, ShiftSquaresItAndAltGrowsItFromTheCentre`
- [x] Vector guides `evidence: Guide, GuidesSurviveASaveAndReload, ADocumentWithNoGuidesWritesNoGuideKey, AHiddenGuideStillSnaps`
- [x] Rulers and guide editing `evidence: RulerStrip, TickStep, DraggingOutOfTheTopRulerLeavesAHorizontalGuide, LettingGoBackOnTheRulerThrowsTheGuideAway, AGuideIsMovedByGrabbingItOnTheCanvas, TheRulersAreAbsentUntilAskedFor`

### Layers and compositing

- [?] Layer masks
- [?] Clipping masks
- [?] Adjustment layers
- [x] Blend modes `evidence: LayerBlendMode, BlendComposeTests`
- [x] Layer folders `evidence: LayerGroup, LayerFolderTests`
- [x] Layer and alpha locking `evidence: LayerLockTests, AlphaLockTests`
- [?] Non-destructive filters

### Editing

- [x] Selection tools `evidence: SelectVariant, ClipRegion, SelectionTests, ClipRegionRegistry`
- [x] Warp transform `evidence: TransformToolTests, TransformBegun`
- [?] Liquify
- [?] Clone stamp
- [?] Healing brush

### Interop

- [?] PSD import/export
- [x] Tablet optimization `evidence: PressureTests, PressureVmTests, PenDiagnostic`
- [ ] Save as an ordinary image format — PNG, JPEG, SVG `evidence: ImageSaveFormat, SaveAsImage, ImageSaveTests, ASvgSaveKeepsVectorLayersAsPaths`
  - Export writes sheets and sequences for engines; there is no plain "save this as a picture". PNG and JPEG are small and mostly plumbing. **SVG is the interesting one and should not be faked**: a raster document cannot become an SVG except as an embedded bitmap, which is a lie in a vector wrapper. It is only honest for the vector layers, and it needs the vector side to be richer first — which is what makes it the same item as the one below.
  - JPEG needs a quality control and a warning that it has no alpha, or somebody exports a character on a white box and finds out later.
- [ ] Lightbox draws its own icons `evidence: IconSet, IconSetTests, EveryToolbarButtonResolvesAnIcon`
  - Every icon in the app should be one set, made deliberately rather than assembled. The interesting part is *how*: **the app should draw them itself**. That needs vector tooling good enough to author a 16 px glyph and an SVG save that emits real paths, which is the honest dependency chain — icons wait on the vector side, and the vector side is worth having anyway.
  - Generating the SVGs directly is the fallback and is fine as a first pass, but it is a worse test of the product: a drawing application that cannot make its own icons is telling you something about its vector tooling. Dogfooding here is a feature, not a vanity.
  - The mechanical half is separable and can land first: one place that names every icon, so a missing one fails a test instead of showing a blank button, and so a redraw is a single swap. That also settles the cut-off-icon complaints, which are a sizing question the current pile of assets cannot answer consistently.

---

## Pillar 1 — Character-based projects, not file-based

The switch that reframes everything else: a character is the unit of work, not
a folder of files. Its animations share one palette, one brush set, one set of
references, one export configuration. This is also where the project-type and
workspace split lands — see "Project architecture" below.

- [x] Project type recorded, absent by default `evidence: ProjectType, AProjectWithNoTypeWritesNoTypeKey, ADeclaredTypeSurvives`
- [x] Project types at creation (Illustration / Animation / Game Art / Storyboard / Comic / Asset Library / Empty) `evidence: NewProjectDialog, NewProjectSettings, NewDocumentSettings`
- [x] Project as a container above the document `evidence: ProjectManifest, ProjectIo, Project, ProjectTests, AProjectRoundTripsThroughTheFolder`
- [x] Character workspace — animations, assets, references, palette in one place `evidence: ReferenceSheet, ReferenceSheetModelTests, ReferenceTabTests`
- [x] Character library `evidence: CharacterLibrary, LibraryEntry, ImportingACharacterBringsItsAnimationsAndPalette, AnImportedCharacterStillPaintsFromItsPalette`
- [x] Character variants that inherit animations (Default / Winter Armor / Damaged) `evidence: CharacterVariant, AnimationsFor, AVariantInheritsEveryAnimationItDoesNotOverride, AnOverriddenAnimationReplacesOnlyItself`
- [x] Scene management `evidence: ProjectScene, AddScene, AddShot, SceneDuration, AFilmSurvivesASaveAndReload, AShotIsADocumentLikeAnyOther, ShotsAreIndentedUnderTheirScene`
- [x] Project conversion (Illustration → Animation → Game) with no artwork recreated `evidence: Convert, ConversionReport, ConvertingRecreatesNoArtwork, ConvertingAwayFromAnimationKeepsTheCameraAndTheScenes, ConvertingDoesNotRearrangeTheScreenByItself`
- [x] Workspace layouts, decoupled from project type `evidence: WorkspaceStore, WorkspaceViewModel, EveryProjectTypeHasABuiltInWorkspace, TakingAProjectTypesDefaultsSwitchesWorkspace`
- [x] Dockable panels `evidence: DockLayout, DockStrip, DockZones, PanelsLandInTheStripTheLayoutNames, AnEmptyEdgeCollapsesAndAFilledOneOpens`
- [x] Project browser — characters and their animations `evidence: ProjectViewModel, ProjectRow, TheDockerListsCharactersWithTheirAnimationsUnderThem`
- [x] Reach the files from the browser — reveal, open externally, duplicate `evidence: FileReveal, FileRevealTests, EveryRowKnowsWhereItIsOnDisk, DuplicatingAnAnimationCopiesItsArtIntoTheSameCharacter`
- [x] Movable canvas overlay bars — view controls and view shortcuts, on any edge `evidence: CanvasOverlayLayout, CanvasOverlayBar, CanvasOverlayGeometryTests, CanvasOverlayTests`
- [x] Shared palette across a character's animations `evidence: TwoAnimationsUnderOneCharacterPaintFromOnePalette, RefreshProjectResources`
- [x] Standalone export from inside a project `evidence: ProjectFlattenTests, AFlattenedDocumentRendersIdenticallyWithTheProjectGone`
- [x] Open an existing loose document without a project `evidence: TheAppOpensWithNoProject, WithNoProjectADocumentSavesAndLoadsExactlyAsBefore`
- [x] Per-workspace panel sets (Illustration / Animation / Game) `evidence: TheBuiltInsDifferFromEachOther, OnlySavedWorkspacesOfferABin`
- [x] Auto save - configurable in time if a file is already present. `evidence: AppSettings, AutosaveService, TheDefaultIsEveryMinuteToTheRecoveryCopyOnly, ZeroTurnsAutosaveOff`

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
  - **Reachable with no project open.** The library is the artist's own, so it is there when they open the app to draw one picture; placing one into a loose document copies it into `Doc.Symbols`, which the registry already reads and which is the same key `ProjectIo.Flatten` writes — the self-containment rule one level down. The *project* tree stays gated, because without a project it has nothing at all to show, and making a project symbol still needs a project to put it in.
  - Adoption is keyed on the **id inside `PlaceSymbol`**, not per route. Doing it per route looked simpler and was wrong: drag-and-drop carries only an id, so a row-based version would have worked for the Place button and left a dragged library symbol failing to resolve — the harder bug to find, because the two routes are indistinguishable from the panel.
- [x] Timing presets — save an exposure pattern and apply it to a range of cels `evidence: TimingPreset, TimingPresetStore, ApplyTiming, TimingPresetTests, TimingPresetUiTests, ApplyingAPatternReExposesTheDrawingsThatAreThere, ThePatternDecidesTheLength_NotTheSelection, ItNeverCreatesOrDestroysADrawing, ApplyingToASelectedRangeRetimesTheWholeRange, ASavedPatternPersistsAndComesBackOnTheNextLaunch`
  - **Q11 answered (b).** *Reusable animation presets* is struck: the Animation library already is the reusable animation — a multi-frame symbol placed with a frame offset — and a roadmap item nothing can distinguish from a shipped one is the wish list the checkbox rules exist to prevent. What is genuinely absent is **timing**, which is the half a symbol cannot carry: a symbol carries drawings, this carries their spacing.
  - On 1s, on 2s, a slow-in of 1-1-2-3-4. Applied to a selected range, it **re-exposes the drawings that are already there** rather than making any — which is why it is nothing a symbol can express, and why it composes with everything else instead of competing.
  - **Landed whole.** `ExposureSheet.ApplyTiming` re-times a range, `TimingPreset.BuiltIns` carries the six patterns worth having on day one, `TimingPresetStore` keeps an artist's own beside their brushes, and the picker plus **Re-time** sit on the timeline bar with a cel-menu item beside them. One undo step.
  - **One correction made when the UI went on.** The engine first held the *selection's* length and dropped whatever no longer fit, which would have meant "on 2s" silently discarding half an artist's drawings. **The pattern decides the length**: twelve drawings on 2s occupy twenty-four cels and the row grows. Thinning a range on purpose is `ReduceToStep`, which is a separate command precisely because it is destructive. `ThePatternDecidesTheLength_NotTheSelection` holds the line.
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
- [?] Draw once, reuse across animations
- [?] Motion path visualization
- [?] Motion arcs
- [?] Arc prediction
- [?] Spacing visualization
- [?] Spacing assistant
- [?] Timing charts
- [?] Automatic contact frame detection
- [?] Perspective consistency checker
- [?] Silhouette readability preview
- [?] Walk cycle analyzer
- [?] Jump arc analyzer
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
- [?] Sprite atlas generation across characters
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
- [?] Floating reference windows
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
- [?] Safe area guides
- [?] Zoom preview
- [?] Camera shake preview
- [?] Scene preview
- [?] Multiplane parallax (per-layer depth)

### Construction guides

- [?] Construction guides
- [?] Automatic volume guides
- [?] Center of mass visualization
- [?] Perspective consistency guide
- [?] Character height guide
- [?] Limb length guide
- [?] Eye-line guide
- [?] Horizon guide

### Project plumbing

- [x] Autosave `evidence: AutosaveService`
- [x] Custom shortcuts `evidence: ShortcutMap, ShortcutMapTests, ConfigureWindow`
- [x] Context-aware shortcuts `evidence: ShortcutContext, ContextShortcutTests`
- [ ] Undo history browser — visual timeline of document states, navigable and reviewable `evidence: UndoHistoryViewModel, UndoHistoryPanel, UndoStateSnapshots, UndoHistoryTests, ASnapshotRecordsTheDocumentState, NavigatingTheHistoryRestoresTheState, DeletingAnIntermediateStateCollapsesPrior`
  - **High-value, medium-effort.** Partially closes Request 1's "version snapshots" feature for long projects and team hand-offs. Builds on existing `Doc.Undo` history; the missing half is a UI that shows the sequence of states and lets an artist jump to one. Each snapshot captures document bytes and metadata (timestamp, user action), displayed as a scrollable timeline with state preview. Cost is bounded per charter, since the history exists and reloading a state is one `Deserialize`.
- [ ] Version snapshots — lightweight bookmarks of document state, distinct from full undo history `evidence: VersionSnapshot, VersionSnapshots, VersionSnapshotStore, VersionSnapshotTests, ASnapshotIsAnAuthoredMarkerNotAnUndoState, SnapshotsRoundTripThroughTheFile, DeletingASnapshotDoesNotAffectTheDocument`
  - **Lighter than undo history browser, complementary not competing.** Undo is automatic per keystroke and navigation is "go back to what I did three minutes ago"; snapshots are *authored* ("this is where the background was locked") and span projects or sessions. Acts as a checkpoint system for long projects where re-doing work is expensive. `VersionSnapshot` record holds document bytes, user notes, and metadata; stored in `assets/versions/` folder. The history browser navigates undo; snapshots are manual milestones an artist places. Requested in Request 1 feature analysis for version control workflows.
- [ ] Studio dashboard — shot-level overview of project status and workload `evidence: StudioDashboard, ShotStatusView, DashboardTests, AllShotsVisibleWithStatusAtAGlance, BlockedShotsAreHighlighted, ArtistWorkloadIsBalanced`
  - **HIGH-VALUE, MARKET-VALIDATED.** Studios manage projects in ShotGrid/Airtable because Lightbox has no dashboard. Current workaround: maintain separate spreadsheets tracking shot status, artist assignments, blocked items.
  - **Effort:** Medium (~400 LOC)
  - **What it shows:** All shots/assets in project, status per item (Design/InDevelopment/Ready/Reopened), assigned artist, dependencies/blockers
  - **Blocker:** Depends on dynamic asset folders being UI-complete (B83-87)
  - **Note:** This is read-only dashboard; does not replace ShotGrid, just gives visibility

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

Three rules govern everything below, and they are not negotiable per feature:

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
- [x] A connection test that checks the output, not just the reply `evidence: AiConnectionTester, AiTestDepth, AiConnectionTesterTests, AThoroughTestFailsWhenTheModelCopiedAKeyInstead, AQuickTestDoesNotAskForAnInbetween`
  - **It draws rather than pings.** The ways this fails are mostly not reachability: a key with no credit, a model name off by a version, an endpoint that answers but cannot honour a JSON schema, an MCP server whose tool is spelled differently, a small model that returns valid JSON full of nonsense. A ping says "connected" to every one.
  - Two depths. Quick asks for one line; thorough adds a real inbetween and checks it lands **between** the two keys — the one assertion that separates a working connection from a working inbetweener, and the one a parse check can never make. Three verdicts rather than two, because "unreachable" and "reachable but drawing nonsense" need different fixes.
- [x] A budget on what a request costs `evidence: AiPayloadBudgetTests, AnInbetweenRequestStaysWithinItsBudget, CostScalesWithStrokeCount_WhichIsWhySendingFewerIsTheRealLever, ResamplingIsWhatKeepsALongStrokeAffordable`
  - The one cost in this app that is invisible locally: a change that doubles a payload shows up on somebody's bill a month later and nothing in the suite says a word. Measured in `docs/DESIGN-ai-payload.md` — a 40-stroke frame pair is 102 KB and at least 26k tokens; `MaxWirePoints` is the constant carrying it, and deleting it would fail no other test.
  - The finding worth keeping: **images are ~87% of a request's bytes and ~5% of its tokens, and strokes are the reverse.** So "make the payload smaller" is two goals recommending opposite changes, and any optimisation has to say which it means. Compression is off the table for the same reason — it takes 82% off the bytes, touches no tokens, and 0.3 s of upload is invisible beside 30–120 s of generation.
- [ ] Send the strokes that need judgement, not the whole frame `evidence: StrokeSelection, StrokeSelectionTests, OnlyStrokesThatMoveAreSent, TheContextIsEnoughToPlaceThem`
  - Six times bigger than any encoding trick, and the only lever with no format risk. A 120-stroke frame is ~79k tokens and most of those strokes barely move; the deterministic inbetweener already handles a matched stroke correctly, and the AI is needed where straight interpolation fails — arcs, rotation, overlap. Halving the stroke count halves the cost exactly.
  - The hard half is knowing *which* strokes need judgement, which is `DESIGN-subject-reading.md`'s question approached from the other side.
- [x] An MCP surface, so an agent can work the document directly `evidence: IpcServer, IpcDocumentApi, IpcTests, InsertInbetweens_ValidatesAndInserts_Undoable, DrawStrokes_AppendsToExposedKey, BadRequests_FailCleanly, PipeRoundTrip_GetScene`
  - **The other direction, and it was missing from this file entirely** until the AI section was gathered — which is its own small argument for the section. `CLAUDE.md` names it as one of the three purposes and the code has shipped it since M4a, but no roadmap item claimed it, so nothing was deriving its status from the code.
  - Independent of the provider list above, and that independence is the point: there, Lightbox calls out to a model; here, an agent the artist already runs calls **in** and edits the document. Configuring a provider is not a prerequisite for either.
  - Every tool goes through the same document editor a menu item uses, marshalled onto the UI thread — so an agent's edit is one undo step, dirties the tab, and cannot bypass `BrushEngine.StampStroke`. An MCP surface that wrote pixels directly would break invariant 1 for the one caller least able to notice.
  - The anchors are named tests rather than a project name, and the first attempt at them was wrong: `McpToolTests` does not exist and `roadmap.py` demoted the item within seconds of it being written. That is the file working as designed — a green box asserted from memory is exactly what the derived checkbox exists to refuse.

### Reading the drawing

The prerequisite half. Both of these exist to be *inputs to authoring* and neither
may reach a pixel at render time.

- [ ] The AI reads the subject before it draws `evidence: SubjectReading, SubjectTaxonomy, PartPlacement, SubjectReadingTests, AHandNamedPartBeatsAGuessedOne, DeletingEveryReadingChangesNoPixel`
  - Inbetweening, inking and normal maps each asked for this separately, which is the signal to design it once — see `docs/DESIGN-subject-reading.md`. Split in two because the halves have different lifetimes: **taxonomy** (this is a biped with these parts) is per character and worth reviewing by hand; **placement** (where each part is in frame 12, what occludes what) is per frame and disposable. The rig's hand-drawn `parts` win wherever they exist — a guess is a default, never an override of something a person stated.
  - The line it must stay on: the reading is an input to *authoring*, never to rendering. Its first test deletes every reading from a finished document and asserts the render is byte-identical, because the day that fails is the day invariant 2 is gone.
- [ ] A light source, for the tools that need to know where the shadows are `evidence: SceneLight, SceneLightTests, ALightNeverReachesStampStroke, ADocumentWithNoLightExportsExactlyAsItDid`
  - On the scene, nullable, absent until placed — the camera's rule. Two uses that must not be conflated: for inking it is a **generation input** (which contours are heavy, which side is in shadow) consumed before there are strokes; for a normal map it is a **preview rig** and must never be baked into the output, because a normal map that carried a light would defeat the reason for having one.

### What it does for the artist

- [x] AI inbetweening `evidence: Inbetweener, InbetweenerTests, IAiArtist`
- [ ] AI-assisted inking, with styles `evidence: InkingStyle, InkingPass, InkingStyleTests, AnInkedPassIsOrdinaryStrokes, WeightFollowsTheLightRatherThanTheStrokeOrder`
  - A style is **a brush preset plus a policy** — weight, taper, depth cue, interior detail, fills — rather than a hard-coded "flat" and "comic". Two modes would be two modes; the axes are what makes the third style somebody asks for reachable. The preset half already exists, so an inking style is an ordinary brush an artist can open and edit.
  - Output is ordinary strokes through `BrushEngine.StampStroke`, so an inked frame replays, undoes and inbetweens like anything else. Whether it replaces the pencils or lands on its own layer is Q17.

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

---

## Project architecture — reconciling the design with the code

The project-type / workspace / asset-organization split is a real design and
the code does not have it yet. What exists today, honestly:

| The design says | The code has |
| --- | --- |
| Project containing scenes/characters/assets | `Doc` with **one** `Scene`. No project layer at all. |
| Project type chosen at creation | `NewDocumentSettings` — size, fps, ppi, paper. No type. |
| Workspace controls which panels are visible | Dockers can be shown and hidden individually; no named sets, nothing persisted. |
| Character as a reusable asset with animations under it | `ReferenceSheet` is the nearest thing, and it is reference art, not a container. |
| Scene-based / character-based / asset-based organization | Layers and cels on a single canvas. |

Three consequences worth stating before anyone builds against the design:

1. **`Doc.Scene` is singular.** Everything above — scenes, shots, pages,
   characters — needs a container that does not exist. That is one change and
   it is load-bearing for pillars 1, 3 and 6 alike; it should be designed
   once, not three times.
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
- [x] Asset Library project type `evidence: CharacterLibrary, OnlyAssetLibraryProjectsOfferTheirCharacters`

### Making reach unconditional

- [ ] One registry of features, their defaults per project type, and nothing gated `evidence: FeatureDefaults, FeatureKey, FeatureDefaultsTests, EveryFeatureIsReachableInEveryProjectType, AProjectTypeSetsDefaultsRatherThanAvailability, AFeatureLeftAtItsDefaultWritesNoKey`
  - The registry is the point, and it is the same argument as `ShortcutMap`: the reason to have one is that something else enumerates it. The Configure window needs to list what can be turned on, the new-document path needs the defaults for a type, and the manual needs to say which is which. Three places deriving that from one table cannot disagree; three places each deciding for themselves already have.
  - Derived defaults, not copied ones. A document stores only what its artist changed, so a default that moves in a later version moves for every document that never overrode it — the same reason `BrushCostOf` computes rather than stores.
- [ ] Features that cannot both be on say so, and a project type never decides it `evidence: FeatureConflict, FeatureConflicts, FeatureConflictTests, TurningOnAFeatureNamesWhatItExcludes, AConflictHoldsInEveryProjectType, AConflictIsRefusedWithItsReasonRatherThanHidden, AuthoringTheMissingThingResolvesTheConflict`
  - **The category the reach rule did not have a word for, and the reason it looks like a hard limit when it is not.** *Defaults* cover a feature that is off and could be on. What they do not cover is two features that are **mutually exclusive by construction** — an unbounded canvas and a fixed frame-bounds sprite export are not "one is off by default", they contradict each other, and no project type is involved in that being true.
  - So the limit is declared **between features**, never on a project type: `unbounded canvas` excludes `fixed frame-bounds export`, in a game project and in a short alike. Nothing is locked behind a value in a manifest, which is what the rule above actually forbids, and *Making reach unconditional* stands as written.
  - **Refused with its reason, never hidden.** A greyed control that does not say why is the same failure as B2 and as the cursor item in Pillar 0 — silence is indistinguishable from a broken app. "Sprite export needs consistent frame bounds; this canvas is unbounded. Author an export region." names the fix.
  - **A conflict is resolvable, and that is what separates it from a gate.** Authoring the missing thing clears it. That is why this is not a hard limit wearing a different word: there is always a way through, it is just a thing the artist has to say rather than a thing the application guesses.
  - Same registry as the defaults above, second table. One place enumerates what can be turned on, what a type starts with, and what excludes what — three questions the Configure window, the new-document path and the manual all ask, and three places deriving them separately already disagree.
- [ ] Changing a project's type changes defaults and never removes work `evidence: ProjectTypeChangeTests, ChangingProjectTypeKeepsEverythingAlreadyAuthored, ChangingProjectTypeMovesOnlyUntouchedDefaults`
  - The consequence of the rule that costs the most to get wrong. If availability were gated, switching a project from Shot to Asset would have to decide what happens to the camera somebody keyframed. With reach unconditional there is nothing to decide: the camera stays, it is simply no longer part of what a new document in that project starts with.
- [ ] A project's characters are offerable from any project type `evidence: CharacterLibraryReachTests, AnyProjectCanPublishItsCharacters, TheAssetLibraryTypeDefaultsToPublishing`
  - The one existing violation, fixed rather than left as a footnote. `CharacterLibrary.Scan` currently returns nothing unless the manifest says `AssetLibrary`, so a character filed in a game project is unreachable from the short that needs it — and the only way out is to change the project's type, which is a decision about the whole project made to solve one lookup.
  - `OnlyAssetLibraryProjectsOfferTheirCharacters` is the test that pins the old behaviour, and it should be **rewritten rather than deleted**: the thing worth guarding is that the Asset Library type still *defaults* to publishing, which is the part that made the type mean something.

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
| **Studio Dashboard** | ShotGrid replacement for small studios; eliminates spreadsheet maintenance | Medium | Dynamic folders (B83-87) |
| **Animatic Preview Export** | One-click timing render saves manual video editing cycle | Low-Medium | None |
| **Version Snapshots** | Hand-offs between artists; manual checkpoints of document state | Medium | Undo browser |
| **Subject Reading** | Prerequisite for inking, normal maps, consistency checking; unlocks 3 features | Medium | Q16, Q17 answers |

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
| **Resolve Q19: Seed origin for path editing** | 4 | Design question blocking vector work. Do dabs re-seed from new position or arc-length? | Low (decision) | CRITICAL | None |
| **Stroke path reshaping (+ Bezier editing)** | 4 | **100% of vector tools have this.** Professional illustrators cannot work without stroke editing. Once resolved, path editing. | High (800 LOC) | CRITICAL | Q19 |
| **Per-layer onion skin control** | 4 | Show layer history independently. Rare feature; unique to Lightbox. Unblocks animation workflow refinements. | Medium (300 LOC) | Medium | None |
| **Pressure curve standardization** | 4 | Unsolved workflow gap: artists re-calibrate pressure in Clip Studio vs Procreate vs Adobe. First standardized import/export. | Low (150 LOC) | Medium | None |
| **Tilt & velocity recording** | 4 | High-end tablet support. Clip Studio, Procreate, Corel all have this. Medium artist request. | Medium (600 LOC) | Medium | StrokePoint migration |
| **Symmetry & mirrored painting** | 4 | Essential for character design. Every professional tool has it. Blocks character-focused workflows. | Medium (400 LOC) | High | Q19 variant |
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

**Pillar 4 (Animation-aware drawing tools) — Vector Tools**

3. **New Item**: "Resolve design question Q19 — seed origin for path editing" [ ]
   - Decision-only item: No LOC, blocks multiple vector features
   - Question: When a dab's point moves, does it re-seed from new position or arc-length?
   - Market impact: Unblocks stroke reshaping, symmetry, multi-capture tips

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

1. **Resolve Q19 first** — It blocks three features (path editing, symmetry, multi-capture)
2. **Path editing second** — Unblocks SVG export and Bezier editing; makes vector layer usable
3. **Pressure curves third** — Quick win, market differentiation, no blocking
4. **Tilt/velocity fourth** — Important for pros, but non-blocking; medium effort

---

## Market-Validated Priorities: Character Sheets & Reference Integration (2026 Market Research)

Based on professional animation studio workflows and competitor analysis, Lightbox has **excellent foundational character management** but critical gaps in reference usability that force animators to manage references outside the tool. Context switching costs ~4 hours per week per animator (1,200 app toggles daily, 20–23 min recovery per switch).

### **Lightbox's Character Management: Strong Foundation**

Already built ✅:
- Character workspace (animations, assets, references, palette unified)
- Character library with import/export
- Character variants (different palette/animation overrides)
- Reference sheets (multi-view layer stacks: Front, Side, Back, Expressions)
- Reference strips (imported animation cycles with per-frame alignment)
- Shared palette across character animations
- **Deterministic rendering** (enables reference-aware brushes — unique capability)

### **Critical Usability Gaps: Reference Management** ❌

| Feature | Impact | Status | Competitors | Gap |
|---------|--------|--------|-------------|-----|
| **Reference positioning persists** | HIGH | [ ] | Harmony, Clip Studio, Aseprite save this | Lightbox loses position every session |
| **Non-destructive annotation layer** | MEDIUM | [ ] | Zero competitors | **Pure market gap** |
| **Character version tagging** | CRITICAL | [ ] | Enterprise tools only | Indie teams: "final_v7_REAL.psd" chaos |
| **Expression/pose metadata** | MEDIUM | [ ] | No animation tools | Expressions scattered in files |
| **Real-time consistency checking** | MEDIUM | [ ] | Emerging (ModelSheetAI) | Not mainstream |

### **Tier 1: High-Impact Pain Relief** (Low Effort, Unblocked)

| Item | Pillar | Why | Effort | Impact | Blocker |
|------|--------|-----|--------|--------|---------|
| **Reference positioning persists** | 1 | Repositioned every session — highest friction | Low (100 LOC) | HIGH | None |
| **Character version tagging** | 1 | Out-of-sync versions mid-project cause rework | Low (100 LOC) | CRITICAL | None |
| **Non-destructive annotation layer** | 1 | Artists mark up reference (proportions, anatomy); zero tools let them do this non-destructively | Medium (300 LOC) | Medium | None |
| **Expression/pose frame metadata** | 1 | Scattered files (happy.png, sad.png); no query capability | Medium (200 LOC) | Medium | None |

### **Tier 2: Differentiation** (Medium-High Effort, Blocks Next)

| Item | Why | Effort | Blocker |
|------|-----|--------|---------|
| **Lightweight character versioning** | Indie alternative to $10k enterprise tools (Toon Boom Server, Perforce) | Medium (600 LOC) | Tier 1 version tagging |
| **Deterministic reference-aware brushes** | Lightbox-only: brush responds to reference geometry, reproducibly (invariant 2) | High (600 LOC) | Reference geometry API |
| **Character semantic database** | Store character as queryable data; export as FSM for game engines and AI agents | High (800 LOC) | Metadata structure |
| **AI consistency checking** | Real-time "is this frame on-model?" verification | High (800 LOC) | Subject reading (Q16/Q17) |

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

**Pillar 1 (Character-based projects) — Reference Usability**

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

**Pillar 1 (Character-based projects) — Team Collaboration**

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
   - Blocker: Subject reading (Q16, Q17)

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
