# ALCT Client

Apex Legends 채팅 번역 오버레이 클라이언트.  
단축키를 누르면 화면의 채팅 영역을 캡처해 번역 서버로 전송하고, 결과를 게임 위에 투명 오버레이로 표시합니다.

## 요구사항

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 실행

```bash
dotnet run
```

앱이 시작되면 작업 표시줄에 아무것도 나타나지 않습니다. 백그라운드에서 단축키를 대기합니다.

| 동작 | 결과 |
|---|---|
| **Ctrl+T** | 채팅 영역 캡처 → 서버 전송 → 번역 결과 오버레이 표시 (5초) |

## 빌드 / 테스트

```bash
dotnet build
dotnet test Tests/AlctClient.Tests.csproj
```

> 앱이 실행 중인 상태에서 `dotnet build`를 실행하면 파일 잠금 오류가 발생합니다. 빌드 전에 앱을 종료하세요.

## 아키텍처

```
[Ctrl+T 단축키]
      ↓
[화면 캡처] → PNG byte[]
      ↓
[WebSocket 전송] → ws://서버주소
      ↓
[번역 결과 수신] ← 서버 응답 (텍스트)
      ↓
[TranslationOverlay 표시] → 5초 후 자동 숨김
```

## 프로젝트 구조

```
alct-client/
├── App.xaml / App.xaml.cs             앱 진입점
├── MainWindow.xaml / .xaml.cs         흐름 연결 (단축키 + WebSocket + 오버레이)
├── Core/
│   ├── AppState.cs                    앱 상태 불변 객체
│   ├── HotkeyManager.cs               전역 단축키 (RegisterHotKey API)
│   ├── ScreenCaptureService.cs        화면 영역 캡처 → PNG byte[]
│   ├── WebSocketClient.cs             WebSocket 클라이언트 (자동 재연결)
│   └── TranslationMockServer.cs       개발용 목서버
├── Overlay/
│   ├── TranslationOverlay.xaml        번역 결과 오버레이 UI
│   └── TranslationOverlay.xaml.cs     클릭통과 + 자동 숨김 로직
├── Utils/
│   └── WindowsApiHelper.cs            Windows API 래퍼 (클릭통과, 항상최상위)
└── Tests/
    ├── HotkeyManagerTests.cs          쿨다운 로직 단위 테스트
    ├── ScreenCaptureServiceTests.cs   캡처 영역 / PNG 인코딩 테스트
    └── AppStateTests.cs               불변 상태 변환 테스트
```

## 개발 시 참고

### 캡처 영역 조정

`Core/ScreenCaptureService.cs`의 상수를 수정합니다.

```csharp
private static readonly Rectangle DEFAULT_CAPTURE_REGION
    = new(x: 0, y: 880, width: 600, height: 200);
```

Ctrl+T를 누르면 `bin/Debug/net8.0-windows/capture_debug.png`가 자동으로 열립니다. 이 파일을 확인하면서 좌표를 조정하세요.

### 목서버

서버 개발이 완료되기 전까지 `TranslationMockServer`가 `localhost:8765`에서 동작합니다.  
수신한 이미지에 상관없이 항상 `"안녕하세요."`를 응답합니다.

### 실제 서버 연동

`MainWindow.xaml.cs`의 URL 상수를 교체합니다.

```csharp
// 변경 전
private const string MOCK_SERVER_URL = "ws://localhost:8765";

// 변경 후
private const string MOCK_SERVER_URL = "ws://실제서버주소/번역";
```

그 다음 `_mockServer` 관련 코드 3줄과 `TranslationMockServer.cs` 파일을 제거합니다.

### 단축키 변경

`MainWindow.xaml.cs`의 상수를 수정합니다.

```csharp
private const uint DEFAULT_HOTKEY_MODIFIERS = (uint)HotkeyModifiers.Ctrl;
private const uint DEFAULT_HOTKEY_VKEY = 0x54; // 'T' → 다른 키코드로 교체
```

가상 키코드 목록: https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes

## 기술 스택

| 항목 | 내용 |
|---|---|
| 언어 | C# / .NET 8 |
| UI | WPF |
| 화면 캡처 | System.Drawing.Common |
| 서버 통신 | System.Net.WebSockets |
| 단축키 | RegisterHotKey (Windows API / P/Invoke) |
| 오버레이 | WS_EX_TRANSPARENT + WS_EX_LAYERED (Windows API) |
| 테스트 | xUnit + Moq |
