# Behaviour inventory

2853 tests, derived from the suite itself. Each line is a
promise the application currently keeps. Treat this as the regression
contract: if a change makes one of these statements false, it is a
regression even when every test still compiles.

## AiConnectionTesterTests
`tests/Lightbox.Ai.Tests/AiConnectionTesterTests.cs`

- AQuick Test Passes On One Good Line — `:68`
- AQuick Test Makes One Call — `:78`
- AThorough Test Makes Two — `:91`
- AStroke With One Point Is Unusable Rather Than APass — `:102`
- AStroke With No Extent Is Unusable — `:113`
- Points Off The Canvas Are Clamped Before The Tester Sees Them — `:125`
- An Empty Reply Fails — `:142`
- AThorough Test Passes When The Inbetween Lands Between The Keys — `:153`
- AThorough Test Fails When The Model Copied AKey Instead — `:163`
- ATiming Outside The Keys Leaves No Usable Frame — `:176`
- The Checks Name The Problem Rather Than Just Failing — `:185`
- Each Stage Is Reported So ALong Test Can Say What It Is Doing — `:196`

## AiPayloadBudgetTests _Category=Performance_
`tests/Lightbox.Ai.Tests/AiPayloadBudgetTests.cs`

- An Inbetween Request Stays Within Its Budget — `:68`
- Resampling Is What Keeps ALong Stroke Affordable — `:81`
- The Fixed Overhead Is Not Worth Optimising — `:99`
- Cost Scales With Stroke Count Which Is Why Sending Fewer Is The Real Lever — `:115`

## AiArtistFactoryTests
`tests/Lightbox.Ai.Tests/AiProviderTests.cs`

- An Incomplete Connection Produces No Artist — `:215`
- The Open Ai Dialect Providers All Build The Same Artist — `:224`
- Ollama Needs No Key — `:235`
- Anthropic Builds From AKey Alone — `:242`
- An Mcp Command That Cannot Start Is Not ACrash — `:250`
- Testing An Incomplete Connection Says What Is Missing Without ACall — `:261`
- Turning Assistance Off Produces No Artist However Complete The Connection Is — `:273`
- The Switch Is On By Default And Survives ARound Trip — `:287`

## AiProviderTests
`tests/Lightbox.Ai.Tests/AiProviderTests.cs`

- Every Provider Has AUnique Id And At Least One Field — `:51`
- An Unknown Provider Id Falls Back Rather Than Throwing — `:62`
- AStored Value Beats The Environment Which Beats The Default — `:69`
- Missing Names The Required Fields That Resolve To Nothing — `:98`
- Anthropic Is Complete From The Environment Alone — `:115`
- Settings Round Trip — `:133`
- ABroken Settings File Means Not Configured Rather Than ACrash — `:148`
- AProvider That No Longer Exists Loads As The Default — `:158`
- Only What Was Typed Is Written Back — `:167`
- The Password Fields Are The Ones Marked Secret — `:183`
- Every Api Provider Names AModel And Every Mcp Provider Names ATool — `:199`

## PromptTests
`tests/Lightbox.Ai.Tests/AiTests.cs`

- Inbetween User Contains Scene Bounds And All Ts — `:159`
- System Prompts Mention Core Principles — `:174`

## SchemaTests
`tests/Lightbox.Ai.Tests/AiTests.cs`

- Schemas Are Valid Json — `:11`
- Schemas Every Object Forbids Additional Properties — `:18`
- Schemas Contain No Numeric Range Constraints — `:44`

## WireMappingTests
`tests/Lightbox.Ai.Tests/AiTests.cs`

- To Wire Resamples Long Strokes And Rounds Coords — `:58`
- To Wire Keeps Short Strokes Intact — `:77`
- From Wire Clamps Everything — `:84`
- From Wire Rejects Unusable Strokes — `:109`
- From Wire Maps Eraser And Label — `:118`
- From Wire List Drops Bad Keeps Good — `:138`

## WireRoundTripTests
`tests/Lightbox.Ai.Tests/AiTests.cs`

- Inbetween Result Dto Parses Model Shaped Json — `:184`

## McpArtistTests
`tests/Lightbox.Ai.Tests/McpArtistTests.cs`

- It Handshakes Once Then Calls The Named Tool — `:59`
- The Tool Is Called With System Prompt And Schema — `:82`
- Text Blocks Are Joined Rather Than Truncated To The First — `:100`
- ATool Error Is Reported Not Parsed — `:122`
- AProtocol Failure Becomes AValue Rather Than An Exception — `:136`
- List Tools Reads The Names — `:149`
- Testing An Mcp Connection Names The Tools When The Chosen One Is Absent — `:162`
- Disposing The Artist Closes The Channel — `:177`
- Arguments Split Like AShell Would — `:195`

## OllamaTests
`tests/Lightbox.Ai.Tests/OllamaTests.cs`

- Request Body Carries Model Schema Prompts And Options — `:47`
- Success Response Parses And Validates — `:71`
- Connection Refused Maps To Retryable With Hint — `:89`
- Model Not Found Suggests Pull — `:102`
- Empty Or Unusable Frames Is Retryable Error — `:117`

## OpenAiTests
`tests/Lightbox.Ai.Tests/OpenAiTests.cs`

- Request Carries Model Schema And Bearer Token — `:38`
- ABase Url With ATrailing Slash Resolves The Same Way — `:66`
- No Key Means No Authorization Header — `:80`
- Reference Images Ride Along As Data Uris Before The Task — `:93`
- Success Parses Into Strokes — `:110`
- Status Codes Become Sentences Somebody Can Act — `:130`
- ARefusal Is ARefusal Rather Than An Error — `:147`
- Hitting The Output Limit Is Truncation Rather Than AParse Failure — `:165`
- Something That Is Not AChat Completion Says So — `:176`

## SubjectReadingTests
`tests/Lightbox.Ai.Tests/SubjectReadingTests.cs`

- An Ordinary Reading Survives Intact — `:28`
- APart Naming AParent Nobody Declared Is Orphaned Rather Than Kept — `:53`
- APart Cannot Be Its Own Ancestor — `:68`
- The Same Name Twice Is One Part — `:100`
- AReading With No Parts Is AFailure Rather Than An Empty Answer — `:116`
- The Schema Cannot Carry Geometry — `:128`
- Without ATaxonomy The Request Is The One Lightbox Always Sent — `:161`
- The Taxonomy Goes At The Front Where ACache Prefix Can Cover It — `:169`
- ATaxonomy Is Cheap Against The Frame Data It Rides On — `:205`
- ATaxonomy Round Trips Through The Project File — `:233`
- AFolder That Was Never Read Writes No Key — `:250`
- ADocument Finds The Subject It Belongs To — `:264`
- ADocument In ASubfolder Still Belongs To The Subject Above It — `:286`
- ANearer Reading Wins Over The One Above It — `:303`

## TaxonomyMeasurementTests
`tests/Lightbox.Ai.Tests/TaxonomyMeasurementTests.cs`

- The Two Arms Of The Comparison Differ In Exactly The Taxonomy — `:98`
- Does The Taxonomy Alone Improve An Inbetween — `:113`

## ActiveColorTests
`tests/Lightbox.App.Tests/ActiveColorTests.cs`

- The App Starts On Black From The Palette Over White — `:17`
- Swapping Trades The Two Colours — `:30`
- Swapping Keeps The Palette Link — `:41`
- Swapping To AColour With No Swatch Drops The Link Rather Than Faking One — `:56`
- Reset Goes Back To Black Over White And Relinks — `:71`
- The Colour Painted With Is The Foreground Whichever Tool Is Active — `:85`
- Every Picker Sees The Documents Palette — `:103`
- Picking From The Palette Links The Swatch Rather Than Taking Its Value — `:115`
- APicker Built With No Document Simply Shows No Palette — `:131`

## AddToPaletteTests
`tests/Lightbox.App.Tests/AddToPaletteTests.cs`

- The Colour On The Wheel Goes Into The Selected Palette — `:29`
- With No Palette Yet One Is Made — `:43`
- Making The Palette And Adding The Colour Is One Undo Step — `:64`
- The Stroke That Follows References The New Swatch Rather Than Copying It — `:77`
- Adding The Background Colour Does Not Change What The Brush Is Loaded With — `:98`
- It Goes To The Selected Palette And Not Simply The First One — `:115`
- Every Picker Can Keep AColour Not Only The One In The Panel — `:132`
- Every Palette Is Offered By Its Path — `:152`
- ANamed Target Beats The Selection — `:167`
- An Anonymous Picker Refuses AColour The Palette Already Has — `:186`
- Refusing Says So Rather Than Doing Nothing — `:204`
- The Wheel In The Palette Panel Is Taken At Face Value — `:223`
- The Foreground Picker Adopts The Swatch That Is Already There — `:238`
- The Adopted Swatch Is Live So Recolouring Reaches The Art — `:259`
- The Background Picker Adopts Too — `:278`
- ADuplicate Is Judged Against The Target Palette Only — `:295`
- Duplicate Swatch Makes An Independent Copy — `:316`
- ASwatch Dragged To Another Palette Keeps Its Id And The Art Follows It — `:342`
- ASwatch Dropped On Its Own Palette Changes Nothing — `:368`
- APicker With No Document Behind It Simply Cannot Add One — `:382`

## AiIntegrationTests
`tests/Lightbox.App.Tests/AiIntegrationTests.cs`

- No Artist Disables Ai — `:56`
- Ai Inbetween Inserts Frames Through Shared Path — `:65`
- Ai Inbetween Refusal Surfaces Message No Mutation — `:96`
- Ai Inbetween Without Second Key Asks For One — `:112`
- Ai Inbetween Refuses ALocked Layer — `:122`
- Every Artist Method Starts From Something The Artist Drew — `:157`

## AiProviderPageTests
`tests/Lightbox.App.Tests/AiProviderPageTests.cs`

- The Picker Offers Every Provider And Starts On The Stored One — `:33`
- Each Provider Shows Its Own Fields And Nobody Elses — `:43`
- Only The Secret Fields Are Masked — `:63`
- Choosing AProvider Persists It Immediately — `:75`
- Typing AValue Persists It Under The Right Provider — `:87`
- ADefault Shows As APlaceholder Rather Than As Typed Text — `:103`
- ARequired Field With Nothing Behind It Says So — `:117`
- Switching Provider Clears AStale Test Verdict — `:134`
- The Ai Page Is The Last Category And Hidden Until Chosen — `:144`
- The View Model Picks Up AProvider Chosen While It Is Running — `:156`
- The Unavailable Hint Points At The Configure Window Rather Than An Environment Variable — `:172`
- Ai Assistance Is On By Default — `:183`
- Turning It Off Persists And Takes The Artist With It — `:193`
- The Provider Fields Stay Usable While Assistance Is Off — `:213`
- Both Test Depths Are Offered And Quick Is The Default — `:235`
- The Depth Picker Explains What Each One Costs — `:246`
- The Progress Bar And Clock Are Hidden Until ATest Is Running — `:261`

## AutoExportTests
`tests/Lightbox.App.Tests/AutoExportTests.cs`

- AProject That Never Sets AStatus Writes No Status Key — `:72`
- Reopened Is Not ASynonym For In Development — `:83`
- Nothing Happens When It Is Switched Off — `:97`
- Re Selecting The Status Something Already Has Does Not Export — `:110`
- Arriving At The Trigger From Anywhere Exports — `:125`
- AStatus That Is Not The Trigger Does Not Export And Says Which One Would — `:136`
- The Trigger Is Configurable Because Some Studios Review In Engine — `:147`
- ARelative Folder Resolves Against The Project — `:161`
- ARelative Folder With No Project Is Refused Rather Than Guessed — `:175`
- No Folder Is Said Out Loud Because It Is AMisconfiguration — `:187`
- AFile Name Comes From The Document Name And Survives Punctuation — `:202`
- An Export Lands In The Folder Named After The Document — `:217`
- AMissing Folder Is Created Rather Than Refused — `:232`
- It Uses The Named Preset And Falls Back Rather Than Failing — `:246`
- AFailed Export Is AMessage Rather Than An Exception — `:268`
- APng Sequence Preset Gets AFolder Rather Than AFile Name — `:284`
- The Settings Round Trip And Are Off By Default — `:304`

## BackgroundLayerTests
`tests/Lightbox.App.Tests/BackgroundLayerTests.cs`

- ANew Paper Document Gets ALocked Background Layer Below The Paint Layer — `:40`
- The Paper Colour Comes Out In The Composite — `:56`
- ATransparent Document Has No Background Layer And Stays Transparent — `:68`
- The Background Layer Refuses Edits Until Unlocked — `:79`
- Erasing The Unlocked Background Reveals Real Transparency — `:101`
- ADocument Saved Before Background Layers Existed Still Opens On Its Paper — `:122`
- The Paper Is AStroke Record Not Baked Pixels — `:138`

## BrushCatalogueTests
`tests/Lightbox.App.Tests/BrushCatalogueTests.cs`

- You Can Draw AWhole Picture Without Touching An Expressive Brush — `:28`
- Every Simulated Medium Has AFast Counterpart — `:50`
- The Expressive Ones Are Only Media And Effect Tools — `:66`
- The Picker Groups Fast Brushes Before Expressive Ones — `:86`
- An Expressive User Preset Joins The Expressive Group — `:107`
- Grouping Keeps Each Kind In Its Declared Order — `:125`
- Only The Expressive Ones Carry ABadge — `:145`
- ABadged Brush Says What It Is Paying For — `:154`
- Cost Is Not Written Into The Saved Preset — `:170`

## BrushCursorTests
`tests/Lightbox.App.Tests/BrushCursorTests.cs`

- With AMouse The Ring Is The Full Brush Width — `:24`
- Hovering Shows The Maximum Even After ALight Stroke — `:33`
- The Ring Matches The Radius The Engine Will Stamp — `:47`
- Turning Tracking Off Pins The Ring To Full Size — `:68`
- When Pressure Is Disabled For The Brush The Ring Ignores It — `:82`

## BrushCurveUiTests
`tests/Lightbox.App.Tests/BrushCurveUiTests.cs`

- ABrush With No Curve Still Shows The Shape Its Gamma Means — `:24`
- Drawing ACurve Replaces The Gamma That Was Driving It — `:40`
- Turning ADynamic Off Clears Both Ways It Could Have Been Driven — `:51`
- ADynamic With No Gamma Of Its Own Is Turned On As ALine — `:67`
- Turning On Something Already Driven Leaves It Alone — `:81`
- Reset Puts ADynamic Back To AStraight Line — `:97`
- The Brush And The Eraser Keep Separate Curves — `:109`
- The Editor Hands Back AWhole Curve Rather Than Mutating One — `:124`
- An Editor With No Curve Shows The Identity Rather Than Nothing — `:142`
- Choosing Normal Stores Nothing At All — `:155`
- The Brush Picker Offers The Same Modes The Layer Docker Does — `:171`
- The Picker Offers Round First And Then Every Tip That Exists — `:183`
- Choosing ATip Copies Its Pixels Into The Drawing — `:195`
- Choosing Round Goes Back To The Engines Own Dab — `:211`
- ADropped Tip Leaves The Drawing Alone — `:222`
- Every Built In Tip Has AThumbnail To Show In The Picker — `:236`

## BrushFilterTests
`tests/Lightbox.App.Tests/BrushFilterTests.cs`

- No Filter Shows Everything In The Order It Came — `:25`
- Search Matches Part Of AName And Ignores Case — `:32`
- Search Matches ATag As Well As AName — `:39`
- Two Tags Mean Either Not Both — `:49`
- Search And Tags Narrow Together — `:60`
- An Untagged Brush Is Hidden By Any Tag Filter And Found By Name — `:69`
- Tag Matching Ignores Case And Surrounding Space — `:76`
- Nothing Matching Is An Empty List Rather Than Everything — `:87`

## BrushGizmoTests
`tests/Lightbox.App.Tests/BrushGizmoTests.cs`

- The Gizmo Follows The Brush Size Without APointer Move — `:78`
- The Gizmo Outlines The Tip Rather Than ACircle — `:114`
- The Eraser Gets Its Own Ring — `:158`
- The Canvas Is Bound To Every Part Of The Ring — `:182`
- ABrush Change Announces The Rings Shape And Not Only Its Size — `:208`

## BrushLibraryTests _Category=Performance_
`tests/Lightbox.App.Tests/BrushLibraryTests.cs`

- Reading Brushes Reports Progress Per File — `:61`
- ABad File Is Named Rather Than Counted And Does Not Stop The Rest — `:83`
- Many Bad Files Do Not Produce AParagraph — `:105`
- Giving Up Keeps What Was Already Read — `:117`
- Nothing To Import Says So Rather Than Claiming Success — `:137`
- Importing Off The Thread Still Lands Every Brush In The List — `:146`
- Removing ACollection Saves Once Rather Than Once Per Brush — `:159`
- Removing ASelection Leaves The Shipped Brushes Alone — `:180`
- Removing The Brush In Your Hand Lets Go Of It — `:198`
- The Library Lists Every Brush With Its Mark — `:230`
- ARow Says Where The Brush Came From Because That Decides What You May Do To It — `:241`
- Selecting AShipped Brush Offers No Rename And Says Why — `:254`
- Renaming An Imported Brush Sticks — `:273`
- Removing From The Library Takes Them Out Of The Picker Too — `:295`
- The Progress Bar Is Gone When The Import Is — `:318`
- Reading ACollection Sized Import Costs Real Time — `:334`

## BrushMemoryTests
`tests/Lightbox.App.Tests/BrushMemoryTests.cs`

- With No Project Open The Brush Belongs To The Tool — `:65`
- AComic Keeps The Brush With The Project Without Being Asked — `:78`
- AStoryboard Keeps One Brush For The Tool — `:87`
- AChosen Scope Overrides The Project Type — `:95`
- Under Global The Project Records Nothing — `:105`
- Painting Records The Brush On The Project — `:118`
- An Agents Stroke Does Not Rewrite It — `:132`
- ANew Document In The Project Is Fed That Brush — `:150`
- AProject With Nothing Recorded Leaves The Brush Alone — `:168`
- Switching To Per Project Mid Session Hands Back What Is Already There — `:182`

## BrushParameterTests
`tests/Lightbox.App.Tests/BrushParameterTests.cs`

- Smudge And Blender Use Strength Not Length — `:44`
- ABlur Strength Is Still Its Flow — `:71`
- Length Stays On The Effects Page Rather Than The Bar — `:89`
- Strength Controls Blending Intensity — `:115`

## BrushPickerTests _Category=Performance_
`tests/Lightbox.App.Tests/BrushPickerTests.cs`

- Opening The Picker Fills It With Tiles Rather Than Bare Presets — `:64`
- Every Brush On Offer Has APicture Of Its Mark — `:78`
- Every Built In Brush Leaves AMark On Its Tile — `:153`
- The Shipped Blur Brush Actually Softens What It Passes Over — `:179`
- The Tile Names The Brush And Its Real Size — `:194`
- Picking ATile Applies That Brush — `:210`
- The Picker Opens On The Brush You Are Already Using — `:225`
- Searching Narrows The Grid And Still Gives Tiles — `:239`
- No Match Says So Rather Than Showing An Empty Grid — `:258`
- The Same Brush Reuses Its Picture Rather Than Redrawing It — `:272`
- Editing ABrush Changes Its Picture — `:291`
- An Imported Brush Shows Its Own Tip Rather Than ARound Dab — `:313`
- ACollection Sized Picker Opens Without Stalling — `:372`

## BrushComparisonTests
`tests/Lightbox.App.Tests/BrushPresetEditingTests.cs`

- Two Untouched Brushes Are The Same — `:354`
- Every Setting That Reaches Pixels Is Compared — `:360`
- The Two Configure Settings Are Not Part Of The Brush — `:390`
- Comparing Does Not Disturb Either Brush — `:399`

## BrushPresetEditingTests
`tests/Lightbox.App.Tests/BrushPresetEditingTests.cs`

- APreset Just Chosen Is Not Modified — `:23`
- Nudging Anything Lights The Indicator — `:35`
- Putting ASetting Back Clears The Indicator — `:51`
- ADeep Setting Counts As Much As ASurface One — `:67`
- Anti Aliasing Is Not Part Of The Brush — `:81`
- Updating Writes The Changes Back And Clears The Indicator — `:97`
- Updating AShipped Brush Survives ARestart — `:110`
- AShadowed Built In Appears Once And Keeps Its Place — `:126`
- Reverting AShipped Brush Gives Back The Original — `:141`
- There Is Nothing To Revert On AShipped Brush Nobody Touched — `:161`
- Saving ACopy Leaves The Original Alone — `:173`
- Deleting ABrush You Made Removes It — `:188`
- Deleting AShipped Brush Reverts It Rather Than Removing It — `:200`
- AShipped Brush Cannot Be Renamed — `:214`
- ABrush You Made Can Be Renamed — `:223`
- Tags Persist And Feed The Filter List — `:237`
- ABrush Nobody Filed Writes No Tags Key — `:249`
- Blank And Duplicate Tags Are Dropped Rather Than Stored — `:260`
- Tagging AShipped Brush Shadows It Rather Than Editing The List — `:270`
- Tagging AShipped Brush Does Not Count As Modifying It — `:283`
- ATag Dropped From The Last Brush Using It Leaves The Choice List — `:296`

## BuiltInPresetMergeTests
`tests/Lightbox.App.Tests/BrushPresetEditingTests.cs`

- AUser Preset Reusing ABuilt In Id Replaces It In Place — `:314`
- AShadow Keeps The Originals Position Rather Than Going To The End — `:326`
- Ordinary User Presets Come After The Shipped Ones — `:337`

## BrushStabilisationTests
`tests/Lightbox.App.Tests/BrushStabilisationTests.cs`

- ABrush Follows The Application Until It Is Told Not To — `:20`
- Ticking Per Brush Changes Nothing About How It Draws — `:30`
- Two Brushes Can Steady The Hand Differently — `:46`
- Unticking Hands The Sliders Back And Drops The Brushes Copy — `:68`
- Editing ABrushes Own Does Not Move The Applications Default — `:85`
- APreset Carries Its Own Stabilisation — `:98`
- APresets Stabilisation Survives ARestart — `:120`
- Changing Stabilisation Counts As Modifying The Brush — `:136`
- ABrush That Follows The Application Writes No Stabilisation Key — `:149`

## BrushPresetTests
`tests/Lightbox.App.Tests/BrushToolTests.cs`

- Selecting APreset Applies Its Settings To The Stroke Record — `:53`
- Each Simulated Medium Reaches The Stroke Record With Its Own Physics — `:84`
- Brush And Eraser Keep Separate Configurations — `:123`
- Last Configured Brush Survives ANew Session — `:138`
- Save Current As Preset Persists User Presets — `:162`
- Imported Brush Becomes APreset And Its Tip Enters The Document — `:212`

## HiddenLayerTests
`tests/Lightbox.App.Tests/BrushToolTests.cs`

- Painting On AHidden Layer Is Blocked Until Visible Again — `:11`
- Ai Inbetween Refuses AHidden Layer — `:31`

## CameraCompositingTests
`tests/Lightbox.App.Tests/CameraCompositingTests.cs`

- ACamera Centred On AQuadrant Frames That Quadrant — `:52`
- Zooming Out Shows More Of The Document — `:67`
- Rolling The Camera Rotates What The Frame Sees — `:83`
- No Transform Composes Exactly As It Did Before Cameras Existed — `:101`
- No Transform Still Honours The Uniform Scale Path — `:114`
- AClip Under ARolling Camera Covers Where The Region Actually Landed — `:123`
- Device Bounds Without ACamera Is The Mapping The Compositor Always Used — `:141`
- The Compose Ring Copies Forward Correctly Under ACamera — `:152`

## CameraExportTests
`tests/Lightbox.App.Tests/CameraExportTests.cs`

- Without ACamera The Export Is Byte For Byte What It Always Was — `:78`
- Without ACamera The Output Is The Canvas — `:102`
- The Output Size Comes From The Camera — `:111`
- APan Produces ADifferent Framing On Each Frame — `:122`
- AStatic Camera Frames The Same Thing On Every Frame — `:139`
- ACamera With No Keys Frames The Scene Centre — `:153`
- APush In Enlarges What The Frame Shows — `:167`

## CameraViewModelTests
`tests/Lightbox.App.Tests/CameraViewModelTests.cs`

- ANew Document Has No Camera And No Overlay — `:22`
- Adding ACamera Frames The Whole Canvas At One To One — `:31`
- Adding ACamera Twice Is Harmless — `:54`
- Removing The Camera Takes The Overlay With It — `:65`
- Editing AFraming Keys It At The Playhead — `:79`
- Scrubbing Between Keys Shows The Interpolated Framing — `:93`
- Clearing AKey Leaves The Others — `:112`
- The Ruler Learns Which Frames Carry AKey — `:131`
- Viewing Through The Camera Drops The Overlay — `:147`
- Viewing Through The Camera Does Not Touch The Document — `:163`
- Painting Still Works While Looking Through The Camera — `:183`
- ACamera With No Keys Still Frames Something — `:203`

## CanvasInputTests
`tests/Lightbox.App.Tests/CanvasInputTests.cs`

- Mouse Drag Paints AStroke — `:37`
- Mouse Drag After Wheel Zoom Still Paints — `:50`
- Mouse Drag After Middle Button Pan Still Paints — `:64`
- Mouse Drag After Mirror Rotate Zoom Still Paints At Correct Doc Point — `:81`

## CanvasOverlayGeometryTests
`tests/Lightbox.App.Tests/CanvasOverlayTests.cs`

- ADrop Goes To The Nearest Edge — `:34`
- The Answer Depends Only On Where The Pointer Is — `:38`
- How Far Along Is AFraction Of The Edge It Is On — `:54`
- ABar On ASide Edge Runs Vertically — `:66`
- ADegenerate Canvas Does Not Divide By Zero — `:75`
- The Default Puts View Top Right And Shortcuts Down The Side — `:82`
- ALayout Clones Rather Than Sharing Its Placements — `:94`

