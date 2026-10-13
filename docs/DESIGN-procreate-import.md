# Importing Procreate brushes

`BrushsetReader` reads `.brushset` and `.brush` files. This note says what the
format is, where that was learned, what is carried across and what is not, and
which numbers are choices.

## Source of truth (SENSITIVITY L1)

**A file the owner supplied**, 2026-10-12: one `.brushset` of ten brushes, opened
and read. Nothing was learned from Procreate's binary, from another importer's
source or from leaked material. Two public formats sit underneath it and were
read from their public descriptions:

- **zip**, through the runtime's `System.IO.Compression`.
- **Apple's binary property list** (`bplist00`), from the layout comment at the
  head of Apple's open-source `CFBinaryPList.c`: header, objects, offset table,
  32-byte trailer; a marker byte per object, type in the high nibble.

The settings file is a *keyed archive*: a property list holding an object table
(`$objects`) and a `$top` entry naming the root, with strings held by reference
and entry 0 the string `$null`. That arrangement was read off the owner's file,
where it is visible as plain keys; it is the one Apple's public Foundation
documentation describes for `NSKeyedArchiver`.

The brush settings themselves are self-describing: a dictionary of named values
(`paintSize`, `plotSpacing`, `shapeInverted` ...). The names were read from the
file. **Their meanings and scales were not published by anybody and are inferred
here**, which is the limit of this importer and is why the manual calls the size
an estimate.

No new dependency. No brush pack is in the repository (L4): the tests build
their archives byte by byte.

**The sample is somebody else's work, and stays out.** It is a third party's
brush pack that the owner downloaded; several of its brushes sit on Procreate's
stock shapes. It is reached through `LIGHTBOX_PROCREATE_SAMPLE` on the owner's
machine and must not be committed as a fixture. For the same reason (L2) no
built-in preset may be tuned by copying its numbers: the follow-on work maps
*what a setting means*, checked against a brush that uses it, and ships no
brush derived from the pack.

## The layout

| Entry | What it is |
| --- | --- |
| `<folder>/Brush.archive` | One brush's settings: a keyed archive (a binary property list holding an object table and a root reference). In a `.brush` it is at the archive's root. |
| `<folder>/Shape.png` | Optional. The tip, greyscale, **white is paint**. 2048 px in the owner's file. |
| `<folder>/Grain.png` | Optional. The texture the tip is drawn through. Not read. |
| `<folder>/QuickLook/Thumbnail.png` | A preview. Not read. |
| `brushset.plist` | Sets only. An XML property list: the set's name and the folders in the artist's order. |

A brush that uses one of Procreate's stock shapes has no `Shape.png`; its
settings name an image inside the app (`bundledShapePath`). Seven of the ten
brushes in the owner's file are like this. Those images are not in the file and
are not ours to ship, so such a brush arrives with no tip.

## What is carried

`ImportedBrush` is the same five things for every format, and this reader fills
them and nothing else:

| Ours | From | How |
| --- | --- | --- |
| Name | `name` | As written; the file's name when absent. |
| Tip | `Shape.png` | Brightness becomes alpha (inverted when `shapeInverted`), longest side brought down to 1024. |
| Size | `paintSize` x `maxSize` | Times `PixelsPerSizeUnit` (100), clamped 1 to 500. **A choice, see below.** |
| Spacing | `plotSpacing` | Read as a fraction of the diameter, floored at the engine's 0.02. |
| Opacity | `maxOpacity` | Clamped 0.01 to 1. |

**`PixelsPerSizeUnit = 100` is this importer's choice and has not been measured
against Procreate.** `paintSize` is the size slider (0 to 1) and `maxSize` is the
brush's own limit, which runs from 0.04 to 16 across the ten brushes. At 100 px a
unit they land at 4 px (HB Pencil) to 278 px (Airbrush Soft), which is the right
order for the brushes' names and nothing more than that. The way to replace it is
a side-by-side: one brush at a known size in Procreate on a canvas of known
pixels, measured.

`paintOpacity`, the opacity slider's last position, is deliberately not read:
it is where the artist left a slider, not a property of the brush.

## What is not carried

Grain (`Grain.png`, `textureScale`, `textureMovement`), pressure, tilt and speed
response (`dynamicsPressure*`, `dynamicsTilt*`, `dynamicsSpeed*` and their
curves), jitter (`dynamicsJitter*`, `plotJitter`, `shapeScatter`,
`shapeRandomise`), taper (`taper*`, `pencilTaper*`), rotation (`shapeRotation`,
`shapeAzimuth`), blend mode, and the wet-mix values (`dynamicsWetAccumulation`,
`dynamicsMix`, `dynamicsLoad`, bleed, dilution).

Several have an obvious home in `BrushSettings`: scatter, the jitters, a grain
as a texture, pressure to size. They are left out because every one of them is a
number whose scale would be a guess, and in the one file there is to test
against most of them are zero, so a mapping could not be checked even by eye.
They are the next step, one at a time, each against a brush that uses it.

The wet-mix values describe Procreate's paint engine. They would be approximated
by this app's colour mixing, never reproduced.

## Untrusted input (SENSITIVITY S1)

A brush pack is a file a stranger made. What the reader does about that:

- **Nothing is written to disk.** Entry names are matched, never used as paths.
  A name that climbs or nests deeper than one folder is not a brush.
- **Inflation is counted**, per entry, against a limit set by what the entry is
  for (1 MB settings, 1 MB manifest, 32 MB shape), whatever length the archive
  declares. Entries no brush needs are never opened.
- **A shape must be a PNG, and its size is read from its header** before a pixel
  is decoded; past 4096 px a side it is refused. The largest thing the reader
  ever holds is such a shape on a transparent background, decoded through RGBA:
  64 MB, plus 16 MB of brightness, for the length of one brush.
- **A file's tips share one budget**: 32 million characters of encoded tip,
  which is 64 MB held, and 256 MB of shape inflated whether it produced a tip or
  not. Past either the remaining brushes arrive without their shapes. Two archive records may point
  at the same bytes, so a megabyte of noise can be named a thousand times.
- **The property list reader builds each object once and pays for it.** A
  reference costs the file a byte and would otherwise cost the reader whatever
  it points at. A second reference gets the value already built; a reference to
  a value still being built is a container that contains itself, refused there;
  and what is built is charged to a budget of 16 bytes per byte of file, which
  is what stops many table entries sharing one offset. Counts are checked
  against the bytes that would hold them, no container is sized from a count,
  nesting is limited to 24 and values to 100,000.
- **The manifest is read forwards and refused past eight levels deep.** Never
  through a document tree: an element's text is gathered by recursion there,
  and forty thousand nested elements, 1.7 KB zipped, overflow the stack. A stack
  overflow cannot be caught; the application goes, with whatever was unsaved.
  The DOCTYPE is skipped and never fetched: no resolver.
- **Not cancellable inside one file.** The import window's Stop is checked
  between files. Under these bounds the worst file found in review costs tens of
  seconds on a background thread, with the app usable throughout: about 0.65 s a
  megabyte-of-noise tip, until the tip budget closes.
- **A brush that cannot be read costs that brush**, and a file with none left is
  a `FormatException` with a sentence in it, which the import window shows as a
  named unreadable file.

`ProcreateBrushImportTests` holds each of these with a file built to break it.

Found in review, not in design: the deep manifest and the shared blob were both
live in the first cut, each reproduced by the sensitivity-guardian with a file
of a few kilobytes that ended the process.
