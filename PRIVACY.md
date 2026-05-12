# Privacy Notes

Version 1.0, 2026-05-07

SnapTranslate（拾译）通过选中文本、Windows UI Automation、鼠标附近 ROI 截图、OCR 和翻译引擎交互来实现取词翻译。

当前 C# 实现会读取鼠标位置，并优先通过 Windows UI Automation 查询鼠标坐标命中的控件或文本范围。UI Automation 无法取词时，才会截取鼠标附近的小块屏幕截图并进行 OCR。如果检测到刚发生过鼠标拖拽选择或双击选词，也会优先读取选中文本。

如果安装 SnapTranslate 浏览器扩展，扩展会在网页中读取 `window.getSelection()`，并把选中文本和鼠标屏幕坐标发送到本机 `127.0.0.1:49387`。扩展不会把选中文本直接发送到互联网。

划选文本优先模式会临时模拟 `Ctrl+C` 读取选区，并尝试恢复原剪贴板内容。某些应用或剪贴板格式可能无法完全恢复。

未配置翻译引擎时，当前 C# 实现不会主动把 OCR 文字发送到网络。如果设置 `SNAPTRANSLATE_MICROSOFT_TRANSLATOR_KEY`，SnapTranslate 会把 OCR 识别出的文字发送到 Azure AI Translator 获取译文。如果设置 `SNAPTRANSLATE_LIBRETRANSLATE_URL` 且未配置微软翻译 key，SnapTranslate 会把 OCR 文字发送到该 LibreTranslate 兼容服务。

当前 C# 实现不会持久化保存取词文本。OCR 临时截图写入系统临时目录，并在 OCR 完成后尽力删除。若发布包内置 Tesseract OCR，截图只会交给本地 OCR 进程处理。

应用设置保存到当前用户目录：

```text
%APPDATA%\SnapTranslate\settings.json
```

该文件可能包含翻译 API key。请不要把它提交到代码仓库或分享给他人。
