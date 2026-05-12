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
    private static readonly TimeSpan ExternalBubbleProtection = TimeSpan.FromSeconds(2);

    private readonly AppOptions _options;
    private readonly SelectedTextCaptureService _selectedTextCaptureService;
    private readonly IPointTextCaptureEngine _pointTextCaptureEngine;
    private readonly ITranslationService _translationService;
    private readonly BubbleWindow _bubbleWindow;
    private readonly DispatcherTimer _timer;
    private readonly GlobalMouseHook _mouseHook;
    private Point _lastPosition;
    private DateTimeOffset _lastMovementAt = DateTimeOffset.UtcNow;
    private CancellationTokenSource? _currentCapture;
    private bool _mouseHookStarted;
    private bool _hoverTriggered;
    private bool _isCapturing;
    private bool _leftMouseDown;
    private bool _dragDetected;
    private bool _selectionCaptureAttempted;
    private DateTimeOffset _bubbleProtectedUntil = DateTimeOffset.MinValue;
    private Point _mouseDownPosition;
    private Point _lastClickReleasePosition;
    private DateTimeOffset _lastClickReleasedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _selectionCandidateUntil = DateTimeOffset.MinValue;

    public HoverCaptureController(
        AppOptions options,
        SelectedTextCaptureService selectedTextCaptureService,
        IPointTextCaptureEngine pointTextCaptureEngine,
        ITranslationService translationService,
        BubbleWindow bubbleWindow)
    {
        _options = options;
        _selectedTextCaptureService = selectedTextCaptureService;
        _pointTextCaptureEngine = pointTextCaptureEngine;
        _translationService = translationService;
        _bubbleWindow = bubbleWindow;
        _lastPosition = Forms.Cursor.Position;
        _timer = new DispatcherTimer { Interval = _options.PollInterval };
        _timer.Tick += OnTimerTick;
        _mouseHook = new GlobalMouseHook();
        _mouseHook.MouseAction += OnGlobalMouseAction;
    }

    public bool IsEnabled { get; set; } = true;

    public async Task TranslateExternalTextAsync(string text, Point cursorPosition, Rectangle? selectionBounds, string sourceStatus)
    {
        if (!IsEnabled)
        {
            return;
        }

        string normalizedText = TextSanitizer.NormalizeForTranslation(text);
        if (!TextSanitizer.IsUsefulForTranslation(normalizedText))
        {
            return;
        }

        CancelCurrentCapture();
        using CancellationTokenSource captureCts = new();
        _currentCapture = captureCts;
        _isCapturing = true;
        _leftMouseDown = false;
        _selectionCaptureAttempted = true;
        _selectionCandidateUntil = DateTimeOffset.MinValue;
        _bubbleProtectedUntil = DateTimeOffset.UtcNow + ExternalBubbleProtection;
        _lastPosition = cursorPosition;
        _lastMovementAt = DateTimeOffset.UtcNow;
        _hoverTriggered = true;

        try
        {
            await TranslateAndShowAsync(cursorPosition, selectionBounds, normalizedText, sourceStatus, captureCts);
            _bubbleProtectedUntil = DateTimeOffset.UtcNow + ExternalBubbleProtection;
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

    public void Start()
    {
        _timer.Start();
        try
        {
            _mouseHook.Start();
            _mouseHookStarted = true;
        }
        catch
        {
            _mouseHookStarted = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _mouseHook.MouseAction -= OnGlobalMouseAction;
        _mouseHook.Dispose();
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
        if (!_mouseHookStarted && UpdateSelectionTracking(currentPosition))
        {
            _bubbleWindow.Hide();
            CancelCurrentCapture();
            return;
        }

        if (_leftMouseDown)
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
            if (DateTimeOffset.UtcNow > _bubbleProtectedUntil)
            {
                _bubbleWindow.Hide();
                CancelCurrentCapture();
            }

            return;
        }

        if (!IsHoverModifierPressed())
        {
            _hoverTriggered = false;
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

            PointTextCaptureResult captureResult = await _pointTextCaptureEngine.CaptureAsync(cursorPosition, captureCts.Token);
            if (captureCts.IsCancellationRequested)
            {
                return;
            }

            if (!captureResult.HasText)
            {
                _bubbleWindow.ShowMessage(cursorPosition, "未识别到文字", captureResult.StatusMessage);
                return;
            }

            await TranslateAndShowAsync(cursorPosition, captureResult.Text!, captureResult.StatusMessage, captureCts);
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

    private void OnGlobalMouseAction(object? sender, GlobalMouseEventArgs e)
    {
        if (!_timer.Dispatcher.CheckAccess())
        {
            _timer.Dispatcher.BeginInvoke(() => HandleGlobalMouseAction(e));
            return;
        }

        HandleGlobalMouseAction(e);
    }

    private void HandleGlobalMouseAction(GlobalMouseEventArgs e)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (e.Action == GlobalMouseAction.LeftButtonDown)
        {
            BeginSelectionTracking(e.Position);
            _bubbleWindow.Hide();
            CancelCurrentCapture();
            return;
        }

        if (e.Action == GlobalMouseAction.LeftButtonUp)
        {
            CompleteSelectionTracking(e.Position, triggerImmediately: true);
        }
    }

    private bool UpdateSelectionTracking(Point currentPosition)
    {
        bool isLeftDown = (Forms.Control.MouseButtons & Forms.MouseButtons.Left) == Forms.MouseButtons.Left;
        if (isLeftDown && !_leftMouseDown)
        {
            BeginSelectionTracking(currentPosition);
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
            CompleteSelectionTracking(currentPosition, triggerImmediately: false);
        }

        return isLeftDown;
    }

    private void BeginSelectionTracking(Point currentPosition)
    {
        _leftMouseDown = true;
        _dragDetected = false;
        _selectionCaptureAttempted = false;
        _selectionCandidateUntil = DateTimeOffset.MinValue;
        _mouseDownPosition = currentPosition;
    }

    private void CompleteSelectionTracking(Point currentPosition, bool triggerImmediately)
    {
        if (!_leftMouseDown)
        {
            return;
        }

        _leftMouseDown = false;
        _dragDetected = _dragDetected || IsDragRelease(currentPosition);
        if (_dragDetected || IsDoubleClickRelease(currentPosition))
        {
            MarkSelectionCandidate();
            if (triggerImmediately)
            {
                _ = TranslateSelectionCandidateAsync(currentPosition);
            }
        }

        _lastClickReleasedAt = DateTimeOffset.UtcNow;
        _lastClickReleasePosition = currentPosition;
        _dragDetected = false;
    }

    private void MarkSelectionCandidate()
    {
        _selectionCandidateUntil = DateTimeOffset.UtcNow + SelectionCandidateWindow;
        _selectionCaptureAttempted = false;
    }

    private bool IsDragRelease(Point currentPosition)
    {
        int dx = Math.Abs(currentPosition.X - _mouseDownPosition.X);
        int dy = Math.Abs(currentPosition.Y - _mouseDownPosition.Y);
        return dx >= DragSelectionThreshold || dy >= DragSelectionThreshold;
    }

    private bool IsDoubleClickRelease(Point currentPosition)
    {
        if (DateTimeOffset.UtcNow - _lastClickReleasedAt > TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime))
        {
            return false;
        }

        Size doubleClickSize = Forms.SystemInformation.DoubleClickSize;
        return Math.Abs(currentPosition.X - _lastClickReleasePosition.X) <= Math.Max(1, doubleClickSize.Width / 2) &&
               Math.Abs(currentPosition.Y - _lastClickReleasePosition.Y) <= Math.Max(1, doubleClickSize.Height / 2);
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

    private async Task TranslateSelectionCandidateAsync(Point cursorPosition)
    {
        if (_isCapturing || !TryConsumeSelectionCandidate())
        {
            return;
        }

        _lastPosition = cursorPosition;
        _lastMovementAt = DateTimeOffset.UtcNow;
        _hoverTriggered = true;
        await TranslateSelectedTextAndShowAsync(cursorPosition);
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
        await TranslateAndShowAsync(cursorPosition, null, sourceText, sourceStatus, captureCts);
    }

    private async Task TranslateAndShowAsync(
        Point cursorPosition,
        Rectangle? selectionBounds,
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
            selectionBounds,
            sourceText,
            translation.TranslatedText,
            CombineStatus(sourceStatus, translation.StatusMessage));
    }

    private bool IsHoverModifierPressed()
    {
        return _options.HoverModifierKey switch
        {
            HoverModifierKey.None => true,
            HoverModifierKey.Ctrl => (Forms.Control.ModifierKeys & Forms.Keys.Control) == Forms.Keys.Control,
            HoverModifierKey.Alt => (Forms.Control.ModifierKeys & Forms.Keys.Alt) == Forms.Keys.Alt,
            HoverModifierKey.Shift => (Forms.Control.ModifierKeys & Forms.Keys.Shift) == Forms.Keys.Shift,
            _ => true
        };
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
