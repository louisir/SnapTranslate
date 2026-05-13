using System;
using System.IO;

namespace SnapTranslate.Services;

internal static class SelectionCaptureDiagnostics
{
    private const long MaxLogBytes = 512 * 1024;
    private static readonly object SyncRoot = new();
    private static bool _initialized;

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SnapTranslate",
        "selection-diagnostics.log");

    public static void Write(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                string? directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!_initialized)
                {
                    File.WriteAllText(
                        LogPath,
                        $"=== SnapTranslate selection diagnostics {DateTimeOffset.Now:O} ==={Environment.NewLine}");
                    _initialized = true;
                }
                else if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                {
                    File.WriteAllText(LogPath, string.Empty);
                }

                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    public static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "<null>";
        }

        string compact = value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
        return compact.Length <= 120 ? compact : compact[..120] + "...";
    }
}
