# Q36 · When does an existing project get migrated? — **answered: it does not**

**Answered 2026-08-07: no migration.** *"The application is in alpha, only used
by me, a single user. So no migration is needed. I am currently only testing and
no production whatsoever has been run."*

Writing a migration for zero real projects is cost with no beneficiary, and it
would be the second code path that `DESIGN-project-scoping.md` exists to remove.

**The consequence, recorded so it cannot be a surprise: project files written
before the change will not open.** Acceptable now, not acceptable in a month, so
the change carries its own tombstone — `ProjectManifest.Version` goes to **2**,
and a version-1 manifest is **refused with a sentence** rather than crashed on,
saying that the drawings are intact because documents are their own files in
their own format. Only the index is lost.

**Write the migration the day a second person has a project.** This entry is the
record that the decision was deliberate rather than overlooked.

**Blocks:** nothing.
