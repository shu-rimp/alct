# ALCT Client

## Overview
C# WPF 오버레이. 핫키 → 화면 캡처 → PNG 서버 전송 → 번역 결과 표시.

## Architecture
```
[Hotkey] → [ScreenCaptureService] → [WebSocketClient.SendImageAsync] → [TranslationOverlay]
```

## Tech Stack
- C# .NET 8, WPF
- Screen Capture: System.Drawing (`Graphics.CopyFromScreen`)
- WebSocket: `System.Net.WebSockets.ClientWebSocket`
- OS API: Windows P/Invoke (click-through, hotkey, always-on-top)

## Key Notes

### WebSocket
- Settings: `{"type":"settings","sourceLang":"JA"|"ZH"|"EN"}` (text message)
- Image: PNG bytes (binary message)
- Reconnect delay (`ConnectionChanged(false)` + `Task.Delay`)는 catch 블록 **바깥**에 위치 — 정상 종료/에러 모두 딜레이 적용
- `ConnectionChanged`는 백그라운드 스레드에서 발생 → WPF 컨트롤 접근 시 `Dispatcher.Invoke` 필수 (없으면 `InvalidOperationException` → 무한 재연결 루프)

### Overlay
- `Topmost = true` + `WS_EX_TRANSPARENT | WS_EX_LAYERED` (클릭 통과)

### Hotkey
- `RegisterHotKey` Windows API, 1000ms 쿨다운

## Project Structure
```
alct-client/
├── App.xaml / MainWindow.xaml(.cs)
├── appsettings.json             # gitignored — ServerUrl
├── appsettings.example.json
├── Core/
│   ├── HotkeyManager.cs
│   ├── ScreenCaptureService.cs
│   └── WebSocketClient.cs
├── Overlay/
│   ├── TranslationOverlay.xaml
│   └── SettingsWindow.xaml
└── Utils/
    └── WindowsApiHelper.cs
```

## Code Style
- Variables: camelCase / Constants: UPPER_SNAKE_CASE
- One function = one responsibility
- Prefer LINQ over raw loops
