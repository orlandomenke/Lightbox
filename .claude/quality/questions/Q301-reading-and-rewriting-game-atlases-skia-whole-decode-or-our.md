# Q301 · Reading and rewriting game atlases: Skia whole decode or our own streaming PNG — **answered 2026-11-27: our own streaming PNG**

Raised by: the sensitivity review of the Unity link's step 2 (Q300,
`docs/DESIGN-unity-link.md`). Opening a clip was meant to decode an atlas a row
at a time and keep only the cells it needs. On the SkiaSharp build Lightbox
ships (3.119.4), `SKCodec.StartScanlineDecode` returns `Unimplemented` for every
PNG, so the fallback, a whole decode, was the only path that ever ran. A real
character atlas of 12 288 × 5 120 cost about 250 MB to take a few cells out, and
nothing but the PNG header's 65 536-a-side cap bounded it.

What it blocks: step 2 landing (S1), and the shape of step 3, which has to
rewrite the same atlas with a few cells replaced.

| | What it costs |
| --- | --- |
| **Our own streaming PNG reader and writer** (recommended, **chosen**) | About 200 lines on .NET's own zlib, for the 8-bit RGB/RGBA non-interlaced PNGs that Unity, Photoshop and Skia write. Memory becomes the cells plus one row. Write-back can rewrite the atlas row by row and keep its bit depth and colour type exactly, and the pixel hash covers the stored bytes rather than a decoder's interpretation of them. Any other PNG falls back to a whole Skia decode under a 64-megapixel budget. One more format to maintain, and it has to be tested as hard as any parser. |
| Whole decode, with a budget | Nothing new to maintain. About 250 MB per open, about 500 MB for a write-back (old and new atlas), and re-encoding through Skia may change the file's bit depth or colour type. |
| Whole decode, no budget | Only the overflow and ordering bugs fixed. A hostile or huge PNG can still exhaust memory, which fails S1. |

## What was built

`PngRows` (Lightbox.Import) reads chunks with their CRCs checked, inflates the
IDAT stream through `ZLibStream`, and undoes the five row filters one row at a
time, handing back RGBA. It reads only as many rows as it is asked for, so a
decompression bomb costs nothing past the last needed row. Anything outside the
supported set is refused by name, and `UnityClipImport` then decodes the
texture whole through Skia, but only under the budget, and records no pixel
hash for it, so write-back leaves those frames alone. The writer half is step 3.

Measured in Release on the largest real atlas (12 288 × 5 120): 0.54 s to read
every row, against Skia's 0.32 s whole decode, holding one 48 KB row instead of
250 MB. Opening a clip is dominated by encoding the placeholder frames, not by
this.
