# Q147 · What does "Delete permanently…" do with a resource's kept versions? — **answered 2026-08-22**

Found by the adversarial pass on the branch that gave the project window its
version surfaces: `ProjectVersions.ClearHistory` was built (Q75) as the
deliberate verb for "the resource itself is deleted", and nothing calls it.
So **Delete permanently…** removes a document's file and leaves its kept
versions orphaned under `versions/<id>/` — disk no surface accounts for, per
deleted document, forever — while the confirmation says "from the project and
from disk" about a thing it only half does.

**Answer: ask per delete.** The confirmation dialog gains a checkbox — *also
delete its N kept versions* — shown only when the resource has any. Chosen
over the recommendation (clear in the same gesture, on the grounds that the
confirmation sentence already claims it), and over keeping the history
silently. What the choice costs, recorded as the rule requires:

- A decision added to every permanent delete of a versioned document, for a
  case most artists will not have thought about until the dialog raises it.
- The checkbox's default is a second question the implementation must answer:
  unticked keeps the current keep-everything behaviour and makes the dialog's
  headline sentence conditional; ticked makes the safety copy opt-out. That
  default was not decided here and should be settled on the implementing
  branch — with the dialog's wording made true either way.

What it buys: the artist decides with the number in front of them ("and its 4
kept versions"), and neither silent data loss nor silent disk growth happens
on anyone's behalf.

Not built on the branch that asked (one objective per branch). The pieces the
implementing branch leans on: `ProjectVersions.ClearHistory`,
`VersionFacts.Count` for the dialog's number, and `DeleteWarning` /
`DeleteNeedsConfirmation` in `ProjectWindowViewModel`, which already own the
dialog's sentence. A versioned document should make `DeleteNeedsConfirmation`
answer true even for a lone document — today only folders with contents ask.
