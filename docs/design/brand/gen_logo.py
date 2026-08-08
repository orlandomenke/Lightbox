#!/usr/bin/env python3
"""Generate the Lightbox brand logo SVGs from measured reference geometry.

Everything here is measured off docs/design/ui-reference.png (glyph crop at
+95+85, 120x120): hexagon frame x 1-73, y 4-98, stroke ~10.5, right edge
terminating at y~46; white face corners (39,27) (72,46) (66,74) (39,59);
gradient samples around the frame:
  apex #F88F3D, upper-left #F35852, left-mid #CD3790, bottom-left #802AC5,
  bottom-right #4143F8, right-end #684BFA, glow near tip.
"""

# ---- icon geometry (scaled x240/94 from the measured 72x94 box) ----------
# viewBox 0 0 200 260, hexagon: apex (100,10), UL (8,66), LL (8,194),
# bottom (100,250), LR (192,194); stroke t=33 (measured 13 of a 72-wide
# glyph — the first pass used 10.5 and the owner called it too thin).

A   = (100, 10)
UL  = (8, 66)
LL  = (8, 194)
B   = (100, 250)
LR  = (192, 194)
# The right edge does not end in a blunt cut: it tapers to a point just
# under the sheet's tip (owner: pointy at the top, never overlapping the
# white box, and not full-width where it meets it).
REND_O = (192, 116)   # the pointy tip, right at the sheet's corner
REND_I = (159, 150)   # where the inner edge starts its taper

AI  = (100, 48.6)     # inner apex (miter intersection)
ULI = (41, 84.6)
LLI = (41, 175.4)
BI  = (100, 211.4)
LRI = (159, 175.4)

FACE = [(105, 69), (189, 117), (174, 189), (105, 150)]

def poly(pts):
    return 'M' + ' L'.join(f'{x:g} {y:g}' for x, y in pts) + ' Z'

# Frame as five quads sharing miter seams, so each can carry its own leg of
# the gradient sweep (SVG has no conic gradient).
SEGS = [
    # (outer-from, outer-to, inner-to, inner-from, stops [(offset, colour)])
    (A,  UL, ULI, AI,  [(0, '#F9913C'), (1, '#F25B55')]),
    (UL, LL, LLI, ULI, [(0, '#F25B55'), (1, '#C9359F')]),
    (LL, B,  BI,  LLI, [(0, '#C9359F'), (0.55, '#802AC5'), (1, '#5A36E0')]),
    (B,  LR, LRI, BI,  [(0, '#5A36E0'), (1, '#4148F8')]),
    (LR, REND_O, REND_I, LRI, [(0, '#4148F8'), (0.72, '#7A5CFF'), (1, '#A98BFF')]),
]

def icon_gradient_defs():
    out = []
    for i, (o1, o2, _i2, _i1, stops) in enumerate(SEGS):
        s = ''.join(f'<stop offset="{off}" stop-color="{c}"/>' for off, c in stops)
        out.append(
            f'<linearGradient id="seg{i}" gradientUnits="userSpaceOnUse" '
            f'x1="{o1[0]}" y1="{o1[1]}" x2="{o2[0]}" y2="{o2[1]}">{s}</linearGradient>')
    out.append(
        '<linearGradient id="face" gradientUnits="userSpaceOnUse" '
        'x1="189" y1="117" x2="105" y2="150">'
        '<stop offset="0" stop-color="#FFFFFF"/>'
        '<stop offset="1" stop-color="#ECE8F4"/></linearGradient>')
    out.append(
        '<filter id="glow" x="-40%" y="-40%" width="180%" height="180%">'
        '<feGaussianBlur stdDeviation="10"/></filter>')
    out.append(
        '<filter id="shadow" x="-40%" y="-40%" width="180%" height="180%">'
        '<feGaussianBlur stdDeviation="5"/></filter>')
    out.append(
        '<radialGradient id="flare" gradientUnits="userSpaceOnUse" '
        'cx="189" cy="117" r="22">'
        '<stop offset="0" stop-color="#FFFFFF" stop-opacity="0.95"/>'
        '<stop offset="0.45" stop-color="#FFD9F0" stop-opacity="0.35"/>'
        '<stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/></radialGradient>')
    return '\n    '.join(out)

def icon_body(mono=None):
    """The glyph. mono=None -> colour; otherwise a single colour string."""
    parts = []
    if mono is None:
        for i, (o1, o2, i2, i1, _stops) in enumerate(SEGS):
            parts.append(f'<path d="{poly([o1, o2, i2, i1])}" fill="url(#seg{i})"/>')
        # the sheet glows through the opening and throws a shadow down-left
        # onto the frame, exactly as in the reference
        parts.append(f'<path d="{poly(FACE)}" fill="#FFFFFF" opacity="0.45" filter="url(#glow)"/>')
        shadow = poly([(x - 7, y + 9) for x, y in FACE])
        parts.append(f'<path d="{shadow}" fill="#0A0A18" opacity="0.75" filter="url(#shadow)"/>')
        parts.append(f'<path d="{poly(FACE)}" fill="url(#face)"/>')
        # bright flare where the sheet's tip crosses the opening
        parts.append('<circle cx="189" cy="117" r="22" fill="url(#flare)"/>')
    else:
        # In one colour the sheet would melt into the band it overlaps, so
        # carve a keyline gap around it — the job the shadow does in colour.
        cx = sum(x for x, _ in FACE) / 4
        cy = sum(y for _, y in FACE) / 4
        grown = [(cx + (x - cx) * 1.13, cy + (y - cy) * 1.13) for x, y in FACE]
        parts.append('<mask id="carve">'
                     '<rect x="0" y="0" width="200" height="260" fill="white"/>'
                     f'<path d="{poly(grown)}" fill="black"/></mask>')
        frame = ''.join(f'<path d="{poly([o1, o2, i2, i1])}" fill="{mono}"/>'
                        for (o1, o2, i2, i1, _stops) in SEGS)
        parts.append(f'<g mask="url(#carve)">{frame}</g>')
        parts.append(f'<path d="{poly(FACE)}" fill="{mono}"/>')
    return '\n    '.join(parts)

