# ALCT Client

## Overview
C# WPF 오버레이. 핫키 → 화면 캡처 → HTTP로 서버 OCR 요청 → 클라이언트 DeepL 번역 → 결과 표시.

## Architecture
```
[Ctrl+T] → [ScreenCaptureService] → [OcrHttpClient.SendImageAsync]
         → OcrTextReceived(normalizedText) → [DeepLTranslationService] → [TranslationOverlay]

[Ctrl+G] → clipboard text → [DeepLTranslationService.TranslateFromKoreanAsync] → paste

[CaptionMonitor] → stabilized text → [DeepLTranslationService.TranslateToKoreanAsync] → [TranslationOverlay]
```

## Tech Stack
- C# .NET 8, WPF
- Screen Capture: System.Drawing (`Graphics.CopyFromScreen`)
- HTTP: `System.Net.Http.HttpClient` (keep-alive 자동)
- Translation: DeepL API (사용자 API 키)
- OS API: Windows P/Invoke (click-through, hotkey, always-on-top)

## Key Notes

### HTTP
- `OcrHttpClient`: `POST {serverUrl}/ocr` — raw PNG bytes body, `{"normalizedText":"..."}` 응답
- 서버 연결 오류는 조용히 무시 (오버레이 미표시)
- `HttpClient`는 static singleton — keep-alive 자동 처리

### Translation (DeepL)
- Free key 감지: `apiKey.EndsWith(":fx")` → `api-free.deepl.com`, 아니면 `api.deepl.com`
- OCR/Caption 번역: `tag_handling=xml, ignore_tags=["x"]` (서버 normalizer의 `<x>Korean</x>` 보존)
- 입력 번역: `source_lang=KO`, target은 현재 선택 언어

### Overlay
- `Topmost = true` + `WS_EX_TRANSPARENT | WS_EX_LAYERED` (클릭 통과)

### Hotkey
- `RegisterHotKey` Windows API, 1000ms 쿨다운

## Project Structure
```
alct-client/
├── App.xaml / MainWindow.xaml(.cs)
├── appsettings.json             # gitignored — ServerUrl, DeepLApiKey
├── appsettings.example.json
├── Core/
│   ├── HotkeyManager.cs
│   ├── ScreenCaptureService.cs
│   ├── OcrHttpClient.cs
│   └── TranslationService.cs   # ITranslationService + DeepLTranslationService
├── Overlay/
│   ├── TranslationOverlay.xaml
│   └── SettingsWindow.xaml     # DeepL API 키 입력 포함
└── Utils/
    └── WindowsApiHelper.cs
```

## Code Style
- Variables: camelCase / Constants: UPPER_SNAKE_CASE
- One function = one responsibility
- Prefer LINQ over raw loops
