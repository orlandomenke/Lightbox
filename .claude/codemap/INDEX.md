# Code index

Generated from `3add3a6` · 133 files · 23659 lines · 382 tests.

Read this before searching. Each entry lists the types a file declares and
the line they start on, so you can open the exact region instead of the
whole file. `codemap.py find <term>` answers targeted questions.

## Lightbox.Ai

- `src/Lightbox.Ai/AnthropicArtist.cs` (184 ln) · 24 indirect only
  - AnthropicArtist:18
- `src/Lightbox.Ai/OllamaArtist.cs` (150 ln) · 1 test files
  - OllamaArtist:17, ChatMessage:33, ChatResponse:39
- `src/Lightbox.Ai/StrokePayload.cs` (126 ln) · 1 test files
  - StrokeWire:15, PointDto:22, StrokeDto:29, InbetweenFrameDto:40, InbetweenResultDto:46, DrawResultDto:51
- `src/Lightbox.Ai/Prompts.cs` (89 ln) · 1 test files
  - Prompts:11
- `src/Lightbox.Ai/ApiKeyProvider.cs` (81 ln) · 24 indirect only
  - ApiKeyProvider:10
- `src/Lightbox.Ai/StrokeSchemas.cs` (73 ln) · 1 test files
  - StrokeSchemas:10
- `src/Lightbox.Ai/AiResult.cs` (41 ln) · 2 test files
  - AiOutcome:3, AiResult:15
- `src/Lightbox.Ai/IAiArtist.cs` (38 ln) · 3 test files
  - SceneInfo:6, InbetweenRequest:8, InbetweenFrameResult:17, DrawRequest:19, IAiArtist:32

## Lightbox.App

- `src/Lightbox.App/ViewModels/MainViewModel.cs` (3618 ln) · 24 test files
  - FrameCell:18, MainViewModel:61, LayerKindChoice:1593
- `src/Lightbox.App/Rendering/CanvasControl.cs` (1432 ln) · 2 test files
  - CanvasControl:28, CanvasToolMode:204, TxDrag:247, DrawOp:1205
- `src/Lightbox.App/Views/MainWindow.axaml` (1311 ln) · **no tests**
  - BrushCategoryList, BrushPageEffects, BrushPageGeneral, BrushPageMedium, BrushPagePresets, BrushPagePressure, Canvas, CanvasHost …
- `src/Lightbox.App/Views/MainWindow.axaml.cs` (1073 ln) · **no tests**
  - MainWindow:13
- `src/Lightbox.App/Services/BrushPresets.cs` (248 ln) · 24 indirect only
  - BrushPreset:7, BuiltInPresets:24, PresetStore:195, State:200
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
- `src/Lightbox.App/Services/PerformanceMonitor.cs` (180 ln) · 24 indirect only
  - PerformanceMonitor:14
- `src/Lightbox.App/Rendering/SceneRenderer.cs` (179 ln) · 1 test files
  - StrokeOverlay:14, RenderPass:17, SceneRenderer:30
- `src/Lightbox.App/Rendering/ComposeRing.cs` (147 ln) · 24 indirect only
  - ComposeRing:21, Buffer:23
- `src/Lightbox.App/Services/IpcServer.cs` (115 ln) · 1 test files
  - IpcServer:13
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (115 ln) · **no tests**
  - CacheBudgetBox, CategoryList, ConflictBar, ConflictText, GroupsHost, MeasuredText, PerformancePage, QualityBox …
- `src/Lightbox.App/Services/ColorSpace.cs` (109 ln) · 1 test files
  - ColorSpace:8
- `src/Lightbox.App/Services/StrokeStabilizer.cs` (103 ln) · 1 test files
  - SmoothingMode:7, StrokeStabilizer:34
- `src/Lightbox.App/Rendering/FrameBitmapCache.cs` (89 ln) · 24 indirect only
  - FrameBitmapCache:12
- `src/Lightbox.App/ViewModels/Tools.cs` (74 ln) · 5 test files
  - ToolId:4, CanvasQuality:20, TransformScope:33, TransformSampling:52, SelectVariant:65
- `src/Lightbox.App/ViewModels/DocumentTab.cs` (72 ln) · 3 test files
  - NewDocumentSettings:8, DocumentTabKind:17, DocumentTab:33
- `src/Lightbox.App/Controls/Docker.cs` (66 ln) · 9 indirect only
  - Docker:12
- `src/Lightbox.App/Views/NewDocumentDialog.axaml.cs` (61 ln) · 1 test files
  - NewDocumentDialog:12, Preset:14
- `src/Lightbox.App/Rendering/ThumbnailRenderer.cs` (60 ln) · 24 indirect only
  - ThumbnailRenderer:7
- `src/Lightbox.App/Views/NewDocumentDialog.axaml` (59 ln) · **no tests**
  - BackgroundBox, FpsBox, HeightBox, NameBox, PpiBox, PresetBox, TransparentBox, WidthBox
- `src/Lightbox.App/Input/StrokeBuilder.cs` (50 ln) · 24 indirect only
  - StrokeBuilder:10
- `src/Lightbox.App/App.axaml` (49 ln) · **no tests**
- `src/Lightbox.App/Services/AutosaveService.cs` (49 ln) · 24 indirect only
  - AutosaveService:12
