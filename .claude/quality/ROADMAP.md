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
- [x] Brush presets and tagging `evidence: BrushPreset, PresetStore, BuiltInPresets, BrushCategoryList`
- [x] Brush stabilization (lazy mouse, weighted, predictive) `evidence: SmoothingMode, StrokeFilters, SmoothingTests`
- [x] Texture brushes `evidence: PaperField, PaperKind, TexturedBrushTests`
- [x] Smudge, blend and mixer brushes `evidence: SmudgeMode, SmudgeFirstDabTests, MediumSettingsTests`
- [x] Eraser variants `evidence: ToolKind, BrushKind, EraserResurrectionTests`
- [?] Pixel-perfect mode
- [x] Pressure curve editor `evidence: BrushPagePressure, PressureVmTests, PressureTests`
- [x] Brush rotation and tilt support `evidence: BrushSettings, BrushDynamicsTests, AngleFollowsDirection`
- [?] Brush symmetry options
- [x] Brush scripting/API `evidence: LightboxTools, IpcDocumentApi`
- [x] Brush importers — .abr / .gbr / .gih / .kpp `evidence: AbrReader, GbrReader, GihReader, KppReader, BrushImportTests`
- [x] Physical media simulation (watercolour, gouache, oil, ink) `evidence: MediumSimulator, FluidLattice, Pigment, MediumRenderingTests`

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

- [?] Perspective rulers
- [?] Vanishing point tools
- [?] Grid and snapping
- [?] Shape tools
- [?] Vector guides

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

- [?] Asset browser
- [?] Asset tagging
- [?] Smart asset search
- [?] Shared symbols
- [?] Symbol editing
- [?] Linked assets — edit once, update everywhere
- [?] Dependency graph
- [?] Asset versioning
- [?] Pose library
- [?] Expression library
- [?] Hand library
- [?] Face library
- [?] Prop library
- [?] FX library
- [?] Animation library
- [?] Reusable backgrounds
- [?] Reusable animation presets
- [?] Animation templates

## Pillar 4 — Animation-aware drawing tools

Tools that know they are operating on a sequence, not a picture. This is the
pillar the determinism invariant exists to make possible: an effect that
varies between similar strokes is fine on one image and boils at 12 fps.

- [x] Deterministic marks across frames (no boiling) `evidence: OutputScaleTests, BrushDynamicsTests, ScalingTheCoordinatesInstead_ProducesADifferentMark`
- [x] AI inbetweening `evidence: Inbetweener, InbetweenerTests, IAiArtist`
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
- [?] Loop regions
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
