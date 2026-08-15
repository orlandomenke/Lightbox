# Q63 · GPU compositing is at its measurement gate — measure, build past it, or switch axes? — **answered 2026-08-10: switch to the layer axis (B165)**

B125 stages 1 through 4 have landed: the lifetime protocol, the pixel-identity
harness, the pass list crossing to the render thread, the culled composite moving
into the draw op, and a GPU surface behind `LIGHTBOX_GPU_COMPOSITE=1`. Stage 4 is
deliberately a **gate** rather than a feature — it uploads every layer every frame,
which is the worst case by construction, and the number that decides whether that
is a 20× win or a 3× one can only come from real hardware. There is no graphics
context in this repository, which is the same reason B122 shipped as an inference
and the render report exists at all.

So the question was genuinely the owner's to answer, and three options were put:
run the measurement first, build stage 5's residency blind, or leave GPU work at
the gate and attack the other axis.

**The recommendation was to measure first.** Stage 5's design is what has to carry
the whole win if the upload dominates, and building it before knowing that means
committing to an invalidation strategy on an assumption.

**The answer was to switch to B165**, and it is a better call than the
recommendation for a reason the recommendation underweighted: B165 is **fully
testable in this repository**, and stage 5 is not. Every line of a resident-texture
cache would land unguarded here, which is the opposite of what the last six pull
requests have been about — and B165 attacks the axis GPU compositing does nothing
whatever for. Ten layers at 4K is 224% of the playback budget *after* a 20× GPU
win. The two axes multiply, so the second one has to be answered regardless of what
the first measures.

**What the choice costs, stated so it is a decision rather than a drift.** Stage 4
remains unmeasured, so `LIGHTBOX_GPU_COMPOSITE` stays an opt-in nobody has taken
and B125's checkbox stays open on a stage that is *built but unproven*. That is a
real hazard: code that exists and has never run on the hardware it was written for
rots quietly, and the longer the gap the more likely the first real run finds
something the CPU fallback was hiding. The mitigation is that the measurement is
one render report whenever the owner wants to spend five minutes on it — it does
not need a session, and it does not block B165.
