using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SnapTranslate.Services;

public static partial class TextSanitizer
{
    public static string NormalizeForTranslation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string normalized = text
            .Normalize(NormalizationForm.FormKC)
            .Replace('\u00A0', ' ')
            .Replace('\u200B', '\0')
            .Replace('\u200C', '\0')
            .Replace('\u200D', '\0')
            .Replace('\uFEFF', '\0')
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        StringBuilder builder = new(normalized.Length);
        foreach (char character in normalized)
        {
            if (character == '\0')
            {
                continue;
            }

            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.Control && character is not '\n' and not '\t')
            {
                continue;
            }

            builder.Append(character);
        }

        normalized = HyphenatedLineBreakRegex().Replace(builder.ToString(), string.Empty);
        normalized = WhitespaceRegex().Replace(normalized, " ");
        normalized = CjkInnerSpaceRegex().Replace(normalized, string.Empty);
        normalized = SpaceBeforePunctuationRegex().Replace(normalized, "$1");

        return normalized.Trim();
    }

    public static bool IsUsefulForTranslation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Any(character =>
            char.IsLetterOrDigit(character) ||
            character is >= '\u4E00' and <= '\u9FFF');
    }

    [GeneratedRegex(@"(?<=\p{L})-\s*\n\s*(?=\p{L})", RegexOptions.CultureInvariant)]
    private static partial Regex HyphenatedLineBreakRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<=[\u4E00-\u9FFF])\s+(?=[\u4E00-\u9FFF])", RegexOptions.CultureInvariant)]
    private static partial Regex CjkInnerSpaceRegex();

    [GeneratedRegex(@"\s+([,.;:!?，。；：！？、）\]\}])", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceBeforePunctuationRegex();
}
