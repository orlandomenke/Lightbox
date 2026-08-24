# Q157 · Does a soft brush build up, or hold its footprint? — **answered 2026-08-24: cap density by the footprint, and change existing art**

The soft-edged half of the defect reported in Q156. The same mechanism — dabs
composited `SrcOver` at a spacing far tighter than their own radius — hardens a
soft brush's edge instead of staggering a hard one's.

## The measurement

Size 30, flow 1, spacing 0.15. Edge width is the distance over which alpha falls
from 0.9 to 0.1, measured **across** the mark. The reference is the same brush's
own single dab, because that is the falloff the artist set:

| hardness | one dab | stroke | lost |
| --- | --- | --- | --- |
| 0.10 | 11 px | **6 px** | 45% |
| 0.35 | 8 px | **4 px** | 50% |
| 0.60 | 4 px | 3 px | 25% |
| 0.90 | 1 px | 1 px | — |

**The softest settings lose the most**, which is the wrong way round: the control
does least where it is asked for most.

## The question

Wash — combining dab coverage with `max` — restores the falloff exactly. It also
destroys flow build-up, which is not a defect but the whole of what flow means:
measured, an airbrush at flow 0.1 builds to 0.384 at the centre of a stroke and
would drop to 0.102 under wash.

| | What it costs |
| --- | --- |
| **Cap density by the footprint** (recommended, **chosen**) | Accumulate as now, but let no pixel exceed the brush's own footprint at that point. Continuous in flow, so no cliff on a slider. Needs no new brush setting. Costs a second accumulation buffer and a span pass — 1.44–1.87× on a whole-mark render. |
| **Wash only when flow = 1** | Narrowest change, cannot touch an airbrush. Rejected: a visible cliff on a continuous control, where dragging flow from 1.00 to 0.99 halves the edge width. |
| **A per-brush wash/build-up switch** | Krita's model, most expressive, familiar. Rejected for now: a new nullable brush key, a Configure entry and a preset field, and it puts a decision on the artist that should be right by default. It remains the growth path rather than a replacement. |

**The ceiling is not the same thing as wash, and the difference is the point.**
At flow 1 it binds everywhere off-centre and the mark recovers the dab's exact
falloff — measured point for point, 255/222/170/118/65/13 against the dab's
255/222/170/117/65/13. At flow 0.1 it does not bind at all, so an airbrush is
untouched, and it converges on a mark carrying the brush's own soft profile
rather than on a hard-edged blob. That is where an airbrush *should* converge.

## Existing art changes, again

Same call as Q156, and it reaches much further: soft brushes are most of any
painting, and **`BrushSettings.Hardness` defaults to 0.8** — so this is not a
change to presets somebody deliberately softened, it is a change to what the
brush does out of the box. Seven pixel fingerprints were re-recorded, and which ones moved is the
evidence that the change is scoped —

- `RuntimeDeterminismTests`: `jitter` (hardness 0.8) and `soft` (0.25) moved;
  `hard-aa` (1.0, so it takes the silhouette route) came back byte-identical.
- `PreMergeDocumentTests`: **both** layers moved, and that is honest — it is a
  document saved by an older build, drawn with soft brushes, and the old pixels
  were the defect rather than the reference.
- `BrushPresetRenderFingerprintTests`: all four shapes, all hardness 0.35.

One cost worth writing down: re-recording `jitter` means its value no longer
dates from .NET 8, so the runtime-migration evidence that class exists for now
rests on `hard-aa` alone.

## Two exclusions, one of them measured rather than reasoned

**A simulated medium is excluded.** Capping the dabs takes density out of the
soft fringe, and that fringe is exactly the paint the fluid solver needs above
its capillary entry pressure. A fully wet wash's spread fell to 1.40× against
B27's 1.4× threshold — that whole fix being quietly undone. It is also the case
where hardness is *not* the control being misrepresented: under a medium the edge
an artist sees comes from the fluid rim, and the dab falloff is one input to a
simulation rather than the shape of the mark.

**A bitmap tip is excluded**, because its footprint is the tip's own alpha and
wants the same treatment by a different route.

## What it costs live, and what that gives up

The ceiling is a property of the whole mark, so it runs in the live
post-process beside medium and wet edge rather than in the per-event fast path.
That makes a soft brush the second exception — after blur — to the manual's
promise that the mark under the pen is the mark you will have. Nothing regressed
against today's *appearance* under the pen; what regressed is its exactness.
**B293** holds the fix, which is to describe the footprint in ~32 nested stroked
bands instead of one draw per dab.
