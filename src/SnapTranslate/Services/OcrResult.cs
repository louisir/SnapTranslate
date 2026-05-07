using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace SnapTranslate.Services;

public sealed class OcrResult
{
    public OcrResult(IReadOnlyList<OcrTextLine> lines, string? statusMessage = null)
    {
        Lines = lines;
        StatusMessage = statusMessage;
    }

    public IReadOnlyList<OcrTextLine> Lines { get; }
    public string? StatusMessage { get; }

    public OcrTextLine? FindNearestLine(Point screenPoint)
    {
        return Lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .OrderBy(line => DistanceToRectangle(screenPoint, line.ScreenBounds))
            .ThenByDescending(line => line.Confidence)
            .FirstOrDefault();
    }

    private static double DistanceToRectangle(Point point, Rectangle rectangle)
    {
        int dx = Math.Max(Math.Max(rectangle.Left - point.X, 0), point.X - rectangle.Right);
        int dy = Math.Max(Math.Max(rectangle.Top - point.Y, 0), point.Y - rectangle.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
