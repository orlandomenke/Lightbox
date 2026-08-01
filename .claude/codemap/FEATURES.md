# Behaviour inventory

429 tests, derived from the suite itself. Each line is a
promise the application currently keeps. Treat this as the regression
contract: if a change makes one of these statements false, it is a
regression even when every test still compiles.

## PromptTests
`tests/Lightbox.Ai.Tests/AiTests.cs`

- Inbetween User Contains Scene Bounds And All Ts — `:168`
- Draw User Contains Prompt And Context — `:183`
- System Prompts Mention Core Principles — `:193`

## SchemaTests
`tests/Lightbox.Ai.Tests/AiTests.cs`

- Schemas Are Valid Json — `:13`
- Schemas Every Object Forbids Additional Properties — `:23`
- Schemas Contain No Numeric Range Constraints — `:52`

## WireMappingTests
`tests/Lightbox.Ai.Tests/AiTests.cs`

- To Wire Resamples Long Strokes And Rounds Coords — `:67`
- To Wire Keeps Short Strokes Intact — `:86`
- From Wire Clamps Everything — `:93`
- From Wire Rejects Unusable Strokes — `:118`
- From Wire Maps Eraser And Label — `:127`
- From Wire List Drops Bad Keeps Good — `:147`

## WireRoundTripTests
`tests/Lightbox.Ai.Tests/AiTests.cs`

- Inbetween Result Dto Parses Model Shaped Json — `:205`

## OllamaTests
`tests/Lightbox.Ai.Tests/OllamaTests.cs`

- Request Body Carries Model Schema Prompts And Options — `:45`
- Success Response Parses And Validates — `:69`
- Connection Refused Maps To Retryable With Hint — `:87`
- Model Not Found Suggests Pull — `:100`
- Empty Or Unusable Frames Is Retryable Error — `:115`
- Draw Parses Strokes — `:127`

## AiIntegrationTests
`tests/Lightbox.App.Tests/AiIntegrationTests.cs`

- No Artist Disables Ai — `:54`
- Ai Inbetween Inserts Frames Through Shared Path — `:63`
- Ai Inbetween Refusal Surfaces Message No Mutation — `:94`
- Ai Inbetween Without Second Key Asks For One — `:110`
- Ai Draw Appends Strokes Undoable — `:120`
- Ai Draw Empty Prompt Does Nothing — `:143`

## BackgroundLayerTests
`tests/Lightbox.App.Tests/BackgroundLayerTests.cs`

- ANew Paper Document Gets ALocked Background Layer Below The Paint Layer — `:32`
- The Paper Colour Comes Out In The Composite — `:48`
- ATransparent Document Has No Background Layer And Stays Transparent — `:60`
- The Background Layer Refuses Edits Until Unlocked — `:71`
- Erasing The Unlocked Background Reveals Real Transparency — `:93`
- ADocument Saved Before Background Layers Existed Still Opens On Its Paper — `:114`
- The Paper Is AStroke Record Not Baked Pixels — `:130`

## BrushCursorTests
`tests/Lightbox.App.Tests/BrushCursorTests.cs`

- With AMouse The Ring Is The Full Brush Width — `:16`
- Hovering Shows The Maximum Even After ALight Stroke — `:25`
- The Ring Matches The Radius The Engine Will Stamp — `:39`
- Turning Tracking Off Pins The Ring To Full Size — `:60`
- When Pressure Is Disabled For The Brush The Ring Ignores It — `:74`

## BrushPresetTests
`tests/Lightbox.App.Tests/BrushToolTests.cs`

- Selecting APreset Applies Its Settings To The Stroke Record — `:47`
- Each Simulated Medium Reaches The Stroke Record With Its Own Physics — `:78`
- Brush And Eraser Keep Separate Configurations — `:117`
- Last Configured Brush Survives ANew Session — `:132`
- Save Current As Preset Persists User Presets — `:156`
- Imported Brush Becomes APreset And Its Tip Enters The Document — `:206`

## HiddenLayerTests
`tests/Lightbox.App.Tests/BrushToolTests.cs`

- Painting On AHidden Layer Is Blocked Until Visible Again — `:11`
- Ai Draw Refuses AHidden Layer — `:31`

## CanvasInputTests
`tests/Lightbox.App.Tests/CanvasInputTests.cs`

- Mouse Drag Paints AStroke — `:37`
- Mouse Drag After Wheel Zoom Still Paints — `:50`
- Mouse Drag After Middle Button Pan Still Paints — `:64`
- Mouse Drag After Mirror Rotate Zoom Still Paints At Correct Doc Point — `:81`

## CanvasViewTests
`tests/Lightbox.App.Tests/CanvasViewTests.cs`

