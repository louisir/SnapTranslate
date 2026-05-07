# PaddleOCR Runner

This directory contains the development runner used by the C# app.

Create a Python environment with a PaddleOCR-supported Python version, then install:

```powershell
python -m venv .venv-paddle
.\.venv-paddle\Scripts\python -m pip install --upgrade pip
.\.venv-paddle\Scripts\python -m pip install -r src\SnapTranslate\ocr\paddle\requirements.txt
```

Run SnapTranslate with this Python:

```powershell
$env:SNAPTRANSLATE_PYTHON = "$PWD\.venv-paddle\Scripts\python.exe"
$env:SNAPTRANSLATE_PADDLEOCR_LANG = "ch"
dotnet run --project src\SnapTranslate\SnapTranslate.csproj
```

The first PaddleOCR run may download models. Later releases should replace this Python script with a packaged `paddleocr-runner.exe` or a C++/ONNX runner.
