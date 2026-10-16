# Q283 · When the canvas grows, what fills the new margin of a Background layer — **answered 2026-10-16, against the recommendation**

Raised while fixing [[B479]], 2026-10-16, on the owner's jump document: its
paper is a grey fill stroke on a locked Background layer covering exactly the
old 1920×1080, and `SceneRenderer.BackgroundOf` draws no paper colour when a
document has a Background layer. So a grown canvas shows the new strip as
checkerboard, which reads as a broken canvas rather than as more paper. Asked
with the question prompt.

What it blocks: the branch that makes resize canvas fill the margin.

**Recommendation was:** extend the background — the resize adds, in the same
undo step, a fill under the Background layer's existing marks covering the new
paper, in that fill's colour. Photoshop and Krita do this to a Background layer;
no existing stroke is touched, so no grain re-rolls. Cost: one stroke per grow,
left harmlessly behind by a later crop. The alternatives were leaving it
transparent (nothing to build, reads as broken) and always drawing the scene's
paper colour underneath (cheap, but white beside grey here, and it changes how
every document with a Background layer renders).

## The answer

**The owner's own rule, which is Photoshop's:** *"if background is locked,
fill. Otherwise transparent."* So the fill happens only when the Background
layer is locked — the state Photoshop's Background layer is always in — and an
unlocked one is treated as an ordinary layer whose new margin is empty. The
recommendation did not distinguish the two, and the answer does; the colour and
the mechanism (a new fill stroke beneath the existing marks, one undo step) are
as recommended.
