# Q68 · Does the distorted-silhouette smear deserve a licence? — **answered 2026-08-12: (a) no — the 2× band stays, that frame is hand-drawn**

Same review, same day, answered alongside Q67 on PR #179: **(a)**, the
recommendation. `AreaSlack = 2.0` refuses a
closed shape whose area moves past 2× the interpolated expectation in either
direction. Area-conserving squash and stretch passes — a 10:1 streak that keeps
its area is fine — and a collapse to an eighth refuses, both as designed. The
edge the review named: **a smear style that deliberately draws the silhouette
larger than the character** (≈3.5× area, to sell a fast whip in some 2D/cutout
styles) is refused as a volume gain.

The decision: **the 2× band stays the documented line.** Most smears are drawn
as separate streak strokes, which drag and interpretation already license; the
distorted-silhouette variant is rare and stays a frame the artist draws by
hand. The accepted cost: one stylised technique the AI cannot propose.

The options not taken: (b) an asymmetric band (collapse strict at 0.5×, gain
loose to ~4×) lets a model that balloons a shape mid-motion — a real
small-model failure — pass as a "smear" nobody asked for; (c) tying the band
to the latitude dial makes one dial move two unrelated tolerances, so turning
it up for looser new ink would also weaken the collapse check.
