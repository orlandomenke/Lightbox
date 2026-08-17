# Q115 · Instrument or guess at the Huion hover flicker? — **answered 2026-08-17: instrument first**

Raised by the owner's report of 2026-08-17, which added a second symptom to
**B126** (the OS pointer flickering over the brush ring while a Huion pen
hovers): hover flyouts are unresponsive to the pen and often collapse the
moment they open — filed as **B254**. Both smell like one mechanism seen from
two places: enter/leave churn, or a second (emulated-mouse) device alternating
with the pen stream, would flip the cursor *and* light-dismiss a fresh flyout.

| | What it costs |
| --- | --- |
| **Instrument first** — ship an input event trace (device type and id, event kind, enter/leave, cursor decisions, timestamps) in the diagnostics surface; the owner hovers for a minute and sends the log (recommended, **chosen**) | One round-trip to the owner's machine before any fix lands. |
| **Try mitigations blind** — `SetWindowFeedbackSetting` per window, plus suppressing emulated-mouse events while a pen is in proximity | Faster if the guess is right; a second round-trip *and* a muddied ledger if not — and B126's entry already records one confident wrong cause surviving a whole exchange because nobody asked it to distinguish the cases it did not explain. |

**Why the trace is the fix's first half rather than a delay.** Nothing about
this is reproducible in this repository — no pen, no Windows — so any fix must
be checked on the reporter's machine *anyway* (B126 says exactly this). A trace
turns that one required round-trip into the one that identifies the mechanism,
instead of spending it on a guess.

**What the trace must capture to decide between the candidates:** per event —
device type and pointer id (two ids alternating is the emulated-mouse
hypothesis confirmed), enter/leave transitions on the canvas and on any open
popup (churn is the flyout hypothesis), what `RefreshCanvasCursor` decided and
when, and timestamps throughout. It rides the existing diagnostics surface
(the pen-pressure readout / diagnostics console), gated off by default.
