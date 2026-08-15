# Q19 · Are Linux and macOS shipping targets, or only development ones? — **answered (a)**

**Answered 2026-08-04: (a), development targets only — Windows is what ships.**
The glibc floor is accepted and closes as not-applicable, on this question's own
reasoning rather than in spite of it: `build.yml` publishes exactly one artifact,
`win-x64`, so nothing crosses the floor and a rising one cannot lose a user who
has nothing to download. Anyone on Linux today built from source and therefore
has a .NET SDK, which puts their distro far above either number.

The consequences, so they are not re-derived: the `net10.0` upgrade is unblocked
and has landed; **B32**'s fix points **up** (the solution moved to `net10.0`
rather than the MCP server moving down to `net8.0`); and a `linux-x64` publish
job stays the separate concern `DESIGN-net10-upgrade.md` files it as, rather than
becoming part of the upgrade. Revisit if a Linux or macOS artifact is ever
shipped — that, not the glibc number, is the thing that would make the floor
matter.

**Blocks:** the `net10.0` decision in `docs/DESIGN-net10-upgrade.md`, and
whether **B32**'s fix points up or down. *(Both now resolved by the answer above.)*

The upgrade is otherwise clean. Avalonia 12.1.1 and SkiaSharp 3.119.4 both
publish explicit `net10.0` dependency groups, every .NET 9 and .NET 10 breaking
change on the official lists was checked against real code and none apply, and
.NET 8 leaves support in November 2026 — so standing still is also a decision
with a date on it. One consequence needs a person: a self-contained `net10.0`
Linux build requires **glibc 2.27** (Ubuntu 18.04-class) where `net8.0` needed
**2.23** (Ubuntu 16.04-class). Windows and macOS floors do not move.

**The reason this is a question and not a footnote is that it cannot be
answered from the code.** It depends on who runs this, and nothing in the
repository records that.

What the code *does* say is that the floor is currently theoretical.
`build.yml` publishes exactly one artifact, `win-x64`, cross-compiled from
Ubuntu. **There is no Linux build and no macOS build shipped at all**, so
today a rising Linux floor cannot lose a user who has nothing to download.
Anyone running this on Linux right now built it from source, which means they
have a .NET SDK, which means their distro is far newer than either floor.

So the glibc number is the wrong thing to decide. The thing to decide is
whether the missing Linux and macOS artifacts are an omission or a choice —
because that is what makes the floor matter, and it is also what decides
whether the publish-path half of **B32** should grow a `linux-x64` job.

**(a) Development targets only — Windows is what ships.** Take the floor; it
costs nothing measurable, because nothing crosses it. Linux stays what it is
today, the place the tests run and the Windows bundle is built. The devcontainer
serves that fully and the glibc question closes as not-applicable.

**(b) Shipping targets, not yet built.** Then the floor is real but still
almost certainly fine — Ubuntu 18.04 left standard support in 2023, and an
application that wants a tablet and a GPU is not being run on an eight-year-old
distro. Worth saying out loud rather than assuming, and it makes a `linux-x64`
publish job part of the upgrade rather than the separate concern
`DESIGN-net10-upgrade.md` currently files it as.

**(c) Stay on `net8.0`.** Keeps the floor and keeps the smaller diff, which is
**B32**'s own prescription. It buys three months and pays a migration's
verification cost twice — once to prove a downgrade changed no pixels, again in
November to prove the upgrade did not.

**Recommend (a)**, on the evidence that the only artifact anyone can download
is a Windows one and no issue in the tracker asks for another. It is the one
reading that makes the glibc floor a non-question rather than a small risk
taken quietly — and if (b) turns out to be the truth, the floor is still very
likely fine and the thing that changes is scope, not safety.

**Blocks:** the last `[?]` but one in Pillar 3.

The pillar lists *Reusable animation presets* and *Animation templates* as
separate from the Animation library — but the Animation library shipped, and
what it delivers is a multi-frame symbol placed with a frame offset, which is
already a reusable animation. Two placements of one cycle run the same drawings
out of step. Whatever these two items are for, it is not that.

The reading that survives is that they are about **timing rather than
drawings** — the part of frame-by-frame work that a symbol does not carry:

- **(a)** *Strike it.* The Animation library is the reusable animation, and
  these two lines are a pre-implementation guess that the design outgrew. A
  roadmap that keeps items nothing can distinguish from shipped ones is the
  wish list this file's checkbox rules exist to prevent.
- **(b)** *A timing preset* — a saved exposure pattern (on 1s, on 2s, a
  slow-in of 1-1-2-3-4) applied to a selected range of cels, re-exposing the
  drawings that are already there. This is a real animator's tool, it is
  genuinely absent, and it is nothing a symbol can express, because a symbol
  carries drawings and this carries their spacing.
- **(c)** *A motion preset* — keyframed placement transforms, so a symbol can
  be told to arc across the frame over twelve cels. This is the largest of the
  three and it needs a decision about whether placements become animatable at
  all, which is a pillar-4 question wearing a pillar-3 hat.

**Recommend (b), and strike the other line as (a).** One item, specified:
*"Timing presets — save an exposure pattern and apply it to a range of cels."*
It is the only one of the three that is both absent and unambiguous.
