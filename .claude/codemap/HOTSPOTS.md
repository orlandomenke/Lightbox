# Hotspots

Where change has been concentrated and where it is risky. **Heat** combines
commit churn, fix-commit churn, how many files depend on this one, and size.
**Risk** is heat discounted by how many test files exercise the file — a hot
file with no tests is the top of this list for a reason.

## Riskiest to change

| File | Risk | Heat | Commits | Fixes | Dependents | Test files |
| --- | --- | --- | --- | --- | --- | --- |
| `src/Lightbox.App/Views/MainWindow.axaml.cs` | 0.63 | 0.63 | 24 | 7 | 1 | 0 |
| `src/Lightbox.App/Views/MainWindow.axaml` | 0.55 | 0.55 | 24 | 5 | 0 | 0 |
| `src/Lightbox.App/Rendering/CanvasControl.cs` | 0.22 | 0.43 | 16 | 4 | 3 | 2 |
| `src/Lightbox.App/ViewModels/MainViewModel.cs` | 0.20 | 0.82 | 26 | 5 | 29 | 25 |
| `src/Lightbox.App/ViewModels/LayerRow.cs` | 0.10 | 0.13 | 5 | 1 | 4 | 1 |
| `src/Lightbox.App/Rendering/SceneRenderer.cs` | 0.09 | 0.18 | 6 | 2 | 4 | 2 |
| `src/Lightbox.App/App.axaml` | 0.09 | 0.09 | 4 | 1 | 0 | 0 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` | 0.09 | 0.09 | 2 | 1 | 2 | 0 |
| `src/Lightbox.Core/Geometry/GeometryOps.cs` | 0.08 | 0.11 | 2 | 1 | 8 | 1 |
| `src/Lightbox.App/Services/BrushPresets.cs` | 0.08 | 0.10 | 4 | 1 | 1 | 0 |
| `src/Lightbox.Core/Documents/Scene.cs` | 0.07 | 0.15 | 5 | 1 | 9 | 2 |
| `src/Lightbox.App/Services/IpcDocumentApi.cs` | 0.07 | 0.10 | 3 | 1 | 2 | 1 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml` | 0.07 | 0.07 | 2 | 1 | 0 | 0 |
| `src/Lightbox.Core/Documents/Frame.cs` | 0.07 | 0.28 | 2 | 0 | 48 | 30 |
| `src/Lightbox.App/ViewModels/ColorPickerViewModel.cs` | 0.06 | 0.09 | 2 | 1 | 2 | 1 |
| `src/Lightbox.App/Services/SequenceExporter.cs` | 0.06 | 0.08 | 3 | 1 | 1 | 1 |
| `src/Lightbox.Core/Documents/Stroke.cs` | 0.06 | 0.25 | 3 | 0 | 40 | 20 |
| `src/Lightbox.Raster/BrushEngine.cs` | 0.06 | 0.24 | 11 | 1 | 8 | 5 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml.cs` | 0.06 | 0.08 | 1 | 1 | 4 | 1 |
| `src/Lightbox.Import/ImportedBrush.cs` | 0.06 | 0.08 | 1 | 1 | 4 | 1 |

## Most active regardless of coverage

- `src/Lightbox.App/ViewModels/MainViewModel.cs` — heat 0.82, 26 commits (5 fixes), 29 dependents
- `src/Lightbox.App/Views/MainWindow.axaml.cs` — heat 0.63, 24 commits (7 fixes), 1 dependents
- `src/Lightbox.App/Views/MainWindow.axaml` — heat 0.55, 24 commits (5 fixes), 0 dependents
- `src/Lightbox.App/Rendering/CanvasControl.cs` — heat 0.43, 16 commits (4 fixes), 3 dependents
- `src/Lightbox.Core/Documents/Frame.cs` — heat 0.28, 2 commits (0 fixes), 48 dependents
- `src/Lightbox.Core/Documents/Stroke.cs` — heat 0.25, 3 commits (0 fixes), 40 dependents
- `src/Lightbox.Raster/BrushEngine.cs` — heat 0.24, 11 commits (1 fixes), 8 dependents
- `src/Lightbox.Core/Documents/BrushSettings.cs` — heat 0.23, 5 commits (1 fixes), 23 dependents
- `src/Lightbox.App/Rendering/SceneRenderer.cs` — heat 0.18, 6 commits (2 fixes), 4 dependents
- `src/Lightbox.Core/Documents/Layer.cs` — heat 0.18, 5 commits (0 fixes), 23 dependents

## Substantial files with no test reference

- `src/Lightbox.App/Views/MainWindow.axaml` (1311 ln)
- `src/Lightbox.App/Views/MainWindow.axaml.cs` (1073 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` (211 ln)
- `src/Lightbox.App/Controls/TimelineRuler.cs` (181 ln)
- `src/Lightbox.Mcp/LightboxTools.cs` (130 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (115 ln)
