using System.Threading;
using System.Threading.Tasks;

namespace SnapTranslate.Services;

public interface IOcrEngine
{
    Task<OcrResult> RecognizeAsync(CaptureRegion capture, CancellationToken cancellationToken);
}
