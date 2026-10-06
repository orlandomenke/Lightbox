# Q193 · Does the document format get a version field, so an older build does not silently drop newer keys? — **answered 2026-10-06: yes, as a roadmap item**

**Answered:** yes. A `version` key the current build writes, and a reader that
sees a higher one than it knows **warns before saving and keeps the keys it does
not understand** rather than dropping them. Filed as a roadmap item under
*Project plumbing*; it gets its own branch and nothing in the symmetry surface
(PR #551) waits on it. The alternative of landing it *before* tile wrap was
offered and not taken — wrap adds one more per-stroke key, but the gap is older
than either and is not made worse by sequencing.

Raised by the sensitivity-guardian reviewing `Scene.Symmetry` (2026-10-06):
`DocJson` writes only the keys this build knows, and `System.Text.Json` drops
unknown members on read, so a file saved by a newer build and re-saved by an
older one loses every key the older build has never heard of. For `symmetry`
on a stroke that is a **pixel change** — the reflected half of every mirrored
mark disappears on the next render — and nothing warns, because to the older
build the file simply never had it.

## Why a version field rather than tolerating unknown keys

Both were considered:

- **Keep unknown keys** (a `[JsonExtensionData]` bag on every record): the data
  survives a round trip, but the build that kept it cannot *render* it, so the
  artist sees a drawing missing its reflections, saves, and has a file that is
  right again only on a newer build. Silent still, just recoverable.
- **A version field**: one integer, written once per document, read once. A
  build that sees a higher number than its own knows it is looking at a file it
  cannot fully represent, and can say so before overwriting it. The warning is
  the point; keeping the keys is the courtesy that makes the warning survivable.

The recommendation is both together, with the warning as the non-negotiable
half. Cost: S for the field and the guard; the extension-data bag touches every
record and wants its own absence test per record (`optional-settings`).

## What it is not

Not a schema migration system. A reader that sees a *lower* version than its
own reads as it does today — every key added so far has been nullable and
absent, which is what *Optional means absent* buys, and that stays the rule.

**Blocks:** nothing. B379 and seamless tile wrap (Q192) proceed independently.
