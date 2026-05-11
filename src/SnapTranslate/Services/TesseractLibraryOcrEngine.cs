using System;
using System.Collections.Generic;
using System.Drawing;
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
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(CreateCanceledResult());
        }

        return Task.Run(() => Recognize(capture, cancellationToken));
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
            if (cancellationToken.IsCancellationRequested)
            {
                return CreateCanceledResult();
            }

            capture.Bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);

            using TesseractEngine engine = new(tessDataDirectory, language, EngineMode.Default);
            using Pix image = Pix.LoadFromFile(imagePath);
            using Page page = engine.Process(image, PageSegMode.SingleBlock);

            if (cancellationToken.IsCancellationRequested)
            {
                return CreateCanceledResult();
            }

            IReadOnlyList<OcrTextLine> lines = ExtractLines(page, capture.Origin);
            if (lines.Count == 0)
            {
                return new OcrResult(Array.Empty<OcrTextLine>());
            }

            string? status = language == _options.OcrLanguage
                ? null
                : $"当前 NuGet OCR 语言：{language}。如需中文，请把 chi_sim.traineddata 放入输出目录 tessdata。";

            return new OcrResult(lines, status);
        }
        catch (OperationCanceledException)
        {
            return CreateCanceledResult();
        }
        catch (Exception ex)
        {
            return new OcrResult(Array.Empty<OcrTextLine>(), $"NuGet OCR 初始化失败：{ex.Message}");
        }
        finally
        {
            TryDelete(imagePath);
        }
    }

    private static IReadOnlyList<OcrTextLine> ExtractLines(Page page, Point origin)
    {
        using ResultIterator iterator = page.GetIterator();
        iterator.Begin();

        List<OcrWord> words = new();
        do
        {
            string? text = iterator.GetText(PageIteratorLevel.Word)?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (!iterator.TryGetBoundingBox(PageIteratorLevel.Word, out Rect bounds))
            {
                continue;
            }

            double confidence = iterator.GetConfidence(PageIteratorLevel.Word);
            if (confidence < 15)
            {
                continue;
            }

            Rectangle screenBounds = new(
                origin.X + bounds.X1,
                origin.Y + bounds.Y1,
                Math.Max(1, bounds.Width),
                Math.Max(1, bounds.Height));

            words.Add(new OcrWord(text, screenBounds, confidence));
        }
        while (iterator.Next(PageIteratorLevel.Word));

        return BuildLines(words);
    }

    private static IReadOnlyList<OcrTextLine> BuildLines(IReadOnlyList<OcrWord> words)
    {
        if (words.Count == 0)
        {
            return Array.Empty<OcrTextLine>();
        }

        List<List<OcrWord>> lines = new();
        foreach (OcrWord word in words.OrderBy(word => word.Bounds.Top).ThenBy(word => word.Bounds.Left))
        {
            List<OcrWord>? line = lines.FirstOrDefault(existing =>
                Math.Abs(GetCenterY(existing[0].Bounds) - GetCenterY(word.Bounds)) <=
                Math.Max(8, Math.Min(existing[0].Bounds.Height, word.Bounds.Height) / 2));

            if (line is null)
            {
                lines.Add(new List<OcrWord> { word });
            }
            else
            {
                line.Add(word);
            }
        }

        return lines
            .Select(line =>
            {
                OcrWord[] orderedWords = line.OrderBy(word => word.Bounds.Left).ToArray();
                string text = string.Join(" ", orderedWords.Select(word => word.Text));
                Rectangle bounds = orderedWords.Select(word => word.Bounds).Aggregate(Rectangle.Union);
                double confidence = orderedWords.Average(word => word.Confidence);
                return new OcrTextLine(text, bounds, confidence);
            })
            .Where(line => line.Text.Length >= 2)
            .ToArray();
    }

    private static int GetCenterY(Rectangle rectangle) => rectangle.Top + rectangle.Height / 2;

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

    private static OcrResult CreateCanceledResult()
    {
        return new OcrResult(Array.Empty<OcrTextLine>());
    }

    private sealed record OcrWord(string Text, Rectangle Bounds, double Confidence);
}
