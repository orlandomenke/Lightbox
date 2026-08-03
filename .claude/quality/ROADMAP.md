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
- [?] Infinite canvas
- [x] Canvas rotation `evidence: CanvasViewTests, CanvasControl`
- [x] Canvas mirroring `evidence: MirrorButton, IsMirrored, CanvasViewTests`
- [x] Render at any output scale without changing the mark `evidence: OutputScaleTests, AHigherOutputScale_RendersTheSameMark, ScalingTheCoordinatesInstead_ProducesADifferentMark`
- [x] Reference image panel `evidence: ReferenceSheet, ReferenceView, ReferenceTabTests`

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
  - **The canvas overlay is built** and places anchors and shapes with one shared gesture set — recorded on the hitbox/hurtbox editor item below, since it is one piece of work serving both.
- [x] Collision shapes `evidence: CollisionShape, ShapeRole, ShapeBox, CollisionShapes, CollisionShapeTests, AShapeRoundTripsThroughTheFile, ADocumentWithNoShapesCarriesNoShapeKeys, AHitboxIsActiveOnlyWhereItIsPlaced`
  - **Rectangles only, and that is the scoping decision rather than a gap.** A rect is what every 2D engine takes directly — `BoxCollider2D`, `RectangleShape2D`, GameMaker's bbox — so it exports without a conversion that could change what the artist meant. A polygon needs a real editor (add, move, delete vertices, over the canvas, per frame) *and* a per-engine decomposition, and half a polygon editor is worse than a whole rect one. Polygons arrive later as a second kind beside this, and every rect authored today keeps working.
  - **Built as a copy of the anchor design on purpose**, down to the six operations: declaration and role on `Scene.Shapes`, rectangle per drawing on `Frame.Shapes`. Same reasoning — a name belongs to the rig, a rectangle belongs to a drawing and must travel with it through a hold, a re-time, a cel drag or a timing preset. A test re-times a range to prove it. The payoff is that the one canvas overlay still to be built places both.
  - `ShapeBox.CentreX`/`CentreY` are `[JsonIgnore]`, because a read-only property serializes like any other one and two derived keys on every shape of every frame is exactly what `BlendOrNormal` did.
- [x] Hitbox and hurtbox editor — one canvas overlay, shared with the anchors `evidence: RigOverlay, RigMark, RigMarkKind, RigCorner, RigHit, RigMarks, DragRig, AddAnchorAt, AddShapeAt, PushRigAcross, RigOverlayTests, RigEditingTests, ADragLandsOnTheDrawingRatherThanOnTheFrameIndex, ASelectedShapesCornerBeatsItsOwnBody, TheSmallerShapeWinsWhenOneIsInsideAnother, DraggingWhileParkedOnAHoldEditsTheDrawingBeingHeld`
  - **Built as one overlay for anchors and shapes together**, which is the whole reason the two records were made the same shape. An anchor is a zero-sized rectangle, so there is one hit-test, one drag and one set of handles rather than two of each — and it turned the two "no canvas overlay yet" caveats on the anchor items into one piece of work.
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
- [ ] GameMaker exporter — `.yy`, and version-coupled enough to need a decision first `evidence: GameMakerExporter, GameMakerExportTests, AYyFileMatchesTheVersionItWasWrittenFor`
  - GameMaker's `.yy` files are JSON, so they are writable — but the schema moves between releases and carries GUIDs the IDE expects to own. Writing one against the wrong version produces a project that will not open, which is a worse failure than not exporting.
  - So this is blocked on a question rather than on effort: which GameMaker versions are supported, and is a sheet-plus-JSON import (which GameMaker can do by hand) enough? Filed rather than started.
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
- [ ] Every engine exporter is checked against that engine's current API `evidence: EngineApiNotes, EngineApiTests`
  - **Filed because it already caught a shipped defect.** The Unity importer used `TextureImporter.spritesheet`, which **stopped working in Unity 2021.2 and was removed in 2022.2**: assigning it throws nothing and slices nothing, so the import logs success and produces one sprite. Every Unity anyone is running would have silently ignored the export. Fixed by branching on `UNITY_2021_2_OR_NEWER` to `ISpriteEditorDataProvider` — `SpriteDataProviderFactories` → `InitSpriteEditorDataProvider` → `SetSpriteRects` → `Apply`, with a per-rect `spriteID = GUID.Generate()` that the old `SpriteMetaData` had no equivalent for and a mechanical port would drop.
  - **The lesson is about the shape of the mistake, not about Unity.** A write-only integration cannot fail visibly here: we produce a file, hand it over, and never see the other side. So "it compiles and the tests pass" says nothing about whether anything reads it. The standing rule is that every engine target names the API version it was written against and what it would do on a newer one.
  - Worse, a test of mine was *enforcing* the bug: it asserted the importer did **not** mention `SpriteDataProviderFactories`, on the assumption that was the physics-shape API. Asserting the absence of an API is only safe when you know what that API is for.
  - What this item wants: a short note per engine recording the API surface used, the minimum version, and the deprecation risk; plus one real import per engine, by hand, recorded. Godot's `.tres`, Unreal's Paper2D and GameMaker's importer are all unwritten and must not repeat this.
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

## Pillar 6 — Production-focused workflow

Assets, animations and scenes as first-class citizens rather than layers on a
canvas. Also the home for everything that keeps a long project alive:
timeline, review, versioning, collaboration.

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
- [?] Undo history browser
- [?] Version snapshots
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

- [?] Comments on frames
- [?] Comments on layers
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

- [?] Inbetween guide generation
- [?] Secondary motion assistant
- [?] Smear frame assistant
- [?] Smart line cleanup suggestions
- [?] Automatic line consistency checker
- [?] Volume consistency checker
- [?] Colour consistency checker
- [?] Animation quality checker
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

- [x] `Project` container above `Doc` (scenes, characters, assets) `evidence: ProjectManifest, Character, DocumentRef, ProjectTests`
- [x] Project type recorded on the document, absent by default `evidence: ProjectType, AProjectWithNoTypeWritesNoTypeKey`
- [?] Named workspaces, persisted
- [?] Storyboard organization (scenes → shots)
- [?] Comic organization (pages → panels)
- [?] Panel tools and speech balloons
- [?] Print workflow (CMYK, bleed, DPI targets)
- [x] Asset Library project type `evidence: CharacterLibrary, OnlyAssetLibraryProjectsOfferTheirCharacters`

---

## How this file stays true

- `scripts/roadmap.py check` runs in the improvement loop's verify step and in
  the Stop hook. Drift between the marks and the code fails it.
- Landing a feature means adding its evidence anchors in the same commit.
  A commit that ships a feature and leaves its box unticked is incomplete in
  the same way a commit without a test is.
- `[?]` items are a backlog of their own: each one is a feature nobody has
  said how they would recognise.
