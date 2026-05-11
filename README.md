# SnapTranslate（拾译）

SnapTranslate（拾译）是一个 Windows 屏幕取词与翻译工具 demo。`csharp` 分支验证基于截图 ROI、OCR 和翻译引擎的新实现。

## 当前状态

- C# / .NET 10 WPF 系统托盘应用，启动后常驻托盘。
- 鼠标悬停后截取鼠标附近 ROI。
- 如果检测到刚发生过鼠标拖拽选择或双击选词，会直接读取选中文本，不再局限于鼠标附近 ROI。
- 悬停 OCR 取词可配置为需要按住 Ctrl、Alt 或 Shift 才触发。
- 优先通过 PaddleOCR runner 识别中英混排屏幕文字。
- 默认通过 NuGet Tesseract native wrapper 做 OCR，VS 调试无需单独安装 Tesseract。
- 可选回退到外部或随发布包携带的 `tesseract.exe`。
- 从 OCR 结果中选择离鼠标最近的一行文字。
- 翻译前会清理 OCR/选中文本中的多余空白、控制字符和常见断行噪声。
- 优先支持微软 Azure AI Translator，保留 LibreTranslate 兼容接口作为备用。
- 未配置翻译引擎时，会显示 OCR 原文和配置提示。
- 托盘菜单提供“设置”窗口，配置会保存到当前用户目录。

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
- PaddleOCR Python runner，可选但推荐用于中英混排
- OCR：英文模型通过 NuGet 包随构建输出；中文模型可放入输出目录 `tessdata`
- 可选 portable Tesseract CLI：发布包可内置，开发环境也可通过 PATH 或环境变量指定
- Azure AI Translator 或 LibreTranslate 兼容翻译服务，可选

构建：

```powershell
dotnet build SnapTranslate.slnx
```

运行：

```powershell
dotnet run --project src/SnapTranslate/SnapTranslate.csproj
```

常规配置：

1. 启动应用。
2. 右键托盘图标。
3. 打开“设置”。
4. 在“翻译”页选择或输入源语言、目标语言，填写微软翻译 Key、Region。
5. 在“OCR”页配置 PaddleOCR Python 或 runner。
6. 保存后立即生效。

配置文件位置：

```text
%APPDATA%\SnapTranslate\settings.json
```

环境变量仍可用于开发调试，并会覆盖配置文件：

```powershell
$env:SNAPTRANSLATE_TESSERACT_PATH = "C:\Program Files\Tesseract-OCR\tesseract.exe"
$env:SNAPTRANSLATE_TESSDATA_DIR = "C:\Program Files\Tesseract-OCR\tessdata"
$env:SNAPTRANSLATE_OCR_LANG = "eng+chi_sim"
$env:SNAPTRANSLATE_PYTHON = "D:\path\to\.venv-paddle\Scripts\python.exe"
$env:SNAPTRANSLATE_PADDLEOCR_LANG = "ch"
$env:SNAPTRANSLATE_MICROSOFT_TRANSLATOR_KEY = "<your-azure-translator-key>"
$env:SNAPTRANSLATE_MICROSOFT_TRANSLATOR_REGION = "<your-resource-region>"
$env:SNAPTRANSLATE_MICROSOFT_TRANSLATOR_TO = "zh-Hans"
$env:SNAPTRANSLATE_LIBRETRANSLATE_URL = "http://localhost:5000"
$env:SNAPTRANSLATE_SOURCE_LANG = "auto"
$env:SNAPTRANSLATE_TARGET_LANG = "zh"
$env:SNAPTRANSLATE_HOVER_MODIFIER_KEY = "Ctrl"
```

微软翻译配置说明：

- `SNAPTRANSLATE_MICROSOFT_TRANSLATOR_KEY`：Azure AI Translator key，配置后优先使用微软翻译。
- `SNAPTRANSLATE_MICROSOFT_TRANSLATOR_REGION`：资源区域。区域资源或多服务资源通常需要填写。
- `SNAPTRANSLATE_MICROSOFT_TRANSLATOR_ENDPOINT`：可选，默认 `https://api.cognitive.microsofttranslator.com`。Azure 中国云可改成对应 endpoint。
- `SNAPTRANSLATE_MICROSOFT_TRANSLATOR_TO`：微软目标语言代码，简体中文建议 `zh-Hans`。
- `SNAPTRANSLATE_HOVER_MODIFIER_KEY`：悬停 OCR 取词快捷键，可选 `None`、`Ctrl`、`Alt`、`Shift`。

不要把 key 写进代码或提交到仓库。

配置 PaddleOCR 开发 runner：

```powershell
python -m venv .venv-paddle
.\.venv-paddle\Scripts\python -m pip install --upgrade pip
.\.venv-paddle\Scripts\python -m pip install -r src\SnapTranslate\ocr\paddle\requirements.txt
$env:SNAPTRANSLATE_PYTHON = "$PWD\.venv-paddle\Scripts\python.exe"
```

如果需要中文 OCR，把 `chi_sim.traineddata` 放到调试输出目录：

```text
src/SnapTranslate/bin/Debug/net10.0-windows/tessdata/chi_sim.traineddata
```

发布包中的 Tesseract CLI 回退运行时可放在应用目录下：

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
