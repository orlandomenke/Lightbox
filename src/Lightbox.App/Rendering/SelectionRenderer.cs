using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Renders visual feedback for selected canvas objects:
/// bounding boxes, corner handles, and pivot points.
/// </summary>
public static class SelectionRenderer
{
    private const float HandleSize = 8f;
    private const float HandleRadius = HandleSize / 2f;
    private const float PivotRadius = 6f;

    /// <summary>Paint for bounding box outline (bright cyan).</summary>
    private static readonly SKPaint SelectionOutline = new()
    {
        Color = SKColors.Cyan,
        StrokeWidth = 2f,
        Style = SKPaintStyle.Stroke,
        IsAntialias = true,
    };

    /// <summary>Paint for corner handles (filled squares).</summary>
    private static readonly SKPaint HandleFill = new()
    {
        Color = SKColors.Cyan,
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    /// <summary>Paint for pivot point indicator.</summary>
    private static readonly SKPaint PivotFill = new()
    {
        Color = SKColors.Orange,
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    /// <summary>
    /// Draw a selection bounding box with corner handles and pivot point.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="bounds">Bounding box to draw.</param>
    /// <param name="pivotX">Pivot point X (world coordinates), or null to skip.</param>
    /// <param name="pivotY">Pivot point Y (world coordinates), or null to skip.</param>
    /// <param name="scale">Canvas zoom scale (used to keep handles constant screen size).</param>
    public static void DrawSelection(SKCanvas canvas, SKRectI bounds, double? pivotX, double? pivotY, double scale)
    {
        if (bounds.IsEmpty) return;

        var rect = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        float handleSize = (float)(HandleSize / scale); // Keep handles constant screen size
        float handleRadius = handleSize / 2f;
        float pivotRadius = (float)(PivotRadius / scale);

        // Draw bounding box outline
        canvas.DrawRect(rect, SelectionOutline);

        // Draw corner handles (8 points: 4 corners + 4 edge midpoints)
        DrawCornerHandles(canvas, rect, handleRadius);
        DrawEdgeMidpointHandles(canvas, rect, handleRadius);

        // Draw pivot point if provided
        if (pivotX.HasValue && pivotY.HasValue)
        {
            var pivotRect = SKRect.Create((float)pivotX.Value - pivotRadius, (float)pivotY.Value - pivotRadius, pivotRadius * 2, pivotRadius * 2);
            canvas.DrawOval(pivotRect, PivotFill);
        }
    }

    /// <summary>Draw 4 corner handles.</summary>
    private static void DrawCornerHandles(SKCanvas canvas, SKRect rect, float radius)
    {
        // Top-left
        canvas.DrawRect(SKRect.Create(rect.Left - radius, rect.Top - radius, radius * 2, radius * 2), HandleFill);
        // Top-right
        canvas.DrawRect(SKRect.Create(rect.Right - radius, rect.Top - radius, radius * 2, radius * 2), HandleFill);
        // Bottom-left
        canvas.DrawRect(SKRect.Create(rect.Left - radius, rect.Bottom - radius, radius * 2, radius * 2), HandleFill);
        // Bottom-right
        canvas.DrawRect(SKRect.Create(rect.Right - radius, rect.Bottom - radius, radius * 2, radius * 2), HandleFill);
    }

    /// <summary>Draw 4 edge midpoint handles.</summary>
    private static void DrawEdgeMidpointHandles(SKCanvas canvas, SKRect rect, float radius)
    {
        float midX = (rect.Left + rect.Right) / 2f;
        float midY = (rect.Top + rect.Bottom) / 2f;

        // Top middle
        canvas.DrawRect(SKRect.Create(midX - radius, rect.Top - radius, radius * 2, radius * 2), HandleFill);
        // Bottom middle
        canvas.DrawRect(SKRect.Create(midX - radius, rect.Bottom - radius, radius * 2, radius * 2), HandleFill);
        // Left middle
        canvas.DrawRect(SKRect.Create(rect.Left - radius, midY - radius, radius * 2, radius * 2), HandleFill);
        // Right middle
        canvas.DrawRect(SKRect.Create(rect.Right - radius, midY - radius, radius * 2, radius * 2), HandleFill);
    }

    /// <summary>Draw hover preview (faint outline).</summary>
    public static void DrawHoverPreview(SKCanvas canvas, SKRectI bounds, double scale)
    {
        if (bounds.IsEmpty) return;

        var rect = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        using var paint = new SKPaint
        {
            Color = SKColors.Cyan.WithAlpha(100),
            StrokeWidth = (float)(1f / scale),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        canvas.DrawRect(rect, paint);
    }
}