- Default View Maps View Center To Doc Center At Fit Scale — `:22`
- Zoom Keeps Anchor Fixed And Scales Mapping — `:35`
- Mirror Flips Horizontally Around View Center — `:50`
- Rotate90 Swaps Axes In The Mapping — `:69`
- Reset View Restores The Default Mapping — `:81`
- View Transform Never Touches The Document — `:98`

## ColorPickerViewModelTests
`tests/Lightbox.App.Tests/ColorPickerTests.cs`

- Channel Edits Commit To Brush Color — `:64`
- External Hex Updates Every Representation — `:74`
- Hue Persists When Color Turns Black — `:92`
- Cmyk Edits Produce Expected Color — `:106`
- Invalid Hex Typed Is Ignored By Picker — `:118`
- Mode Flags Follow Selection — `:127`

## ColorSpaceTests
`tests/Lightbox.App.Tests/ColorPickerTests.cs`

- Known Color Red Converts To Every Model — `:10`
- Every Model Round Trips Through Hex — `:35`
- Invalid Hex Returns Null — `:53`

## LayerRowTests
`tests/Lightbox.App.Tests/DockerUiTests.cs`

- Rows Show Topmost Layer First And Track Cells — `:13`
- Rename Through Row Writes To Document And Is Undoable — `:29`
- Rename To Blank Snaps Back Without An Undo Step — `:44`
- Visibility Toggle Through Row Is Undoable — `:58`
- Select Frame On Another Layers Cell Selects That Layer And Frame — `:70`
- Add Layer Button Follows Kind Dropdown — `:85`

## PerLayerOnionTests
`tests/Lightbox.App.Tests/DockerUiTests.cs`

- Disabling Layer Onion Removes Its Ghosts From The Snapshot — `:108`

## PlaybackSpeedTests
`tests/Lightbox.App.Tests/DockerUiTests.cs`

- Speed Percent Clamps To Sane Range — `:133`
- Clock Interval Scales With Fps And Speed — `:143`

## SidebarTests
`tests/Lightbox.App.Tests/DockerUiTests.cs`

- Toggle Sidebar Flips Visibility — `:154`
- Switch Sidebar Side Flips Side — `:165`
- Toggle Timeline Flips Visibility — `:176`

## BackgroundColorTests
`tests/Lightbox.App.Tests/DocumentTabTests.cs`

- Scene Background Round Trips And Tints The Snapshot — `:135`
- Transparent Background Renders Transparent Pixels — `:153`

## ColorWheelFidelityTests
`tests/Lightbox.App.Tests/DocumentTabTests.cs`

- Wheel Value Is Not Rewritten While Dragging — `:169`
- Slider Channels Are Not Rewritten While Editing — `:188`

## DocumentTabTests
`tests/Lightbox.App.Tests/DocumentTabTests.cs`

- Starts With One Clean Untitled Tab — `:12`
- New Document Adds Tab With Settings And Activates It — `:23`
- Painting Marks The Tab Dirty Save Clears It — `:40`
- Switching Tabs Keeps Each Document And Its Undo History — `:58`
- Switching Tabs Does Not Mark Anything Dirty And Restores Playhead — `:84`
- Close Tab Activates Neighbor And Never Leaves Zero Tabs — `:100`
- Open Document Tab Uses File Name And Keeps Existing Tabs — `:119`

## ContextShortcutTests
`tests/Lightbox.App.Tests/FoldersContextsPickerTests.cs`

- Same Key Means Different Things Per Context — `:12`
- Global Bindings Fire In Every Context Unless Shadowed — `:23`
- Conflicts Only Count When Contexts Overlap — `:36`

## LayerFolderTests
`tests/Lightbox.App.Tests/FoldersContextsPickerTests.cs`

- Create Folder Groups The Active Layer And Shows AHeader Row — `:121`
- Folder Visibility Gates Its Members In Compositing And Painting — `:136`
- Collapse Hides Member Rows From The Docker Panel Only — `:155`
- Add And Remove Keep The Folder Contiguous — `:168`
- Folder Color Is Undoable And Serializes — `:185`
- Dissolve Ungroups Everything And Folders Serialize — `:203`

## NudgeSelectionTests
`tests/Lightbox.App.Tests/FoldersContextsPickerTests.cs`

- Nudge Shifts Every Contour Point By Whole Pixels — `:90`
- Nudge Without ASelection Is ANo Op — `:103`

## PickerToolTests
`tests/Lightbox.App.Tests/FoldersContextsPickerTests.cs`

- Pick Color At Reads The Composited Color And Paper When Empty — `:49`
- Insert Keyframe At Playhead Keys The Active Cel — `:74`

## IpcTests
`tests/Lightbox.App.Tests/IpcTests.cs`

