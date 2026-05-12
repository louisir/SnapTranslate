using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

namespace SnapTranslate.Services;

public static class TextTokenSelector
{
    private static readonly Regex TokenRegex = new(
        @"[\p{L}\p{N}][\p{L}\p{N}_+#@.'’-]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static OcrTextLine? SelectNearestToken(OcrTextLine line, Point screenPoint, int maxDistance)
    {
        return EstimateTokens(line.Text, line.ScreenBounds, line.Confidence)
            .Where(token => TextSanitizer.IsUsefulForTranslation(token.Text))
            .Where(token => DistanceToRectangle(screenPoint, token.ScreenBounds) <= maxDistance)
            .OrderBy(token => DistanceToRectangle(screenPoint, token.ScreenBounds))
            .ThenByDescending(token => token.ScreenBounds.Contains(screenPoint))
            .ThenByDescending(token => token.Confidence)
            .FirstOrDefault();
    }

    public static string SelectNearestTokenText(string text, Rectangle bounds, Point screenPoint, int maxDistance)
    {
        OcrTextLine line = new(text, bounds, 100);
        return SelectNearestToken(line, screenPoint, maxDistance)?.Text ?? text;
    }

    private static IEnumerable<OcrTextLine> EstimateTokens(string text, Rectangle bounds, double confidence)
    {
        MatchCollection matches = TokenRegex.Matches(text);
        if (matches.Count <= 1)
        {
            yield break;
        }

        int textLength = text.Length;
        foreach (Match match in matches.Cast<Match>())
        {
            Rectangle tokenBounds = EstimateTokenBounds(bounds, match.Index, match.Length, textLength);
            yield return new OcrTextLine(match.Value, tokenBounds, confidence);
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
