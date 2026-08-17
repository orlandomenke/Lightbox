# Q109 · Default dockers per workspace: which tab groups ship, and where — **answered 2026-08-17, all three as recommended**

Raised by: the owner — *"let's address default dockers per workspace including
tabs. So in a project, the project, reference sheets and tool options are
tabbed. The colour family is always tabbed by default on all workspaces. In
gamedev and any animation aligned workspace the xsheet, timeline and graph
editor always open and tabbed."*

What it blocked: three of the six shipped arrangements could not reach the
gradient at all, Storyboard offered no colour panel, Tool options was hidden
everywhere until the gear was pressed, and Asset library — whose deliverable is
a character cycle — opened without a timeline.

1. **The work group ships in every built-in**, Illustration included:
   **Project · Reference sheets · Tool options**, project tree in front. The
   Project tab is already absent until a document belongs to a project, so a
   single-image arrangement shows the other two and grows the third the day work
   is filed. The alternatives: **only where Project already appeared** reads the
   instruction literally and gives an illustrator who later files work a
   different arrangement from everybody else; **only the project-shaped types**
   splits one rule three ways for somebody to rediscover.
2. **Animation-aligned means Animation, Game art, Storyboard, Asset library**
   (and the Default workspace), which get **Timeline · X-sheet · Graph editor**
   open and tabbed. The first three already had it; Asset library is the one
   this added, and the reason it is not obvious is that a library reads as a
   drawer of stills while its deliverable is a sprite cycle. Illustration and
   Comic stay timeline-free: the bottom strip is screen that single-image work
   gets back, which is most of what makes those two feel different.
3. **Tool options ships docked rather than waiting for the gear.** It was hidden
   by default on the rule that a panel arrives when it is first wanted; a tab
   costs a word in a header instead of a strip of sidebar, which is the same
   trade that put the palette and the gradient in front of people. Keeping it
   hidden was the alternative, and it would have made "tool options is tabbed
   with the project" true only after the first press of the gear — which is not
   a default.

**The rule this bends, stated plainly.** `NoArrangementTabsTwoPanelsAnArtistNeedsAtOnce`
guarded two panels against being tabbed: Layers *and the project tree*, on the
grounds that both are read while drawing. Layers keeps the guard — it is clicked
during a stroke, and tabbing it trades a scroll for a click on every mark. The
project tree is reclassified: which document am I on, which sheet am I drawing
from, what is this tool set to are all questions asked *between* pieces of work,
which is exactly what the groups are for. That is a real change of position and
the cost is real too — an artist who switches documents mid-stroke sequence now
clicks a tab first. Recorded rather than smoothed over, because the previous
answer was written down with a reason and this overrides it.

**One bug the change created, found by the suite and fixed here.** The gear
called `SetVisible(ToolOptions, true)` — and the panel is now always visible, so
the call became a no-op and the gear did nothing at all, for everybody. Opening
means *bringing the tab forward* once panels ship grouped; `IsActiveInItsSlot`
is the distinction the view model was missing, and the toggle's close half uses
it too.

**What an existing install sees.** Nothing, until asked: a saved built-in keeps
whatever arrangement is in the file, because B230 made *Save current* overwrite
built-ins in place and rewriting them on load would throw that away. **View ▸
Workspace ▸ Reset** restores the shipped layout from code, which is the promise
that entry already makes. Deliberately not the quick bar's migration (B203),
where the null being filled in was written by the app rather than chosen by
anybody.