## CanvasOverlayTests
`tests/Lightbox.App.Tests/CanvasOverlayTests.cs`

- Both Bars Are On The Canvas To Start With — `:123`
- ABar On ASide Edge Stacks Downwards With Its Icons Upright — `:144`
- ABar Follows The Pointer While It Is Being Dragged — `:170`
- The Zoom Readout Turns Its Feet Towards The Canvas — `:201`
- The Onion Toggle Agrees With The Layers Panel — `:222`
- Closing ABar Hides It And The View Menu Brings It Back — `:241`
- Collapsing ABar Survives AWorkspace Reset — `:261`
- The Bars Are Listed Separately From The Panels — `:278`
- The Onion Toggle Acts On The Layer Being Drawn On — `:294`
- One Button For Play And Pause — `:305`
- An Illustration Project Is Not Offered Transport Controls — `:320`
- The Camera Toggle Is Absent Until There Is ACamera — `:340`

## CanvasQualityEffectTests
`tests/Lightbox.App.Tests/CanvasQualityEffectTests.cs`

- Half Quality Composites Fewer Pixels Than Full — `:49`
- The Snapshot Still Describes The Document It Came From — `:68`
- An Export Is Full Resolution Whatever The Canvas Is Set To — `:90`
- Full Quality Is Unaffected By What The Screen Can Show — `:109`

## CanvasReliefTests
`tests/Lightbox.App.Tests/CanvasReliefTests.cs`

- ACanvas That Cannot Keep Up Gets Its Quality Turned Down — `:48`
- ACanvas That Is Keeping Up Is Left Alone — `:60`
- ASlow Start Is Not Enough To Act On — `:70`
- ASoftware Renderer Gets Help Sooner — `:83`
- AChosen Quality Is Never Revised — `:111`
- Choosing The Default On Purpose Is Still AChoice — `:124`
- It Happens Once And Then Leaves The Artist Alone — `:139`
- It Says So — `:154`
- It Does Not Announce ALowering That Did Not Happen — `:166`
- The Backend Path And The Measured Path Do Not Both Fire — `:190`

## CanvasViewTests
`tests/Lightbox.App.Tests/CanvasViewTests.cs`

- Default View Maps View Center To Doc Center At Fit Scale — `:22`
- Zoom Keeps Anchor Fixed And Scales Mapping — `:35`
- Mirror Flips Horizontally Around View Center — `:50`
- Rotate90 Swaps Axes In The Mapping — `:69`
- Reset View Restores The Default Mapping — `:81`
- View Transform Never Touches The Document — `:98`

## CelDragGestureTests
`tests/Lightbox.App.Tests/CelDragGestureTests.cs`

- Opening AContext Menu Cancels The Pending Drag — `:40`
- Without The Menu APull Still Drags The Cel — `:55`
- Letting Go Disarms The Gesture — `:67`
- AWobble Under The Threshold Is AClick Not ADrag — `:81`
- AMove With The Button Up Disarms Rather Than Waits — `:94`
- An Empty Slot Is Not Armed At All — `:105`
- ARight Click Alone Never Arms It — `:117`
- Moving Over ADifferent Cel Does Not Start The Armed One — `:128`
- ADrag Already Running Ignores ACancel — `:137`

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

## ColorSwatchGestureTests
`tests/Lightbox.App.Tests/ColorSwatchGestureTests.cs`

- Each Half Of The Pair Carries Its Own Picker — `:77`
- Clicking ASwatch Opens Its Picker — `:103`
- Clicking The Background Swatch Opens The Background Picker And Not The Foreground One — `:114`
- Dragging Off ASwatch Does Not Also Open Its Picker — `:126`
- The Dropdown Beside The Pair Opens The Foreground Picker — `:145`
- The Background Picker Starts On The Background Colour — `:163`
- Swapping And Resetting Move The Background Picker Too — `:171`
- Editing The Background Picker Changes The Background And Leaves The Foreground Alone — `:187`
- Picking ABackground From The Palette Keeps The Link Through ASwap — `:200`
- The Background Picker Sees The Documents Palette As Well — `:217`

## ComposeRingTests
`tests/Lightbox.App.Tests/ComposeRingTests.cs`

- After ALarge Change The Next Publishes Still Only Repaint Their Own Dirty Rect — `:61`
- ABuffer Still Holding ASnapshot Is Caught Up Once It Comes Free — `:100`
- Catching Up Does Not Dispose The Image The Canvas Is Still Showing — `:151`
- Every Publish Is ACorrect Full Composite — `:172`
- Invalidate All Forces AFull Repaint Even With ASmall Dirty Rect — `:215`

## ControlTreatmentTests
`tests/Lightbox.App.Tests/ControlTreatmentTests.cs`

- The Button Enter Presses Is The One That Looks Like The Answer — `:44`
- Nothing Destructive Is The Primary Button — `:71`
- ARank Never Sets ASize — `:101`
- Every Rank And Badge Resolves — `:124`
- The Primary Rank Carries The Accent Gradient — `:142`
- Badges Are Named For Meaning Rather Than Colour — `:153`

## CrashScenarioTests
`tests/Lightbox.App.Tests/CrashScenarioTests.cs`

- Every Scenario Says What It Proves And What It Costs — `:40`
- The Destructive Ones Ask First And The Harmless Ones Do Not — `:57`
- The Survivable Failure Is Survivable And Lands In The Log — `:86`
- Writing To The Console Does Not Fail When There Is No Console — `:101`
- The Reporter Listens On All Three Channels AFailure Can Arrive By — `:125`
- The Dispatcher Handler Records Without Changing What The App Does — `:142`
- The Triggers Are Absent Until Diagnostics Are Turned On — `:155`
- Every Menu Entry Names AScenario That Exists — `:168`

## CursorAlignmentTests
`tests/Lightbox.App.Tests/CursorAlignmentTests.cs`

- Culling Never Moves The Document Point Under The Pointer — `:111`
- AViewport Culled Canvas Round Trips ADocument Point — `:168`
- Ink Lands Under The Pointer With Culling Live — `:200`
- ACulled Compose Puts The Art Where AFull Compose Does — `:289`
- Repeated Zooming Does Not Drift The Anchor — `:379`

## DensityScaleTests
`tests/Lightbox.App.Tests/DensityScaleTests.cs`

- The Scale The Doc Describes Is The Scale The Code Applies — `:56`
- No View Redeclares ASize The Scale Owns — `:89`
- The Overlay Tile Agrees With The Scale — `:135`

## DiagnosticLogTests
`tests/Lightbox.App.Tests/DiagnosticLogTests.cs`

- ACrash Is Written Down With The Build It Came From — `:43`
- The Cause Underneath Is Not Lost — `:65`
- The Next Run Is Told Once And Then Not Again — `:79`
- AClean Run Has Nothing To Report — `:91`
- ASurvivable Failure Is Recorded Once Per Context — `:98`
- An Unwritable Folder Is Survived Rather Than Thrown — `:112`
- The Log Sits Beside The Other App Data — `:126`

## DiagnosticsMenuTests
`tests/Lightbox.App.Tests/DiagnosticsMenuTests.cs`

- The Console Switch Is Off Until Somebody Asks — `:24`
- The Switch Is Listed In The Settings File Like Every Other Preference — `:30`
- The Switch Survives The Restart It Exists For — `:43`
- Help Offers The Folder The Console And The Build — `:53`
- The Build Label Names The Commit Rather Than Just AVersion — `:72`

## DirtyRevisionTests
`tests/Lightbox.App.Tests/DirtyRevisionTests.cs`

- Undoing Back To The Saved State Clears The Badge — `:46`
- Redoing To The Saved State Is Still Saved — `:75`
- Trimming The Undo Stack Does Not Fake AClean Document — `:102`
- ADocument At Its Saved Revision Is Not Dirty — `:120`
- Choosing ABrush Does Not Mark The Document — `:143`
- ANever Saved Document Badges But Only Prompts Once Drawn In — `:179`
- ASheet Edit Raises The Badge On Both Tabs — `:210`

## DockDragGhostTests
`tests/Lightbox.App.Tests/DockDragGhostTests.cs`

- It Is Not There Until Something Is Being Dragged — `:20`
- It Never Takes The Pointer — `:26`
- Showing It Puts It On Screen And Hiding Takes It Off — `:41`
- Hiding What Is Already Hidden Is Harmless — `:53`
- It Follows The Pointer Without Being Rebuilt — `:68`

## DockLayoutTests
`tests/Lightbox.App.Tests/DockLayoutTests.cs`

- The Default Layout Opens The Sidebar Panels And ATimeline — `:14`
- Docking Into AStrip Puts The Panel At The Asked For Position — `:31`
- Orders Are Always Contiguous From Zero — `:43`
- Moving The Last Panel Out Of An Area Empties It — `:63`
- Hiding APanel Keeps Where It Was So Showing It Puts It Back — `:76`
- Panels Sharing ASlot Are One Group — `:89`
- The One You Just Dropped Is The One Showing — `:108`
- Exactly One Tab Shows Per Slot However The Layout Arrived — `:120`
- APanel Is Still In Exactly One Place — `:135`
- AGroup That Loses Its Last Member Stops Existing — `:153`
- Hiding APanel Leaves Its Group Intact — `:168`
- ASidebar Is Capped By Its Panels But An Uncapped Panel Removes The Ceiling — `:181`
- The Timeline Is Not Draggable — `:195`
- ALayout Round Trips Through Json — `:204`
- ACorrupt Layout Falls Back Rather Than Throwing — `:220`
- Cloning ALayout Keeps The Rulers And Guide Flags — `:230`
- AClone Shares Nothing With The Original — `:250`
- ALayout Saved Before Tabs Existed Still Loads — `:266`
- Sizing ASlot Sizes Every Tab In It — `:312`
- AStrip Is Sized By The Tabs Showing Not The Ones Hidden — `:328`

## DockZoneTests
`tests/Lightbox.App.Tests/DockZoneTests.cs`

- Near An Empty Edge The Whole Area Is Offered — `:19`
- Each Edge Is Reachable — `:37`
- The Middle Of The Canvas Is Not ADrop Target — `:43`
- The Timeline Cannot Be Dropped — `:51`
- The Upper Half Of APanel Inserts Above It — `:62`
- The Lower Half Of APanel Inserts Below It — `:72`
- The Preview Is ABand At The Boundary So The Neighbour Visibly Makes Room — `:78`
- Dropping APanel Back Where It Already Is Changes Nothing — `:91`
- Dragging The Only Panel Of AStrip Over Itself Offers Nothing — `:102`
- AGap Below AShort Stack Appends To It — `:110`
- ATop Strip Splits Left To Right Rather Than Top To Bottom — `:126`
- Dropping On AHeader Tabs Into That Slot — `:139`
- Dropping On The Body Still Makes ASlot Of Its Own — `:161`
- APanel Cannot Be Tabbed Into Itself — `:178`
- AHeaderless Slot Offers No Tab Target — `:193`

## LayerRowTests
`tests/Lightbox.App.Tests/DockerUiTests.cs`

- Rows Show Topmost Layer First And Track Cells — `:13`
- Rename Through Row Writes To Document And Is Undoable — `:31`
- Rename To Blank Snaps Back Without An Undo Step — `:46`
- Visibility Toggle Through Row Is Undoable — `:60`
- Select Frame On Another Layers Cell Selects That Layer And Frame — `:72`
- Add Layer Button Follows Kind Dropdown — `:89`

## PerLayerOnionTests
`tests/Lightbox.App.Tests/DockerUiTests.cs`

- Disabling Layer Onion Removes Its Ghosts From The Snapshot — `:112`

## PlaybackSpeedTests
`tests/Lightbox.App.Tests/DockerUiTests.cs`

- Speed Percent Clamps To Sane Range — `:141`
- Clock Interval Scales With Fps And Speed — `:151`

## SidebarTests
`tests/Lightbox.App.Tests/DockerUiTests.cs`

- Toggle Sidebar Flips Visibility — `:162`
- Switch Sidebar Side Flips Side — `:173`
- Toggle Timeline Flips Visibility — `:184`

## DocumentScopedStateTests
`tests/Lightbox.App.Tests/DocumentScopedStateTests.cs`

- Zoom Is Remembered Per Document Rather Than Shared — `:44`
- The Whole Framing Travels Not Only The Zoom — `:82`
- ADocument With No Reference Shows No Reference Panel — `:117`
- Switching Tabs Restores That Documents Tool State — `:148`
- The Brush Does Not Follow The Document Because Q9 Says So — `:180`

## BackgroundColorTests
`tests/Lightbox.App.Tests/DocumentTabTests.cs`

- Scene Background Round Trips And Tints The Snapshot — `:184`
- Transparent Background Renders Transparent Pixels — `:202`

## ColorWheelFidelityTests
`tests/Lightbox.App.Tests/DocumentTabTests.cs`

- Wheel Value Is Not Rewritten While Dragging — `:218`
- Slider Channels Are Not Rewritten While Editing — `:237`

## DocumentTabTests
`tests/Lightbox.App.Tests/DocumentTabTests.cs`

- Starts With One Untitled Tab That Says It Is Not On Disk — `:12`
- New Document Adds Tab With Settings And Activates It — `:29`
- Painting Marks The Tab Dirty Save Clears It — `:49`
- Switching Tabs Keeps Each Document And Its Undo History — `:68`
- Switching Tabs Does Not Mark Anything Dirty And Restores Playhead — `:94`
- Close Tab Activates Neighbor And Never Leaves Zero Tabs — `:112`
- Open Document Tab Uses File Name And Keeps Existing Tabs — `:131`
- ADocument With No Layers Opens Rather Than Throwing — `:165`

## EngineApiTests
`tests/Lightbox.App.Tests/EngineApiTests.cs`

- Every Symbol The Record Names Is Actually Called By That Importer — `:55`
- The Game Maker Record Is Checked Against Behaviour Because There Is No Script — `:71`
- Every Engine Target Has ARecord — `:91`
- Every Engine Export Target Maps To An Engine With ARecord — `:104`
- No Record Is Empty Or Vague — `:131`
- The Importer Itself Names The Version It Needs — `:151`
- Unitys Version Branch Is Compiled Rather Than Checked At Runtime — `:160`
- No Importer Has Been Run Against The Real Engine And The Record Says So — `:175`
- The Unverified Query Narrows Rather Than Being All Or Nothing — `:185`

## ExportConfigPageTests
`tests/Lightbox.App.Tests/ExportConfigPageTests.cs`

- The Export Page Is The One That Shows At Its Index — `:65`
- It Opens Showing What Is Stored Rather Than ADefault — `:77`
- The Toggle Writes Back And Persists — `:95`
- Changing The Trigger Status Writes The Status And Not The Index — `:109`
- The Preset Picker Offers The Built Ins And The Artists Own — `:123`
- The Preset List Is Reread Each Time The Page Is Opened — `:140`
- An Empty Folder Means Unset Rather Than The Working Directory — `:157`
- Loading The Page Does Not Itself Change Anything — `:173`

## ExportPinTests
`tests/Lightbox.App.Tests/ExportPinTests.cs`

- The Three States Are Three And The Default Is Absent — `:26`
- Pinning Is One Undo Step And Marks The Document Dirty — `:49`
- Setting The Pin It Already Has Does Nothing — `:65`
- APinned Layer Is Left Out Of The Sheet It Would Otherwise Appear In — `:81`

## ExportWindowTests
`tests/Lightbox.App.Tests/ExportWindowTests.cs`

- APreset Round Trips Through The File — `:67`
- The Built Ins Are Never Written To The File — `:90`
- ACorrupt File Leaves AWorking App Rather Than ADialog — `:104`
- ANameless Preset Is Dropped Rather Than Shown As ABlank Row — `:113`
- The Built Ins Take The Positions This Pillar Already Argued For — `:120`
- ASheet Preset Writes The Image And The Sidecar — `:142`
- AUnity Preset Also Writes The Importer And Keeps Its Block — `:156`
- AUnity Preset Still Reports What It Left Out — `:173`
- APng Sequence Preset Writes Frames And Reports No Omissions — `:187`
- The Status Line Names The Layers It Left Out — `:205`
- An Export With Nothing Left Out Says Only What It Made — `:228`
- The Controls Round Trip APreset — `:239`
- What Does Not Apply Is Hidden Rather Than Disabled — `:269`
- Saving APreset Keeps It And Selects It — `:290`
- Saving Over An Existing Name Replaces It Rather Than Adding ASecond Row — `:303`
- ABlank Name Saves Nothing — `:317`
- ABuilt In Cannot Be Deleted — `:325`
- ASheet Preset With ANormal Map Writes It Beside The Sheet — `:341`
- There Is No Normal Map Unless It Is Asked For — `:355`
- The Map Is The Same Size As The Sheet It Came From — `:367`
- AUnity Preset Gets The Map Too And Still Keeps Its Block — `:385`
- The Map Settings Appear Only Once The Map Is Asked For — `:398`
- The Green Convention Round Trips Through The Window — `:418`
- AGarbled Number Falls Back Rather Than Refusing The Export — `:439`

## ExportWiringTests
`tests/Lightbox.App.Tests/ExportWiringTests.cs`

- Saving Bumps The Document Version And The Manifest Keeps It — `:41`
- The Export Plan Describes What Would Be Written — `:61`
- An Export Goes Stale When Its Documents Move On — `:80`
- Declaring APreset Sets The Artifact Boundary — `:114`
- ATest Export Goes Elsewhere And Ignores Grouping And Status — `:152`
- ATest Export Needs ADocument Selected — `:189`
- Nothing Exported Means Nothing Stale — `:199`
- Resolving AGrouped Plan Loads Its Documents And Names One File — `:215`
- Running AGrouped Plan Writes One Sheet Holding Every Document — `:247`
- ATarget That Cannot Hold Several Documents Refuses Rather Than Exporting The First — `:291`
- ADocument That Cannot Be Read Is Named Rather Than Throwing — `:315`
- The Confirmation Names What Has Drifted Since It Was Built — `:352`

## FieldShapeTests
`tests/Lightbox.App.Tests/FieldShapeTests.cs`

- Every Field Is The Same Shape As Every Other One — `:53`
- The Inner Text Box Fits Inside The Field That Hosts It — `:75`
- AField Is AWell Rather Than ARaised Surface — `:94`

## FileRevealTests
`tests/Lightbox.App.Tests/FileRevealTests.cs`

- Windows Selects AFile Inside Its Folder — `:15`
- Windows Opens AFolder Rather Than Selecting It — `:26`
- Mac Reveals With Dash R — `:34`
- Linux Opens The Containing Folder Because There Is No Portable Select — `:45`
- Opening Hands The Path To The Desktop — `:56`
- APath With No Parent Is Its Own Folder — `:66`
- Nothing Is Revealed For APath That Is Not There — `:72`

## FillAndBackgroundBugTests
`tests/Lightbox.App.Tests/FillAndBackgroundBugTests.cs`

- ASecond Fill In ADifferent Colour Replaces The First — `:39`
- AFill Still Tucks Under The Line Work — `:54`
- AFill After Erasing Is Not Swallowed By The Eraser — `:74`
- Erasing Produces Transparency Not Paper — `:103`
- The Startup Document Opens On Paper — `:121`
- The Startup Document Lands On APaintable Layer — `:143`
- ATransparent Document Has No Background Layer — `:151`
- The Brush Ring Follows The Size Slider — `:163`

## FloatingPanelTests
`tests/Lightbox.App.Tests/FloatingPanelTests.cs`

- APanel Can Be Torn Out And Docked Again Without Crashing — `:53`
- ADocked Panel Is In The Strip Rather Than Parked In The Pool — `:71`
- The Floating Window Is Gone Once The Panel Is Docked Again — `:92`
- It Is The Same Panel Instance All The Way Round — `:107`
- Floating And Docking Repeatedly Stays Stable — `:127`
- Closing The Floating Window Parks The Panel Rather Than Losing It — `:148`

## ContextShortcutTests
`tests/Lightbox.App.Tests/FoldersContextsPickerTests.cs`

- Same Key Means Different Things Per Context — `:12`
- Global Bindings Fire In Every Context Unless Shadowed — `:23`
- Conflicts Only Count When Contexts Overlap — `:36`

## LayerFolderTests
`tests/Lightbox.App.Tests/FoldersContextsPickerTests.cs`

- Create Folder Groups The Active Layer And Shows AHeader Row — `:132`
- Folder Visibility Gates Its Members In Compositing And Painting — `:147`
- Collapse Hides Member Rows From The Docker Panel Only — `:166`
- Add And Remove Keep The Folder Contiguous — `:179`
- Folder Color Is Undoable And Serializes — `:196`
- Dissolve Ungroups Everything And Folders Serialize — `:214`

## NudgeSelectionTests
`tests/Lightbox.App.Tests/FoldersContextsPickerTests.cs`

- Nudge Shifts Every Contour Point By Whole Pixels — `:96`
- Nudge Without ASelection Is ANo Op — `:109`

## PickerToolTests
`tests/Lightbox.App.Tests/FoldersContextsPickerTests.cs`

- Pick Color At Reads The Composited Color And Paper When Empty — `:55`
- Insert Keyframe At Playhead Keys The Active Cel — `:80`

## FrameBitmapCacheTests
`tests/Lightbox.App.Tests/FrameBitmapCacheTests.cs`

- The Byte Budget Is Honoured Even When It Cannot Afford The Frame Floor — `:33`
- The Frame Floor Still Applies When The Budget Can Afford It — `:50`
- The Same Frame At Two Scales Is Cached Twice Rather Than Thrashing — `:63`
- The Same Frame At Two Document Sizes Is Also Cached Separately — `:83`
- Invalidating AFrame Drops Every Render Of It — `:97`
- Invalidating One Frame Leaves The Others Alone — `:116`
- Cached Bytes Tracks What Is Actually Held — `:130`

## GameMakerExportTests
`tests/Lightbox.App.Tests/GameMakerExportTests.cs`

- An Untagged Document Is One Strip Named With Its Frame Count — `:84`
- The Number In The Name Is The Number Of Cells In The Image — `:94`
- No Project File Is Ever Written — `:111`
- There Is No Importer Script Because Game Maker Cannot Run One — `:123`
- The Strip Is One Row — `:137`
- APadding Request Is Overridden Rather Than Honoured — `:150`
- APacked Layout Is Overridden Rather Than Honoured — `:165`
- APer Frame Trim Is Refused And Said So Rather Than Silently Changed — `:179`
- ANone Trim Is Still Honoured Because It Keeps Cells Uniform — `:194`
- Each Tag Becomes Its Own Strip Because AGame Maker Sprite Is One Animation — `:209`
- Each Tags Strip Is Exactly Its Own Frames Wide — `:219`
- ATags Strip Holds The Frames That Tag Covers And Not The Ones Before It — `:232`
- The Whole Sheet Is Not Left Behind When It Was Cut Up — `:269`
- Two Tags With The Same Name Do Not Overwrite Each Other — `:281`
- The Speed Is Given In Frames Per Second Because The Editor Offers Two Units — `:300`
- AHold Is Already Expressed As Repeated Cells So Uniform Speed Is Enough — `:308`
- APing Pong Tag Is Reported Rather Than Quietly Flattened — `:332`
- ATag That Does Not Loop Is Reported Because AGame Maker Sprite Always Does — `:346`
- An Ordinary Document Has Nothing To Report And Says Nothing — `:357`
- The Generic Sidecar Keeps Every Key It Already Had — `:370`
- The Sidecar Names Every Strip And The Sprite It Becomes — `:385`
- ADocument With No Pivot Carries No Origin — `:399`
- APivot Becomes An Origin Inside The Cell — `:407`
- AGame Maker Preset Writes Every Strip And The Sidecar — `:426`
- The Runner Puts The Notes In The Summary Where Somebody Will See Them — `:439`
- AGame Maker Preset Still Reports What It Left Out — `:456`
- ANormal Map Is Written For Every Strip Rather Than Only The First — `:468`
- The Strip Layout Controls Are Hidden Rather Than Shown And Overridden — `:482`

## GodotExportTests
`tests/Lightbox.App.Tests/GodotExportTests.cs`

- No Tres File Is Ever Written — `:64`
- The Generic Sidecar Keeps Every Key It Already Had — `:77`
- The Godot Block Carries Only What The Generic File Lacks — `:92`
- Frame Durations Arrive As Multipliers Rather Than Milliseconds — `:108`
- ADocument With No Pivot Carries No Offsets — `:122`
- AFeet Pivot Becomes An Upward Sprite Offset — `:131`
- An Edited Importer Is Not Overwritten — `:150`
- The Importer Can Be Suppressed — `:161`
- The Script Never Touches Project Godot Or The Cache — `:173`
- The Script Builds The Resource Through Godots Own Api — `:184`
- The Script Only Claims Sidecars That Are Ours — `:198`
- The Script Clips Atlas Filtering So Neighbouring Sprites Cannot Bleed — `:209`
- The Script Removes The Default Animation When Tags Supply Their Own — `:217`
- The Script Says It Is Godot Four And Why — `:225`
- AGodot Preset Writes The Sheet The Sidecar And The Script — `:237`
- AGodot Preset Still Reports What It Left Out — `:250`

## GradientRampTests
`tests/Lightbox.App.Tests/GradientRampTests.cs`

- Clicking The Colour Track Adds AStop Where You Clicked — `:24`
- Dragging AColour Stop Moves It — `:40`
- The Last Two Colour Stops Cannot Be Removed — `:51`
- Adding AColour Stop Is Undoable — `:63`
- AGradient Has No Alpha Track Until You Add One — `:77`
- The First Opacity Stop Seeds Both Ends So It Does Not Fade Everything — `:85`
- Editing The Selected Opacity Shows In The Ramp — `:102`
- Removing Down To One Opacity Stop Drops The Track Entirely — `:115`
- Opacity And Colour Stops Are Selected Independently — `:130`
- An Opacity Hole Shows On The Canvas — `:145`

