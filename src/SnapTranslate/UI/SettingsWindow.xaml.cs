using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using SnapTranslate.Configuration;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SnapTranslate.UI;

public partial class SettingsWindow : Window
{
    private static readonly LanguageOption[] CommonLanguageOptions =
    {
        new("zh-Hans", "中文（简体）"),
        new("zh-Hant", "中文（繁体）"),
        new("en", "英语"),
        new("ja", "日语"),
        new("ko", "韩语"),
        new("fr", "法语"),
        new("de", "德语"),
        new("es", "西班牙语"),
        new("ru", "俄语"),
        new("pt", "葡萄牙语"),
        new("it", "意大利语"),
        new("ar", "阿拉伯语"),
        new("vi", "越南语"),
        new("th", "泰语"),
        new("id", "印尼语"),
        new("ms", "马来语"),
        new("hi", "印地语"),
        new("tr", "土耳其语"),
        new("pl", "波兰语"),
        new("nl", "荷兰语")
    };

    private static readonly LanguageOption[] SourceLanguageOptions =
        new[] { new LanguageOption("auto", "自动检测") }.Concat(CommonLanguageOptions).ToArray();

    private static readonly LanguageOption[] TargetLanguageOptions = CommonLanguageOptions;

    private static readonly LanguageOption[] AllLanguageOptions =
        SourceLanguageOptions
            .Concat(TargetLanguageOptions)
            .GroupBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private readonly AppSettings _settings;
    private bool _isRefreshingLanguageFilter;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        InitializeLanguageCombo(SourceLanguageBox, SourceLanguageOptions);
        InitializeLanguageCombo(TargetLanguageBox, TargetLanguageOptions);
        LoadSettings();
    }

    public event EventHandler<AppSettings>? SettingsSaved;

    private void LoadSettings()
    {
        LoadCaptureSettings(_settings);

        SetLanguageCombo(SourceLanguageBox, _settings.SourceLanguage, SourceLanguageOptions);
        SetLanguageCombo(TargetLanguageBox, GetConfiguredTargetLanguage(_settings), TargetLanguageOptions);

        MicrosoftEndpointBox.Text = _settings.MicrosoftTranslatorEndpoint;
        MicrosoftKeyBox.Password = _settings.MicrosoftTranslatorKey;
        MicrosoftRegionBox.Text = _settings.MicrosoftTranslatorRegion;

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

        string sourceLanguage = GetLanguageCode(SourceLanguageBox, "auto");
        string targetLanguage = GetLanguageCode(TargetLanguageBox, "zh-Hans");
        _settings.SourceLanguage = sourceLanguage;
        _settings.TargetLanguage = NormalizeLibreTranslateTargetLanguage(targetLanguage);
        _settings.MicrosoftTranslatorTo = NormalizeMicrosoftTargetLanguage(targetLanguage);
        _settings.HoverModifierKey = NormalizeHoverModifierKey(HoverModifierBox.SelectedValue?.ToString());

        _settings.MicrosoftTranslatorEndpoint = NormalizeText(MicrosoftEndpointBox.Text, "https://api.cognitive.microsofttranslator.com");
        _settings.MicrosoftTranslatorKey = MicrosoftKeyBox.Password.Trim();
        _settings.MicrosoftTranslatorRegion = MicrosoftRegionBox.Text.Trim();

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
        HoverModifierBox.SelectedValue = NormalizeHoverModifierKey(settings.HoverModifierKey);
        CaptureWidthBox.Text = settings.CaptureWidth.ToString(CultureInfo.InvariantCulture);
        CaptureHeightBox.Text = settings.CaptureHeight.ToString(CultureInfo.InvariantCulture);
    }

    private void InitializeLanguageCombo(WpfComboBox comboBox, IEnumerable<LanguageOption> options)
    {
        comboBox.ItemsSource = options.ToArray();
        comboBox.Loaded += (_, _) =>
        {
            if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is WpfTextBox textBox)
            {
                textBox.TextChanged += (_, _) => RefreshLanguageFilter(comboBox);
            }
        };
    }

    private void RefreshLanguageFilter(WpfComboBox comboBox)
    {
        if (_isRefreshingLanguageFilter)
        {
            return;
        }

        _isRefreshingLanguageFilter = true;
        try
        {
            string filterText = comboBox.Text.Trim();
            ICollectionView view = CollectionViewSource.GetDefaultView(comboBox.ItemsSource);
            view.Filter = item => item is LanguageOption option && option.Matches(filterText);
            view.Refresh();

            if (comboBox.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(filterText))
            {
                comboBox.IsDropDownOpen = comboBox.HasItems;
            }
        }
        finally
        {
            _isRefreshingLanguageFilter = false;
        }
    }

    private static void SetLanguageCombo(WpfComboBox comboBox, string languageCode, IEnumerable<LanguageOption> options)
    {
        LanguageOption? option = FindLanguageOption(languageCode, options);
        comboBox.SelectedItem = option;
        comboBox.Text = option?.ToString() ?? languageCode;
    }

    private static string GetLanguageCode(WpfComboBox comboBox, string fallback)
    {
        string text = comboBox.Text.Trim();
        if (comboBox.SelectedItem is LanguageOption option)
        {
            if (text.Equals(option.Code, StringComparison.OrdinalIgnoreCase) ||
                text.Equals(option.Name, StringComparison.OrdinalIgnoreCase) ||
                text.Equals(option.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return option.Code;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        LanguageOption? knownOption = FindLanguageOption(text, AllLanguageOptions);
        if (knownOption is not null)
        {
            return knownOption.Code;
        }

        int openParenthesis = text.LastIndexOf('(');
        int closeParenthesis = text.LastIndexOf(')');
        if (openParenthesis >= 0 && closeParenthesis > openParenthesis)
        {
            string code = text[(openParenthesis + 1)..closeParenthesis].Trim();
            return string.IsNullOrWhiteSpace(code) ? fallback : code;
        }

        return text;
    }

    private static LanguageOption? FindLanguageOption(string value, IEnumerable<LanguageOption> options)
    {
        string trimmed = value.Trim();
        return options.FirstOrDefault(option =>
            option.Code.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
            option.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
            option.ToString().Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetConfiguredTargetLanguage(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.MicrosoftTranslatorTo))
        {
            return settings.MicrosoftTranslatorTo;
        }

        return NormalizeMicrosoftTargetLanguage(settings.TargetLanguage);
    }

    private static string NormalizeMicrosoftTargetLanguage(string language)
    {
        string trimmed = NormalizeText(language, "zh-Hans");
        return trimmed.ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-hans" => "zh-Hans",
            "zh-tw" or "zh-hk" or "zh-hant" => "zh-Hant",
            _ => trimmed
        };
    }

    private static string NormalizeLibreTranslateTargetLanguage(string language)
    {
        string trimmed = NormalizeText(language, "zh");
        return trimmed.ToLowerInvariant() switch
        {
            "zh-hans" or "zh-hant" or "zh-cn" or "zh-tw" or "zh-hk" => "zh",
            _ => trimmed
        };
    }

    private static string NormalizeHoverModifierKey(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out HoverModifierKey modifierKey)
            ? modifierKey.ToString()
            : HoverModifierKey.None.ToString();
    }

    private static string NormalizeText(string value, string fallback)
    {
        string trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private sealed record LanguageOption(string Code, string Name)
    {
        public bool Matches(string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
            {
                return true;
            }

            string[] terms = filterText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return terms.All(term =>
                Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                ToString().Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        public override string ToString()
        {
            return $"{Name} ({Code})";
        }
    }
}
