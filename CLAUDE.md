# ALCT Client

## Overview
C# WPF 오버레이. 핫키 → 화면 캡처 → HTTP로 서버 OCR 요청 → 클라이언트 번역(DeepL/Gemini/MyMemory) → 결과 표시.

## Architecture
```
[Ctrl+T] → [ScreenCaptureService] → [OcrHttpClient.SendImageAsync]
         → OcrTextReceived(normalizedText) → _textTranslationService → [ChatTranslationOverlay]

[Ctrl+G] → clipboard text → _textTranslationService.TranslateFromKoreanAsync → paste

[CaptionMonitor] → stabilized text → _voiceTranslationService.TranslateToKoreanAsync → [VoiceTranslationOverlay]
```

## Tech Stack
- C# .NET 8, WPF
- Screen Capture: System.Drawing (`Graphics.CopyFromScreen`)
- HTTP: `System.Net.Http.HttpClient` (keep-alive 자동)
- Translation: DeepL / Gemini / MyMemory (팩토리 패턴으로 런타임 선택)
- OS API: Windows P/Invoke (click-through, hotkey, always-on-top)

## Key Notes

### HTTP
- `OcrHttpClient`: `POST {serverUrl}/ocr` — raw PNG bytes body, `{"normalizedText":"..."}` 응답
- 서버 연결 오류는 조용히 무시 (오버레이 미표시)
- `HttpClient`는 static singleton — keep-alive 자동 처리
- `BuildConstants.SERVER_TOKEN`: 빌드 시 실제 토큰으로 치환, `X-ALCT-Token` 헤더로 전송 (placeholder 그대로면 헤더 미전송)

### Translation
- 엔진은 용도별로 분리: `_voiceTranslationService` (라이브캡션), `_textTranslationService` (OCR + 입력창 번역)
- 엔진 선택: `TranslationEngineFactory.Create(engine, apiKey)` — DeepL / Gemini / MyMemory
- appsettings 키: `VoiceTranslationEngine`, `TextTranslationEngine` (구버전 `OcrTranslationEngine` 폴백 유지)
- `ITranslationService`: `TranslateToKoreanAsync`, `TranslateFromKoreanAsync`, `MapLanguageCode` 필수 구현
- `ITranslationService.StripXmlTags()`: 인터페이스 정적 메서드, OCR normalizer의 `<x>Korean</x>` 태그 제거 공통 처리
- 테스트 주입: 각 서비스는 `internal` 생성자로 `HttpClient` 주입 가능 (`public` 생성자는 static singleton 사용)

#### DeepL
- Free key 감지: `apiKey.EndsWith(":fx")` → `api-free.deepl.com`, 아니면 `api.deepl.com`
- OCR/Caption 번역: `tag_handling=xml, ignore_tags=["x"]` (서버 normalizer의 `<x>Korean</x>` 보존)
- 입력 번역: `source_lang=KO`, target은 현재 선택 언어

#### Gemini
- 모델: `gemini-3.1-flash-lite` (`generativelanguage.googleapis.com`)
- 프롬프트 방식: 한국어 지시문으로 번역 요청, `temperature=0.1`

#### MyMemory
- 무료 API, 일일 한도 초과 시 `quotaFinished` 플래그로 감지해 예외 발생

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
│   │   ├── BuildConstants.cs        # SERVER_TOKEN placeholder — 빌드 시 CI가 치환
│   │   ├── CaptionMonitorService.cs
│   │   ├── HotkeyManager.cs
│   │   ├── OcrHttpClient.cs
│   │   ├── ScreenCaptureService.cs
│   │   ├── TranslationService.cs    # 빈 파일 (리팩토링됨 → Translation/ 참고)
│   │   └── Translation/             # 번역 엔진
│   │       ├── ITranslationService.cs       # 인터페이스 + TranslationEngine enum + StripXmlTags
│   │       ├── TranslationEngineFactory.cs  # 팩토리
│   │       ├── DeepLTranslationService.cs
│   │       ├── GeminiTranslationService.cs
│   │       └── MyMemoryTranslationService.cs
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
