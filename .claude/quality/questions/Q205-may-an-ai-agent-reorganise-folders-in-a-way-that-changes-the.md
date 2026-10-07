# Q205 · May an AI agent reorganise folders in a way that changes the picture

Raised by: art-director, reviewing the MCP folder tools (G12, 2026-10-07).
Filing a layer into a folder puts it at the folder's top, and grouping layers
that are apart gathers them into one run. Either can change which layer is in
front — and so the image, on every frame of an animation. The first draft's
`move_to_folder` description even said "layer order otherwise stays as it was",
which was false.

What it blocks: the default of `move_to_folder` and `group_layers` over MCP.
The in-app gesture is not in question — for the artist, Ctrl+G gathers as
Photoshop and Krita do (Q204).

## Asked, with the recommendation and the cost of each alternative

**Recommended: refuse unless asked.** A move or group that would change the
stacking is refused, naming the layers it would pass; the agent sends
`reorder: true` to go ahead, and the reply says `reordered: true`. Housekeeping
that leaves the picture alone just works. Cost: an agent that really means it
needs a second call.

Alternative: allow it and report it, as Ctrl+G does in the app — the reply says
`reordered: true` and the undo step reads "Agent: …". Cost: an agent tidying up
can change the picture without asking first.

The argument for the recommendation is the one the same diff already made for
locks: an agent's request carries no intent, so it should be stricter than the
artist's own hand.

## Answered 2026-10-07: refuse unless asked

As recommended. Built in the same change: `reorder` on both tools, `reordered`
in every reply, the refusal naming the layers in between, and "Agent: …" on
every undo step an agent makes with these tools, so the history shows whose it
was. `IpcFolderTests.GroupingScatteredLayersIsRefusedUnlessReorderIsSaid` and
`CreateMoveAndGroup_AreEachOneUndoStep_AndShowInGetScene` pin it.
