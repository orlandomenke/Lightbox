# Q97 · With both a marquee and a line selection up, which does Ctrl+T take? — **answered 2026-08-16: the marquee, always**

Asked while fixing **B223**, which made a line selection transformable for the
first time. That created a state the application had never had to resolve: two
selections of different kinds, both live, both able to narrow a transform.

They are genuinely different things — a marquee is a *region* and the picked
lines are a set of *records* — and Q48 settled long ago that the two tools stay
separate for exactly that reason. Nothing said which wins when both are up,
because until now only one of them could.

| | What it costs |
| --- | --- |
| **The tool in hand decides** (recommended, *not* chosen) | An arrow in hand means the lines, anything else means the marquee. Reuses `ObjectSelectionIsTheSubject`, which `Ctrl+A` and `Ctrl+D` already share, so a third command could not drift from those two. |
| **The marquee always wins** (**chosen**) | Predictable without looking at the toolbar — and a marquee left up somewhere off screen silently outranks the lines you can see highlighted. |
| **Lines always win** | The same failure mirrored: a line selection left up narrows every transform, and picking up the brush would not change what `Ctrl+T` means. |

**The owner chose the marquee, against the recommendation.** The reasoning that
supports it is real: a rule stated as "the marquee is what a transform narrows
to" can be held in one sentence, where the tool-in-hand rule needs you to know
which tools count as arrows, and the answer changes under you when you switch
tools without touching either selection.

**The cost is precise and it is not hypothetical.** A marquee is invisible when
it is off screen or scrolled away, and `Ctrl+D` is documented as the answer to
"the brush seems to have stopped working" for exactly that reason. The same
outline can now silently decide what a transform moves. The recommendation's
advantage was that the tool in your hand is always visible in the rail; that is
what was traded away.

**So the choice obliges something the alternative did not**, and it is part of
this fix rather than a follow-up: `TransformSubject` names what the live session
took — *"the selection"*, *"3 selected lines"*, *"this drawing"* — and the
status line says it when a session opens and again when it commits. A precedence
whose failure mode is silent has to stop being silent; that is the price of
picking the order that can surprise you, and it is cheap.

**A line selection is not intersected with the marquee**, under any of the three
options. Two filters ANDed would give a transform that moves neither the region
you marqueed nor the lines you picked, and there is no way to show that on a
canvas — the moving/static split `PartsFor` renders would be honest about it and
still incomprehensible.

## What this did not decide

**A drag on a picked line is not governed by this**, and B223 says so at the call
site. A menu command asks *what is selected*; a direct drag asks *what am I
holding*. Letting a marquee outrank the line under the pointer would mean
grabbing a line and watching something else move, which is not a precedence
question — it is a broken gesture. `BeginLineMove` therefore passes its filter
explicitly rather than deriving it.