- Get Scene Reports Layers And Keys — `:31`
- Get Frame Strokes Returns Wire Format — `:44`
- Render Frame Returns Decodable Png — `:56`
- Insert Inbetweens Validates And Inserts Undoable — `:68`
- Draw Strokes Appends To Exposed Key — `:110`
- Bad Requests Fail Cleanly — `:134`
- Pipe Round Trip Get Scene — `:147`

## LargeCanvasPerformanceTests _Category=Performance_
`tests/Lightbox.App.Tests/LargeCanvasPerformanceTests.cs`

- Four K Pointer Event During AStroke Repaints Only What Changed — `:69`
- Four K Whole Stroke Including Commit Has No Pen Lift Stall — `:89`
- Four K Undo After AStroke Stays Responsive — `:109`
- Four K Frame Cache Stays Within Its Memory Budget — `:129`
- Headroom Reports Smooth While Painting On Four K — `:149`

## AlphaSelectAndWandTests
`tests/Lightbox.App.Tests/LayerCompositingTests.cs`

- Select Layer Alpha Selects Only The Painted Pixels — `:152`
- Select Layer Alpha On An Empty Layer Is ANo Op With AMessage — `:166`
- Select Layer Alpha Subtract Carves Out Of An Existing Selection — `:175`
- Wand Selects The Clicked Color Region — `:191`
- Wand On Empty Canvas Selects The Connected Emptiness — `:203`
- Fill Inside AWand Selection Stays Inside And Records The Clip — `:215`

## BlendComposeTests
`tests/Lightbox.App.Tests/LayerCompositingTests.cs`

- Multiply Darkens And Screen Lightens — `:32`
- Opacity Still Applies Under ABlend Mode — `:44`
- To Skia Maps Normal To Src Over And Covers Every Mode — `:52`

## CelClipboardTests
`tests/Lightbox.App.Tests/LayerCompositingTests.cs`

- Copy Paste Deep Clones With Fresh Ids And Extends The Timeline — `:240`
- Cut Copies Then Clears So The Cel Becomes AHold — `:259`
- Paste Across Kinds Converts Strokes But Refuses Baseline Pixels Onto Vector — `:272`
- Exposure Editing From Cells Extends And Clears — `:293`

## LayerPanelTests
`tests/Lightbox.App.Tests/LayerCompositingTests.cs`

- Active Layer Opacity Writes Through As Percent — `:66`
- Active Layer Blend Mode Is Undoable — `:75`
- Move Layer Flips Compositing Order And Follows The Active Layer — `:86`
- Layer Thumb Renders The Exposed Drawing And Follows The Playhead — `:101`

## LayerDeletionTests
`tests/Lightbox.App.Tests/LayerDeletionTests.cs`

- Delete Layer Removes It And Keeps AValid Active Index — `:10`
- Deleting The Last Layer Regrows ABlank One — `:24`
- Clear Layer Blanks Every Drawing But Keeps The Timing — `:45`
- Docker Visibility Toggles Round Trip — `:70`

## LayerLockTests
`tests/Lightbox.App.Tests/LayerLockTests.cs`

- Painting Is Refused With AReason That Names The Layer — `:32`
- Fill Is Refused — `:47`
- Transform Is Refused — `:58`
- Deleting The Layer Is Refused — `:66`
- External Writers Are Refused — `:76`
- Visibility Opacity And Blend Mode Stay Available — `:91`
- ALocked Folder Locks Its Members And Says So — `:107`
- Locking Is Undoable — `:128`
- Alpha Lock Is Recorded On The Stroke Not Read Back From The Layer — `:140`

## LivePreviewPixelTests
`tests/Lightbox.App.Tests/LivePreviewPixelTests.cs`

- Mid Stroke The Published Snapshot Shows The Line — `:36`
- Self Crossing Looks The Same Live And Committed — `:59`

## LivePreviewTests
`tests/Lightbox.App.Tests/LivePreviewTests.cs`

- Batched Moves Produce One Stroke With All Points — `:10`
- Committed Pixels Match Direct Rasterization — `:30`
- Pointer Up Without Down Is Harmless — `:56`

## MainViewModelTests
`tests/Lightbox.App.Tests/MainViewModelTests.cs`

- Paint Stroke Lands In Document — `:14`
- Paint On Hold Frame Targets Exposed Key — `:29`
- Frame Commands Keep Document And Cells Consistent — `:47`
- Undo Redo Round Trips Paint — `:63`
- Insert Inbetweens Fills Timeline — `:81`
- Snapshot Published On Paint And Navigation — `:112`
- Replace Document Resets State — `:132`
- Toggle Playback Flips State — `:148`
- Paint While Playing Is Ignored — `:159`

## MainWindowTests
`tests/Lightbox.App.Tests/MainViewModelTests.cs`

