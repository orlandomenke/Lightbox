# Q137 · Shot-ready analysers: ground, range, slide, depth, camera — **answered 2026-08-20**

The walk and jump analysers (Q134) shipped with an asset lens: horizontal
ground at the lowest ink, the whole layer as one repeating cycle, constant
depth. Right for sprite cycles; wrong for shots, where movement is dynamic —
slopes, tilted layouts, travelling walks, motion toward camera, and a camera
of its own. The owner asked whether they could carry broader utility; five
decisions were prompted together.

**The ground: an authored line guide, any angle, with today's derived
horizontal as the fallback** (recommended, accepted). A `Line` guide named
"ground" defines the ground; contacts, bob and jump gravity measure along its
normal. The pivot-vs-centroid pattern again: the artist's statement wins, the
guess fills in. Fitting a line to the contact points was declined — a
staircase fits one wrong line, and it is a guess judging the artist.

**The unit of analysis: the tag under the playhead, else the whole layer**
(recommended, accepted). Range tags already exist for engine clips and are
the natural "frames 30–70 are the walk"; no new record key, no new UI.

**Loop checks gate on the tag's own `Loop` flag, else on being in-place; and
foot-slide detection lands** (recommended, accepted — sharpened during
implementation: the prompt offered an in-place heuristic, and the tag record
turned out to already carry an authored `Loop` flag, which wins over the
heuristic for the standing authored-beats-derived reason). A travelling shot
walk skips the seam checks it was never meant to pass, and gains the check
that matters there: during a contact, the planted foot must hold a constant
rate along the ground — zero for a travelling walk, the tread rate for an
in-place one — which is the classic foot-slide error the asset slice could
not see.

**Depth: hedge, don't flag** (recommended, accepted). When ink size drifts
past a band across the analysed range, the walk and jump analysers report
"reads as depth motion — the flat fit doesn't apply" instead of findings.
Perspective-aware ballistics needs depth the record does not have; knowing
when not to judge is the honest extension.

**Camera-space reading: included in this slice** (the owner chose this over
the recommended deferral; the cost accepted is a roughly doubled slice).
"Through the camera" projects each drawing's subject by that frame's
`CameraFraming` — what the audience sees. One scoping judgement follows from
what the numbers mean rather than from preference, recorded here because it
was not separately prompted:

- **Camera-space applies to the spacing assistant and the jump arc** — both
  ask "how does it read on screen", which a moving camera genuinely changes.
  Spacing targets are computed in camera view and mapped back through that
  frame's inverse framing, so the ghost ticks still sit on the world canvas
  and the nudge still moves world geometry — to the place whose *projection*
  is evenly spaced.
- **The walk checks stay world-space always** — whether a foot slides,
  whether contacts land evenly, whether a cycle closes are physical facts a
  camera move cannot change, and judging them through a pan would flag
  correct animation.
- The jump arc's camera-space verdict carries a hedge ("as the camera sees
  it") because world-correct gravity can legitimately read as a non-parabola
  under a camera move — that is information, not an error.
