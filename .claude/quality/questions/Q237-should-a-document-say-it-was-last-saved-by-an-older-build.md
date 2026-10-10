# Q237 · Should a document say it was last saved by an older build — **answered: yes, add the marker now**

**Answered 2026-10-09: add it now.** A document an older build saves over a
newer one's carries `savedByFormat` — the older format — so the newer build,
opening it again, can say some of what it wrote may be missing.

Raised 2026-10-09 by the sensitivity review of Q234 (#671). An older build
writes the newer version number back on save — the safer choice, because
writing its own number would have the newer build re-run a migration over
content that still carries the newer keys — so the newer build could not tell
that an older one had rewritten the file and perhaps dropped per-point data
(Q234's gap).

**Recommendation was the chosen one.** The alternative was to rely on the
older build's warning alone. Like that warning, the marker only works if the
older build already writes it, which is why it goes in now, before any format
change that needs it.

Shape, so it costs ordinary files nothing: the key is written only by a build
saving a document newer than it writes, and a build that writes the
document's own format clears it on its next save, so the warning is said once.
