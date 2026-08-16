# Q23 · How does a tab say whether its document belongs to a project? — **answered (a)**

**Answered 2026-08-04: (a), a badge on the tab.** What was asked for, and the
whole of the reported need: self-contained, no OS interaction, sitting exactly
where the ambiguity is. The window title (b) is deliberately not taken now —
Avalonia sets it per window rather than per tab, so with several tabs open it can
only ever describe the active one, and that is a second design rather than a
free addition.

Worth building *after* **B67**, not before: when dockers become document-scoped
the panels visibly change as tabs switch, and the badge is what stops that
reading as a bug. Filed as roadmap work rather than a bug — nothing is broken,
something is absent.

**Blocks:** nothing.

Reported alongside **B67**: "there is no good way to identify open documents
(tabs) as part of a project or not. A small boxed P in the tab would already
help a lot. In the title bar of the OS would be a great additional position."

The reporter has proposed a design, which makes this a question about *how far*
rather than *whether*. It matters more once B67 lands, because when dockers
become document-scoped the panels an artist sees will change as they switch
tabs, and a visible reason for the change is what stops that reading as a bug.

**(a) A badge on the tab.** What was asked for. Self-contained, no OS
interaction, and it sits exactly where the ambiguity is.
**(b) Badge plus the window title.** The title bar is where every other
application says which file is open, and Avalonia sets it per window rather than
per tab — so with multiple tabs it can only describe the active one. That is
probably fine and is worth saying out loud rather than discovering.
**(c) The project name rather than a badge.** More informative and much wider;
tabs are already short on room.

**Recommend (a) first**, because it is the whole of the reported need and is
cheap, with (b) as a follow-up once B67 makes project membership matter visibly.
