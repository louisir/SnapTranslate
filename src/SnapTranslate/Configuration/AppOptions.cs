using System;
using System.Drawing;

namespace SnapTranslate.Configuration;

public sealed class AppOptions
{
    public TimeSpan HoverDelay { get; init; } = TimeSpan.FromMilliseconds(700);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public Size CaptureSize { get; init; } = new(360, 140);
    public string OcrLanguage { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_OCR_LANG") ?? "eng+chi_sim";
    public string TargetLanguage { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_TARGET_LANG") ?? "zh";
    public string SourceLanguage { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_SOURCE_LANG") ?? "auto";
    public Uri? LibreTranslateEndpoint { get; init; } = TryCreateUri(Environment.GetEnvironmentVariable("SNAPTRANSLATE_LIBRETRANSLATE_URL"));
    public string? LibreTranslateApiKey { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_LIBRETRANSLATE_API_KEY");
    public string? TesseractPath { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_TESSERACT_PATH");
    public string? TessDataDirectory { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_TESSDATA_DIR");

    private static Uri? TryCreateUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;
    }
}
