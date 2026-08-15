# Q82 · Docker-dependent shortcuts: what falls back over a docker, and how the editor shows it — **answered 2026-08-14, both against the recommendation**

Prompted while closing the gaps in hover-scoped shortcuts. The model
(`ShortcutScope`, panel-scoped bindings, `I` = insert-key over the timeline)
was already built and tested; what was missing was everything between the
pointer and the resolver. Two forks came out of that, one behavioural and one
about the screen an artist reads the rule from.

1. **The fallback chain over a docker is panel → canvas → general.**
   *Recommended: keep panel → general.* The chain used to stop at the docker's
   edge, so `Delete` over the Colour docker did nothing at all. The argument
   for stopping there was that a destructive key firing on the canvas from a
   place that gives no hint the canvas is the target is a bad surprise with
   nothing on screen explaining it. The argument that won: an artist's model is
   *the docker overrides, everything else still works*, and a key that goes
   dead over eleven of twelve dockers contradicts it — "a panel overrides, it
   never blocks" is now the sentence in the manual.

   **What it costs, accepted knowingly.** `Delete` and `Backspace` over any
   docker that does not claim them now reach the canvas and change the
   *drawing* — the selection's contents cleared, or flooded with the
   background — while the pointer is over a swatch or a layer list. Arrow keys
   likewise nudge from any unclaimed docker. The mitigation is that the dockers
   where this would be most surprising already claim those keys (Layers owns
   Delete, Backspace and the up/down arrows; the timeline owns left/right), so
   the exposure is the dockers with no key bindings at all. `ShortcutFallbackChainTests`
   pins all three rungs and the order between them.

   **One thing this does not do:** the general scope (`ShortcutScope.General`,
   "nowhere in particular") does *not* pick up the canvas rung. It is
   deliberately the narrowest scope there is, and a marquee-clearing `Delete`
   reachable from it would be the bad surprise the recommendation was worried
   about, with none of the compensating model.

2. **The Configure window groups shortcuts by scope, not by category.**
   *Recommended: a scope badge per row, keeping the category grouping.* The
   defect either option fixes: two rows bound to `I` — "Color picker tool"
   under Tools, "Insert keyframe at playhead" under Timeline — read as the same
   key bound twice, with nothing saying one of them only answers over the
   timeline. The feature worked and the one screen that explains it described
   something else.

   **What it costs.** "Everywhere" is now much the largest group — most
   bindings are general — and commands an artist thinks of together are split
   across headings when they happen to differ in scope. Two things buy that
   back, both under test rather than asserted in a comment: category still
   *orders* within a group, so the wide one is not a wall
   (`TheWidestGroupIsStillOrderedByCategory`), and every heading carries a line
   stating its rule, so the chain is read rather than inferred. Groups are
   ordered widest-first, so the page reads top to bottom in the order the
   resolver actually walks. The scope is searchable too — it became a heading,
   and typing the name of a group was otherwise the one query that emptied the
   list.

**The third thing found on the way, which needed no decision.** A docker torn
out into its own window answered *no shortcuts at all* — not its own, not the
general ones — because `FloatingPanelWindow` wired no key handling and the main
window never saw the press. Docking it again brought them back, which is what
made it read as a mystery rather than as a missing handler. Every test of this
feature called `IdFor` directly, so the resolver was correct, tested, and
unreachable from half the places a docker can be; `HoverShortcutScopeTests`
drives the real window instead and fails on all three floating cases without
the fix.
