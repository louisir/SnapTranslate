using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SnapTranslate.Configuration;

namespace SnapTranslate.Services;

public sealed class MicrosoftTranslatorService : ITranslationService
{
    private readonly AppOptions _options;
    private readonly HttpClient _httpClient = new();

    public MicrosoftTranslatorService(AppOptions options)
    {
        _options = options;
    }

    public async Task<TranslationResult> TranslateAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.MicrosoftTranslatorKey))
        {
            return new TranslationResult(text, "未配置微软翻译。设置 SNAPTRANSLATE_MICROSOFT_TRANSLATOR_KEY 后可调用 Azure AI Translator。");
        }

        Uri requestUri = BuildRequestUri();
        using HttpRequestMessage request = new(HttpMethod.Post, requestUri);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.MicrosoftTranslatorKey);
        request.Headers.Add("X-ClientTraceId", Guid.NewGuid().ToString());

        if (!string.IsNullOrWhiteSpace(_options.MicrosoftTranslatorRegion))
        {
            request.Headers.Add("Ocp-Apim-Subscription-Region", _options.MicrosoftTranslatorRegion);
        }

        request.Content = JsonContent.Create(new[] { new MicrosoftTranslatorRequest(text) });

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new TranslationResult(
                text,
                $"微软翻译失败 HTTP {(int)response.StatusCode}: {TrimForStatus(responseText)}");
        }

        MicrosoftTranslatorResponse[]? result =
            System.Text.Json.JsonSerializer.Deserialize<MicrosoftTranslatorResponse[]>(responseText);
        string? translatedText = result?[0].Translations?[0].Text;

        return new TranslationResult(
            string.IsNullOrWhiteSpace(translatedText) ? text : translatedText,
            null);
    }

    private static string TrimForStatus(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300];
    }

    private Uri BuildRequestUri()
    {
        List<string> queryParts = new()
        {
            "api-version=3.0",
            $"to={Uri.EscapeDataString(_options.MicrosoftTranslatorTargetLanguage)}"
        };

        if (!_options.SourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            queryParts.Add($"from={Uri.EscapeDataString(_options.SourceLanguage)}");
        }

        UriBuilder builder = new(_options.MicrosoftTranslatorEndpoint)
        {
            Path = CombinePath(_options.MicrosoftTranslatorEndpoint.AbsolutePath, "translate"),
            Query = string.Join('&', queryParts)
        };

        return builder.Uri;
    }

    private static string CombinePath(string basePath, string relativePath)
    {
        string trimmedBase = basePath.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmedBase) || trimmedBase == "/")
        {
            return relativePath;
        }

        return $"{trimmedBase}/{relativePath}";
    }

    private sealed record MicrosoftTranslatorRequest(
        [property: JsonPropertyName("Text")] string Text);

    private sealed record MicrosoftTranslatorResponse(
        [property: JsonPropertyName("translations")] MicrosoftTranslation[]? Translations);

    private sealed record MicrosoftTranslation(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("to")] string To);
}
