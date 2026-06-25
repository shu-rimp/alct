# Third-Party Notices

ALCT includes or makes use of the following third-party software/models.

## PaddleOCR / PP-OCRv5 models (RapidOCR distribution)

The PP-OCRv5 mobile ONNX models (det/cls/rec) and dictionary (dict) bundled under
`src/assets/models/v5/` are derived from PaddleOCR (PP-OCRv5) and were obtained from the RapidOCR distribution.

- PaddleOCR: https://github.com/PaddlePaddle/PaddleOCR
- RapidOCR: https://github.com/RapidAI/RapidOCR
- License: Apache License 2.0

## RapidOcrNet

OCR inference uses the NuGet package `RapidOcrNet` (BobLd).

- Project: https://github.com/BobLd/RapidOcrNet
- License: Apache License 2.0 (based on RapidOCR)

---

## Windows built-in OCR (Snipping Tool / oneocr)

As the **default engine** for chat OCR, ALCT **loads at runtime** the
`oneocr.dll`/`oneocr.onemodel` from the Windows Snipping Tool installed on the user's PC
(this is the "built-in OCR (Snipping Tool)" engine referred to in the README).
These files are proprietary to Microsoft, and ALCT does **not bundle or redistribute** them. Because it is an
unofficial, undocumented interface, its behavior may change with OS/Snipping Tool updates; if it is not installed
or fails to initialize, ALCT automatically falls back to the fallback engine (`RapidOcrNet` · PP-OCRv5).

## SnippingToolOcrSharp (OneOCR interop code)

The oneocr.dll P/Invoke signatures, structs, and call flow in `src/Core/Ocr/OneOcrEngine.cs`, which invoke the engine above, were referenced from SnippingToolOcrSharp.

- Project: https://github.com/ksasao/SnippingToolOcrSharp
- License: Apache License 2.0