## GradientToolTests
`tests/Lightbox.App.Tests/GradientToolTests.cs`

- ANew Document Has No Gradients And The Docker Is Hidden — `:42`
- ANew Gradient Is Black To White And Undoable — `:51`
- Dragging Lays Down AGradient Stroke With The Drag As Its Axis — `:64`
- The Ramp Runs Along The Drag — `:78`
- AClick With No Drag Paints Nothing — `:93`
- Escape During The Drag Abandons It — `:104`
- Editing AStop Repaints The Gradient Already Laid Down — `:118`
- Switching To Radial Changes The Shape Of What Is Painted — `:136`
- Adding AStop Inserts It Between The Selected One And The Next — `:157`
- The Last Two Stops Cannot Be Removed — `:171`
- AGradient Stroke Survives AReload — `:182`
- Undoing Removes The Gradient Stroke — `:205`
- The Tool Makes ABlack To White Gradient If There Is None — `:216`
- ALocked Layer Refuses AGradient — `:233`
- The Ramp Is Visible While Dragging And Survives The Pen Lift — `:242`
- Opacity Is Recorded On The Stroke Not Read At Render Time — `:273`
- Transforming AGradient Moves Its Axis — `:291`
- ASelection Over AGradient Finds It — `:311`
- ASelection Elsewhere Still Leaves Ordinary Strokes Alone — `:331`

## GuideAndShapeTests
`tests/Lightbox.App.Tests/GuideAndShapeTests.cs`

- ADocument With No Guides Draws Exactly As Before — `:35`
- The First Guide Brings The Machinery And The Last One Takes It Away — `:52`
- Adding And Removing AGuide Is Undoable — `:67`
- AStroke On AGrid Records The Snapped Points — `:80`
- Moving AGuide Afterwards Does Not Move The Art — `:95`
- ARuler Straightens The Stroke Drawn Along It — `:110`
- AStroke Across The Ruler Is Left Freehand — `:129`
- Turning Snapping Off Leaves The Stroke Alone — `:146`
- AStroke To ADead Guide Is Unconstrained — `:159`
- AShape Is An Ordinary Stroke — `:174`
- AShape Carries The Current Brush And Swatch — `:194`
- Shift Squares It And Alt Grows It From The Centre — `:209`
- AClick With No Drag Is Not AShape — `:226`
- AShape Is Undoable In One Step — `:241`
- AShape Snaps To The Grid Like Anything Else — `:256`
- The Shape Tool Does Not Paint On ADrag With The Brush — `:272`
- The Canvas Draws No Guides Until There Are Some — `:290`
- AHidden Guide Is Not Drawn But Still Snaps — `:315`

## GuidePainterTests
`tests/Lightbox.App.Tests/GuidePainterTests.cs`

- AGuide Is Visible Over An Opaque Drawing — `:74`
- The Art Still Reads Through It — `:87`
- No Guides Means Nothing Is Painted Over The Art — `:103`
- ADraft Is Brighter Than APlaced Guide — `:115`
- AVanishing Point Is Marked Where Its Rays Meet — `:128`
- AGrid Too Fine To Read Is Not Drawn At All — `:141`
- AGrid Coarse Enough To Read Is Drawn — `:164`

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
- Four K The First Events Of AStroke Repaint No More Than The Middle Of One — `:142`
- Four K Masked Stroke Costs No More Than An Unmasked One — `:198`
- Four K Wet Media Stroke Stays Within Its Budget — `:230`
- Four K Frame Cache Stays Within Its Memory Budget — `:271`
- Headroom Reports Smooth While Painting On Four K — `:291`

## AlphaSelectAndWandTests
`tests/Lightbox.App.Tests/LayerCompositingTests.cs`

- Select Layer Alpha Selects Only The Painted Pixels — `:158`
- Select Layer Alpha On An Empty Layer Is ANo Op With AMessage — `:172`
- Select Layer Alpha Subtract Carves Out Of An Existing Selection — `:181`
- Wand Selects The Clicked Color Region — `:197`
- Wand On Empty Canvas Selects The Connected Emptiness — `:209`
- Fill Inside AWand Selection Stays Inside And Records The Clip — `:221`

## BlendComposeTests
`tests/Lightbox.App.Tests/LayerCompositingTests.cs`

- Multiply Darkens And Screen Lightens — `:32`
- Opacity Still Applies Under ABlend Mode — `:44`
- To Skia Maps Normal To Src Over And Covers Every Mode — `:52`

## CelClipboardTests
`tests/Lightbox.App.Tests/LayerCompositingTests.cs`

- Copy Paste Deep Clones With Fresh Ids And Extends The Timeline — `:249`
- Cut Copies Then Clears So The Cel Becomes AHold — `:268`
- Paste Across Kinds Converts Strokes But Refuses Baseline Pixels Onto Vector — `:281`
- Exposure Editing From Cells Extends And Clears — `:303`

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
- Clear Layer Blanks Every Drawing But Keeps The Timing — `:48`
- Docker Visibility Toggles Round Trip — `:73`

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

## LiveMaskPixelTests
`tests/Lightbox.App.Tests/LiveMaskPixelTests.cs`

- Alpha Locked The Live Stroke Is Already Masked — `:81`
- Alpha Locked Live And Committed Agree Everywhere — `:104`
- Not Alpha Locked The Stroke Still Covers The Whole Canvas — `:121`
- Selected The Live Stroke Is Already Clipped — `:135`
- Alpha Locked And Selected Both Masks Apply Live — `:161`

## LiveMatchesCommittedTests _Category=Visual_
`tests/Lightbox.App.Tests/LiveMatchesCommittedTests.cs`

- AHard Round Ink Stroke Is The Same Live And Committed — `:203`
- An Airbrush Keeps Its Opacity When The Pen Lifts — `:218`
- The Live Stroke Does Not Shrink On Release — `:233`
- AScattered Jittered Brush Does Not Reshuffle On Release — `:248`
- ASimulated Medium Looks The Same Live And Committed — `:271`
- Paint Load Depletes While Drawing And Not Only On Release — `:286`
- ALong Stroke Does Not Diverge More Than AShort One — `:335`
- Sampling Density Does Not Change Whether The Mark Matches — `:358`

## LiveMediumPixelTests
`tests/Lightbox.App.Tests/LiveMediumPixelTests.cs`

- AMedium Stroke Looks The Same Live As Committed — `:121`
- AWet Edge Is Visible Before The Pen Lifts — `:136`
- Paper Texture Is Visible Before The Pen Lifts — `:151`
- AMedium Stroke Is Not Just Flat Dabs — `:167`
- APlain Brush Does Not Pay For The Post Process — `:191`

## LivePreviewPixelTests
`tests/Lightbox.App.Tests/LivePreviewPixelTests.cs`

- Mid Stroke The Published Snapshot Shows The Line — `:44`
- Self Crossing Looks The Same Live And Committed — `:67`
- An Effect Stroke Looks The Same Live And Committed — `:123`

## LivePreviewTests
`tests/Lightbox.App.Tests/LivePreviewTests.cs`

- Batched Moves Produce One Stroke With All Points — `:10`
- Committed Pixels Match Direct Rasterization — `:30`
- Pointer Up Without Down Is Harmless — `:56`

## LivePreviewVisualTests _Category=Visual_
`tests/Lightbox.App.Tests/LivePreviewVisualTests.cs`

- What The Canvas Shows Mid Drag Against What It Keeps — `:89`
- Every Shipped Brush Draws Its Own Stroke — `:159`

## LiveSampleRebakeTests
`tests/Lightbox.App.Tests/LiveSampleRebakeTests.cs`

- ALive Smudge Follows An Edit To The Layer Under It — `:91`
- That Changes What The Layer Renders As — `:108`
- ABaked Smudge Does Not Follow — `:122`
- An Ordinary Document Is Not Touched — `:138`
- With Nothing Underneath The Sample Is Dropped Rather Than Kept Stale — `:156`
- AHand Drawn Baked Smudge Freezes What Was Under It — `:178`
- Rebaking Is Not An Undo Step — `:207`

## LiveToolPreviewTests
`tests/Lightbox.App.Tests/LiveToolPreviewTests.cs`

- Smudge Shows Mid Drag — `:56`
- The Smudge Preview Matches The Commit — `:84`
- Blur Shows Mid Drag — `:111`
- An Abandoned Smudge Leaves No Trace — `:143`
- ALive Blur Does Not Cover More Ground Than The Mark That Commits — `:214`
- The Pulling Brushes Ship With AFlow An Artist Can Steer — `:255`
- An Effect Brush Mid Drag Cannot Exceed The Wash It Started From — `:361`
- Abandoning An Effect Brush Does Not Block The Next Ordinary Stroke — `:412`
- Blur Ships With ARadius You Can Actually See — `:445`

## LongStrokeCostTests _Category=Performance_
`tests/Lightbox.App.Tests/LongStrokeCostTests.cs`

- APointer Event Stays In Budget Deep Into ALong Stroke — `:39`

## LoopRangeHandleTests
`tests/Lightbox.App.Tests/LoopRangeHandleTests.cs`

- Dragging The Start Handle Sets The Start Frame — `:87`
- Dragging The End Handle Sets The End Frame — `:100`
- The Bounds Cannot Cross — `:113`
- Dragging AHandle Does Not Scrub — `:128`
- Clicking Away From The Handles Still Scrubs — `:143`
- With No Range Yet The Handles Sit On The Whole Timeline — `:155`
- Alt Clicking AHandle Resets The Range — `:172`
- Alt Clicking Between Them Resets It Too — `:183`
- Alt Clicking Outside The Range Leaves It Alone — `:195`
- Alt Clicking Does Not Scrub Either — `:206`
- Right Clicking Asks For The Menu Rather Than Scrubbing — `:217`
- Reset Range Clears Both Bounds — `:231`

## MainViewModelTests
`tests/Lightbox.App.Tests/MainViewModelTests.cs`

- Paint Stroke Lands In Document — `:14`
- Paint On AHold Starts ANew Drawing — `:29`
- Paint On AHold Can Still Edit The Held Drawing — `:53`
- Frame Commands Keep Document And Cells Consistent — `:80`
- Undo Redo Round Trips Paint — `:96`
- Insert Inbetweens Fills Timeline — `:114`
- Snapshot Published On Paint And Navigation — `:145`
- Replace Document Resets State — `:165`
- Toggle Playback Flips State — `:181`
- Paint While Playing Is Ignored — `:192`

## MainWindowTests
`tests/Lightbox.App.Tests/MainViewModelTests.cs`

- Main Window Constructs And Shows — `:206`

## MarkerNotesTests
`tests/Lightbox.App.Tests/MarkerNotesTests.cs`

- AMarker With No Note Writes No Note Key — `:31`
- Writing ANote On An Unmarked Frame Makes The Marker — `:44`
- ANote Survives Renaming The Marker — `:60`
- Clearing The Text Removes The Note And Keeps The Marker — `:78`
- ANote Is Not An Event — `:93`
- The Event Flag Goes Back To Absent Rather Than False — `:104`
- Notes Are Listed In Frame Order — `:117`
- ANote Is One Undo Step — `:128`
- The Markers Can Be Walked Forwards And Backwards — `:141`
- Walking Past The Last Marker Stays Put Rather Than Wrapping — `:161`
- With No Markers Navigation Does Nothing — `:176`
- ATag Carries Prose Too — `:189`
- ATag With No Note Writes No Note Key — `:204`

## MediumPresetTests
`tests/Lightbox.App.Tests/MediumPresetTests.cs`

- AWet Stroke Keeps Pigment Down Its Middle — `:62`
- Every Medium Preset Has ATip And Some Variation — `:88`

## ExportTests
`tests/Lightbox.App.Tests/Milestone3Tests.cs`

- Export Png Sequence Writes One File Per Frame Resolving Holds — `:125`

## FpsTests
`tests/Lightbox.App.Tests/Milestone3Tests.cs`

- Fps Clamps And Persists To Scene — `:160`

## LayerTests
`tests/Lightbox.App.Tests/Milestone3Tests.cs`

- Add Vector Layer Becomes Active Painting Creates Vector Strokes — `:12`
- Inbetweens On Vector Layer Produce Vector Frames — `:37`
- New Layer Is Padded To Frame Count And Undoable — `:57`

## SmoothingTests
`tests/Lightbox.App.Tests/Milestone3Tests.cs`

- Smoothing On Reduces Spikes Preserves Endpoints — `:75`
- Smoothing Off Keeps Raw Points — `:91`

## ThumbnailTests
`tests/Lightbox.App.Tests/Milestone3Tests.cs`

- Keyed Cells Get Thumbnails Holds Do Not — `:107`

## DropColorFillTests
`tests/Lightbox.App.Tests/ModifiersAndDropFillTests.cs`

- Dropping AColour Fills And Adopts It — `:89`
- It Works Whichever Tool Is Selected — `:106`
- ALocked Layer Still Refuses It — `:119`

## TemporaryToolModifierTests
`tests/Lightbox.App.Tests/ModifiersAndDropFillTests.cs`

- Alt Held Erases With The Current Brush Without Switching Tools — `:23`
- Without Alt The Same Call Paints — `:38`
- The Eraser Tool Still Erases Even Without Alt — `:48`

## MoveToolAndModifierTests
`tests/Lightbox.App.Tests/MoveToolAndModifierTests.cs`

- Moving The Drawing Translates Its Strokes — `:41`
- Shift Holds The Move To One Axis — `:56`
- AWhole Move Is One Undo Step — `:71`
- AClick That Goes Nowhere Is Not An Edit — `:92`
- Ctrl Moves Every Drawing On The Layer — `:110`
- Moving One Drawing Leaves The Others Where They Are — `:136`
- Shift Click With The Brush Runs AStraight Segment From The Last Stroke — `:159`
- Shift Click Chains Into APolyline — `:177`
- With Nothing To Join To AShift Click Is An Ordinary Stroke — `:192`
- An Undo Takes The Join Anchor With It — `:204`
- Changing Frame Or Layer Takes The Join Anchor With It — `:217`
- Shift Snaps AGradient To An Angle Without Clamping Its Length — `:229`
- Shift Snaps ALine Shape To Forty Five Degrees — `:245`
- An Unconstrained Line Goes Exactly Where It Was Dragged — `:255`
- Shift Flips The Fills Sampling For One Click Only — `:264`
- ASoftware Backend Turns The Canvas Quality Down — `:280`
- AGpu Backend Changes Nothing — `:301`
- AChosen Quality Is Never Revised — `:319`
- Picking The Shape Tool Announces It So The Options Can Appear — `:343`
- Choosing AShape Selects The Shape Tool — `:359`
- AShape In Progress Is Shown While It Is Being Dragged — `:371`
- Creating ASingle File Reuses The Blank Document Already Open — `:388`
- It Never Reuses ADocument Somebody Has Drawn On — `:403`

## OnionTests
`tests/Lightbox.App.Tests/OnionTests.cs`

- Onion Settings Survive The Application Closing — `:83`
- Rearranging The Panels Leaves Onion Skin Alone — `:113`
- Opening ADocument Does Not Reset How Far Back You Can See — `:134`
- The Drawing Before This One Shows Through In Its Tint — `:147`
- Depth Decides How Far Back The Ghosts Reach — `:159`
- Falloff Makes The Further Ghost Fainter — `:174`
- Playback Shows The Animation Not The Ghosts — `:196`
- ACel That Exposes Nothing Still Shows Its Ghosts — `:210`
- Draw Over Puts The Ghost On Top Of The Current Drawing — `:244`
- ALight Table Dims The Other Layers And Drops The Time Ghosts — `:273`
- ALight Table Leaves The Paper Alone — `:300`
- APinned Frame Ghosts From Anywhere In The Sequence — `:320`
- APinned Frame Never Ghosts Itself — `:340`
- Unpinning And Clearing Put The Canvas Back — `:354`
- ADocument That Pins Nothing Writes No Pin Key — `:386`
- ALayer With Onion Off Contributes No Ghosts — `:400`

## OverflowBarTests
`tests/Lightbox.App.Tests/OverflowBarTests.cs`

- Everything Fits When There Is Room — `:15`
- Exactly Enough Room Still Fits — `:23`
- The Overflow Button Takes Its Own Room Once It Is Needed — `:31`
- An Item Too Wide For The Bar At All Is Pushed Into The Menu — `:41`
- An Empty Bar Overflows Nothing — `:49`
- The Order Is Kept — `:55`

## OverlayBarLayoutTests
`tests/Lightbox.App.Tests/OverlayBarLayoutTests.cs`

- Every Shortcut Tile Leaves Room For Its Glyph — `:79`
- The Shortcut Tiles Do Not Override The One Place That Sizes Them — `:102`
- The Zoom Readout Is Wide Enough To Read At Full Percent — `:122`
- The Zoom Readout Turns And Grows The Other Way On ASide Bar — `:142`
- The Readout Keeps The Tile Width Across The Bar — `:168`
- Changing The Zoom Keeps The Readouts Own Layout — `:182`

## PaletteDockerTests
`tests/Lightbox.App.Tests/PaletteDockerTests.cs`

- ANew Document Starts With Black And White Selected On Black — `:50`
- Adding ASwatch Takes The Current Colour And Is Undoable — `:68`
- Selecting ASwatch Paints With It — `:82`
- Choosing AColour Any Other Way Breaks The Swatch Link — `:106`
- Recolouring ASwatch Repaints The Art That Used It — `:127`
- Edit Mode Routes The Picker Into The Selected Swatch — `:150`
- ARun Of Colour Edits Is One Undo Step — `:165`
- Undoing AStructural Edit Does Not Swallow An Uncommitted Recolour — `:186`
- ASwatch Survives Undo With Its Identity — `:204`
- Removing ASwatch Leaves The Art In The Colour It Was Drawn In — `:228`
- Switching Documents Switches Palettes — `:251`
- Palettes Round Trip Through The Document With Their Links — `:271`
- Imported Gpl Becomes APalette On The Document — `:290`
- Exported Gpl Reads Back As The Same Palette — `:314`
- An Unparseable Hex Is Rejected Rather Than Painting Black — `:338`

## PaletteHierarchyTests
`tests/Lightbox.App.Tests/PaletteHierarchyTests.cs`

- ADocument With No Folders Has No Folder Machinery — `:69`
- Deleting The Last Folder Puts The Document Back To Having None — `:83`
- ANew Folder Lands Inside Whatever Is Selected — `:98`
- ANew Palette Lands In The Selected Folder — `:113`
- An Empty Folder Stays In The Tree — `:123`
- Renaming ARow Renames The Model — `:136`
- Dragging APalette Onto AFolder Files It — `:153`
- Dropping Onto APalette Means Beside It — `:167`
- AFolder Cannot Be Dropped Into Itself — `:181`
- Assign To Offers The Top Level And Every Folder By Its Path — `:195`
- Assign And Drag Agree Because They Are The Same Call — `:211`
- Deleting AFolder Keeps The Palettes In It — `:228`
- With AProject Open Both Scopes Get AHeading — `:254`
- The Project Opens With Its Hierarchy Already In Place — `:267`
- Nothing Crosses Between The Document And The Project — `:288`
- AFolder Made With AProject Row Selected Belongs To The Project — `:312`
- ASwatch Added To AProject Palette Lands In The Project — `:326`

## PaletteTests
`tests/Lightbox.App.Tests/PaletteTests.cs`

- Every Token The Design Names Resolves — `:45`
- The Core Surfaces Are The Colours The Design Gave — `:71`
- The Theme Agrees With The Palette — `:94`
- Nothing That Floats Keeps Fluents Grey — `:132`
- The Theme Palette Is Written In Hex On Purpose — `:155`
- No View Invents Its Own Chrome Colour — `:182`

## PresentedFrameTests
`tests/Lightbox.App.Tests/PresentedFrameTests.cs`

- ASmall Change Copies ASmall Patch — `:65`
- Patching Lands The Same Image AFull Repaint Would — `:95`
- ADropped Frames Change Is Still Owed And Arrives With The Next Patch — `:126`
- Not Being Told About ADropped Frame Is What Would Lose It — `:156`
- ARepaint With No New Publish Costs Nothing — `:181`
- The Seq Shortcut Still Honours An Owed Region — `:209`
- An Image Handed Out Survives Later Presents — `:249`
- Retirement Is Bounded Rather Than Unlimited — `:282`
- AResize Repaints In Full — `:300`
- Force Full Repaints Everything Even When ASmall Region Is Offered — `:327`

## ProjectCreateMenuTests
`tests/Lightbox.App.Tests/ProjectCreateAndRenameTests.cs`

- Every Create Entry Produces Something On Disk — `:132`
- The Create Menu Says Which Entries Are Folders — `:166`

## ProjectCreatePromptTests
`tests/Lightbox.App.Tests/ProjectCreateAndRenameTests.cs`

- Creating An Item Asks For Its Name First And Uses The Answer — `:56`
- Nothing Is Written To Disk If The Name Is Cancelled — `:79`
- With No Asker Attached The Suggested Name Is Used — `:103`

## RenameProjectItemTests
`tests/Lightbox.App.Tests/ProjectCreateAndRenameTests.cs`

- Renaming An Item Renames It On Disk — `:196`
- Renaming AFolder Moves It And Everything Under It — `:219`
- ARename That Would Collide Is Refused With Its Reason — `:250`
- AFolder Rename That Cannot Reach Disk Leaves Everything Alone — `:279`
- Renaming Something Never Saved Still Works — `:306`

## ProjectCreationTests
`tests/Lightbox.App.Tests/ProjectCreationTests.cs`

- No Unwanted Asset Folders Created — `:22`
- New Project Has Correct Default Structure — `:50`
- All Folders Appear At Project Root — `:75`
- New Project Folder Structure Is Correct — `:100`
- All Default Folders Are Listed In Project File — `:137`

## ProjectDeleteTests
`tests/Lightbox.App.Tests/ProjectDeleteTests.cs`

- Deleted Files Can Be Permanently Removed From Disk — `:49`
- Remove From Project Leaves The File Where It Is — `:77`
- Missing Files Are Tracked And Not Reloaded On Next Open — `:106`
- Empty Folders Are Deleted Without Prompt — `:133`
- Deleted Folders With Files Prompt For Confirmation — `:151`
- Deleting AFolder Takes Its Subtree And Files With It — `:178`
- Removing AFolder Keeps Its Documents In The Project — `:209`
- ADelete Cannot Escape The Project Folder — `:241`
- ASibling With AMatching Prefix Is Not Inside The Project — `:270`

## ProjectDockerTests
`tests/Lightbox.App.Tests/ProjectDockerTests.cs`

- The App Opens With No Project — `:92`
- With No Project ADocument Saves And Loads Exactly As Before — `:105`
- New Project Adopts The Document Already Open — `:121`
- The Docker Lists AFolders Documents Under It — `:148`
- Adding An Animation Opens It As ATab Bound To Its Slot — `:169`
- Opening An Animation Twice Focuses The Tab Rather Than Duplicating It — `:184`
- File New Makes AProject Document In One List — `:224`
- Two Animations Under One Character Paint From One Palette — `:252`
- Save Writes The Project Without APicker — `:290`
- Without AProject Or APath There Is Nothing To Save In Place — `:307`
- AProject Reopens With Its Characters And Animations — `:315`
- Removing An Animation Leaves Its File On Disk — `:339`
- The New Menu Offers One Entry Per Kind Of Thing — `:362`
- ADocument Created With Nothing Selected Belongs To The Project — `:383`
- ALoose Document Gets Its Own Row With No Character Above It — `:400`
- Moving ADocument To Another Character Repaths It And Keeps Its Id — `:415`
- Moving ADocument To Another Character Moves Its File Rather Than Copying It — `:453`
- Moving ADocument To The Project Takes It Out Of Every Folder — `:482`
- Moving ADocument Where It Already Is Does Nothing — `:498`
- AMoved Document Survives ASave And Reopen — `:508`
- Renaming ARow Writes Through — `:529`
- Every Row Knows Where It Is On Disk — `:545`
- With No Project There Is No Path To Show — `:566`
- Copy Path Gives The Selected Rows File — `:576`
- Opening Externally Says So When The File Is Not Written Yet — `:589`
- Duplicating An Animation Copies Its Art Into The Same Character — `:607`
- Duplicating Writes The Copy On The Next Save — `:642`
- Deleting AFolder On Disk Removes It From The Docker — `:672`
- The Docker Refreshes Without Being Reopened — `:704`
- An Unsaved Project Does Not Report Every Row As Missing — `:730`
- The Watch Follows The Project And Not The Application — `:769`
- ABurst Of Disk Events Costs One Refresh — `:804`
- ADeletion On Disk Reaches The Row Without ARefresh Call — `:859`
- ARefresh Keeps The Rows That Still Stand For The Same Thing — `:920`
- AManual Re Read Is Reachable And Reports What It Found — `:976`
- Creating An Item Asks For Its Name First — `:1037`
- The Suggested Name Matches The Numbered Fallback — `:1059`
- ABlank Name Falls Back Rather Than Creating An Unnamed Item — `:1076`
- The Unnamed Command Still Creates The Numbered Default — `:1093`
- The Project Root Is Visible In The Docker — `:1117`
- The Project Row Cannot Be Removed Renamed Or Deleted — `:1169`
- Show In File Manager Opens The Selected Item — `:1214`
- Selecting AFolder Survives ARebuild — `:1267`

## ProjectHierarchyTests
`tests/Lightbox.App.Tests/ProjectHierarchyTests.cs`

