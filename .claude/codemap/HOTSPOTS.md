# Hotspots

Where change has been concentrated and where it is risky. **Heat** combines
commit churn, fix-commit churn, how many files depend on this one, and size.
**Risk** is heat discounted by how many test files exercise the file — a hot
file with no tests is the top of this list for a reason.

## Riskiest to change

| File | Risk | Heat | Commits | Fixes | Dependents | Test files |
| --- | --- | --- | --- | --- | --- | --- |
| `src/Lightbox.App/Views/MainWindow.axaml.cs` | 0.60 | 0.60 | 25 | 7 | 1 | 0 |
| `src/Lightbox.App/Views/MainWindow.axaml` | 0.54 | 0.54 | 27 | 5 | 0 | 0 |
| `src/Lightbox.App/Rendering/CanvasControl.cs` | 0.21 | 0.42 | 18 | 4 | 3 | 2 |
| `src/Lightbox.App/ViewModels/MainViewModel.cs` | 0.20 | 0.81 | 30 | 5 | 30 | 26 |
| `src/Lightbox.App/Services/BrushPresets.cs` | 0.09 | 0.12 | 6 | 1 | 2 | 0 |
| `src/Lightbox.App/Rendering/SceneRenderer.cs` | 0.09 | 0.18 | 7 | 2 | 4 | 2 |
| `src/Lightbox.App/ViewModels/LayerRow.cs` | 0.09 | 0.12 | 5 | 1 | 4 | 1 |
| `src/Lightbox.App/App.axaml` | 0.08 | 0.08 | 4 | 1 | 0 | 0 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` | 0.08 | 0.08 | 2 | 1 | 2 | 0 |
| `src/Lightbox.Core/Geometry/GeometryOps.cs` | 0.08 | 0.11 | 2 | 1 | 8 | 1 |
| `src/Lightbox.Raster/Media/PigmentModel.cs` | 0.08 | 0.11 | 3 | 1 | 4 | 1 |
| `src/Lightbox.Core/Documents/Scene.cs` | 0.07 | 0.14 | 5 | 1 | 9 | 2 |
| `src/Lightbox.Raster/BrushEngine.cs` | 0.07 | 0.27 | 14 | 1 | 11 | 7 |
| `src/Lightbox.Core/Documents/Frame.cs` | 0.07 | 0.27 | 2 | 0 | 50 | 32 |
| `src/Lightbox.App/Services/IpcDocumentApi.cs` | 0.07 | 0.09 | 3 | 1 | 2 | 1 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml` | 0.07 | 0.07 | 2 | 1 | 0 | 0 |
| `src/Lightbox.Core/Documents/Stroke.cs` | 0.06 | 0.26 | 3 | 0 | 45 | 24 |
| `src/Lightbox.Core/Documents/BrushSettings.cs` | 0.06 | 0.25 | 7 | 1 | 25 | 16 |
| `src/Lightbox.App/ViewModels/ColorPickerViewModel.cs` | 0.06 | 0.08 | 2 | 1 | 2 | 1 |
| `src/Lightbox.App/Services/SequenceExporter.cs` | 0.06 | 0.08 | 3 | 1 | 1 | 1 |

## Most active regardless of coverage

- `src/Lightbox.App/ViewModels/MainViewModel.cs` — heat 0.81, 30 commits (5 fixes), 30 dependents
- `src/Lightbox.App/Views/MainWindow.axaml.cs` — heat 0.60, 25 commits (7 fixes), 1 dependents
- `src/Lightbox.App/Views/MainWindow.axaml` — heat 0.54, 27 commits (5 fixes), 0 dependents
- `src/Lightbox.App/Rendering/CanvasControl.cs` — heat 0.42, 18 commits (4 fixes), 3 dependents
- `src/Lightbox.Raster/BrushEngine.cs` — heat 0.27, 14 commits (1 fixes), 11 dependents
- `src/Lightbox.Core/Documents/Frame.cs` — heat 0.27, 2 commits (0 fixes), 50 dependents
- `src/Lightbox.Core/Documents/Stroke.cs` — heat 0.26, 3 commits (0 fixes), 45 dependents
- `src/Lightbox.Core/Documents/BrushSettings.cs` — heat 0.25, 7 commits (1 fixes), 25 dependents
- `src/Lightbox.Core/Timeline/DocumentEditor.cs` — heat 0.22, 8 commits (2 fixes), 6 dependents
- `src/Lightbox.Core/Documents/Layer.cs` — heat 0.19, 6 commits (0 fixes), 25 dependents

## Substantial files with no test reference

- `src/Lightbox.App/Views/MainWindow.axaml` (1446 ln)
- `src/Lightbox.App/Views/MainWindow.axaml.cs` (1120 ln)
- `src/Lightbox.Raster/Media/MediumSimulator.cs` (298 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` (211 ln)
- `src/Lightbox.App/Controls/TimelineRuler.cs` (181 ln)
- `src/Lightbox.Mcp/LightboxTools.cs` (130 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (115 ln)
