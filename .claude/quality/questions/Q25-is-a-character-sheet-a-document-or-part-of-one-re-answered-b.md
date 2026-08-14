# Q25 · Is a character sheet a document, or part of one? — **re-answered (b), inside a project**

**Re-answered 2026-08-12, by the owner, overriding (a):** inside a project a
sheet is **its own file**, filed on a folder the way a document is. The owner's
words: *"every document can create a reference sheet but it's always assigned
to the first top folder by default. So it becomes a file. All documents within
the folder can access the character sheet. We see it in the project docker and
through the project manager we can re-assign if need be."*

What that cost, and where the line was drawn — the four sub-decisions were
prompted and answered the same day:

- **Filed like documents** (`SheetRef` with `FolderId` in the manifest), not a
  scoped-resource declaration. One concept — *where is it filed* — and
  visibility is the folder's subtree. The reference declarations stay for what
  filing cannot say (B133 still owns their unread half).
- **On disk inside the assigned folder's directory** (`<folder>/<slug>.sheet.json`),
  so the tree in a file manager matches the panel; re-assigning therefore
  **moves a file**, disk-first like a document move (B106's order).
- **Standalone documents keep (a)** — sheets stay in `Doc.ReferenceSheets`,
  B66's prompt-to-save unchanged. Two storage shapes exist, switched by
  context; that is the accepted cost of not making loose files travel in pairs.
- **Migration is promote-on-open**: a project document carrying old in-document
  sheets lifts them into the registry (filed on its top folder) the first time
  it is read, and its next save writes both halves. Idempotent because sheets
  keep their ids.

(a)'s reasoning below is kept because most of it still holds — the format-change
cost it predicted is exactly what was paid, and what finally justified paying it
was the sharing argument the last line of (a) anticipated: *"if sheets later
need to be shared between documents, that is the argument for (b)."* B133's
measurement showed sheets could not reach sibling documents at all, and the
docker/window visibility the owner asked for needs a real slot in the manifest.

**The first answer, 2026-08-04: (a), it stays part of a document.** No format
change, no new project-manifest slot, and no new docker row type that is not a
file. The reported pain is losing work — *"character sheets are not saved to
disk"* — and that is fixed by making sure there is a file behind the document
the sheet lives in, which costs one prompt.

The docker-visibility half of the report is answered rather than implemented: a
character sheet **is** visible in the project docker, as the document that
contains it. If sheets later need to be shared between documents, that is the
argument for (b) and it is a better one than this.

**B66 is unblocked** and is now two ordinary pieces: ask for the name before
writing anything (B65's rule on another surface), and prompt to save a document
that has never been saved so the sheet has somewhere to live.

**Blocks:** nothing.

The report says: *"Outside of a project (single file) a character sheet is a
manually saved document. Creating a character sheet should directly prompt
saving. In a project, a character sheet is directly added, similar to how the
project dockers add them directly."*

That describes a character sheet as **a document with its own file**. The code
has it as **part of a document**: a `ReferenceSheet` lives in
`Doc.ReferenceSheets`, so it is saved when its document is saved and has no file
of its own. The project manifest holds `DocumentRef` (animations, shots,
project documents) and `Character` — there is no slot a reference sheet could
occupy, which is why it cannot appear in the project docker today.

So the two halves of the report need different things, and only one is a defect:

**(a) It stays part of a document, and the bug is that an unsaved document loses
it.** Then the fix is the prompt: creating a sheet on a never-saved document
prompts to save, so there is a file behind the work. Nothing in the format
changes, nothing new appears in the project docker, and "not visible in the
project docker" is answered with *it is inside a document, and the document is
listed*. Cheapest by a wide margin.

**(b) It becomes a document in its own right** — its own file, its own
`DocumentRef`, listed in the docker beside animations. Matches the report's
wording most literally and makes "add it directly in a project" fall out for
free. It is a **format change**: sheets move out of `Doc`, existing documents
need migrating, and `CLAUDE.md`'s rule that a proposal requiring a format change
has "drifted into redefining what a document is" applies squarely.

**(c) Both — it stays in the document and the docker learns to show it.** No
format change, and the docker gains a row type that is not a file, which every
path that maps a row to a path (`PathOf`, reveal, copy path, rename in **B64**)
then has to have an answer for.

**Recommend (a)**, because the reported pain is losing work — "character sheets
are not saved to disk" — and (a) fixes exactly that at the cost of one prompt.
The docker visibility that (b) and (c) buy is a smaller complaint, and (b) spends
a format migration on it. If sheets later need to be shared between documents,
that is the argument for (b) and it is a better one than this.
