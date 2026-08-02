# Hotspots

Where change has been concentrated and where it is risky. **Heat** combines
commit churn, fix-commit churn, how many files depend on this one, and size.
**Risk** is heat discounted by how many test files exercise the file — a hot
file with no tests is the top of this list for a reason.

## Riskiest to change

| File | Risk | Heat | Commits | Fixes | Dependents | Test files |
| --- | --- | --- | --- | --- | --- | --- |
| `src/Lightbox.App/Views/MainWindow.axaml` | 0.54 | 0.54 | 39 | 5 | 0 | 0 |
| `src/Lightbox.App/Views/MainWindow.axaml.cs` | 0.46 | 0.61 | 38 | 7 | 2 | 1 |
| `src/Lightbox.App/ViewModels/MainViewModel.cs` | 0.23 | 0.92 | 44 | 7 | 50 | 43 |
| `src/Lightbox.App/Rendering/CanvasControl.cs` | 0.11 | 0.44 | 23 | 5 | 6 | 5 |
| `src/Lightbox.App/App.axaml` | 0.09 | 0.09 | 7 | 1 | 0 | 0 |
| `src/Lightbox.Raster/BrushEngine.cs` | 0.09 | 0.34 | 19 | 3 | 14 | 10 |
| `src/Lightbox.App/Services/BrushPresets.cs` | 0.08 | 0.10 | 6 | 1 | 3 | 0 |
| `src/Lightbox.App/ViewModels/LayerRow.cs` | 0.07 | 0.10 | 5 | 1 | 4 | 1 |
| `src/Lightbox.Core/Documents/Frame.cs` | 0.07 | 0.26 | 2 | 0 | 72 | 50 |
| `src/Lightbox.Core/Geometry/GeometryOps.cs` | 0.07 | 0.09 | 2 | 1 | 8 | 1 |
| `src/Lightbox.Raster/Media/PigmentModel.cs` | 0.07 | 0.09 | 3 | 1 | 4 | 1 |
| `src/Lightbox.Raster/Media/MediumSimulator.cs` | 0.06 | 0.06 | 2 | 1 | 0 | 0 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` | 0.06 | 0.07 | 2 | 1 | 2 | 0 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml` | 0.06 | 0.06 | 2 | 1 | 0 | 0 |
| `src/Lightbox.Core/Documents/Stroke.cs` | 0.06 | 0.24 | 4 | 0 | 60 | 38 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml` | 0.06 | 0.06 | 2 | 1 | 0 | 0 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml.cs` | 0.06 | 0.08 | 2 | 1 | 5 | 1 |
| `src/Lightbox.App/Services/IpcDocumentApi.cs` | 0.06 | 0.08 | 3 | 1 | 2 | 1 |
| `src/Lightbox.Core/Documents/BrushSettings.cs` | 0.05 | 0.22 | 7 | 1 | 35 | 26 |
| `src/Lightbox.Core/Documents/Scene.cs` | 0.05 | 0.20 | 8 | 2 | 16 | 5 |

## Most active regardless of coverage

- `src/Lightbox.App/ViewModels/MainViewModel.cs` — heat 0.92, 44 commits (7 fixes), 50 dependents
- `src/Lightbox.App/Views/MainWindow.axaml.cs` — heat 0.61, 38 commits (7 fixes), 2 dependents
- `src/Lightbox.App/Views/MainWindow.axaml` — heat 0.54, 39 commits (5 fixes), 0 dependents
- `src/Lightbox.App/Rendering/CanvasControl.cs` — heat 0.44, 23 commits (5 fixes), 6 dependents
- `src/Lightbox.Raster/BrushEngine.cs` — heat 0.34, 19 commits (3 fixes), 14 dependents
- `src/Lightbox.Core/Documents/Frame.cs` — heat 0.26, 2 commits (0 fixes), 72 dependents
- `src/Lightbox.Core/Documents/Stroke.cs` — heat 0.24, 4 commits (0 fixes), 60 dependents
- `src/Lightbox.Core/Documents/BrushSettings.cs` — heat 0.22, 7 commits (1 fixes), 35 dependents
- `src/Lightbox.Core/Documents/Scene.cs` — heat 0.20, 8 commits (2 fixes), 16 dependents
- `src/Lightbox.App/Rendering/SceneRenderer.cs` — heat 0.19, 11 commits (2 fixes), 7 dependents

## Substantial files with no test reference

- `src/Lightbox.App/Views/MainWindow.axaml` (2078 ln)
- `src/Lightbox.Raster/Media/MediumSimulator.cs` (298 ln)
- `src/Lightbox.App/Controls/TimelineRuler.cs` (181 ln)
- `src/Lightbox.App/Controls/ColorField.cs` (171 ln)
- `src/Lightbox.App/Styles/Density.axaml` (155 ln)
- `src/Lightbox.App/Styles/ColorPicker.axaml` (135 ln)
- `src/Lightbox.Mcp/LightboxTools.cs` (130 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (115 ln)
- `src/Lightbox.App/Controls/DockDropIndicator.cs` (101 ln)
- `src/Lightbox.App/App.axaml` (84 ln)
