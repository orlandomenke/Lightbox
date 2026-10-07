using Avalonia.Input;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Part of the CanvasControl — the transform gizmo's cage mode (Q199).
/// </summary>
/// <remarks>
/// <para>
/// <b>A fourth gizmo mode, beside the affine box, the free quad and the
/// bands.</b> In cage mode a lattice of control points covers the box and
/// dragging one bends the drawing smoothly around it. The box itself is
/// pinned, as in band mode, for the same reason: a box that resized would be
/// a scale again, and the mode exists to do the thing a scale cannot.
/// </para>
/// <para>
/// <b>The gizmo decides nothing about the warp.</b> It holds a
/// <see cref="CageWarp.Lattice"/>, moves a point when one is dragged, and
/// hands the lattice to the view model for the commit and its mesh for the
/// preview — the same split every other mode keeps. The mesh is rebuilt once
/// per gizmo change, not per publish, so a drag costs one tessellation and the
/// renderer draws the same vertices until the next move.
/// </para>
/// </remarks>
public partial class CanvasControl
{
    private bool _txCage;
    private CageWarp.Lattice _txLattice;
    private int _txCageGrid = CageWarp.DefaultGrid;
    private int? _txCageDrag;
    private PassMesh? _txCageMesh;
    private (SKPoint[][] Rows, SKPoint[][] Columns)? _txCageLines;

