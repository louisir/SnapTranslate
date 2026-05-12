using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace SnapTranslate.Services;

public sealed class OcrResult
{
    private const int MaxLineDistance = 56;
    private const int MaxTokenDistance = 32;
    private const double MinConfidence = 20.0;

    public OcrResult(IReadOnlyList<OcrTextLine> lines, string? statusMessage = null)
    {
        Lines = lines;
        StatusMessage = statusMessage;
    }

    public IReadOnlyList<OcrTextLine> Lines { get; }
    public string? StatusMessage { get; }

    public OcrTextLine? FindNearestText(Point screenPoint)
    {
        OcrTextLine? line = FindNearestLine(screenPoint);
        if (line is null)
        {
            return null;
        }

        OcrTextLine? token = TextTokenSelector.SelectNearestToken(line, screenPoint, MaxTokenDistance);

        return token ?? line;
    }

    public OcrTextLine? FindNearestLine(Point screenPoint)
    {
        OcrTextLine? nearby = Lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .Where(line => line.Confidence >= MinConfidence)
            .Where(line => DistanceToRectangle(screenPoint, line.ScreenBounds) <= MaxLineDistance)
            .OrderBy(line => DistanceToRectangle(screenPoint, line.ScreenBounds))
            .ThenByDescending(line => line.Confidence)
            .FirstOrDefault();

        if (nearby is not null)
        {
            return nearby;
        }

        return Lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .Where(line => line.ScreenBounds.Contains(screenPoint))
            .OrderByDescending(line => line.Confidence)
            .FirstOrDefault();
    }

    private static double DistanceToRectangle(Point point, Rectangle rectangle)
    {
        int dx = Math.Max(Math.Max(rectangle.Left - point.X, 0), point.X - rectangle.Right);
        int dy = Math.Max(Math.Max(rectangle.Top - point.Y, 0), point.Y - rectangle.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
