# Hotspots

Where change has been concentrated and where it is risky. **Heat** combines
commit churn, fix-commit churn, how many files depend on this one, and size.
**Risk** is heat discounted by how many test files exercise the file — a hot
file with no tests is the top of this list for a reason.

## Riskiest to change

| File | Risk | Heat | Commits | Fixes | Dependents | Test files |
| --- | --- | --- | --- | --- | --- | --- |
| `src/Lightbox.App/Views/MainWindow.axaml.cs` | 0.64 | 0.64 | 22 | 7 | 1 | 0 |
| `src/Lightbox.App/Views/MainWindow.axaml` | 0.54 | 0.54 | 21 | 5 | 0 | 0 |
| `src/Lightbox.App/Rendering/CanvasControl.cs` | 0.22 | 0.45 | 15 | 4 | 3 | 2 |
| `src/Lightbox.App/ViewModels/MainViewModel.cs` | 0.20 | 0.81 | 23 | 5 | 26 | 22 |
| `src/Lightbox.App/Rendering/SceneRenderer.cs` | 0.14 | 0.19 | 6 | 2 | 3 | 1 |
| `src/Lightbox.App/App.axaml` | 0.10 | 0.10 | 4 | 1 | 0 | 0 |
| `src/Lightbox.App/ViewModels/LayerRow.cs` | 0.09 | 0.12 | 4 | 1 | 4 | 1 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` | 0.09 | 0.09 | 2 | 1 | 2 | 0 |
| `src/Lightbox.Core/Geometry/GeometryOps.cs` | 0.09 | 0.12 | 2 | 1 | 8 | 1 |
| `src/Lightbox.App/Services/IpcDocumentApi.cs` | 0.08 | 0.10 | 3 | 1 | 2 | 1 |
| `src/Lightbox.App/Views/ConfigureWindow.axaml` | 0.07 | 0.07 | 2 | 1 | 0 | 0 |
| `src/Lightbox.Core/Documents/Scene.cs` | 0.07 | 0.15 | 4 | 1 | 9 | 2 |
| `src/Lightbox.Core/Documents/Frame.cs` | 0.07 | 0.28 | 2 | 0 | 45 | 27 |
| `src/Lightbox.App/Services/SequenceExporter.cs` | 0.07 | 0.09 | 3 | 1 | 1 | 1 |
| `src/Lightbox.App/ViewModels/ColorPickerViewModel.cs` | 0.07 | 0.09 | 2 | 1 | 2 | 1 |
| `src/Lightbox.App/Services/BrushPresets.cs` | 0.06 | 0.09 | 2 | 1 | 1 | 0 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml.cs` | 0.06 | 0.08 | 1 | 1 | 4 | 1 |
| `src/Lightbox.Import/ImportedBrush.cs` | 0.06 | 0.08 | 1 | 1 | 4 | 1 |
| `src/Lightbox.App/Views/NewDocumentDialog.axaml` | 0.06 | 0.06 | 1 | 1 | 0 | 0 |
| `src/Lightbox.Core/Inbetween/StrokeRecordCleaner.cs` | 0.06 | 0.08 | 1 | 1 | 3 | 1 |

## Most active regardless of coverage

- `src/Lightbox.App/ViewModels/MainViewModel.cs` — heat 0.81, 23 commits (5 fixes), 26 dependents
- `src/Lightbox.App/Views/MainWindow.axaml.cs` — heat 0.64, 22 commits (7 fixes), 1 dependents
- `src/Lightbox.App/Views/MainWindow.axaml` — heat 0.54, 21 commits (5 fixes), 0 dependents
- `src/Lightbox.App/Rendering/CanvasControl.cs` — heat 0.45, 15 commits (4 fixes), 3 dependents
- `src/Lightbox.Core/Documents/Frame.cs` — heat 0.28, 2 commits (0 fixes), 45 dependents
- `src/Lightbox.Raster/BrushEngine.cs` — heat 0.23, 9 commits (1 fixes), 6 dependents
- `src/Lightbox.Core/Documents/BrushSettings.cs` — heat 0.22, 5 commits (1 fixes), 20 dependents
- `src/Lightbox.Core/Documents/Stroke.cs` — heat 0.22, 2 commits (0 fixes), 35 dependents
- `src/Lightbox.App/Rendering/SceneRenderer.cs` — heat 0.19, 6 commits (2 fixes), 3 dependents
- `src/Lightbox.Core/Timeline/DocumentEditor.cs` — heat 0.18, 7 commits (1 fixes), 5 dependents

## Substantial files with no test reference

- `src/Lightbox.App/Views/MainWindow.axaml` (1164 ln)
- `src/Lightbox.App/Views/MainWindow.axaml.cs` (1071 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml.cs` (211 ln)
- `src/Lightbox.App/Controls/TimelineRuler.cs` (181 ln)
- `src/Lightbox.Mcp/LightboxTools.cs` (130 ln)
- `src/Lightbox.App/Views/ConfigureWindow.axaml` (115 ln)
