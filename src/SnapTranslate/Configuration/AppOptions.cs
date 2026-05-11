using System;
using System.Drawing;
using System.Globalization;

namespace SnapTranslate.Configuration;

public sealed class AppOptions
{
    public AppOptions()
        : this(AppSettings.Load())
    {
    }

    public AppOptions(AppSettings settings)
    {
        HoverDelay = TimeSpan.FromMilliseconds(GetIntEnvironment("SNAPTRANSLATE_HOVER_DELAY_MS", settings.HoverDelayMs));
        PollInterval = TimeSpan.FromMilliseconds(50);
        CaptureSize = new Size(
            GetIntEnvironment("SNAPTRANSLATE_CAPTURE_WIDTH", settings.CaptureWidth),
            GetIntEnvironment("SNAPTRANSLATE_CAPTURE_HEIGHT", settings.CaptureHeight));

        OcrLanguage = GetEnvironment("SNAPTRANSLATE_OCR_LANG", settings.OcrLanguage);
        TargetLanguage = GetEnvironment("SNAPTRANSLATE_TARGET_LANG", settings.TargetLanguage);
        SourceLanguage = GetEnvironment("SNAPTRANSLATE_SOURCE_LANG", settings.SourceLanguage);

        MicrosoftTranslatorEndpoint =
            TryCreateUri(GetEnvironment("SNAPTRANSLATE_MICROSOFT_TRANSLATOR_ENDPOINT", settings.MicrosoftTranslatorEndpoint))
            ?? new Uri("https://api.cognitive.microsofttranslator.com");
        MicrosoftTranslatorKey = EmptyToNull(GetEnvironment("SNAPTRANSLATE_MICROSOFT_TRANSLATOR_KEY", settings.MicrosoftTranslatorKey));
        MicrosoftTranslatorRegion = EmptyToNull(GetEnvironment("SNAPTRANSLATE_MICROSOFT_TRANSLATOR_REGION", settings.MicrosoftTranslatorRegion));
        MicrosoftTranslatorTargetLanguage = GetEnvironment(
            "SNAPTRANSLATE_MICROSOFT_TRANSLATOR_TO",
            string.IsNullOrWhiteSpace(settings.MicrosoftTranslatorTo)
                ? NormalizeMicrosoftTargetLanguage(TargetLanguage)
                : settings.MicrosoftTranslatorTo);

        LibreTranslateEndpoint = TryCreateUri(GetEnvironment("SNAPTRANSLATE_LIBRETRANSLATE_URL", settings.LibreTranslateUrl));
        LibreTranslateApiKey = EmptyToNull(GetEnvironment("SNAPTRANSLATE_LIBRETRANSLATE_API_KEY", settings.LibreTranslateApiKey));

        TesseractPath = EmptyToNull(GetEnvironment("SNAPTRANSLATE_TESSERACT_PATH", settings.TesseractPath));
        TessDataDirectory = EmptyToNull(GetEnvironment("SNAPTRANSLATE_TESSDATA_DIR", settings.TessDataDirectory));
        PaddleOcrRunnerPath = EmptyToNull(GetEnvironment("SNAPTRANSLATE_PADDLEOCR_RUNNER", settings.PaddleOcrRunnerPath));
        PythonPath = GetEnvironment("SNAPTRANSLATE_PYTHON", settings.PythonPath);
        PaddleOcrLanguage = GetEnvironment("SNAPTRANSLATE_PADDLEOCR_LANG", settings.PaddleOcrLanguage);
        OcrProcessTimeout = TimeSpan.FromSeconds(30);
    }

    public TimeSpan HoverDelay { get; }
    public TimeSpan PollInterval { get; }
    public Size CaptureSize { get; }
    public string OcrLanguage { get; }
    public string TargetLanguage { get; }
    public string SourceLanguage { get; }
    public Uri MicrosoftTranslatorEndpoint { get; }
    public string? MicrosoftTranslatorKey { get; }
    public string? MicrosoftTranslatorRegion { get; }
    public string MicrosoftTranslatorTargetLanguage { get; }
    public Uri? LibreTranslateEndpoint { get; }
    public string? LibreTranslateApiKey { get; }
    public string? TesseractPath { get; }
    public string? TessDataDirectory { get; }
    public string? PaddleOcrRunnerPath { get; }
    public string PythonPath { get; }
    public string PaddleOcrLanguage { get; }
    public TimeSpan OcrProcessTimeout { get; }

    private static Uri? TryCreateUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;
    }

    private static string GetEnvironment(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int GetIntEnvironment(string name, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string NormalizeMicrosoftTargetLanguage(string language)
    {
        return language.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-Hans"
            : language;
    }
}
