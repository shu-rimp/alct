# ALCT (Apex Legends Chat Translator)

## Overview
에이펙스 레전드 인게임 채팅 실시간 번역 오버레이

## Features
- 단축키로 채팅 영역 캡처 → 번역 결과 오버레이 표시
- 입력창 번역 후 게임에 전송

## Architecture
```
[Client] 단축키 → 화면 캡처 → WebSocket으로 PNG 전송
                                        ↓
[Server] RapidOCR → 중복 제거 → LibreTranslate → 번역 결과 반환
                                        ↓
[Client] 오버레이에 번역 결과 표시
```

## Tech Stack
| | |
|---|---|
| Client | C#, WPF, .NET 8 |
| Server | Python, FastAPI, RapidOCR, LibreTranslate |
| 통신 | WebSocket |
| 배포 | Oracle Cloud Free Tier ARM VM |

## Project Structure
```
alct/
├── client/
│   ├── Core/       # AppState, HotkeyManager, ScreenCaptureService, WebSocketClient
│   ├── Overlay/    # TranslationOverlay (always-on-top 투명 오버레이)
│   └── Utils/      # Windows API helpers
└── server/
    ├── api/        # WebSocket handler
    ├── core/       # OCR, 번역, 세션 관리
    └── models/     # ONNX 모델 파일
```

## Status
🚧 In Development
