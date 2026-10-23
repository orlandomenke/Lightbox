# Q290 · How should a docker be split by a drop? — **answered 2026-10-23: its trailing quarter splits**

Raised by: the owner, 2026-10-23 — "the right side has more drop targets than
the left; on the right I can split the sidebar so four dockers live there, the
left does not seem to have that logic."

**A probe found the logic identical on both sides.** With three dockers stacked
on each side, a sweep of `DockZones.Resolve` down each strip gives the same
targets: the body joins as a tab, and an 18 px `InsertBand` at the docker's
edge inserts. What made the left feel different is that the left starts empty
and the right starts with four dockers. A lone docker has exactly one split
target, the 18 px band along its bottom, because the header covers the top
band and claims it for joining. (The probe also found B500, a real left-only
off-by-one, and Q291.)

What it blocks: splitting a sidebar is a target too thin to find on either
side, so in practice a second docker on the left tabs into the first.

| | What it costs |
| --- | --- |
| **The trailing quarter of each docker splits** (recommended, **chosen**) | The join target shrinks from nearly the whole body to the leading three-quarters. Narrows the earlier "the body is one target" call rather than reversing it. |
| Keep the 18 px bands | Nothing to build. Splitting stays fiddly on both sides. |
| Dock guide arrows over the docker | Clearest of the three, and a new overlay to build and theme — roughly a day, not an hour. |

## What was built

The bottom quarter of a docker in a **side** strip inserts a new slot below it
(`DockZones.SplitShare`). Three things came out of review:

- **Not the top and bottom strips.** The bottom strip is the timeline, whose
  right-hand quarter is frames an artist aims at; the question was about
  sidebars. Those strips keep the 18 px sliver.
- **The leading band is unreachable under a header.** The header is ~31 px and
  wins over the 18 px band, so a split always goes *after* the docker dropped
  on. That was already true on the right and stays so; the manual says it.
- **A full side joins instead.** `DockLayout` refuses a fifth slot and tabs the
  panel in, so the split preview on a full side showed an outcome that never
  happened. On a side with `MaxSlotsPerSide` slots the split zone now offers
  the join the drop will actually be, on the docker under the pointer.

The preview band is unchanged — as deep as the dragged panel wants, at the
boundary — so it marks a split, not the exact zone it came from.
