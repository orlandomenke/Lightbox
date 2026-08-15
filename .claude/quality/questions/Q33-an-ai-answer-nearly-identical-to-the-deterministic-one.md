# Q33 · An AI answer nearly identical to the deterministic one — reject or report? — **answered (a)**

**Answered 2026-08-07: (a), report only, never reject.** Distance from the
deterministic answer is a cost signal and a diagnostic, never a veto.

**Blocks:** nothing.

The deterministic engine is both the fallback and the reference, so distance
from it is free. Too far is suspicious. Too close means the model added nothing.

**(a) Report only, never reject.** Agreeing with the cheap engine is not
incorrect. Surface it as a cost signal — *"this model added nothing on 9 of 12
frames"* — and let the artist decide.

**(b) Reject and fall back.** Cleaner cost story, at the risk of throwing away
answers that were right.

**Recommend (a).** Rejecting a correct answer for being unimaginative is
indefensible on correctness grounds, and the cost argument is fully served by
saying so out loud. The threshold for "nearly identical" is also exactly the
sort of number that gets tuned until it passes.

**Blocks:** nothing — this can be added after phase 0.
