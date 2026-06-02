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
client/
├── AlctClient.sln
├── CLAUDE.md / README.md
├── docs/                        # UI 스펙, 디자인 문서
├── Tests/                       # xUnit 테스트
├── src/                         # 소스 루트
│   ├── AlctClient.csproj
│   ├── App.xaml / MainWindow.xaml(.cs)
│   ├── appsettings.json         # gitignored — ServerUrl, DeepLApiKey
│   ├── appsettings.example.json
│   ├── assets/                  # 아이콘, 이미지
│   ├── Core/                    # 비즈니스 로직 + 도메인 모델
│   │   ├── AppState.cs
│   │   ├── CaptionMonitorService.cs
│   │   ├── HotkeyManager.cs
│   │   ├── OcrHttpClient.cs
│   │   ├── ScreenCaptureService.cs
│   │   ├── TranslationService.cs    # ITranslationService + DeepLTranslationService
│   │   ├── UserSettings.cs          # 설정 모델
│   │   └── UserSettingsService.cs   # 설정 저장/로드
│   ├── Themes/
│   │   └── Colors.xaml
│   ├── Utils/                   # OS/플랫폼 헬퍼 + 재사용 UserControl
│   │   ├── HintIcon.xaml
│   │   ├── Logger.cs
│   │   ├── TrayIconManager.cs
│   │   └── WindowsApiHelper.cs
│   └── Views/                   # WPF 창/오버레이/모달
│       ├── Modals/              # 모달 창
│       │   └── ApiConfigModal.xaml
│       ├── Overlays/            # 화면 오버레이
│       │   ├── ChatTranslationOverlay.xaml
│       │   ├── EditPanelOverlay.xaml
│       │   ├── QuickSettingsOverlay.xaml
│       │   └── VoiceTranslationOverlay.xaml
│       └── Windows/             # 일반 WPF 창
│           └── SettingsWindow.xaml
```

## Code Style
- Variables: camelCase / Constants: UPPER_SNAKE_CASE
- One function = one responsibility
- Prefer LINQ over raw loops
