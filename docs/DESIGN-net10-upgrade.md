# .NET 10: what is safe, what is unverified, and what it narrows

This document exists to stop a target-framework bump being done on the
strength of "it compiled". The solution's whole value proposition is that a
document re-renders to the same pixels a month later — invariants 1, 2 and 7 —
and **nothing in the suite today would notice if a runtime change altered that
by one bit**. Every reproducibility test computes both sides in the same
process, so all of them pass on a runtime that renders differently from
yesterday's. That gap is the reason to write this down before touching a TFM,
not after.

Status: **done — built, run, and measured.** The solution targets `net10.0`
throughout as of this change. Everything below that was marked UNVERIFIED has
been settled by running it on .NET 10.0.10 / SkiaSharp 3.119.4 on linux-x64,
against the .NET 8.0.29 baseline recorded before the move. The original
research is kept rather than rewritten, because what a prediction got wrong is
worth as much as what it got right — and here it was wrong in the safe
direction twice.

| Item | Verdict | Basis |
| --- | --- | --- |
| Avalonia 12.1.1 on `net10.0` | safe | explicit `net10.0` dependency group; **confirmed, 1344 UI tests green** |
| SkiaSharp 3.119.4 on `net10.0` | safe | explicit `net10.0` dependency group; **confirmed, 360 raster tests green** |
| `Hash01`, all three copies | safe by construction | integer arithmetic only; bit-exact by language spec |
| JSON `$type`/`$id` rejection (.NET 10) | not applicable | `FrameConverter` is hand-rolled on a `kind` discriminator |
| Float→int saturating casts (.NET 9) | not applicable | no narrowing casts in the hash path |
| `System.IO.Pipes`, globalisation, publish | no entry in either breaking-changes list | dotnet/docs 9.0 and 10.0 |
| xunit 2.4.2 + Test.Sdk 17.6.0 on `net10.0` | **VERIFIED safe** | all three classic-xunit projects discovered and ran; VSTest adapter reports `64-bit .NET 10.0.10` |
| Transcendental drift (`Math.Cos`/`Sin`/`Sqrt`) | **VERIFIED absent** | all three `RuntimeDeterminismTests` fingerprints bit-identical to the .NET 8.0.29 baseline |
| Linux minimum glibc | **narrows**, 2.23 → 2.27 | dotnet/core supported-os notes — the one item still open, as **Q19** |
| CI's .NET 8 runtime | **retired** | the solution has no `net8.0` assembly left to run; `build.yml` names `10.0.x` alone |

## What the migration actually found

Two predictions were wrong, both in the direction of the doc having been too
cautious, and one of them is the whole reason this file exists.

**The render did not move by a bit.** This was the risk the document was
written around — scatter turns a hash into an angle and moves the dab by
`Math.Cos`/`Math.Sin`, transcendental results are not guaranteed bit-identical
across runtime versions, and a one-ULP shift changes an antialiased edge
without looking like anything. It was a real risk correctly identified. It
simply did not happen: all three fingerprints match exactly, including
`jitter`, which is the only scenario that reaches the transcendental path at
all.

That is a *measurement*, not a reassurance, and the distinction matters for
anyone reading this later: it holds for .NET 8.0.29 → 10.0.10 on linux-x64,
which is the axis the upgrade moved along. It says nothing about
Windows or macOS, and it never could have — cross-*platform* bit-identity was
neither guaranteed nor tested before this change either, so it is not a
question the migration opened. `RuntimeDeterminismTests` now guards the runtime
axis permanently, which is more than existed before.

**The xunit question was the one that could have grown the work, and it was
fifteen minutes.** Three of the four test projects run classic xunit 2.4.2 with
`Microsoft.NET.Test.Sdk` 17.6.0, versions that predate .NET 8, and the document
reserved the right to grow a test-stack migration if they could not discover
tests against a `net10.0` target. They can. The VSTest adapter reports
`64-bit .NET 10.0.10` and all three projects ran clean. The contingency in
*Rejected* — downgrade `Lightbox.Mcp` as an interim move — was never needed.

