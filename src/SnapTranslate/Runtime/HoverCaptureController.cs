using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using SnapTranslate.Configuration;
using SnapTranslate.Services;
using SnapTranslate.UI;

namespace SnapTranslate.Runtime;

public sealed class HoverCaptureController : IDisposable
{
    private static readonly TimeSpan SelectionCandidateWindow = TimeSpan.FromSeconds(4);
    private const int DragSelectionThreshold = 6;

    private readonly AppOptions _options;
    private readonly ScreenCaptureService _captureService;
    private readonly SelectedTextCaptureService _selectedTextCaptureService;
    private readonly IOcrEngine _ocrEngine;
    private readonly ITranslationService _translationService;
    private readonly BubbleWindow _bubbleWindow;
    private readonly DispatcherTimer _timer;
    private Point _lastPosition;
    private DateTimeOffset _lastMovementAt = DateTimeOffset.UtcNow;
    private CancellationTokenSource? _currentCapture;
    private bool _hoverTriggered;
    private bool _isCapturing;
    private bool _leftMouseDown;
    private bool _dragDetected;
    private bool _selectionCaptureAttempted;
    private Point _mouseDownPosition;
    private DateTimeOffset _selectionCandidateUntil = DateTimeOffset.MinValue;

    public HoverCaptureController(
        AppOptions options,
        ScreenCaptureService captureService,
        SelectedTextCaptureService selectedTextCaptureService,
        IOcrEngine ocrEngine,
        ITranslationService translationService,
        BubbleWindow bubbleWindow)
    {
        _options = options;
        _captureService = captureService;
        _selectedTextCaptureService = selectedTextCaptureService;
        _ocrEngine = ocrEngine;
        _translationService = translationService;
        _bubbleWindow = bubbleWindow;
        _lastPosition = Forms.Cursor.Position;
        _timer = new DispatcherTimer { Interval = _options.PollInterval };
        _timer.Tick += OnTimerTick;
    }

    public bool IsEnabled { get; set; } = true;
    public void Start() => _timer.Start();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        CancelCurrentCapture();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (!IsEnabled)
        {
            CancelCurrentCapture();
            return;
        }

        Point currentPosition = Forms.Cursor.Position;
        if (UpdateSelectionTracking(currentPosition))
        {
            _bubbleWindow.Hide();
            CancelCurrentCapture();
            return;
        }

        if (!_isCapturing && TryConsumeSelectionCandidate())
        {
            _lastPosition = currentPosition;
            _lastMovementAt = DateTimeOffset.UtcNow;
            _hoverTriggered = true;
            await TranslateSelectedTextAndShowAsync(currentPosition);
            return;
        }

        if (currentPosition != _lastPosition)
        {
            _lastPosition = currentPosition;
            _lastMovementAt = DateTimeOffset.UtcNow;
            _hoverTriggered = false;
            _bubbleWindow.Hide();
            CancelCurrentCapture();
            return;
        }

        if (_hoverTriggered || _isCapturing || DateTimeOffset.UtcNow - _lastMovementAt < _options.HoverDelay)
        {
            return;
        }

