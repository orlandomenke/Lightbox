# Hotspots

Where change has been concentrated and where it is risky. **Heat** combines
commit churn, fix-commit churn, how many files depend on this one, and size.
**Risk** is heat discounted by how many test files exercise the file — a hot
file with no tests is the top of this list for a reason.

## Riskiest to change

| File | Risk | Heat | Commits | Fixes | Dependents | Test files |
| --- | --- | --- | --- | --- | --- | --- |
| `src/Lightbox.App/Views/MainWindow.axaml` | 0.55 | 0.55 | 44 | 5 | 0 | 0 |
| `src/Lightbox.App/ViewModels/MainViewModel.cs` | 0.23 | 0.93 | 48 | 7 | 53 | 46 |
| `src/Lightbox.App/Views/MainWindow.axaml.cs` | 0.16 | 0.63 | 42 | 7 | 4 | 3 |
| `src/Lightbox.App/Rendering/CanvasControl.cs` | 0.11 | 0.43 | 24 | 5 | 6 | 5 |
| `src/Lightbox.App/App.axaml` | 0.10 | 0.10 | 9 | 1 | 0 | 0 |
| `src/Lightbox.Raster/BrushEngine.cs` | 0.08 | 0.33 | 19 | 3 | 14 | 10 |
| `src/Lightbox.App/Services/BrushPresets.cs` | 0.07 | 0.10 | 6 | 1 | 3 | 0 |
| `src/Lightbox.App/ViewModels/LayerRow.cs` | 0.07 | 0.09 | 5 | 1 | 4 | 1 |
| `src/Lightbox.Core/Documents/Frame.cs` | 0.07 | 0.26 | 2 | 0 | 74 | 52 |
| `src/Lightbox.Core/Geometry/GeometryOps.cs` | 0.06 | 0.09 | 2 | 1 | 8 | 1 |
| `src/Lightbox.Raster/Media/PigmentModel.cs` | 0.06 | 0.08 | 3 | 1 | 4 | 1 |
| `src/Lightbox.Raster/Media/MediumSimulator.cs` | 0.06 | 0.06 | 2 | 1 | 0 | 0 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml` | 0.06 | 0.06 | 2 | 1 | 0 | 0 |
| `src/Lightbox.Core/Documents/Stroke.cs` | 0.06 | 0.23 | 4 | 0 | 60 | 38 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml` | 0.06 | 0.06 | 2 | 1 | 0 | 0 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml.cs` | 0.06 | 0.07 | 2 | 1 | 5 | 1 |
| `src/Lightbox.App/Services/IpcDocumentApi.cs` | 0.05 | 0.07 | 3 | 1 | 2 | 1 |
| `src/Lightbox.Core/Documents/BrushSettings.cs` | 0.05 | 0.21 | 7 | 1 | 35 | 26 |
| `src/Lightbox.Core/Documents/Scene.cs` | 0.05 | 0.21 | 10 | 2 | 17 | 5 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` | 0.05 | 0.07 | 2 | 1 | 2 | 0 |

## Most active regardless of coverage

- `src/Lightbox.App/ViewModels/MainViewModel.cs` — heat 0.93, 48 commits (7 fixes), 53 dependents
- `src/Lightbox.App/Views/MainWindow.axaml.cs` — heat 0.63, 42 commits (7 fixes), 4 dependents
- `src/Lightbox.App/Views/MainWindow.axaml` — heat 0.55, 44 commits (5 fixes), 0 dependents
- `src/Lightbox.App/Rendering/CanvasControl.cs` — heat 0.43, 24 commits (5 fixes), 6 dependents
- `src/Lightbox.Raster/BrushEngine.cs` — heat 0.33, 19 commits (3 fixes), 14 dependents
- `src/Lightbox.Core/Documents/Frame.cs` — heat 0.26, 2 commits (0 fixes), 74 dependents
- `src/Lightbox.Core/Documents/Stroke.cs` — heat 0.23, 4 commits (0 fixes), 60 dependents
- `src/Lightbox.Core/Documents/BrushSettings.cs` — heat 0.21, 7 commits (1 fixes), 35 dependents
- `src/Lightbox.Core/Documents/Scene.cs` — heat 0.21, 10 commits (2 fixes), 17 dependents
- `src/Lightbox.App/Rendering/SceneRenderer.cs` — heat 0.20, 13 commits (2 fixes), 7 dependents

## Substantial files with no test reference

- `src/Lightbox.App/Views/MainWindow.axaml` (2318 ln)
- `src/Lightbox.Raster/Media/MediumSimulator.cs` (298 ln)
- `src/Lightbox.App/Controls/TimelineRuler.cs` (181 ln)
- `src/Lightbox.App/Controls/ColorField.cs` (171 ln)
- `src/Lightbox.App/Styles/Density.axaml` (155 ln)
- `src/Lightbox.App/Styles/ColorPicker.axaml` (135 ln)
- `src/Lightbox.Mcp/LightboxTools.cs` (130 ln)
- `src/Lightbox.App/App.axaml` (119 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (115 ln)
- `src/Lightbox.App/Controls/DockDropIndicator.cs` (101 ln)