**One test failed, and it was supposed to.**
`CiRuntimeTests.TheEightPointZeroRuntimeIsStillActuallyRequired` asserted the
TFM was still `net8.0`, purely so that moving it would produce a failure
telling whoever did it that the `8.0.x` line in `build.yml` had become clutter.
It fired exactly as designed and both are now gone. It is worth naming as a
pattern: **an expiring workaround that cannot announce its own expiry is how
the original problem survived** — B53's inherited runtime dependency sat
invisible for exactly that reason.

## Why this is on the table at all

Three things arrive together, and only the first is a deadline.

**.NET 8 leaves support in November 2026** — roughly three months out. .NET 9
(STS) went out of support in May 2026, so there is no intermediate step left:
the next supported LTS is .NET 10, out to late 2028. Staying put is not the
zero-risk option it looks like, it is the option that ends in an unsupported
runtime on a known date.

**The build already requires the .NET 10 SDK.** `build.yml` installs
`10.0.x` and says why: Avalonia 12's source generators need newer Roslyn than
the .NET 8 SDK ships. So the toolchain moved a while ago and only the target
framework stayed behind.

**B32 is the same argument arriving from the packaging side.** `Lightbox.Mcp`
already targets `net10.0` while everything else targets `net8.0`, and the
consequence is two full self-contained runtimes in every download — 35.3 MB of
a 105 MB bundle. B32's prescribed fix is to move the MCP server *down* to
`net8.0`. A solution-wide move *up* resolves the same duplication from the
other end and buys the support window with it, which is why that entry has to
be reconciled rather than worked around.

## The SDK and the target framework are different questions

This is the confusion worth killing first, because it makes the CI comment
look like it contradicts `CLAUDE.md`. The SDK is the compiler and tooling that
runs the build; the target framework is the API surface and runtime the output
binds to. They move independently, and building a `net8.0` target with a .NET
10 SDK is an ordinary, supported arrangement — it is what happens on every CI
run today. Nothing in this document is about the SDK. Everything in it is
about the TFM.

The practical consequence is that `README.md` and `CLAUDE.md` both understate
the requirement: an SDK-8-only machine cannot build this repo now, before any
upgrade. A Codespace provisioned from the .NET 8 image would fail on the
source generators and the failure would read as an Avalonia problem.

### Building it and running it need different runtimes, and CI only declares one

This one is worth its own heading because it bites *before* the upgrade, it
bites hardest on a fresh Linux container, and the error it produces points
somewhere else entirely.

The .NET 10 SDK builds a `net8.0` target happily. It cannot *run* one. No
`runtimeconfig` in this repository sets `rollForward`, so the default `Minor`
applies, and that does not cross a major version — a `net8.0` test assembly
requires the .NET 8 runtime to be present and will not fall back to 10. So a
container that installs only the SDK named in `build.yml` compiles the whole
solution and then fails to launch a single test, with an error about a missing
framework rather than about the SDK choice that caused it.

**CI does not hit this because the `ubuntu-latest` runner image preinstalls
.NET 8**, so `setup-dotnet` naming only `10.0.x` lands on a machine that
already had what it needed. That is an inherited dependency, not a declared
one, and it has an expiry date: when GitHub drops .NET 8 from the runner image
after November 2026, `build.yml` starts failing for a reason nothing in it
mentions. Recorded as **B53**.

The upgrade makes this go away — a solution that is entirely `net10.0` needs
exactly one runtime, which is the SDK's own. Until then `.devcontainer` installs
both and says why, and step 2 of the order below is impossible without the .NET
8 runtime: recording the old runtime's fingerprint means running on it.

## Hash01 is safe by construction, and that is not the same as the render being safe

This is the finding that reorders the whole risk list, and it cuts both ways.

All three `Hash01` implementations — `BrushEngine`, `Media/PaperField`,
`Tips/TipGenerator` — are FNV-then-avalanche over `uint`/`int`, seeded through
`BitConverter.SingleToInt32Bits`. Every operation in them is integer
arithmetic with defined wrapping semantics. **A runtime cannot change the
result**: the language specifies these bit patterns, and `SingleToInt32Bits`
is a reinterpretation rather than a conversion. The hash functions themselves
need no test to survive a migration, and a pinned-constant test over them
would guard the thing least at risk.

