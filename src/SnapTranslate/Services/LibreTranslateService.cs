using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SnapTranslate.Configuration;

namespace SnapTranslate.Services;

public sealed class LibreTranslateService : ITranslationService
{
    private readonly AppOptions _options;
    private readonly HttpClient _httpClient = new();

    public LibreTranslateService(AppOptions options)
    {
        _options = options;
    }

    public async Task<TranslationResult> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        if (_options.LibreTranslateEndpoint is null)
        {
            return new TranslationResult(text, "未配置翻译引擎。设置 SNAPTRANSLATE_LIBRETRANSLATE_URL 后可调用 LibreTranslate。");
        }

        Uri requestUri = new(_options.LibreTranslateEndpoint, "/translate");
        LibreTranslateRequest request = new(text, _options.SourceLanguage, _options.TargetLanguage, "text", _options.LibreTranslateApiKey);
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(requestUri, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        LibreTranslateResponse? translated = await response.Content.ReadFromJsonAsync<LibreTranslateResponse>(cancellationToken: cancellationToken);
        return new TranslationResult(translated?.TranslatedText ?? text);
    }

    private sealed record LibreTranslateRequest(
        [property: JsonPropertyName("q")] string Text,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("api_key")] string? ApiKey);

    private sealed record LibreTranslateResponse([property: JsonPropertyName("translatedText")] string TranslatedText);
}
