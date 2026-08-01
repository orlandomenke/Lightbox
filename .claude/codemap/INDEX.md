# Code index

Generated from `ace1d7c` · 158 files · 31989 lines · 533 tests.

Read this before searching. Each entry lists the types a file declares and
the line they start on, so you can open the exact region instead of the
whole file. `codemap.py find <term>` answers targeted questions.

## Lightbox.Ai

- `src/Lightbox.Ai/AnthropicArtist.cs` (184 ln) · 30 indirect only
  - AnthropicArtist:18
- `src/Lightbox.Ai/OllamaArtist.cs` (150 ln) · 1 test files
  - OllamaArtist:17, ChatMessage:33, ChatResponse:39
- `src/Lightbox.Ai/StrokePayload.cs` (126 ln) · 1 test files
  - StrokeWire:15, PointDto:22, StrokeDto:29, InbetweenFrameDto:40, InbetweenResultDto:46, DrawResultDto:51
- `src/Lightbox.Ai/Prompts.cs` (89 ln) · 1 test files
  - Prompts:11
- `src/Lightbox.Ai/ApiKeyProvider.cs` (81 ln) · 30 indirect only
  - ApiKeyProvider:10
- `src/Lightbox.Ai/StrokeSchemas.cs` (73 ln) · 1 test files
  - StrokeSchemas:10
- `src/Lightbox.Ai/AiResult.cs` (41 ln) · 2 test files
  - AiOutcome:3, AiResult:15
- `src/Lightbox.Ai/IAiArtist.cs` (38 ln) · 3 test files
  - SceneInfo:6, InbetweenRequest:8, InbetweenFrameResult:17, DrawRequest:19, IAiArtist:32

## Lightbox.App

- `src/Lightbox.App/ViewModels/MainViewModel.cs` (4302 ln) · 30 test files
  - FrameCell:18, MainViewModel:61, LayerKindChoice:1827
- `src/Lightbox.App/Rendering/CanvasControl.cs` (1579 ln) · 3 test files
  - CanvasControl:28, CanvasToolMode:250, TxDrag:293, DrawOp:1267
- `src/Lightbox.App/Views/MainWindow.axaml` (1476 ln) · **no tests**
  - BrushCategoryList, BrushPageEffects, BrushPageGeneral, BrushPageMedium, BrushPagePresets, BrushPagePressure, Canvas, CanvasHost …
- `src/Lightbox.App/Views/MainWindow.axaml.cs` (1126 ln) · **no tests**
  - MainWindow:13
- `src/Lightbox.App/Services/SpriteSheetExporter.cs` (323 ln) · 24 test files
  - SpriteTrim:11, SpriteSheetOptions:35, SpriteSheetResult:46, SpriteSheetExporter:69, SheetDocument:275, SheetFrame:281, SheetMeta:295, Box:310 …
- `src/Lightbox.App/Rendering/SceneRenderer.cs` (292 ln) · 4 test files
  - StrokeOverlay:23, RenderPass:35, SceneRenderer:48
- `src/Lightbox.App/Services/BrushPresets.cs` (271 ln) · 31 indirect only
  - BrushPreset:7, BuiltInPresets:24, PresetStore:218, State:223
- `src/Lightbox.App/Rendering/ComposeRing.cs` (264 ln) · 2 test files
  - ComposeRing:31, Buffer:33
- `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` (211 ln) · **no tests**
  - ShortcutRow:11, ShortcutGroup:23, ConfigureWindow:36
- `src/Lightbox.App/ViewModels/ColorPickerViewModel.cs` (209 ln) · 1 test files
  - ColorMode:7, ColorPickerViewModel:23
- `src/Lightbox.App/Services/ShortcutMap.cs` (196 ln) · 2 test files
  - ShortcutContext:7, ShortcutDefinition:16, ShortcutMap:41
- `src/Lightbox.App/ViewModels/LayerRow.cs` (194 ln) · 1 test files
  - GroupRow:11, LayerRow:90
- `src/Lightbox.App/Services/IpcDocumentApi.cs` (193 ln) · 1 test files
  - IpcDocumentApi:15, FrameRef:86, InsertPayload:118, DrawPayload:146, ViewRef:167
- `src/Lightbox.App/Controls/TimelineRuler.cs` (181 ln) · **no tests**
  - TimelineRuler:17
- `src/Lightbox.App/Services/PerformanceMonitor.cs` (180 ln) · 30 indirect only
  - PerformanceMonitor:14
