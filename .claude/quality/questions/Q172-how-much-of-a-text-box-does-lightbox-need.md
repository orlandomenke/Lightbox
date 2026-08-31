# Q172 · How much of a text box does Lightbox need

Raised by: the owner, 2026-08-29 — *"I do not like the current implementation of
the on canvas text tool. Photoshop indicates and actively needs the user to
double click with the arrow or hover with the text tool over the bounding box
and click to enter the bounding box. It also offers the option to select text
that way. Ours is currently limited."*

What it blocks: every part of the text tool's interaction, because the answer
decides whether the box an artist enters is **measured** or **authored**.

## The question

`TextElement` is point text: `X`, `Y`, `Size`, `Align`, `Tracking`,
`LineHeight`, and no width. Lines break where the artist typed `\n` and nowhere
else. Photoshop's "enter the box" gesture is usually its *paragraph* box, which
has an authored width and rewraps when you drag its edge.

So "show the bounding box and let me click into it" has two readings that differ
by an order of magnitude:

1. The box is the **measured extent** of what was typed. `TextLayout.Box`
   already computes it. Nothing is added to the record; nothing is serialized
   for a document nobody has typed in.
2. The box is **authored**. `TextElement` gains a width, `TextLayout.Of` gains a
   line-breaking pass, and the box gains a resize gizmo that reflows the words.

## Recommendation: (1), the measured box, with (2) filed

Everything the report actually names — indicate the box, click into it, select
text — works on a measured box, and it is where the tool is limited today:
`TypeAt` hit-tests against **glyph outlines**, so clicking the gap between two
letters starts a second text block on top of the first, and picking type up
always puts the caret at the end. Fixing that is most of the felt improvement
and touches no serialized field.

What (2) costs, and why it is not free:

- **A new authored field on a document type**, which `optional-settings` governs:
  a width has to be absent from the JSON of every document that never set one,
  or every existing document grows a key for a feature it does not use.
- **Line breaking is a layout engine, not a loop.** Where a line breaks decides
  where the caret goes, which decides what a click selects — so the interaction
  work sits on top of it either way, and getting wrapping wrong shows up as the
  caret landing in the wrong place rather than as text looking odd.
- **Two kinds of type in one tool.** Point text and paragraph text behave
  differently on the same gestures (dragging the box edge moves one and rewraps
  the other), and every one of those differences is a thing to explain in the
  manual and a branch in the code.

## Answered: (2) — build the real paragraph box

**The owner chose the authored box, against the recommendation.** Recorded here
with what it costs so the choice is legible later rather than looking like the
obvious thing to have done.

The reasoning it answers to is the one in `CLAUDE.md`: type that cannot be given
a column is type an artist has to break by hand, and a title block or a caption
column is exactly the case where "Lightbox does not do that" sends the work to
another application. A measured box would have been the cheap answer to the
complaint as phrased and not to the complaint as meant.

**Also decided in the same exchange, both with the recommendation:**

- Text selection gets **the standard set** — drag a range, double-click a word,
  Shift+arrows to extend, Ctrl+A, and typing or Backspace replaces the
  selection. Half-working selection is worse than none.
- Clicking anywhere inside the box picks the type up, rather than only a hit on
  a glyph outline. The box you can see is the box that responds.

## How it is being built

Two branches, because they are two objectives and the second rests on the first:

1. **Enter the box, and select** — box hit-testing, a caret placed where you
   clicked, the selection set above, the box drawn while hovering with the Text
   tool, and a double-click with the Arrow to enter. No record change.
2. **A paragraph box that reflows** — the authored width, wrapping in
   `TextLayout.Of`, and a resize gizmo. This is the one that touches the
   document, and it is where `optional-settings` applies.

Doing them the other way round would mean a resizable box nobody can click
into. Doing (1) first costs no rework: the selection and caret code is written
against `TextLayout`, which does not care *why* a line broke — wrapping simply
produces more lines.