- Main Window Constructs And Shows — `:173`

## ExportTests
`tests/Lightbox.App.Tests/Milestone3Tests.cs`

- Export Png Sequence Writes One File Per Frame Resolving Holds — `:124`

## FpsTests
`tests/Lightbox.App.Tests/Milestone3Tests.cs`

- Fps Clamps And Persists To Scene — `:159`

## LayerTests
`tests/Lightbox.App.Tests/Milestone3Tests.cs`

- Add Vector Layer Becomes Active Painting Creates Vector Strokes — `:12`
- Inbetweens On Vector Layer Produce Vector Frames — `:36`
- New Layer Is Padded To Frame Count And Undoable — `:57`

## SmoothingTests
`tests/Lightbox.App.Tests/Milestone3Tests.cs`

- Smoothing On Reduces Spikes Preserves Endpoints — `:74`
- Smoothing Off Keeps Raw Points — `:90`

## ThumbnailTests
`tests/Lightbox.App.Tests/Milestone3Tests.cs`

- Keyed Cells Get Thumbnails Holds Do Not — `:106`

## DropColorFillTests
`tests/Lightbox.App.Tests/ModifiersAndDropFillTests.cs`

- Dropping AColour Fills And Adopts It — `:77`
- It Works Whichever Tool Is Selected — `:94`
- ALocked Layer Still Refuses It — `:107`

## TemporaryToolModifierTests
`tests/Lightbox.App.Tests/ModifiersAndDropFillTests.cs`

- Alt Held Erases With The Current Brush Without Switching Tools — `:17`
- Without Alt The Same Call Paints — `:32`
- The Eraser Tool Still Erases Even Without Alt — `:42`

## ReferenceAiTests
`tests/Lightbox.App.Tests/ReferenceSheetTests.cs`

- Render Reference View Produces Decodable Png — `:131`
- Ai Inbetween Carries Reference Images — `:154`
- Ipc List And Render Expose Reference Views — `:183`

## ReferenceSheetModelTests
`tests/Lightbox.App.Tests/ReferenceSheetTests.cs`

- Sheets Round Trip Through Json And Legacy Docs Load Empty — `:12`

## ReferenceTabTests
`tests/Lightbox.App.Tests/ReferenceSheetTests.cs`

- Add View Opens Reference Tab Timeline Hidden — `:53`
- Painting In Reference Tab Lands In Owning Document And Dirties Owner — `:69`
- Save From Reference Tab Serializes The Owning Document — `:92`
- Closing Owner Tab Closes Its Reference Tabs — `:108`
- Opening Same View Focuses Existing Tab — `:118`

## ShortcutMapTests
`tests/Lightbox.App.Tests/ShortcutMapTests.cs`

- Defaults Cover The Core Commands Without Duplicates — `:12`
- Conflict Detection Finds The Other Command — `:29`
- Assign With Unbind Steals The Gesture — `:41`
- Overrides Persist And Reload — `:51`
- Corrupt Store Falls Back To Defaults — `:69`

## DeltaCommitTests
`tests/Lightbox.App.Tests/SmoothingAndCommitTests.cs`

- Stroke Commit Undo Redo Works Without Snapshots — `:133`
- Delta And Snapshot Steps Interleave Cleanly — `:149`
- Stroke Under Selection Undo Removes The Deduped Clip Too — `:178`
- Global Anti Aliasing Stamps Into Strokes And Fills — `:194`

## SmoothingVmTests
`tests/Lightbox.App.Tests/SmoothingAndCommitTests.cs`

- Smooth Strokes Compat Maps To The Mode — `:86`
- Pulled String Records The Smoothed Path Not The Cursor — `:99`
- Lazy Gizmo Radius Only Shows For Pulled String Paint Tools — `:116`

## StrokeFilterTests
`tests/Lightbox.App.Tests/SmoothingAndCommitTests.cs`

- Moving Average Filters Tremor And Anchors Endpoints — `:32`
- Savitzky Golay Smooths But Keeps APeak Better Than Averaging — `:43`
- Pulled String Has ADead Zone Then Trails The Cursor — `:58`
- Ema Lags Behind And Converges — `:72`

## FrameInsertionTests
`tests/Lightbox.App.Tests/TimelineExpansionTests.cs`

- Insert On Virtual Cell Extends Timeline Padding All Layers — `:116`
- Insert Roles Color The Cells And Undo Removes Frame — `:134`
- Insert On Existing Frame Just Remarks Its Role — `:152`
- Generated Inbetweens Are Marked As Inbetweens — `:167`

## FrameRoleSerializationTests
`tests/Lightbox.App.Tests/TimelineExpansionTests.cs`

- Role Survives Save And Load Defaults To Key For Legacy Docs — `:191`

