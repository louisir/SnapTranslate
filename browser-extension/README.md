# SnapTranslate Browser Bridge

这是 SnapTranslate 的浏览器划词桥接扩展，用于 Chrome / Edge。

扩展会在网页中读取 `window.getSelection()`，把选中文本和鼠标屏幕坐标发送到本机 SnapTranslate：

```text
http://127.0.0.1:49387/selection
```

## 安装

1. 启动 SnapTranslate 桌面应用。
2. 打开 Chrome / Edge 的扩展管理页。
3. 开启“开发者模式”。
4. 选择“加载已解压的扩展”。
5. 选择本目录 `browser-extension`。

安装后，在普通网页里鼠标划选文本或双击选词，扩展会直接把 DOM 选中文本发给本机应用，不再依赖 OCR 或模拟 `Ctrl+C`。

## 说明

- 扩展只把划选文本发送到 `127.0.0.1` 本机端口。
- 翻译请求仍由 SnapTranslate 桌面应用按你的翻译引擎配置发出。
- 浏览器扩展无法在 Chrome Web Store、浏览器内置页面、扩展商店、PDF 内置查看器等受限制页面运行。
