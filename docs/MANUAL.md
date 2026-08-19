# Lightbox — user manual

Lightbox is for **frame-by-frame animation** and **digital painting**, with AI
assistance throughout — most visibly filling in the inbetweens.

This manual describes what the application does **today**. Anything not yet
built is marked *Planned*, with no promise of when. Nothing here is aspirational
prose about a feature that does not exist: if a section describes a button, that
button is in the build.

> **Keeping it true.** This manual is part of the definition of done. A change
> that alters what an artist sees or does updates the relevant section — the
> file under `docs/manual/` — in the same commit. A feature moving from
> *Planned* to real means deleting the *Planned* marker and writing how it
> actually works — not how it was going to.

The sections live in [`docs/manual/`](manual/), one file each, so a page is
about one thing and nobody scrolls past twenty thousand words of brush
documentation to read about layers.

**Contents**

<!-- contents:start -->

1. [Getting started](manual/01-first-run.md)
2. [Documents and projects](manual/02-documents-and-projects.md)
3. [Tools and strokes](manual/03-tools-and-strokes.md)
4. [Brushes](manual/04-brushes.md)
5. [Colour](manual/05-colour.md)
6. [Layers, selections and guides](manual/06-layers-selections-and-guides.md)
7. [The timeline](manual/07-the-timeline.md)
8. [Onion skin, references and the camera](manual/08-onion-skin-and-references.md)
9. [Symbols](manual/09-symbols.md)
10. [Saving and recovery](manual/10-saving-and-recovery.md)
11. [Exporting to a game engine](manual/11-exporting-to-a-game-engine.md)
12. [AI assistance](manual/12-ai-assistance.md)
13. [Keyboard, performance and what is planned](manual/13-keyboard-and-troubleshooting.md)
14. [Bones and rigging](manual/14-bones-and-rigging.md)
15. [Effects — fire, smoke and water](manual/15-effects.md)

<!-- contents:end -->

The list above is generated from the files in `docs/manual/` by
`python3 scripts/manual.py sync`, and CI fails if it drifts — so adding a
section means adding a file rather than remembering to edit two places.
