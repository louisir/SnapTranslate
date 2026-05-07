# Privacy Notes

Version 1.0, 2026-05-07

SnapTranslate（拾译）通过鼠标附近 ROI 截图、OCR 和翻译引擎交互来实现取词翻译。

当前 C# 实现会读取鼠标位置、鼠标附近的小块屏幕截图、OCR 识别出的文本和文本框位置。

当前 C# 实现不模拟 `Ctrl+C`，不会主动改写剪贴板。

未配置翻译引擎时，当前 C# 实现不会主动把 OCR 文字发送到网络。如果设置 `SNAPTRANSLATE_LIBRETRANSLATE_URL`，SnapTranslate 会把 OCR 识别出的文字发送到该 LibreTranslate 兼容服务获取译文。

当前 C# 实现不会持久化保存取词文本。OCR 临时截图写入系统临时目录，并在 OCR 完成后尽力删除。若发布包内置 Tesseract OCR，截图只会交给本地 OCR 进程处理。
