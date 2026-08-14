# Q71 · Remove the infinite canvas? — **answered 2026-08-12: yes, capability only — the engine stays**

**Asked and answered in conversation, owner's call.** The owner cut the
infinite canvas to focus on a different direction: a simplified 3D environment
to draw in — 2D line data placed in a space that can be rotated and zoomed, no
meshes. That feature has its own design work (asked as its own questions, not
settled here).

**The scope question was the real decision**, because by removal day the
"infinite canvas machinery" was load-bearing for bounded documents. Three
options were put up:

- **(a) Capability only** *(recommended, and chosen)*: remove what an artist
  can reach — `FeatureKey.UnboundedCanvas`, its project defaults, the Configure
  toggle, the `FeatureConflict` registry whose only conflict was
  unbounded-vs-sprite-export, the exporter refusal — and keep the tile engine,
  `StrokeIndex` and B82's viewport culling, which now serve playback
  (`tileModeOn = IsPlaying`), stroke picking/selection, and every zoomed-in
  publish respectively.
- **(b) Full rip-out**: also delete the tile engine and the culling. Costs
  playback its compositor (B144/Q62 measured 145 → 14 ms a frame at 1080p),
  costs picking its index, and undoes B82.
- **(c) Hide the toggle, keep everything**: cheapest and dishonest — dead
  capability, dead tests and a maintained design doc for a feature nobody can
  reach.

**Costs of (a), recorded so they are not rediscovered as bugs:** the tiled
compositor is now reachable *only* while playing, so its live-drawing pixel
tests (`UnboundedCanvasPixelTests`) went with the feature — their regressions
that still have a reachable path were re-pinned through playback
(`ASecondPlayingPublishShowsTheSamePictureAsTheFirst`, the flatten-cache and
bake tests, all converted to toggle playback). The renames follow the same
logic: `ComposeRoute.Unbounded` → `Tiled`, `ComposeUnbounded` → `ComposeTiled`,
because a route named after a removed feature reads as dead code when it is
playback's hot path. `docs/DESIGN-infinite-canvas.md` is deleted with this
entry; Q20 and Q21 above are its decision record and are marked superseded.