- AProject With No Folders Shows No Folder Rows — `:51`
- Subfolders Can Be Created Within Folders — `:61`
- Folders Can Be Collapsed And Expanded — `:85`
- Collapse Survives ARefresh — `:118`
- Folders Can Be Dragged Within Project — `:135`
- AFolder Cannot Be Dropped On Its Own Descendant — `:160`
- Documents Can Be Dragged Within Project — `:176`
- Documents Created In Folders Appear In Correct Folder — `:206`
- ADocument Made Beside Another Joins Its Folder — `:227`
- With Nothing Selected ADocument Still Goes To The Project Root — `:250`
- Folder Structure Reflects File System Hierarchy — `:273`
- The Tree Indent Reaches The Row Template — `:317`
- Moving ADocument Into AFolder Moves Its File Rather Than Copying It — `:363`
- Moving ADocument Back To The Root Moves Its File Too — `:399`
- AMove That Cannot Reach Disk Leaves The Tree Alone — `:430`
- ADocument Made In AFolder Is Named After It — `:471`
- Accepting The Offered Stem Drops The Dangling Separator — `:501`
- With No Folder Selected The Numbered Suggestion Stands — `:526`
- AFolder Is Not Offered An Animation Name — `:554`
- Expanding AFolder Makes It Where New Work Lands — `:596`
- Selecting On Toggle Does Not Stop The Toggle — `:632`
- APress Selects Any Kind Of Row — `:662`
- The Reveal Button Runs The Same Command As The Menu — `:695`

## ProjectWindowTests
`tests/Lightbox.App.Tests/ProjectWindowTests.cs`

- The Window And The Docker List The Same Documents — `:39`
- AFolder Is Followed By What Is In It — `:56`
- Setting AStatus On ASelection Changes Every Document In It — `:71`
- AFolder In The Selection Is Skipped By AStatus Edit — `:85`
- Tagging ASelection Takes Folders And Documents Alike — `:100`
- ABulk Edit That Changed Nothing Says So — `:116`
- An Edit Tells The Owner The Project Moved — `:130`
- The Selection Survives The Rebuild An Edit Causes — `:144`
- ATag Filter Keeps AFolder Only For What Is Under It — `:161`
- AFolder Tag Reaches The Documents Under It In The Filter Too — `:176`
- Clearing AFilter Brings Everything Back — `:191`
- The Status Board Narrows With The Filter — `:204`
- No Status Is Its Own Column And Comes Last — `:223`
- Dragging Between Columns Moves One Document — `:236`
- Renaming Somebody Fixes Every Row At Once — `:251`
- Removing Somebody Says What It Costs Before It Happens — `:269`
- Unassigning Is In The Same List As Assigning — `:286`
- Removing The Filtered Person Clears The Filter — `:304`
- The Assets Tab Shows All Three Levels At Once — `:322`
- AScope That Declares Nothing Says So — `:344`
- Sharing Something With AScope Declares It There — `:354`
- The First Declaration Of AKind Says That Scoping Is Now On — `:370`
- ADocument Can Declare Something Of Its Own — `:386`
- Taking Back The Last Declaration Says Everything Applies Again — `:402`
- Undeclaring Empties Back To Nothing — `:421`
- Something Already Shared Here Is Not Offered Twice — `:436`
- AChip Reads As AName Rather Than An Id — `:450`
- The Facet Editor Appears For Exactly One Folder — `:464`
- Notes Are Written Through And Empty Back To Nothing — `:482`
- APivot Is Given And Taken Away — `:495`
- Clearing AReading Names What It Costs First — `:509`
- The Reviewed Flag Can Finally Be Set — `:538`
- Removing AVariant Keeps The Art It Replaced — `:554`
- The Export Tab Shows What Would Be Written — `:575`
- The Export Tab Says What Is Held Back By Status — `:596`
- The Footer Counts What Is True — `:621`

## RecentItemsTests
`tests/Lightbox.App.Tests/RecentItemsTests.cs`

- The Newest Is First — `:27`
- Reopening Moves It To The Top Rather Than Adding It Again — `:35`
- The Path Is What Makes Two Entries The Same — `:46`
- ATrailing Separator Is Not ADifferent Project — `:58`
- The List Stops Growing — `:68`
- An Entry With No Name Is Named After Its File — `:83`
- Nothing Is Recorded For An Empty Path — `:96`
- Forgetting Removes One And Clearing Removes All — `:107`
- Only The Entries Still On Disk Are Offered — `:121`
- AProject Is AFolder And ADocument Is AFile — `:148`
- The List Survives Being Written And Read Back — `:173`

## ReferenceGridEditTests
`tests/Lightbox.App.Tests/ReferenceGridEditTests.cs`

- The Grid Mode Takes The Canvas Away From The Tools — `:48`
- Leaving The Mode Gives The Canvas Back And Drops The Selection — `:65`
- ABox Is Where The Compositor Puts It — `:85`
- Clicking Inside ABox Finds It — `:101`
- Moving AGroup Of Boxes Nudges Them And Leaves Their Window On The Sheet — `:120`
- ASlow Group Box Drag Still Arrives Where It Was Taken — `:147`
- AWhole Group Box Drag Is One Undo Step — `:160`
- AGroup Box Drag That Went Nowhere Is Not An Edit — `:180`
- Moving ABox Moves Only That One — `:195`
- Resizing ABox Shows More Of The Sheet Rather Than Scaling It — `:210`
- Dragging The Other Corner Moves The Origin Too — `:227`
- ABox Cannot Be Shrunk To Nothing — `:241`
- Deleting ABox Leaves The Sheet Alone And Relays The Rest — `:253`
- ABox Can Be Drawn By Hand — `:267`
- APivot Is Placed In Sheet Pixels So The Sheet Can Move Under It — `:287`
- Placing APivot Is Undoable — `:306`
- Generating Keyframes Grows The Timeline To Fit The Sheet — `:319`
- Generating Keyframes Registers The Cells On Their Pivots — `:337`
- Aligning Twice Changes Nothing — `:364`
- The Gizmos Are Absent Until The Mode Is On — `:384`
- AGizmo Sits Where Its Drawing Is — `:410`
- An Unplaced Pivot Still Gets AMark — `:436`
- Generating Keyframes Is One Undo Step — `:458`

## ReferenceImagePayloadTests
`tests/Lightbox.App.Tests/ReferenceImagePayloadTests.cs`

- Reference Views Are Encoded Once Not Per Call — `:96`
- The Encoded View Is Reused While The Sheet Is Unchanged — `:125`
- Editing The Drawing Throws The Encoded View Away — `:140`
- Hiding ALayer Changes What Is Sent — `:159`
- The Long Edge Is Capped On The Way Out Rather Than On The View — `:177`
- ASmall View Is Not Upscaled To The Cap — `:206`
- Two Tabs Whose Views Share An Id Do Not Share APicture — `:219`
- The Downscaled Reference Still Has The Drawing In It — `:284`

## CharacterSheetFileTests
`tests/Lightbox.App.Tests/ReferenceSheetTests.cs`

- ACharacter Sheet Outside AProject Prompts To Save — `:238`
- ACharacter Sheet In AProject Is Written On Creation — `:258`
- ACharacter Sheet Asks For Its Name Before Its Location — `:289`

## ReferenceAiTests
`tests/Lightbox.App.Tests/ReferenceSheetTests.cs`

- Render Reference View Produces Decodable Png — `:149`
- Ai Inbetween Carries Reference Images — `:172`
- Ipc List And Render Expose Reference Views — `:201`

## ReferenceSheetModelTests
`tests/Lightbox.App.Tests/ReferenceSheetTests.cs`

- Sheets Round Trip Through Json And Legacy Docs Load Empty — `:26`

## ReferenceTabTests
`tests/Lightbox.App.Tests/ReferenceSheetTests.cs`

- Add View Opens Reference Tab Timeline Hidden — `:67`
- Painting In Reference Tab Lands In Owning Document And Dirties Owner — `:83`
- Save From Reference Tab Serializes The Owning Document — `:110`
- Closing Owner Tab Closes Its Reference Tabs — `:126`
- Opening Same View Focuses Existing Tab — `:136`

## ReferenceStripTests
`tests/Lightbox.App.Tests/ReferenceStripTests.cs`

- Importing ASheet Finds Its Frames — `:91`
- Importing Extends The Timeline To Fit The Reference — `:101`
- Asking Not To Add Frames Leaves The Timeline Alone — `:115`
- An Oversized Sheet Is Scaled To Fit The Canvas — `:126`
- ASheet That Will Not Decode Is Refused Rather Than Crashing — `:141`
- ASheet Can Be Cut On ADeclared Grid Instead — `:150`
- Each Frame Shows Its Own Part Of The Sheet — `:165`
- The Reference Sits Over The Paper And Under The Drawing — `:180`
- Hiding The Reference Takes It Off The Canvas — `:197`
- AFrame With No Reference Shows Nothing — `:208`
- Nudging ACell Moves That Frame And Only That Frame — `:220`
- Nudging The Sheet Moves Every Frame Together — `:237`
- Scale Applies To Every Frame — `:250`
- Clearing Alignment Undoes Every Nudge — `:271`
- Alignment Is Undoable — `:285`
- Adding AFrame Moves The Reference Along With The Animation — `:300`
- AReference Pinned To Absolute Timing Stays Put — `:314`
- AReference Is Never Exported — `:329`
- Removing The Last Reference Leaves The Document With No Key For One — `:345`
- ADocument With No Reference Composites Exactly As It Always Has — `:358`
- AReference Survives Saving And Reopening — `:374`

## RenderReportTests
`tests/Lightbox.App.Tests/RenderReportTests.cs`

- The Report Says Why The Durable Frame Is Not On The Gpu — `:66`
- The Texture Limit Is Compared Against The Surface That Must Fit — `:105`
- AStartup Report Says It Has No Measurements Yet — `:125`
- The Startup Report Lands In The Diagnostics Folder And Names The Build — `:135`
- The Startup Report Is Written Only Once — `:153`
- ASilent Fallback To Cpu Is Reported Loudly — `:167`
- The On Demand Report Carries The Session Totals And The Saving — `:185`
- The Upload Probe Compares Two Presents Without AGpu — `:209`
- The Probe Refuses ADegenerate Surface Instead Of Throwing — `:231`
- An Unwritable Folder Is Survived Rather Than Thrown — `:242`
- The Presented Frame Counts Full Against Patched And Free Repaints — `:260`

## RigEditingTests
`tests/Lightbox.App.Tests/RigEditingTests.cs`

- With The Mode Off There Is Nothing To Draw — `:36`
- ADocument With No Rig Has Nothing Declared — `:53`
- Adding Declares And Places In One Step — `:66`
- AShape Dragged Out Backwards Arrives The Right Way Round — `:88`
- ADrag Lands On The Drawing Rather Than On The Frame Index — `:106`
- Dragging While Parked On AHold Edits The Drawing Being Held — `:127`
- Resizing AShape Moves Only The Corner That Was Grabbed — `:146`
- Pressing Selects What It Hit And Clears On Empty Canvas — `:163`
- Every Rig Edit Is One Undo Step — `:181`
- ADrag Of Nothing Records Nothing — `:198`
- Clearing Here Leaves It Declared So AHitbox Can Stop Partway Through ASwing — `:214`
- Deleting Takes The Declaration And Every Placement Of It — `:235`
- Pushing Across Visits Every Drawing Once Even On2s — `:252`
- Pushing With Nothing Selected Does Nothing — `:271`

## RigMarkEditTests
`tests/Lightbox.App.Tests/RigMarkEditTests.cs`

- Moving AGroup Of Anchors Is One Undo Step — `:78`
- Undoing An Anchor Group Move Puts Them All Back — `:105`
- Redoing An Anchor Group Move Reapplies It Once — `:127`
- An Anchor Group Drag That Went Nowhere Is Not An Edit — `:144`
- Moving AGroup Of Collision Boxes Is One Undo Step — `:161`
- Undoing ACollision Box Group Move Puts Them All Back — `:182`
- Moving ACollision Box Leaves Its Size Alone — `:197`

## RigOverlayPainterTests
`tests/Lightbox.App.Tests/RigOverlayPainterTests.cs`

- The Rig Overlay Reaches The Canvas — `:89`
- Only The Selected Shape Shows Handles — `:119`
- An Anchors Cross Is Screen Sized Rather Than Document Sized — `:144`
- An Anchor Inside AShape Is Still Visible — `:165`
- Rig Edit Mode Is Bindable — `:205`
- The Canvas Is Handed The Marks — `:238`
- APress In Rig Mode Selects AMark Instead Of Painting — `:284`

## RigOverlayTests
`tests/Lightbox.App.Tests/RigOverlayTests.cs`

- ASelected Shapes Corner Beats Its Own Body — `:25`
- An Unselected Shape Has No Corners To Grab — `:39`
- An Anchor Inside AShape Is Still Reachable — `:53`
- The Smaller Shape Wins When One Is Inside Another — `:70`
- APress On Nothing Hits Nothing — `:85`
- AHandle Stays The Same Size To The Hand At Every Zoom — `:97`
- An Anchor Has AMore Forgiving Target Than AHandle — `:111`
- An Impossible Zoom Does Not Divide By Zero — `:124`
- Moving AShape Never Resizes It — `:134`
- Resizing Leaves The Opposite Corner Exactly Where It Was — `:145`
- Each Corner Moves Only Its Own Two Edges — `:160`
- Dragging ACorner Past Its Opposite Flips Rather Than Going Negative — `:178`
- AShape Cannot Be Collapsed To Nothing — `:193`
- An Anchor Moves Even When ACorner Is Somehow Named — `:204`
- The Cursor Says What The Gesture Will Do — `:220`

## RigPreviewTests
`tests/Lightbox.App.Tests/RigPreviewTests.cs`

- Anchors Follow The Pointer While The Group Is Being Dragged — `:40`
- Collision Boxes Follow The Pointer While The Group Is Being Dragged — `:61`
- The Preview Leaves The Marks It Was Given Alone — `:85`
- Clearing The Preview Goes Back To What The Record Says — `:99`
- With No Drag In Flight The List Is Handed Back Untouched — `:114`
- An Empty Or Absent Overlay Previews Nothing — `:125`
- ADragged Corner Previews AResize Rather Than AMove — `:138`
- AGroup Preview Is Measured From Where The Drag Was Picked Up — `:156`
- Anchors And Boxes Measure From Their Own Press — `:178`
- AGroup Preview With No Selection Manager Does Nothing — `:210`
- An Anchor Ignores The Corner Because It Has No Size — `:220`

## RulerAndGuideEditTests
`tests/Lightbox.App.Tests/RulerAndGuideEditTests.cs`

- Ticks Stay On Round Numbers At Every Zoom — `:60`
- Zooming In Gives AFiner Ruler — `:78`
- AStrip Maps Both Ways Consistently — `:85`
- AMirrored View Gives ABackwards Ruler Rather Than None — `:94`
- The Rulers Are Absent Until Asked For — `:107`
- Turning Them On Insets The Canvas And Back Off Returns It — `:119`
- Dragging Out Of The Top Ruler Leaves AHorizontal Guide — `:144`
- Dragging Out Of The Left Ruler Leaves AVertical Guide — `:160`
- AGuide Lands Where The Pointer Was On The Other Axis — `:173`
- Letting Go Back On The Ruler Throws The Guide Away — `:193`
- The Draft Is Drawn While The Guide Is Being Placed — `:212`
- AGuide Is Only Grabbable With The Move Tool — `:234`
- Locking Them Stops The Grab Without Hiding Anything — `:260`
- Hiding Guides Takes Them Off The Canvas And Out Of Reach — `:276`
- AHidden Guide Still Constrains The Stroke — `:292`
- AGuide Is Moved By Grabbing It On The Canvas — `:309`
- AGrab Misses With ADrawing Tool In Hand — `:333`
- AWhole Drag Of AGuide Is One Undo Step — `:352`
- ALocked Guide Does Not Budge — `:371`
- Dragging ASelected Group Of Guides Moves Every One The Full Distance — `:392`
- Guides Follow The Pointer While The Group Is Being Dragged — `:414`
- AWhole Group Guide Drag Is One Undo Step — `:427`
- ALocked Guide In The Selection Does Not Budge — `:455`
- AGroup Guide Drag That Went Nowhere Is Not An Edit — `:473`
- Each Straight Guide Is Marked On The Ruler It Crosses — `:489`
- AGrid Is Not Marked On The Rulers — `:504`
- The Rulers Track The Pointer Over The Canvas — `:517`
- The Configure Window Lists The Grids On The Document — `:537`
- Changing The Default Pitch Does Not Touch AGrid Already Placed — `:547`
- Editing APlaced Grid Is Undoable — `:560`
- Turning AGrids Snapping Off Leaves The Stroke Alone — `:574`

## SaveAndStatusGateTests
`tests/Lightbox.App.Tests/SaveAndStatusGateTests.cs`

- Save And Save As Are Both Registered With The Keys An Artist Expects — `:59`
- The Two Save Keys Do Not Collide With Each Other Or With The Select Tool — `:74`
- Both Are Rebindable Which Is The Whole Point Of Registering Them — `:90`
- The Menu Shows The Gesture It Will Actually Respond To — `:101`
- Neither Save Item Carries AHard Coded Gesture Any More — `:114`
- An Unsaved Document Is What The Gate Sees For The Document In Front — `:135`
- AStatus Change Asks About Its Own Row Rather Than The Active Tab — `:150`
- ARow With No Document At All Is Not Treated As Saved — `:169`

## SaveRequirementTests
`tests/Lightbox.App.Tests/SaveRequirementTests.cs`

- ADocument That Was Never Saved Has To Be Asked About — `:21`
- ASaved And Unchanged Document Is Ready To Go — `:29`
- Edits The File Does Not Have Are Written Without Asking — `:37`
- AFile That Is No Longer There Is Not The Same As Never Saved — `:49`
- Only The Two Gates That Need An Answer Block — `:62`
- An Explanation Says What Was Being Attempted — `:73`
- AReady Document Has Nothing To Explain — `:84`
- The Two Blocking Gates Give Different Reasons — `:90`

## PlaybackEvictionTests
`tests/Lightbox.App.Tests/ScanEvictionTests.cs`

- Playing Switches The Cache To Scan Eviction — `:141`

## ScanEvictionTests
`tests/Lightbox.App.Tests/ScanEvictionTests.cs`

- An Lru Scan Throws Away Everything It Is About To Need — `:68`
- Evicting The Most Recent Keeps Half The Sheet Resident — `:80`
- Scan Eviction Is Better Than Lru On AScan — `:91`
- The Frame Being Shown Is Never The One Evicted — `:100`
- Drawing Keeps The Lru It Wants — `:126`

## SceneDockerTests
`tests/Lightbox.App.Tests/SceneDockerTests.cs`

- AProject That Ordered Nothing Shows No Running Order — `:58`
- The First Folder Brings The Reorder Buttons With It — `:71`
- AFolder Can Carry AReading And An Order At Once — `:85`
- AFolder Nobody Labelled Uses The Plain Glyph And Writes No Key — `:110`
- The Glyph Is Whatever The Artist Picked — `:121`
- AGlyph Outside The Grid Is Accepted — `:136`
- Clearing The Glyph Takes The Key Back Out Of The File — `:151`
- ADocument Row Never Wears Its Folders Glyph — `:168`
- An Ordinary Folder Says Nothing About What It Carries — `:185`
- The Details Panel Lists Facets And Names No Kind — `:197`
- AHand Corrected Reading Says So In The Details — `:220`
- Documents Are Indented Under Their Folder — `:236`
- ADocument With No Folder Selected Belongs To The Project — `:250`
- ADocument Opens As ATab — `:264`
- AFolder Row Shows How Long It Runs — `:277`
- An Empty Folder Says Nothing Rather Than Zero — `:292`
- Folders Move Up And Down And The Selection Follows — `:305`
- Documents Move Within Their Folder — `:328`
- Reordering ALoose Document Does Nothing — `:347`
- Deleting AFolder Keeps Its Documents As Loose Ones — `:364`
- Converting Changes The Type And Recreates No Artwork — `:384`
- Converting Does Not Rearrange The Screen By Itself — `:402`
- Converting Tells The Artist What Changed — `:420`
- Converting With No Project Open Does Nothing — `:433`

## ScopeDeclarationTests
`tests/Lightbox.App.Tests/ScopeDeclarationTests.cs`

- Sharing APalette Onto AFolder Scopes The Project — `:43`
- Sharing The Same Palette Twice Is One Declaration — `:71`
- Promoting ADeclaration Reaches The Whole Project — `:86`
- Recolouring Repaints Every Open Document That Uses The Swatch — `:112`
- Undoing ARecolour Of AProject Palette Restores It — `:160`
- AGradient Can Be Shared With AFolder — `:196`
- Guides Can Be Shared With AFolder — `:215`
- ADefault Template Replaces Rather Than Accumulating — `:233`
- ADrawing Can Be Made Reference For Its Neighbours Or For Everything — `:274`
- Unpublishing Leaves The Record As An Ordinary Declaration — `:320`
- ADeclaration Says What It Is And Can Be Undone — `:338`
- ADeclaration Pointing At Nothing Still Shows And Still Clears — `:367`
- Declaring ASymbol Narrows The Grid For Other Folders — `:395`
- ASymbol Declaration Says What It Is And Undoes To Unscoped — `:436`
- Declaring ABrush Tip Narrows The Projects Own And Not The User Library — `:472`
- ATip Declaration Says What It Is And Undoes To Unscoped — `:515`

## SelectionAdjustTests
`tests/Lightbox.App.Tests/SelectionAdjustTests.cs`

- Shrinking The Whole Canvas Pulls In From All Four Edges — `:54`
- An Edge Touching Selection Shrinks On The Edge It Touches — `:74`
- Shrink And Grow Leave The Selection Where It Was — `:93`
- ACircle Shrinks By The Same Amount On Every Side — `:113`
- Growing Pushes Out On Every Side Too — `:129`
- Shrinking Past Nothing Leaves No Selection Rather Than An Inside Out One — `:145`

## SelectionVariantTests
`tests/Lightbox.App.Tests/SelectionVariantTests.cs`

- The Hold List Switches The Variant — `:37`
- The Tool Options Bar Follows The Variant — `:65`
- Clicking ARadio In The Bar Switches The Variant Too — `:91`

## ShortcutMapTests
`tests/Lightbox.App.Tests/ShortcutMapTests.cs`

- Defaults Cover The Core Commands Without Duplicates — `:12`
- Conflict Detection Finds The Other Command — `:29`
- Assign With Unbind Steals The Gesture — `:41`
- Overrides Persist And Reload — `:51`
- Corrupt Store Falls Back To Defaults — `:69`

## ShortcutRegistrationTests
`tests/Lightbox.App.Tests/ShortcutRegistrationTests.cs`

- Every Gesture In The Xaml Is Also In The Shortcut Registry — `:69`
- The Exemption List Describes Gestures That Are Actually There — `:90`
- An Exempted Gesture Stops Being Exempt Once It Is Registered — `:105`

## SimulatedMediumTests
`tests/Lightbox.App.Tests/SimulatedMediumTests.cs`

- The Simulated Watercolour Actually Deposits Pigment — `:76`
- Layering AWash Deepens It — `:105`
- ABody Medium Stays Opaque — `:126`
- Pigment Density Reads Like AConcentration — `:142`

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

## SnapshotRetirementTests
`tests/Lightbox.App.Tests/SnapshotRetirementTests.cs`

- Publishes That Outrun The Renderer Never Free An Image Still In Flight — `:37`
- Once Rendered The Old Frames Are Still Released — `:77`

## SplashScreenTests
`tests/Lightbox.App.Tests/SplashScreenTests.cs`

- The Splash Is Orange — `:23`
- The Splash Takes Its Colour From One Place — `:33`
- The Splash Looks Like ASplash Rather Than AWindow — `:49`

## SpriteSheetExportTests
`tests/Lightbox.App.Tests/SpriteSheetExportTests.cs`

- Trimming Defaults To The Union So Every Cell Is The Same Size And Nothing Jitters — `:65`
- The Union Covers Every Frames Ink — `:90`
- Per Frame Trimming Records Where Each Cell Came From — `:105`
- No Trim Gives Every Cell The Whole Canvas — `:121`
- The Grid Holds Every Frame And The Sheet Is That Size — `:131`
- Every Cell Actually Contains Its Frame — `:146`
- Padding Leaves ATransparent Gutter Without Losing Ink — `:171`
- Without APivot The Sidecar Carries None — `:184`
- The Pivot Is Recorded Per Cell So Trimming Cannot Shift The Character — `:193`
- The Sidecar Is Aseprite Shaped — `:221`
- An Opaque Background Layer Does Not Defeat Trimming — `:249`
- An Empty Document Still Produces ASheet — `:262`
- The Grid Is Still The Default And Its Bytes Are Unchanged — `:278`
- APacked Sheet Is Smaller Than The Grid On Ragged Frames — `:299`
- APacked Sheet Reports No Grid Rather Than APlausible One — `:333`
- The Sidecar Carries Every Sprites Own Rect — `:348`
- Packing The Same Document Twice Produces The Same File — `:383`
- APacked Sheet Still Carries The Pivot Per Cell — `:398`
- Padding Still Separates Every Sprite When Packed — `:422`
- ADocument With No Anchors Writes No Anchor Key — `:443`
- An Anchor Is Exported Per Frame By Name And Inside The Cell — `:450`
- An Anchor On AHeld Drawing Is Exported On Every Frame It Shows — `:475`
- Packing Does Not Move An Anchor Relative To Its Cell — `:494`
- The Default Export Is Byte Identical To Before Background Handling Existed — `:571`
- AFlooded Layer Is Omitted Under Detection And Kept Without It — `:593`
- APinned In Layer Survives Detection Even Though It Fills The Canvas — `:626`
- APinned Out Layer Goes Even Under Paper Only — `:642`
- ALayer That Fills The Canvas On One Frame Only Is Not ABackground — `:657`
- AHeld Flood Is Still Recognised Across Its Holds — `:689`
- Everything Puts The Paper Back In — `:705`
- ALayer Named Like ABackground Is Reported Rather Than Removed — `:728`
- AHidden Layer Is Reported So Its Absence Has An Answer — `:744`
- ADocument With No Shapes Writes No Shape Key — `:759`
- AShape Is Exported With Its Role And Inside The Cell — `:766`
- AShape Only Appears On The Frames It Was Placed On — `:807`
- AShape On AHeld Drawing Is Exported On Every Frame It Shows — `:827`
- Packing Does Not Move AShape Relative To Its Cell — `:844`
- ADocument With No Tags Or Events Writes Neither Key — `:868`
- ATag Is Exported As AClip In The Established Shape — `:878`
- ATag That Ran Past The End Is Shortened Rather Than Lost — `:905`
- ATag Entirely Past The End Is Dropped — `:920`
- Only Markers Marked As Events Are Exported — `:931`
- An Event Past The End Is Not Exported — `:952`
- Several Documents Concatenate In The Order They Are Given — `:970`
- Each Document Becomes ATag So An Engine Can Tell The Clips Apart — `:984`
- ASingle Document Sheet Still Writes No Tags When Nothing Is Tagged — `:1007`
- Tags And Events On ALater Document Move To Where Its Frames Landed — `:1018`
- Every Frame Keeps Its Own Documents Pivot — `:1042`
- The Untrimmed Cell Holds The Largest Canvas — `:1065`
- Each Frames Duration Comes From Its Own Documents Fps — `:1085`
- One Documents Paper Layer Cannot Silence Anothers Art — `:1100`
- ASheet Needs At Least One Document — `:1125`

