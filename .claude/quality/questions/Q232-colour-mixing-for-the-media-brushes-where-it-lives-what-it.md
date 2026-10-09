# Q232 · Colour mixing for the media brushes: where it lives, what it mixes with, the medium, the arithmetic — **answered 2026-10-09**

**Answered:** all four as recommended — a **mixing option on the paint path**,
mixing with **the layer beneath and the stroke's own earlier dabs**, the
simulator **seeded from the dabs' own colours**, and the **pigment model** for
the arithmetic.

Raised by the owner, 2026-10-09, after the investigation of why the simulated
media "look like ass" next to Clip Studio Paint: *"Now start on the CSP-style
colour mixing for the media brushes."* The investigation
(`docs/DESIGN-colour-mixing.md` carries it) found that CSP has no fluid
simulation at all; its realism is per-dab colour mixing with the ground under
textured tips, and Lightbox had no per-dab colour pickup for a Paint brush —
`Pickup` had been reserved since B23.

What it blocks: the media presets reading as paint rather than as a tinted
blob; every later decision about the fluid lattice, which can only be judged
once the dabs it is fed carry real colour.

## The four decisions

1. **Where it lives — a per-stroke `Mixing` option on the paint path**, on by
   default in the four media presets and reachable from any Paint brush.
   Alternative: make the media presets Smudge-kind brushes with `ColorRate`,
   which exists today and costs no engine work — but loses the medium, the
   paper texture, the wet edge and the dab-opacity semantics, and a tap paints
   nothing. It is a different brush, not the media brushes mixing.
2. **What a dab mixes with — the layer beneath and the stroke's own earlier
   dabs** (CSP's *mix ground colour*), so one stroke carries colour wet-into-wet.
   Deterministic in dab order, which both the commit and the live path already
   keep. Alternative: the layer only — cheaper, and a stroke never mixes with
   itself, so dragging through your own wet paint does nothing until the next
   stroke.
3. **Under a simulated medium — the lattice is seeded from the dabs' own
   colours**, cell by cell from the stamped scratch, instead of one uniform
   stroke colour. This is what lets mixing, colour jitter and tips survive the
   simulation; it also restores jitter to existing media documents, so the
   render fingerprints for those presets are re-recorded with the reason beside
   each. Alternative: leave the simulator — old renders stay byte-identical and
   the shipped media presets stay flat.
4. **Arithmetic — the engine's Kubelka–Munk `Pigment`**, so yellow into blue
   goes green rather than grey, which is what reads as paint and matches CSP's
   *perceptual* mode. Alternative: the straight sRGB lerp the smudge brush uses
   (`MixPigment`, misnamed) — cheapest, and it mixes complements toward grey.

## Not a preference, whichever way these went

The carried colour has to be checkpointed at the live path's settled boundary
exactly as `SmudgeCarry` is, or the live mark stops matching the commit — the
promise `LiveMatchesCommittedTests` holds to a part in 255. And symmetry copies
take the identity pass's mixed colours rather than sampling at their own
position, for the reason `Hash01` seeds from authored coordinates: the copy
*is* the mark, moved.
