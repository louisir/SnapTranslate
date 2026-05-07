using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SnapTranslate.Configuration;

namespace SnapTranslate.Services;

public sealed class PaddleOcrProcessEngine : IOcrEngine
{
    private readonly AppOptions _options;

    public PaddleOcrProcessEngine(AppOptions options)
    {
        _options = options;
    }

    public async Task<OcrResult> RecognizeAsync(CaptureRegion capture, CancellationToken cancellationToken)
    {
        PaddleRunner? runner = ResolveRunner();
        if (runner is null)
        {
            return new OcrResult(Array.Empty<OcrTextLine>(), "未找到 PaddleOCR runner。");
        }

        string imagePath = Path.Combine(Path.GetTempPath(), $"snaptranslate-paddle-{Guid.NewGuid():N}.png");
        try
        {
            capture.Bitmap.Save(imagePath, ImageFormat.Png);

            ProcessStartInfo startInfo = runner.CreateStartInfo(imagePath, _options.PaddleOcrLanguage);
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 PaddleOCR runner。");

            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.OcrProcessTimeout);

            string stdout = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            string stderr = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            if (process.ExitCode != 0)
            {
                return new OcrResult(Array.Empty<OcrTextLine>(), CleanStatus(stderr, "PaddleOCR runner 执行失败。"));
            }

            PaddleOcrResponse? response = JsonSerializer.Deserialize<PaddleOcrResponse>(stdout);
            if (response?.Lines is null || response.Lines.Count == 0)
            {
                return new OcrResult(Array.Empty<OcrTextLine>(), response?.Status ?? CleanStatus(stderr, "PaddleOCR 未识别到文字。"));
            }

            List<OcrTextLine> lines = new();
            foreach (PaddleOcrLine line in response.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text) || line.Box.Length < 4)
                {
                    continue;
                }

                Rectangle bounds = new(
                    capture.Origin.X + line.Box[0],
                    capture.Origin.Y + line.Box[1],
                    Math.Max(1, line.Box[2]),
                    Math.Max(1, line.Box[3]));
                lines.Add(new OcrTextLine(line.Text.Trim(), bounds, line.Score * 100.0));
            }

            return new OcrResult(lines, response.Status);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OcrResult(Array.Empty<OcrTextLine>(), "PaddleOCR runner 超时。");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new OcrResult(Array.Empty<OcrTextLine>(), $"PaddleOCR runner 失败：{ex.Message}");
        }
        finally
        {
            TryDelete(imagePath);
        }
    }

    private PaddleRunner? ResolveRunner()
    {
        if (!string.IsNullOrWhiteSpace(_options.PaddleOcrRunnerPath) &&
            File.Exists(_options.PaddleOcrRunnerPath))
        {
            return PaddleRunner.FromPath(_options.PaddleOcrRunnerPath, _options.PythonPath);
        }

        foreach (string candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "ocr", "paddle", "paddleocr-runner.exe"),
                     Path.Combine(AppContext.BaseDirectory, "ocr", "paddle", "runner.py"),
                     Path.Combine(AppContext.BaseDirectory, "paddleocr-runner.exe")
                 })
        {
            if (File.Exists(candidate))
            {
                return PaddleRunner.FromPath(candidate, _options.PythonPath);
            }
        }

        return null;
    }

    private static string CleanStatus(string stderr, string fallback)
    {
        string trimmed = stderr.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

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

    private sealed record PaddleOcrResponse(
        [property: JsonPropertyName("lines")] List<PaddleOcrLine> Lines,
        [property: JsonPropertyName("status")] string? Status);

    private sealed record PaddleOcrLine(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("box")] int[] Box,
        [property: JsonPropertyName("score")] double Score);

    private sealed class PaddleRunner
    {
        private readonly string _fileName;
        private readonly string _argumentPrefix;

        private PaddleRunner(string fileName, string argumentPrefix)
        {
            _fileName = fileName;
            _argumentPrefix = argumentPrefix;
        }

        public static PaddleRunner FromPath(string path, string pythonPath)
        {
            if (Path.GetExtension(path).Equals(".py", StringComparison.OrdinalIgnoreCase))
            {
                return new PaddleRunner(pythonPath, $"\"{path}\"");
            }

            return new PaddleRunner(path, string.Empty);
        }

        public ProcessStartInfo CreateStartInfo(string imagePath, string language)
        {
            string arguments = string.IsNullOrWhiteSpace(_argumentPrefix)
                ? $"--image \"{imagePath}\" --lang {language}"
                : $"{_argumentPrefix} --image \"{imagePath}\" --lang {language}";

            return new ProcessStartInfo
            {
                FileName = _fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }
    }
}
