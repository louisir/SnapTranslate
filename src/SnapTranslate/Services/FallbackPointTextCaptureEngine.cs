using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SnapTranslate.Services;

public sealed class FallbackPointTextCaptureEngine : IPointTextCaptureEngine
{
    private readonly IPointTextCaptureEngine[] _engines;

    public FallbackPointTextCaptureEngine(params IPointTextCaptureEngine[] engines)
    {
        _engines = engines;
    }

    public async Task<PointTextCaptureResult> CaptureAsync(Point screenPoint, CancellationToken cancellationToken)
    {
        string[] statuses = Array.Empty<string>();

        foreach (IPointTextCaptureEngine engine in _engines)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new PointTextCaptureResult(null);
            }

            PointTextCaptureResult result = await engine.CaptureAsync(screenPoint, cancellationToken);
            if (result.HasText)
            {
                return result;
            }

            if (!string.IsNullOrWhiteSpace(result.StatusMessage))
            {
                statuses = statuses.Append(result.StatusMessage).ToArray();
            }
        }

        return new PointTextCaptureResult(
            null,
            statuses.Length == 0 ? null : string.Join(Environment.NewLine, statuses));
    }
}
