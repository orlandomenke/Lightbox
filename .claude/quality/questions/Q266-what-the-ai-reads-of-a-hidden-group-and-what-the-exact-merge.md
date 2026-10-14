# Q266 · What the AI reads of a hidden group, and what the exact merge option is called — **answered 2026-10-13**

**Answered: a group that shows nothing at all is left out of what the AI reads; and the merge dialog's exact option is called "Keep exact".** Two questions asked together with `AskUserQuestion`; the first as recommended, the second against the recommendation.

Raised by the sensitivity and UI reviews of the grouped merge (Q265), 2026-10-13.

What it blocks: nothing that was built — both were answerable after the fact. They were asked rather than defaulted because one decides what leaves the machine and the other is a word the artist reads.

## 1. Hidden ink

A merged drawing keeps its strokes in a group, and the inbetweener reads through groups. So a group's lines reached the model whatever its mask, clip or opacity did to them. Against an unmerged masked layer that is nothing new — its strokes were always read whole. Against the flattened merge this replaces it is: masked-out ink was gone from the pixels and was sent as nothing.

- **Drop fully hidden groups** (recommended) — **chosen.** A group at zero opacity, or behind a mask that applies with nothing painted on it, is left out. Anything partly hidden is still read whole.
- *Send everything and say so.* No code. Cost: the model may inbetween lines nobody can see, and is paid for them.
- *Send only what shows.* Cut each group's strokes by its mask and clip first. The faithful answer and the most work — the stroke cutter would have to learn to cut by a mask — so its own branch.

**What the chosen answer does not do**, said so it is not mistaken for more: a line a mask hides *part* of is sent in full; a clip onto nothing and a mask whose paint happens to miss every line cannot be told without rendering, and are sent. `StrokeRecordCleaner.ShowsNothing` is the whole rule.

## 2. The label

The dialog asked at merge time offers "Cut the lines" (plain strokes, nearly exact) and an exact option that keeps the erasers with their drawing in a group. It was labelled "Keep the erasers, in a group", and "group" already means a layer folder in this application (Ctrl+G).

- *Keep the erasers with the lines* (recommended) — keeps "group" out of the label and in the explanation only.
- *Keep the erasers, in a group* — as it was; names the thing **Split group** will later undo.
- **Keep exact** — **chosen.** Names the outcome rather than the mechanism, opposite "Cut the lines". The explanation under it still says group, and so does the manual.

The owner chose the shortest. What it gives up is that the label alone no longer says erasers are what the question is about; the dialog's first line does.