## PlaybackTransportTests
`tests/Lightbox.App.Tests/TimelineExpansionTests.cs`

- Step Playback Loops Forward Inside Selected Range — `:18`
- Step Playback Backwards Wraps To Range End — `:34`
- Go To Start And End Use Range When Set Else Timeline Bounds — `:50`
- Keyframe Navigation Skips Holds — `:73`
- Range Selection Greys Out Cells Outside It — `:95`

## CelRangeSelectionTests
`tests/Lightbox.App.Tests/TimelineRangeAndPressureTests.cs`

- Shift Click Selects ARange And Plain Click Clears It — `:20`
- Copy Range Preserves Holds And Paste Replays Them — `:38`
- Cut Range Clears Every Cel In The Range In One Undo Step — `:61`
- Move Cel Refuses Cross Layer Drops — `:81`
- Markers Add Edit Remove Are Undoable And Feed The Ruler — `:96`

## PressureVmTests
`tests/Lightbox.App.Tests/TimelineRangeAndPressureTests.cs`

- Master Switch Writes Into The Stroke Record — `:120`
- Per Setting Checkboxes Map To The Response Curves — `:134`

## FillToolTests
`tests/Lightbox.App.Tests/ToolSelectionFillTests.cs`

- Fill At Records AFill Stroke Below The Line Work — `:79`
- Fill At Above Line Work Appends On Top — `:96`
- Fill At Under ASelection Stays Inside It And Records The Clip — `:111`
- Fill At Is Undoable — `:133`

## SelectionTests
`tests/Lightbox.App.Tests/ToolSelectionFillTests.cs`

- Painting Under ASelection Tags The Stroke And Dedupes The Region — `:150`
- Selection Combine Ops Add Subtract Replace — `:170`
- Select All Invert Deselect — `:191`
- Grow And Shrink Move The Outline — `:210`
- Polygon Selection Builds From Vertices And Esc Cancels — `:227`
- Paint Outside The Selection Leaves No Visible Pixels — `:247`

## ToolModelTests
`tests/Lightbox.App.Tests/ToolSelectionFillTests.cs`

- Select Shortcut Activates Then Cycles Variants — `:10`
- Is Eraser Compat Tracks The Active Tool — `:39`
- Non Paint Tools Do Not Produce Brush Strokes — `:54`

## TransformToolTests
`tests/Lightbox.App.Tests/TransformToolTests.cs`

- Begin Transform Reports The Stroke Bounds And Commit Moves The Points — `:34`
- Mirror Commit Flips Around The Pivot Without Moving — `:60`
- Perspective Commit Maps The Corners Exactly — `:74`
- Degenerate Perspective Is Refused And The Session Survives — `:94`
- Cel Range Scope Transforms Each Distinct Drawing Once — `:107`
- Entire Animation Scope Moves Every Layer — `:127`
- Selection Region Limits The Transform To Strokes Inside It — `:146`
- Empty Scope Refuses To Start — `:166`

## ColorTests
`tests/Lightbox.Core.Tests/Geometry/GeometryTests.cs`

- Hex To Rgb Parses — `:99`
- Hex To Rgb Invalid Throws — `:106`
- Lerp Color Endpoints — `:110`

## GeometryTests
`tests/Lightbox.Core.Tests/Geometry/GeometryTests.cs`

- Resample Produces Exact Count — `:9`
- Resample Preserves Endpoints — `:17`
- Resample Spacing Is Even By Arc Length — `:27`
- Resample Degenerates — `:39`
- Resample Is Deterministic — `:54`
- Path Length Sums Segments — `:65`
- Centroid Averages Positions — `:72`
- Smooth Preserves Endpoints And Count — `:80`

## EraserResurrectionTests
`tests/Lightbox.Core.Tests/Inbetween/CleanerTests.cs`

- Erased Stroke Does Not Appear In Inbetweens — `:71`
- Tween Output Preserves Paint Order — `:92`

## StrokeRecordCleanerTests
`tests/Lightbox.Core.Tests/Inbetween/CleanerTests.cs`

- Fully Erased Stroke Is Dropped Erasers Too — `:16`
- Partially Erased Stroke Is Kept — `:26`
- Eraser Before Stroke Does Not Affect It — `:37`
- Untouched Strokes Survive In Order — `:48`

## InbetweenerTests
`tests/Lightbox.Core.Tests/Inbetween/InbetweenTests.cs`

- Matched Strokes Interpolate — `:141`
- Unmatched A Fades Out In First Half — `:153`
- Unmatched B Fades In Second Half — `:167`
- Easing Shifts Parameter — `:180`
- Inbetween Series Produces Evenly Spaced Ts — `:191`
- Easing Functions Pin Endpoints — `:207`

