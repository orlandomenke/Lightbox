# Q201 · Should a local test run include the timed legs? — **answered 2026-10-07: no, opt-in with `--perf`; CI keeps running them**

Raised by the owner: *"so this application tests while building eats most if not
all available RAM. Effectively blocking other work and even outputting beeps
from my laptop when the load gets really heavy. At the same time, tests take
quite a long time, somewhere between 20 - 30 minutes I believe."*

What it blocks: how long `python3 scripts/testplan.py run` takes, and what a
local green means.

**Measured from the TRX of the owner's last full local run (2026-10-06, 16
threads, 31 GB):**

| phase | wall clock |
| --- | --- |
| eight ordinary legs at once | 6.5 min |
| App `[timed]` leg, alone | 3.8 min |
| Raster `[timed]` leg, alone | 3.6 min |

So the two legs that must run alone were more than half the test time — and in
that run three of App's budgets failed with nothing wrong, which is the pattern
the project's notes already record: on this laptop the budgets are red often
enough that a local red from one is not evidence of a regression.

**Not a preference, and settled first:** the timed legs were cut by *class*, so
App's held 120 tests of which a handful measured anything (`PigmentModelTests`
is one budget among 29 tests). Skipping that leg would have skipped every
ordinary test sitting beside a budget — a skipped test reading as a passing one,
which is B269 and B281's failure. `testplan.py` therefore now cuts the timed leg
by **method** (`performance_tests`): 34 test methods in App (37 tests counting theory rows), 41 in Raster (45), and the
ordinary tests in those classes run in the parallel pool. That change is right
whatever the answer here, because it also shortens the leg when it does run.

**Recommendation:** opt-in locally. `testplan.py run` skips the timed legs and
says so; `--perf` runs them, alone, at normal priority, after the rest. CI runs
every one on every pull request, so no budget goes unchecked before merge.
The cost: a local green no longer covers the budgets, so a performance
regression surfaces on the PR instead of on the laptop.

Alternatives and what they cost:

- *Keep running them locally, alone* — no loss of local coverage, and ~7.5 min on
  every run for a result that is often noise here.
- *Run them only when the diff touches a hot path* — a second selector, and a
  selector that skips things is the hazard the plan's whole design is against.

**Answered: opt-in locally, as recommended.**
