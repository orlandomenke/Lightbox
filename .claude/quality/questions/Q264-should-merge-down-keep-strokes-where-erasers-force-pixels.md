# Q264 · Should merge down keep strokes where erasers force pixels? — **answered 2026-10-13**

**Answered: ask at merge time — keep strokes (nearly exact) or flatten (exact) — with a "remember this choice" box.** The recommendation was to cut strokes always; the owner chose to be asked, with the answer rememberable.

Raised by the owner on 2026-10-13, on finding that the merged line art in their AI test documents had become pixels: *"Oh is merging down layers rasterizing the strokes? … Illustrator can have path joining, wouldn't that be similar to merging? … Merging down 2 layers can discard any erased stroke, keeping the end result — merged layers won't need the history."*

What it blocks: making a merged line-art test document for the AI inbetweener at all — and, more generally, merging line art without losing it as line art.

## What was found first

Merge down already keeps strokes wherever it can be exact. What forced the owner's drawings to pixels was erasers: an eraser is recorded as a stroke that removes whatever is under it, so merged into the layer below it would start removing that layer's marks too. Nearly every line drawing has erasing in it, so in practice the rule meant merged line art was always pixels.

The owner's proposal is the vector answer: apply each eraser to the drawing's own strokes as a cut, keep what is left, and drop the eraser. Lightbox already did the coarse half of this before sending strokes to a model (`StrokeRecordCleaner` drops strokes an eraser mostly covered).

## What was asked, and what was answered

**Should merge cut the strokes where erasers crossed them and keep strokes?** Recommended: *cut strokes, nearly exact*, always. **Answered: ask at merge time, with a remember-my-answer checkmark.** Against: always flattening, with folders as the way to keep line art usable.

## What it costs — each accepted by name

- **A soft or half-strength eraser becomes all-or-nothing.** A cut cannot fade.
- **The ends of a cut are the brush's own round end.** Each piece stops where its dab would first touch what the eraser took, so ink does not come back into the gap and the corners of each cut come out slightly short. The first version pulled back a fixed half-width along the line, which is right only for a square crossing — the adversary pass measured 94 pixels of ink inside the erased area at 45° and 339 at a shallow slant. Measured now: none at either.
- **An eraser that only shaved a line's edge**, never reaching its centre, leaves the line whole — the shaved sliver returns. The same holds where an eraser runs along a line after crossing it: the retreat from a cut is bounded at four half-widths.
- **A textured brush restarts at the cut**, so its grain shifts there.
- **Merge no longer promises pixel-exact** when strokes are kept. Measured rather than promised: 2.7% of painted pixels on a deliberately hard case (a 24 px line and a 30 px eraser), against 9.2% for dropping the eraser and 9.2% for letting it eat the lower layer; 0.00%, 0.11% and 0.27% on three merges of the owner's line art.
- **Some drawings still flatten** whatever is chosen: a blend mode, opacity, clip, alpha-locked or smudge stroke — and every case where a cut would not be the picture, which is refused rather than approximated: an erasure that reaches a fill, gradient, text, blur or wet-media stroke; an erasure used inside a selection (the renderer confines it there); anything drawn with symmetry or tile wrap (its copies erase, or are erased, elsewhere). The last two were the sensitivity review's blockers: the first build cut them by the eraser's path alone.
- **Time.** The first build took 3.8 s on a real layer of 117 strokes with 23 erasures, and ran the cut three times per merge before anything showed. It now takes 0.13 s, decides whether to ask without cutting at all, and cuts a held drawing once.
- **Drawings already flattened stay flattened.** This changes future merges; a trace-to-strokes rescue for pixels is a different feature.
