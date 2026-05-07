# OCR Runtime

Place the portable OCR runtime here before publishing a user-facing build:

```text
ocr/
  tesseract/
    tesseract.exe
    tessdata/
      eng.traineddata
      chi_sim.traineddata
```

Files under this directory are copied to the application output directory by the project file.

The Tesseract executable and language data are third-party components. Do not commit large binary runtime files into this repository unless their licenses and redistribution notices have been reviewed.
