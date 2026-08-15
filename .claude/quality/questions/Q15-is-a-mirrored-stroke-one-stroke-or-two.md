# Q15 · Is a mirrored stroke one stroke or two? — **answered (c)**

**Answered 2026-08-07: (c), one stroke while drawing with an explicit "break
symmetry" that expands to two.** So `Mirror` lives **on the stroke**, not on the
scene — that is the part this answer actually settles, and the reason it could
not be deferred.

Turning symmetry off is meaningful while the stroke is whole: it removes the
reflection rather than leaving an orphan. Breaking symmetry is a deliberate,
undoable act that writes two ordinary strokes and forgets the pairing, which is
correct — after the break they are two marks and pretending otherwise would owe
the artist a promise nothing keeps.

**Blocks:** nothing. Symmetry can be built.


Symmetry does not exist yet and it should — for character design, which is what
this application is for, a vertical mirror is not a nicety. What has to be
decided before anything is written is what the *record* holds when an artist
paints with a mirror on.

**(a) One stroke, rendered twice.** `Stroke.Mirror` names an axis; the engine
stamps the dabs and their reflections. The record stays the size of what was
drawn, and turning symmetry off afterwards is meaningful — it removes the
reflection rather than leaving an orphaned copy. Invariant 1 pushes here: the
mark is one gesture, so one entry.

**(b) Two strokes, emitted at commit.** Simpler in the engine, and the artist
can then edit, erase or transform the halves independently — which they
frequently want, because symmetry is usually a scaffold rather than a promise.
The cost is that the record has no memory of the pair, so "turn symmetry off"
cannot mean anything and the two halves drift as soon as either is touched.

**(c) Both — (a) while drawing, with an explicit "break symmetry" that expands
to (b).** Probably where this ends up, and worth naming as a target rather than
arriving at by accident, because it decides whether `Mirror` is on the stroke or
on the scene.

The reason it is a question rather than a guess: (a) and (b) are not
interchangeable later. A file written under (b) cannot be read back as (a), so
picking the easy one first forecloses the other.