**What is at risk is the floating-point arithmetic that computes the position
handed to them, and the arithmetic downstream of what they return.** Scatter
is the concrete example: `Hash01` yields an angle, and the dab moves by
`Math.Cos(angle)` and `Math.Sin(angle)`. Microsoft does not guarantee
bit-identical transcendental results across runtime versions or platforms —
this has moved before, in .NET Core 3.0, in the direction of IEEE compliance.
A one-ULP shift in `Math.Cos` moves a dab by a sub-pixel amount, which changes
the antialiased edge, which changes the pixels. It would not look like a bug.
It would look like nothing at all, until a document rendered on two machines
disagreed.

So the instrument this migration needs is **not** a unit test over `Hash01`.
It is a recorded, cross-runtime fingerprint of the *rendered output* of the
most stochastic brush the engine can produce — which catches transcendental
drift, Skia behaviour and hash changes in one assertion, and is the only one
of the three that can catch anything at all.

## What determinism coverage exists today — in-run only

The suite is not thin here, it is aimed one step to the left. The
`Convert.ToHexString(SHA256.HashData(bmp.GetPixelSpan()))` fingerprint already
recurs in four files, and the comparisons are exact:
`EffectBrushes_AreDeterministic_AcrossRerenders`,
`FillStroke_SurvivesDocumentSerialization_PixelForPixel`,
`ClippedStroke_ReRendersIdenticallyFromJsonAlone`,
`WithoutACamera_TheExportIsByteForByteWhatItAlwaysWas`, and
`OutputScaleTests`' exact `MeanDifference` of zero.

**Both sides of every one of them are computed in the same process.** They
prove the engine is a function — that it does not consult a clock, an RNG or
uninitialised memory — which is exactly what invariant 2 asks for and is worth
having. They cannot prove the function is the same function it was on another
runtime, because they never compare against a value from one.

`PaperFieldTests` and `TipGeneratorTests` have the same shape, down to
comparing `SingleToInt32Bits` of two same-run results. The one genuinely
pinned computed constant in the suite is `Pigment.ToLinear` in
`PigmentModelTests`.

The gap, stated precisely: **there is no stored value from a known-good
runtime anywhere in the repository.** Recording one is cheap, has to happen
*before* the TFM moves, and is worthless if it happens after.

## What is unverified, and can only be settled by running it

**Three of the four test projects run classic xunit 2.4.2 with
`Microsoft.NET.Test.Sdk` 17.6.0** — versions that predate .NET 8. The specific
mechanism that could have broken them turns out not to apply: .NET 10's
`dotnet test` rework routes through Microsoft.Testing.Platform only for
projects that opt in, and nothing here does — no `global.json`, no MTP
properties — so all four projects stay on the VSTest path they use today.

That is an argument that the known breakage does not apply. It is not evidence
that this particular 2022-vintage trio discovers and runs tests against a
`net10.0` target, and no source found says either way. `Lightbox.App.Tests`
already runs xunit.v3 3.2.2, so the repo contains a modern stack to move the
other three onto if it comes to that — but that is a bigger change than the
TFM bump and should not be bundled with it speculatively.

**This is the one item that makes the devcontainer a prerequisite rather than
a convenience.** It is a fifteen-minute question with a working `dotnet` and
an unanswerable one without.

## What it narrows — the Linux floor

Self-contained `net10.0` output requires **glibc 2.27** (Ubuntu 18.04-class),
up from 2.23 (Ubuntu 16.04-class); musl moves 1.2.2 → 1.2.3. Windows
(10 v1607, Server 2012 R2 + ESU) and macOS (14+) floors are unchanged.

This is the only user-visible loss in the whole upgrade, and it is not
recoverable by a code change. It is also the only item here that the project
has no way to answer from the code: it depends on who runs this, which nobody
has written down. Recorded as **Q19** rather than assumed away — an
eight-year-old glibc floor is very likely fine for a desktop art application,
and "very likely" is not the standard for a decision that silently removes
users.

## Rejected: stay on net8.0 and downgrade Lightbox.Mcp

**The case for it is real and it is B32's own prescription.** It is the
smaller diff by a wide margin — one TFM line and a publish path, against nine
projects and a re-verification of the entire render path. It keeps the glibc
floor where it is. It fixes the duplicate-runtime waste completely, which is
the actual measured harm. And it needs no answer to **Q19** at all.

