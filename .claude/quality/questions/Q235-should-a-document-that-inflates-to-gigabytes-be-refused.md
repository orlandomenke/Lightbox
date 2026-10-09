# Q235 · Should a document that inflates to gigabytes be refused — **answered: cap and refuse**

**Answered 2026-10-09: cap and refuse.** Opening a document that decompresses
past a generous limit is refused with a plain message, rather than exhausting
memory.

Raised 2026-10-09 by the sensitivity reviews of #654 and B430. Every saved
document is gzip, and nothing limits how far one may expand on load. A small
file that inflates to gigabytes would exhaust memory before deserialization
ends, and `OutOfMemoryException` is not caught on any open path. Native
documents are not in SENSITIVITY.md's imported-file row; this settles that they
are guarded anyway.

**Recommendation was the chosen one.** The alternative was to treat documents
as trusted because they come from the artist's own machine. The cost of the
cap is a few lines in `DocJson.Load` and a hostile-input test; the limit has to
be generous enough that no real document comes near it.