    /// <summary>
    /// Cage mode: a lattice of handles over the box instead of a box that
    /// resizes.
    /// </summary>
    /// <remarks>
    /// Switching in reseeds the lattice from the current bounds, so the handles
    /// belong to the box as it is now; switching out throws it away — a cage is
    /// a gesture, not a document, as Q199 decided along with the bands.
    /// </remarks>
    public bool TransformCage
    {
        get => _txCage;
        set
        {
            if (_txCage == value) return;
            _txCage = value;
            if (value)
            {
                _txPerspective = false;
                TxDropBands();
                SeedCageFromBounds();
            }
            else
            {
                _txCageMesh = null;
            }
            _txCageDrag = null;
            TxChanged();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Cells per axis, two to six. Changing it reseeds the lattice: a denser
    /// grid over a half-dragged one has no honest answer for where the new
    /// points should stand.
    /// </summary>
    public int TransformCageGrid
    {
        get => _txCageGrid;
        set
        {
            var clamped = Math.Clamp(value, CageWarp.MinGrid, CageWarp.MaxGrid);
            if (_txCageGrid == clamped) return;
            _txCageGrid = clamped;
            if (_txCage)
            {
                SeedCageFromBounds();
                _txCageDrag = null;
                TxChanged();
                InvalidateVisual();
            }
        }
    }

    private void SeedCageFromBounds()
    {
        _txLattice = CageWarp.Lattice.Identity(_txMinX, _txMinY, _txMaxX, _txMaxY, _txCageGrid, _txCageGrid);
        _txCageMesh = null;
        _txCageLines = null;
    }

    /// <summary>The lattice, for the commit.</summary>
    public CageWarp.Lattice TransformCageResult => _txLattice;

    /// <summary>Whether cage mode would move anything.</summary>
    public bool TransformCageIsIdentity => !_txLattice.Moves;

    /// <summary>
    /// The lattice as the mesh the preview draws — null outside cage mode or
    /// while the lattice is untouched, which is what the pass builder keys on.
    /// </summary>
    /// <remarks>
    /// Texture coordinates are the undeformed positions in document pixels,
    /// which are the moving bitmap's own pixels: the band crops already rely
    /// on document space and bitmap space being the same thing here. Cached
    /// until the next gizmo change, because a publish happens per pointer
    /// event and the mesh only changes when a handle moves.
    /// </remarks>
    public PassMesh? TransformCageMesh
    {
        get
        {
            if (!_txActive || !_txCage || !_txLattice.Moves) return null;
            return _txCageMesh ??= ToPassMesh(CageWarp.MeshOf(_txLattice));
        }
    }

    /// <summary>Core's doubles and ints to Skia's points and indices.</summary>
    internal static PassMesh ToPassMesh(CageWarp.Mesh mesh)
    {
        var positions = new SKPoint[mesh.Target.Length];
        var texs = new SKPoint[mesh.Source.Length];
        for (var k = 0; k < mesh.Source.Length; k++)
        {
            positions[k] = new SKPoint((float)mesh.Target[k].X, (float)mesh.Target[k].Y);
            texs[k] = new SKPoint((float)mesh.Source[k].X, (float)mesh.Source[k].Y);
        }
        var indices = new ushort[mesh.Triangles.Length];
        for (var k = 0; k < mesh.Triangles.Length; k++) indices[k] = (ushort)mesh.Triangles[k];
        return new PassMesh(positions, texs, indices);
    }

    /// <summary>How close the pointer must come to grab a handle, in document px.</summary>
    private double TxCageTolerance() => 8.0 / Math.Max(0.01, FitScale() * _zoom);

    /// <summary>The control point under a document point, or null.</summary>
    internal int? TxCageHitTest(double x, double y)
    {
        if (!_txCage) return null;
        var tol = TxCageTolerance();
        var best = -1;
        var bestDist = tol;
        for (var k = 0; k < _txLattice.Points.Count; k++)
        {
            var (px, py) = _txLattice.Points[k];
            var d = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
            if (d <= bestDist)
            {
                bestDist = d;
                best = k;
            }
        }
        return best >= 0 ? best : null;
    }

    /// <summary>
    /// A press in cage mode: take hold of a handle, or nothing at all.
    /// </summary>
    /// <returns>True when cage mode owns the press.</returns>
    /// <remarks>
    /// <b>Cage mode owns every press inside the box</b>, handle or not, so the
    /// corner and edge handles beneath cannot resize a box that is meant to be
    /// pinned. A press outside the box falls through to whatever the canvas
    /// does with one, as a band-mode press does.
    /// </remarks>
    internal bool TxCagePress(double x, double y)
    {
        if (!_txCage) return false;
        if (TxCageHitTest(x, y) is { } hit)
        {
            _txCageDrag = hit;
            InvalidateVisual();
            return true;
        }
        var tol = TxCageTolerance();
        return x >= _txMinX - tol && x <= _txMaxX + tol && y >= _txMinY - tol && y <= _txMaxY + tol;
    }

    /// <summary>Drag the held handle to a document point.</summary>
    internal void TxCageDragTo(double x, double y)
    {
        if (_txCageDrag is not { } held) return;
        _txLattice = CageWarp.Drag(_txLattice, held, x, y);
        _txCageMesh = null;
        _txCageLines = null;
        TxChanged();
        InvalidateVisual();
    }

    private void TxCageRelease()
    {
        _txCageDrag = null;
        TxSettle();
    }

    /// <summary>Put every handle back where the grid had it.</summary>
    private void TxResetCage()
    {
        if (_txCage) SeedCageFromBounds();
    }

    // ---- the two grid modes, dispatched as one --------------------------------
    //
    // Bands and the cage are the gizmo's two modes that pin the box and put
    // something inside it. CanvasControl.cs is on the monolith ratchet, so the
    // places it asks "is a grid mode up, and does it want this?" ask once, here,
    // rather than once per mode there — a third such mode adds a line to this
    // file, not to that one.

    /// <summary>Leave both grid modes, keeping nothing.</summary>
    private void TxDropGridModes()
    {
        TxDropBands();
        TxDropCage();
    }

    /// <summary>A fresh session: box mode, both grid modes seeded from nothing.</summary>
    private void TxResetGridModes()
    {
        TxResetBands();
        TxDropCage();
    }

    /// <summary>Reset's half that is about shapes: the free quad and the cage's handles.</summary>
    private void TxReseedShapes()
    {
        SeedQuadFromCorners();
        TxResetCage();
    }

    /// <summary>Whether the grid mode in force is the identity, or null when neither is up.</summary>
    private bool? TxGridModeIdentity =>
        _txCage ? TransformCageIsIdentity
        : _txBands ? TransformBandsAreIdentity
        : null;

    private bool TxGridModePressHandled(double x, double y, PointerEventArgs e) =>
        TxBandPressHandled(x, y, e) || TxCagePressHandled(x, y, e);

    private bool TxGridModeMoveHandled(PointerEventArgs e) =>
        TxBandMoveHandled(e) || TxCageMoveHandled(e);

    private bool TxGridModeReleaseHandled(PointerEventArgs e) =>
        TxBandReleaseHandled(e) || TxCageReleaseHandled(e);

    private static bool DrawGridModeInstead(SKCanvas canvas, TxGizmoData g, float scale) =>
        DrawBandsInstead(canvas, g, scale) || DrawCageInstead(canvas, g, scale);

    /// <summary>Leave cage mode, keeping nothing.</summary>
    private void TxDropCage()
    {
        _txCage = false;
        _txCageDrag = null;
        _txCageMesh = null;
        _txCageLines = null;
    }

    private bool TxCagePressHandled(double x, double y, PointerEventArgs e)
    {
        if (!TxCagePress(x, y)) return false;
        e.Pointer.Capture(this);
        e.Handled = true;
        return true;
    }

    private bool TxCageMoveHandled(PointerEventArgs e)
    {
        if (!_txActive || _txCageDrag is null) return false;
        var (x, y) = ViewToDoc(e.GetPosition(this));
        TxCageDragTo(x, y);
        e.Handled = true;
        return true;
    }

    private bool TxCageReleaseHandled(PointerEventArgs e)
    {
        if (!_txActive || _txCageDrag is null) return false;
        TxCageRelease();
        e.Pointer.Capture(null);
        e.Handled = true;
        return true;
    }

    /// <summary>The lattice's grid lines and handles, for the overlay.</summary>
    /// <remarks>
    /// The lines are the mesh's own rows and columns rather than straight
    /// segments between handles, so what the artist sees bending is the curve
    /// the drawing will take. Four quads per cell is what the preview mesh
    /// uses, so the two agree. Tessellated once per handle move and kept —
    /// this is asked for on every paint, pans and zooms included, and the
    /// lattice does not change on those (leak-hunter's one finding).
    /// </remarks>
    internal readonly record struct TxCageOverlay(
        SKPoint[] Handles, SKPoint[][] Rows, SKPoint[][] Columns, int? Held);

    private TxCageOverlay? TxCageOverlayNow()
    {
        if (!_txCage) return null;
        var (rows, columns) = _txCageLines ??= CageLines(_txLattice);
        var handles = new SKPoint[_txLattice.Points.Count];
        for (var k = 0; k < handles.Length; k++)
        {
            handles[k] = new SKPoint((float)_txLattice.Points[k].X, (float)_txLattice.Points[k].Y);
        }
        return new TxCageOverlay(handles, rows, columns, _txCageDrag);
    }

    private static (SKPoint[][] Rows, SKPoint[][] Columns) CageLines(in CageWarp.Lattice lattice)
    {
        var mesh = CageWarp.MeshOf(lattice);
        var rows = new SKPoint[lattice.Rows + 1][];
        var columns = new SKPoint[lattice.Cols + 1][];
        const int sub = 4;
        for (var j = 0; j <= lattice.Rows; j++)
        {
            var row = new SKPoint[mesh.Across];
            for (var i = 0; i < mesh.Across; i++)
            {
                var (x, y) = mesh.Target[j * sub * mesh.Across + i];
                row[i] = new SKPoint((float)x, (float)y);
            }
            rows[j] = row;
        }
        for (var i = 0; i <= lattice.Cols; i++)
        {
            var column = new SKPoint[mesh.Down];
            for (var j = 0; j < mesh.Down; j++)
            {
                var (x, y) = mesh.Target[j * mesh.Across + i * sub];
                column[j] = new SKPoint((float)x, (float)y);
            }
            columns[i] = column;
        }
        return (rows, columns);
    }

    /// <summary>
    /// Draw the lattice instead of the pivot, in cage mode. Returns true when
    /// it has drawn, so the shared painter is one line — as the bands are.
    /// </summary>
    private static bool DrawCageInstead(SKCanvas canvas, TxGizmoData g, float scale)
    {
        if (g.Cage is not { } cage) return false;
        var colour = new SKColor(0xe0, 0xa0, 0x30, 240);
        using var line = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f / scale,
            Color = colour.WithAlpha(170),
        };
        using var path = new SKPath();
        foreach (var poly in cage.Rows.Concat(cage.Columns))
        {
            path.Reset();
            path.MoveTo(poly[0]);
            for (var k = 1; k < poly.Length; k++) path.LineTo(poly[k]);
            canvas.DrawPath(path, line);
        }
        using var grip = new SKPaint { IsAntialias = true, Color = colour };
        using var held = new SKPaint { IsAntialias = true, Color = SKColors.White };
        var r = 4f / scale;
        for (var k = 0; k < cage.Handles.Length; k++)
        {
            canvas.DrawCircle(cage.Handles[k], r, grip);
            if (cage.Held == k) canvas.DrawCircle(cage.Handles[k], r * 0.5f, held);
        }
        return true;
    }
}
