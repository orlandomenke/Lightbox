# Hotspots

Where change has been concentrated and where it is risky. **Heat** combines
commit churn, fix-commit churn, how many files depend on this one, and size.
**Risk** is heat discounted by how many test files exercise the file — a hot
file with no tests is the top of this list for a reason.

## Riskiest to change

| File | Risk | Heat | Commits | Fixes | Dependents | Test files |
| --- | --- | --- | --- | --- | --- | --- |
| `src/Lightbox.App/Views/MainWindow.axaml` | 0.49 | 0.49 | 60 | 6 | 0 | 0 |
| `src/Lightbox.App/ViewModels/MainViewModel.cs` | 0.38 | 0.76 | 69 | 10 | 5 | 2 |
| `src/Lightbox.App/Views/MainWindow.axaml.cs` | 0.13 | 0.54 | 57 | 7 | 7 | 6 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml` | 0.12 | 0.12 | 6 | 3 | 0 | 0 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` | 0.10 | 0.13 | 6 | 3 | 2 | 0 |
| `src/Lightbox.App/Rendering/CanvasControl.cs` | 0.08 | 0.34 | 28 | 5 | 6 | 5 |
| `src/Lightbox.Raster/BrushEngine.cs` | 0.07 | 0.29 | 23 | 4 | 17 | 12 |
| `src/Lightbox.App/App.axaml` | 0.07 | 0.07 | 9 | 1 | 0 | 0 |
| `src/Lightbox.Core/Documents/Frame.cs` | 0.07 | 0.26 | 3 | 0 | 94 | 66 |
| `src/Lightbox.Core/Documents/Stroke.cs` | 0.06 | 0.23 | 6 | 0 | 75 | 52 |
| `src/Lightbox.App/ViewModels/MainViewModel.Symbols.cs` | 0.06 | 0.23 | 4 | 0 | 73 | 65 |
| `src/Lightbox.App/Services/SpriteSheetExporter.cs` | 0.05 | 0.21 | 2 | 0 | 74 | 48 |
| `src/Lightbox.App/Services/BrushPresets.cs` | 0.05 | 0.07 | 6 | 1 | 3 | 0 |
| `src/Lightbox.App/ViewModels/LayerRow.cs` | 0.05 | 0.07 | 5 | 1 | 4 | 1 |
| `src/Lightbox.Core/Documents/BrushSettings.cs` | 0.05 | 0.20 | 8 | 1 | 47 | 37 |
| `src/Lightbox.Raster/Media/PigmentModel.cs` | 0.05 | 0.06 | 3 | 1 | 4 | 1 |
| `src/Lightbox.Raster/Media/MediumSimulator.cs` | 0.04 | 0.04 | 2 | 1 | 0 | 0 |
| `src/Lightbox.App/ViewModels/PaletteDockerViewModel.cs` | 0.04 | 0.06 | 6 | 0 | 6 | 1 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml` | 0.04 | 0.04 | 3 | 1 | 0 | 0 |
| `src/Lightbox.Core/Documents/Scene.cs` | 0.04 | 0.17 | 11 | 2 | 23 | 7 |

## Most active regardless of coverage

- `src/Lightbox.App/ViewModels/MainViewModel.cs` — heat 0.76, 69 commits (10 fixes), 5 dependents
- `src/Lightbox.App/Views/MainWindow.axaml.cs` — heat 0.54, 57 commits (7 fixes), 7 dependents
- `src/Lightbox.App/Views/MainWindow.axaml` — heat 0.49, 60 commits (6 fixes), 0 dependents
- `src/Lightbox.App/Rendering/CanvasControl.cs` — heat 0.34, 28 commits (5 fixes), 6 dependents
- `src/Lightbox.Raster/BrushEngine.cs` — heat 0.29, 23 commits (4 fixes), 17 dependents
- `src/Lightbox.Core/Documents/Frame.cs` — heat 0.26, 3 commits (0 fixes), 94 dependents
- `src/Lightbox.Core/Documents/Stroke.cs` — heat 0.23, 6 commits (0 fixes), 75 dependents
- `src/Lightbox.App/ViewModels/MainViewModel.Symbols.cs` — heat 0.23, 4 commits (0 fixes), 73 dependents
- `src/Lightbox.App/Services/SpriteSheetExporter.cs` — heat 0.21, 2 commits (0 fixes), 74 dependents
- `src/Lightbox.Core/Documents/BrushSettings.cs` — heat 0.20, 8 commits (1 fixes), 47 dependents

## Substantial files with no test reference

- `src/Lightbox.App/Views/MainWindow.axaml` (2681 ln)
- `src/Lightbox.Raster/Media/MediumSimulator.cs` (298 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (248 ln)
- `src/Lightbox.App/Styles/Density.axaml` (208 ln)
- `src/Lightbox.App/Styles/ColorPicker.axaml` (174 ln)
- `src/Lightbox.App/Controls/ColorField.cs` (171 ln)
- `src/Lightbox.Mcp/LightboxTools.cs` (130 ln)
- `src/Lightbox.App/App.axaml` (119 ln)
- `src/Lightbox.App/Controls/DockDropIndicator.cs` (101 ln)
- `src/Lightbox.App/Views/StartScreen.axaml` (84 ln)
