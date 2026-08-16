# Q43 · How is "who is working on this" modelled? — **answered: a people list**

**Answered 2026-08-07: named people on the project, assigned by picking.**
Against the recommendation, which was a free-text name per document.

The case for free text was that a name is a label like the folder glyph, and a
registry is a table nobody maintains in a single-user alpha. The case that won
is the feature's own purpose: this is the surface that replaces a spreadsheet,
and **two spellings of one person is exactly the spreadsheet problem.** Grouping
and filtering by assignee have to be exact to be worth having, and a rename has
to fix every row rather than none.

**The costs, recorded because they are real:**

- It is a registry somebody maintains, and in a one-person project it is
  overhead with no payoff until the second person arrives.
- It is the first half of an accounts system with no second half — no auth, no
  sync, no identity. A `Person` here is a name and an id, and must not start
  looking like a login.
- A document can name a person who was deleted. The palette path already has
  this shape and wants the same answer rather than a bespoke one — and deleting
  a person says how many documents name them first, the way Q35's warning does.
