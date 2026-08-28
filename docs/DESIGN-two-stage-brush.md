# Why a brush stamps twice, and when it does not

**This question has been asked more than once and the answer has never been
written down, which is the actual defect.** Recorded 2026-08-27 on the owner's
observation that *"apparently it does not stick"*. If you are about to explain
the two-stage brush from first principles again, explain it from here instead,
and add what you learn.

## The short answer

**Most brushes already are a single pass.** The second stage exists for exactly
five things, and every one of them is a property of the **whole mark** rather
than of a dab. `MainViewModel.Painting.NeedsLivePostProcess` is the list:

- a simulated medium (watercolour, gouache, oil, ink wash)
- a wet edge
- a paper texture
- granulation
- the footprint ceiling of a soft brush

A hard round brush, a pencil, an eraser, an inking brush: `NeedsLivePostProcess`
is false, no pass is scheduled, the dabs go into the scratch and that is what
you see. The render report says so in as many words — *"live tip not applicable
— no post-process pass ran"*. **There is no double draw on those brushes at
all**, and a capture taken with one says nothing about B322 for that reason.

## Why the five cannot be done per dab

Because they are **retroactive**: a dab laid down now changes pixels that were
already laid down.

| effect | what a later dab does to earlier pixels |
| --- | --- |
| footprint ceiling | The ceiling is a running **maximum** over the whole stroke. Capping per dab feeds a clamped value back into the next dab's accumulation, so the mark darkens where it crosses itself — the exact fault Q157 settled and B294 is the fast-path version of. |
| wet edge | The rim belongs to the **mark**, not to a dab. Ink you laid a second ago was the rim then and is the interior now, so its pixels have to change. |
| granulation, paper texture | The field is indexed from the **document** corner so two strokes crossing the same patch sit on the same tooth. That is a property of where the mark is, not of the order its dabs arrived. |
| simulated medium | The fluid lattice redistributes pigment across the mark and reads the layer beneath for re-wetting. Pigment that moves has to come from somewhere already painted. |

Compute any of these forward, dab by dab, and the live mark stops matching the
commit — which `LiveMatchesCommittedTests` holds to within 1 part in 255, and
which is the promise that *a stroke looks while you draw it the way it will look
when you let go*.

## So why does Photoshop or Krita not need this

**They do not have this class of effect in the brush.** A Photoshop round brush
accumulates forward: a new dab never changes an old pixel, so a single pass is
not an optimisation, it is simply what the definition allows. The difference is
not *they chose one pass and we chose two* — it is **where the effect is
defined**. Define the wet edge per dab and you get a single pass for free, and
you also get a rim around every dab instead of around the mark, granulation that
tiles per dab, and a soft brush that darkens where it crosses itself.

That is a decision about what the marks look like, not about efficiency. It is
available, it has never been asked for, and it would be a different painting
application.

## What the artist actually feels, which is not the second pass

The second pass is not slow in itself — a healthy capture puts it at **12.9 ms
reading 2.2% of the mark** (B331, 15:05). What hurts is that it runs on a worker
thread and the screen shows the mark **as of the last pass that finished**, so
during a fast stroke the newest dabs are missing. That is **B322**, and it is a
latency problem rather than a throughput one: the same brush with the pass
keeping up shows the preview under the nib the whole way.

Three things follow, and they are the ones worth keeping straight:

1. **Making the pass cheaper helps** — B313 made it read only the band that
   moved, B331 is the case where that stops engaging.
2. **Making the preview cover the gap helps** — B322's live tip.
3. **Merging the two stages helps nothing**, because the gap is not the second
   stamp, it is the fact that the effects cannot be finished until the mark is.

## The one honest cost of the split

The work is done twice over a stroke's life: once for the live preview, once at
the commit. That is inherent to previewing anything and is not what the pass
costs mid-stroke — the commit's copy happens at pen-up, where nobody is waiting
on a pointer event. It is worth stating because it is the thing "double draw"
most naturally describes, and it is the half that has never been a problem.

## If someone wants to remove the split anyway

The question to answer first is not *can we merge the passes* but **is the
footprint ceiling still a running maximum**. Everything else follows from that
answer, because it is the effect whose whole-mark definition is load-bearing for
the ordinary soft brush rather than for a specialist medium. Q157 is where that
was decided, and the decision has held.
