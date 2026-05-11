using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using SnapTranslate.Configuration;
using WpfMessageBox = System.Windows.MessageBox;

namespace SnapTranslate.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadSettings();
    }

    public event EventHandler<AppSettings>? SettingsSaved;

    private void LoadSettings()
    {
        LoadCaptureSettings(_settings);

        SourceLanguageBox.Text = _settings.SourceLanguage;
        TargetLanguageBox.Text = _settings.TargetLanguage;

        MicrosoftEndpointBox.Text = _settings.MicrosoftTranslatorEndpoint;
        MicrosoftKeyBox.Password = _settings.MicrosoftTranslatorKey;
        MicrosoftRegionBox.Text = _settings.MicrosoftTranslatorRegion;
        MicrosoftToBox.Text = _settings.MicrosoftTranslatorTo;

        LibreUrlBox.Text = _settings.LibreTranslateUrl;
        LibreKeyBox.Password = _settings.LibreTranslateApiKey;

        PythonPathBox.Text = _settings.PythonPath;
        PaddleRunnerBox.Text = _settings.PaddleOcrRunnerPath;
        PaddleLanguageBox.Text = _settings.PaddleOcrLanguage;

        OcrLanguageBox.Text = _settings.OcrLanguage;
        TesseractPathBox.Text = _settings.TesseractPath;
        TessDataBox.Text = _settings.TessDataDirectory;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParsePositiveInt(HoverDelayBox.Text, "悬停延迟", out int hoverDelay) ||
            !TryParsePositiveInt(CaptureWidthBox.Text, "截图宽度", out int captureWidth) ||
            !TryParsePositiveInt(CaptureHeightBox.Text, "截图高度", out int captureHeight))
        {
            return;
        }

        _settings.HoverDelayMs = hoverDelay;
        _settings.CaptureWidth = captureWidth;
        _settings.CaptureHeight = captureHeight;

        _settings.SourceLanguage = NormalizeText(SourceLanguageBox.Text, "auto");
        _settings.TargetLanguage = NormalizeText(TargetLanguageBox.Text, "zh");

        _settings.MicrosoftTranslatorEndpoint = NormalizeText(MicrosoftEndpointBox.Text, "https://api.cognitive.microsofttranslator.com");
        _settings.MicrosoftTranslatorKey = MicrosoftKeyBox.Password.Trim();
        _settings.MicrosoftTranslatorRegion = MicrosoftRegionBox.Text.Trim();
        _settings.MicrosoftTranslatorTo = NormalizeText(MicrosoftToBox.Text, "zh-Hans");

        _settings.LibreTranslateUrl = LibreUrlBox.Text.Trim();
        _settings.LibreTranslateApiKey = LibreKeyBox.Password.Trim();

        _settings.PythonPath = NormalizeText(PythonPathBox.Text, "python");
        _settings.PaddleOcrRunnerPath = PaddleRunnerBox.Text.Trim();
        _settings.PaddleOcrLanguage = NormalizeText(PaddleLanguageBox.Text, "ch");

        _settings.OcrLanguage = NormalizeText(OcrLanguageBox.Text, "eng+chi_sim");
        _settings.TesseractPath = TesseractPathBox.Text.Trim();
        _settings.TessDataDirectory = TessDataBox.Text.Trim();

        try
        {
            _settings.Save();
            SettingsSaved?.Invoke(this, _settings);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, ex.Message, "保存设置失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetCaptureSettings_Click(object sender, RoutedEventArgs e)
    {
        LoadCaptureSettings(new AppSettings());
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OpenConfigDirectory_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.SettingsDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = AppSettings.SettingsDirectory,
            UseShellExecute = true
        });
    }

    private bool TryParsePositiveInt(string value, string fieldName, out int parsed)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) &&
            parsed > 0)
        {
            return true;
        }

        WpfMessageBox.Show(this, $"{fieldName} 必须是大于 0 的整数。", "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void LoadCaptureSettings(AppSettings settings)
    {
        HoverDelayBox.Text = settings.HoverDelayMs.ToString(CultureInfo.InvariantCulture);
        CaptureWidthBox.Text = settings.CaptureWidth.ToString(CultureInfo.InvariantCulture);
        CaptureHeightBox.Text = settings.CaptureHeight.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeText(string value, string fallback)
    {
        string trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }
}