## StartScreenTests
`tests/Lightbox.App.Tests/StartScreenTests.cs`

- Escape Leaves ABlank Document Rather Than Nothing — `:36`
- Dont Show Again Is Remembered And Can Be Turned Back On — `:53`
- Offering The Screen Does Nothing When It Is Turned Off — `:68`
- New File Uses The Values The Screen Collected — `:82`
- Opening ARecent Document Opens It — `:99`
- AFile That Has Moved Says So Rather Than Doing Nothing — `:120`
- Opening ADocument Puts It In The Recents — `:138`
- Saving Somewhere New Records It Too — `:161`
- Clearing The List Empties It On Disk As Well — `:184`
- Only What Is Still On Disk Is Offered — `:207`

## StartupHandoffTests
`tests/Lightbox.App.Tests/StartupHandoffTests.cs`

- The Main Window Is Up Before The Splash Goes Down — `:72`
- The Splash Is Never The Window The Application Adopts — `:90`
- The Application Adopts The Main Window Before It Is Shown — `:103`
- The Start Screen Is Offered Only After The Splash Has Gone — `:114`

## StartupTimingTests
`tests/Lightbox.App.Tests/StartupTimingTests.cs`

- An Instant Load Still Shows The Splash For The Minimum — `:19`
- APart Loaded Splash Waits Out The Remainder — `:27`
- ASlow Load Is Not Padded Further — `:35`
- The Minimum Is Long Enough To Be Seen And Short Enough Not To Be Waited On — `:44`

## StrokeLatencyTests
`tests/Lightbox.App.Tests/StrokeLatencyTests.cs`

- The First Frame Of ABurst Is Not Stale — `:133`
- APen Burst Is One Frame Not One Per Event — `:180`
- When The Burst Has Drained The Mark Reaches The Pen — `:212`
- Smoothings Own Lag Is Measured Separately — `:246`

## SubjectReadingWiringTests
`tests/Lightbox.App.Tests/SubjectReadingWiringTests.cs`

- Reading ACharacter Keeps The Answer On It — `:72`
- AReading Somebody Edited Is Not Overwritten By ARe Read — `:89`
- Without AProject It Says So Rather Than Failing Quietly — `:110`
- With No Sheet To Read It Says What Is Missing — `:122`
- An Inbetween On ACharacters Animation Carries Its Taxonomy — `:142`
- ADocument With No Character Sends No Taxonomy At All — `:177`

## SymbolBrowserTests
`tests/Lightbox.App.Tests/SymbolBrowserTests.cs`

- With No Project There Is Nothing To Browse — `:73`
- The Symbols Panel Is In The Catalogue And Can Be Toggled — `:86`
- Opening AProject Fills The Browser — `:98`
- The Kind Filter Is What The Six Libraries Are — `:115`
- No Filter Shows Everything — `:128`
- Search Matches AName Or ATag — `:141`
- Search And Kind Narrow Together — `:157`
- An Empty Grid Says Which Kind Of Empty It Is — `:170`
- Every Tile Gets AThumbnail — `:184`
- Making ASymbol Takes The Drawing And Leaves APlacement — `:195`
- Making ASymbol Does Not Change What The Drawing Looks Like — `:210`
- Making ASymbol Is One Undo Step — `:228`
- Making ASymbol From Nothing Says So — `:242`
- Without AProject There Is Nowhere To Put ASymbol — `:251`
- An Unnamed Symbol Still Gets AName — `:261`
- Placing The Selected Symbol Puts It On The Drawing — `:272`
- Place Goes To The Middle And ADrop Goes Where It Was Dropped — `:285`
- Placing With Nothing Selected Does Nothing — `:305`
- Deleting ASymbol Leaves Its Placements Alone And They Stop Drawing — `:315`
- Renaming ARow Renames The Symbol — `:336`
- Renaming Does Not Count As Editing The Drawing — `:347`
- Tags Edit As One Line — `:361`

## SymbolEditingTests
`tests/Lightbox.App.Tests/SymbolEditingTests.cs`

- Opening ASymbol Makes ATab For It — `:71`
- The Tab Edits The Symbols Own Frames Rather Than Copies — `:83`
- Opening The Same Symbol Twice Focuses The Tab It Already Has — `:95`
- ACycle Opens With ACel Per Frame — `:107`
- ASymbol Tab Has No Paper Behind It — `:119`
- ASymbol Tab Is Not Something To Save As AFile — `:131`
- An Empty Symbol Still Opens — `:143`
- Editing ASymbol Changes Every Placement Of It — `:157`
- An Edit Bumps The Version — `:177`
- Editing An Animation Does Not Bump Any Symbol — `:191`
- Adding ACel In The Symbol Tab Adds AFrame To The Symbol — `:205`
- Changing The Symbols Fps Sticks To The Symbol — `:219`
- The Browser Tile Follows The Edit — `:231`
- Editing The Selected Symbol Opens It — `:245`
- APlacement Made Before An Edit Is Reported As Outdated — `:259`
- APlacement Made After The Edit Is Not Outdated — `:280`
- The Report Counts Placements And Names Symbols — `:294`
- Acknowledging Quietens The Report Without Changing The Drawing — `:310`
- Acknowledging Is An Undo Step — `:331`
- Acknowledging Nothing Is Not An Edit — `:348`

## SymbolLibraryTests
`tests/Lightbox.App.Tests/SymbolLibraryTests.cs`

- The Library Round Trips With Its Drawings — `:55`
- ACorrupt Library Is An Empty One Rather Than ACrash — `:72`
- No Library Yet Is Not An Error — `:79`
- The Grid Shows Both Scopes And Badges The Global Ones — `:87`
- The Scope Filter Narrows To One Library — `:103`
- An Adopted Library Symbol Appears Once As AProject Symbol — `:121`
- Placing ALibrary Symbol Copies It Into The Project — `:142`
- Dragging ALibrary Symbol Onto The Canvas Copies It Too — `:159`
- APlaced Library Symbol Still Renders With The Library Deleted — `:176`
- Promoting Puts AProject Symbol In The Library On Disk — `:195`
- AGlobal Row Cannot Be Promoted — `:211`
- The Project Is Offered ANewer Library Version And Takes It On Asking — `:223`
- With Nothing Newer There Is Nothing To Offer — `:245`
- The Library Is There With No Project Open — `:260`
- Placing One In ALoose Document Copies It Into The Document — `:277`
- Project Scope Stays Project Only — `:298`
- With No Project The Empty Message Does Not Tell You To Do Something Impossible — `:316`

## SymbolPlacingTests
`tests/Lightbox.App.Tests/SymbolPlacingTests.cs`

- Placing ASymbol Puts It On The Cel — `:65`
- APlacement Records What It Was Placed Against — `:77`
- Placing Is One Undo Step — `:88`
- Placing An Unknown Symbol Does Nothing And Says So — `:101`
- ALocked Layer Refuses APlacement — `:113`
- Removing APlacement Leaves The Symbol Alone — `:122`
- Removing Is One Undo Step And Keeps The Order — `:134`
- APlacement Can Be Found Under The Pointer — `:151`
- Empty Canvas Under The Pointer Finds Nothing — `:160`
- The Topmost Placement Wins — `:169`
- The Move Tool Grabs APlacement Before The Drawing — `:183`
- The Move Tool Still Moves The Drawing Away From APlacement — `:195`
- Dragging APlacement Moves It — `:208`
- Shift Holds The Placement To One Axis — `:222`
- Moving APlacement Is One Undo Step — `:236`
- Moving APlacement Does Not Touch The Symbol — `:253`
- AClick That Went Nowhere Is Not An Edit — `:272`
- Cancelling AMove Puts It Back — `:285`
- Dragging ASelected Group Moves Every One The Full Distance — `:309`
- ASelection Makes The Move Tool Move It Wherever The Drag Starts — `:339`
- Moving AGroup Is One Undo Step — `:357`
- Shift Holds AGroup To One Axis — `:386`
- ALocked Layer Refuses To Move ASelected Group — `:405`
- AGroup Drag That Went Nowhere Is Not An Edit — `:422`
- Cancelling AGroup Move Puts Them All Back — `:443`
- Moving AGroup Does Not Touch The Symbol — `:468`
- Breaking The Link Leaves Ordinary Strokes — `:489`
- Broken Strokes Keep Their Swatch — `:506`
- Breaking The Link Is One Undo Step — `:526`
- Breaking One Link Leaves Other Placements Linked — `:540`
- AScaled Placement Bakes Its Size Into The Brush — `:553`

## SymbolScopeTests
`tests/Lightbox.App.Tests/SymbolScopeTests.cs`

- With No Project Open Nothing Resolves — `:32`
- AProjects Symbols Resolve Once The Project Is Open — `:46`
- Closing AProject Stops Its Symbols Resolving — `:60`
- Deleting ASymbol Stops It Resolving — `:78`

## TabSwitchCrashTests
`tests/Lightbox.App.Tests/TabSwitchCrashTests.cs`

- Switching Back And Forth Between Tabs Does Not Crash — `:62`
- The Other Pair In The Report Behaves The Same Way — `:87`
- The Docker Showing After ASwitch Is The One In The Strip — `:103`

## TemplateUiTests
`tests/Lightbox.App.Tests/TemplateUiTests.cs`

- With No Project There Is Nothing To Offer And Nothing To Pull — `:30`
- ADocument That Is Not ATemplate Writes No Template Keys — `:44`
- Marking ADocument As ATemplate Is Just AFlag — `:60`
- AMarked Document Appears In The List — `:78`
- New From Template Opens ACopy In Its Own Tab — `:93`
- Editing The Template Afterwards Leaves The Copy Alone — `:113`
- Switching Tabs Changes The Answer — `:146`
- ADocument That Did Not Come From ATemplate Cannot Be Asked — `:171`
- ACopy Can Be Asked And Reports Nothing When It Matches — `:181`
- ANew Layer In The Template Is Offered And Arrives — `:192`
- APull Is One Undo Step — `:207`
- APull That Changes Nothing Leaves No Undo Step To Press Through — `:225`
- APull Never Touches The Artists Drawings — `:237`
- ALayer The Artist Drew On Is Reported As Such And Skipped — `:253`
- The Dialog Turns The Preview Into Ticks And Back — `:276`
- The Dialog Offers Only What Actually Differs — `:296`
- The Dialog Lists ADrawn On Layer And Defaults It Off — `:314`
- Cancelling Returns Nothing — `:334`

## TimelineBugTests
`tests/Lightbox.App.Tests/TimelineBugTests.cs`

- Onion Ghosts Show Over The Paper — `:38`
- Adding AFrame Holds The Paper Rather Than Blanking It — `:70`
- Painting With No Key At The Playhead Creates One — `:90`
- Filling With No Key At The Playhead Creates One — `:107`
- Keying By Drawing Is Undoable — `:128`
- Redrawing ACleared Cel Brings The Thumbnail Back — `:149`
- Delete Cel Ripples The Rest — `:173`
- Delete Cel Leaves Other Layers Alone — `:205`

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

## TimelineHoldTests
`tests/Lightbox.App.Tests/TimelineHoldTests.cs`

- AFilled Hold Becomes ADrawing Of Its Own — `:23`
- The Held Drawing Is Left Alone — `:40`
- Keying Is ASeparate Undo Step From The Mark — `:54`
- An Ordinary Keyed Cel Is Still Drawn On Directly — `:74`
- Playback Wraps By Default — `:88`
- With Looping Off It Stops On The Last Frame — `:101`
- The Ruler Pitch Follows The Frame Width — `:127`
- The Frame Width Stays Within Readable Bounds — `:141`

## CelRangeSelectionTests
`tests/Lightbox.App.Tests/TimelineRangeAndPressureTests.cs`

- Shift Click Selects ARange And Plain Click Clears It — `:26`
- Copy Range Preserves Holds And Paste Replays Them — `:44`
- Cut Range Clears Every Cel In The Range In One Undo Step — `:68`
- Move Cel Refuses Cross Layer Drops — `:88`
- Markers Add Edit Remove Are Undoable And Feed The Ruler — `:103`

## PressureVmTests
`tests/Lightbox.App.Tests/TimelineRangeAndPressureTests.cs`

- Master Switch Writes Into The Stroke Record — `:127`
- Per Setting Checkboxes Map To The Response Curves — `:141`

## TimelineRowAlignmentTests
`tests/Lightbox.App.Tests/TimelineRowAlignmentTests.cs`

- ALayers Name And Its Cels Share One Row — `:35`

## TimingPresetUiTests
`tests/Lightbox.App.Tests/TimingPresetUiTests.cs`

- The Picker Offers The Built Ins And Starts On Something — `:52`
- Applying To ASelected Range Retimes The Whole Range — `:65`
- With No Range It Applies To The Cel Alone — `:79`
- Retiming Is One Undo Step From The View Model — `:93`
- The Ruler Follows The New Length — `:109`
- ARange Of Nothing Says So Rather Than Silently Doing Nothing — `:127`
- The Status Line Says Which Way The Length Went — `:145`
- ASaved Pattern Persists And Comes Back On The Next Launch — `:166`
- The Built Ins Are Not Written To The Store — `:185`
- ATyped Pattern That Will Not Parse Is Refused With AReason — `:200`
- Saving The Same Name Twice Replaces It Rather Than Adding ASecond — `:214`
- ABuilt In Name Gets Its Own Entry Rather Than Overriding The One — `:230`
- Deleting Removes It From The List And The File — `:245`
- ABuilt In Cannot Be Deleted — `:259`
- ACorrupt Store Leaves The Built Ins — `:271`
- The Cel Menu Names The Pattern The Bar Has Chosen — `:283`

## BrushTipsWindowTests
`tests/Lightbox.App.Tests/TipLibraryTests.cs`

- Generating ATip Puts It In The Library As Pixels — `:179`
- Only The Controls The Shape Actually Reads Are Shown — `:197`
- An Empty Library Says So Rather Than Showing Nothing — `:217`
- The Library Lists What Is In The Store — `:230`
- ABuilt In Cannot Be Deleted Or Renamed — `:251`
- Editing ACopy Loads The Recipe Without Touching The Original — `:269`
- The Preview Bakes Small However Big The Output Is — `:286`

## TipLibraryTests
`tests/Lightbox.App.Tests/TipLibraryTests.cs`

- ALibrary Round Trips — `:30`
- ACorrupt Library Is Empty Rather Than Fatal — `:44`
- AProject Tip Comes Before AUser Tip — `:54`
- With No Project The Library Is Just The Users Own — `:73`
- Painting With ALibrary Tip Copies It Into The Drawing — `:92`
- Deleting From The Library Cannot Change ADrawing — `:105`
- AProject That Never Made ATip Writes No Tips Key — `:121`
- AProject Tip Survives Save And Reload — `:142`

## ToolBarAlignmentTests
`tests/Lightbox.App.Tests/ToolBarAlignmentTests.cs`

- Every Value Field In The Bar Is The Same Width — `:34`
- Every Slider In The Bar Has The Same Track Length — `:52`
- No Value Field In The Bar Sets AWidth Of Its Own — `:67`
- Every Group In The Bar Shares One Vertical Centre — `:83`
- Nothing In The Bar Asks For More Height Than The Bar Has — `:104`
- Every Tile In An Overlay Bar Is The Same Square — `:123`
- The Brush Parameter Flyout Is Not Pinned To One Height — `:163`
- Deleting The Paper Leaves Transparency Rather Than White — `:180`
- Putting The Paper Back Is Undo And The Document Is Opaque Again — `:198`
- Deleting An Ordinary Layer Does Not Touch The Paper — `:212`

## ToolOptionsBarTests
`tests/Lightbox.App.Tests/ToolOptionsBarTests.cs`

- The Parameters Button Sits Right Of The Brush Button — `:56`
- The Parameters Button Survives Switching To The Eraser — `:84`
- The Parameters Button Is Gone For Tools That Do Not Paint — `:100`
- The Effect Strength Control Is Bound To Flow — `:114`

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

## TransformPreviewTests
`tests/Lightbox.App.Tests/TransformPreviewTests.cs`

- Transform Preview Moves The Pixels — `:50`
- The Preview Is Not An Edit — `:66`
- Cancelling Puts The Pixels Back — `:85`
- What The Preview Showed Is What Apply Produces — `:99`
- With ASelection Only The Selected Strokes Move — `:116`

## TransformToolTests
`tests/Lightbox.App.Tests/TransformToolTests.cs`

- Begin Transform Reports The Stroke Bounds And Commit Moves The Points — `:45`
- Mirror Commit Flips Around The Pivot Without Moving — `:71`
- Perspective Commit Maps The Corners Exactly — `:85`
- Degenerate Perspective Is Refused And The Session Survives — `:105`
- Cel Range Scope Transforms Each Distinct Drawing Once — `:118`
- Entire Animation Scope Moves Every Layer — `:138`
- Selection Region Limits The Transform To Strokes Inside It — `:157`
- Empty Scope Refuses To Start — `:177`

## UnboundedCanvasPixelTests
`tests/Lightbox.App.Tests/UnboundedCanvasPixelTests.cs`

- Tiled Rendering Stroke Across Tile Boundary Has No Visible Seam — `:50`
- Tiled Rendering Multiple Tiles Stroke Maintains Relative Position — `:81`
- Tiled Rendering With Viewport Offset Strokes Render Correctly — `:117`
- Tiled Rendering Multiple Renders Produces Consistent Output — `:152`
- Tiled Rendering Large Document Renders Tiles Correctly — `:202`

## UnityExportTests
`tests/Lightbox.App.Tests/UnityExportTests.cs`

- The Generic Sidecar Keeps Every Key It Already Had — `:62`
- An Ordinary Export Still Has No Unity Block — `:79`
- Every Sprite Gets ARect And The Count Matches The Frames — `:88`
- The Rects Are The Ones The Sheet Exporter Wrote — `:99`
- AFeet Pivot Arrives As Bottom Centre Normalised — `:122`
- An Anchor Is Converted The Same Way As The Pivot — `:153`
- Pixels Per Unit Follows The World Size Asked — `:170`
- Seconds Per Frame Is Exact Rather Than Rounded — `:180`
- ADocument With No Shapes Has No Collider Key — `:189`
- AHurtbox Below The Feet Pivot Arrives With ANegative YOffset — `:196`
- Trimming Cannot Move ACollider — `:238`
- With No Pivot The Collider Is Measured From The Cell Centre — `:271`
- ACollider Is Offset And Size Rather Than ARect — `:303`
- The Importer Exposes Colliders Without Trying To Apply Them — `:326`
- Slicing Goes Through The Data Provider On Any Modern Unity — `:341`
- Every Sliced Sprite Gets Its Own Id — `:372`
- AMissing Sprite Package Is Reported Rather Than Crashing — `:381`
- Each Tag Becomes AClip — `:392`
- An Event Is Timed From Its Own Clip Rather Than From The Sheet — `:412`
- An Event Outside AClip Is Not Attached To It — `:431`
- AMarker That Is Not An Event Never Reaches AClip — `:443`
- The Importer Is Written Beside The Sheet — `:457`
- An Edited Importer Is Not Overwritten — `:470`
- No Meta File Is Ever Written — `:484`
- The Importer Can Be Declined — `:495`
- Exporting Twice Produces The Same Sidecar — `:505`

## UnreadSettingsTests
`tests/Lightbox.App.Tests/UnreadSettingsTests.cs`

- ASetting The Engine Ignores Is Not Offered To The Artist — `:54`
- No Shipped Brush Sets ASetting The Engine Ignores — `:73`
- The Unread List Does Not Outlive The Settings On It — `:98`
- ASetting That Was Implemented Is Not Still Listed As Unread — `:113`

## UnrealExportTests
`tests/Lightbox.App.Tests/UnrealExportTests.cs`

- No Asset File Is Written Because Lightbox Cannot Write One — `:73`
- The Generic Sidecar Keeps Every Key It Already Had — `:83`
- The Block Carries Only What The Generic File Lacks — `:98`
- The Pixels Per Unit Is Unreals Centimetre Figure And Not Unitys — `:111`
- Frame Runs Are Counts Of Frames And Never Zero — `:126`
- ADocument With No Pivot Carries No Pivot Points — `:138`
- AFeet Pivot Arrives Relative To The Cell And Inside It — `:148`
- The Sidecar Names The Assets So The Script Never Has To — `:171`
- An Untagged Sheet Still Gets Exactly One Flipbook Name — `:185`
- Each Tag Becomes AFlipbook Name In Tag Order — `:195`
- Two Tags With The Same Name Do Not Collapse Into One Asset — `:212`
- An Edited Importer Is Not Overwritten — `:232`
- The Importer Can Be Suppressed — `:243`
- Every Property Write Goes Through The Helper That Cannot Fail Silently — `:255`
- AFailed Property Write Is Reported As An Error And Not Just Logged — `:276`
- The Script Builds Assets Through Unreals Own Api — `:289`
- The Script Only Claims Sidecars That Are Ours — `:303`
- The Script Turns Mips Off Because That Is What Bleeds An Atlas — `:312`
- The Script Sets The Region After Creation Because The Factory Takes No Texture — `:324`
- The Script Says Why It Is AScript At All — `:338`
- The Script Does No Name Cleaning Of Its Own — `:349`
- The Shipped Script Is Structurally Intact Python — `:364`
- And It Actually Catches Each Of Those Mistakes — `:380`
- An Unreal Preset Writes The Sheet The Sidecar And The Script — `:471`
- The Runner Passes The World Height Through Rather Than Defaulting It — `:485`
- An Unreal Preset Still Reports What It Left Out — `:502`
- The World Height Field Is Offered For Unreal And Not Only Unity — `:514`

## UnsavedDocumentTests
`tests/Lightbox.App.Tests/UnsavedDocumentTests.cs`

- ANew Document Is Not On Disk Until It Is Saved — `:65`
- An Unsaved Document Is Shown As Pending In The Docker — `:87`
- ADeleted File Is Missing Rather Than Pending — `:137`
- Discarding An Unsaved Document Removes It From The Docker — `:169`
- ANew Document Appears In The Project Docker — `:214`
- AProject Save Writes ADocument Made With File New — `:247`
- Closing ANever Saved Document Takes Its Row With It — `:274`
- Closing ASaved Document Keeps Its Row — `:304`
- Closing ADocument Whose File Vanished Keeps Its Row — `:334`
- ANew Document With No Project Open Changes Nothing — `:361`
- Saving ADocument Outside The Project Takes It Out Of The Project — `:409`
- Saving ADocument Into The Project Repaths Its Row — `:450`

## ViewportCullingPerformanceTests
`tests/Lightbox.App.Tests/ViewportCullingPerformanceTests.cs`

- APointer Event In ACulled Viewport Repaints The Dab Not The Viewport — `:50`
- Culling Does Not Enlarge The Clip AStroke Implies — `:99`
- AWhole Canvas Publish Still Composes Only The Viewport — `:139`
- An Incremental Publish After ACulled One Does Not Show The Previous Frame — `:191`

## AutosaveSettingsTests
`tests/Lightbox.App.Tests/WorkspaceStoreTests.cs`

- The Default Is Every Minute To The Recovery Copy Only — `:267`
- Zero Turns Autosave Off — `:278`
- An Absurd Interval Is Clamped Rather Than Honoured — `:286`
- Settings Round Trip And Survive Corruption — `:295`

## WorkspaceStoreTests
`tests/Lightbox.App.Tests/WorkspaceStoreTests.cs`

- The Shipped Arrangements Tab The Colour Tools — `:15`
- Tabbing Offers More Panels Without Spending More Height — `:30`
- No Arrangement Tabs Two Panels An Artist Needs At Once — `:44`
- Every Project Type Has ABuilt In Workspace — `:65`
- The Built Ins Differ From Each Other — `:80`
- Saving Under ANew Name Adds AWorkspace And Selects It — `:93`
- Saving Over Your Own Workspace Replaces It — `:106`
- Saving Over ABuilt In Forks It Instead — `:121`
- Only Your Own Workspaces Can Be Deleted — `:137`
- Deleting The Selected Workspace Selects Another — `:148`
- AStore Round Trips And Gains Built Ins It Predates — `:160`
- ACorrupt Store Falls Back Rather Than Throwing — `:178`
- Applying AWorkspace Replaces The Layout And Clears The Star — `:188`
- Reset Goes Back To What The Workspace Says — `:203`
- Taking AProject Types Defaults Switches Workspace — `:220`
- Only Saved Workspaces Offer ABin — `:233`
- The Label Marks AWorkspace The User Has Since Rearranged — `:244`

