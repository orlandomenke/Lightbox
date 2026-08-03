# .NET 10: what is safe, what is unverified, and what it narrows

This document exists to stop a target-framework bump being done on the
strength of "it compiled". The solution's whole value proposition is that a
document re-renders to the same pixels a month later — invariants 1, 2 and 7 —
and **nothing in the suite today would notice if a runtime change altered that
by one bit**. Every reproducibility test computes both sides in the same
process, so all of them pass on a runtime that renders differently from
yesterday's. That gap is the reason to write this down before touching a TFM,
not after.

Status: **researched against primary sources, nothing built.** The container
this was assessed in has no .NET SDK installed, so no claim below has been
compiled or run. Package support was read from published NuGet manifests and
breaking changes from the official dotnet/docs lists; the items marked
UNVERIFIED are the ones that need `dotnet test` and cannot be settled by
reading.

| Item | Verdict | Basis |
| --- | --- | --- |
| Avalonia 12.1.1 on `net10.0` | safe | explicit `net10.0` dependency group in the published nuspec |
| SkiaSharp 3.119.4 on `net10.0` | safe | explicit `net10.0` dependency group |
| `Hash01`, all three copies | safe by construction | integer arithmetic only; bit-exact by language spec |
| JSON `$type`/`$id` rejection (.NET 10) | not applicable | `FrameConverter` is hand-rolled on a `kind` discriminator |
| Float→int saturating casts (.NET 9) | not applicable | no narrowing casts in the hash path |
| `System.IO.Pipes`, globalisation, publish | no entry in either breaking-changes list | dotnet/docs 9.0 and 10.0 |
| xunit 2.4.2 + Test.Sdk 17.6.0 on `net10.0` | **UNVERIFIED** | nothing says it breaks; nothing validates it |
| Transcendental drift (`Math.Cos`/`Sin`/`Sqrt`) | **UNVERIFIED, and unguarded** | no cross-runtime baseline exists to compare against |
| Linux minimum glibc | **narrows**, 2.23 → 2.27 | dotnet/core supported-os notes |
| CI's .NET 8 runtime | **inherited, not declared** | no `rollForward` anywhere; `build.yml` names only `10.0.x` |

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
mentions. Recorded as **B39**.

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

## Order

Nothing here is reversible in the sense that matters — a baseline not recorded
before the bump cannot be recorded afterwards — so the sequence is the
deliverable, not a suggestion.

1. **Make a Linux toolchain exist.** `.devcontainer/devcontainer.json` with
   the .NET 10 SDK and `libfontconfig1`. Every step below needs it and none of
   them can be done by reading.
2. **Record the determinism baseline on `net8.0`, and commit it.**
   `RuntimeDeterminismTests` is inert until its `Baseline` constant is filled
   in; filling it in is this step. Doing it after step 4 records the new
   runtime's output as the reference and destroys the only evidence that would
   have shown a change.
3. **Answer the xunit question.** Bump one test project's TFM to `net10.0`,
   run it, revert. Fifteen minutes, and it decides whether this upgrade is a
   TFM change or a TFM change plus a test-stack migration.
4. **Bump the TFMs.** Four files, not one: `Directory.Build.props` plus the
   local `net8.0` redeclarations in `Lightbox.Core.Tests`,
   `Lightbox.Raster.Tests` and `Lightbox.Ai.Tests`. `Lightbox.App.Tests`
   inherits from the props file alone. `Lightbox.Mcp` is already there.
5. **Re-run the baseline.** A match closes the determinism question. A
   mismatch is a bit-exact diff to localise, not a reason to re-record.
6. **Re-measure the budgets, slopes first.**
7. **Reconcile B32 and update the docs that assert .NET 8** — `CLAUDE.md`,
   `README.md`, and the MCP publish path so the runtime is shared rather than
   duplicated.

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
