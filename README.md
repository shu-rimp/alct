<h1 align="center">ALCT</h1>

<p align="center">
  <img src="src/assets/alct.png" alt="ALCT" width="120" />
</p>
<p align="center">
  ALCT는 온라인 게임에 특화된 실시간 번역 오버레이입니다.<br/> 현재는 'Apex Legends' 플레이 환경에 최적화 되어있습니다.<br/>
  실시간으로 변하는 게임 상황에서 팀원의 음성과 채팅, 그리고 내 채팅을 쉽고 빠르게 번역합니다.
</p>

<p align="center">
  <b>한국어</b> | <a href="README.en.md">English</a> 
</p>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6" alt="platform" />
  <img src="https://img.shields.io/badge/.NET-8-512BD4" alt="dotnet" />
  <img src="https://img.shields.io/badge/license-Apache_2.0-green" alt="license" />
</p>

> **한국인 게이머들을 위해 만들었습니다.** 외국어를 **한국어로** 번역하며, 앱 UI도 한국어예요. (다른 언어 지원은 검토 중)

---

## 요약

ALCT는 화면에 보이는 텍스트(또는 음성)을 읽어, 게임 위에 투명 오버레이로 한국어 번역을 띄웁니다. **게임 프로세스에 절대 개입하지 않으며**, 디스코드·OBS 등의 단순 유틸 오버레이와 같은 방식으로 동작합니다.

**1. 가볍습니다** — 1프레임 단위에도 민감한 게이머들을 위해 리소스가 많이 드는 작업을 외부(번역: 서비스 API / OCR: 자체 OCR전용 서버)에서 처리하도록 설계하여 게임 성능에 부담을 거의 주지 않습니다.

<details>
<summary>리소스 사용량 측정 그래프 (펼치기)</summary>
측정 환경: AMD Ryzen 7 9800X3D 8-Core Processor (8C/16T) · RAM 32GB · NVIDIA GeForce RTX 4070 Ti · Windows 11 Pro 25H2
<br/>

**전 기능 사용 시(ALCT+Live Captions)** — 60분 지속 발화 음성 번역 + 채팅 번역 + 입력 번역

1시간 측정 기준 CPU 평균 약 1%, GPU 약 0%, 메모리 약 600 MB 수준

<p align="center">
  <img src="src/assets/usage-chart.png" alt="ALCT 리소스 사용량 측정 그래프 (전 기능 사용)" width="560" />
</p>

> 위 수치는 모든 기능을 활성화한 채로 테스트한 결과로, 일반적인 사용 환경에서는 이보다 적게 측정됩니다.<br/>
> 그래프 중간의 메모리 급락은 .NET 가비지 컬렉터가 누적된 미사용 메모리를 한 번에 회수·반환하는 정상 동작으로, 장시간 사용해도 메모리 누수 없이 일정 범위에서 유지됨을 보여줍니다.

**음성 번역 OFF 시(ALCT 단독)** — CPU 평균 약 0.4%, 메모리 약 200 MB 수준으로 더 낮아집니다.

<p align="center">
  <img src="src/assets/usage-chart-2.png" alt="ALCT 리소스 사용량 측정 그래프 (음성 번역 OFF)" width="560" />
</p>

<br/>
(해당 데이터의 CPU/GPU 사용량 단위는 퍼센트(%)이므로, 사용자의 PC 사양에 따라 달라질 수 있습니다.)
</details><br/>

**2. 쉽습니다** — 모든 기능들이 단축키, 또는 자동으로 제공됩니다. 최대한 게임 환경에만 몰입할 수 있도록 번거로운 작업과 UI를 최소화하고, 편의성을 위한 다양한 맞춤 설정을 제공합니다. 설치 시 기능 소개 및 설정을 돕는 온보딩을 통해 자연스럽게 사용방법을 익힐 수 있습니다.

<details>
<summary>예시 화면 (펼치기)</summary>

<br/>

유저 플로우 기반의 온보딩, 번역 언어·엔진, 단축키, 오버레이 편집 등 다양한 설정을 직관적인 UI로 제공합니다.

<table>
<tr>
<td align="center"><img src="src/assets/settings-1.png" alt="번역 설정" width="330" /></td>
<td align="center"><img src="src/assets/settings-2.png" alt="화면 설정" width="330" /></td>
</tr>
</table>

</details><br/>

**3. 정확합니다** — 게임 특화 용어집 전처리를 통해 자주 쓰이는 용어들은 미리 번역합니다. 모든 번역 엔진에 동일하게 적용됩니다. 용어집은 별도의 업데이트 없이 서버를 통해 최신 용어집으로 갱신됩니다. Gemini 또는 DeepL 번역 엔진 선택 시 은어 또는 로마자 일본어 표기(예: yorosiku) 같은 까다로운 번역도 준수한 품질로 제공합니다.

