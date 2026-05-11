using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using SnapTranslate.Configuration;
using SnapTranslate.Services;
using SnapTranslate.UI;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace SnapTranslate.Runtime;

public sealed class AppController : IDisposable
{
    private readonly WpfApplication _application;
    private readonly BubbleWindow _bubbleWindow;
    private readonly Forms.NotifyIcon _notifyIcon;
    private AppSettings _settings;
    private HoverCaptureController _hoverController;

    public AppController(WpfApplication application)
    {
        _application = application;
        _settings = AppSettings.Load();
        _bubbleWindow = new BubbleWindow();
        _hoverController = CreateHoverController(_settings.ToOptions());
        _notifyIcon = CreateNotifyIcon();
    }

    private HoverCaptureController CreateHoverController(AppOptions options)
    {
        ScreenCaptureService captureService = new(options);
        IOcrEngine ocrEngine = new FallbackOcrEngine(
            new PaddleOcrProcessEngine(options),
            new TesseractLibraryOcrEngine(options),
            new TesseractCliOcrEngine(options));
        ITranslationService translationService = new CachedTranslationService(CreateTranslationService(options));

        return new HoverCaptureController(options, captureService, ocrEngine, translationService, _bubbleWindow);
    }

    public void Start()
    {
        _notifyIcon.Visible = true;
        _hoverController.Start();
    }

    public void Dispose()
    {
        _hoverController.Dispose();
        _bubbleWindow.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private static ITranslationService CreateTranslationService(AppOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.MicrosoftTranslatorKey))
        {
            return new MicrosoftTranslatorService(options);
        }

        return new LibreTranslateService(options);
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        Forms.ContextMenuStrip menu = new();
        Forms.ToolStripMenuItem enabledItem = new("启用悬停 OCR 取词")
        {
            Checked = true,
            CheckOnClick = true
        };
        enabledItem.CheckedChanged += (_, _) =>
        {
            _hoverController.IsEnabled = enabledItem.Checked;
            if (!enabledItem.Checked)
            {
                _bubbleWindow.Hide();
            }
        };

        Forms.ToolStripMenuItem settingsItem = new("设置");
        settingsItem.Click += (_, _) => ShowSettingsWindow();

        Forms.ToolStripMenuItem exitItem = new("退出");
        exitItem.Click += (_, _) => _application.Shutdown();

        menu.Items.Add(enabledItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        return new Forms.NotifyIcon
        {
            Text = "SnapTranslate（拾译）",
            Icon = CreateTrayIcon(),
            ContextMenuStrip = menu
        };
    }

    private void ShowSettingsWindow()
    {
        _application.Dispatcher.Invoke(() =>
        {
            SettingsWindow window = new(_settings);
            window.SettingsSaved += (_, settings) =>
            {
                _settings = settings;
                ReloadRuntime();
                WpfMessageBox.Show(
                    "设置已保存并生效。",
                    "SnapTranslate（拾译）",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            };
            window.ShowDialog();
        });
    }

    private void ReloadRuntime()
    {
        bool wasEnabled = _hoverController.IsEnabled;
        _hoverController.Dispose();
        _bubbleWindow.Hide();
        _hoverController = CreateHoverController(_settings.ToOptions());
        _hoverController.IsEnabled = wasEnabled;
        _hoverController.Start();
    }

    private static Icon CreateTrayIcon()
    {
        using Bitmap bitmap = new(64, 64);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using Brush brush = new SolidBrush(Color.FromArgb(24, 96, 180));
        graphics.FillEllipse(brush, new Rectangle(4, 4, 56, 56));
        using Font font = new("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
        using StringFormat format = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("ST", font, Brushes.White, new RectangleF(0, 0, 64, 64), format);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
