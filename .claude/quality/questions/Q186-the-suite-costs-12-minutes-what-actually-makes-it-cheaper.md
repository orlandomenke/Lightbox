# Q186 · The suite costs 12 minutes — what actually makes it cheaper? — **answered 2026-09-12: all three levers, because selection alone is the weakest of them**

Raised by the owner: *"So for each change irregardless of what it touches we are
running the full suite. Which takes 8 to 20 min at a time. And especially if I
want to get a test build that is annoying. I believe we could split the test
into domain specific tests and if a change did not touch something from another
domain we could only run that test suite. Right?"*

The premise is right and the proposed lever is the weakest of the three
available, which is only visible once the run is measured rather than estimated.

**Measured 2026-09-12, this repository, Release, 4 cores, cold build:**

| | wall clock | share |
| --- | --- | --- |
| `dotnet build Lightbox.sln` | 50 s | 7% |
| `Lightbox.Core.Tests` | 6 s | 1% |
| `Lightbox.Raster.Tests` | 125 s | 17% |
| `Lightbox.Ai.Tests` | 4 s | 1% |
| **`Lightbox.App.Tests`** | **557 s** (4 404 tests) | **75%** |
| total, serial | 742 s | |

**Why "only run that domain's suite" does not do it.** Two reasons, and the
second is the fatal one.

- **The domains are a ledger vocabulary, not a code partition.** `brush` is in
  `Lightbox.Core`, `Lightbox.Raster` and `Lightbox.App` at once. Cutting the
  suite along them means re-homing 650 test files into assemblies that do not
  exist, and buys nothing the project split does not already offer — because the
  unit .NET builds and runs is the assembly.
- **`Lightbox.App.Tests` is 75% of the run and it references everything.** App
  depends on Core, Raster, Ai, Import and Mcp, so a sound selection rule can
  skip it only when a change touches none of them. Selection alone therefore
  saves 135 s on an App-only change and nothing at all on a Core change, which
  is the shape of most changes here.

**Answered: build all three levers**, in the order of what they are worth.

1. **Shard `Lightbox.App.Tests` across several legs.** It is three quarters of
   the run in one serial process — `HeadlessSessionSerialisation.cs` turns
   collection parallelism off assembly-wide, and 154 of its test classes are
   `[AvaloniaFact]` classes that serialise on the headless dispatcher whatever
   xUnit does. Separate processes on separate runners is the one split that
   sidesteps both, and it sidesteps B93's mechanism too: two headless sessions
   cannot race each other from different machines.
2. **Run the legs concurrently.** Turns the sum into the maximum. Free on a
   public repository's standard runners, which is what this one is.
3. **Select the legs a change can reach.** The smallest of the three, and worth
   having anyway because it is nearly free once the matrix exists — and because
   it generalises a filter `build.yml` already has.

Expected wall clock ≈ setup and build + slowest leg. Measured after the fact
from the shards' own TRX: the slowest App leg is **145 s** and the slowest
Raster leg **128 s**, both of them the leg carrying that suite's timed tests —
so **about 200 s against 742 s**.

Two things fell out of building it that were not part of the question, and both
were defects the old shape had been hiding:

- **A project nothing tests stopped being built.** `dotnet test Lightbox.sln`
  compiled the whole solution as a side effect; per-project legs do not, so
  `tools/Lightbox.Bench` would have gone uncompiled with every check green. The
  plan emits compile-only legs for anything outside every test's closure,
  derived rather than listed.
- **Timed tests cannot share a machine.** The first parallel local run turned
  `FourK_WholeStrokeIncludingCommit_HasNoPenLiftStall` red with nothing wrong
  with the code: a budget measuring wall clock measures the load instead. Every
  `[Trait("Category", "Performance")]` class is now gathered into one leg per
  suite and that leg is run alone. CI never had the problem — one runner per
  leg — but the local tool is the one used every day, and a tool that cries
  wolf is one people stop believing.

**What this costs, recorded because it is not free.** Three things.

- **More moving parts between a push and a verdict.** One `dotnet test
  Lightbox.sln` is a thing anybody can run and reason about; a plan that emits a
  matrix is not. Mitigated by keeping the rules in one script with a selftest CI
  runs, rather than in YAML — but the honest cost is that the suite now has a
  program in front of it.
- **The count guard has to be made whole again.** `testcount.py verify` compares
  reported names against discovery per assembly, and sharding breaks that by
  construction: each leg reports a subset. B269 and B281 are precisely the
  failure of a suite proving less than it claims, so the fan-in that reassembles
  the union is not a nicety — without it this change would reintroduce the bug
  the guard exists for, in a form that looks like success.
- **Selection can be wrong in the direction that matters.** A skipped test reads
  exactly like a passing one. The rule is therefore derived from the MSBuild
  reference graph (exact) rather than from the codemap's reference index, which
  is regex name-matching built for risk scoring and can miss an edge. Anything
  the graph does not cover — repo-root files, `scripts/`, `.github/` — selects
  everything unless a declared, test-asserted rule says otherwise.

**`main` and `v*` tags always run everything.** Selection applies to the
feedback loop, never to the merge.
