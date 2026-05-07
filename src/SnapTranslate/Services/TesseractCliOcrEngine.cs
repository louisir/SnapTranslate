using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SnapTranslate.Configuration;

namespace SnapTranslate.Services;

public sealed class TesseractCliOcrEngine : IOcrEngine
{
    private readonly AppOptions _options;

    public TesseractCliOcrEngine(AppOptions options)
    {
        _options = options;
    }

    public async Task<OcrResult> RecognizeAsync(CaptureRegion capture, CancellationToken cancellationToken)
    {
        string? executable = ResolveExecutable();
        if (executable is null)
        {
            return new OcrResult(Array.Empty<OcrTextLine>(), "未找到 tesseract.exe。请安装 Tesseract，或设置 SNAPTRANSLATE_TESSERACT_PATH。");
        }

        string imagePath = Path.Combine(Path.GetTempPath(), $"snaptranslate-{Guid.NewGuid():N}.png");
        try
        {
            capture.Bitmap.Save(imagePath, ImageFormat.Png);
            ProcessStartInfo startInfo = new()
            {
                FileName = executable,
                Arguments = $"\"{imagePath}\" stdout -l {_options.OcrLanguage} --psm 6 tsv",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 Tesseract OCR 进程。");
            string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                return new OcrResult(Array.Empty<OcrTextLine>(), stderr.Trim());
            }

            return new OcrResult(ParseTsv(stdout, capture.Origin));
        }
        finally
        {
            TryDelete(imagePath);
        }
    }

    private string? ResolveExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_options.TesseractPath) && File.Exists(_options.TesseractPath))
        {
            return _options.TesseractPath;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(directory.Trim(), "tesseract.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyList<OcrTextLine> ParseTsv(string tsv, Point origin)
    {
        List<TsvWord> words = new();
        string[] rows = tsv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string row in rows.Skip(1))
        {
            string[] columns = row.Split('\t');
            if (columns.Length < 12 || columns[0] != "5")
            {
                continue;
            }

            string text = columns[11].Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (!TryParseInt(columns[6], out int left) ||
                !TryParseInt(columns[7], out int top) ||
                !TryParseInt(columns[8], out int width) ||
                !TryParseInt(columns[9], out int height))
            {
                continue;
            }

            double confidence = TryParseDouble(columns[10], out double parsedConfidence) ? parsedConfidence : 0;
            string key = string.Join(':', columns[1], columns[2], columns[3], columns[4]);
            words.Add(new TsvWord(key, text, new Rectangle(origin.X + left, origin.Y + top, width, height), confidence));
        }

        return words
            .GroupBy(word => word.LineKey)
            .Select(group =>
            {
                TsvWord[] lineWords = group.OrderBy(word => word.Bounds.Left).ToArray();
                string text = string.Join(" ", lineWords.Select(word => word.Text));
                Rectangle bounds = lineWords.Select(word => word.Bounds).Aggregate(Rectangle.Union);
                double confidence = lineWords.Average(word => word.Confidence);
                return new OcrTextLine(text, bounds, confidence);
            })
            .ToArray();
    }

    private static bool TryParseInt(string value, out int result) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    private static bool TryParseDouble(string value, out double result) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record TsvWord(string LineKey, string Text, Rectangle Bounds, double Confidence);
}
