# Q22 · Is a "Document" called a Workfile, and what else is in that menu? — **answered (a)**

**Answered 2026-08-04: (a), *Document* stays.** Fix the grouping and the dead
entries; do not rename. The report says the *menu* is undecipherable rather than
the *word*, and folders being visually indistinguishable from files is the
complaint the fix should answer first. (b) stays available if the confusion
survives that — but two names for one thing is usually the cause of the next
confusion, and `Document` is load-bearing in the manual, the roadmap,
`DocumentRef` and the MCP surface.

**B63 is unblocked entirely**: both halves are now ordinary work.

**Blocks:** nothing.

Raised in a report: the create-in-project menu is "undecipherable — what is a
folder and what is a workfile", with a suggestion to rename *Document* to
*Workfile*.

The defect underneath is filed (**B63**: entries that produce nothing, and no
visual split between folders and files). What cannot be decided from the code is
the vocabulary. *Document* is used throughout the manual, the roadmap and the
serialization (`DocumentRef`, `NewDocumentSettings`), so a rename is not a label
change — it is a rename across the UI, the docs and the artist's mental model.

**(a) Keep *Document*.** Fix only the grouping and the dead entries. Cheapest,
and the word is already established everywhere else in the product.
**(b) Rename to *Workfile* in the UI only.** The record keeps `Document`. Solves
the reported confusion at the cost of two names for one thing, which is the
thing that usually causes the next confusion.
**(c) Rename everywhere.** Consistent, and it touches the manual, the roadmap,
the MCP surface and every serialized name — an expensive change to make for a
menu.

**Recommend (a) plus B63's grouping fix**, on the grounds that the report says
the *menu* is undecipherable rather than the *word* — folders and files being
visually indistinguishable is the complaint the fix should answer first. If the
confusion survives that, (b) is still available.
