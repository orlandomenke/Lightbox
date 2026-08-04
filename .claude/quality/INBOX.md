# Inbox

Raw, unstructured bug reports from outside this repo's tooling — a person, or
an agent (ChatGPT, another assistant) that does not know `BUGS.md`'s
conventions. Land them here, not in `BUGS.md` directly.

**Why the separation.** `BUGS.md`'s checkboxes are derived, not typed:
`scripts/bugs.py sync` expects `evidence:` to name a real, existing test (or
the literal `manual`), ids to be unique across the whole file, a domain from
a fixed list, and a priority read off the severity × reach matrix. A report
written by something that has not read those rules will not follow them —
not out of carelessness, but because it cannot see `bugs.py` or the codemap
that would let it name a real test. An entry with a guessed evidence line is
worse than no entry: it either fails `bugs.py check` loudly, or — worse —
happens to resolve against an unrelated test and reports a bug fixed that
never was.

**Format: whatever the reporter can produce.** A sentence, a screenshot
description, a repro. No structure is enforced here on purpose — the cost of
writing a report should not be "learn this file's conventions first."

**Processing.** A Claude Code session periodically works through this file:
for each entry, it either
- turns it into a proper `BUGS.md` entry — a real id, a domain, a priority,
  and either a named regression test or `evidence: manual` if none can reach
  it headlessly — following the format documented at the top of `BUGS.md`;
- or, if the report does not describe a real defect (already fixed, not
  reproducible, out of scope), removes it and says why in the commit;
- or, if it is ambiguous enough to need a person, leaves it here with a note
  under **Needs a decision** below, rather than guessing.

Processed entries are deleted from this file — `INBOX.md` is a queue, not an
archive. The archive is `BUGS.md` itself.

---

## Unprocessed

<!-- Append new reports below this line, oldest first. -->
Project Management / project docker
1. Project docker should always reflect what is on disk unless marked as remove from project. I created a project with multiple folders and documents. Removed the folders and documents from disk, but the project docker still lists them.
2. The show project folder in the file manager button should always navigate to the selected folder or file in the docker. This is confusing as the other option, which should stay, is hidden in the RMB context menu. This also means the root folder should be visible in the docker.
3. The create something in this project dropdown; no other items except for Character and Document produce files. And using the dropdown is confusing, it is undecipherable what is a folder and what is a workfile. Perhaps rename document also to workfile.
4. We cannot rename items in the project docker. This should be reflected on disk.
5. Creating a folder of file from the project should prompt a name, before writing it to disk. Folders and files now just are numbered due to same name.
6. Character sheets are not saved to disk, not visible in the project docker. Outside of a project (single file) a character sheet is a manually saved document. Creating a character sheet should directly prompt saving. In a project, a character sheet is directly added, similar to how the project dockers add them directly. Character sheets should also prompt for a names of the file before the saving prompt.
7. It point one to 5 might be a processing issue. After closing and reopening lightbox all files where on disk. This has to happen real-time.

UI + Interface
1. dockers, settings and tabs either keep all data persistent across documents instead of per document. Few examples; I added a reference to a document, switch tab to a document without a reference. All fields were visible; I might have adjusted brush settings, tweaked the fill bucket or might have zoomed in in one document but don't want that on another. Layers, Timeline, Character sheets seem correct. All others seemingly not. Imagine creating a new document not part of any project than I do not want to view the project docker, or project related symbols or any other config, or brush settings. 
There is no good way the identify open document (tabs) that they are part of a project or not. A small boxed P in the tab would already help a lot. In the title bar of the OS would be a great additional position. Not sure if that is possible based on tab, or needed. 


Character sheet / character sheet docker
1. this might have been a fluke, restarting Lightbox does not reproduce the issue: i switch documents a couple of times Untitled document and character sheet and I was unable to paint anything anymore. 

Palette docker / swatches
1. swatches seem not to be saved in the project on creation. And they also seems not to be saved and loaded per single file file.

blur, smudge and blender tool
1. click and release changes the effected area. The strokes seems to "settle" on release. What we paint should be what we see. No post-processing settling of any kind
2. Setting brush tip does not seem to effect these brushes.
3. Individual brush settings need to be cached for the duration of the session. On close and reopend brush settings are back to default.
4. When brush settings are changed, present the user a save settings button next to the all brush settings. This is stored per file and/or per project.  

## Needs a decision

<!-- Reports that could not be turned into a BUGS.md entry without a human call. -->
