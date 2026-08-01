# Hotspots

Where change has been concentrated and where it is risky. **Heat** combines
commit churn, fix-commit churn, how many files depend on this one, and size.
**Risk** is heat discounted by how many test files exercise the file — a hot
file with no tests is the top of this list for a reason.

## Riskiest to change

| File | Risk | Heat | Commits | Fixes | Dependents | Test files |
| --- | --- | --- | --- | --- | --- | --- |
| `src/Lightbox.App/Views/MainWindow.axaml.cs` | 0.59 | 0.59 | 26 | 7 | 1 | 0 |
| `src/Lightbox.App/Views/MainWindow.axaml` | 0.53 | 0.53 | 28 | 5 | 0 | 0 |
| `src/Lightbox.App/ViewModels/MainViewModel.cs` | 0.20 | 0.81 | 32 | 5 | 34 | 30 |
| `src/Lightbox.App/Rendering/CanvasControl.cs` | 0.11 | 0.43 | 20 | 4 | 4 | 3 |
| `src/Lightbox.App/Services/BrushPresets.cs` | 0.09 | 0.12 | 6 | 1 | 2 | 0 |
| `src/Lightbox.App/ViewModels/LayerRow.cs` | 0.09 | 0.11 | 5 | 1 | 4 | 1 |
| `src/Lightbox.App/App.axaml` | 0.08 | 0.08 | 4 | 1 | 0 | 0 |
| `src/Lightbox.Raster/BrushEngine.cs` | 0.08 | 0.32 | 16 | 2 | 11 | 7 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` | 0.08 | 0.08 | 2 | 1 | 2 | 0 |
| `src/Lightbox.Core/Geometry/GeometryOps.cs` | 0.08 | 0.10 | 2 | 1 | 8 | 1 |
| `src/Lightbox.Raster/Media/PigmentModel.cs` | 0.08 | 0.10 | 3 | 1 | 4 | 1 |
| `src/Lightbox.Raster/Media/MediumSimulator.cs` | 0.07 | 0.07 | 2 | 1 | 0 | 0 |
| `src/Lightbox.Core/Documents/Frame.cs` | 0.07 | 0.27 | 2 | 0 | 57 | 37 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml` | 0.07 | 0.07 | 2 | 1 | 0 | 0 |
| `src/Lightbox.App/Services/IpcDocumentApi.cs` | 0.06 | 0.09 | 3 | 1 | 2 | 1 |
| `src/Lightbox.Core/Documents/Stroke.cs` | 0.06 | 0.25 | 3 | 0 | 49 | 28 |
| `src/Lightbox.Core/Documents/BrushSettings.cs` | 0.06 | 0.24 | 7 | 1 | 29 | 20 |
| `src/Lightbox.App/ViewModels/ColorPickerViewModel.cs` | 0.06 | 0.08 | 2 | 1 | 2 | 1 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml` | 0.05 | 0.05 | 1 | 1 | 0 | 0 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml.cs` | 0.05 | 0.07 | 1 | 1 | 4 | 1 |

## Most active regardless of coverage

- `src/Lightbox.App/ViewModels/MainViewModel.cs` — heat 0.81, 32 commits (5 fixes), 34 dependents
- `src/Lightbox.App/Views/MainWindow.axaml.cs` — heat 0.59, 26 commits (7 fixes), 1 dependents
- `src/Lightbox.App/Views/MainWindow.axaml` — heat 0.53, 28 commits (5 fixes), 0 dependents
- `src/Lightbox.App/Rendering/CanvasControl.cs` — heat 0.43, 20 commits (4 fixes), 4 dependents
- `src/Lightbox.Raster/BrushEngine.cs` — heat 0.32, 16 commits (2 fixes), 11 dependents
- `src/Lightbox.Core/Documents/Frame.cs` — heat 0.27, 2 commits (0 fixes), 57 dependents
- `src/Lightbox.Core/Documents/Stroke.cs` — heat 0.25, 3 commits (0 fixes), 49 dependents
- `src/Lightbox.Core/Documents/BrushSettings.cs` — heat 0.24, 7 commits (1 fixes), 29 dependents
- `src/Lightbox.App/Rendering/SceneRenderer.cs` — heat 0.21, 9 commits (2 fixes), 7 dependents
- `src/Lightbox.Core/Timeline/DocumentEditor.cs` — heat 0.20, 8 commits (2 fixes), 6 dependents

## Substantial files with no test reference

- `src/Lightbox.App/Views/MainWindow.axaml` (1476 ln)
- `src/Lightbox.App/Views/MainWindow.axaml.cs` (1126 ln)
- `src/Lightbox.Raster/Media/MediumSimulator.cs` (298 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` (211 ln)
- `src/Lightbox.App/Controls/TimelineRuler.cs` (181 ln)
- `src/Lightbox.Mcp/LightboxTools.cs` (130 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (115 ln)
