export const meta = {
  name: 'improve-loop',
  description: 'Audit → fix → adversarially verify, looping until the findings run dry',
  whenToUse:
    'A broad "harden/audit/improve this project" request where the work should be found rather than specified. Costs a dozen or more agents per round; use /improve for a single round on the main thread.',
  phases: [
    { title: 'Assess', detail: 'feature-guard, perf-warden, leak-hunter and hotspot scouts in parallel' },
    { title: 'Verify', detail: 'one adversary per finding, refuting by default' },
    { title: 'Fix', detail: 'confirmed findings become tests and fixes' },
    { title: 'Report', detail: 'synthesis, questions for the user, loop journal' },
  ],
}

// Each round: look with several independent lenses, refute what is found,
// fix only what survives. Loop until two rounds in a row find nothing new.
const MAX_ROUNDS = 3
const DRY_ROUNDS_TO_STOP = 2

const FINDINGS = {
  type: 'object',
  required: ['findings'],
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        required: ['id', 'severity', 'summary', 'where', 'evidence'],
        properties: {
          id: { type: 'string', description: 'stable slug, e.g. undo-publishes-stale-pixels' },
          severity: { type: 'string', enum: ['critical', 'major', 'minor'] },
          summary: { type: 'string' },
          where: { type: 'string', description: 'path:line' },
          evidence: { type: 'string', description: 'what was run or read that shows this' },
          fix: { type: 'string', description: 'the smallest change that would settle it' },
        },
      },
    },
  },
}

const VERDICT = {
  type: 'object',
  required: ['verdict', 'reasoning'],
  properties: {
    verdict: { type: 'string', enum: ['CONFIRMED', 'REFUTED', 'PARTIAL'] },
    reasoning: { type: 'string' },
    hole: { type: 'string', description: 'where the claim stops being true, if any' },
  },
}

const OUTCOME = {
  type: 'object',
  required: ['done', 'summary'],
  properties: {
    done: { type: 'boolean' },
    summary: { type: 'string' },
    test: { type: 'string', description: 'the test that now guards this' },
    blocked: { type: 'string', description: 'why it could not be finished' },
  },
}

const ORIENT = `Read .claude/quality/CHARTER.md, .claude/codemap/HOTSPOTS.md and
.claude/quality/LOOP.md first. Use \`python3 scripts/codemap.py find <term>\` and
\`python3 scripts/codemap.py file <path>\` instead of grepping the repository —
the index already holds every symbol, its line, its dependents and its tests.
\`python3 scripts/roadmap.py next\` says what is closest to done; its marks are
derived from the code, so it is a status report rather than a wish list.`

const LENSES = [
  {
    key: 'promises',
    agent: 'feature-guard',
    prompt: `${ORIENT}
Audit whether the behaviour inventory still holds. Report broken promises,
user-visible behaviour with no guarding test, and tests that cannot fail.
Only report what you verified by running or reading. Do not pad the list.`,
  },
  {
    key: 'performance',
    agent: 'perf-warden',
    prompt: `${ORIENT}
Run the performance budgets and compare against the charter's table. Report
regressions with the specific cost that caused them, and opportunities only
where the win is real. Do not raise a budget.`,
  },
  {
    key: 'leaks',
    agent: 'leak-hunter',
    prompt: `${ORIENT}
Review the working diff — \`git diff\`, or \`git show HEAD\` if the tree is
clean — for the performance leak shapes in your brief. Cheap and static: you
are the pass that runs every round, because the measured budgets only cover
paths that already have one, and every real stall here was in a path that did
not. Report CLEAR if the diff is clean; a false alarm every round is worse
than a miss.`,
  },
  {
    key: 'hotspots',
    agent: 'general-purpose',
    prompt: `${ORIENT}
Take the top five entries of HOTSPOTS.md that have no test reference. For each,
identify the single most valuable regression test that does not exist yet —
the one that would have caught the most likely defect in that file. Report the
gap, not a written test.`,
  },
  {
    key: 'invariants',
    agent: 'general-purpose',
    prompt: `${ORIENT}
Check the invariants in CLAUDE.md against the code: single pixel path through
BrushEngine.StampStroke, no randomness in rendering, fills and selections in
the record, pixel-affecting settings stored per stroke, view transform never
touching the document, painting bounded to the region a stroke reaches.
Report only actual violations, with file:line.`,
  },
]