- `src/Lightbox.App/Rendering/FrameBitmapCache.cs` (126 ln) · 1 test files
  - FrameBitmapCache:13
- `src/Lightbox.App/Services/IpcServer.cs` (115 ln) · 1 test files
  - IpcServer:13
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (115 ln) · **no tests**
  - CacheBudgetBox, CategoryList, ConflictBar, ConflictText, GroupsHost, MeasuredText, PerformancePage, QualityBox …
- `src/Lightbox.App/Services/ColorSpace.cs` (109 ln) · 1 test files
  - ColorSpace:8
- `src/Lightbox.App/Services/StrokeStabilizer.cs` (103 ln) · 1 test files
  - SmoothingMode:7, StrokeStabilizer:34
- `src/Lightbox.App/Rendering/CameraTransform.cs` (95 ln) · 1 test files
  - CameraTransform:16
- `src/Lightbox.App/ViewModels/Tools.cs` (74 ln) · 7 test files
  - ToolId:4, CanvasQuality:20, TransformScope:33, TransformSampling:52, SelectVariant:65
- `src/Lightbox.App/ViewModels/DocumentTab.cs` (72 ln) · 7 test files
  - NewDocumentSettings:8, DocumentTabKind:17, DocumentTab:33
- `src/Lightbox.App/Controls/Docker.cs` (66 ln) · 12 indirect only
  - Docker:12
- `src/Lightbox.App/Services/SequenceExporter.cs` (62 ln) · 2 test files
  - SequenceExporter:20
- `src/Lightbox.App/Views/NewDocumentDialog.axaml.cs` (61 ln) · 1 test files
  - NewDocumentDialog:12, Preset:14
- `src/Lightbox.App/Rendering/ThumbnailRenderer.cs` (60 ln) · 30 indirect only
  - ThumbnailRenderer:7
- `src/Lightbox.App/Views/NewDocumentDialog.axaml` (59 ln) · **no tests**
  - BackgroundBox, FpsBox, HeightBox, NameBox, PpiBox, PresetBox, TransparentBox, WidthBox
- `src/Lightbox.App/Input/StrokeBuilder.cs` (50 ln) · 30 indirect only
  - StrokeBuilder:10
- `src/Lightbox.App/App.axaml` (49 ln) · **no tests**
- `src/Lightbox.App/Services/AutosaveService.cs` (49 ln) · 30 indirect only
  - AutosaveService:12
- `src/Lightbox.App/Services/IpcProtocol.cs` (43 ln) · 4 test files
  - IpcProtocol:12, Request:23, Response:29
- `src/Lightbox.App/Services/PlaybackClock.cs` (35 ln) · 1 test files
  - PlaybackClock:9
- `src/Lightbox.App/Rendering/RenderSnapshot.cs` (24 ln) · 6 test files
  - RenderSnapshot:16
- `src/Lightbox.App/App.axaml.cs` (20 ln) · 51 indirect only
  - App:8
- `src/Lightbox.App/Program.cs` (16 ln) · **no tests**
  - Program:5

## Lightbox.Core

- `src/Lightbox.Core/Timeline/DocumentEditor.cs` (509 ln) · 4 test files
  - DocumentEditor:14, IEditStep:104, SnapshotStep:117, DeltaStep:136
- `src/Lightbox.Core/Documents/BrushSettings.cs` (242 ln) · 20 test files
  - SmudgeMode:8, BrushKind:25, BrushSettings:44
- `src/Lightbox.Core/Geometry/TransformOps.cs` (219 ln) · 1 test files
  - TransformOps:12
- `src/Lightbox.Core/Documents/MediumSettings.cs` (191 ln) · 7 test files
  - MediumKind:10, PaperKind:29, MediumSettings:54
- `src/Lightbox.Core/Documents/Camera.cs` (146 ln) · 3 test files
  - CameraKey:10, Camera:46, CameraOps:65
- `src/Lightbox.Core/Geometry/GeometryOps.cs` (130 ln) · 1 test files
  - GeometryOps:5
- `src/Lightbox.Core/Documents/Layer.cs` (115 ln) · 12 test files
  - LayerKind:3, LayerBlendMode:14, Cel:38, LayerGroup:49, Layer:67
- `src/Lightbox.Core/Inbetween/Inbetweener.cs` (94 ln) · 2 test files
  - Inbetweener:19
- `src/Lightbox.Core/Inbetween/StrokeRecordCleaner.cs` (87 ln) · 1 test files
  - StrokeRecordCleaner:20
