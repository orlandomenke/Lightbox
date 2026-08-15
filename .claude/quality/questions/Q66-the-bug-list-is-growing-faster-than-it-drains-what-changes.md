# Q66 · The bug list is growing faster than it drains — what changes? — **answered 2026-08-12: fix rather than file, and a blocked run asks in a PR**

Raised by the owner: *"I notice a lot more bugs being reported by the agentic
system. Most seemingly not resolved only recorded. Which increases the bug list.
I want the agentic system to auto fix any bugs they encounter and prompt me
questions in this interface whenever a major decision had to be made."*

**The observation is right, and measuring it made it sharper than the
impression.** Share of each block of ids still open:

```
B1–B60      9%
B61–B120   14%
B121–B160  18%
B161–B179  79%
```

That is a regime change rather than a drift, and it is not explained by the
newest entries having had least time: four P1s were open at the time of asking
— B144, B168, B178, B179, all `canvas` — and B144 had been open for
thirty-one merges. The mechanism is visible in the log: recent work had settled
into diagnose-and-file (`B178: file the frame-wait fault the first GPU-on report
exposed`, `B179: report what memory is actually held`), which is exactly right
for a hard performance problem and became the default for everything.

**Two things needed deciding rather than assuming, because each collided with a
rule already in `CLAUDE.md`.** Both were put to the owner with a recommendation
and both recommendations were taken.

**(a) Auto-fixing collides with "a branch is one objective."** That rule is
emphatic — *if the sentence describing the branch needs an "and", it is two
branches* — and "fix everything you encounter" is an instruction to grow an
"and". **Answer: fix it, on its own branch, after finishing the one in hand.**
So the two rules now say the same thing from different directions: finding a
second defect produces another branch, in sequence, each doing one thing. It
costs more pull requests, and that is the price of not accumulating. Severity
sets the bar — P1 and P2 always, P3 when small — and filing instead is an
exception that must name its reason in the entry.

**Rejected: fixing it in the current branch.** Faster to green and it would have
required relaxing the branch rule in the same change, which trades a measurable
problem for the one the repo's own history says causes drift.

**(b) "Prompt me in this interface" is already the rule, and the runs doing the
filing cannot obey it.** `AskUserQuestion` needs an interface; a scheduled or
background run has none, which is precisely why its questions went to a file.
Restating the rule harder would not have changed that. **Answer: such a run
stops, pushes what is finished, and puts the question at the top of the pull
request, titled `[needs a decision]`.** The point is to move unanswered
questions to where the owner already looks — an open PR is visible, and the
evidence of this file is that a line in it is not.

`QUESTIONS.md` still records the answer once it arrives. What changed is where
the *question* waits.

**What would show this worked**, and is worth checking rather than assuming: the
share of open ids in the next block of twenty. If B180–B199 sits near the
historical 9–18% rather than near 79%, the rule took. If it does not, the
constraint is not the instruction and this entry should be reopened rather than
the instruction reworded — which is the failure mode the questions section
already has a paragraph about.

---
