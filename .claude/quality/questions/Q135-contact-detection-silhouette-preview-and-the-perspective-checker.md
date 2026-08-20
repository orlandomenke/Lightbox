# Q135 · Contact detection, silhouette preview, and the perspective checker — **answered 2026-08-20**

The next three Pillar 4 `[?]` items after the analysers (Q134), prompted
together before any was written. Four decisions.

**Contact frame detection: a command that writes markers, and feeds the jump
arc** (recommended, accepted). "Detect contacts" reads the ground-band logic
the walk analyser already carries across the layer and places named frame
markers ("contact") as one undo step — authored, visible in the timeline,
export-optional like every other marker. The jump arc analyser then trims its
fit to the airborne stretch between contacts when contacts exist. Detection
runs on request, never silently per refresh: continuous auto-markers were
declined because they write to the record without being asked and fight any
contact the artist marks by hand; view-only rings were declined because
nothing downstream could use what was found.

**Silhouette readability preview: a view-only mode, no metric** (recommended,
accepted). A registered toggle renders the active layer's ink as solid black
on white — the classic pose-reading check — riding the existing render-filter
machinery. The artist's eye is the judge: a readability score was declined
because any single number for "does the pose read" is a guess wearing a
number, and it invites trusting the number over the eye.

**Perspective checker: authored vanishing points first, inferred ones as the
fallback — both** (the owner chose both options over the
recommended authored-only, to cover more bases). On demand, the checker
measures near-straight strokes against the document's vanishing-point guides
and flags a line that almost-but-not-quite converges (within an angular
band). Where no VP is declared, it clusters the ink's own directions to infer
candidate vanishing points and judges against those — presented explicitly as
inferred, because an inferred horizon is a guess judging the artist, and the
authored guide always wins where both exist. The accepted cost: inference can
be wrong on drawings with no dominant perspective, so its findings must read
as suggestions, never as errors. Live per-stroke checking stays declined —
the ruler's snapping already covers the live case, and a warning per stroke
is the nagging that gets a feature switched off.

**Branching: one branch per feature** (the owner chose this over continuing
on the analyser branch, and explicitly authorized the new branches).
Contact detection stacks on the analyser branch, whose `WalkCycleAnalyser`
and `JumpArcAnalyser` it consumes; the silhouette preview and the perspective
checker branch off `main`. The cost accepted: the stacked branch waits on the
analyser PR, and three PRs replace one.
