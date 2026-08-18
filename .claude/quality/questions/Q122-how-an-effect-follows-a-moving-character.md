# Q122 · How an effect follows a moving character — **answered 2026-08-18**

Raised by the owner while step 4 was in flight: *"Can this system take into
account the movement direction? How about change of direction? Or should I
generate multiple times to account for it? … It could be a parameter. So that
way I can account for wind (gusts) and other movements."* Four choices were
prompted and all four took the recommendation.

**The framing first, because three different things get conflated.** *Ambient
wind* is a background flow pushed into the field, and it moves smoke that is
already in the air. *Emitter motion* is the source travelling through the
element, which lays a trail. *Attachment* is the element's box moving on the
canvas so the effect stays with a character. A run cycle wants wind and
attachment; a swung torch wants emitter motion.

**Generating multiple times is the wrong answer, and the reason is inertia.**
When a character turns, the smoke already in the air keeps going the old way
while new smoke goes the new way — the whip and the lag come free from the
simulation having history. Baking a "run right" element and a "run left" element
and cutting between them cannot produce that, because each bake starts from
still air and the cut pops. One element with a keyed wind is strictly better
than several bakes, and this is the clearest case so far of the simulation
earning its cost over a library of pre-made cycles.

**1. A keyable wind vector on the element.** One direction-and-strength pair,
keyed over the frame range: gusts, a character running, and changes of direction
all fall out of it. Emitters keep their existing constant velocity for the
separate case of a source that moves.

The cost accepted is a teaching one and it is real: **running right means wind
from the right** — it is a change of reference frame, not a wind anybody would
describe that way — so the manual has to say it or an artist will key the wind
backwards. Per-emitter keys alone were declined because wind that blows on smoke
already in the air cannot be expressed that way, and that is most of what wind
does.

**2. Bind the element to a drawing's anchor.** The app already stores per-frame
anchors on drawings and the motion trail already reads them, so an element bound
to one follows the character with no keying at all — a torch flame that stays in
the hand through a whole run.

The cost is the interesting one: **the bake now depends on something outside the
element.** That is the same coupling `docs/DESIGN-fluid-effects.md` deferred
under *"fluid that flows around drawn art"* — it makes a bake depend on another
layer, which is a record question rather than a solver one. It is worth paying
here because the alternative is re-keying an effect by hand every time the
character's timing changes, which is exactly the drudgery anchors exist to
remove.

**3. A pre-roll before frame 0.** Run the simulation for N frames before the
first drawn one, so an element starts from an established plume rather than from
still air. One integer on the record, and it fixes the commonest complaint
before anybody reports it — the first half-second of an effect looking thin.

**It does not make a cycle seamless and it is not pretended to.** The blended
loop that would was declined for a specific reason rather than for cost:
blending two contour sets is not blending two images, because the strokes have
no correspondence between frames — which is precisely what Q116 chose when it
took per-frame tracing over advected contours. Looping is therefore a known gap,
and it lands on the pillar the export tooling serves, since game asset work is
mostly cycles. Should it become the priority, it is an argument for revisiting
Q116 rather than for a blend.

**4. Reuse the effects design's key vocabulary.** `docs/DESIGN-effects.md`
already specifies `EffectParam { Value, Keys }` with `Easing` — the same
key-plus-easing language `CameraKey` uses — so a camera move, a drawing inbetween
and a wind gust are described one way and share a timeline editor.

The cost accepted: **it makes an unbuilt design a dependency**, and the first
user of a shared vocabulary usually shapes it, so the wind branch will end up
defining what `EffectParam` actually is. That is better than the alternative it
was weighed against — a second keying vocabulary and a timeline editor built
twice, which is how a node system ships twice, the thing that design warns about
in its own words.

**Not part of step 4.** Step 4's objective is fire end to end and this is a
second objective; it gets its own branch. What is recorded here is the shape it
takes when it does.
