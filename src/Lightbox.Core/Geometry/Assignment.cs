namespace Lightbox.Core.Geometry;

/// <summary>
/// Minimum-cost assignment: pair every row with at most one column so the
/// total cost is as small as possible.
/// </summary>
/// <remarks>
/// <para>
/// The Hungarian algorithm (Kuhn–Munkres), O(n²m). It exists because
/// <b>greedy pairing is not merely worse on average — it is wrong in a way an
/// artist sees immediately.</b> Take the cheapest pair, commit to it, repeat,
/// and one cheap pair can force an expensive one that a different first choice
/// would have avoided. B113 is that failure: a box moved down half its height
/// paired its bottom edge with the new top edge because that was the single
/// cheapest pair on the board, which left the old top edge nothing to go to but
/// the new bottom edge. Total cost 120 where the obvious answer costs 80, and
/// on screen the top of the box swept downwards through the shape.
/// </para>
/// <para>
/// Rectangular is fine: with more columns than rows every row is assigned and
/// the surplus columns are left out, and the transpose handles the other way
/// round. Callers pair the leftovers with null themselves, because what a
/// leftover *means* differs — an unmatched stroke fades in or out.
/// </para>
/// </remarks>
public static class Assignment
{
    /// <summary>
    /// Solve for <paramref name="cost"/>[row, column]. Returns one entry per
    /// row: the column it was assigned, or -1 when there was no column left.
    /// </summary>
    public static int[] Solve(double[,] cost)
    {
        var rows = cost.GetLength(0);
        var cols = cost.GetLength(1);
        if (rows == 0 || cols == 0) return Filled(rows);

        // The algorithm below needs at least as many columns as rows.
        if (rows > cols)
        {
            var flipped = new double[cols, rows];
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                    flipped[c, r] = cost[r, c];

            var byColumn = Solve(flipped);
            var back = Filled(rows);
            for (var c = 0; c < cols; c++)
                if (byColumn[c] >= 0) back[byColumn[c]] = c;
            return back;
        }

        // Potentials u/v, and p[j] = the row currently assigned to column j.
        // One-based internally: index 0 is the algorithm's virtual start.
        var u = new double[rows + 1];
        var v = new double[cols + 1];
        var p = new int[cols + 1];
        var way = new int[cols + 1];

        for (var i = 1; i <= rows; i++)
        {
            p[0] = i;
            var j0 = 0;
            var minv = new double[cols + 1];
            var used = new bool[cols + 1];
            Array.Fill(minv, double.PositiveInfinity);

            do
            {
                used[j0] = true;
                int i0 = p[j0], j1 = 0;
                var delta = double.PositiveInfinity;

                for (var j = 1; j <= cols; j++)
                {
                    if (used[j]) continue;
                    var cur = cost[i0 - 1, j - 1] - u[i0] - v[j];
                    if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                    if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                }

                for (var j = 0; j <= cols; j++)
                {
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                    else { minv[j] -= delta; }
                }

                j0 = j1;
            }
            while (p[j0] != 0);

            // Walk the augmenting path back, flipping the assignments on it.
            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            }
            while (j0 != 0);
        }

        var result = Filled(rows);
        for (var j = 1; j <= cols; j++)
            if (p[j] != 0) result[p[j] - 1] = j - 1;
        return result;
    }

    private static int[] Filled(int n)
    {
        var a = new int[n];
        Array.Fill(a, -1);
        return a;
    }
}