        _hoverTriggered = true;
        await CaptureTranslateAndShowAsync(currentPosition);
    }

    private async Task CaptureTranslateAndShowAsync(Point cursorPosition)
    {
        CancelCurrentCapture();
        using CancellationTokenSource captureCts = new();
        _currentCapture = captureCts;
        _isCapturing = true;

        try
        {
            string? selectedText = await TryGetRecentSelectedTextAsync(captureCts.Token);
            if (captureCts.IsCancellationRequested)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                await TranslateAndShowAsync(cursorPosition, selectedText, "来自划选文本", captureCts);
                return;
            }

            using CaptureRegion capture = _captureService.CaptureAround(cursorPosition);
            OcrResult ocrResult = await _ocrEngine.RecognizeAsync(capture, captureCts.Token);
            if (captureCts.IsCancellationRequested)
            {
                return;
            }

            OcrTextLine? line = ocrResult.FindNearestLine(cursorPosition);
            if (line is null || string.IsNullOrWhiteSpace(line.Text))
            {
                _bubbleWindow.ShowMessage(cursorPosition, "未识别到文字", ocrResult.StatusMessage);
                return;
            }

            string normalizedText = TextSanitizer.NormalizeForTranslation(line.Text);
            if (!TextSanitizer.IsUsefulForTranslation(normalizedText))
            {
                _bubbleWindow.ShowMessage(cursorPosition, "未识别到可翻译文本", ocrResult.StatusMessage);
                return;
            }

            await TranslateAndShowAsync(cursorPosition, normalizedText, ocrResult.StatusMessage, captureCts);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _bubbleWindow.ShowMessage(cursorPosition, "取词失败", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_currentCapture, captureCts))
            {
                _currentCapture = null;
            }

            _isCapturing = false;
        }
    }

    private void CancelCurrentCapture()
    {
        _currentCapture?.Cancel();
        _currentCapture = null;
    }

    private bool UpdateSelectionTracking(Point currentPosition)
    {
        bool isLeftDown = (Forms.Control.MouseButtons & Forms.MouseButtons.Left) == Forms.MouseButtons.Left;
        if (isLeftDown && !_leftMouseDown)
        {
            _leftMouseDown = true;
            _dragDetected = false;
            _selectionCaptureAttempted = false;
            _selectionCandidateUntil = DateTimeOffset.MinValue;
            _mouseDownPosition = currentPosition;
        }
        else if (isLeftDown)
        {
            int dx = Math.Abs(currentPosition.X - _mouseDownPosition.X);
            int dy = Math.Abs(currentPosition.Y - _mouseDownPosition.Y);
            if (dx >= DragSelectionThreshold || dy >= DragSelectionThreshold)
            {
                _dragDetected = true;
            }
        }
        else if (_leftMouseDown)
        {
            _leftMouseDown = false;
            if (_dragDetected)
            {
                _selectionCandidateUntil = DateTimeOffset.UtcNow + SelectionCandidateWindow;
                _selectionCaptureAttempted = false;
            }

            _dragDetected = false;
        }

        return isLeftDown;
    }

    private async Task<string?> TryGetRecentSelectedTextAsync(CancellationToken cancellationToken)
    {
        if (!TryConsumeSelectionCandidate())
        {
            return null;
        }

        return await _selectedTextCaptureService.TryCaptureSelectedTextAsync(cancellationToken);
    }

    private bool TryConsumeSelectionCandidate()
    {
        if (_selectionCaptureAttempted || DateTimeOffset.UtcNow > _selectionCandidateUntil)
        {
            return false;
        }

        _selectionCaptureAttempted = true;
        _selectionCandidateUntil = DateTimeOffset.MinValue;
        return true;
    }

    private async Task TranslateSelectedTextAndShowAsync(Point cursorPosition)
    {
        CancelCurrentCapture();
        using CancellationTokenSource captureCts = new();
        _currentCapture = captureCts;
        _isCapturing = true;

        try
        {
            string? selectedText = await _selectedTextCaptureService.TryCaptureSelectedTextAsync(captureCts.Token);
            if (captureCts.IsCancellationRequested)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                _bubbleWindow.ShowMessage(
                    cursorPosition,
                    "未获取到划选文本",
                    "已跳过截图 OCR。请确认目标窗口支持 Ctrl+C 复制划选内容。");
                return;
            }

            await TranslateAndShowAsync(cursorPosition, selectedText, "来自划选文本", captureCts);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _bubbleWindow.ShowMessage(cursorPosition, "取词失败", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_currentCapture, captureCts))
            {
                _currentCapture = null;
            }

            _isCapturing = false;
        }
    }

    private async Task TranslateAndShowAsync(
        Point cursorPosition,
        string sourceText,
        string? sourceStatus,
        CancellationTokenSource captureCts)
    {
        TranslationResult translation = await _translationService.TranslateAsync(sourceText, captureCts.Token);
        if (captureCts.IsCancellationRequested)
        {
            return;
        }

        _bubbleWindow.ShowResult(
            cursorPosition,
            sourceText,
            translation.TranslatedText,
            CombineStatus(sourceStatus, translation.StatusMessage));
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