## WorkspaceTests
`tests/Lightbox.App.Tests/WorkspaceTests.cs`

- Panels Land In The Strip The Layout Names — `:73`
- Moving APanel Moves The Control — `:85`
- An Empty Edge Collapses And AFilled One Opens — `:97`
- Closing APanel Parks It Rather Than Destroying It — `:119`
- Tabbed Panels Share One Slot And One Shows — `:138`
- The Tab Showing Is The One That Looks Like It Is Showing — `:161`
- An Untabbed Docker Looks Exactly As It Did — `:257`
- Grouping APanel Marks The Workspace Unsaved — `:270`
- The Project Panel Appears As Soon As There Is AProject — `:285`
- The Canvas Gets The Room Left Over By The Strips — `:308`
- The Project Row Menu Actually Does Something When Clicked — `:332`
- The New Menu Actually Makes Things — `:469`
- The Reference Panel Is Absent Until It Is Asked For — `:541`
- ACapped Strip Is No Wider Than Its Panels Can Use — `:557`

## AnchorTests
`tests/Lightbox.Core.Tests/AnchorTests.cs`

- ADocument With No Anchors Carries No Anchor Keys — `:24`
- Removing The Last Anchor Leaves The Document As It Was — `:33`
- Deleting ADeclaration Clears Its Positions Too — `:49`
- Setting Across ARange Touches Every Drawing In It — `:69`
- AHeld Drawing Is Visited Once Rather Than Twice — `:86`
- ARange Starting On AHold Still Sets The Drawing It Shows — `:110`
- Clearing Across ARange Removes It And The Key With It — `:125`
- ANon Range Is ANo Op — `:144`
- An Anchor Travels With Its Drawing When The Timing Changes — `:154`
- An Anchor Round Trips Through The File — `:174`
- Resolving At AFrame Reads Through Holds — `:191`
- An Upper Layer Wins When Two Place The Same Anchor — `:207`
- No Declarations Means Nothing To Resolve — `:223`

## AnimationTagTests
`tests/Lightbox.Core.Tests/AnchorTests.cs`

- An Untouched Marker Writes No Event Key — `:236`
- ADocument With No Tags Writes No Tag Key — `:252`
- ATag Round Trips With Its Direction And Loop — `:258`
- ATags Length Counts Both Ends And Survives Being Backwards — `:275`
- AMarker Marked As An Event Round Trips — `:285`

## BackgroundRulesTests
`tests/Lightbox.Core.Tests/BackgroundRulesTests.cs`

- ADocument That Never Pins ALayer Writes No Key — `:23`
- Pinning ALayer Out Beats Every Mode — `:31`
- Pinning ALayer In Survives Detection And The Paper Flag — `:46`
- AHidden Layer Is Reported Rather Than Just Absent — `:59`
- APinned Out Layer Is Reported As Pinned Even When Hidden — `:70`
- Paper Only Drops The Paper Layer And Nothing Else — `:83`
- Detected Also Drops AFull Canvas Fill — `:98`
- Everything Keeps The Paper Layer — `:110`
- AName That Reads Like ABackground Is Advisory And Never Acted On — `:119`
- Nothing Is Suspected When It Was Already Omitted Or Pinned In — `:133`
- Name Matching Is Whole Word And Case Insensitive — `:157`
- The Pin Round Trips Through The File — `:163`
- The Threshold Leaves Room For ASoft Edge Without Letting ADrawing Through — `:183`

## BrushCostTests
`tests/Lightbox.Core.Tests/BrushCostTests.cs`

- An Ordinary Brush Is Fast — `:18`
- ASimulated Medium Is Expressive — `:26`
- Smudge And Blur Are Expressive — `:35`
- Sampling Other Layers Is Expressive — `:44`
- Jitter And Scatter And Texture Are Not Expensive — `:53`
- Turning The Medium Off Makes It Fast Again — `:80`
- The Reason Names Every Cause So It Can Be Acted On — `:92`

## BrushDynamicsSerializationTests
`tests/Lightbox.Core.Tests/BrushDynamicsSerializationTests.cs`

- ACurve Round Trips Through The File — `:33`
- ACurve Means The Same Thing After ALoad As Before — `:52`
- ABlend Mode Round Trips — `:72`
- ABrush That Uses Neither Writes Neither Key — `:80`
- AFile Written Before Curves Existed Loads And Paints The Same — `:91`

## BrushScopeTests
`tests/Lightbox.Core.Tests/BrushScopeTests.cs`

- Work With AHouse Style Keeps The Brush With The Project — `:21`
- Work You Move Through In One Pass Keeps One Brush For The Tool — `:32`
- With No Project It Is What The Application Always Did — `:42`
- AProject That Never Asks For This Writes No Brush Key — `:51`
- ARemembered Brush Survives ASave And Reload — `:73`
- Every Document In The Project Is Fed The Same Brush — `:102`
- Remembering ABrush Changes No Pixel In The Record — `:128`

## CameraTests
`tests/Lightbox.Core.Tests/CameraTests.cs`

- No Camera Frames The Whole Scene Dead Centre — `:30`
- No Keys Frames The Whole Scene Dead Centre — `:37`
- One Key Is AStatic Framing — `:44`
- Outside The Authored Range The Camera Holds Rather Than Drifting — `:57`
- Pan And Roll Interpolate Linearly Between Keys — `:71`
- Zoom Interpolates Geometrically So APush Holds AConstant Rate — `:80`
- Each Easing Shapes The Move — `:101`
- AZero Or Negative Zoom Falls Back To One Rather Than Dividing The Framing Away — `:109`
- Keys Out Of Order Still Interpolate In Timeline Order — `:116`
- Set Key Replaces The Key Already On That Frame — `:130`
- Clear Key Removes Only That Frame — `:144`
- ANew Scene Has No Camera — `:156`
- ADocument Without ACamera Serializes With No Camera Key At All — `:163`
- Adding Then Removing ACamera Returns The Document To Its Original Bytes — `:175`
- ANew Scene Has No Pivot And The File Has No Pivot Key — `:191`
- APivot Round Trips And Defaults To Feet Centre — `:199`
- AShot Document Carries No Pivot And ASprite Document No Camera — `:213`
- ACamera Round Trips Through Save And Load — `:226`

## CharacterVariantTests
`tests/Lightbox.Core.Tests/CharacterVariantTests.cs`

- Character Documents Are In The Project — `:70`
- AFolder Palette Reaches ACharacters Animation — `:83`
- AFolder Nobody Varied Carries No Variant Keys — `:99`
- AVariant Copies The Palette Keeping Every Swatch Id — `:113`
- Recolouring AVariant Leaves The Base Subject Alone — `:127`
- Selecting AVariant Switches Which Palette The Subject Paints With — `:139`
- AVariant Inherits Every Document It Does Not Override — `:152`
- An Overridden Document Replaces Only Itself — `:168`
- AVariants Own Art Is An Ordinary Document In The Project — `:188`
- Variants Round Trip With Their Palettes — `:210`
- Order Is Partial And The Rest Sort By Name — `:229`
- An Order Id Whose Document Is Gone Is Skipped — `:244`
- Only Asset Library Projects Offer Their Subjects — `:256`
- AFolder With No Reading Is Offered Too — `:272`
- Scanning Ignores Folders That Are Not Projects — `:287`
- Importing ASubject Brings Its Documents And Palette — `:296`
- An Imported Subject Still Paints From Its Palette — `:315`
- Importing Copies Rather Than Links — `:334`
- Importing Carries Variants And Rebases Their Overrides — `:351`
- Importing Twice Gives Two Distinct Folders — `:376`
- Clearing AReading Names Everything It Would Discard — `:396`
- AHand Corrected Reading Is Named As Such — `:411`
- An Ordinary Folder Loses Nothing — `:424`

## CiDraftGateTests
`tests/Lightbox.Core.Tests/CiDraftGateTests.cs`

- Every Job Is Switched Off For ADraft Pull Request — `:74`
- Marking ADraft Ready Triggers ABuild — `:102`
- AMerge To Main Still Builds — `:123`

## CiRuntimeTests
`tests/Lightbox.Core.Tests/CiRuntimeTests.cs`

- The Job That Runs Tests Asks For The Runtime Those Tests Need — `:79`
- The Sdk Is Still Named Too — `:101`
- The Retired Eight Point Zero Runtime Has Not Come Back — `:122`

## CollisionShapeTests
`tests/Lightbox.Core.Tests/CollisionShapeTests.cs`

- ADocument With No Shapes Carries No Shape Keys — `:31`
- The Centre Is Not Serialized Alongside The Rectangle — `:42`
- Removing The Last Shape Leaves The Document As It Was — `:57`
- Deleting ADeclaration Clears Its Rectangles Too — `:71`
- Setting Across ARange Touches Every Drawing In It — `:89`
- AHeld Drawing Is Visited Once Rather Than Twice — `:109`
- AHitbox Is Active Only Where It Is Placed — `:130`
- Clearing Across ARange Removes It And The Key With It — `:148`
- ANon Range Is ANo Op — `:165`
- AShape Travels With Its Drawing When The Timing Changes — `:176`
- AShape Round Trips Through The File — `:196`
- ARole Defaults To Hurtbox Because That Is What An Artist Draws First — `:213`
- The Centre Is Where AColliders Offset Will Be Measured From — `:221`
- Resolving At AFrame Reads Through Holds — `:231`
- An Upper Layer Wins When Two Place The Same Shape — `:247`
- No Declarations Means Nothing To Resolve — `:262`
- Anchors And Shapes Do Not Interfere With Each Other — `:268`

## DensifyTests
`tests/Lightbox.Core.Tests/DensifyTests.cs`

- ACurve Is Followed Rather Than Cut Across — `:51`
- The Ends Are The One Place It Cannot Help — `:67`
- Every Recorded Point Is Still On The Path — `:80`
- ADrawn Corner Stays Sharp — `:96`
- AStraight Line Is Not Bent — `:120`
- Pressure Rides The Same Curve — `:130`
- ATwo Point Stroke Is Left Exactly As It Is — `:151`
- Points Already Closer Than The Chord Are Not Multiplied — `:161`
- AStalled Pen Does Not Break The Curve — `:179`
- The Same Points Always Give The Same Path — `:199`

## ExportPlanTests
`tests/Lightbox.Core.Tests/ExportPlanTests.cs`

- AWhole Project Export Includes Character Animations — `:64`
- AWhole Project Export Includes AVariants Own Art — `:94`
- One Artifact Packs The Whole Subtree Into One File — `:114`
- Per Child Folder Shares Settings And Splits The Output — `:135`
- ANearer Declaration Makes Its Own Artifact — `:152`
- The Status Filter Holds Work Back And Says So — `:173`
- An Empty Artifact Is Reported Rather Than Skipped — `:191`
- An Unscoped Project Exports Per Document — `:205`
- Staleness Catches Edits Additions And Removals — `:222`
- Reopening An Animation Makes The Sheet Stale Not Empty — `:254`
- Reaching Ready Rebuilds The Artifact Holding It — `:278`
- APreset With No Trigger Never Fires — `:299`

## ExportScopeTests
`tests/Lightbox.Core.Tests/ExportScopeTests.cs`

- AProject That Declares No Preset Is Not Scoped — `:26`
- The Nearest Preset Wins — `:35`
- AScope Exports One Way — `:48`
- The Declaring Folder Is The Artifact Boundary — `:66`
- APreset With No Filter Admits Everything — `:86`
- AFiltered Preset Admits Only The Statuses It Lists — `:97`
- Staleness Is Two Integers Differing — `:114`
- Grouping Defaults To One Artifact Per Document — `:127`

## FeatureConflictTests
`tests/Lightbox.Core.Tests/FeatureConflictTests.cs`

- Unbounded Canvas Excludes Fixed Frame Bounds Export — `:11`
- Conflict Reason Is Descriptive — `:24`
- No Conflict When Both False — `:38`
- No Conflict When First False — `:49`
- No Conflict When Second False — `:60`
- Conflict Involves Is Both Orders — `:71`
- Conflict Other Returns The Other Feature — `:85`
- Conflicts With Returns All Conflicts For Feature — `:98`
- Conflicts With Empty For Feature With No Conflicts — `:108`
- AConflict Holds In Every Project Type — `:116`
- All Returns All Declared Conflicts — `:133`

## FeatureDefaultsTests
`tests/Lightbox.Core.Tests/FeatureDefaultsTests.cs`

- Every Feature Is Reachable In Every Project Type — `:11`
- AProject Type Sets Defaults Rather Than Availability — `:30`
- AFeature Left At Its Default Writes No Key — `:49`
- Illustration Defaults Are Minimal — `:66`
- Animation Defaults Include Exposure Sheet — `:76`
- Game Art Defaults Include Fixed Frame Bounds Export — `:85`
- Storyboard Defaults Include Camera — `:93`
- Asset Library Defaults Include Fixed Frame Bounds Export — `:101`
- Get Default Throws For Unknown Type — `:107`
- Features For Returns All Features For Type — `:115`
- Types For Returns All Types With Feature — `:124`
- Layers Is Enabled In Every Project Type — `:132`

## FeatureIntegrationTests
`tests/Lightbox.Core.Tests/FeatureIntegrationTests.cs`

- Document Inherits Project Defaults — `:21`
- Get Feature Resolves Default — `:34`
- Document Can Override Default — `:46`
- Features Only Serialized When Changed — `:61`
- Conflict Detection For Sprite Export — `:80`
- Detect Conflict Between Unbounded And Fixed — `:94`
- No Conflict When Unbounded Disabled — `:116`
- All Project Types Have Consistent Defaults — `:135`
- Conflict Is Documented In Feature Key — `:152`
- Feature Overrides Serialize To Json As Strings — `:172`
- Unbounded Canvas Conflicts With Fixed Frame Export — `:195`

## FigureFinderTests
`tests/Lightbox.Core.Tests/FigureFinderTests.cs`

- Touching Diagonally Is Still One Thing — `:49`
- ABlob Is Bounded By What It Actually Covers — `:62`
- AWatermark Is Too Small To Be ADrawing — `:77`
- ATitle Banner Is Not ARow Of Frames — `:95`
- AFigure Drawn In Several Pieces Counts Once — `:113`
- APiece Separated From Every Other Row Becomes Its Own Row — `:137`
- ASheet With Only One Row Keeps It — `:158`
- An Empty Sheet Finds Nothing Rather Than One Huge Frame — `:170`
- Detection Cuts An Even Atlas Into Equal Cells — `:178`
- The Banner Is Gone From The Cells Too — `:192`
- Two Rows Become Two Bands Of Cells — `:211`
- Cells In ARow Share Their Top And Height Even When The Poses Do Not — `:226`
- ACell With No Pivot Assumes The Middle Of Its Foot — `:244`
- APlaced Pivot Is Absolute So Resizing The Cell Does Not Move It — `:255`

## GameMakerConvertTests
`tests/Lightbox.Core.Tests/GameMakerConvertTests.cs`

- AStrip Is Named With Its Own Frame Count — `:20`
- The Name Survives ARound Trip Through The Reader — `:27`
- Punctuation Game Maker Rejects Is Gone From The Name — `:41`
- AStrip Cannot Claim Zero Frames — `:52`
- AName With No Suffix Reads As ASingle Frame — `:60`
- AName That Only Looks Like AStrip Is Not One — `:69`
- It Reads The Last Suffix When An Earlier One Is Part Of The Name — `:78`
- The Origin Is Pixels From The Cells Top Left — `:90`
- Game Maker And Unreal Happen To Agree About The Pivot And This Is Where That Is Recorded — `:99`
- AFeet Origin Is The Larger YBecause Game Makers YRuns Down — `:113`

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

## GodotConvertTests
`tests/Lightbox.Core.Tests/GodotConvertTests.cs`

- An Ordinary Frame Is One Tick Of The Animations Speed — `:19`
- Rounding Recovers The Value The Milliseconds Were Rounded From — `:29`
- Passing Milliseconds Straight Through Would Be Wildly Wrong — `:46`
- AMissing Duration Falls Back To One Rather Than Zero — `:63`
- An Impossible Frame Rate Does Not Divide By Zero — `:72`
- APivot At The Centre Needs No Offset — `:81`
- AFeet Pivot Pushes The Drawing Upward — `:90`
- Godots YRuns Down Like Ours So There Is No Flip Here — `:102`
- It Measures From The Cells Centre Rather Than The Canvas Centre — `:116`
- ADegenerate Cell Is Treated As One Pixel — `:130`
- ABlank Tag Name Gets AUsable Placeholder — `:139`

## GradientAndTemplateScopeTests
`tests/Lightbox.Core.Tests/GradientAndTemplateScopeTests.cs`

- AGradient On AFolder Reaches Every Document Under It — `:34`
- AGradient On One Character Is Not Visible To Another — `:47`
- ANew Document In AScope Starts From That Scopes Template — `:69`
- Setting ATemplate Twice Leaves One Declaration — `:92`
- Gradients Palettes References And Templates Do Not Collide — `:106`

## GradientTests
`tests/Lightbox.Core.Tests/GradientTests.cs`

- The Ends Are The Stops Themselves — `:24`
- Interpolation Is In Linear Light Not Srgb — `:32`
- AMulti Stop Ramp Hits Each Stop Exactly — `:43`
- Coincident Stops Are AHard Edge — `:60`
- Stops Out Of Order Still Ramp In Position Order — `:79`
- Spread Decides What Happens Off The Ends — `:101`
- Alpha Interpolates As Coverage Not As Light — `:109`
- An Empty Gradient Is Transparent Rather Than ACrash — `:126`
- Gradients Round Trip Through The Document — `:133`
- ANew Document Has No Gradients — `:152`
- With No Alpha Track The Colour Stops Carry Their Own Alpha — `:160`
- An Alpha Track Overrides The Colour Stops Alpha — `:180`
- Opacity And Colour Change At Their Own Positions — `:197`
- One Alpha Stop Holds Its Value Everywhere — `:225`
- An Alpha Track Round Trips Through The Document — `:234`
- AGradient With No Alpha Track Writes No Alpha Key — `:250`

## GuideScopeTests
`tests/Lightbox.Core.Tests/GuideScopeTests.cs`

- AGuide Set On AFolder Reaches Every Document Under It — `:45`
- AGuide Set On One Character Is Not Visible To Another — `:60`
- APublished Guide Set Reaches Every Document — `:76`
- AProject With No Guide Sets Writes No Key — `:95`

## GuideTests
`tests/Lightbox.Core.Tests/GuideTests.cs`

- AGrid Pulls To Its Intersections — `:35`
- APoint Outside Tolerance Is Left Alone — `:46`
- ATilted Grid Still Snaps — `:56`
- ALine Pulls Perpendicularly Onto Itself — `:70`
- AVanishing Point Pulls To Itself — `:79`
- AGuide That Does Not Snap Is Ignored — `:89`
- AHidden Guide Still Snaps — `:100`
- The Nearest Guide Wins When Two Are In Range — `:114`
- AStroke Does Not Lock Until It Has Committed To ADirection — `:125`
- AStroke Locks To The Guide It Is Heading Along — `:136`
- Drawing Backwards Along ARuler Is Still Drawing Along It — `:146`
- AStroke Across Every Guide Locks To None — `:156`
- An Isometric Guide Offers Three Axes — `:164`
- AConstrained Stroke Keeps Its Length And Loses Its Wobble — `:179`
- AVanishing Points Direction Depends On Where You Are Standing — `:190`
- AStroke Is Held On The Ray From The Vanishing Point — `:202`
- An Isometric Stroke Is Held On Whichever Axis It Meant — `:215`
- ADocument With No Guides Writes No Guide Key — `:232`
- Guides Survive ASave And Reload — `:242`
- No Guides Means No Snapping — `:264`

## BoxEdgeMatchTests
`tests/Lightbox.Core.Tests/Inbetween/BoxEdgeMatchTests.cs`

- Each Edge Pairs With Its Own Edge — `:46`
- The Midway Frame Is Still ABox — `:70`
- The Match Is Minimum Total Cost Rather Than Cheapest First — `:87`

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
- Geometric Matching Picks Nearest Centroids — `:29`
- Length Mismatch Inflates Cost — `:42`
- Unmatched Pair With Null — `:52`
- Empty Frames Produce Only One Sided Pairs — `:60`

## IncrementalDensifyTests _Category=Performance_
`tests/Lightbox.Core.Tests/IncrementalDensifyTests.cs`

- Every Prefix Matches Densify Exactly — `:52`
- Only The Tail Is Recomputed When APoint Is Appended — `:75`
- ARewritten Tail Invalidates Far Enough Back — `:97`
- AShorter List Is Handled Rather Than Indexed Past — `:116`
- Under Three Points Behaves Like Densify — `:137`
- An Append Costs AFraction Of Re Densifying — `:199`

## NormalMapTests
`tests/Lightbox.Core.Tests/NormalMapTests.cs`

- The Distance Field Is Zero Outside And Grows Inward — `:40`
- Both Sweeps Are Needed And The Second One Runs — `:62`
- The Left Edge Faces Left And The Right Edge Faces Right — `:80`
- Green Is Bright At The Top Under Open Gl And At The Bottom Under Direct X — `:99`
- The Two Conventions Differ Only In Green — `:130`
- The Flat Interior Points Straight Out And The Outside Is Flat Not Black — `:155`
- Alpha Is Carried Through So The Map Masks Like The Sprite — `:176`
- AWider Bevel Tilts Further From The Edge — `:191`
- More Strength Tilts Further Without Moving The Bevel — `:223`
- ARounded Edge Has No Crease Where AChamfer Does — `:240`
- The Same Silhouette Gives Byte Identical Output — `:274`
- The Preview Light Is Not Baked Into The Map — `:286`
- ABevel From The Silhouette Needs No Dependency — `:311`
- ADegenerate Size Is Not ACrash — `:332`

## OnionSkinTests
`tests/Lightbox.Core.Tests/OnionSkinTests.cs`

- On Ones The Neighbours Are The Frames Either Side — `:41`
- On Threes The Neighbours Are Still The Drawings Either Side — `:52`
- ADrawing Is Never Ghosted Against Itself — `:63`
- The Same Drawing Is Never Ghosted Twice — `:74`
- Steps Count Drawings Not Frames — `:83`
- Keys Only Walks Keyed Cels And Ignores Holds — `:96`
- Keys Only Standing On AHold Finds The Keys Not The Held Drawing — `:104`
- Before And After Are Asked For Separately — `:114`
- Zero Depth Ghosts Nothing — `:125`
- The Ends Of The Sequence Simply Run Out — `:131`
- Falloff Halves Each Further Ghost — `:141`
- AFalloff Of One Makes Every Ghost Equally Visible — `:149`

## PaletteScopeTests
`tests/Lightbox.Core.Tests/PaletteScopeTests.cs`

- AProject That Declares No Scopes Is Not Filtered — `:42`
- APalette On One Character Is Not Visible To Another — `:60`
- Two Animations Under One Folder Paint From One Palette — `:83`
- AProject Palette Reaches Every Document — `:96`
- ADocument The Project Does Not Know Is Not Filtered — `:112`
- AStroke Painted From APalette Records Which One — `:130`

## PaletteTests
`tests/Lightbox.Core.Tests/PaletteTests.cs`

- Swatches Get Stable Identities — `:27`
- APalette Round Trips Through The Document — `:36`
- ANew Document Starts With Black And White — `:55`
- Gpl Round Trips Names And Colours — `:70`
- Gpl Writes The Header Other Tools Look For — `:85`
- Gpl Reading Is Forgiving Of Real Files — `:100`
- AMulti Word Swatch Name Survives — `:110`
- Garbage Lines Are Skipped Rather Than Throwing — `:117`
- Without AColumns Header AReasonable Grid Is Chosen — `:125`

## PaletteTreeTests
`tests/Lightbox.Core.Tests/PaletteTreeTests.cs`

- AFolder Holds Folders And Palettes And The Top Level Is Just Null — `:20`
- AFolder Can Be Empty And Stays There — `:38`
- The Path Reads From The Top Down — `:49`
- AFolder Can Be Moved Under Another — `:63`
- AFolder Cannot Be Dropped Into Its Own Descendant — `:75`
- Moving To AFolder That Is Not There Changes Nothing — `:92`
- Moving To The Top Level Is Always Allowed — `:103`
- Deleting AFolder Keeps The Colours And Lifts Them One Level — `:117`
- Deleting AFolder Takes The Folders Beneath It Too — `:135`
- The Subtree Finds AGrandchild Listed Before Its Parent — `:154`
- APalette Filed Under AFolder That Is Gone Comes Back To The Top Level — `:168`
- ACycle In The File Is Broken Rather Than Looped Over — `:182`
- ADocument With No Folders Writes No Folder Keys — `:201`
- The Hierarchy Survives ASave And AReload — `:214`

## ProjectBoardTests
`tests/Lightbox.Core.Tests/ProjectBoardTests.cs`

- ADocument Nobody Tagged Or Assigned Writes Neither Key — `:33`
- Tagging And Untagging Leaves Nothing Behind — `:49`
- The Vocabulary Is Every Tag In Use — `:65`
- The Same Tag In Two Cases Is One Tag — `:79`
- ATag On AFolder Reaches Every Document Under It — `:93`
- ALoose Document Sees Only Its Own Tags — `:114`
- Adding The Same Person Twice Is One Person — `:127`
- Renaming APerson Fixes Every Row At Once — `:140`
- Removing Somebody Says How Many Documents Named Them — `:157`
- APerson Carries No Role And No Rights — `:179`
- The Summary Counts What Is True Rather Than The Whole Enum — `:192`
- Nobody Has Said Is Its Own Column Rather Than Design — `:214`
- The Chain Is Four Deep And Nearest Wins — `:229`
- AProject Overrides The Artists Own Default — `:247`
- With No User Library The Chain Is Exactly What It Always Was — `:259`
- ADocument Declaration Writes No Reach Key — `:271`
- Nothing Here Needs The Document Open — `:287`

