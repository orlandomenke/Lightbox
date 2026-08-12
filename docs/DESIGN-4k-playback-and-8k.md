# Brush performance beyond 4k: what the GPU can and cannot buy us

*Findings report, 2026-08-08. Measurements are from `tools/Lightbox.Bench` on
the dev container (reference loop 6 ms, software Skia); the milliseconds are
the container's, the growth exponents are the code's.*

*Status at writing: rung 1 (display-bounded Full quality, PR #79) and rung 2
(layer-stack baking, PR #80) are implemented; rung 2 measured 65.7 → 12.3 ms
per whole-canvas publish on a 16-layer document. Rungs 3–6 are analysis only.*

*Update 2026-08-08: rung 3's first increment is in — playback on bounded
documents goes through the existing tile store (`tileNativeDoc` in
`PublishSnapshot`, guarded by `PlaybackThroughTilesTests`). Measured on the
sparse-cel sweep: 145 → 14 ms a frame at 1080p, 334 → 39 ms at 4K, both inside
the 12 fps budget. The rest of rung 3 — tile-level eviction, scrubbing and the
paused canvas, the still-full-frame fallbacks — remains open under B144/Q62.*

---

## 0. The target, stated by the owner

> **4k animations must play back fluidly — ideally with many, many frames — on
> decent to high-end hardware.** 8k/300dpi single-image work is the stretch
> goal; 4k *sequences* are the requirement.

That target changes the emphasis of everything below. Playback is a frame
change every 83 ms (12 fps) or 42 ms (24 fps), so the binding constraints are
the two cliffs of §1 — full-recomposite cost and frame-cache capacity — not
stamping:

- **Compute:** a 4k frame change measured ~230 ms on the container (n^1.04 by
  megapixels). On high-end hardware call it ~60 ms — right at the 12 fps budget
  with nothing left for 24 fps or for onion skin. Rung 1 (display-resolution
  compose) plus rung 2 (layer-stack caching) put it comfortably under budget,
  because a 4k document plays back through a ≤2 MP window on any real monitor.
- **Memory — this is where "many, many frames" bites.** One 4k RGBA frame
  bitmap is 33 MB, so the 512 MB cache holds ~15 frames; a 200-frame scene
  cannot ever be resident. The scan-aware eviction (B28) already keeps
  sequential playback from destroying the cache, but half-resident is the
  ceiling today. Rung 3 (tile store) is what makes long sequences cheap —
  most cels in real animation are sparse (a character on empty paper) and
  holds, symbols and untouched tiles share storage instead of each costing a
  full-canvas buffer. With display-scale caching (rung 1) the resident set
  shrinks by another factor of zoom².

So the target does not add new rungs — it *reorders the payoff*: rungs 1–3 are
not "nice for 8k", they are what makes a 300-frame 4k scene play at speed on
the hardware you named.

## 1. Where the time actually goes today

The pipeline, stage by stage:

| Stage | When it runs | Cost grows with | Measured |
| --- | --- | --- | --- |
| Dab stamping (`BrushEngine.StampStroke`) | per stroke | brush area × dabs, **not** canvas | n^0.05 against canvas size — 9 ms at 33 MP (8k) |
| Live preview repaint (`ComposeRing`) | per pointer event | dirty region (dab-sized) | area-independent since B121 |
| Full recomposite (frame change, undo, scrub) | per frame change | **canvas MP × layers** | n^1.04 by MP, n^0.99 by layers — 1 050 ms at 8k, ~18 ms/layer at bench res |
| Frame re-rasterise on cache miss | cache thrash | strokes per frame | n^1.06 — 1 670 ms at 100 strokes |
| Upload to screen | per repaint | dirty tiles since B122 | fixed for strokes; viewport-sized on frame change |

Three conclusions fall straight out of the table:

1. **Drawing itself is already solved.** A stroke commit is bounded work — brush
   area times dab count — and measured at 9 ms on an 8k document *on the slow
   container*. This is invariant 6 holding. No GPU is needed to draw on 8k.
2. **Frame changes are the cliff.** Every layer bitmap is document-sized, so a
   scrub, an undo, or a playback step recomposites megapixels × layers. At 8k
   that is ~1 s per frame change here, and on your machine (call it 4× faster)
   still ~250 ms against an 83 ms playback budget.
3. **Memory is the second cliff, and it arrives with layers.** One 8k RGBA
   buffer is 133 MB. The frame cache budget is 512 MB — under four full-canvas
   bitmaps for the *whole application*. A three-layer 8k scene fills it with one
   moment of one animation; the next frame evicts, the frame after replays
   strokes from scratch (the 1 670 ms row). This is why your instinct is right
   that multilayer artwork will fall off sharply while single-layer line work
   feels fine: single-layer at 4k sits comfortably inside the cache; multilayer
   8k lives in permanent cache-miss.

So the honest problem statement for 8k/300dpi is **not** "stamping is too slow
on the CPU". It is "full-canvas buffers are too big to hold and too big to
re-blend".

## 2. The proposal you pasted, claim by claim

> *"Rasterization overhead: converting strokes into millions of pixels at 8K
> forces the CPU to do heavy math for every pixel."*

**True for frame changes, already false for drawing.** The per-stroke path
never touches most of those pixels (n^0.05 above). The advice diagnoses the
right disease in the wrong organ.

> *"Blit bottleneck: pushing a fully rendered 8K frame from RAM to screen
> saturates CPU-to-GPU bandwidth."*

**Was true; largely fixed.** The tiled dirty-region upload that landed earlier
this week means a stroke uploads only the tiles it touched. What still pays a
big blit is a frame change — which the compose-side fixes below shrink at the
source.

> *"Option 1 — SkiaSharp GRContext: switch surfaces to GPU, no rewrite needed."*

**Half true, and we are further along than the advice assumes.** Avalonia
already hands us its GPU context through the Skia lease
(`CanvasControl.RunWithGpuContext`, `MaxTextureSize`, and the opt-in GPU-backed
`PresentedFrame` behind `LIGHTBOX_DURABLE_FRAME` all exist today). The parts
that genuinely port for free are **compositing and presentation**. The parts
that do not:

- **Determinism.** Invariant 2 requires a stroke to re-render identically on
  load, undo and AI inbetween, and `RuntimeDeterminismTests` pins the render
  bit-for-bit. GPU rasterization is not bit-identical across drivers, GPU
  generations or OSes. A file painted on your Huion laptop would re-render
  *almost* the same on another machine — which for an animation replayed frame
  by frame is flicker, the thing the invariant exists to kill.
- **Read-back media.** Smudge and the simulated media read target pixels back
  per dab (`StampSmudge`, the medium passes). On a GPU that is a
  round-trip-per-dab, which is *slower* than the CPU path — Skia's own GPU
  backend degrades badly under sample-what-you-just-drew workloads.
- **Skia's GPU blending** is exactly the "custom-blended raster dabs" caveat
  the advice itself names — and our dabs are heavily custom-blended.

> *"Option 2 — Silk.NET/OpenTK compute shaders: the GPU stamps thousands of
> dabs simultaneously."*

**Overpromised.** Dabs within a stroke are order-dependent — alpha compositing
does not commute, and smudge carries state from dab to dab. A compute shader
cannot stamp them "simultaneously"; it can parallelise the *pixels within* a
dab, which is what any GPU rasteriser already does. The real content of this
option is rewriting ~20 brush dynamics, the blend modes, the AA and three
simulated media as shader code, then keeping two implementations
visually-identical forever. That is a rewrite of the engine's soul for a
bottleneck that (see §1) is not where the advice thinks it is.

> *"Option 3 — Veldrid: best cross-platform native API."*

**Dated.** Veldrid has been effectively unmaintained for years; recommending it
for new work in 2026 is a red flag for the whole document's currency. The
modern equivalent in .NET-land is VelloSharp (bindings over Vello, a compute
2D renderer) — genuinely interesting, still young, and its Skia-shim covers
paths and fills, not our per-pixel media.

> *"RGBA16F/32F layer framebuffers in VRAM prevent banding."*

True and expensive: an 8k RGBA32F layer is **530 MB**. Four layers of that is
your whole VRAM on a mid-range laptop. Not for us at 8k.

**Verdict: the ceiling it describes is real, the prescription order is wrong
for this codebase.** It proposes starting where the risk is largest (GPU
stamping) to fix a cost we do not pay (per-stroke rasterisation), while
skipping the two document-shaped problems (compose cost, buffer memory) that
GPU stamping would not fix — a GPU still cannot hold thirty 133 MB layer
buffers.

## 3. The ladder I would actually climb

Ordered by win-per-risk. Each rung is independently shippable and none breaks
an invariant until the last.

**1. Compose at displayed resolution.** Today `ComposeScale` is a quality
*setting* (Full/Half/Display); the fix is making it automatic: compose at
`min(1.0, zoom × dpiScale)` — the resolution the screen can actually show.
Zoomed out on an 8k document (the normal state — no monitor shows 33 MP), a
frame change composes ~2 MP instead of 33 MP: **the 1 050 ms row becomes
~65 ms, a 16× win, from arithmetic alone.** Invariant 7 makes this legitimate:
output scale is a canvas transform, geometry is never touched, so it is
deterministic and view-only. It also dissolves your Full-vs-Half dilemma — at
100%+ zoom you get today's Full quality, zoomed out you get Full's sharpness at
Display's cost, because downscaling *after* composing (today's Full) and
composing at the downscaled size produce the same picture on screen. Needs:
zoom-aware scale plumbed where `renderScale` already flows, a re-compose on
zoom-in, and the frame cache keyed by scale.

**2. Layer-stack caching (Krita's "projections").** You edit one layer at a
time. Bake `below-active` and `above-active` once per layer-switch; every
repaint then blends **3** bitmaps (below + active + live) instead of N. The
n^0.99-by-layers row goes flat: 24 layers cost what 3 cost. Pure CPU, fully
deterministic, moderate effort — and it compounds with rung 1 (the baked stacks
are display-scale too).

**3. A real tile store.** Frames become sparse grids of 256/512 px tiles
instead of document-sized bitmaps. Memory follows *ink* rather than canvas —
an 8k document that is mostly paper costs a fraction of 133 MB per frame, the
cache evicts tiles rather than whole frames (ending the replay-from-strokes
disaster), and only touched tiles re-composite or upload. This is the
structural fix for 8k-and-bigger, it is what the unbounded-canvas TODO in the
compose path already asks for, and it is the largest single piece of work on
this list. Krita's entire large-canvas story rests on exactly this.

**4. GPU compositing of the final stack.** With tiles (3) and few blend inputs
(2), let the GPU do what it is unbeatable at: blend the visible tiles and apply
pan/zoom/rotation on present. The machinery is half-built — dirty tiles already
upload, `PresentedFrame` already holds a GPU surface behind a flag. **No
determinism risk**: this composits for *display only*; export and the stroke
record stay CPU-canonical. This is the one place the pasted advice
(SkiaSharp-on-GRContext) is right, applied where it is safe.

**5. GPU live-stroke preview (hybrid), only if latency still bites.** Stamp the
*in-flight* stroke on the GPU for feel; on pen-up, the commit re-stamps
CPU-canonically — which is already how live preview and commit are split
today. Any GPU/CPU divergence exists only while the pen is down, on one
machine, and is overwritten at commit. Feel of GPU painting, record stays
deterministic.

**6. Full GPU stamping — a decision, not an optimization.** Only if 1–5 are not
enough (I expect they are). It requires answering, in `QUESTIONS.md` terms:
*is the document's canonical appearance defined by the CPU render, with GPU
allowed to differ imperceptibly per machine?* That is a real weakening of
invariant 2 and of `RuntimeDeterminismTests`, it makes cross-machine flicker
possible in principle, and it still has to keep the read-back media (smudge,
watercolour) on a compute-shader diet nobody has costed. High effort, real
risk, and by rung 5 the user-visible payoff is likely zero.

**Cheap side quests**, orthogonal to the ladder: SIMD the per-pixel medium
loops (`System.Numerics.Vector` over the mask/height loops in `BrushEngine`),
parallelise tile-sized chunks *within* a large dab across cores, and coalesce
pointer events when a repaint is already in flight.

## 4. Bottom line

- **Will the proposal do what it promises?** Its diagnosis of 8k costs is
  half-right, its remedy order is backwards for this codebase, its library
  advice is stale (Veldrid), and it is silent on the one constraint that makes
  naive GPU stamping wrong here: deterministic replay is a product feature
  (undo, load, AI inbetweens, no flicker at 12 fps), not an implementation
  detail.
- **Can we get flawless 8k/300dpi and bigger?** Yes — and rungs 1–4 get there
  without touching the determinism invariant. Rung 1 alone turns the worst
  measured number (1 s per frame change at 8k) into a frame-budget number, and
  rung 3 makes "or bigger" true in the only sense that scales: cost follows
  what you drew and what you can see, never the size of the paper.
- **Can 4k animations with many, many frames play fluidly on decent-to-high-end
  hardware?** Yes, and by the same three rungs — see §0. Compute is solved by
  composing at display scale with a flat layer count (rungs 1–2); long
  sequences are solved by tiles making sparse cels and holds nearly free
  (rung 3). Playback never needs GPU stamping either.

*Sequencing note: 1 → 2 are weeks-scale each and pay immediately at 4k too;
3 is the big rock and unlocks both 4 and the unbounded canvas; 5–6 wait for
evidence they are needed.*
