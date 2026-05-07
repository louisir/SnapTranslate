# Third-Party Notices

SnapTranslate（拾译）依赖以下第三方或平台组件。它们不由 SnapTranslate 授权，仍按各自许可证或系统条款使用。

## .NET

The C# implementation uses .NET 10, WPF, and Windows Forms tray APIs.

## Tesseract OCR

The C# implementation can call a bundled or external `tesseract.exe` process to OCR the captured ROI image. Tesseract and its language data files are not committed in this repository, but official release packages may include a reviewed portable OCR runtime.

## LibreTranslate-Compatible Service

The C# implementation can send recognized text to a LibreTranslate-compatible HTTP service when `SNAPTRANSLATE_LIBRETRANSLATE_URL` is configured. The service itself is not bundled in this repository.

## Qt

The legacy Qt prototype under `swc` uses Qt modules such as Qt Core, Qt GUI, and Qt Widgets.
