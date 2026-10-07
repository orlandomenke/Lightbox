# Q202 · How many legs should a local test run start at once? — **answered 2026-10-07: memory-aware — a quarter of the cores, capped by free memory**

Raised by the owner, in the same request as Q201: the local run "eats most if not
all available RAM", to the point of audible distress from the laptop.

What it blocks: the default of `testplan.py run --jobs`.

**Measured 2026-10-07 on the owner's laptop (16 threads, 31 GB):**

- The old default, `cpu_count // 2`, is **8** here — exactly the number of
  ordinary legs, so every one started within 46 s of the first. Inside each,
  xUnit ran its own pool of 16 threads.
- One testhost, run alone, peaked at **1.3 GB**. About **3.2 GB** more was held
  by ten idle MSBuild worker nodes and the compiler server the build had left
  resident (`nodeReuse:true` keeps a node for fifteen minutes — the whole run).
  ~9 GB was free with nothing running.
- The parallelism was not buying time. An App shard that took **304 s** in the
  eight-leg run took **158 s** alone; `testplan.py filter` went from 2 s to 24 s
  and `bugs.py ids` from 33 s to 188 s, which is why two Core gate tests cost
  ten minutes of CPU between them.

**Not preferences, and done regardless:** the build runs with
`-nodeReuse:false`; ordinary legs start below normal priority (the timed legs
do not — a budget measured at low priority measures the scheduler); and a
finished leg's slot is refilled at once rather than after the oldest leg ends.

Tried and dropped: capping each leg's xUnit pool with
`-- xUnit.MaxParallelThreads=N`. xunit v3 (App) honours it, but App runs with
parallelism off anyway; on the v2 adapter (Core, Raster, Ai) a Raster shard
took 15 s at the default, at 4 and at 1 thread alike. A setting with no
measured effect was not kept.

**Recommendation:** memory-aware — `max(2, cores // 4)`, capped at one leg per
2 GB of memory free *after* the build. Four here on a quiet machine, fewer when
the owner has other work open. `--jobs` still overrides it, and the run prints
which rule chose the number.

Alternatives and what they cost:

- *Fixed at 4* — predictable, and blind to how much memory is actually free,
  which is the variable that produced the complaint.
- *Keep `cores // 2`* — the behaviour that produced the complaint.

**Answered: memory-aware, as recommended.**
