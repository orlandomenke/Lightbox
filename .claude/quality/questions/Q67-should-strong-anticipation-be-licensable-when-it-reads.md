# Q67 · Should strong anticipation be licensable when it reads exactly like a copied key? — **answered 2026-08-12: (a) keep the band; anticipation is authored, in a breakdown**

Raised by art-director in the G12 review of the Phase 0 verifier (2026-08-12),
prompted in-conversation, and answered the same day on the `[needs a decision]`
PR (#179): **(a)**, the recommendation.

The betweenness band refuses a matched stroke sitting more than ~40% of its own
travel from where interpolation puts it. That number was calibrated so a copied
key refuses — the failure a small model most often produces — and it does its
job. The cost the review measured: **a strong anticipation pose drawn into an
inbetween is geometrically the same signature** — deviation opposite the
travel, similar magnitude — so anticipation past roughly a third of the travel
is refused, and the verifier cannot tell a directorial choice from the failure
it exists to catch. On an 80px swing, 30px of wind-back passes and 55px is
refused as "did not stay between the keys".

The decision: **the band stays as calibrated, and anticipation is routed
through authorship.** An artist who wants a strong anticipation draws it as a
breakdown, which Phase 1 makes a hard constraint the arc must pass through;
the copied-key refusal — the commonest small-model failure — stays intact.
The accepted cost, recorded so it is not rediscovered as a bug: **the AI
cannot invent strong anticipation mid-run**, only follow one the artist
stated, and a model that tries will see "did not stay between the keys".

The options not taken: (b) widening `TravelSlack` admits anticipation
everywhere and reopens the copied-key hole the band was tuned against;
(c) a shape signal — a copied key matches the key's *shape* near-exactly,
real anticipation redraws it — stays the upgrade to prototype **if (a)
pinches in practice**, and (b) stays rejected even then.
