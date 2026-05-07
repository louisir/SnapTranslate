# Third-Party Notices

SnapTranslate（拾译）依赖以下第三方或平台组件。它们不由 SnapTranslate 授权，仍按各自许可证或系统条款使用。

## .NET

The C# implementation uses .NET 10, WPF, and Windows Forms tray APIs.

## Tesseract OCR

The C# implementation uses the Tesseract NuGet package for native OCR and can fall back to a bundled or external `tesseract.exe` process. English language data is provided by the `Tesseract.Data.English` NuGet package. Additional language data files, such as Simplified Chinese, should be reviewed before being bundled in official releases.

## PaddleOCR

The C# implementation can call an external PaddleOCR runner process for Chinese/English mixed OCR. The development runner under `src/SnapTranslate/ocr/paddle` uses the PaddleOCR Python package. Official release packages should review and include the relevant PaddleOCR, PaddlePaddle, model, and runtime notices before bundling a runner.

## LibreTranslate-Compatible Service

The C# implementation can send recognized text to a LibreTranslate-compatible HTTP service when `SNAPTRANSLATE_LIBRETRANSLATE_URL` is configured. The service itself is not bundled in this repository.

## Qt

The legacy Qt prototype under `swc` uses Qt modules such as Qt Core, Qt GUI, and Qt Widgets.
