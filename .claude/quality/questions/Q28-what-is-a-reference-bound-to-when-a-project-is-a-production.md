# Q28 · What is a reference bound to, when a project is a production? — **answered (b)**

**Answered 2026-08-05: (b), a binding list.** A reference names any number of
targets — the project, one or more folders, specific documents — because
"multiple folder" was in the request and no single-scope field can express it
without duplicating the reference.

The reason it beat tags, which is where the wider vision points: a binding list
**grows into** tagging rather than being replaced by it. A tag becomes a fourth
kind of target, so `binds: tag/prop` arrives later without touching the model or
migrating a file. Choosing tags first would have made every reference's reach
depend on a tag somebody else edited — action at a distance, and the shape
invariant 4 is suspicious of.

What still needs deciding when it is built: what happens to a binding whose
folder is deleted while another binding survives. Not a blocker — a reference
with no remaining bindings is simply project-wide or orphaned, and either is a
one-line rule — but it should be chosen deliberately rather than fallen into.

Raised 2026-08-05, alongside Q29 and the scope note in the ledger's project
entries. **Not answerable from the code**, because the code has only ever had
one answer and it was chosen when a project meant one character.

A `ReferenceSheet` lives in `Doc.ReferenceSheets` — it belongs to **one
document**. That was coherent when Pillar 1 said a project *is* a character:
the turnaround belonged to the animation you were drawing.

It stops being coherent the moment a project is a production. The reporter's
own examples are the argument: a character sheet should reach **every animation
of that character**, a level design should reach **the environment it
describes**, and on a film an art-direction board is wanted **project-wide**.
The type is also wider than "character sheet" — level designs, world designs,
environmental sketches are the same kind of thing pointed at something else.

So the question is not *should a reference be shared* but **what does a
reference name as its scope**, and the options differ in what they cost:

**(a) A scope field on the reference: project, folder, or document.** One
nullable key, absent by default, and it reads like the camera does. Cheapest,
and it cannot express "these three folders" — which the reporter asked for by
name ("or multiple folder").

**(b) A list of bindings.** A reference names any number of folders, documents
or the project. Expresses everything asked for. The cost is that a reference
stops being ownable by anything, so deleting a folder has to answer what
happens to a reference that named it and one other.

**(c) Tags on both sides.** A reference carries tags; a folder or document
carries tags; a reference shows where the tags meet. This is where the owner's
"custom tags and be able to tag" points, and it is the only option where
"every character animation" is expressible without listing them. It is also the
one that can surprise: adding a tag to a folder silently changes what a
document sees, which is the *shape* invariant 4 is suspicious of.

**What makes this urgent rather than interesting:** invariant 1. A document
currently re-renders from its own record, and a reference resolved through a
project, a folder or a tag is a document that does not. The camera precedent
says how that is repaid — `ProjectIo.Flatten` inlines everything referenced when
a document leaves the app — so whichever option wins, **Flatten has to inline
resolved references and there must be a pixel-identity test for it**, or the
escape hatch rots silently. That part is not a preference and does not need a
decision.

---
