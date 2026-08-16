# Q54 · Does Lightbox go public, and under what licence? — **answered 2026-08-08: yes, GPL-3.0, history and all**

**What forced the question.** CI stopped allocating runners on 2026-08-08 — run
#488 on `main` passed `docs`, `changes` and `test`, then `publish-win-x64` failed
in two seconds with `runner_id: 0`, no steps and a 404 on its logs, and every run
after it failed the same way on whichever job came first. Not a code fault: the
account had run out of Actions minutes. Measured burn was **9 billed minutes per
run** (GitHub rounds each *job* up to the minute, so `changes` at 8 s and `docs`
at 19 s cost a minute each), and **18 per merged change** — once for the pull
request, again for the push to `main`.

**Public repositories get unlimited free Actions minutes**, so the answer removed
the constraint rather than managing it. The owner intended to open-source
Lightbox anyway; the bill only set the date.

**Three decisions, and what each cost.**

- **Everything is published, history included.** Splitting the planning docs out
  was considered and rejected twice over. Retroactively: `BUGS.md` has 178
  commits, `ROADMAP.md` 105 and `QUESTIONS.md` 47, so purging them would rewrite
  nearly every SHA — including the commit references the ledgers themselves cite.
  Going forward: `bugs.py` and `roadmap.py` derive their checkboxes by resolving
  evidence anchors against the code index **in the same tree**, so a separate
  private repo would turn every derived checkbox back into an assertion. That is
  the precise failure B81 exists to prevent.
- **GPL-3.0.** Checked rather than assumed: every dependency is permissive
  (Avalonia, SkiaSharp, CommunityToolkit, the Anthropic and MCP SDKs all MIT;
  xunit Apache-2.0, which is GPLv3-compatible but *not* GPLv2-compatible — which
  is why the v3 family and not v2), and a scan for copied third-party source
  found only ordinary prose. AGPL was considered for the MCP and IPC surfaces,
  where "someone hosts Lightbox as a service" is not far-fetched, and declined:
  the network clause is a no-op for a desktop application and AGPL is on enough
  corporate blocklists to cost more than it buys.
- **`main` is protected, with admin bypass kept.** A pull request and passing
  checks are required, and `LIGHTBOX_PUSH_TO_MAIN=1` still works when a merge is
  genuinely intended. `.githooks/pre-push` stays the first line; protection is
  the second.

**The cost that is worth naming, because it is permanent.** Publishing is prior
art. It forecloses patenting anything in this tree — immediate in most of Europe
under absolute novelty, with a twelve-month grace period in the US. Nothing here
looks patentable (brush stamping, flood fill, Bézier geometry and layer blending
are decades of prior art), but the deterministic `Hash01` dab seeding and the
inbetweening approach are the two places anyone would look, and that door is now
shut. Accepted knowingly.

**What keeps the commercial option open**, and it is one thing: **sole
copyright**. GPL binds recipients, not the owner, so the same code can be
relicensed later — but only while one person holds all of it. `CONTRIBUTING.md`
therefore declines pull requests during alpha rather than leaving it to silence.
The day that changes, it needs a CLA first.

**Not legal advice, and the owner was told so.** An hour with an IP solicitor
before the switch is cheap if the commercial stakes are real.

---
