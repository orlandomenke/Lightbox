using Xunit;

// Run this assembly's collections one at a time (B93).
//
// WHAT THIS IS FOR
//
// B93 is the long-running CI flake whose signature never varies: a test fails
// at 1 ms — so its body never ran — with a Test Case Cleanup Failure carrying
//
//     Dispatcher.VerifyAccess()
//       <- DefaultRenderLoop.Add(IRenderLoopTask)
//       <- ServerCompositor..ctor
//       <- AvaloniaHeadlessPlatform.Initialize
//       <- HeadlessUnitTestSession.EnsureIsolatedApplication
//
// "The calling thread cannot access this object because a different thread
// owns it." Every frame of that stack is inside Avalonia's headless harness
// *starting an application*. Nothing in it belongs to this codebase, which is
// why sixteen sightings across fourteen unrelated test classes never
// converged on a defect in any of them.
//
// WHY PARALLELISM IS THE SUSPECT
//
// Collections that share mutable global state already disable parallelisation
// individually (BrushState, FrameCacheBudget, ExportPresetFile,
// LargeCanvasPerformance). Everything else runs concurrently — and every B93
// victim has been an [AvaloniaFact] class in a *different* collection from the
// last one. On 2026-08-10 alone: ProjectWindowTests, DocumentTabTests,
// SymbolScopeTests, ActiveColorTests, four runs, four collections, one
// unchanged commit between two of them.
//
// A test-level defect cannot move between classes when nothing changed. Two
// headless sessions initialising at once can, and the stack says session
// initialisation is exactly where it dies.
//
// WHY IT COSTS NOTHING MEASURABLE, WHICH IS THE PART THAT SETTLES IT
//
// Measured on this repository, App suite, Release, 4 cores, wall clock:
//
//     parallel     249 s, 285 s, 290 s
//     serialised   242 s, 297 s
//
// The two are indistinguishable. Note the spread *within* each configuration —
// this box varies by about 20% run to run, which is larger than any difference
// between them, so the honest claim is "no measurable cost" rather than a
// percentage either way. Two runs that happened to read 242 and 249 would have
// supported "3% faster", and that would have been noise dressed as a finding.
//
// That there is no cost reads as a contradiction and is not: 154 of the 203
// test classes here are [AvaloniaFact] classes, and those already serialise on
// the headless dispatcher whatever xUnit does with the collections. So the
// parallelism was never overlapping the *work*. The only thing it overlapped
// was session creation, which is the race.
//
// THE THEORY WAS WRONG (2026-08-10)
//
// B93 recurred with this in place: CharacterSheetFileTests at 1 ms, the same
// stack, on PR #153. Collection parallelism is therefore not the mechanism —
// sessions can still be created concurrently with it off, so whatever races is
// inside Avalonia's harness lifecycle rather than in how this suite schedules
// classes.
//
// This line stays anyway, and that is a decision rather than inertia. Nine CI
// runs since it landed: eight green, one red. The four-in-five failure rate
// that prompted it has not returned. Both samples are small, so the honest
// claim is "the rate looks much better and the bug is not fixed" — and a rate
// that is genuinely lower is worth keeping at a cost measured to be nothing.
//
// HONESTY ABOUT WHAT THIS IS
//
// **A hypothesis with a mechanism and no measured cost, not a proven fix.**
// B93 has never reproduced locally in any of its sightings, so there is no
// red test here that this turns green, and claiming otherwise would be the
// kind of assertion this repository's ledgers exist to prevent. The proof is
// CI staying green across the next several pull requests. If B93 appears
// again with this in place, the parallelism theory is wrong and B93's entry
// should say so rather than this line being quietly deleted.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
