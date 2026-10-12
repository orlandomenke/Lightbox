# Q242 · Scanned papers: which sources may ship, and what the pencil becomes — **answered 2026-10-10**

**Answered: CC0 scans from ambientCG ship as built-in papers; the pencil becomes the softer graphite on the scanned Plain sheet at a finer grain.** Three prompts and two corrections, recorded in order because the first answer was not the one that stood.

Raised by the owner, 2026-10-10, while the paint path's phase 2 was in the perf lab: *"I am still not convinced of the medium effect. In part the way it interacts with the paper and the texture of it. Would it help to provide real paper texture scans. I have royalty free paper PBR textures."* And of the pencil: *"still does not look anything like graphite; too dark, too consistent."*

What it blocks: the paper under every textured brush and, in phase 3 of the paint path, under the media recipes — which are meant to read one height field for tooth, grain, wet edge and dry brush instead of three unrelated noises.

## What was asked, and what was answered

**1. Which pencil?** Seven rows on a sheet: today's pencil, the softer settings on the built-in paper, and the softer settings on three scans. **Answered: the softer graphite on the white paper scan** (recommended). Opacity 0.8, flow 0.5, pressure-to-flow gamma 1.2, depth 0.9, tooth 0.7 — graphite is never black, builds with passes, and leaves the sheet's deepest valleys even under a firm stroke.

**2. May the scans ship?** The owner's first answer was *CC0 or public domain*. The file names said FreePBR, the owner then said so, and its pages were read: the paid licence grants commercial **use** and asks that the file sets not be redistributed. The owner's position — *"I have a licence… it specifically mentions not to redistribute them on other sites. That is not what we are doing."* — is right about the application and not about this repository: a public repository is the files on another site, under a licence that tells every visitor they may pass them on, which is a right the owner cannot grant for the author. Three routes were offered (embed at release from outside git; ask the author; commit anyway on the owner's say). **The owner chose a fourth: look at ambientCG first**, and picked Paper 001, 002 and 006 there.

**3. The canvas and the stucco.** The owner found no canvas on ambientCG. Fabric 061 (a tight plain weave) and Plaster 001 were proposed as stand-ins and **approved with the three papers**: *"yes download all five."*

**4. Which grain for the pencil, on the CC0 sheet?** A second sheet, the chosen row beside the same settings on the ambientCG papers. **Answered: row 4 — Plain (Paper 001) at a finer grain**, scale 5.

## What it costs

- **Nothing from FreePBR is in the repository**, including the height maps derived from its normal maps to make the first sheet. They are on the owner's machine and usable through *Paper image…*, which stores a paper in the document that uses it.
- **Five PNGs, 3.6 MB, in a public repository for good.** A scan is pinned by fingerprint (`ScannedPaperTests.TheSurfaceIsTheOneThatShipped`): replacing one changes existing art, so a better scan is a new paper kind and not a new file under an old name.
- **The pencil changes for everyone**, and strokes already drawn with it do not (invariant 4: its settings are in each stroke).
- **The simulated media do not use these yet.** They sample their own paper on the lattice's grid. That is phase 3 of `docs/DESIGN-paint-path.md`, and is the half of the owner's complaint this does not answer.

`docs/CREDITS-papers.md` names each source and what was done to it.
