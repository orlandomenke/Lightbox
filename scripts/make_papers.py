"""Build Lightbox's scanned papers from ambientCG (CC0) displacement maps.

    python make_papers.py <folder with the extracted ambientCG sets> <output folder>

Each paper is the set's 1K displacement map, as 8-bit grey, white = peaks:
  1. undulation longer than 256 px is removed (a smooth high-pass in the
     Fourier domain, so the result still tiles) - a brush wants the sheet's
     tooth, not a tile-sized light patch that would repeat across a canvas;
  2. stretched between the 0.5th and 99.5th percentile, so every paper uses
     the whole range and "depth" means the same thing on each.
"""
import sys, pathlib
import numpy as np
from PIL import Image

root = pathlib.Path(sys.argv[1])
out = pathlib.Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)

SETS = {"plain": "Paper001", "kraft": "Paper002", "fibre": "Paper006", "linen": "Fabric061", "plaster": "Plaster001"}
CUT = 256.0

for name, asset in SETS.items():
    a = np.asarray(Image.open(root / asset / f"{asset}_1K-JPG_Displacement.jpg").convert("L"), dtype=np.float64)
    h, w = a.shape
    fx = np.fft.fftfreq(w)[None, :]
    fy = np.fft.fftfreq(h)[:, None]
    f = np.sqrt(fx ** 2 + fy ** 2)
    keep = 1 - np.exp(-(f * CUT) ** 2)
    b = np.real(np.fft.ifft2(np.fft.fft2(a) * keep))
    lo, hi = np.percentile(b, [0.5, 99.5])
    n = np.clip((b - lo) / (hi - lo), 0, 1)
    img = Image.fromarray((n * 255 + 0.5).astype(np.uint8), "L")
    path = out / f"{name}.png"
    img.save(path, optimize=True)
    print(f"{name:8s} {asset:11s} {w}x{h}  mean {n.mean():.2f}  std {n.std():.2f}  {path.stat().st_size // 1024} KB")