<details>
<summary>번역 엔진별 예시 (펼치기)</summary>

<br/>

기본 엔진(MyMemory)과 Gemini의 번역 예시입니다. 로마자 일본어(`ima no yaba sugiru www`)나 은어처럼 까다로운 입력일수록 차이가 큽니다.

<p align="center">
  <img src="src/assets/translation-quality-compare.png" alt="번역 엔진별 품질 비교 (MyMemory vs Gemini)" width="560" />
</p>

</details><br/>


**지원 언어**: 일본어 · 중국어(간체) · 영어


## 기능

세 가지 기능을 제공합니다.

### 🎙️ 음성 번역

**Windows 11 라이브 캡션(Live Captions)** 이 변환한 텍스트를 읽어, 한국어로 번역해 오버레이 자막으로 표시합니다. 오디오 스트림을 직접 녹음하거나 가로채지 않고, 오직 Live Captions만 참조합니다. 해당 옵션을 활성화 하면 음성이 감지될 때 자동으로 한국어 자막을 생성합니다.

> 라이브 캡션은 **Windows 11 22H2 이상**에서만 제공되므로, 음성 번역도 해당 버전 이상에서만 사용할 수 있습니다.

<video src="https://github.com/shu-rimp/alct/raw/main/src/assets/voice-translation-demo.mp4" controls width="640"></video>

### 💬 채팅 번역 `기본: Ctrl+T`

단축키를 누르면 채팅 영역을 캡처 → OCR 서버로 전송(텍스트 추출) → 번역 → 결과를 오버레이로 표시합니다.

<video src="https://github.com/shu-rimp/alct/raw/main/src/assets/chat-translation-demo.mp4" controls width="640"></video>

### ⌨️ 입력 번역 `기본: Ctrl+G`

입력한 채팅을 복사 → 단축키를 누르면 ALCT가 대상 언어로 번역해 클립보드에 넣어줍니다. `Ctrl+V`로 붙여넣어 사용합니다. 

> 번역은 단축키를 누를 때만 동작하므로, 평소의 복사·붙여넣기에는 영향을 주지 않습니다.

<video src="https://github.com/shu-rimp/alct/raw/main/src/assets/input-translation-demo.mp4" controls width="640"></video>

---


## 설치

