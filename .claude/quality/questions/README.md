# Open questions

Decisions the loop could not make for you. Each one blocks something specific;
each can be answered in a line.

**One question, one file** — `Q<id>-<slug>.md`, and the id comes from
`python3 scripts/bugs.py freeid question` rather than from reading the highest
number in sight. `.claude/quality/QUESTIONS.md` is a generated index over this
directory and is not committed, in the manner of `.claude/codemap/INDEX.md`
(Q55): a derived file that is stored is a file that can drift, and every branch
regenerates it, so storing it just moves the collision one artefact along.

That shape is the answer to a measured problem rather than tidiness. As one
file, every branch that raised a question appended a section at the same place,
so two branches raising two questions conflicted **by construction** — on top of
the id collision they had already had. A new file conflicts with nothing.

```bash
python3 scripts/questions.py new "Does the camera keyframe on 2s?"   # allocate and open
python3 scripts/questions.py find onion      # which question settled this
python3 scripts/questions.py open            # the unanswered ones
python3 scripts/questions.py build           # rewrite the index
```

## How a question gets answered

**Ask it in the conversation first, with a recommendation, and write the file
afterwards — never the other way round.** A question written straight to a file
and mentioned in passing is a decision the owner has to go looking for, and the
file then records deliberation nobody took part in. Ask with `AskUserQuestion`,
batched up to four, each with a recommendation marked and the cost of the
alternatives stated — a paragraph inside a wall of findings is skippable and
gets skipped.

Record the answer here once it arrives, faithfully when it goes against the
recommendation, **with what that choice costs**. `Q32` is the worked example;
`Q90` is one where the recommendation won and the cost of it is written down
anyway, which is the part that survives being read a year later.

A question in this directory that was never prompted is a defect, in the same
way a bug with no evidence line is: it looks like deliberation and is a guess.

## What an entry looks like

The first line is the heading and the ledger gate reads the id from the
filename, so the two have to agree — `questions.py check` refuses them
disagreeing. An answered question keeps its heading and gains the marker:

```markdown
# Q90 · Ledger ids collide on every parallel branch — **answered 2026-08-14: issue them**
```

Nothing is deleted. An answered question keeps its file, because the argument is
the point and a verdict on its own gets re-litigated the first time somebody
finds it inconvenient. `DECISIONS.md` carries the long-form reasoning for the
handful that earned one.
