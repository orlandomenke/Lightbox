# Q230 · What happens to drawing data a newer build wrote — **answered: keep it and write it back**

**Answered 2026-10-09: keep it. Keys this build does not understand are carried
through untouched, so an older build can open, edit and save a newer file
without losing what it could not read.**

Raised 2026-10-09 by the sensitivity review of #654 (the frame read parsing
once). That change kept the existing behaviour: an unknown frame key is skipped
on open, so the next save drops it silently. SENSITIVITY.md's "work is never
lost" says a newer file must not be re-saved in a way that drops what this
build did not understand. The owner's deployed build lags `main` by hours, so a
file saved by a newer build and reopened in the deployed one is the realistic
path.

What it blocks: the fix in `FrameConverter` (and any other hand-rolled reader
that skips unknown keys).

**Recommendation was the chosen one.** The alternatives:
- open read-only with a warning — safe, but blocks working on the file;
- leave as is — simplest, and it loses data exactly when builds diverge.

The cost: the converter holds the raw JSON of each unknown key and writes it
back after the keys it owns. "Optional means absent" is unaffected, because the
keys were present in the file already.