# ---- wordmark -------------------------------------------------------------
# Monoline squared caps, cap height 36, stroke 4, hand-authored to match the
# reference letterforms: detached T crossbar, floating G bar, squared B,
# rounded-square O, X solid at the crossing with flat-cut terminals.

CAP = 36.0
T_W = 4.0          # stroke
GAP = 20.0         # tracking

def stroked(d, w):
    return (f'<path d="{d}" fill="none" stroke="CURRENT" '
            f'stroke-width="{T_W}" stroke-linecap="butt" stroke-linejoin="miter"/>', w)

def filled(d, w):
    return (f'<path d="{d}" fill="CURRENT"/>', w)

# The reference face is EXTENDED: letters run ~1.6x wider than the cap
# height (O measures ~30 wide against an 18px cap), monoline throughout.

def letter_L():
    return stroked('M2 0 V34 H44', 44)

def letter_I():
    return stroked('M2 0 V36', 4)

def letter_G():
    # squared C with rounded left corners + floating middle bar from the right
    d = 'M56 2 H12 Q2 2 2 12 V24 Q2 34 12 34 H56 M56 18 H36'
    return stroked(d, 56)

def letter_H():
    return stroked('M2 0 V36 M54 0 V36 M2 18 H54', 56)

def letter_T():
    # crossbar detached from the stem (clear gap in the reference)
    return stroked('M0 2 H56 M28 9 V36', 56)

def letter_B():
    d = ('M2 0 V36 '
         'M2 2 H46 Q52 2 52 8 V12 Q52 18 46 18 H2 '
         'M2 18 H46 Q52 18 52 24 V28 Q52 34 46 34 H2')
    return stroked(d, 54)

def letter_O():
    d = 'M14 2 H44 Q58 2 58 14 V22 Q58 34 44 34 H14 Q2 34 2 22 V14 Q2 2 14 2 Z'
    return stroked(d, 60)

def letter_X():
    # solid at the crossing (owner's call), terminals cut flat on the cap
    # line and baseline like the reference's squared corners
    w, hw = 58.0, 7.6   # hw = horizontal width of a bar at a flat cut
    bar1 = [(0, 0), (hw, 0), (w, CAP), (w - hw, CAP)]
    bar2 = [(w - hw, 0), (w, 0), (hw, CAP), (0, CAP)]
    return filled(poly(bar1) + ' ' + poly(bar2), w)

LETTERS = [letter_L, letter_I, letter_G, letter_H, letter_T, letter_B, letter_O, letter_X]

def wordmark(colour):
    x = 0.0
    out = []
    for fn in LETTERS:
        body, adv = fn()
        out.append(f'<g transform="translate({x:g} 0)">{body.replace("CURRENT", colour)}</g>')
        x += adv + GAP
    width = x - GAP
    return '\n      '.join(out), width

# ---- assembly -------------------------------------------------------------

def icon_svg(mono=None):
    defs = '' if mono else f'<defs>\n    {icon_gradient_defs()}\n  </defs>\n  '
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 260">\n'
            f'  {defs}<g>\n    {icon_body(mono)}\n  </g>\n</svg>\n')

def lockup_svg(mono=None):
    wm_colour = mono if mono else '#FFFFFF'
    wm, wm_w = wordmark(wm_colour)
    # icon centred over the wordmark; wordmark width : icon width ~ 3.26 in
    # the reference, so scale the wordmark to 3.26x the icon's 184 width.
    target = 184 * 3.26
    s = target / wm_w
    icon_x = (target - 184) / 2 - 8  # centre the 184-wide hexagon (x starts at 8)
    view_w = target + 40
    view_h = 260 + 40 + CAP * s + 40
    defs = '' if mono else f'<defs>\n    {icon_gradient_defs()}\n  </defs>\n  '
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {view_w:g} {view_h:g}">\n'
            f'  {defs}<g transform="translate({20 + icon_x:g} 20)">\n    {icon_body(mono)}\n  </g>\n'
            f'  <g transform="translate(20 {260 + 40 + 20:g}) scale({s:g})">\n      {wm}\n  </g>\n</svg>\n')

import pathlib
here = pathlib.Path(__file__).parent
(here / 'lightbox-icon.svg').write_text(icon_svg())
(here / 'lightbox-icon-black.svg').write_text(icon_svg('#000000'))
(here / 'lightbox-icon-white.svg').write_text(icon_svg('#FFFFFF'))
(here / 'lightbox-logo.svg').write_text(lockup_svg())
(here / 'lightbox-logo-black.svg').write_text(lockup_svg('#000000'))
(here / 'lightbox-logo-white.svg').write_text(lockup_svg('#FFFFFF'))
print('written')
