# Q138 · The character library's surface: entry points, roots, and what a re-import does — **answered 2026-08-20**

The owner chose the character library as the next Pillar 1 work. The engine
already exists and its decisions are recorded in `CharacterLibrary.cs` — a
library is an `AssetLibrary`-typed project rather than a package format,
import copies rather than links, every folder is offered (Q40), and swatch
ids are the one identity preserved. But nothing in the App calls `Scan` or
`Import`, and the two roadmap anchors that would prove the import promise
(`ImportingACharacterBringsItsAnimationsAndPalette`,
`AnImportedCharacterStillPaintsFromItsPalette`) do not exist. Three surface
decisions were prompted, plus one follow-up the third answer required.

**Where the artist meets it: both a picker and a dedicated window, now** —
against the recommendation of picker-first. The picker ("Import from
library…" on the project browser) is the fast path; the window is the
browsing home. The cost accepted: two surfaces to keep consistent from day
one, for a feature with zero current users. What keeps that cheap is one
view model feeding both — the window is a bigger view over the same scanned
entries, never a second scan or a second import path.

**Where libraries live: a roots list in the Configure window** (recommended,
accepted). App-level, because which disks hold libraries is a property of
the machine, not the artwork; empty by default, so the feature is absent
until asked for. `Scan` already accepts a root that is a library itself or a
folder of several. Scanning happens when a surface opens, never at startup.

**A name clash on import: merge into the existing folder**, and the
follow-up settled what merge means — **match by import provenance**
(recommended at that level):

- `Import` stamps each copied document and folder with the id of its library
  source — an optional manifest key, absent on anything never imported, so
  the "optional means absent" rule holds.
- Re-import replaces exactly the documents whose provenance matches the same
  source entry, adds documents the library gained, and never touches
  documents the artist created locally.
- Replacing a copy the artist has edited since import warns first,
  Q35-style, at the moment of the destructive act. Detecting "edited since
  import" wants a content hash stamped beside the provenance id; hash
  differs → warn.
- A target folder that merely shares a name but was never imported (no
  provenance anywhere in it) still merges by folder — incoming documents are
  added beside the existing ones, since nothing matches.

The costs stated and accepted: a provenance key and content hash join the
manifest format, and a warning dialog joins the import flow. What was
declined: numbered-beside import (nothing would ever update), name-matched
replacement (silent overwrite of a local animation that shares a name), and
add-only merge (re-import doubles the character every time).

## The slices this implies

1. **Prove the engine** — the two named roadmap tests, end to end, before
   any UI. An import path with one test file is where the bugs are.
2. **The way in** — roots preference, scan-on-open, the picker, the window
   over the same view model, provenance stamping and the merge rule.
3. **The registries** — the import command in `ShortcutMap`, the roots page
   in Configure, the manual's project-browser section, and the MCP surface
   so an agent can import a character.