- `src/Lightbox.Core/Documents/Scene.cs` (85 ln) · 4 test files
  - FrameMarker:4, Scene:13
- `src/Lightbox.Core/Geometry/StrokeFilters.cs` (80 ln) · 1 test files
  - StrokeFilters:10
- `src/Lightbox.Core/Inbetween/StrokeMatcher.cs` (73 ln) · 1 test files
  - StrokePair:6, StrokeMatcher:13
- `src/Lightbox.Core/Documents/DocumentFactory.cs` (71 ln) · 14 test files
  - DocumentFactory:3
- `src/Lightbox.Core/Serialization/FrameConverter.cs` (64 ln) · 37 indirect only
  - FrameConverter:14
- `src/Lightbox.Core/Documents/Stroke.cs` (61 ln) · 28 test files
  - Stroke:9
- `src/Lightbox.Core/Documents/ReferenceSheet.cs` (50 ln) · 1 test files
  - ReferenceSheet:11, ReferenceView:21
- `src/Lightbox.Core/Timeline/ExposureSheet.cs` (50 ln) · 2 test files
  - ExposureSheet:8
- `src/Lightbox.Core/Documents/Frame.cs` (47 ln) · 37 test files
  - FrameRole:8, Frame:19, VectorFrame:27, PaintedFrame:40
- `src/Lightbox.Core/Serialization/DocJson.cs` (44 ln) · 8 test files
  - DocJson:11
- `src/Lightbox.Core/Inbetween/StrokeInterpolator.cs` (43 ln) · 1 test files
  - StrokeInterpolator:6
- `src/Lightbox.Core/Documents/Doc.cs` (39 ln) · 6 test files
  - Doc:8, ClipRegion:33
- `src/Lightbox.Core/Geometry/ColorOps.cs` (29 ln) · 1 test files
  - ColorOps:3
- `src/Lightbox.Core/Inbetween/Easing.cs` (20 ln) · 6 test files
  - Easing:3, EasingOps:11
- `src/Lightbox.Core/Documents/Pivot.cs` (16 ln) · 2 test files
  - Pivot:7
- `src/Lightbox.Core/Documents/Ids.cs` (14 ln) · 56 indirect only
  - Ids:3
- `src/Lightbox.Core/Documents/ToolKind.cs` (14 ln) · 23 test files
  - ToolKind:3

## Lightbox.Import

- `src/Lightbox.Import/AbrReader.cs` (178 ln) · 1 test files
  - AbrReader:13
- `src/Lightbox.Import/KppReader.cs` (95 ln) · 1 test files
  - KppReader:14
- `src/Lightbox.Import/GbrReader.cs` (85 ln) · 1 test files
  - GbrReader:12, GihReader:60
- `src/Lightbox.Import/ImportedBrush.cs` (55 ln) · 1 test files
  - ImportedBrush:11, BrushImport:20

## Lightbox.Mcp

- `src/Lightbox.Mcp/LightboxTools.cs` (130 ln) · **no tests**
  - LightboxTools:21
- `src/Lightbox.Mcp/PipeBridge.cs` (72 ln) · **no tests**
  - PipeBridge:12, Response:24, LightboxUnavailableException:69, LightboxOpException:72

## Lightbox.Raster

- `src/Lightbox.Raster/BrushEngine.cs` (1073 ln) · 7 test files
  - BrushEngine:25
- `src/Lightbox.Raster/Media/FluidLattice.cs` (820 ln) · 1 test files
  - FluidLattice:59
- `src/Lightbox.Raster/FloodFill.cs` (416 ln) · 2 test files
  - FloodFill:13, Options:15, Result:21, ContourTracer:280
- `src/Lightbox.Raster/Media/PaperField.cs` (406 ln) · 2 test files
  - PaperField:26, Tile:81
- `src/Lightbox.Raster/Media/PigmentModel.cs` (356 ln) · 1 test files
  - Pigment:31
- `src/Lightbox.Raster/Media/MediumSimulator.cs` (298 ln) · **no tests**
  - MediumSimulator:22
- `src/Lightbox.Raster/FrameRasterizer.cs` (104 ln) · 8 test files
  - FrameRasterizer:10
- `src/Lightbox.Raster/BrushTipRegistry.cs` (34 ln) · 1 test files
  - BrushTipRegistry:12
- `src/Lightbox.Raster/ClipRegionRegistry.cs` (27 ln) · 2 test files
  - ClipRegionRegistry:12
- `src/Lightbox.Raster/PngCodec.cs` (22 ln) · 1 test files
  - PngCodec:6
