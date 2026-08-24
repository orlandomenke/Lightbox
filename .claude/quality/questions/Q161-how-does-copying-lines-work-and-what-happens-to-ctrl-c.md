# Q161 · How does copying lines work, and what happens to Ctrl+C? — **answered 2026-08-24**

Asked when the owner requested copy/paste of lines through every selection tool
(Arrow, Direct Selection, Move, Select). Five things did not resolve against the
existing rules, so all five were prompted before any of it was built. Each is
recorded with what the rejected options would have cost.

## 1. What does a copy take when the region catches only part of a line?

| | What it costs |
| --- | --- |
| **Clip to the region** (recommended, **chosen**) | The stroke travels whole with the selection as its clip, so the paste shows exactly the boxed pixels and the record still holds one line rather than two fragments. Needs clip *intersection* when the copied stroke already carried one — see below. |
| Copy the stroke whole | Simpler, and consistent with how the transform classifies (lines move whole). Rejected because pasting would then show ink outside the box the artist drew, which is not what "copy this region" means anywhere else. |

**The intersection is the part worth writing down.** A stroke painted under an
earlier selection is only visible inside it. Giving the copy the *new* region as
its clip would replace that carve rather than add to it, so the paste would show
ink the artist has never seen — B297's resurrection bug wearing a different hat.
`ClipMeeting` ANDs the two masks and re-traces, rather than intersecting the
contours analytically: the shapes come from hand-drawn lassos and may be
concave, multiple and holed, and a mask says what a polygon library would have
to be trusted to say.

## 2. Where does a paste land?

**In place** — the same document coordinates, on the new layer. Chosen over
centring in the view and over a small offset, both of which break the case that
matters most: carrying a drawing to another frame or another shot, where landing
anywhere but the original position means lining it up by hand every time. On the
same frame the copy is invisible until moved, which is the accepted cost; the
tools to move it are already in hand.

## 3. How far does the clipboard reach?

**Across open documents, in-app.** A static holder, cloned in and cloned out.
Rejected: per-document (kills the most valuable use — pulling a character
between two shots), and an OS-clipboard JSON format (pasteable between app
instances and inspectable, but it commits to a public wire format for strokes
now, which is a serialization promise far beyond what this feature needs).

The clips travel *by value* with the strokes and are re-registered into the
target document on paste. Carrying only the id would work inside one session and
produce strokes clipped by nothing the moment the pasted document was saved and
reopened elsewhere (invariant 3).

## 4. Ctrl+C already copies a cel. Which wins?

**The selection wins; with nothing selected it is still the cel.** Chosen over
deciding by keyboard focus, and over giving lines their own keys.

The reason is what the deciding state *looks like*: a selection is on screen —
marching ants, or a highlighted line — so the key never changes meaning for a
reason the artist cannot see. Focus is invisible by comparison. The cost is
stated rather than hidden: **copying a cel while a selection is up needs Ctrl+D
first**, or the timeline's right-click menu, which is always the cel.

`Ctrl+V` asks which clipboard is *newer* rather than which has content — both
can hold something at once, and an artist means the last thing they copied.
The two share one counter (`StrokeClipboard.NextOrder`).

Line-only commands (`edit.copyLines`, `edit.cutLines`, `edit.pasteLines`) are in
`ShortcutMap` with **no default gesture**: a second default for the same act
would be a gesture nobody asked for, but a command absent from the map cannot be
seen, searched or rebound, which is the failure the map exists for.

## 5. Cut as well, or copy and paste only?

**Cut as well** — the owner's call, against a recommendation to defer it. The
recommendation was wrong about the cost, and that is worth recording: the
concern was that cutting a partial region needs the region subtracted from the
strokes that stay, which sounded like a second feature. It is not.
`DeleteSelectionContents` already does exactly that and already uses this same
precedence (B173, marquee over picked lines): a region becomes a
`ToolKind.ClearRegion` stroke so lines crossing the edge keep their outside
part, and picked lines are removed outright. Cut is therefore copy plus that
existing command, and deleting the strokes the copy took would have been the
*wrong* implementation — a line half inside the box would have vanished whole.

## What the build added that no option named

**An erasure is only copied if it carves ink also being copied.** The transform
classifier (`TransformErasures.MovingWithin`) deliberately lets erasures travel
by their raw position, because a moved erasure still sits over the drawing it
always did. A *copied* one lands on a fresh layer with nothing beneath it, so an
erasure taken on its own is an invisible stroke that erases nothing and cannot
be selected to remove (B232) — precisely the stray Q102 exists to stop. A copy
whose region holds no visible ink at all therefore takes nothing, which is also
what lets Ctrl+C fall through to the cel. Found by a test, not by reasoning.
