using System.Threading;
using System.Threading.Tasks;

namespace SnapTranslate.Services;

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(string text, CancellationToken cancellationToken);
}
