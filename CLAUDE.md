# ALCT Client

## Overview
C# desktop overlay application for Apex Legends chat translation.
Captures screen region on hotkey press and sends image to translation server.
Displays translated result as transparent overlay on top of the game.

## Core Features
1. Hotkey-triggered screen capture → send image to server
2. Display translation result on always-on-top transparent overlay
3. Overlay input box for outgoing chat translation

## Architecture
```
[Hotkey Listener]
      ↓
[Screen Capture - target region only]
      ↓
[Send image via WebSocket]
      ↓
[Receive translation result]
      ↓
[Overlay Display]
```

## Tech Stack
- Language: C# (.NET 8)
- UI: WPF (transparent overlay window)
- Screen Capture: System.Drawing / Windows Graphics Capture API
- Server Communication: WebSocket (System.Net.WebSockets)
- OS API: Windows API via P/Invoke (click-through, always-on-top)
- Hotkey: RegisterHotKey (Windows API)

## Key Implementation Notes

### Overlay Window
- Always-on-top: `Topmost = true`
- Transparent background with click-through via P/Invoke:
  `WS_EX_TRANSPARENT | WS_EX_LAYERED`
- Must not intercept game mouse/keyboard input

### Screen Capture
- Capture fixed region only (Apex Legends chat area: bottom-left of screen)
- Triggered by hotkey only — no polling
- Send raw image bytes (PNG) over WebSocket

### Hotkey
- Register global hotkey via `RegisterHotKey` Windows API
- Default: configurable by user
- Cooldown: 1000ms to prevent spam

### WebSocket
- Persistent connection (reconnect on disconnect)
- On connect: send settings message with current sourceLang
- Send (settings): `{"type":"settings","sourceLang":"JA"|"EN"}` as text message
- Send (image): PNG bytes as binary message
- Receive: `{"translatedText": "...", "cached": bool}` JSON

### Input Box (Feature 2)
- Separate overlay input window
- User types → send text to server for translation → inject result via clipboard + SendKeys

### State Management
- Centralize all state in a single `AppState` record
- Drive UI reactively from state changes

## Project Structure
```
alct-client/
├── App.xaml
├── MainWindow.xaml(.cs)         # hidden 1x1 window, app entry point
├── appsettings.json             # gitignored — ServerUrl 설정
├── appsettings.example.json     # 템플릿
├── Core/
│   ├── HotkeyManager.cs
│   ├── ScreenCaptureService.cs
│   └── WebSocketClient.cs       # SendImageAsync + SendSettingsAsync
├── Overlay/
│   ├── TranslationOverlay.xaml  # 번역 결과 오버레이 (좌하단, 5초 자동 숨김)
│   └── SettingsWindow.xaml      # 언어 선택 패널 (좌상단, 앱 시작 시 표시)
└── Utils/
    └── WindowsApiHelper.cs
```

## Development Environment
- IDE: VS Code (C# Dev Kit extension)
- Target OS: Windows 10/11
- Framework: .NET 8

## Code Style
- Variables: camelCase / Constants: UPPER_SNAKE_CASE
- No raw coding — abstract magic values and repeated logic
- Function and variable names must clearly describe purpose
- One function = one responsibility
- Avoid raw loops; prefer LINQ and method chains

## Testing
- Framework: xUnit + Moq
- Strategy:
  | Target | Method |
  |---|---|
  | Hotkey cooldown logic | Unit test |
  | WebSocket send/receive | Mock server |
  | Capture region logic | Unit test with fixed rect |
  | Overlay / Windows API | Manual verification only |
