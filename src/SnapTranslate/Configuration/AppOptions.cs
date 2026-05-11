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
    public Uri MicrosoftTranslatorEndpoint { get; init; } =
        TryCreateUri(Environment.GetEnvironmentVariable("SNAPTRANSLATE_MICROSOFT_TRANSLATOR_ENDPOINT"))
        ?? new Uri("https://api.cognitive.microsofttranslator.com");
    public string? MicrosoftTranslatorKey { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_MICROSOFT_TRANSLATOR_KEY");
    public string? MicrosoftTranslatorRegion { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_MICROSOFT_TRANSLATOR_REGION");
    public string MicrosoftTranslatorTargetLanguage { get; init; } =
        Environment.GetEnvironmentVariable("SNAPTRANSLATE_MICROSOFT_TRANSLATOR_TO")
        ?? NormalizeMicrosoftTargetLanguage(Environment.GetEnvironmentVariable("SNAPTRANSLATE_TARGET_LANG") ?? "zh");
    public Uri? LibreTranslateEndpoint { get; init; } = TryCreateUri(Environment.GetEnvironmentVariable("SNAPTRANSLATE_LIBRETRANSLATE_URL"));
    public string? LibreTranslateApiKey { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_LIBRETRANSLATE_API_KEY");
    public string? TesseractPath { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_TESSERACT_PATH");
    public string? TessDataDirectory { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_TESSDATA_DIR");
    public string? PaddleOcrRunnerPath { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_PADDLEOCR_RUNNER");
    public string PythonPath { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_PYTHON") ?? "python";
    public string PaddleOcrLanguage { get; init; } = Environment.GetEnvironmentVariable("SNAPTRANSLATE_PADDLEOCR_LANG") ?? "ch";
    public TimeSpan OcrProcessTimeout { get; init; } = TimeSpan.FromSeconds(30);

    private static Uri? TryCreateUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;
    }

    private static string NormalizeMicrosoftTargetLanguage(string language)
    {
        return language.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-Hans"
            : language;
    }
}
