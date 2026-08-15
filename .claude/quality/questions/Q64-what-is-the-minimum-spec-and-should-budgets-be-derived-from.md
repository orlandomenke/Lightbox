# Q64 · What is the minimum spec, and should budgets be derived from it? — **answered 2026-08-10: indie 2D game work on 8 GB with integrated graphics; budgets derived, not chosen**

Raised by the owner while reviewing the GPU and cache work, and it is a
correction rather than a question: **every performance decision in this session
had been reasoned from one laptop.** A Ryzen 7 PRO 5850U with 32 GB and shared
graphics memory is not the machine this has to run on — it is one sample, and
the conclusions drawn from it were being treated as facts about the product.

Two specific consequences the owner named, both correct:

- **A path that is bad on that machine is not necessarily bad in general.** The
  5850U shares memory between processor and card, so an upload competes with the
  compositing beside it; a discrete card crosses PCIe once and then blends from
  dedicated memory. Residency (B167 phase 5) helps the second case *more*, so
  killing it on the first machine's number would repeat B125's mis-aim exactly.
- **Budgets tuned to 32 GB are wrong in both directions.** `FrameBitmapCache`
  held 512 MB and `LayerTextureCache` 192 MB, both constants chosen while looking
  at that machine. On a 64 GB workstation they leave performance unclaimed; on a
  minimum-spec laptop they are more than can be spared.

### The minimum spec, from the production flow rather than from a spec sheet

**Indie 2D game work.** The output is often 4K, but the *documents* are sprite
sheets and character cycles, which are typically well under it — so the floor
has to make a sprite document comfortable rather than make a 4K film document
possible. The machine: an ordinary laptop, integrated graphics, **8 GB of RAM**.

That is what every floor in `MemoryBudget` is chosen against, and it is what
makes "works on minimum specs" checkable instead of a hope.

### The rule: derive, clamp, allow an override

A fraction of what the machine actually has, floored so the minimum spec works
and ceilinged so a large machine is not handed more than it can usefully spend.
The artist's setting stays the final word; this fixes the *default*, which is the
thing that was wrong. Frame cache takes an eighth (1 GB on the minimum spec,
4 GB ceiling); layer textures a sixteenth, deliberately meaner because on
integrated graphics they are the same memory the compositor is competing for;
tiles a thirty-second, both because a tiled frame holds only the tiles a stroke
touched and so buys far more frames per byte, and because **the three budgets are
additive in the worst case** — an eighth plus two sixteenths is a full quarter of
an 8 GB laptop, which is a machine that swaps rather than one with a fast cache.
`MemoryBudgetTests.TheFloorsAreAffordableOnTheMinimumSpec` is where that sum is
checked, and it is the test that caught it.

**The artist's floor is allowed below the derived floor, and that is deliberate
rather than an inconsistency the clamps failed to catch.** The derived floor is
what a minimum-spec machine needs for the cache to be worth having at all; the
setting's floor is how far somebody may go when they have decided they would
rather have the memory back — which is exactly what the Configure page offers in
its own words. The *ceiling* is shared, because past it the cache holds bytes it
will never spend no matter who asked for them.

**What it cannot see is VRAM**, and there is no portable way to ask. System
memory is the proxy: exact on integrated graphics, an underestimate on a discrete
card — which errs toward not exhausting it, and a refused allocation falls back
to the processor rather than failing.

### The cost of generalising, so it is a decision rather than a reflex

Every alternative path is one more thing that can rot, and **none of the GPU
paths can be exercised in this repository at all**. The mitigation is to
parameterise one implementation rather than branch into two — which is what the
composite already does, taking a surface whose provenance is the only difference.
Generalising by *guessing* at machines nobody has measured would be the same
error in a new direction; the render report is what turns guesses into data over
time.
