# Hotspots

Where change has been concentrated and where it is risky. **Heat** combines
commit churn, fix-commit churn, how many files depend on this one, and size.
**Risk** is heat discounted by how many test files exercise the file — a hot
file with no tests is the top of this list for a reason.

## Riskiest to change

| File | Risk | Heat | Commits | Fixes | Dependents | Test files |
| --- | --- | --- | --- | --- | --- | --- |
| `src/Lightbox.App/Views/MainWindow.axaml.cs` | 0.61 | 0.61 | 25 | 7 | 1 | 0 |
| `src/Lightbox.App/Views/MainWindow.axaml` | 0.54 | 0.54 | 26 | 5 | 0 | 0 |
| `src/Lightbox.App/Rendering/CanvasControl.cs` | 0.22 | 0.43 | 18 | 4 | 3 | 2 |
| `src/Lightbox.App/ViewModels/MainViewModel.cs` | 0.20 | 0.82 | 29 | 5 | 30 | 26 |
| `src/Lightbox.App/Services/BrushPresets.cs` | 0.09 | 0.13 | 6 | 1 | 2 | 0 |
| `src/Lightbox.App/Rendering/SceneRenderer.cs` | 0.09 | 0.19 | 7 | 2 | 4 | 2 |
| `src/Lightbox.App/ViewModels/LayerRow.cs` | 0.09 | 0.12 | 5 | 1 | 4 | 1 |
| `src/Lightbox.App/App.axaml` | 0.09 | 0.09 | 4 | 1 | 0 | 0 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` | 0.08 | 0.08 | 2 | 1 | 2 | 0 |
| `src/Lightbox.Core/Geometry/GeometryOps.cs` | 0.08 | 0.11 | 2 | 1 | 8 | 1 |
| `src/Lightbox.Core/Documents/Scene.cs` | 0.07 | 0.14 | 5 | 1 | 9 | 2 |
| `src/Lightbox.App/Services/IpcDocumentApi.cs` | 0.07 | 0.09 | 3 | 1 | 2 | 1 |
| `src/Lightbox.Core/Documents/Frame.cs` | 0.07 | 0.27 | 2 | 0 | 49 | 31 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml` | 0.07 | 0.07 | 2 | 1 | 0 | 0 |
| `src/Lightbox.Raster/BrushEngine.cs` | 0.07 | 0.26 | 13 | 1 | 10 | 6 |
| `src/Lightbox.Core/Documents/Stroke.cs` | 0.06 | 0.25 | 3 | 0 | 43 | 22 |
| `src/Lightbox.App/ViewModels/ColorPickerViewModel.cs` | 0.06 | 0.08 | 2 | 1 | 2 | 1 |
| `src/Lightbox.App/Services/SequenceExporter.cs` | 0.06 | 0.08 | 3 | 1 | 1 | 1 |
| `src/Lightbox.Core/Documents/BrushSettings.cs` | 0.06 | 0.23 | 6 | 1 | 24 | 15 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml.cs` | 0.06 | 0.08 | 1 | 1 | 4 | 1 |

## Most active regardless of coverage

- `src/Lightbox.App/ViewModels/MainViewModel.cs` — heat 0.82, 29 commits (5 fixes), 30 dependents
- `src/Lightbox.App/Views/MainWindow.axaml.cs` — heat 0.61, 25 commits (7 fixes), 1 dependents
- `src/Lightbox.App/Views/MainWindow.axaml` — heat 0.54, 26 commits (5 fixes), 0 dependents
- `src/Lightbox.App/Rendering/CanvasControl.cs` — heat 0.43, 18 commits (4 fixes), 3 dependents
- `src/Lightbox.Core/Documents/Frame.cs` — heat 0.27, 2 commits (0 fixes), 49 dependents
- `src/Lightbox.Raster/BrushEngine.cs` — heat 0.26, 13 commits (1 fixes), 10 dependents
- `src/Lightbox.Core/Documents/Stroke.cs` — heat 0.25, 3 commits (0 fixes), 43 dependents
- `src/Lightbox.Core/Documents/BrushSettings.cs` — heat 0.23, 6 commits (1 fixes), 24 dependents
- `src/Lightbox.Core/Documents/Layer.cs` — heat 0.19, 6 commits (0 fixes), 24 dependents
- `src/Lightbox.App/Rendering/SceneRenderer.cs` — heat 0.19, 7 commits (2 fixes), 4 dependents

## Substantial files with no test reference

- `src/Lightbox.App/Views/MainWindow.axaml` (1358 ln)
- `src/Lightbox.App/Views/MainWindow.axaml.cs` (1120 ln)
- `src/Lightbox.Raster/Media/MediumSimulator.cs` (252 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` (211 ln)
- `src/Lightbox.App/Controls/TimelineRuler.cs` (181 ln)
- `src/Lightbox.Mcp/LightboxTools.cs` (130 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (115 ln)
