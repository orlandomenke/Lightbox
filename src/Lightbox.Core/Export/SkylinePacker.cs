namespace Lightbox.Core.Export;

/// <summary>Where one sprite ended up on the sheet.</summary>
/// <param name="Index">The frame it came from, so the caller can match it back.</param>
public sealed record PackedRect(int Index, int X, int Y, int Width, int Height);

/// <summary>A finished pack.</summary>
/// <param name="Rects">One per input, in <b>input order</b> rather than pack order.</param>
public sealed record PackResult(int Width, int Height, IReadOnlyList<PackedRect> Rects)
{
    /// <summary>Pixels the sheet occupies.</summary>
    public long Area => (long)Width * Height;

    /// <summary>Pixels the sprites actually use, padding included.</summary>
    public long UsedArea => Rects.Sum(r => (long)r.Width * r.Height);
}

/// <summary>
/// Bottom-left skyline packing: sprites of different sizes on one sheet, tighter
/// than a uniform grid.
/// </summary>
/// <remarks>
/// <para>
/// The grid stays the default and the reason is in <c>SpriteSheetExporter</c>:
/// equal cells are what union bounds produce anyway, and every engine importer
/// reads a grid. Packing earns its place when the frames are <em>ragged</em> —
/// per-frame trimming, or a sheet holding several animations — and it is only
/// usable at all alongside per-sprite rects in the sidecar, which is why the two
/// landed together.
/// </para>
/// <para>
/// <b>Deterministic, and this is not a nicety.</b> Same sizes in, byte-identical
/// sheet out. Not for invariant 2's sake — no dab dynamic is involved — but
/// because a re-export that reshuffles the atlas makes every downstream diff
/// meaningless and turns "did the art change?" into a question nobody can answer.
/// So the sort is total: height, then width, then <b>input index</b>, which never
/// ties. A sort that stopped at height would depend on enumeration order and be
/// stable only by luck.
/// </para>
/// </remarks>
public static class SkylinePacker
{
    /// <summary>One horizontal run of the skyline: everything at or above y is free.</summary>
    private record struct Node(int X, int Y, int Width);

    /// <summary>
    /// Pack sizes onto the smallest sheet this algorithm finds.
    /// </summary>
    /// <param name="sizes">Sprite sizes, in frame order.</param>
    /// <param name="padding">Transparent gutter added around every sprite.</param>
    /// <param name="width">
    /// Sheet width, or null to choose one. The choice is a near-square target —
    /// the square root of the total area — widened to fit the widest sprite,
    /// because a sheet narrower than its widest sprite cannot be packed at all.
    /// </param>
    public static PackResult Pack(IReadOnlyList<(int Width, int Height)> sizes, int padding = 0, int? width = null)
    {
        if (sizes.Count == 0) return new PackResult(1, 1, []);

        // Padding is part of the box from here on, so the packer never has to
        // remember it and no gutter can be lost at a sheet edge.
        var boxes = sizes
            .Select((s, i) => (Index: i, W: Math.Max(1, s.Width) + padding * 2, H: Math.Max(1, s.Height) + padding * 2))
            .ToList();

        var widest = boxes.Max(b => b.W);
        var totalArea = boxes.Sum(b => (long)b.W * b.H);
        var sheetWidth = Math.Max(widest, width ?? (int)Math.Ceiling(Math.Sqrt(totalArea)));

        // The total order that makes the result reproducible.
        var order = boxes
            .OrderByDescending(b => b.H)
            .ThenByDescending(b => b.W)
            .ThenBy(b => b.Index)
            .ToList();

        var skyline = new List<Node> { new(0, 0, sheetWidth) };
        var placed = new PackedRect?[sizes.Count];
        var sheetHeight = 0;

        foreach (var box in order)
        {
            var (x, y) = Fit(skyline, box.W, sheetWidth);
            placed[box.Index] = new PackedRect(
                box.Index, x + padding, y + padding, box.W - padding * 2, box.H - padding * 2);
            Raise(skyline, x, box.W, y + box.H);
            sheetHeight = Math.Max(sheetHeight, y + box.H);
        }

        return new PackResult(sheetWidth, Math.Max(1, sheetHeight), placed.Select(p => p!).ToList());
    }

    /// <summary>
    /// Lowest position a box of this width fits, leftmost on a tie.
    /// </summary>
    /// <remarks>
    /// Bottom-left: the y is what is minimised, and x breaks the tie. Preferring
    /// low over left is what keeps the sheet short rather than merely tidy on the
    /// first row.
    /// </remarks>
    private static (int X, int Y) Fit(List<Node> skyline, int boxWidth, int sheetWidth)
    {
        var bestX = 0;
        var bestY = int.MaxValue;

        for (var i = 0; i < skyline.Count; i++)
        {
            var x = skyline[i].X;
            if (x + boxWidth > sheetWidth) break;

            // The box rests on the highest run it spans — anything lower would
            // overlap what is already there.
            var y = 0;
            var remaining = boxWidth;
            for (var j = i; j < skyline.Count && remaining > 0; j++)
            {
                y = Math.Max(y, skyline[j].Y);
                remaining -= skyline[j].Width;
            }
            if (remaining > 0) break;   // runs out of skyline before the box fits

            if (y < bestY)
            {
                bestY = y;
                bestX = x;
            }
        }

        return bestY == int.MaxValue ? (0, skyline.Max(n => n.Y)) : (bestX, bestY);
    }

    /// <summary>Raise the skyline over [x, x+width) to y, and tidy up.</summary>
    private static void Raise(List<Node> skyline, int x, int width, int y)
    {
        var right = x + width;

        // Split any run the new box lands in the middle of, so the edges stay
        // exact — an off-by-one here shows up as sprites overlapping by a pixel,
        // which reads as a rendering bug rather than a packing one.
        for (var i = 0; i < skyline.Count; i++)
        {
            var node = skyline[i];
            var nodeRight = node.X + node.Width;
            if (node.X < x && nodeRight > x)
            {
                skyline[i] = node with { Width = x - node.X };
                skyline.Insert(i + 1, new Node(x, node.Y, nodeRight - x));
            }
            else if (node.X < right && nodeRight > right)
            {
                skyline[i] = node with { Width = right - node.X };
                skyline.Insert(i + 1, new Node(right, node.Y, nodeRight - right));
            }
        }

        skyline.RemoveAll(n => n.X >= x && n.X + n.Width <= right);
        skyline.Add(new Node(x, y, width));
        skyline.Sort((a, b) => a.X.CompareTo(b.X));

        // Merge equal-height neighbours, or the skyline grows a run per sprite
        // and Fit turns quadratic on a long sheet.
        for (var i = skyline.Count - 1; i > 0; i--)
        {
            if (skyline[i].Y != skyline[i - 1].Y) continue;
            skyline[i - 1] = skyline[i - 1] with { Width = skyline[i - 1].Width + skyline[i].Width };
            skyline.RemoveAt(i);
        }
    }
}
