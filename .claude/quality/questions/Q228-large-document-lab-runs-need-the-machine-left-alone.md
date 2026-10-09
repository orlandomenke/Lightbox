# Q228 · Large-document lab runs need the machine left alone — **answered: run now, the owner steps away**

**Answered 2026-10-09: run the lab now; the owner steps away for it.**

Raised 2026-10-09 by Claude, after the perf lab's input had been refused since
late morning (`SendInput` error 5: the session locked, or an elevated window in
front). The lab drives the real app with synthetic mouse and keyboard, so it
needs an unlocked session nobody touches for about an hour, and a run started
while the owner is at the machine takes over their pointer.

What it blocked: app-level numbers for the still cache holding the frame on
screen (item 1, B425) and for the neighbour warm (#655), which had tests and
review but no measurement.

**Recommendation:** run now while away. The alternatives were to ship item 1
unmeasured (fast, but it changes the default picture budget from an eighth to a
quarter of RAM without evidence) or to wait for an evening slot.

Standing practice that follows: the lab is started only when the machine has
been idle for minutes (`GetLastInputInfo`), never on a guess that the owner has
left.
