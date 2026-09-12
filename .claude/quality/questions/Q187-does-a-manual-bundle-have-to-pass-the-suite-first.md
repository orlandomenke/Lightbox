# Q187 · Does a manual bundle have to pass the suite first? — **answered 2026-09-12: no, on request — against the recommendation**

`release.yml` runs `dotnet test Lightbox.sln` before it will publish anything,
on both ways in: a `v*` tag and a manual **Run workflow**. That is the whole of
the "I just want a test build" complaint that raised Q186 — a bundle to try
something on Windows costs the 742 s suite before the 2 min publish.

Asked with three options and a recommendation that the manual path pay the same
reduced, parallel suite a pull request pays (≈190 s after Q186), keeping the
property that nothing ships untested.

**Answered against that recommendation: add an opt-in `skip_tests` input**, on
`workflow_dispatch` only. A tag still runs the suite unconditionally and cannot
be made to skip it.

**What it costs, which is the reason the recommendation went the other way.** A
bundle built with `skip_tests` has proven nothing, and a zip is not
self-describing — handed to somebody a week later it is indistinguishable from
one that passed, and the first thing a crash from it will cost is the hour spent
looking for the bug in the code rather than in the provenance. The mitigation is
to make the artefact say so itself rather than to rely on whoever pressed the
button remembering:

- the bundle's **label carries `-untested`**, so the downloaded file's own name
  says it;
- the run summary says it in as many words;
- and the input defaults to **off**, so the fast path is the one you ask for
  rather than the one you get.

That is not the same guarantee and is not claimed to be. It makes the untested
bundle identifiable after the fact, which is the part that was actually going to
cost something.

**Why the answer is nevertheless right for this repository.** The manual bundle
exists for exactly one purpose — the comment at the top of `release.yml` says it:
"build me a bundle off this branch so I can try it". Its audience is the owner,
on a branch, minutes after a push whose pull request already ran the suite. The
test run in that path is usually re-proving something proved ten minutes ago,
and a gate that is nearly always redundant is a gate people route around by
hand. A `v*` tag is where the guarantee belongs, and there it is untouched.