## ProjectFolderTests
`tests/Lightbox.Core.Tests/ProjectFolderTests.cs`

- AProject That Never Made AFolder Writes No Folder Key — `:31`
- AFolder That Was Never Tagged Writes No Tags Key — `:39`
- AFolder Tree Survives ARound Trip — `:50`
- Folders Take Any Name And Nest To Any Depth — `:71`
- The Name Keeps Its Punctuation And The Path Does Not — `:96`
- Two Folders Of The Same Name In One Place Are Numbered — `:108`
- The Same Name Under ADifferent Parent Is Fine — `:120`
- ARename That Would Collide Is Refused — `:140`
- AFolder Moves Under Another And Back To The Root — `:155`
- AFolder Cannot Be Moved Inside Itself Or Its Own Descendant — `:178`
- ACycle From AHand Edited File Does Not Hang — `:201`
- ADocument Filed In AFolder Takes That Folders Path — `:216`
- Two Documents Of One Name In One Folder Get Distinct Files — `:232`
- ADocument Filed At The Root Goes To Documents — `:249`
- Removing AFolder Returns Everything That Was In It — `:275`
- Contents Reports The Whole Subtree Before Anything Happens — `:296`
- AProject Written Before Folders Keeps Its Paths — `:325`
- AProject Written Before The Rename Keeps Its Documents Folder — `:367`

## ProjectTests
`tests/Lightbox.Core.Tests/ProjectTests.cs`

- AProject Round Trips Through The Folder — `:49`
- AStatus Round Trips And An Unset One Writes No Key — `:64`
- Marking Something Ready Does Not Touch The Artwork — `:86`
- The Layout Is The One Documented — `:102`
- An Animation On Disk Is An Ordinary Document — `:116`
- AProject With No Type Writes No Type Key — `:128`
- ADeclared Type Survives — `:142`
- Loading AProject Does Not Read Its Documents — `:150`
- Saving Rewrites Only The Dirty Document — `:166`
- Saving With No Dirty Set Writes Every Loaded Document — `:185`
- An Interrupted Write Leaves The Previous File Intact — `:199`
- Shared Palettes Live On The Project And Round Trip — `:216`
- ASaved Project Keeps Its Swatch Ids — `:232`
- Folders Are Unique Even When Names Collide — `:259`
- Slugs Are Always Usable As AFolder Name — `:277`
- Migrating ALoose Document Gives AOne Document Project — `:281`
- Flatten Inlines The Swatches The Document Actually Uses — `:308`
- Flatten Inlines Referenced Gradients — `:330`
- Flatten Does Not Mutate The Open Document — `:352`
- An Empty Project Saves And Loads With Nothing In It — `:369`
- Loading Something That Is Not AProject Fails — `:378`
- The Palette Hierarchy Survives AProject Save And Reload — `:387`
- AProject With No Folders Writes No Folder File — `:406`
- Deleting The Last Folder Reaches The Disk — `:418`
- APalette Filed Under AMissing Folder Still Shows Up On Load — `:435`

## PublishLayoutTests
`tests/Lightbox.Core.Tests/PublishLayoutTests.cs`

- The Windows Bundle Is Published As ASingle File — `:73`
- Native Libraries Are Not Self Extracted — `:87`
- Native Debug Symbols Are Not Shipped — `:105`
- Crash Reports Can Name The Line They Came From — `:116`
- The Server Publishes Into Its Own Folder And The App Does Not — `:135`
- The Documented Server Path Matches Where It Is Published — `:146`
- The Manual Testing Checklist Names The Folder The Server Is In — `:159`

## ReferenceScopeTests
`tests/Lightbox.Core.Tests/ReferenceScopeTests.cs`

- ASheet On The Knight Folder Serves Every Drawing Under It — `:48`
- APublished Environment Document Reaches ACharacters Drawing — `:69`
- References Of Different Targets Are Resolved Separately — `:91`
- References Accumulate From Every Scope Above — `:109`
- AProject That Declares No References Is Not Filtered — `:128`
- Only AReference Writes ATarget — `:143`
- References And Palettes Resolve Independently — `:164`

## ReferenceStripTests
`tests/Lightbox.Core.Tests/ReferenceStripTests.cs`

- Each Frame Shows Its Own Cell By Default — `:37`
- Past The End Of The Reference There Is Nothing — `:46`
- AReference Can Start Later Than The First Frame — `:58`
- Any Cell Can Be Assigned To Any Frame — `:70`
- Assigning Past The End Fills The Gap With Nothing — `:83`
- Centring Puts The First Cell In The Middle Of The Canvas — `:93`
- Inserting AFrame Moves The Later References Along — `:106`
- Duplicating AFrame Moves The Reference Too — `:121`
- Deleting AFrame Closes The Gap In The Reference — `:132`
- Inserted Inbetweens Push The Reference Along — `:144`
- AStrip Pinned To Absolute Timing Stays Where It Is — `:168`
- Undoing AFrame Insert Puts The Reference Back — `:182`
- ADocument With No Reference Writes No Key For One — `:200`
- Editing The Timeline Of ADocument With No Reference Is Untouched — `:212`
- AReference Round Trips Through Json — `:224`

## ResourceScopeTests
`tests/Lightbox.Core.Tests/ResourceScopeTests.cs`

- APalette On The Knight Reaches Its Animations At Any Depth — `:47`
- Adding ALevel Between The Scope And The Document Changes Nothing — `:64`
- Declarations Accumulate And The Nearest Wins ATie — `:79`
- The Same Id Declared Twice Resolves To The Nearer — `:98`
- APublished Resource Reaches ADocument In Another Branch — `:116`
- Locality Beats Publication — `:136`
- Resolving One Kind Ignores The Others — `:148`
- ADocument At The Project Root Still Resolves — `:161`
- AProject That Declares Nothing Writes No Resource Keys — `:178`
- Only APromoted Resource Writes Its Reach — `:190`
- With Nothing Declared Every Tip Is Still Offered — `:216`
- ATip Declared On AFolder Is Offered There And Not Elsewhere — `:230`

## PressureResponseTests
`tests/Lightbox.Core.Tests/ResponseCurveTests.cs`

- ABrush With No Curves Behaves Exactly As It Always Did — `:206`
- ATarget Nothing Drives Returns One — `:227`
- ACurve Wins Over The Gamma For The Same Target — `:242`
- The Master Switch Beats Everything — `:255`
- The Shape To Show Is The Curve Or The Gammas Own — `:270`
- The Shown Shape Is ACopy And Cannot Be Edited By Accident — `:285`
- Cloning ABrush Clones Its Curves Rather Than Sharing Them — `:295`

## ResponseCurveTests
`tests/Lightbox.Core.Tests/ResponseCurveTests.cs`

- ACurve Nobody Has Touched Is AStraight Line — `:13`
- ACurve Never Leaves The Unit Square — `:25`
- ARising Curve Never Dips — `:44`
- The Handles Themselves Are Hit Exactly — `:64`
- Outside Its Handles The Curve Is Flat — `:80`
- AGamma Becomes The Curve It Describes — `:94`
- AGamma Of Zero Means No Response At All — `:125`
- Handles Out Of Order Are Sorted Rather Than Followed — `:139`
- Two Handles At The Same Pressure Do Not Divide By Zero — `:153`
- ADegenerate Curve Still Answers — `:166`
- The Same Curve Answers The Same Way Every Time — `:175`

## SceneAndConversionTests
`tests/Lightbox.Core.Tests/SceneAndConversionTests.cs`

- AProject With No Folders Writes No Folder Key — `:38`
- AFolder Nobody Arranged Writes No Order Key — `:53`
- AShot Is ADocument Like Any Other — `:66`
- AFilm Survives ASave And Reload — `:85`
- Two Scenes With The Same Name Are Distinguished — `:107`
- Two Shots With The Same Name In One Scene Do Not Overwrite Each Other — `:123`
- Scenes And Shots Can Be Reordered — `:136`
- One Order Arranges Both The Shots And The Sub Scenes — `:159`
- An Impossible Move Changes Nothing — `:182`
- AShot Can Move To Another Scene — `:197`
- AFolder Knows How Long It Runs — `:218`
- AShot Of Unknown Length Makes The Running Time Unknown Rather Than Short — `:232`
- The Length Hint Is Refreshed When The Document Is Written — `:252`
- Deleting AScene Keeps Its Shots — `:272`
- Converting Recreates No Artwork — `:291`
- Converting Away From Animation Keeps The Camera And The Scenes — `:312`
- Converting To No Type Takes The Key Out Of The File — `:331`
- Converting Reports What The Artist Should Know — `:346`
- Converting To The Type It Already Is Says So And Does Nothing — `:366`
- Conversion Survives ASave And Reload — `:379`

## CultureInvarianceTests
`tests/Lightbox.Core.Tests/Serialization/CultureInvarianceTests.cs`

- The Hostile Locale Is Actually Loaded — `:109`
- Saving In AHostile Locale Produces The Same Bytes — `:122`
- The Compact Wire Format Is Also Locale Independent — `:142`
- Opening In AHostile Locale Restores The Same Values — `:163`
- ADocument Saved In One Locale Opens In Another — `:186`
- No Number Is Written With ADecimal Comma — `:208`

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

## ShapeBuilderTests
`tests/Lightbox.Core.Tests/ShapeBuilderTests.cs`

- ALine Is Its Two Ends — `:20`
- AClosed Shape Repeats Its First Point — `:30`
- ARectangle Is Its Four Corners Whichever Way You Dragged — `:43`
- Dragging From The Centre Grows Both Ways — `:54`
- Holding It Regular Squares It Up — `:65`
- An Ellipse Fits Its Box And Closes — `:74`
- ABig Ellipse Gets More Segments Than ASmall One — `:87`
- APolygon Starts At The Top — `:100`
- APolygon Cannot Have Fewer Than Three Sides — `:112`
- The Same Corners Always Give The Same Points — `:120`
- Every Point Is At Full Pressure — `:133`

## SkylinePackerTests
`tests/Lightbox.Core.Tests/SkylinePackerTests.cs`

- Nothing Overlaps And Nothing Leaves The Sheet — `:35`
- Every Sprite Comes Back At Its Own Size And In Input Order — `:50`
- The Same Input Packs Identically — `:68`
- Equal Sized Sprites Are Ordered By Index Rather Than By Luck — `:82`
- Packing Beats AGrid On Ragged Input — `:98`
- Packing Is No Worse Than AGrid On Uniform Input — `:113`
- Padding Surrounds Every Sprite Including At The Sheet Edge — `:128`
- ASheet Is Never Narrower Than Its Widest Sprite — `:145`
- AFixed Width Is Honoured When It Fits — `:156`
- AVery Small Sheet Still Packs — `:166`
- No Sprites Is Not ACrash — `:174`
- AZero Sized Sprite Is Given APixel Rather Than Disappearing — `:182`
- ALong Sheet Stays Fast Enough To Export — `:194`

## StripSlicerTests
`tests/Lightbox.Core.Tests/StripSlicerTests.cs`

- AStrip Of Frames Is Cut At The Gutters — `:46`
- The Cells Tile The Sheet With Nothing Left Over — `:57`
- AFrame Keeps Where Its Drawing Sat On The Sheet — `:75`
- Uneven Gutters Are Snapped To An Even Grid — `:102`
- AGrid Is Found In Both Directions — `:121`
- Reading Order Is Left To Right Then Top To Bottom — `:135`
- An Empty Sheet Has No Frames — `:146`
- ASingle Drawing Is One Frame Covering The Sheet — `:154`
- Asking For Columns Ignores What The Pixels Say — `:166`
- ADeclared Grid Keeps Its Empty Cells — `:182`
- ADetected Grid Drops Cells With Nothing In Them — `:196`
- ADrawing Off Centre In Its Cell Does Not Drag The Cut With It — `:213`
- AGenuinely Irregular Sheet Falls Back To The Gutters — `:234`
- Grid Slices Without Looking At The Image At All — `:248`
- AGrid That Does Not Divide Evenly Still Tiles The Sheet — `:258`
- Transparent Surround Is Background — `:274`
- AFlat Opaque Surround Is Background Too — `:289`
- ASheet With No Flat Surround Is All Content — `:305`
- Nearly Transparent Noise Is Not Content — `:321`

## StrokeCloneTests
`tests/Lightbox.Core.Tests/StrokeCloneTests.cs`

- AClone Keeps Its Link To The Palette — `:30`
- AClone Keeps Everything Else Too — `:42`
- AClone Is ANew Stroke With Its Own Points — `:58`
- ADuplicated Cel Still Paints From The Same Swatch — `:70`

## SymbolGraphTests
`tests/Lightbox.Core.Tests/SymbolGraphTests.cs`

- ASymbol Knows How Many Placements It Has And Where — `:75`
- ASymbol Nothing Places Says So Rather Than Going Missing — `:92`
- APlacement Left Behind By ADelete Is Still Reported — `:106`
- AHeld Cel Is One Placement Not One Per Exposure — `:122`
- Placements On Every Layer Are Counted — `:140`
- Two Symbols Are Counted Apart — `:153`
- ADocument That Cannot Be Read Is Skipped Rather Than Fatal — `:168`
- AVariants Own Art Is Counted As Its Own Document — `:182`
- Of Is The Same Answer For One Symbol — `:209`

## SymbolRecordTests
`tests/Lightbox.Core.Tests/SymbolRecordTests.cs`

- ADocument With No Placements Writes No Placement Key — `:57`
- AFresh Painted Frame Has No Placements — `:74`
- AProject With No Symbols Writes No Symbol File — `:83`
- APlacement Survives ASave And Reload — `:102`
- Symbols Survive ASave And Reload Of The Project — `:133`
- Deleting The Last Symbol Reaches The Disk — `:162`
- ANested Placement Is Refused On Load Rather Than Half Supported — `:188`
- AProp Shows Its One Frame On Every Cel — `:217`
- ACycle Wraps Across The Timeline — `:226`
- An Offset Runs The Same Cycle Out Of Step — `:237`
- ANegative Offset Starts Part Way Through Rather Than Going Out Of Range — `:249`
- An Empty Symbol Still Reports One Frame So Nothing Divides By Zero — `:258`
- APlacement Remembers What It Was Placed Against — `:265`

## SymbolScopeTests
`tests/Lightbox.Core.Tests/SymbolScopeTests.cs`

- Placing AGlobal Symbol Copies It Into The Project — `:37`
- The Project Renders With The Library Gone — `:54`
- Adopting Twice Adopts Once — `:73`
- Promoting Copies Up And Keeps The Id — `:91`
- Promoting An Edited Symbol Replaces The Library Entry — `:108`
- Editing ALibrary Symbol Does Not Reach Into AProject That Placed It — `:127`
- Asking For The Update Takes It — `:144`
- APull Never Goes Backwards — `:166`
- APull Leaves Placements Alone — `:184`
- ASymbol The Library Does Not Have Is Not Stale — `:208`
- Pulling Something The Project Does Not Have Is ANo Op — `:219`
- ALibrary Symbol Round Trips Through The Documents Own Serializer — `:226`
- With Nothing Declared Every Symbol Is Still Offered — `:274`
- ASymbol Declared On AFolder Is Offered There And Not Elsewhere — `:284`
- ADeclaration Reaches Every Depth Beneath It — `:301`
- APublished Symbol Reaches The Whole Project — `:320`
- Scoping Narrows The Picker And Never What Is Already Drawn — `:350`

## TemplateTests
`tests/Lightbox.Core.Tests/TemplateTests.cs`

- An Ordinary Document Carries No Template Keys — `:37`
- The Flag Round Trips And Clearing It Removes The Key — `:52`
- ANew Document From ATemplate Is ACopy Not ALink — `:67`
- Editing ATemplate Leaves Earlier Copies Alone — `:91`
- ACopy Survives The Template Being Deleted — `:108`
- The Project Lists Its Templates Apart From Its Animations — `:124`
- The Preview Finds Layers The Template Has And The Document Does Not — `:141`
- APull Never Removes ALayer The Artist Has — `:156`
- Nothing To Pull Reports Nothing — `:170`
- ALayer The Artist Has Drawn On Is Skipped Unless Ticked — `:184`
- An Untouched Layer Takes The Properties Without Being Ticked — `:209`
- Erasing Counts As Drawn On Too — `:227`
- Imported Pixels Count As Work Even With No Strokes — `:248`
- ANew Layer Arrives With Its Drawings — `:264`
- APull Never Touches Drawings On ALayer The Document Already Has — `:284`
- APull Never Touches The Exposure Sheet — `:299`
- Guides Are Replaced Wholesale And Grids Come With Them — `:318`
- Fps Is Pulled And Size Is Not — `:339`
- ACamera Is Added When Absent And Never Overwritten — `:359`
- Each Part Of The Pull Can Be Declined — `:379`
- APull Into ADocument That Is Not ACopy Still Behaves — `:397`

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

## RetimingTests
`tests/Lightbox.Core.Tests/Timeline/RetimingTests.cs`

- Stretch Holds Each Drawing And Loses None — `:41`
- Stretch On Threes Holds For Three — `:52`
- Stretching Twice Does Not Compound — `:60`
- Reduce Keeps Every Nth And Keeps The Length — `:74`
- Reduce Is The One That Throws Work Away — `:87`
- Both Are Undoable As One Step — `:101`
- AStep Of One Or Less Is ANo Op — `:118`

## TimingPresetTests
`tests/Lightbox.Core.Tests/TimingPresetTests.cs`

- Applying APattern Re Exposes The Drawings That Are There — `:26`
- The Pattern Decides The Length Not The Selection — `:41`
- Going To Ones Shrinks The Range — `:57`
- APattern Shorter Than The Range Repeats — `:76`
- An Uneven Pattern Is Laid Down In Order — `:86`
- It Never Creates Or Destroys ADrawing — `:97`
- ARange Of Only Holds Is ANo Op And Nothing Blanks — `:112`
- ARange Beginning Mid Hold Does Not Drag The Outside Drawing In — `:130`
- ARange Of One Cel Leaves Everything After It Intact — `:152`
- ANon Range Is ANo Op — `:168`
- ADegenerate Pattern Behaves Rather Than Throwing — `:176`
- The Built Ins Are The Ones An Animator Asks For By Name — `:190`
- APattern Can Be Typed With Commas Or Spaces — `:207`
- ATypo Fails The Whole Parse Rather Than Being Skipped — `:220`
- APattern Round Trips Through Its Text — `:228`
- Retiming Is One Undo Step — `:249`
- The Scene Grows To Fit But Never Shrinks — `:268`
- An Unknown Layer Is ANo Op Rather Than AThrow — `:286`

## UnityConvertTests
`tests/Lightbox.Core.Tests/UnityConvertTests.cs`

- The Centre Of ACell Is The Centre Either Way Up — `:18`
- The Top Of The Cell Becomes One And The Bottom Becomes Zero — `:29`
- AFeet Centre Pivot Comes Out At The Bottom Middle — `:41`
- It Normalises Within The Trimmed Cell Rather Than The Canvas — `:53`
- Flipping Before Normalising Would Give ADifferent Answer On ANon Square Cell — `:66`
- APivot Outside The Cell Is Not Clamped — `:80`
- ADegenerate Cell Is Treated As One Pixel Rather Than Dividing By Zero — `:96`
- ARectangle Centred On The Pivot Has No Offset — `:106`
- ARectangle Below The Pivot Has ANegative YOffset — `:119`
- XRuns The Same Way In Both Systems And YDoes Not — `:136`
- Pixels Per Unit Scales Both The Offset And The Size — `:152`
- Moving The Rect And The Pivot Together Changes Nothing — `:165`
- An Impossible Pixels Per Unit Falls Back Rather Than Dividing By Zero — `:185`
- Frame Duration Comes From Fps Rather Than From Rounded Milliseconds — `:197`
- An Impossible Frame Rate Does Not Divide By Zero — `:218`
- Pixels Per Unit Is Derived From The World Size The Project Chose — `:224`
- An Unset World Height Falls Back To Unitys Own Default — `:235`

## UnrealConvertTests
`tests/Lightbox.Core.Tests/UnrealConvertTests.cs`

- Unreals Figure Is AHundred Times Unitys Because AUnit Is ACentimetre — `:23`
- Using Unitys Figure Would Make ACharacter Centimetres Tall — `:40`
- The Constant Says What It Is Rather Than Being AHundred Somewhere — `:62`
- An Impossible World Height Does Not Divide By Zero — `:72`
- An Ordinary Frame Runs For One Flipbook Frame — `:81`
- AHold On2s Runs For Two — `:88`
- Truncating Rather Than Rounding Would Delete The Frame Entirely — `:95`
- No Duration Ever Produces AFrame That Never Shows — `:113`
- An Impossible Frame Rate Still Gives AUsable Run — `:121`
- The Pivot Is Measured From The Sprite Rectangles Own Corner — `:129`
- It Is Neither Unitys Normalised Pivot Nor Godots Centre Offset — `:138`
- Unreals Texture Space YRuns Down So AFeet Pivot Is The Larger Number — `:164`
- ATrimmed Cell Moves The Pivot With It Rather Than Leaving It Behind — `:176`
- Punctuation Unreal Rejects Becomes An Underscore — `:196`
- AName With Nothing Usable In It Falls Back Rather Than Being Empty — `:206`
- ARun Of Punctuation Collapses Rather Than Becoming ARow Of Underscores — `:214`

## VersioningTests
`tests/Lightbox.Core.Tests/VersioningTests.cs`

- Create Version Increment Number And Saves Metadata — `:22`
- Create Branch Diverges From Parent — `:38`
- Create Branch From Nonexistent Version Throws — `:50`
- Revert To Version Sets Active And Creates Audit Entry — `:58`
- Revert To Version Invalid Version Throws — `:79`
- Set Milestone Marking Version As Ready — `:89`
- Is Version Orphaned Main Line Not Orphaned — `:100`
- Is Version Orphaned Branch With Valid Parent Not Orphaned — `:107`
- Is Version Orphaned Nonexistent Version Orphaned — `:116`
- Get Active Branches Returns Versions With Divergent Children — `:122`
- Validate Version History Consistent Tree No Errors — `:136`
- Validate Version History Orphaned Parent Reports Error — `:148`
- Cleanup Orphaned Versions Removes Invalid Branches — `:165`
- Get Milestones Filters Versions By Status — `:183`
- Version Entry Get All Descendants Recurses Tree — `:202`
- Clear Version History Removes All Versions For Resource — `:218`

## WindowsSubsystemTests
`tests/Lightbox.Core.Tests/WindowsSubsystemTests.cs`

- The App Ships As AGui Program So No Console Window Opens Beside It — `:63`
- The Stdio Programs Stay Console Programs — `:71`
- The Opt In Traces Still Have Somewhere To Print — `:84`
- Asking For AConsole Works Even When There Is No Terminal To Attach To — `:98`

## AlphaLockTests
`tests/Lightbox.Raster.Tests/AlphaLockTests.cs`

- Paint Only Lands Where The Layer Already Had Content — `:51`
- The Silhouette Is Unchanged — `:63`
- Without The Lock The Same Stroke Spills Outside — `:77`
- The Flag Survives AClone And ARound Trip — `:85`
- Re Rendering The Whole Frame Reproduces The Mask Without Storing It — `:98`

## BrushDynamicsTests
`tests/Lightbox.Raster.Tests/BrushDynamicsTests.cs`

- Size Jitter Varies The Stroke But Minimum Diameter Stops Dabs Vanishing — `:67`
- Flow Jitter Changes Alpha Without Changing Coverage — `:86`
- Roundness Squashes The Dab — `:97`
- Angle Follows Direction Changes AFlat Tip But Not ACircular One — `:109`
- Color Dynamics Drift Toward The Second Colour — `:132`
- Texture Bites Into The Stroke And Is Anchored To The Paper — `:154`
- Every Dynamic Is Deterministic — `:172`
- ABrush With No Dynamics Set Renders Exactly As Before — `:196`

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

## BrushPreviewRendererTests _Category=Performance_
`tests/Lightbox.Raster.Tests/BrushPreviewRendererTests.cs`

- APreview Actually Has AMark On It — `:39`
- The Mark Stays Inside The Tile — `:49`
- No Brush In The Range Runs Off The Tile — `:69`
- An Effect Brush Gets Something To Work On Rather Than Blank Paper — `:88`
- An Effect Brush Keeps Its Real Size Wherever It Already Fits — `:109`
- ABigger Brush Always Reads Bigger Right Across The Range — `:140`
- The Whole Range Still Fits Inside The Tile — `:152`
- Size Is Shown On ACurve Rather Than Linearly — `:173`
- Two Different Brushes Do Not Produce The Same Picture — `:189`
- The Same Brush Renders The Same Picture Twice — `:216`
- ABrush The Engine Cannot Honour Gives ABlank Tile Rather Than Throwing — `:234`
- Sixty Previews Render Fast Enough To Open APicker — `:250`

## BrushTipOutlineTests _Category=Performance_
`tests/Lightbox.Raster.Tests/BrushTipOutlineTests.cs`

- ABar Tip Outlines As ABar And Not As ACircle — `:83`
- ANon Square Tip Keeps The Aspect The Engine Stamps It At — `:116`
- AHollow Tip Keeps Its Hole — `:144`
- Nothing To Outline Is Null Rather Than ACircle — `:185`
- The Trace Is Paid Once Per Tip And Not Per Frame — `:211`

## BrushTipSamplingTests
`tests/Lightbox.Raster.Tests/BrushTipSamplingTests.cs`

- AMinified Tip Is Averaged Not Point Sampled — `:91`
- Point Sampling Is What That Rules Out — `:109`
- AHeavily Minified Tip Keeps The Ink Density It Actually Has — `:148`
- An Enlarged Tip Is Smoothed Rather Than Blocky — `:171`

