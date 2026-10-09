# Q236 · Paint load reaches further than twelve diameters: the reach is continuous in the load — **answered 2026-10-09**

**Answered: make the reach continuous**, as recommended — `Size × 6 × load / (1 − load)` pixels per e-fold, so a half load keeps the feel it had, 0.9 lasts nine times as long, and the reach goes to "never" continuously as the load goes to 1. Taken knowing it moves existing art: every saved stroke drawn with a load below 1 re-renders with the new reach, including strokes made with the shipped Oil (0.85) and Bristle (0.6), whose presets are re-tuned on the same branch so a new stroke keeps the feel it had.

Raised by the B434 branch (the shipped watercolour runs out along the stroke), 2026-10-09, while choosing the Watercolor preset's load. `BrushEngine.LoadAt` depleted paint as `exp(−travelled / (Size × 12 × load))`, which made the slider a cliff: at exactly 1 the brush never ran out, at 0.99 it was at 2 % after about nineteen diameters, and a long gentle wash — forty diameters, say — was not reachable at any setting. Measured 0.7, 0.8 and 0.9 on the wash and they were nearly the same stroke, which is how the cliff was noticed.

What it blocks: the Watercolor preset's load meaning anything beyond "runs dry within twenty diameters"; an artist's ability to ask for a long wash that still fades.

## The two options as put to the owner

- **(a) Make the reach continuous** — recommended and taken. Cost: an art change on saved documents drawn with a load below 1, the B349 kind — the fix reaching old strokes rather than a change of taste. The shipped Oil and Bristle presets are re-tuned so a new stroke keeps its feel (Oil 0.85 → 0.63, Bristle 0.6 → 0.55: the load whose new reach equals the old one, `d / (d + 6)` for a reach of `d` diameters), and the Watercolor lands at 0.6, re-measured.
- **(b) Leave the reach as it is.** The shipped Watercolor at 0.85 worked within it. Cost: long washes always run dry within about twenty diameters, and a wash that lasts means load exactly 1 and no fade at all.

Asked and answered in the conversation with the question prompt, in the same exchange that gave the word to merge PRs #661, #663 and #666.
