---
name: scope-call
description: Decide whether a proposed feature belongs in Lightbox and how far to take it — reach versus configuration, how far to simulate a medium, per-stroke versus per-preference, what to do when the answer is not there. Read when scoping a feature, triaging a wish list, arguing about a project type, or before raising a question in .claude/quality/questions/.
---

# Does this belong, and how far does it go?

The two rules that settle most of this — *optional means absent, not disabled*
and *every feature is reachable* — are in `CLAUDE.md` because they are needed
constantly. What follows is the worked reasoning behind them, and the protocol
for the case they do not settle.

There is a third thing that is neither, and it is the one to refuse:

> **Every feature is reachable in every project type. A project type sets
> defaults, never availability.**

An artist doing a comic who wants an exposure sheet gets one; somebody drawing a
single illustration who wants a camera can have it. A project type decides what
is *on*, what is *in front of you*, and what a new document starts with — never
what the application can do.

These two rules govern different things and both hold at once:

| Rule | What it governs |
| --- | --- |
| *Optional means absent, not disabled* | The **record** and the **UI**. Unused writes no keys and shows no controls. |
| *Every feature is reachable* | The **capability**. Nothing is locked behind a value in a manifest. |

The camera is already the proof of all three: absent from the file until
authored, absent from the UI until asked for, and askable for anywhere. So when
a feature arrives framed as "this is for feature film" or "this is for games",
that describes **which project type turns it on by default** — not who is
allowed
to have it. `ROADMAP.md` → *Reach and configuration* carries the plan and the
one
place the codebase currently breaks this.

Most scope questions answer themselves once asked against these. Some worked
examples, so the reasoning is reusable:

- *"How much of Photoshop's brush panel should we take?"* — the parts that
  change how a **mark reads** and that `.abr`/`.kpp` files actually carry.
  Not the parts that only pay off on a single illustration you will spend a
  day on, because every one of them also has to survive being replayed across
  two hundred frames.
- *"Should a simulation be allowed?"* — yes, if it is deterministic. A stroke
  is replayed on load, on undo, and by the inbetweener; a mark that cannot be
  reproduced exactly is not a mark, it is a one-off. This is why invariant 2
  is absolute rather than a preference.
- *"Should this be per-document or per-preference?"* — if it reaches pixels,
  per stroke. An artist who returns to a scene after a month must find it
  exactly as they left it.
- *"Is flicker acceptable?"* — no. An effect that varies subtly between
  similar strokes looks fine on one image and boils at 12 fps. Anything
  stochastic must be seeded from geometry, not from an index or a clock.
- *"How far do we go simulating a medium?"* — **as far as the expression goes,
  and not one step further.** This is a drawing application, not a physics
  paper: we are not recreating watercolour, we are giving an artist the part
  of watercolour that makes a mark say something. Where a cheap approximation
  and an accurate simulation look the same to a person, the cheap one is
  correct and the accurate one is a defect. Krita's engine pushes further than
  most and still leaves this on the table; the edge is in the *expression*
  rather than in the fidelity, and chasing fidelity at the cost of a frame
  budget spends the advantage rather than earning it.
- *"Should this expensive brush option exist at all?"* — yes, if an artist
  would reach for it deliberately, and **no if it becomes the default**. The
  costly options are opt-in, they live on presets, and the picker badges them
  (`BrushCostOf`, derived from the settings so it cannot lie) so the trade is
  made knowingly. Every simulated medium also ships a fast counterpart — a
  medium nobody can afford is a trap, not a feature.

Two things that read as the same word and are not: **visual variation** is
wanted, **logical randomness** is forbidden. Marks should differ the way real
media differ — because of where they are, what they are on and how fast the
hand moved. That is invariant 2 restated from the artist's side rather than a
constraint fighting it.

When a request genuinely does not resolve against these, it belongs in
`.claude/quality/questions/` rather than in a guess — **one file per question**,
`python3 scripts/questions.py new "<title>"` to raise one, and
`.claude/quality/QUESTIONS.md` is a generated index over the directory that is
not committed (Q55's argument, Q92's application of it). Raising a question used
to mean appending a section to a single file, so two branches raising two
questions conflicted by construction; a new file conflicts with nothing.

**Ask it in the conversation first, with a recommendation, and write the file
afterwards — never the other way round.** A question written straight to the
directory and mentioned in passing is a decision the owner has to go
looking for, and the file then records deliberation nobody took part in.
Asking first makes the file record an *answer*; asking after makes it record a
guess waiting to be corrected.

**Ask with the question prompt, not with prose.** This paragraph was here and
still failed, on 2026-08-07: four questions were put in the body of a long
message, went unanswered twice while the conversation moved on, and were written
to the file anyway. The owner's correction is the rule now — *"prompt me the
questions then record them to the file, instead of letting me navigate to the
questions file."* A paragraph inside a wall of findings is skippable and gets
skipped; a prompt is answered in one click. So:

- **Use `AskUserQuestion`.** Batch up to four, each with a recommendation marked
  and the cost of the alternatives stated. Prose alongside it is fine; prose
  *instead* of it is the failure above.
- **Write the file after the answer arrives**, recording the decision — and
  record it faithfully when it goes against the recommendation, with what that
  choice costs. Q32 is the worked example.
- **A question in the file that was never prompted is a defect**, the same way a
  bug with no evidence line is. It looks like deliberation and is a guess.

**A run that cannot reach the owner stops and asks in a pull request.** The rule
above says to use `AskUserQuestion`, and a scheduled or background run has no
interface to put it in — which is how questions ended up accumulating in a file
instead of being answered. So when such a run hits a decision it cannot make:

- **Stop.** Do not guess, and do not pick the reversible option and carry on.
- **Push what is finished** to its branch and open the pull request.
- **Put the question first in the PR body**, above the diagnosis — a short block
  that states the choice and what each option costs, so it can be answered in a
  sentence by somebody who has not read the rest.
- Title it so it cannot be mistaken for ready: `[needs a decision] …`.

The point is to move unanswered questions to where the owner already looks. An
open pull request with a question at the top is visible; a file under
`questions/` is only visible to whoever opens it, and the evidence is
that nobody did. The directory still records the *answer* once it arrives —
that has not changed, and it is what makes the decision survive the thread.

**The session-start hook prints every unanswered question**, because a rule that
depends on remembering is the rule that just failed. Its first run listed five
that had been sitting unasked for weeks. If that list is non-empty at the start
of a session, ask them before doing work they block.

Two things that make the asking worth the interruption:

- **Lead with a recommendation and the reason for it.** "Here are three
  options" hands the work back. "(b), because it grows into tagging rather than
  being replaced by it" is a position that can be agreed with in one word or
  argued down in two.
- **Separate what needs deciding from what does not.** Q28 had three live
  options and one part that was not a preference at all — whichever won,
  `Flatten` still has to inline resolved references or invariant 1 stops
  holding. Saying so keeps the question about the actual choice.

Batch them: several questions in one exchange costs one interruption, and the
answers are usually related enough that seeing them together improves all of
them.
