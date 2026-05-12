using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SnapTranslate.Services;

public sealed class OcrPointTextCaptureEngine : IPointTextCaptureEngine
{
    private readonly ScreenCaptureService _captureService;
    private readonly IOcrEngine _ocrEngine;

    public OcrPointTextCaptureEngine(ScreenCaptureService captureService, IOcrEngine ocrEngine)
    {
        _captureService = captureService;
        _ocrEngine = ocrEngine;
    }

    public async Task<PointTextCaptureResult> CaptureAsync(Point screenPoint, CancellationToken cancellationToken)
    {
        using CaptureRegion capture = _captureService.CaptureAround(screenPoint);
        OcrResult ocrResult = await _ocrEngine.RecognizeAsync(capture, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return new PointTextCaptureResult(null);
        }

        OcrTextLine? line = ocrResult.FindNearestText(screenPoint);
        if (line is null || string.IsNullOrWhiteSpace(line.Text))
        {
            return new PointTextCaptureResult(null, ocrResult.StatusMessage);
        }

        string normalizedText = TextSanitizer.NormalizeForTranslation(line.Text);
        if (!TextSanitizer.IsUsefulForTranslation(normalizedText))
        {
            return new PointTextCaptureResult(null, ocrResult.StatusMessage);
        }

        return new PointTextCaptureResult(
            normalizedText,
            CombineStatus("来自 OCR", ocrResult.StatusMessage));
    }

    private static string? CombineStatus(params string?[] statuses)
    {
        string[] parts = statuses
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Select(status => status!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return parts.Length == 0 ? null : string.Join(Environment.NewLine, parts);
    }
}
