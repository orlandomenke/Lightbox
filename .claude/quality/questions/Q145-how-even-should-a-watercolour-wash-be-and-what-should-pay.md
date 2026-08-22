# Q145 · How even should a watercolour wash be, and what should pay for it — **answered 2026-08-22: neither preset lever, fix the stroke seams first**

Raised by: the owner, on seeing splotched strokes — *"With watercolor the paper
in part will influence the look, where dimples will pool water and ink. But even
still strokes in general look more even. Right?"* Correct, and B279 has the
numbers: one broad Watercolor stroke measures 5.5% mottle against Ink wash's
0.9% and Gouache's 0.2%.

What it blocked: whether to retune the Watercolor preset, and which of three
deliberate design choices to spend.

**Recommendation was to raise `PigmentDensity` 0.5 → ~0.8**, which halves the
mottle (5.5% → 2.8%), on the argument that 0.5 conflates *transparent* with
*dilute* when transparency is already `Hiding 0.05`'s job. The alternatives
costed: `PressureWater` 0.8 → 0.3 buys about 24% (5.5% → 4.2%) and spends the
light-touch blooming a test pins; converging the projection harder buys the most
(5.5% → 3.2% at 32 sweeps) and breaks blooming outright — raising sweeps to 8
fails one guarded lattice behaviour and to 12 fails two.

**The answer went the other way, and the reasoning is better than the
recommendation's.** None of the three is bought without giving up something an
artist wants, and all three are second-order next to the actual complaint: a
*wash* — the commonest thing a watercolourist paints — is built from overlapping
strokes, and those read as ribbons with pale seams. Measured, the swatch mottles
at 6.3% where one broad stroke covering the same ground gives 3.4%. A single
stroke being nearly twice as even as the wash made of them is the wrong way
round, and no preset value fixes it.

So: leave the presets alone, and spend the effort on the cross-stroke wet window
(roadmap, Pillar 0 → Brush engine). B279 stays open behind it, because how even
a *stroke* should be is a fair question again once a *wash* is even — and the
answer may be that it needs nothing.

**Carried out, 2026-08-22.** The wet window landed: `BrushSettings.WetStrokes`,
`WetRun.Split` and one shared scratch per run, with `WetRunCommit` keeping the
interactive path and the preview agreeing with it. Six bands through the app go
from **16.2% to 1.8%** row-to-row unevenness. The presets were left alone as this
answer said, except for the window itself (Watercolor 3, Ink wash 2) — and B279
is now the whole of what is left, which is what makes it worth re-measuring
rather than fixing.
