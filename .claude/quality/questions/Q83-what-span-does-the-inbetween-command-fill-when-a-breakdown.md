# Q83 · What span does the inbetween command fill when a breakdown sits inside it? — **answered 2026-08-14**

**Asked with the question prompt, three questions in one pass, all three
recommendations taken.** Phase 1's second half in
`docs/DESIGN-ai-correctness.md` asked for "`FrameRole.Breakdown` as a hard
constraint — the arc must pass through it", and measuring first found the
premise already met for the wrong reason: `ExposureSheet.NextKeyIndex` finds
the next *drawing* whatever its role, so a breakdown was already the interval's
endpoint and the arc passed through it trivially. There was no constraint to
add.

What the measurement did find was a **disagreement between two notions of a
span** living in the same timeline:

| | closes a span at |
| --- | --- |
| the inbetween command | the next **drawing** |
| `SpacingChart.Intended` | the next **extreme**, or the end of the sheet |

On key(0) · breakdown(1) · key(2) the chart overlay saw one run of 0→2 while
the command saw an interval of 0→1. Two consequences: the easing restarted at
the breakdown, so one slow-out/slow-in across a run came out as two with a
stutter in the middle; and a Q58 timing chart authored on the opening key meant
different things in the two places.

1. **The span is the run** — extreme to extreme, through breakdowns, matching
   `SpacingChart`. One action fills every gap, one undo step. It is what an
   animator means by "inbetween this span", and it makes the two notions agree.
   The cost is a behaviour change for anyone already using breakdowns, accepted
   because the old behaviour was the stutter.
2. **A timing chart spans the run**, the traditional ladder, which is what
   `SpacingChart` already read it as. A rung is a position across the run and
   lands in whichever gap contains it; a rung that merely describes a drawing
   already there asks for nothing.
3. **The AI path does not follow, for now.** `✦ AI Inbetween` keeps asking one
   gap at a time. A third drawing in the request is roughly +50% strokes and
   strokes are the dominant token cost (`docs/DESIGN-ai-payload.md`), and
   `InbetweenVerifier`'s betweenness check would have to become piecewise —
   which is Phase 3/4 work. The breakdown remains a hard constraint for the AI
   regardless, because it is still that gap's endpoint.

**The one thing (3) costs, recorded so it is not rediscovered as a bug:** the
two producers now disagree about the span, which is exactly the disagreement (1)
existed to remove — just moved. It is bounded (the AI's frames are still
correct, just spaced per gap) and it is written into `docs/manual/12-ai-assistance.md`
rather than left for an artist to notice.

**Nothing is ever moved.** Each waypoint keeps its frame and its pose, and a
renormalized local fraction cannot leave its own gap, so no inbetween between
the key and the breakdown can show a pose from beyond it. A breakdown that
disagrees with the easing is not corrected here — `SpacingChart` exists to
*show* that disagreement, and silently re-spacing a drawing the artist placed
would be the application arguing with them.
