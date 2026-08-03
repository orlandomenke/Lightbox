# What a template is

Status: **designed, nothing built.** Q12 answered (a) — *a document in the
project marked as a template* — with a follow-up worth answering properly:
*what is your definition of a template, and how do you use it?*

## The definition

**A template is an ordinary animation document with a flag set.**

Not a new file type. Not a new format. Not a list built into the app. One
boolean on a document that already exists, and everything else falls out of
that.

The flag is on the document rather than a `templates/` folder, and that is the
load-bearing choice: **a folder makes you decide at creation time, a flag lets
you decide afterwards.** Real templates are not designed in advance; they are
the third walk cycle you set up, at which point you notice you have set up the
same thing three times. A flag means that realisation costs one menu item
instead of a re-organisation.

## The one rule everything else depends on

**A template is copied, never referenced.**

That is the whole difference between a template and a symbol, and the two are
easy to conflate because both are "reuse":

| | Symbol | Template |
| --- | --- | --- |
| What it is | one drawing used in many places | a starting point |
| The link | **live** — edit it and every placement changes | **none** — the copy is yours from the first stroke |
| Editing it | rewrites finished work, on purpose | affects only *future* copies |
| What it carries | drawings | a document's shape |

This is what makes **changeable on the fly** safe, and it is the reason you can
have it. Because a copy has no link back, editing a template can never reach
into an animation somebody already started — so there is no need to lock
templates, no need to version them, no dialog asking whether to propagate. You
open it and draw. If templates were live references, "changeable on the fly"
would mean "any edit silently rewrites every animation ever started from this",
which is the opposite of what a starting point means.

## What it carries

Everything a document carries, which is the point — a template that only
remembered the canvas size would be `NewDocumentSettings`, which already
exists:

- **Scene** — size and fps.
- **The layer stack**, with names, kinds, blend modes, opacities and locks.
  This is the biggest single win: *Rough / Clean / Colour / BG* set up right,
  in order, with the background locked.
- **The exposure sheet** — frame count, and which cels are keys and which are
  holds. A twelve-frame walk already timed on 2s, with the contact and passing
  positions marked.
- **Guides and grids** — a horizon, a character-height line, a ground plane.
- **The camera**, if it has one. Absent if not, per the camera's own rule.
- **Any drawing you want in it** — a pivot cross, a ground line, construction
  guides on their own locked layer. A template with drawings in it is still
  just a document.

Nothing new to serialize, load, render, export or migrate. That is the test of
whether this definition is the right one.

## How you use it

**Make one.** Any open document → **File ▸ Save as template**, or right-click a
document in the Project panel → **Use as template**. It does not move and does
not change; it gains a flag and starts appearing in one more list.

**Start from one.** **File ▸ New from template…** lists the project's
templates. Pick one and you get a **copy** — untitled, unsaved, yours. The
template is untouched, and the copy has no idea where it came from.

**Edit one.** Open it and draw. It is a document; every tool works on it.
Changes apply to copies made *after* the edit and to nothing made before.

**Stop being one.** Clear the flag. The document stays exactly where it is.

Without a project, none of this is needed: a standalone template is a file you
Open and then Save as, which works today. The feature exists because a project
can *list* its templates, and a loose folder cannot.

## Why it is not the other two options

**(b) a built-in list** — walk 8 on 2s, run 6, blink 4, take 12 — is better on
day one and worthless on day two. Every studio times its own walk differently,
and a list nobody can add to becomes a list nobody opens.

**(c) both** stays available and costs nothing to defer: built-ins seeded as
project documents on first use are (a) plus content. Adding a starter pack
later changes no mechanism, which is the property that makes deferring it safe
rather than merely cheaper.

## Where it sits next to timing presets

Q11 landed on **timing presets** — save an exposure pattern, apply it to a
range of cels — and the two look adjacent enough to be worth separating on
purpose:

- A **template** gives you a document's *shape*, at creation, once.
- A **timing preset** re-times drawings *you already made*, at any point, as
  often as you like.

They both concern spacing, and that is an argument for keeping them apart
rather than merging them: one is "start here", the other is "re-time this", and
a mechanism that tried to be both would be worse at each. A template can
certainly *contain* a timing that came from a preset — it is a document, and
that is what documents do.

## What to build, in order

1. `IsTemplate` on the document, defaulting false, absent from the JSON when
   false — the camera's rule, so no existing file changes by one byte.
2. **New from template…**, which is `ProjectIo`'s existing copy path plus a
   rename. The test that matters: a document made from a template has no
   reference to it, and editing the template afterwards leaves the copy alone.
3. The Project panel listing templates apart from animations.
4. **Save as template** / **Use as template** / clear the flag.

Step 2 is the one carrying the design. If a copy ever ends up linked, this
whole definition collapses into a worse symbol.
