---
name: brush-measurement
description: How to measure a brush without fooling yourself — alpha saturation along a stroke, why a test of flow can pass on a build where flow is wired to nothing, and what to measure instead. Read before writing or debugging any test that renders a stroke and reads pixels back.
---

# Measuring a brush: the saturation trap

This has cost real time three times. It is short, and it is the difference
between a test that proves a control works and a test that passes either way.

**Dabs overlap, so alpha along a stroke saturates.** A brush at `Spacing = 0.05`
lays about twenty dabs on every pixel, and twenty dabs of flow `a` come out at
`1 - (1-a)^20` — which is **0.92 at a flow of 0.12**. So a test that sets flow
to 0.1, renders, and reads the alpha down the middle of the stroke gets 0.93
and concludes the control works, when a brush at flow 1.0 also reads 1.00 and
the two are a hair apart. The same test passes on a build where flow is wired
to nothing.

This has now cost real time three times — B26's depletion tuning (`Reach` 24 →
50 → 12 before the overlap was noticed), and twice since in tests that had to
be rewritten. The rules:

- **Measure below saturation, or not along the stroke.** Either use values low
  enough that `1-(1-a)^n` is still climbing (flow ~0.01–0.02 at ordinary
  spacing), or widen the spacing so the dabs barely overlap, or measure
  something that does not accumulate — stroke *width*, a profile across the
  stroke, the ratio between two places on the same stroke.
- **Always print both numbers.** `output.WriteLine($"faint {a:F3}, full {b:F3}")`
  is what turns "the assertion passed" into "0.929 vs 1.000, which is nothing".
- **Sanity-check the other way.** Assert the faint mark is *present* as well as
  fainter; a test that only checks `a < b` also passes when `a` is zero because
  the brush is broken.

The general form is the lesson `docs/DESIGN-performance.md` records for
measurement: **the number was real and the attribution was not.** Ask *what
else is in this measurement* before *what is wrong with the code*.
