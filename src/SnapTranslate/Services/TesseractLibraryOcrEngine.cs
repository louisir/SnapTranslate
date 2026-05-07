using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SnapTranslate.Configuration;
using Tesseract;

namespace SnapTranslate.Services;

public sealed class TesseractLibraryOcrEngine : IOcrEngine
{
    private readonly AppOptions _options;

    public TesseractLibraryOcrEngine(AppOptions options)
    {
        _options = options;
    }

    public Task<OcrResult> RecognizeAsync(CaptureRegion capture, CancellationToken cancellationToken)
    {
        return Task.Run(() => Recognize(capture, cancellationToken), cancellationToken);
    }

    private OcrResult Recognize(CaptureRegion capture, CancellationToken cancellationToken)
    {
        string? tessDataDirectory = ResolveTessDataDirectory();
        if (tessDataDirectory is null)
        {
            return new OcrResult(Array.Empty<OcrTextLine>(), "未找到 NuGet OCR 语言数据。");
        }

        string language = ResolveLanguage(tessDataDirectory);
        if (string.IsNullOrWhiteSpace(language))
        {
            return new OcrResult(Array.Empty<OcrTextLine>(), $"OCR 语言数据不足：{tessDataDirectory}");
        }

        string imagePath = Path.Combine(Path.GetTempPath(), $"snaptranslate-{Guid.NewGuid():N}.png");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            capture.Bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);

            using TesseractEngine engine = new(tessDataDirectory, language, EngineMode.Default);
            using Pix image = Pix.LoadFromFile(imagePath);
            using Page page = engine.Process(image, PageSegMode.Auto);

            cancellationToken.ThrowIfCancellationRequested();
            string text = page.GetText()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return new OcrResult(Array.Empty<OcrTextLine>());
            }

            Rectangle bounds = new(capture.Origin, capture.Bitmap.Size);
            double confidence = page.GetMeanConfidence() * 100.0;
            string? status = language == _options.OcrLanguage
                ? null
                : $"当前 NuGet OCR 语言：{language}。如需中文，请把 chi_sim.traineddata 放入输出目录 tessdata。";

            return new OcrResult(new[] { new OcrTextLine(text, bounds, confidence) }, status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new OcrResult(Array.Empty<OcrTextLine>(), $"NuGet OCR 初始化失败：{ex.Message}");
        }
        finally
        {
            TryDelete(imagePath);
        }
    }

    private string? ResolveTessDataDirectory()
    {
        foreach (string? candidate in new[]
                 {
                     _options.TessDataDirectory,
                     Path.Combine(AppContext.BaseDirectory, "tessdata"),
                     Path.Combine(AppContext.BaseDirectory, "ocr", "tesseract", "tessdata"),
                     Path.Combine(AppContext.BaseDirectory, "ocr", "tessdata")
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string ResolveLanguage(string tessDataDirectory)
    {
        string[] requestedLanguages = _options.OcrLanguage
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string[] availableLanguages = requestedLanguages
            .Where(language => File.Exists(Path.Combine(tessDataDirectory, $"{language}.traineddata")))
            .ToArray();

        if (availableLanguages.Length > 0)
        {
            return string.Join('+', availableLanguages);
        }

        string? firstLanguage = Directory
            .EnumerateFiles(tessDataDirectory, "*.traineddata")
            .Select(Path.GetFileNameWithoutExtension)
            .FirstOrDefault();

        return firstLanguage ?? string.Empty;
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
}
