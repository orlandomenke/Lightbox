# Q238 · Fix the crash on a corrupt project document next — **answered: yes, next**

**Answered 2026-10-09: fix it next,** ahead of more responsiveness work.

Raised 2026-10-09 by the sensitivity review of Q235: a corrupt document inside
a project crashed every route that read it (about a dozen call sites; it
predates the day's work). The alternative was to file it with its cost and
carry on. The fix is B436.
