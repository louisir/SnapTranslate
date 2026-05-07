# SnapTranslate（拾译）

SnapTranslate（拾译）是一个 Windows 屏幕取词与翻译工具 demo。`csharp` 分支验证基于截图 ROI、OCR 和翻译引擎的新实现。

## 当前状态

- C# / .NET 10 WPF 系统托盘应用，启动后常驻托盘。
- 鼠标悬停后截取鼠标附近 ROI。
- 通过外部 `tesseract.exe` 对 ROI 图片做 OCR。
- 从 OCR 结果中选择离鼠标最近的一行文字。
- 可通过 LibreTranslate 兼容接口调用翻译引擎。
- 未配置翻译引擎时，会显示 OCR 原文和配置提示。

## 授权

SnapTranslate 是免费闭源软件，不是开源软件。

官方发布的二进制版本允许个人、团队或组织内部免费自用。源码、品牌、图标和项目资源保留全部权利，未经作者书面许可，不得复制、修改、分发、二次打包、出售、逆向工程、嵌入其他产品或用于衍生项目。

完整条款见 [LICENSE.md](LICENSE.md)。

## 打赏

SnapTranslate 可以免费使用。打赏是自愿支持，不构成购买授权、售后合同、功能承诺或优先服务。说明见 [DONATE.md](DONATE.md)。

## 隐私

SnapTranslate 会截取鼠标附近的小块屏幕区域并在本地 OCR。配置在线翻译引擎后，识别出的文字会发送给对应翻译服务。详情见 [PRIVACY.md](PRIVACY.md)。

## 开发构建

以下说明仅供作者和获授权开发者使用，不构成源码授权。

环境要求：

- Windows
- .NET SDK 10
- OCR 运行时：发布包推荐内置 portable Tesseract，开发环境也可通过 PATH 或环境变量指定
- LibreTranslate 兼容翻译服务，可选

构建：

```powershell
dotnet build SnapTranslate.slnx
```

运行：

```powershell
dotnet run --project src/SnapTranslate/SnapTranslate.csproj
```

可选配置：

```powershell
$env:SNAPTRANSLATE_TESSERACT_PATH = "C:\Program Files\Tesseract-OCR\tesseract.exe"
$env:SNAPTRANSLATE_TESSDATA_DIR = "C:\Program Files\Tesseract-OCR\tessdata"
$env:SNAPTRANSLATE_OCR_LANG = "eng+chi_sim"
$env:SNAPTRANSLATE_LIBRETRANSLATE_URL = "http://localhost:5000"
$env:SNAPTRANSLATE_SOURCE_LANG = "auto"
$env:SNAPTRANSLATE_TARGET_LANG = "zh"
```

发布包中的 OCR 运行时推荐放在应用目录下：

```text
ocr/
  tesseract/
    tesseract.exe
    tessdata/
      eng.traineddata
      chi_sim.traineddata
```

程序会按以下顺序查找 OCR：

1. 应用目录 `ocr/tesseract/tesseract.exe`
2. 应用目录 `tesseract/tesseract.exe`
3. `SNAPTRANSLATE_TESSERACT_PATH`
4. 系统 PATH 中的 `tesseract.exe`

Qt/C++ 原型仍保留在 [swc](swc) 目录，后续以 C# 实现为主线。