## InterpolateTests
`tests/Lightbox.Core.Tests/Inbetween/InbetweenTests.cs`

- T0 Matches A T1 Matches B — `:80`
- Midpoint Is Halfway — `:93`
- Reversed Stroke B Gets Flipped — `:105`
- Interpolation Is Structurally Deterministic — `:119`

## MatchTests
`tests/Lightbox.Core.Tests/Inbetween/InbetweenTests.cs`

- Label Match Beats Proximity — `:16`
- Greedy Matching Picks Nearest Centroids — `:29`
- Length Mismatch Inflates Cost — `:42`
- Unmatched Pair With Null — `:52`
- Empty Frames Produce Only One Sided Pairs — `:60`

## MediumSettingsTests
`tests/Lightbox.Core.Tests/Serialization/MediumSettingsTests.cs`

- Every Medium Parameter Survives ARound Trip — `:29`
- ADocument Saved Before Media Existed Loads As No Medium — `:68`
- Clone Deep Copies The Medium So Tweaking APreset Cannot Edit Past Strokes — `:123`

## RoundTripTests
`tests/Lightbox.Core.Tests/Serialization/RoundTripTests.cs`

- Round Trip Preserves Everything — `:42`
- Serialize Uses Camel Case And Kind Discriminator — `:72`
- Deserialize Accepts Kind Anywhere In Object — `:92`
- Onion Enabled Round Trips And Defaults True For Older Docs — `:113`
- Deserialize Unknown Kind Throws — `:139`
- Clone Is Deep And Independent — `:146`
- Save And Load File Round Trips — `:155`

## CelRangeTests
`tests/Lightbox.Core.Tests/Timeline/CelRangeTests.cs`

- Move Cel Moves The Drawing And Clears The Source — `:16`
- Move Cel With Copy Keeps The Source And Clones With AFresh Id — `:30`
- Move Cel From AHold Is ANo Op — `:45`
- Clear Cels Clears Every Drawing In The Range — `:57`
- Set Frame Range Writes The Sequence Holds Included — `:74`
- Frame Markers Survive Serialization — `:91`

## DocumentEditorTests
`tests/Lightbox.Core.Tests/Timeline/DocumentEditorTests.cs`

- Add Frame Grows All Layers And Frame Count — `:11`
- Duplicate Frame Copies Exposed Content — `:26`
- Delete Frame Refuses Last Frame — `:43`
- Delete Frame Removes Cel On Every Layer — `:51`
- Undo Redo Restores State — `:62`
- Perform Clears Redo Stack — `:80`
- Undo Stack Trims Oldest Beyond Limit Keeps Newest History — `:91`
- Insert Inbetweens Replaces Hold Cels Between Keys — `:121`
- Insert Inbetweens No Gap Inserts New Cels — `:144`

## ExposureEditingTests
`tests/Lightbox.Core.Tests/Timeline/ExposureEditingTests.cs`

- Extend Exposure Inserts AHold On That Layer Only — `:22`
- Reduce Exposure Removes Only Holds — `:41`
- Clear Cel Makes AHold That Shows The Previous Drawing — `:56`
- Set Frame At Replaces The Cel And Extends The Timeline — `:73`
- Move Layer Reorders The Stack And Clamps At The Edges — `:87`
- Clone Frame Deep Clones With AFresh Id — `:100`
- Layer Blend Mode Survives Serialization — `:115`

## ExposureTests
`tests/Lightbox.Core.Tests/Timeline/ExposureTests.cs`

- Exposed Frame Walks Holds Backward — `:17`
- Exposed Frame Past End Uses Last Key — `:28`
- Exposed Frame Nothing Keyed Returns Null — `:35`
- Frame At Exact Index Ignores Holds — `:42`
- Next Key Index Finds Strictly After — `:53`
- Key Index At Or Before Works — `:62`

## AlphaLockTests
`tests/Lightbox.Raster.Tests/AlphaLockTests.cs`

- Paint Only Lands Where The Layer Already Had Content — `:51`
- The Silhouette Is Unchanged — `:63`
- Without The Lock The Same Stroke Spills Outside — `:77`
- The Flag Survives AClone And ARound Trip — `:85`
- Re Rendering The Whole Frame Reproduces The Mask Without Storing It — `:98`

## BrushEngineV2Tests
`tests/Lightbox.Raster.Tests/BrushEngineV2Tests.cs`

- Effect Brushes Are Deterministic Across Rerenders — `:21`
- Flow Builds Up Within AStroke But Opacity Caps It — `:37`
- Wet Edge Darkens The Rim Relative To The Center — `:57`
- Granulation Carves Texture Into The Stroke — `:69`
- Smudge Brush Drags Existing Color And Replays Deterministically — `:84`
- Blur Brush Softens AHard Edge — `:113`
- Custom Tip Stamps Its Shape — `:137`

