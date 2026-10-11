# Q247 · Busy bars: plain accent or the splash gradient — **answered: plain accent**

**Answered 2026-10-11: keep the plain accent.** The in-app busy bars — opening a
document, preparing a loop for play — stay the stock accent colour, like every
other control. The gradient stays a splash-screen flourish.

Raised 2026-10-11 by the UI review of the buffering indicator (playback phase 4,
Q214). The design reference's only busy bar is the splash's "Loading brushes",
filled with a coral → magenta → violet gradient over a faint track; the two
in-app bars use the Fluent accent, so they agree with each other and not with
the splash.

What it blocks: nothing. Both in-app bars now share one style
(`ProgressBar.busy` in Density.axaml), so if this is ever revisited it is one
change that both pick up.

**Recommendation was the chosen one.** The alternative was the splash gradient on
both bars: a small branch of its own, with a UI review.
