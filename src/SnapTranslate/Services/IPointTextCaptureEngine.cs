using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace SnapTranslate.Services;

public interface IPointTextCaptureEngine
{
    Task<PointTextCaptureResult> CaptureAsync(Point screenPoint, CancellationToken cancellationToken);
}