## BrushImportTests
`tests/Lightbox.Raster.Tests/BrushImportTests.cs`

- Gbr Imports Name Spacing And Tip Alpha — `:29`
- Gih Imports Multiple Tips — `:43`
- Abr V2 Imports Sampled Brush — `:62`
- Abr V6 Imports Raw Sampled Brush — `:94`
- Kpp Imports Parameter Subset — `:124`
- Unsupported Extension Throws — `:152`

## AntiAliasTests
`tests/Lightbox.Raster.Tests/DraftAndAaTests.cs`

- Anti Alias Off Produces Hard Pixel Edges — `:79`
- Fill Stroke Honors Anti Alias — `:89`
- Anti Alias Is Per Stroke So Old Art Never Changes — `:105`

## DraftPreviewTests
`tests/Lightbox.Raster.Tests/DraftAndAaTests.cs`

- Draft Paints The Segment And Nothing Far From It — `:17`
- Draft Eraser Still Erases — `:31`
- Exact Render Is Unaffected By The Draft Refactor — `:45`

## FluidLineTests
`tests/Lightbox.Raster.Tests/DraftAndAaTests.cs`

- Light Pressure Strokes Have No Gaps Along The Centerline — `:181`
- Pressure Ramp Keeps The Stroke Connected — `:211`

## ScratchPreviewTests
`tests/Lightbox.Raster.Tests/DraftAndAaTests.cs`

- Scratch Preview Matches Exact Render Where The Stroke Crosses Itself — `:122`

## FillStrokeTests
`tests/Lightbox.Raster.Tests/FloodFillTests.cs`

- Fill Stroke Renders Its Region With Holes Left Empty — `:157`
- Fill Stroke Survives Document Serialization Pixel For Pixel — `:169`
- Clipped Stroke Re Renders Identically From Json Alone — `:187`
- Feather Softens The Clip Edge — `:223`

## FloodFillTests
`tests/Lightbox.Raster.Tests/FloodFillTests.cs`

- Fill Stops At Barriers Within Tolerance And Crosses Them Beyond It — `:33`
- Fill Traces Inner Contours As Holes — `:52`
- Gap Closing Seals Small Openings But Not Larger Ones — `:72`
- Grow And Shrink Overfill And Underfill The Region — `:99`
- Fill Is Deterministic — `:113`
- Fill Stays Inside ASelection Mask — `:124`

## FluidLatticeTests _Category=Performance_
`tests/Lightbox.Raster.Tests/FluidLatticeTests.cs`

- Pigment Is Conserved Across Every Channel — `:38`
- Conservation Holds At Every Parameter Corner — `:66`
- Deposit Never Exceeds Pigment That Was Seeded — `:86`
- Run Zero Changes Nothing At All — `:113`
- Two Runs Are Bit Identical — `:143`
- Inviscid Undragged Deluge Stays Finite — `:186`
- Extreme Parameters Do Not Produce Na N — `:221`
- Edge Pull Concentrates Deposit Near The Wet Boundary — `:244`
- Granularity Biases Deposit Into The Papers Valleys — `:291`
- Paper Influence Zero Makes The Paper Irrelevant — `:335`
- Water Spreads Beyond Where It Was Seeded — `:359`
- Thin Wash Pins Instead Of Creeping Forever — `:375`
- Mis Sized Buffers Are Rejected — `:410`
- Four Hundred Square Twelve Steps Stays Within Budget — `:431`

## MediumRenderingTests
`tests/Lightbox.Raster.Tests/MediumRenderingTests.cs`

- The Four Media Do Not Render Identically — `:89`
- APlain Brush Is Untouched By Any Of This — `:135`
- Watercolour Light Pressure Is Paler And Spreads Further — `:148`
- Oil ASecond Stroke Disturbs The First — `:165`
- Every Medium Re Renders Identically — `:192`

## PaperFieldScaleTests
`tests/Lightbox.Raster.Tests/PaperFieldTests.cs`

- Scale Actually Changes The Grain Across The Usable Range — `:337`
- Below Nyquist The Field Saturates Rather Than Aliasing — `:355`

## PaperFieldTests _Category=Performance_
`tests/Lightbox.Raster.Tests/PaperFieldTests.cs`

- Rebuilt Tile Is Bit Identical — `:75`
- Fill Agrees With Height At Exactly — `:100`
- Tile Wraps Without ASeam — `:124`
- Height Stays In Range And Centred — `:168`
- Tooth Depth Separates The Three Papers — `:179`
- Rough Has The Longer Wavelength — `:194`
- Canvas Is Directional And Cold Press Is Not — `:207`
- Scale Sets The Wavelength — `:224`
- Different Scales Are Different Fields — `:242`
- Fill Rejects AToo Small Destination — `:252`
- Fill Is Fast Enough For AFull Frame — `:260`
- Fill Cost Follows The Region Not The Canvas — `:286`

