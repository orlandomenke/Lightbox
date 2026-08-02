# Hotspots

Where change has been concentrated and where it is risky. **Heat** combines
commit churn, fix-commit churn, how many files depend on this one, and size.
**Risk** is heat discounted by how many test files exercise the file — a hot
file with no tests is the top of this list for a reason.

## Riskiest to change

| File | Risk | Heat | Commits | Fixes | Dependents | Test files |
| --- | --- | --- | --- | --- | --- | --- |
| `src/Lightbox.App/Views/MainWindow.axaml` | 0.54 | 0.54 | 56 | 6 | 0 | 0 |
| `src/Lightbox.App/ViewModels/MainViewModel.cs` | 0.38 | 0.76 | 63 | 8 | 5 | 2 |
| `src/Lightbox.App/Views/MainWindow.axaml.cs` | 0.15 | 0.59 | 53 | 7 | 7 | 6 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml` | 0.10 | 0.10 | 5 | 2 | 0 | 0 |
| `src/Lightbox.App/Rendering/CanvasControl.cs` | 0.10 | 0.39 | 28 | 5 | 6 | 5 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` | 0.08 | 0.11 | 5 | 2 | 2 | 0 |
| `src/Lightbox.App/App.axaml` | 0.08 | 0.08 | 9 | 1 | 0 | 0 |
| `src/Lightbox.Raster/BrushEngine.cs` | 0.07 | 0.27 | 19 | 3 | 15 | 10 |
| `src/Lightbox.Core/Documents/Frame.cs` | 0.07 | 0.27 | 3 | 0 | 88 | 62 |
| `src/Lightbox.App/Services/BrushPresets.cs` | 0.06 | 0.08 | 6 | 1 | 3 | 0 |
| `src/Lightbox.App/ViewModels/LayerRow.cs` | 0.06 | 0.08 | 5 | 1 | 4 | 1 |
| `src/Lightbox.Core/Geometry/GeometryOps.cs` | 0.05 | 0.07 | 2 | 1 | 8 | 1 |
| `src/Lightbox.Core/Documents/Stroke.cs` | 0.05 | 0.21 | 4 | 0 | 68 | 45 |
| `src/Lightbox.Raster/Media/MediumSimulator.cs` | 0.05 | 0.05 | 2 | 1 | 0 | 0 |
| `src/Lightbox.Raster/Media/PigmentModel.cs` | 0.05 | 0.07 | 3 | 1 | 4 | 1 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml` | 0.05 | 0.05 | 3 | 1 | 0 | 0 |
| `src/Lightbox.App/Services/SpriteSheetExporter.cs` | 0.05 | 0.21 | 2 | 0 | 67 | 41 |
| `src/Lightbox.App/ViewModels/MainViewModel.Symbols.cs` | 0.05 | 0.20 | 0 | 0 | 68 | 60 |
| `src/Lightbox.Core/Documents/Scene.cs` | 0.05 | 0.20 | 11 | 2 | 23 | 7 |
| `src/Lightbox.App/ViewModels/PaletteDockerViewModel.cs` | 0.05 | 0.06 | 6 | 0 | 6 | 1 |

## Most active regardless of coverage

- `src/Lightbox.App/ViewModels/MainViewModel.cs` — heat 0.76, 63 commits (8 fixes), 5 dependents
- `src/Lightbox.App/Views/MainWindow.axaml.cs` — heat 0.59, 53 commits (7 fixes), 7 dependents
- `src/Lightbox.App/Views/MainWindow.axaml` — heat 0.54, 56 commits (6 fixes), 0 dependents
- `src/Lightbox.App/Rendering/CanvasControl.cs` — heat 0.39, 28 commits (5 fixes), 6 dependents
- `src/Lightbox.Raster/BrushEngine.cs` — heat 0.27, 19 commits (3 fixes), 15 dependents
- `src/Lightbox.Core/Documents/Frame.cs` — heat 0.27, 3 commits (0 fixes), 88 dependents
- `src/Lightbox.Core/Documents/Stroke.cs` — heat 0.21, 4 commits (0 fixes), 68 dependents
- `src/Lightbox.App/Services/SpriteSheetExporter.cs` — heat 0.21, 2 commits (0 fixes), 67 dependents
- `src/Lightbox.App/ViewModels/MainViewModel.Symbols.cs` — heat 0.20, 0 commits (0 fixes), 68 dependents
- `src/Lightbox.Core/Documents/Scene.cs` — heat 0.20, 11 commits (2 fixes), 23 dependents

## Substantial files with no test reference

- `src/Lightbox.App/Views/MainWindow.axaml` (2580 ln)
- `src/Lightbox.Raster/Media/MediumSimulator.cs` (298 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (228 ln)
- `src/Lightbox.App/Styles/Density.axaml` (208 ln)
- `src/Lightbox.App/Styles/ColorPicker.axaml` (174 ln)
- `src/Lightbox.App/Controls/ColorField.cs` (171 ln)
- `src/Lightbox.Mcp/LightboxTools.cs` (130 ln)
- `src/Lightbox.App/App.axaml` (119 ln)
- `src/Lightbox.App/Controls/DockDropIndicator.cs` (101 ln)
- `src/Lightbox.App/Views/StartScreen.axaml` (84 ln)
