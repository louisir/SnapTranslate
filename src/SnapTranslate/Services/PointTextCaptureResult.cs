namespace SnapTranslate.Services;

public sealed record PointTextCaptureResult(string? Text, string? StatusMessage = null)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}
