using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

namespace SnapTranslate.Services;

public sealed class OcrResult
{
    private const int MaxLineDistance = 56;
    private const int MaxTokenDistance = 32;
    private const double MinConfidence = 20.0;
    private static readonly Regex TokenRegex = new(
        @"[\p{L}\p{N}][\p{L}\p{N}_+#@.'’-]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        OcrTextLine? token = EstimateTokens(line)
            .Where(token => TextSanitizer.IsUsefulForTranslation(token.Text))
            .Where(token => DistanceToRectangle(screenPoint, token.ScreenBounds) <= MaxTokenDistance)
            .OrderBy(token => DistanceToRectangle(screenPoint, token.ScreenBounds))
            .ThenByDescending(token => token.ScreenBounds.Contains(screenPoint))
            .ThenByDescending(token => token.Confidence)
            .FirstOrDefault();

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

    private static IEnumerable<OcrTextLine> EstimateTokens(OcrTextLine line)
    {
        MatchCollection matches = TokenRegex.Matches(line.Text);
        if (matches.Count <= 1)
        {
            yield break;
        }

        int textLength = line.Text.Length;
        foreach (Match match in matches.Cast<Match>())
        {
            Rectangle bounds = EstimateTokenBounds(line.ScreenBounds, match.Index, match.Length, textLength);
            yield return new OcrTextLine(match.Value, bounds, line.Confidence);
        }
    }

    private static Rectangle EstimateTokenBounds(Rectangle lineBounds, int tokenStart, int tokenLength, int textLength)
    {
        if (textLength <= 0 || lineBounds.Width <= 1)
        {
            return lineBounds;
        }

        int left = lineBounds.Left + (int)Math.Round(lineBounds.Width * (tokenStart / (double)textLength));
        int right = lineBounds.Left + (int)Math.Round(lineBounds.Width * ((tokenStart + tokenLength) / (double)textLength));
        left = Math.Clamp(left, lineBounds.Left, lineBounds.Right - 1);
        right = Math.Clamp(right, left + 1, lineBounds.Right);

        return new Rectangle(left, lineBounds.Top, right - left, lineBounds.Height);
    }

    private static double DistanceToRectangle(Point point, Rectangle rectangle)
    {
        int dx = Math.Max(Math.Max(rectangle.Left - point.X, 0), point.X - rectangle.Right);
        int dy = Math.Max(Math.Max(rectangle.Top - point.Y, 0), point.Y - rectangle.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
