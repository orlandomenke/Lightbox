# Q229 · Should opening a document load the file off the UI thread — **answered: yes, an "Opening…" tab with a loading animation**

**Answered 2026-10-09: yes. The new document's tab appears at once, named and
marked as opening, with no canvas to draw on until it arrives — and ideally a
loading animation rather than a static label.**

The owner's words: *"option 1 but ideally with a loading animation?"*

Raised 2026-10-09 by Claude from a profile of opening the lab's 30-layer,
200-drawing document. Parsing the file holds the UI thread about 2.5 s cold
(after #654 made each drawing parse once), on top of the first frame's render
(made parallel by #650) and building the window.

What it blocks: the rest of the opening freeze (roadmap item 4 of the
large-documents work).

**Recommendation was the chosen one.** The alternatives:
- keep the previous document live and switch when the new one arrives — nicer,
  but an edit racing the switch is a way to lose work;
- leave it — 2.5 s once per open is tolerable, and the effort could go to
  flipping and painting.

The animation is a design detail to settle against `docs/design/ui-reference.png`
when it is built: something that says "working" without implying a percentage
the parser cannot honestly report.