[릴리스 페이지](https://github.com/shu-rimp/alct/releases/latest)에서 최신 **`ALCT.exe`** 를 다운로드해 실행합니다. (self-contained로 별도 설치 프로그램이나 .NET 설치가 필요 없습니다.) 최초 실행 시 기능 소개 및 설정을 돕는 온보딩이 진행됩니다.

> **⚠️ SmartScreen 경고.** ALCT는 개인이 개발한 서명되지 않은 오픈소스 실행 파일이므로, Windows가 *"Windows의 PC 보호"* 경고를 띄울 수 있습니다. 서명되지 않은 앱에서 정상적으로 나타나는 현상입니다. **추가 정보 → 실행** 을 누르면 실행됩니다.


---

## 작동 방식

```
음성 번역:   Windows 라이브 캡션 → UI Automation 폴링 → 번역 API → 자막 오버레이
채팅 번역:   단축키 → 화면 캡처(PNG) → OCR 릴레이 서버 → 번역 API → 오버레이
입력 번역:   클립보드(한국어) → 번역 API → 클립보드(번역문) → 사용자가 붙여넣기
```

- **OCR 릴레이 서버** 는 오픈소스 RapidOCR로 텍스트만 추출하고, 이미지는 즉시 폐기합니다. 현재 서버 사양이 낮아 사용자가 늘면 채팅 번역이 느려질 수 있습니다. (음성·입력 번역은 서버를 거치지 않아 무관하게 사용할 수 있습니다.) 
  > 셀프 호스팅을 원하면 [서버 저장소](https://github.com/shu-rimp/alct-server)를 참고하세요.
- **번역** 은 **사용자 본인의 API 키** 를 사용해 서버를 거치지 않고, 클라이언트가 직접 번역 서비스 API로 전송합니다: MyMemory(기본, 키 불필요), DeepL, Gemini.
- 데이터 처리·개인정보에 관한 전체 내용은 [개인정보 처리방침](PRIVACY.md)을 참고하세요.

---

## 기여해 주세요!💫

버그 제보, 기능 제안, 코드 기여 모두 환영해요 — [기여 가이드](CONTRIBUTING.md)를 참고해주세요. **게임 용어집**은 서버 저장소 [alct-server](https://github.com/shu-rimp/alct-server)에서 관리합니다.

---

## 기술 스택

| 항목 | 내용 | 버전 |
|---|---|---|
| 언어 | C# | 12 |
| 런타임 | .NET (net8.0-windows) | 8 |
| 배포 | self-contained · win-x64 · single-file (런타임 번들) | — |
| UI | WPF + WPF-UI | 3.1.1 |
| 화면 캡처 | System.Drawing.Common | 8.0.0 |
| API 키 암호화 | System.Security.Cryptography.ProtectedData (DPAPI) | 8.0.0 |
| 시스템 정보 | System.Management | 8.0.0 |
| 전역 단축키 | RegisterHotKey (Win32 / P/Invoke) | — |
| 오버레이 | WS_EX_TRANSPARENT + WS_EX_LAYERED (Win32) | — |
| 테스트 | xUnit / Moq / Microsoft.NET.Test.Sdk | 2.9.3 / 4.20.72 / 18.6.0 |

---

## 라이선스

[Apache 2.0](LICENSE) © 2026 shu-rimp

---

## 이용약관 및 면책 조항

ALCT는 비영리 오픈소스 개인 프로젝트이며, 어떠한 보증도 없이 **"있는 그대로(as-is)"** 제공됩니다. 핵심을 요약하면 다음과 같으며, 설치·사용 시 아래 전체 내용에 동의한 것으로 간주합니다.

- **안티치트·게임 약관** — ALCT는 게임에 전혀 개입하지 않고 외부에서 화면만 읽습니다(메모리 접근·인젝션·후킹·합성 입력 및 플레이에 이득을 주는 행위를 하지 않습니다). 다만 이것이 특정 게임의 제재 없음을 보장하지는 않으며, 본 프로그램을 사용하며 일어날 수 있는 **계정 제재 등 모든 책임은 사용자 본인에게 있습니다.**
- **무저장·BYOK** — 사용자 데이터를 저장하지 않습니다. 번역은 사용자 본인의 API 키로 클라이언트가 직접 처리하고, OCR 서버는 응답 후 이미지 및 추출한 텍스트를 즉시 폐기합니다.
- **민감한 내용** — 기본 엔진(MyMemory)은 전송된 문장을 번역 메모리에 보관합니다. 민감한 내용은 데이터 미사용을 보장하는 유료 키 사용을 권장합니다.

<details>
<summary><b>전체 이용약관 및 면책 조항 펼치기</b></summary>
<br>

### 1. 데이터 처리 및 개인정보

데이터 처리·개인정보에 관한 전체 내용은 별도 문서로 분리했습니다 → **[개인정보 처리방침 (PRIVACY.md)](PRIVACY.md)**

### 2. 안티치트 및 게임 이용약관

ALCT는 게임 클라이언트에 절대 개입하지 않고, 외부에서 화면을 읽어 번역 결과만 표시하도록 설계되었습니다. 구체적으로 본 프로그램은 다음을 **수행하지 않습니다.**

- 게임 프로세스에 대한 메모리 읽기/쓰기
- 게임 프로세스에 대한 DLL·코드 인젝션
- 게임 렌더링(DirectX 등)에 대한 후킹
- 키보드·마우스 입력의 가상(합성) 주입 — 모든 선택·복사·붙여넣기는 사용자의 실제 키 입력으로만 이루어집니다.
- 저수준 키보드 후킹을 통한 입력 감시
- 게임 플레이에 이득을 줄 수 있는 행위

번역 결과를 표시하는 오버레이는 게임 프로세스에 주입되지 않는 **독립된 최상위 창**으로, Discord·OBS 등의 단순 유틸 오버레이와 동일한 방식으로 동작합니다.

다만 위 설계가 특정 게임의 안티치트 시스템이나 이용약관에서 제재받지 않음을 보장하지는 않습니다. 안티치트의 탐지 정책은 비공개이며 게임마다 다르고 수시로 변경될 수 있습니다. 또한 일부 게임은 기술적 안전성과 무관하게 **제3자 오버레이·화면 캡처 소프트웨어의 사용 자체를 약관으로 제한**할 수 있습니다. 본 프로그램 사용으로 인한 계정 제재 등 어떠한 불이익에 대해서도 개발자는 책임지지 않으며, 사용 여부 및 해당 게임 이용약관의 준수는 전적으로 사용자 본인의 판단과 책임에 따릅니다.

### 3. 일반 면책

본 소프트웨어는 명시적이든 묵시적이든 어떠한 보증도 없이 "있는 그대로(as-is)" 제공됩니다. 본 프로그램의 사용 또는 사용 불능으로 인해 발생하는 직접적·간접적 손해에 대하여 개발자는 어떠한 책임도 지지 않습니다.

### 4. 상표 및 저작권

ALCT는 비공식 제3자 도구이며, Electronic Arts Inc. 및 Respawn Entertainment와 제휴·후원 관계가 없고 이들로부터 보증받지 않았습니다. 'Apex Legends'를 비롯한 모든 관련 상표와 README, 온보딩 시연 영상에 사용된 게임 내 영상·이미지의 권리는 각 권리자에게 있습니다.

</details>