## PerformanceTests _Category=Performance_
`tests/Lightbox.Raster.Tests/PerformanceTests.cs`

- Live Preview Effect Brush Segment Is Bounded To The Segment — `:54`
- Live Preview Plain Brush Segment Stays Cheap — `:72`
- Stroke Commit Exact Append Is Independent Of Frame Complexity — `:82`
- Live Preview Large Brush Segment Stays Interactive — `:95`
- Flood Fill Full Canvas Region Meets Budget — `:108`
- Flood Fill Inside Region With Hole Meets Budget — `:124`

## PigmentModelTests _Category=Performance_
`tests/Lightbox.Raster.Tests/PigmentModelTests.cs`

- Over Zero Thickness Returns Backdrop Bit For Bit — `:34`
- Over Zero Thickness Changes Nothing At All — `:51`
- Over Laying Paint Down Adds Opacity — `:59`
- Over Fully Hiding Converges To Mass Tone Whatever Is Underneath — `:92`
- Over Fully Hiding Is Independent Of Backdrop To The Bit — `:108`
- Over No Scattering Is Beer Lambert — `:116`
- Over No Absorption Is Pure Scattering — `:140`
- Over No Pigment At All Leaves The Backdrop Alone — `:160`
- Over Denormal Thickness Does Not Produce Garbage — `:169`
- Over Extreme Inputs Stay In Gamut — `:182`
- Yellow Glaze Over Blue Is Greener Than Every Possible Alpha Blend — `:212`
- Yellow Glaze Over Blue Is More Saturated Than Alpha Blending — `:256`
- Yellow Glaze Over Blue Darkens Rather Than Averaging — `:276`
- Yellow Glaze Over Pure Blue Goes Black And Says So Honestly — `:286`
- Over Thicker Glaze Moves Monotonically Away From The Backdrop — `:306`
- Coverage Rises With Hiding And With Thickness — `:320`
- From Color Hiding Dial Closes On The Chosen Colour Monotonically — `:338`
- From Color White With No Hiding Is Invisible — `:360`
- Mix At The Ends Reproduces The Inputs Exactly — `:371`
- Mix Is Continuous — `:386`
- Mix Is Clamped And Symmetric In Its Endpoints — `:429`
- Mix Yellow And Blue Makes Green — `:439`
- Over Is Deterministic — `:459`
- From Coefficients And From Color Agree When They Describe The Same Film — `:482`
- Srgb Conversion Round Trips Every Single Level — `:492`
- Srgb Conversion Is The Real Transfer Function Not AGamma Guess — `:499`
- Srgb Conversion Is Monotonic — `:514`
- Over Works In Linear Light Not On Encoded Values — `:534`
- Over Costs Under AMicrosecond Per Pixel — `:548`

## PressureTests
`tests/Lightbox.Raster.Tests/PressureTests.cs`

- Master Switch Off Ignores Pen Pressure Entirely — `:20`
- Pressure Hardness Softens The Edge At Light Pressure — `:42`
- Pressure Settings Survive Clone And Serialization — `:60`

## BrushEngineTests
`tests/Lightbox.Raster.Tests/RasterTests.cs`

- Rasterize Same Strokes Identical Pixels — `:26`
- Rasterize Marks Pixels Along The Path — `:39`
- Pressure Controls Dab Radius — `:51`
- Stroke Opacity Does Not Stack Within AStroke — `:66`
- Eraser Removes Painted Pixels — `:81`
- Soft Brush Falls Off Toward The Rim — `:92`
- Dab Positions Spacing Follows Arc Length — `:108`
- Dab Positions Spacing Shrinks With Pressure — `:122`
- Append Matches Batch Rasterization — `:141`

## PngCodecTests
`tests/Lightbox.Raster.Tests/RasterTests.cs`

- Encode Decode Round Trips Pixels — `:158`
- Materialize Composites Baseline Plus Strokes — `:172`

## SmudgeFirstDabTests
`tests/Lightbox.Raster.Tests/TexturedBrushTests.cs`

- ASingle Tap On ABoundary Softens It Rather Than Doing Nothing — `:162`
- ATap On Flat Colour Changes Nothing — `:182`
- Smudge Never Deposits The Brush Colour — `:196`

## TexturedBrushTests _Category=Performance_
`tests/Lightbox.Raster.Tests/TexturedBrushTests.cs`

- Wet Edge Darkens The Outline Not The Interior — `:47`
- Granulation Is Deterministic And Anchored To The Document — `:81`
- Textured Stroke Commit Does Not Stall The Pen — `:100`
