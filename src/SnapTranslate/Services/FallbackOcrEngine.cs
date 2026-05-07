using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SnapTranslate.Services;

public sealed class FallbackOcrEngine : IOcrEngine
{
    private readonly IOcrEngine[] _engines;

    public FallbackOcrEngine(params IOcrEngine[] engines)
    {
        _engines = engines;
    }

    public async Task<OcrResult> RecognizeAsync(CaptureRegion capture, CancellationToken cancellationToken)
    {
        string[] errors = Array.Empty<string>();

        foreach (IOcrEngine engine in _engines)
        {
            OcrResult result = await engine.RecognizeAsync(capture, cancellationToken);
            if (result.Lines.Count > 0)
            {
                return result;
            }

            if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            {
                errors = errors.Append(result.StatusMessage).ToArray();
            }
        }

        string? status = errors.Length == 0 ? null : string.Join(Environment.NewLine, errors);
        return new OcrResult(Array.Empty<OcrTextLine>(), status);
    }
}