const seen = new Set()
const fixed = []
const carried = []
let dry = 0

for (let round = 1; round <= MAX_ROUNDS && dry < DRY_ROUNDS_TO_STOP; round++) {
  log(`Round ${round}: assessing with ${LENSES.length} independent lenses`)
  phase('Assess')

  const reports = await parallel(
    LENSES.map((lens) => () =>
      agent(lens.prompt, {
        label: `assess:${lens.key}`,
        phase: 'Assess',
        agentType: lens.agent,
        schema: FINDINGS,
      }),
    ),
  )

  const fresh = reports
    .filter(Boolean)
    .flatMap((r) => r.findings ?? [])
    .filter((f) => f && !seen.has(f.id))

  if (fresh.length === 0) {
    dry++
    log(`Round ${round}: nothing new (${dry}/${DRY_ROUNDS_TO_STOP} dry rounds)`)
    continue
  }
  dry = 0
  fresh.forEach((f) => seen.add(f.id))
  log(`Round ${round}: ${fresh.length} candidate findings, verifying each`)

  // Refute first, fix second: a finding that cannot survive an adversary is
  // not worth an edit, and edits are the expensive part.
  const verified = await pipeline(
    fresh,
    (finding) =>
      agent(
        `Try to refute this claim about the Lightbox codebase. Default to REFUTED
if the evidence is thin.

CLAIM: ${finding.summary}
WHERE: ${finding.where}
EVIDENCE OFFERED: ${finding.evidence}

${ORIENT}`,
        {
          label: `refute:${finding.id}`,
          phase: 'Verify',
          agentType: 'adversary',
          schema: VERDICT,
        },
      ),
    (verdict, finding) => ({ finding, verdict }),
  )

  const survivors = verified
    .filter(Boolean)
    .filter((v) => v.verdict?.verdict !== 'REFUTED')
    .filter((v) => v.finding.severity !== 'minor')

  log(`Round ${round}: ${survivors.length} of ${fresh.length} findings survived verification`)
  if (survivors.length === 0) continue

  phase('Fix')
  const outcomes = await parallel(
    survivors.map((s) => () =>
      agent(
        `Fix this confirmed issue in the Lightbox codebase, then prove the fix.

ISSUE: ${s.finding.summary}
WHERE: ${s.finding.where}
SUGGESTED FIX: ${s.finding.fix ?? 'your judgement'}
ADVERSARY NOTES: ${s.verdict.reasoning}${s.verdict.hole ? ` | hole: ${s.verdict.hole}` : ''}

${ORIENT}

Requirements: write a test that fails without your fix and passes with it;
leave \`dotnet build Lightbox.sln\` and \`dotnet test\` green; change nothing
beyond what this issue needs. If the fix turns out to need a product decision,
stop and report it as blocked rather than guessing.`,
        {
          label: `fix:${s.finding.id}`,
          phase: 'Fix',
          agentType: 'general-purpose',
          schema: OUTCOME,
        },
      ),
    ),
  )

  outcomes.filter(Boolean).forEach((outcome, i) => {
    const entry = { finding: survivors[i].finding, outcome }
    if (outcome.done) fixed.push(entry)
    else carried.push(entry)
  })
}

phase('Report')
const report = await agent(
  `Write the round summary for the Lightbox improvement loop.

FIXED (${fixed.length}):
${fixed.map((f) => `- ${f.finding.summary} → ${f.outcome.summary} [${f.outcome.test ?? 'no test named'}]`).join('\n') || '- nothing'}

BLOCKED OR INCOMPLETE (${carried.length}):
${carried.map((c) => `- ${c.finding.summary} → ${c.outcome.blocked ?? c.outcome.summary}`).join('\n') || '- nothing'}

Do three things:
1. Run \`dotnet build Lightbox.sln\` and \`dotnet test\` and report the real result.
2. Append a round entry to .claude/quality/LOOP.md in the format that file uses,
   including a Rejected line for anything considered and dropped.
3. Append any product decisions the fixers were blocked on to
   .claude/quality/QUESTIONS.md, each with options and a recommendation.

Then return a short user-facing summary: what changed, what it means, and the
questions that need an answer. No preamble.`,
  { label: 'synthesis', phase: 'Report' },
)

return { fixed: fixed.length, blocked: carried.length, report }
