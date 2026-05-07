using System;
using System.Drawing;
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
    private readonly AppOptions _options;
    private readonly ScreenCaptureService _captureService;
    private readonly IOcrEngine _ocrEngine;
    private readonly ITranslationService _translationService;
    private readonly BubbleWindow _bubbleWindow;
    private readonly DispatcherTimer _timer;
    private Point _lastPosition;
    private DateTimeOffset _lastMovementAt = DateTimeOffset.UtcNow;
    private CancellationTokenSource? _currentCapture;
    private bool _hoverTriggered;
    private bool _isCapturing;

    public HoverCaptureController(AppOptions options, ScreenCaptureService captureService, IOcrEngine ocrEngine, ITranslationService translationService, BubbleWindow bubbleWindow)
    {
        _options = options;
        _captureService = captureService;
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
            using CaptureRegion capture = _captureService.CaptureAround(cursorPosition);
            OcrResult ocrResult = await _ocrEngine.RecognizeAsync(capture, captureCts.Token);
            OcrTextLine? line = ocrResult.FindNearestLine(cursorPosition);
            if (line is null || string.IsNullOrWhiteSpace(line.Text))
            {
                _bubbleWindow.ShowMessage(cursorPosition, "未识别到文字", ocrResult.StatusMessage);
                return;
            }

            TranslationResult translation = await _translationService.TranslateAsync(line.Text, captureCts.Token);
            _bubbleWindow.ShowResult(cursorPosition, line.Text, translation.TranslatedText, translation.StatusMessage);
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
}