## BrushVisualTests _Category=Visual_
`tests/Lightbox.Raster.Tests/BrushVisualTests.cs`

- ADragged Blur Looks Like The Blur That Commits — `:95`
- An Effect Brush Lands In The Same Place Whatever The Output Scale — `:169`
- The Simulated Media Can Be Compared At True Size — `:246`

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

- Light Pressure Strokes Have No Gaps Along The Centerline — `:182`
- Pressure Ramp Keeps The Stroke Connected — `:212`

## ScratchPreviewTests
`tests/Lightbox.Raster.Tests/DraftAndAaTests.cs`

- Scratch Preview Matches Exact Render Where The Stroke Crosses Itself — `:122`

## EffectArtefactTests
`tests/Lightbox.Raster.Tests/EffectArtefactTests.cs`

- An Effect Brush Cannot Raise Alpha Above What It Could Have Sampled — `:111`
- ADraft Effect Cannot Raise Alpha Either — `:141`
- The Live Preview Accumulation Cannot Raise Alpha Either — `:168`
- The Reported Configuration Cannot Raise Alpha Either — `:208`
- The Stroke Still Does Something — `:268`

## EffectBrushTipTests _Category=Visual_
`tests/Lightbox.Raster.Tests/EffectBrushTipTests.cs`

- ATip Changes What An Effect Brush Touches — `:126`
- AFollowing Tip Turns With The Stroke — `:177`
- An Untipped Effect Brush Is Unchanged — `:239`

## EffectOnTranslucentArtTests
`tests/Lightbox.Raster.Tests/EffectOnTranslucentArtTests.cs`

- An Effect Brush Does Not Make AWash More Opaque Than It Was — `:72`
- ASmudge Still Moves Colour Rather Than Doing Nothing — `:96`

## EffectPreviewMatchesCommitTests
`tests/Lightbox.Raster.Tests/EffectPreviewMatchesCommitTests.cs`

- ASmudge Preview Matches Its Commit — `:231`
- ABlender Preview Matches Its Commit — `:240`
- The Affected Area Does Not Change On Release — `:252`
- No Settling Visible Between Preview And Commit — `:263`
- ASmudge Preview Stays Same On Release — `:271`
- ABlender Preview Stays Same On Release — `:277`

## FillStrokeTests
`tests/Lightbox.Raster.Tests/FloodFillTests.cs`

- Fill Stroke Renders Its Region With Holes Left Empty — `:158`
- Fill Stroke Survives Document Serialization Pixel For Pixel — `:170`
- Clipped Stroke Re Renders Identically From Json Alone — `:188`
- Feather Softens The Clip Edge — `:224`

## FloodFillTests
`tests/Lightbox.Raster.Tests/FloodFillTests.cs`

- Fill Stops At Barriers Within Tolerance And Crosses Them Beyond It — `:34`
- Fill Traces Inner Contours As Holes — `:53`
- Gap Closing Seals Small Openings But Not Larger Ones — `:73`
- Grow And Shrink Overfill And Underfill The Region — `:100`
- Fill Is Deterministic — `:114`
- Fill Stays Inside ASelection Mask — `:125`

## FluidLatticeTests _Category=Performance_
`tests/Lightbox.Raster.Tests/FluidLatticeTests.cs`

- Pigment Is Conserved Across Every Channel — `:39`
- Conservation Holds At Every Parameter Corner — `:67`
- Deposit Never Exceeds Pigment That Was Seeded — `:87`
- Drying Puts Every Grain On The Paper — `:114`
- How Long The Solver Runs Does Not Decide How Much Paint Lands — `:145`
- Drying AMark That Never Flowed Leaves It Exactly Where It Was Stamped — `:172`
- Dry Paper Stays Dry However Often It Is Dried — `:189`
- Run Zero Changes Nothing At All — `:214`
- Two Runs Are Bit Identical — `:244`
- Inviscid Undragged Deluge Stays Finite — `:287`
- Extreme Parameters Do Not Produce Na N — `:322`
- Edge Pull Concentrates Deposit Near The Wet Boundary — `:345`
- Edge Pull Responds Monotonically — `:453`
- Edge Pull Builds ARim Instead Of Mottling The Middle — `:481`
- Granularity Biases Deposit Into The Papers Valleys — `:500`
- Paper Influence Zero Makes The Paper Irrelevant — `:544`
- Water Spreads Beyond Where It Was Seeded — `:568`
- Thin Wash Pins Instead Of Creeping Forever — `:584`
- Mis Sized Buffers Are Rejected — `:619`
- Four Hundred Square Twelve Steps Stays Within Budget — `:640`

## ImpastoTests
`tests/Lightbox.Raster.Tests/ImpastoTests.cs`

- Thick Paint Is Modelled And Flat Paint Is Not — `:71`
- The Raised Edge Catches The Light — `:89`
- Paint With No Body Renders Exactly As It Always Did — `:130`
- Relief Is As Repeatable As Everything Else — `:147`
- Shading Paints No Extra Coverage — `:160`

## ImportedTextureTests
`tests/Lightbox.Raster.Tests/ImportedTextureTests.cs`

- An Imported Paper Bites Into The Stroke — `:67`
- Depth Zero Leaves The Stroke Exactly As It Was — `:81`
- An Imported Paper Wins Over The Built In Surfaces — `:97`
- The Paper Is Anchored To The Document Rather Than To The Stroke — `:121`
- The Same Paper Paints The Same Mark Every Time — `:177`
- ATexture That Is Not Registered Is Ignored Rather Than Fatal — `:193`
- AHuge Scan Is Reduced Rather Than Held Whole — `:208`
- ASmall Texture Is Left At Its Own Size — `:223`
- Height Comes From Luminance Not From One Channel — `:234`
- ANegative Document Coordinate Tiles Rather Than Throwing — `:250`

## IncrementalDabWalkTests
`tests/Lightbox.Raster.Tests/IncrementalDabWalkTests.cs`

- Growing AStroke One Point At ATime Gives The Same Pixels — `:86`
- The Stable Count Grows And Never Exceeds The Dabs There Are — `:122`
- ASingle Point Is Entirely Settled So ATap Inks At Once — `:149`
- AWalked Dab Carries The Stroke Globals That Made It Position Dependent — `:167`
- The Bounds Of ADab Range Cover Every Dab In It — `:185`
- An Empty Range Has No Bounds — `:211`

## LiveBlurFidelityTests
`tests/Lightbox.Raster.Tests/LiveBlurFidelityTests.cs`

- ADragged Blur Matches The Committed One — `:140`
- One Draft Call Of AWhole Stroke Is Already Exact — `:225`

## LivePaletteTests
`tests/Lightbox.Raster.Tests/LivePaletteTests.cs`

- Recolouring ASwatch Recolours The Stroke — `:43`
- One Swatch Drives Every Frame And Every Layer — `:57`
- Vector And Raster Frames Resolve The Same Swatch — `:72`
- AStroke With No Swatch Keeps Its Own Colour — `:96`
- AMissing Swatch Falls Back To The Colour The Artist Last Saw — `:110`
- AFill Resolves The Swatch Too — `:120`
- Re Registering APalette Replaces Rather Than Ignores — `:143`

## MediumPerformanceTests _Category=Performance_
`tests/Lightbox.Raster.Tests/MediumPerformanceTests.cs`

- AWatercolour Stroke Commits Within Budget — `:65`
- The Medium Costs The Same On AHuge Canvas As On ASmall One — `:77`
- AMedium Stroke Does Not Allocate ALattice Each Time — `:99`
- AReused Lattice Renders Exactly What AFresh One Would — `:129`

## MediumRenderingTests
`tests/Lightbox.Raster.Tests/MediumRenderingTests.cs`

- The Four Media Do Not Render Identically — `:89`
- APlain Brush Is Untouched By Any Of This — `:135`
- Watercolour Light Pressure Is Paler And Spreads Further — `:148`
- Oil ASecond Stroke Disturbs The First — `:165`
- Flow Steps Decide Where The Paint Goes Not How Much Of It There Is — `:199`
- AMedium That Never Flows Still Paints The Stroke — `:228`
- Every Medium Re Renders Identically — `:245`

## MediumSpreadTests
`tests/Lightbox.Raster.Tests/MediumSpreadTests.cs`

- AWet Medium Bleeds Past The Brush — `:59`
- AWetter Brush Bleeds Further — `:73`
- It Keeps Spreading The Longer It Runs — `:83`

## OutputScaleTests
`tests/Lightbox.Raster.Tests/OutputScaleTests.cs`

- AHigher Output Scale Renders The Same Mark — `:142`
- Scaling The Coordinates Instead Produces ADifferent Mark — `:160`
- Output Scale One Is Untouched — `:180`
- AHigher Output Scale Actually Resolves More Detail — `:193`
- ASmudge At Higher Output Scale Lands In The Same Place — `:236`
- AClipped Stroke Clips To The Same Region At Every Scale — `:278`
- An Alpha Locked Stroke Stays Inside Existing Paint At Every Scale — `:322`

## PaintLoadTests
`tests/Lightbox.Raster.Tests/PaintLoadTests.cs`

- ALoaded Brush Starts Full And Runs Out — `:54`
- AFull Brush Never Runs Out — `:69`
- ALess Loaded Brush Runs Out Sooner — `:81`
- ABigger Brush Carries Further — `:97`
- It Works Without ASimulated Medium — `:114`
- Running Out Is As Repeatable As Everything Else — `:127`
- Load Is Not Applied Twice When AMedium Is On — `:139`

## PaperFieldScaleTests
`tests/Lightbox.Raster.Tests/PaperFieldTests.cs`

- Scale Actually Changes The Grain Across The Usable Range — `:343`
- Below Nyquist The Field Saturates Rather Than Aliasing — `:361`

## PaperFieldTests _Category=Performance_
`tests/Lightbox.Raster.Tests/PaperFieldTests.cs`

- Rebuilt Tile Is Bit Identical — `:76`
- Fill Agrees With Height At Exactly — `:101`
- Tile Wraps Without ASeam — `:125`
- Height Stays In Range And Centred — `:169`
- Tooth Depth Separates The Three Papers — `:180`
- Rough Has The Longer Wavelength — `:195`
- Canvas Is Directional And Cold Press Is Not — `:208`
- Scale Sets The Wavelength — `:225`
- Different Scales Are Different Fields — `:243`
- Fill Rejects AToo Small Destination — `:253`
- Fill Is Fast Enough For AFull Frame — `:261`
- Fill Cost Follows The Region Not The Canvas — `:292`

## PerformanceTests _Category=Performance_
`tests/Lightbox.Raster.Tests/PerformanceTests.cs`

- Live Preview Effect Brush Segment Is Bounded To The Segment — `:42`
- Live Preview Plain Brush Segment Stays Cheap — `:60`
- Stroke Commit Exact Append Is Independent Of Frame Complexity — `:70`
- Live Preview Large Brush Segment Stays Interactive — `:83`
- Flood Fill Full Canvas Region Meets Budget — `:96`
- Flood Fill Inside Region With Hole Meets Budget — `:112`

## PigmentModelTests _Category=Performance_
`tests/Lightbox.Raster.Tests/PigmentModelTests.cs`

- Over Zero Thickness Returns Backdrop Bit For Bit — `:35`
- Over Zero Thickness Changes Nothing At All — `:52`
- Over Laying Paint Down Adds Opacity — `:60`
- Over Fully Hiding Converges To Mass Tone Whatever Is Underneath — `:93`
- Over Fully Hiding Is Independent Of Backdrop To The Bit — `:109`
- Over No Scattering Is Beer Lambert — `:117`
- Over No Absorption Is Pure Scattering — `:141`
- Over No Pigment At All Leaves The Backdrop Alone — `:161`
- Over Denormal Thickness Does Not Produce Garbage — `:170`
- Over Extreme Inputs Stay In Gamut — `:183`
- Yellow Glaze Over Blue Is Greener Than Every Possible Alpha Blend — `:213`
- Yellow Glaze Over Blue Is More Saturated Than Alpha Blending — `:257`
- Yellow Glaze Over Blue Darkens Rather Than Averaging — `:277`
- Yellow Glaze Over Pure Blue Goes Black And Says So Honestly — `:287`
- Over Thicker Glaze Moves Monotonically Away From The Backdrop — `:307`
- Coverage Rises With Hiding And With Thickness — `:321`
- From Color Hiding Dial Closes On The Chosen Colour Monotonically — `:339`
- From Color White With No Hiding Is Invisible — `:361`
- Mix At The Ends Reproduces The Inputs Exactly — `:372`
- Mix Is Continuous — `:387`
- Mix Is Clamped And Symmetric In Its Endpoints — `:430`
- Mix Yellow And Blue Makes Green — `:440`
- Over Is Deterministic — `:460`
- From Coefficients And From Color Agree When They Describe The Same Film — `:483`
- Srgb Conversion Round Trips Every Single Level — `:493`
- Srgb Conversion Is The Real Transfer Function Not AGamma Guess — `:500`
- Srgb Conversion Is Monotonic — `:515`
- Over Works In Linear Light Not On Encoded Values — `:535`
- Over Costs Under AMicrosecond Per Pixel — `:549`

## PostProcessDabsTests _Category=Performance_
`tests/Lightbox.Raster.Tests/PostProcessDabsTests.cs`

- Post Processing Pre Stamped Dabs Matches Rendering From Scratch — `:102`
- AStroke That Reaches Nothing Reports No Bounds — `:123`
- The Cost Of APass Does Not Grow With The Length Of The Stroke — `:134`

## PressureCurveTests
`tests/Lightbox.Raster.Tests/PressureCurveTests.cs`

- ACurve Drives The Dab Where AGamma Would Have — `:63`
- An Artist Drawn Curve Does What No Gamma Could — `:80`
- ACurve On Flow Changes How Dark The Mark Is — `:99`
- Pressure Can Open The Scatter Without Reshuffling It — `:121`
- ADriven Dynamic Is Still As Repeatable As Everything Else — `:147`
- The Master Switch Still Turns Everything Off — `:165`
- The Brush Ring Agrees With The Stroke The Curve Produces — `:179`
- ABrush Blend Mode Changes How The Stroke Meets What Is Under It — `:209`
- ABrush That Sets No Blend Mode Paints Exactly As It Always Did — `:222`
- An Eraser Ignores The Blend Mode Entirely — `:234`
- ABlended Stroke Carries Its Blend Through ACopy — `:248`

## PressureTests
`tests/Lightbox.Raster.Tests/PressureTests.cs`

- Master Switch Off Ignores Pen Pressure Entirely — `:20`
- Pressure Hardness Softens The Edge At Light Pressure — `:42`
- Pressure Settings Survive Clone And Serialization — `:60`

## ProjectFlattenTests
`tests/Lightbox.Raster.Tests/ProjectFlattenTests.cs`

- AFlattened Document Renders Identically With The Project Gone — `:64`
- Without Flattening The Same Export Would Render Differently — `:89`
- AFlattened Gradient Renders Identically Too — `:107`
- ADocument That References Nothing Shared Flattens To Itself — `:138`

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

## RuntimeDeterminismTests
`tests/Lightbox.Raster.Tests/RuntimeDeterminismTests.cs`

- The Fingerprint Is Stable Within ARun — `:190`
- The Baseline Is Still Recorded — `:217`
- The Fingerprint Matches The Recorded Baseline — `:232`

## SampleSourceTests
`tests/Lightbox.Raster.Tests/SampleSourceTests.cs`

- This Layer Ignores What Is Underneath — `:92`
- All Layers Live Reads The Backdrop — `:104`
- All Layers Live Follows The Backdrop When It Changes — `:112`
- With Nothing Underneath All Layers Is Just The Layer — `:132`
- ABaked Stroke Carries What It Sampled — `:146`
- ABaked Sample Is Cropped To What The Stroke Can Reach — `:158`
- ABaked Stroke Renders From Its Own Sample With No Backdrop Given — `:174`
- ABaked Stroke Ignores ABackdrop That Changed Under It — `:188`
- ABaked Stroke With No Sample Falls Back To Its Layer — `:208`
- Sampling Reads The Layer Over The Backdrop Not The Backdrop Alone — `:222`

## SmudgeCostTests _Category=Performance_
`tests/Lightbox.Raster.Tests/SmudgeCostTests.cs`

- AWhole Smudge Stroke Stays Affordable — `:29`

## SmudgeTransportTests
`tests/Lightbox.Raster.Tests/SmudgeTransportTests.cs`

- AMark Smudges Its Own Colour Rather Than ADarkened Ghost — `:102`
- AColoured Mark Keeps Its Hue Down The Stroke — `:137`
- Smudging Does Not Hollow Out The Body Of AMark — `:169`
- The Trail Length Responds To Its Controls — `:201`

## StampingArcTests
`tests/Lightbox.Raster.Tests/StampingArcTests.cs`

- AFast Curve Is Inked Right Out To Its Edge — `:86`
- How Fast It Was Drawn Barely Changes The Mark — `:99`
- AThick Brush Shows It Worst — `:113`
- AStraight Stroke Is Untouched — `:125`
- ADrawn Corner Is Still ACorner — `:154`

## StrokeIndexTests
`tests/Lightbox.Raster.Tests/StrokeIndexTests.cs`

- AQuery Returns Strokes In Record Order — `:39`
- AStroke Spanning Many Cells Is Returned Once — `:60`
- AQuery Agrees With Checking Every Stroke By Hand — `:85`
- AQuery Outside Everything Returns Nothing — `:124`
- AStroke That Reaches Nothing Is Recorded Rather Than Skipped — `:138`
- Negative Coordinates Index And Query The Same As Positive Ones — `:152`
- ATile Sized Query Touches AFraction Of ABusy Drawing — `:176`

## SubjectReadingIsNotRenderedTests
`tests/Lightbox.Raster.Tests/SubjectReadingIsNotRenderedTests.cs`

- Deleting Every Reading Changes No Pixel — `:59`
- Renaming APart Changes No Pixel — `:91`

## SymbolFlattenTests
`tests/Lightbox.Raster.Tests/SymbolFlattenTests.cs`

- AFlattened Document Renders Its Placements With The Project Gone — `:96`
- Without Flattening The Same Export Would Render Nothing — `:116`
- ASymbols Own Swatches Travel With It — `:136`
- ADocument That Places Nothing Carries No Symbols — `:164`
- Only The Symbols The Document Places Travel — `:179`
- AFlattened Document Survives ASave And Reload — `:196`
- ASymbol The Document Already Carries Still Has Its Swatches Walked — `:217`
- Flattening AFlattened Document Keeps What Travelled The First Time — `:237`

## SymbolRenderTests
`tests/Lightbox.Raster.Tests/SymbolRenderTests.cs`

- Registering ASymbol Makes It Resolvable — `:123`
- Reset Drops What The Last Project Had — `:133`
- APlaced Symbol Reaches The Pixels — `:148`
- APlacement Lands Its Pivot Where It Was Put — `:159`
- AFrame With No Placements Renders Exactly As It Always Did — `:185`
- An Unresolved Symbol Draws Nothing And Says So — `:198`
- AZero Opacity Placement Draws Nothing — `:209`
- The Same Symbol Placed Twice Is Pixel Identical — `:223`
- Two Placements On One Cel Are The Same Mark Twice — `:239`
- Editing The Symbol Changes Every Placement Of It — `:256`
- An Edit That Forgets To Bump The Version Serves The Old Drawing — `:273`
- Scaling APlacement Does Not Rewrite Its Geometry — `:293`
- Rendering At Twice The Output Scale Is The Same Placement Sharper — `:311`
- Scales That Differ By Noise Share One Cached Render — `:330`
- The Render Cache Stays Bounded — `:352`
- APlaced Cycle Advances With The Cel Index — `:375`
- An Offset Placement Runs The Same Cycle Out Of Step — `:394`
- AProp Shows Its One Drawing On Every Cel — `:408`

## SmudgeFirstDabTests
`tests/Lightbox.Raster.Tests/TexturedBrushTests.cs`

- ASingle Tap On ABoundary Softens It Rather Than Doing Nothing — `:183`
- ATap On Flat Colour Changes Nothing — `:203`
- Smudge Never Deposits The Brush Colour — `:217`

## TexturedBrushTests _Category=Performance_
`tests/Lightbox.Raster.Tests/TexturedBrushTests.cs`

- Wet Edge Darkens The Outline Not The Interior — `:48`
- Granulation Is Deterministic And Anchored To The Document — `:82`
- Paper Texture Commit Does Not Stall The Pen — `:101`
- Textured Stroke Commit Does Not Stall The Pen — `:121`

## TileCullingTests
`tests/Lightbox.Raster.Tests/TileCullingTests.cs`

- Recompositing Costs What Is On Screen Not What Exists — `:87`
- AWider Viewport Draws More Tiles In Proportion — `:118`
- Panning Across Empty Space Allocates Nothing — `:132`
- AViewport Straddling The Origin Draws Tiles On Both Sides — `:156`
- AViewport With No Area Draws Nothing — `:170`
- Asking For ABitmap With No Area Is Refused — `:194`
- ACulled Composite Matches The Same Rectangle Of An Untiled Render — `:226`

## TileStoreTests
`tests/Lightbox.Raster.Tests/TileStoreTests.cs`

- ATile Address Rounds Toward Negative Infinity — `:36`
- Tiles Left Of The Origin Are Full Width And Do Not Overlap — `:48`
- ARectangle Covers The Tiles It Touches And No Others — `:67`
- An Empty Rectangle Covers Nothing — `:86`
- An Untouched Tile Is Never Allocated — `:100`
- Memory Follows Ink Rather Than Area — `:149`
- Renting Twice Returns The Same Tile Rather Than ASecond One — `:178`
- AFresh Tile Contributes Nothing So Absent And Blank Composite The Same — `:202`
- Intersecting Returns Only Tiles That Exist And Are In The Rectangle — `:227`
- Intersecting Empty Space Returns Nothing And Allocates Nothing — `:243`
- Ink Bounds Is Null Until Something Is Drawn — `:254`
- Dropping ATile Releases Its Bytes — `:266`
- The Tile Size Is AParameter Rather Than Baked In — `:283`

## TiledRasterizerTests
`tests/Lightbox.Raster.Tests/TiledRasterizerTests.cs`

- ATiled Render Is Bit Identical To An Untiled One — `:74`
- The Tile Size Does Not Change The Render — `:99`
- Empty Parts Of The Document Are Never Allocated — `:118`
- An Empty Stroke List Renders An Empty Store And An Empty Image — `:148`
- An Effect Brush Across ATile Boundary Is Measured Rather Than Assumed — `:173`
- Effect Brushes Are Refused By The Tiled Path Until B59 Is Fixed Properly — `:254`
- The Whole Frame Fallback Still Stores Only Tiles With Ink — `:281`

## TipCatalogueTests
`tests/Lightbox.Raster.Tests/TipCatalogueTests.cs`

- There Are Eight Built Ins And Every One Bakes — `:12`
- Every Built In Is ADistinct Shape — `:22`
- The Ids Are Frozen — `:34`
- The Pixels Behind ABuilt In Do Not Move Between Runs — `:59`
- The Catalogue Is Baked Once And Shared Afterwards — `:73`
- The Three Staples Are The Ones An Artist Expects To Find First — `:82`
- APaintbrush Is Flat Enough To Read As One — `:94`

## TipFromImageTests
`tests/Lightbox.Raster.Tests/TipFromImageTests.cs`

- Ink Becomes The Shape And Paper Becomes Nothing — `:42`
- AMark Touching The Crop Is Rejected Rather Than Faded — `:57`
- AGood Crop Is Still Feathered At The Border — `:73`
- The Crop Follows The Mark Rather Than The Page — `:93`
- Levels Decide What Counts As Paper — `:116`
- AMask That Is Already White On Black Is Not Inverted Again — `:137`
- One Set Of Levels Applied To ABatch Gives Matching Tips — `:148`
- The Pivot Starts At The Inks Own Centre — `:166`
- ACollapsed Level Range Is Clamped Rather Than Dividing By Zero — `:181`

## TipGeneratorTests
`tests/Lightbox.Raster.Tests/TipGeneratorTests.cs`

- AGenerated Edge Is Coverage Not AStaircase — `:24`
- AHard Circle Is Round And Centred — `:42`
- Hardness Decides How Far The Core Reaches — `:63`
- ASoft Tip Fades Without ACrease — `:79`
- ARing Is Hollow — `:105`
- AChisel Is Flat Across Its Short Axis — `:118`
- Angle Is Baked Into The Shape — `:130`
- Hatch Rules Are Drawn As Width Not As Single Pixels — `:145`
- AHatch Stays Inside The Round Footprint — `:168`
- Cross Hatch Rules Both Ways — `:182`
- ABaked Tip Carries Its Shape In Alpha — `:197`
- The Same Recipe Bakes The Same Tip Every Time — `:211`
- ARecipe Is Provenance And Travels With The Tip — `:227`
- ABristle Tip Is Combed At The Rim And Solid In The Middle — `:245`
- More Bristles Means More Channels — `:270`
- One Exponent Walks The Whole Superellipse Family — `:289`
- ASuperellipse Is Still Flattened By Roundness — `:322`
- APolygon Has Corners That Reach Further Than Its Flats — `:335`
- Rounding APolygon Pulls It Toward The Circle It Sits Inside — `:354`
- Spatter Grains Have ASize Rather Than Being Fog — `:373`
- Spatter Coverage Is Monotone And The Cell Count Sets The Grain Size — `:404`
- AHalo Is Denser At Its Rim Than In Its Middle — `:430`
- The Halo Rim Slider Takes It Back To AFlat Wash — `:448`
- Every Shape Bakes Something And Stays Inside The Matrix — `:462`
- Every Shape Bakes The Same Way Twice — `:480`
- An Absurd Size Is Clamped Rather Than Allocated — `:549`
