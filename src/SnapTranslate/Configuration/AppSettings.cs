using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnapTranslate.Configuration;

public sealed class AppSettings
{
    public int HoverDelayMs { get; set; } = 700;
    public int CaptureWidth { get; set; } = 360;
    public int CaptureHeight { get; set; } = 140;
    public string HoverModifierKey { get; set; } = "None";

    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "zh";

    public string MicrosoftTranslatorEndpoint { get; set; } = "https://api.cognitive.microsofttranslator.com";
    public string MicrosoftTranslatorKey { get; set; } = string.Empty;
    public string MicrosoftTranslatorRegion { get; set; } = string.Empty;
    public string MicrosoftTranslatorTo { get; set; } = "zh-Hans";

    public string LibreTranslateUrl { get; set; } = string.Empty;
    public string LibreTranslateApiKey { get; set; } = string.Empty;

    public string PaddleOcrRunnerPath { get; set; } = string.Empty;
    public string PythonPath { get; set; } = "python";
    public string PaddleOcrLanguage { get; set; } = "ch";

    public string OcrLanguage { get; set; } = "eng+chi_sim";
    public string TesseractPath { get; set; } = string.Empty;
    public string TessDataDirectory { get; set; } = string.Empty;

    [JsonIgnore]
    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapTranslate");

    [JsonIgnore]
    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    public AppOptions ToOptions()
    {
        return new AppOptions(this);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
