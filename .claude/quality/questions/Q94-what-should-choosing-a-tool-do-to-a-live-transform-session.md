# Q94 · What should choosing a tool do to a live transform session? — **answered 2026-08-14: cancel it**

Asked while fixing **B194**, where Ctrl+T broke its own session: holding Ctrl borrows
the eyedropper, the `T` claims the canvas for the gizmo, and *letting go of Ctrl* handed
the canvas back to the brush with the gizmo still on screen — so the handles did nothing
and the press painted across the drawing.

The fix itself was not in doubt: a session owns the canvas until Enter or Escape, so
`SyncCanvasToolMode` must not assert the mode from the tool while a gizmo is up. What
that fix *left* undecided is the case it makes reachable — an artist deliberately
choosing a tool mid-session.

| | What it costs |
| --- | --- |
| **Session keeps the canvas** (recommended) | Coherent, and reads as broken: the rail highlights the brush and the canvas will not paint until the session ends. |
| **Choosing a tool cancels** (**chosen**) | The drag is discarded with no way to ask for it back. |
| **Choosing a tool applies** | Writes to the document from a gesture that never said "apply" — recoverable through undo, but it is an edit nobody asked for. |

**The owner chose cancel, against the recommendation, and the reasoning holds up better
than the recommendation did.** Reaching for a tool means you are done transforming; a
highlighted tool that does not work is the kind of state an artist reads as a bug rather
than as a rule. The cost is real — a drag in progress is gone — and it is the cheap side
of the asymmetry: the preview was never an edit (invariant 1), so cancelling costs a
gesture, while applying would cost a document change from a gesture that did not request
one. Enter stays the only thing that commits.

**Where it went matters as much as what it does.** The cancel lives in
`LeaveToolStateBehind`, beside the half-drawn polygon, the parked pen path, the dropped
line selection and the ended isolation session — all of which already end this way. That
placement buys the hard half for nothing: `SetToolWithoutSideEffects` suppresses the
whole method, so a *borrowed* tool cannot cancel. Without that, the fix would have eaten
its own tail, since Ctrl+T is a tool change (borrow the picker) followed by a tool change
(give the brush back), and a naive cancel-on-tool-change would cancel the session the
shortcut had just started.

**The comment above that line had claimed this behaviour since B147** — *"Every other
modal thing here behaves the same way ... the transform session"* — and the line was
never there. A comment describing a rule nothing enforces, sitting next to four rules
that are enforced, is what evidence anchors exist to prevent one level up.
