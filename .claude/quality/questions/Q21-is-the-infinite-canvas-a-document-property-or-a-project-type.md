# Q21 · Is the infinite canvas a document property or a project-type default? — **answered (c), both, and they are not alternatives** — *superseded by Q71: the infinite canvas was removed 2026-08-12*

**Answered 2026-08-04: both — and the question contained a false choice.**
"Document property *or* project default" reads as two designs; it is one. The
reach rule already says exactly this: a project type decides *what is on, what
is in front of you, and what a new document starts with — never what the
application can do*. So the **property lives on the document** (that is the
capability, available everywhere) and a **project supplies the default** (that
is what a new document starts with). Answering "both" is the rule applied, not
a compromise between two readings.

Both cases the answer came from are real and neither needs a mechanism the
other lacks: somebody making *one* infinite-canvas animation turns the property
on for that document, and somebody producing a run of product or service
animations sets it once on the project so every new document starts that way
rather than switching it on each time.

**The mechanism exists and is proven, which is why this is cheap.** A project
already feeds new documents a default brush — `BrushScope`,
`BrushScopeDefaults`, guarded by `ANewDocumentInTheProjectIsFedThatBrush`, and
by `AProjectThatNeverAsksForThisWritesNoBrushKey` so an unused default writes
nothing. An infinite-canvas default is the same shape against the same
precedent. Note it is the **project** that carries it, not only the project
*type*: a studio's own project can default to unbounded whatever type it is,
which is what the reach rule means by defaults never deciding availability.

**Blocks:** nothing. It was never a blocker — a project type can only default a
property that exists, so the document property comes first under either answer.


*The analysis below is what the answer was reached from, kept for the reasoning
rather than as a live recommendation — (a) and (b) turned out to be one design.*

The reach rule settles the hard half already: every feature is reachable in every
project type, so this is not "who is allowed an infinite canvas". It is what a
new document starts with, and whether turning it on later is a document edit or a
project setting.

**(a) A document property, off by default everywhere.** Matches the camera
exactly — absent from the file until authored, askable for anywhere. Simplest,
and "optional means absent" falls out for free.

**(b) A project-type default.** A storyboard or an illustration starts unbounded,
a sprite project starts fixed. More convenient on day one, and it puts a
behaviour an artist has to reason about into a manifest they rarely open.

**Recommend (a)** until somebody asks for (b), because (a) is a prerequisite for
(b) rather than an alternative to it: a project type can only default a property
that already exists.