- `src/Lightbox.App/Services/SequenceExporter.cs` (45 ln) · 1 test files
  - SequenceExporter:13
- `src/Lightbox.App/Services/IpcProtocol.cs` (43 ln) · 4 test files
  - IpcProtocol:12, Request:23, Response:29
- `src/Lightbox.App/Services/PlaybackClock.cs` (35 ln) · 1 test files
  - PlaybackClock:9
- `src/Lightbox.App/Rendering/RenderSnapshot.cs` (24 ln) · 3 test files
  - RenderSnapshot:16
- `src/Lightbox.App/App.axaml.cs` (20 ln) · 26 indirect only
  - App:8
- `src/Lightbox.App/Program.cs` (16 ln) · **no tests**
  - Program:5

## Lightbox.Core

- `src/Lightbox.Core/Timeline/DocumentEditor.cs` (437 ln) · 3 test files
  - DocumentEditor:14, IEditStep:104, SnapshotStep:117, DeltaStep:136
- `src/Lightbox.Core/Geometry/TransformOps.cs` (219 ln) · 1 test files
  - TransformOps:12
- `src/Lightbox.Core/Documents/MediumSettings.cs` (158 ln) · 2 test files
  - MediumKind:10, PaperKind:29, MediumSettings:54
- `src/Lightbox.Core/Geometry/GeometryOps.cs` (130 ln) · 1 test files
  - GeometryOps:5
- `src/Lightbox.Core/Documents/BrushSettings.cs` (112 ln) · 14 test files
  - BrushKind:4, BrushSettings:23
- `src/Lightbox.Core/Documents/Layer.cs` (106 ln) · 9 test files
  - LayerKind:3, LayerBlendMode:14, Cel:38, LayerGroup:49, Layer:67
- `src/Lightbox.Core/Inbetween/Inbetweener.cs` (94 ln) · 2 test files
  - Inbetweener:19
- `src/Lightbox.Core/Inbetween/StrokeRecordCleaner.cs` (87 ln) · 1 test files
  - StrokeRecordCleaner:20
- `src/Lightbox.Core/Geometry/StrokeFilters.cs` (80 ln) · 1 test files
  - StrokeFilters:10
- `src/Lightbox.Core/Inbetween/StrokeMatcher.cs` (73 ln) · 1 test files
  - StrokePair:6, StrokeMatcher:13
- `src/Lightbox.Core/Serialization/FrameConverter.cs` (64 ln) · 29 indirect only
  - FrameConverter:14
- `src/Lightbox.Core/Documents/Stroke.cs` (61 ln) · 19 test files
  - Stroke:9
- `src/Lightbox.Core/Documents/Scene.cs` (60 ln) · 2 test files
  - FrameMarker:4, Scene:13
- `src/Lightbox.Core/Documents/ReferenceSheet.cs` (50 ln) · 1 test files
  - ReferenceSheet:11, ReferenceView:21
- `src/Lightbox.Core/Timeline/ExposureSheet.cs` (50 ln) · 2 test files
  - ExposureSheet:8
- `src/Lightbox.Core/Documents/Frame.cs` (47 ln) · 29 test files
  - FrameRole:8, Frame:19, VectorFrame:27, PaintedFrame:40
- `src/Lightbox.Core/Serialization/DocJson.cs` (44 ln) · 7 test files
  - DocJson:11
- `src/Lightbox.Core/Inbetween/StrokeInterpolator.cs` (43 ln) · 1 test files
  - StrokeInterpolator:6
- `src/Lightbox.Core/Documents/Doc.cs` (39 ln) · 3 test files
  - Doc:8, ClipRegion:33
- `src/Lightbox.Core/Geometry/ColorOps.cs` (29 ln) · 1 test files
  - ColorOps:3
- `src/Lightbox.Core/Documents/DocumentFactory.cs` (25 ln) · 9 test files
  - DocumentFactory:3
- `src/Lightbox.Core/Inbetween/Easing.cs` (20 ln) · 4 test files
  - Easing:3, EasingOps:11
- `src/Lightbox.Core/Documents/Ids.cs` (14 ln) · 42 indirect only
  - Ids:3
- `src/Lightbox.Core/Documents/ToolKind.cs` (14 ln) · 15 test files
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

- `src/Lightbox.Raster/BrushEngine.cs` (787 ln) · 5 test files
  - BrushEngine:25
- `src/Lightbox.Raster/FloodFill.cs` (416 ln) · 2 test files
  - FloodFill:13, Options:15, Result:21, ContourTracer:280
- `src/Lightbox.Raster/Media/PigmentModel.cs` (326 ln) · 1 test files
  - Pigment:31
- `src/Lightbox.Raster/FrameRasterizer.cs` (74 ln) · 7 test files
  - FrameRasterizer:10
- `src/Lightbox.Raster/BrushTipRegistry.cs` (34 ln) · 1 test files
  - BrushTipRegistry:12
- `src/Lightbox.Raster/ClipRegionRegistry.cs` (27 ln) · 1 test files
  - ClipRegionRegistry:12
- `src/Lightbox.Raster/PngCodec.cs` (22 ln) · 1 test files
  - PngCodec:6
