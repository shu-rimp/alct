# 기여 가이드

ALCT에 관심 가져주셔서 감사합니다!🙌 버그 제보, 기능 제안, 코드 기여 모두 환영합니다.

## 버그 제보 · 기능 제안

[이슈](../../issues/new/choose)를 열어주세요. 버그/기능 템플릿이 준비되어 있어요.

- 버그는 **재현 방법**과 **환경(Windows 버전, ALCT 버전 등)** 을 함께 적어주시면 큰 도움이 됩니다.
- 로그(`%APPDATA%/ALCT/alct.log`)나 스크린샷을 첨부해주시면 더 빠르게 확인할 수 있어요.

> **게임 용어집**은 서버 저장소에서 관리합니다. 용어 추가·수정 제안은 [alct-server](https://github.com/shu-rimp/alct-server?tab=contributing-ov-file#게임-용어집-기여)를 이용해주세요.

## 코드 기여 (Pull Request)

1. 저장소를 **fork** 후 로컬에 클론합니다.
2. `dev` 브랜치에서 작업 브랜치를 만듭니다. (예: `feat/voice-overlay`, `fix/hotkey-conflict`)
3. 변경 후 빌드와 테스트가 통과하는지 확인합니다.
   ```bash
   dotnet build
   dotnet test Tests/AlctClient.Tests.csproj
   ```
   > 앱이 실행 중이면 빌드가 파일 잠금 오류로 실패해요. 앱을 종료한 뒤 빌드하세요.
4. `dev` 브랜치로 **Pull Request** 를 보냅니다. 무엇을, 왜 바꿨는지 간단히 적어주세요.

## 직접 빌드해서 실행하기

> **⚠️ 직접 클론해 빌드한 클라이언트는 공식 OCR 서버를 사용할 수 없어요.** 실제 OCR 결과를 확인하려면 **본인의 OCR 서버**가 필요합니다. (입력·음성 번역은 서버를 거치지 않으므로 무관합니다.)

본인 서버를 지정하는 가장 간단한 방법은 `%APPDATA%/ALCT/appsettings.json`에 `ServerUrl`, `ServerToken`을 추가하는 것입니다. 빌드 기본값으로 두려면 `src/Core/BuildConstants.cs`를 수정해도 되지만, `appsettings.json` 값이 우선되고 이 값을 공개 저장소에 올리지 않도록 주의하세요.

```json
{
  "ServerUrl": "https://your-server.example.com",
  "ServerToken": ""
}
```

> 서버를 호스팅하려면 [서버 저장소](https://github.com/shu-rimp/alct-server)를 참고하세요.

## 코드 스타일

**기존 코드의 스타일을 따라주시면** 충분합니다.

- **네이밍**: 메서드·클래스는 `PascalCase`, 변수·매개변수는 `camelCase`, private 필드는 `_camelCase`, 상수는 `UPPER_SNAKE_CASE`, 비동기 메서드는 `Async`접미사(`TranslateToKoreanAsync`)
- **언어**: 로그는 영어, 사용자에게 보이는 UI 문구는 한국어(해요체). 주석은 한·영 자유롭게.

### 이것만 주의해주세요

아래는 어기면 동작이나 정책이 깨질 수 있어 꼭 지켜주세요.

- **합성 입력 금지** — `SendInput`·자동 붙여넣기를 도입하지 마세요(안티치트 제약). 입력 번역은 클립보드 읽기/쓰기만 사용합니다. 이 외에도 게임 프로세스에 관여할 수 있는 모든 수정을 금지합니다.
- 번역 엔진은 `TranslationEngineFactory.Create()`로 만들고, 번역 상태는 `TranslationCoordinator`를 거쳐 변경하세요(용어집이 자동 적용돼요).
- **`CaptionMonitorService.cs`는 신중하게 수정해 주세요.**                
폴링 타이밍에 민감하고 정교한 상태 로직이 얽혀 있어, 작은 수정도 과거 버그를 회귀시키거나 전체 타이밍을 어그러뜨리기 쉽습니다. 수정할 경우에는, 먼저 설계 문서([`docs/caption_monitor_logic.md`](docs/caption_monitor_logic.md))를 읽고 아래를 지켜주세요.
  1. **회귀 테스트를 함께** 추가·갱신하세요 (`Tests/CaptionMonitorServiceTests.cs`).
  2. 파일 안의 `[DEBUG]` 주석 처리된 `Logger.Info`(진단 로그)를 켜고, **실제 발화로** 동작을 검증하세요. 
  3. 발화 케이스를 **전부** 확인하세요: 빠른 발화 / 느린 발화 / 단발성(한 마디 후 멈춤) / 연속성(쉼 없이 길게).
  > 디버그 로그를 활성화하면 로깅하는 만큼 폴링을 느리게 만들어 버그가 가려질 수 있습니다. 빠른/연속 발화는 영상 2배속 등으로 부하를 높여 확인해 주세요.

## 라이선스

기여하신 내용은 프로젝트와 동일하게 [Apache License 2.0](LICENSE)으로 배포됩니다.