**It loses on the deadline, and the deadline is not negotiable.** November
2026 arrives whether or not the bundle got smaller, and this option pays the
full cost of a migration's verification work — the determinism baseline, the
budget re-measurement — twice: once now to prove the downgrade did not change
the render, and again in three months to prove the upgrade did not. Doing it
once, upward, is the same work with a longer shelf life.

There is one exception worth keeping in reserve. If the xunit question comes
back badly — if 2.4.2 genuinely cannot run against `net10.0` — then the
upgrade grows a test-stack migration, and downgrading the MCP server becomes
the right *interim* move to stop the bandwidth bleed while that is planned
properly. That is a decision to make with the answer in hand, not before it.

## The budgets need re-measuring, not relaxing

Gate G4 says performance budgets hold and are raised only with a measurement
and a reason. A runtime change is precisely the case
`docs/DESIGN-performance.md` already anticipates: absolute milliseconds are
machine-relative and a new JIT invalidates them, while the *slopes* — is this
path proportional to canvas area, is this repaint bounded to the stroke —
survive unchanged.

So the reading order after a bump is slopes first. A budget that moved by 15%
on a new JIT is a re-baseline; a budget that moved by 4× is a regression
wearing a runtime's clothes, and the temptation to attribute it to the
migration is exactly the trap `DESIGN-performance.md` records: **the number was
real and the attribution was not.**

## Order — followed, and what each step actually cost

Nothing here is reversible in the sense that matters — a baseline not recorded
before the bump cannot be recorded afterwards — so the sequence was the
deliverable, not a suggestion. It is kept in the past tense rather than deleted,
because the next migration wants the sequence more than it wants the outcome.

1. ~~**Make a Linux toolchain exist.**~~ Done. Worth one warning for whoever
   provisions the next container: `dot.net`'s install script is not reachable
   through every network policy, and the Ubuntu `dotnet-sdk-10.0` /
   `dotnet-sdk-8.0` packages are the fallback that works.
2. ~~**Record the determinism baseline on `net8.0`.**~~ Done as B55, before the
   bump — which is the only reason step 5 means anything.
3. ~~**Answer the xunit question.**~~ Answered: classic xunit 2.4.2 runs clean on
   `net10.0`. No test-stack migration.
4. ~~**Bump the TFMs.**~~ **Nine files, not four.** This estimate was wrong and
   is worth correcting in place, because the failure mode of an under-count is a
   half-migrated solution that still builds: the four the doc named
   (`Directory.Build.props` and the three test projects that redeclare) plus
   **every `src/` project except `Lightbox.Mcp`** — `Lightbox.Core`,
   `Lightbox.Raster`, `Lightbox.Ai` and `Lightbox.App` all pin their own TFM
   rather than inheriting — and `tools/Lightbox.Bench`. Only `Lightbox.App.Tests`
   and `Lightbox.Import` inherit from the props file alone.
5. ~~**Re-run the baseline.**~~ Match, all three scenarios. See above.
6. ~~**Re-measure the budgets, slopes first.**~~ Done; no budget moved enough to
   need re-baselining.
7. ~~**Update the docs that assert .NET 8**~~ — `CLAUDE.md`, `README.md`,
   `.devcontainer/devcontainer.json` and `build.yml`. **B32 is reconciled but not
   fixed here, deliberately**: the upgrade removes its stated cause (the two
   folders now hold two copies of the *same* runtime, which is the accidental
   duplication a shared output folder fixes) and leaves a user-visible path
   change behind — `mcp\Lightbox.Mcp.exe` is a documented Claude Desktop
   `command`, so moving it belongs in its own change with its own migration note.

## Not in scope

**Moving the three classic-xunit projects to xunit.v3.** Worth doing on its
own merits and it must not ride along here — if the TFM bump and a test-runner
change land together, a red suite has two suspects.

**Trimming or AOT.** `PublishTrimmed=false` is set deliberately and .NET 10's
interop search-path changes only bite single-file publishes. Untouched.

**A Linux publish target.** CI cross-publishes `win-x64` from Ubuntu and there
is no `linux-x64` job. That is a real gap in what CI exercises, it is
independent of this upgrade, and bundling it here would mean debugging two new
things at once.
