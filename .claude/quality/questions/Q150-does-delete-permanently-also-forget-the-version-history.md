# Q150 · Does "Delete permanently…" also forget the version history? — **answered 2026-08-23: ask per delete, with a checkbox**

Found while the adversarial pass on the project-window versions branch (PR
#391) was probing the facts cache: `ProjectVersions.ClearHistory` existed with
a doc comment naming its purpose — "for when the resource itself is deleted" —
and nothing called it. So **Delete permanently…** removed a resource from the
project and from disk while its kept version bytes stayed under
`versions/<resourceId>/` forever: unreachable (the id is gone from the
manifest, so no surface can list or revert them) and unaccounted (no UI, no
footer, no cost anywhere).

**The standing answer: ask per delete.** The confirmation dialog gains a
checkbox — *also delete its N kept versions* — shown only when the resource
has any. The artist decides with the number in front of them, and neither
silent data loss nor silent disk growth happens on anyone's behalf. Chosen
**over** the recommendation (clear in the same gesture, on the grounds that
the confirmation sentence already claims "from the project and from disk"),
and the price of the choice recorded as the rule requires:

- A decision added to every permanent delete of a versioned document, for a
  case most artists will not have thought about until the dialog raises it.
- The checkbox's default is a second question the implementing branch must
  settle — unticked keeps history and makes the dialog's headline sentence
  conditional; ticked makes the safety copy opt-out — with the wording made
  true either way.

**This question was asked twice and the record must say so.** It was first
prompted and answered *checkbox* during PR #391 (filed then as Q147, an id a
parallel branch also allocated — the collision repair moved this record
here). The asking session's context was then summarised, the first answer was
lost with it, and the question was prompted again — recommendation first this
time — and answered *clear it*, which PR #394 implemented as an unconditional
clear. On 2026-08-23 the owner confirmed the **first** answer stands.
So: **#394's unconditional clearing is interim behaviour**, honest about disk
but stronger than decided, and the checkbox supersedes it when its branch
lands (roadmapped, with `DeletePermanentlyAsksAboutKeptVersions` among the
anchors). The lesson for the asking machinery: a re-asked question is a
red flag — check the questions directory for the same subject before
prompting, because the second answer arrives with less deliberation than the
first.

Implementation notes for the checkbox branch, carried over from the first
record: `VersionFacts.Count` supplies the dialog's number;
`DeleteWarning`/`DeleteNeedsConfirmation` in `ProjectWindowViewModel` own the
sentence, and a lone versioned document must make `DeleteNeedsConfirmation`
answer true — today only folders with contents ask. **Remove from project
keeps the history** regardless, and that line is not in question: the file
survives, re-adding is cheap, and a re-added document keeps its id.

Sheets filed in a deleted folder are a separate pre-existing defect (B283 —
nothing re-homes or detaches them) and their history follows whatever that
fix does with the refs.
