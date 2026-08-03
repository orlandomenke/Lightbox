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

- [x] Deterministic marks across frames (no boiling) `evidence: OutputScaleTests, BrushDynamicsTests, ScalingTheCoordinatesInstead_ProducesADifferentMark`
- [x] AI inbetweening `evidence: Inbetweener, InbetweenerTests, IAiArtist`
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
- [ ] The AI reads the subject before it draws `evidence: SubjectReading, SubjectTaxonomy, PartPlacement, SubjectReadingTests, AHandNamedPartBeatsAGuessedOne, DeletingEveryReadingChangesNoPixel`
  - Inbetweening, inking and normal maps each asked for this separately, which is the signal to design it once — see `docs/DESIGN-subject-reading.md`. Split in two because the halves have different lifetimes: **taxonomy** (this is a biped with these parts) is per character and worth reviewing by hand; **placement** (where each part is in frame 12, what occludes what) is per frame and disposable. The rig's hand-drawn `parts` win wherever they exist — a guess is a default, never an override of something a person stated.
  - The line it must stay on: the reading is an input to *authoring*, never to rendering. Its first test deletes every reading from a finished document and asserts the render is byte-identical, because the day that fails is the day invariant 2 is gone.
- [ ] A light source, for the tools that need to know where the shadows are `evidence: SceneLight, SceneLightTests, ALightNeverReachesStampStroke, ADocumentWithNoLightExportsExactlyAsItDid`
  - On the scene, nullable, absent until placed — the camera's rule. Two uses that must not be conflated: for inking it is a **generation input** (which contours are heavy, which side is in shadow) consumed before there are strokes; for a normal map it is a **preview rig** and must never be baked into the output, because a normal map that carried a light would defeat the reason for having one.
- [ ] AI-assisted inking, with styles `evidence: InkingStyle, InkingPass, InkingStyleTests, AnInkedPassIsOrdinaryStrokes, WeightFollowsTheLightRatherThanTheStrokeOrder`
  - A style is **a brush preset plus a policy** — weight, taper, depth cue, interior detail, fills — rather than a hard-coded "flat" and "comic". Two modes would be two modes; the axes are what makes the third style somebody asks for reachable. The preset half already exists, so an inking style is an ordinary brush an artist can open and edit.
  - Output is ordinary strokes through `BrushEngine.StampStroke`, so an inked frame replays, undoes and inbetweens like anything else. Whether it replaces the pencils or lands on its own layer is Q17.
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
- [?] Smear frame assistant
- [?] Inbetween guide generation
- [?] Secondary motion assistant
- [?] Automatic contact frame detection
- [?] Smart line cleanup suggestions
- [?] Automatic line consistency checker
- [?] Perspective consistency checker
- [?] Volume consistency checker
- [?] Colour consistency checker
- [?] Animation quality checker
- [?] Motion readability analysis
- [?] Silhouette readability preview
- [?] Walk cycle analyzer
- [?] Jump arc analyzer
- [?] Timing diagnostics

## Pillar 5 — One-click export to game engines

Atlases, metadata, pivots, events and hitboxes, in one action. Half of this
exists; the half that does not is what makes it *one* click.

- [x] Sprite sheet generation `evidence: SpriteSheetExporter, SpriteSheetExportTests`
- [x] Consistent trimmed bounds across a sequence `evidence: SpriteTrim, SpriteSheetOptions, TrimmingDefaultsToTheUnion_SoEveryCellIsTheSameSizeAndNothingJitters`
- [ ] Normal maps for the sprites `evidence: NormalMapGenerator, NormalMapPanel, NormalMapTests, ABevelFromTheSilhouetteNeedsNoDependency, ThePreviewLightIsNotBakedIntoTheMap`
  - Three tiers, and the cheapest one first because it makes the panel, the preview light and the export path real: a Sobel over the silhouette's distance field needs no dependency and no model. Then Laigter for artists who have it, then AI for the thing neither can do — knowing a cheek is round and a sleeve fold is a crease, which is the whole argument for spending a model on it and why the subject reading is its prerequisite.
  - **Laigter is GPL-3.0.** Linking it in would put Lightbox under GPL-3.0, which is a project-level licensing decision and must not be made by accident inside a normal-map task. Running its CLI as a separate optional tool keeps the licences apart and is how it should behave anyway — absent unless the artist has it, degrading to the built-in generator rather than breaking. See `docs/DESIGN-subject-reading.md`.
- [?] Automatic packing (rect/skyline, tighter than a grid)
- [?] Atlas optimization
- [?] Sprite atlas generation across characters
- [x] Generic JSON exporter `evidence: SheetDocument, SheetMeta, SheetFrame`
- [x] Export frame durations `evidence: SheetFrame, TheSidecarIsAsepriteShaped`
- [x] Export metadata `evidence: SheetMeta, SpriteSheetResult`
- [x] Pivot editor `evidence: Pivot, ThePivotIsRecordedPerCell_SoTrimmingCannotShiftTheCharacter`
- [?] Multi-frame pivot editing
- [?] Named pivot points
- [?] Socket system
- [?] Hitbox editor
- [?] Hurtbox editor
- [?] Collision shapes
- [?] Physics shapes
- [?] Export collision data
- [?] Frame events
- [?] Animation events
- [?] Export animation tags
- [?] Export animation clips
- [?] Unity exporter
- [?] Godot exporter
- [?] Unreal Paper2D exporter
- [?] GameMaker exporter
- [?] MonoGame exporter
- [?] Raylib exporter
- [?] Modular exporter plugin system
- [?] One-click game-ready export

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
- [?] Frame tagging
- [?] Animation tagging
- [?] Timeline bookmarks
- [?] Animation notes
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
