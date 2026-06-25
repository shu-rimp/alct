# ALCT - CaptionMonitorService 로직 (음성 자막 분할 발송)

Windows 실시간 자막(Live Captions)을 읽어, 화자가 말한 내용을 **번역하기 좋은 단위**로 잘라
내보내는 서비스. 핵심 과제는 
- 같은 내용을 두 번 안 보내고(중복 번역 방지)
- 한 번에 누적분을 다 쏟지 않게(폭탄 방지)
- 너무 짧거나(번역의 품질을 저하시킴) 너무 길지(번역이 느리게 됨) 않게, 또는 어색하게 끊어서 의미없는 번역이 되지 않도록 '자연스러운' 대화의 단위로 자르기.

구현: `src/Core/CaptionMonitorService.cs` / 테스트: `Tests/CaptionMonitorServiceTests.cs`

---

## 1. 입력 특성 - Live Captions가 주는 텍스트

Live Captions는 UI Automation의 `CaptionsTextBlock` 요소 하나에 **"지금까지 말한 전체"** 를
문자열로 계속 보여준다. 이걸 `POLL_MS`(50ms)마다 읽는다.

읽을 때마다 텍스트는 이렇게 변한다:

```
poll 1: "오늘 날씨가"
poll 2: "오늘 날씨가 정말"
poll 3: "오늘 날씨가 정말 좋네요"     ← 뒤에 단어가 붙음 (정상적인 성장)
poll 4: "오늘 날씨가, 정말 좋네요"    ← *이미 나온 단어 사이에 쉼표가 끼어듦 (사후 재작성)
   ⋮
poll n: "오늘 날씨가, 정말 좋네요\n 에어컨이 고장나서 새로 샀어요 ⋯ 이번주에는"  ← 매 poll마다 전체 대화를 반환
poll n+1: "이번주에는 영화를 보러" ← 누적 텍스트의 앞부분은 사라질 수 있음(메모리 정리)
```

완성된 문장은 개행(`\n`)으로 구분되고, **맨 끝 줄**이 아직 말하는 중인 **partial**이다:

```
"안녕하세요\n오늘 날씨가 정말 좋네요"
 └ 완성 줄 ┘ └──── 진행 중(partial) ────┘
```

주의할 점:
- 여기서 "문장"은 의미 단위가 아니라 STT가 끊은 **텍스트 덩어리**다 (화자 분리·문장 종결이 부정확).
- partial은 단어가 추가되기도 하고, **이미 확정된 듯한 앞부분이 사후 수정**되기도 한다 (poll 4).
- 마지막 발화는 `\n`이 안 붙어서, 멈춤을 따로 감지해야 한다.

---

## 2. 핵심 아이디어 - "어디까지 보냈는지"를 텍스트로 기억

partial은 계속 자라므로 매번 전체를 보내면 중복이 된다. 따라서,

- 이미 발송한 앞부분을 `_committedText`에 저장한다.
- 새 줄에서 그 앞부분을 뺀 **나머지(remaining)** 만 본다 → `GetRemaining()`.

```
partial      = "오늘 날씨가 정말 좋네요"
_committedText = "오늘 날씨가"            (이미 보냄)
remaining    =           " 정말 좋네요"   (이번에 볼 부분)
```

**char 위치를 offset이 아닌 텍스트로 들고 있는 이유**          
줄이 재작성되면(poll 4) offset은 어긋나 엉뚱한 곳을 자른다. 텍스트로 비교하면 "어디까지 그대로고 어디부터 바뀌었는지"를 알아낼 수 있어서, 잘림과 중복을 막을 수 있다.

---

## 3. 발송하는 3가지 경우

발송 = `CaptionStabilized` 이벤트 발생 = 번역 엔진에 보냄. (`CaptionUpdating`은 미리보기 전용이라 번역 안 됨.)

| 경우 | 트리거 | 메서드 | 동작 |
|---|---|---|---|
| 1차 | 새 `\n` 등장 | `ProcessTextChange` | 그 줄은 확정 → (보낸 앞부분 제외) 나머지 즉시 발송 |
| 2차 | partial 멈춤 | `CheckDebounce` | `DEBOUNCE_MS`(800ms)간 변화 없으면 마지막 발화로 간주하고 발송 |
| 3차 | partial이 자라기만 함 | `CheckStablePrefix` | 안정된 앞부분이 쌓이면 경계에서 잘라 부분 발송 |

3차(부분 커밋)는 긴 연속 발화(`\n` 없이 계속 말함)가 거대한 덩어리로 안 나가도록 중간중간 끊어 보내는 역할이다.

---

## 4. 부분 커밋(`CheckStablePrefix`) 

긴 partial을 안전하게 끊으려면 두 가지를 본다.

**(a) 끊어도 되나? (안정성)** - `_stableCount` / `MAX_PARTIAL_MS`
- partial 뒷부분은 아직 흔들릴 수 있으므로, "앞부분이 그대로 유지된 채 뒤만 늘어난" 횟수(`_stableCount`)가 `PREFIX_STABLE_COUNT`(3)회 이상이면 앞부분은 확정으로 본다.
- STT가 자꾸 사후 수정해서 3회를 못 채워도, `MAX_PARTIAL_MS`(6초)가 지나면 무조건 끊는다. 이 6초 타임아웃이 **한 청크가 무한정 커지지 않게 묶는 안전장치** 역할을 한다.

**(b) 어디서 끊나? (경계)** - `FindLastBoundary`
- 우선순위: 문장 종결부호(`. ? ! 。`) > 절 구분(`, 、 ，`) > 공백.
- 끝 10자는 아직 흔들릴 수 있어 제외하고, 그 앞에서 마지막 경계를 찾는다.
- 약한 경계(쉼표/공백)에서 너무 짧은 조각이 나오면 발사해도 의미 없는 번역이 되므로 보류한다 (`MIN_COMMIT_LENGTH / 2` 미만).
- 길이 기준 `MIN_COMMIT_LENGTH`(100)은 **가중 길이**다: CJK 문자는 정보량이 라틴의 ~2배라 2로 센다(`EffectiveLength`). 즉 CJK ~50자 / 라틴 ~100자.

---

## 5. 과거에 터졌던 함정 (수정 완료, 회귀 테스트로 고정)

### 5-1. 재작성 폭탄 (`RepunctuatedTail_DoesNotRefireWholeLine`)
빠른 발화 시 Live Captions가 **이미 확정된 부분에 쉼표를 끼워넣는** 등 재구두점을 한다 (`...节日嗯` → `...节日，嗯`). 그러면 새 줄이 더 이상 `_committedText`로 시작하지 않는다.

- **예전:** prefix 불일치 시 `_committedText = ""`로 **커밋 전체를 무효화** → 이미 조각조각 발송했던 누적 줄 전체가 remaining으로 되살아나 **한 번에 재발송·재번역**(688자 폭탄).
- **현재:** 공통 접두사(`CommonPrefixLength`)까지만 커밋으로 유지하고 **실제로 달라진 꼬리만** remaining으로 반환. 재발송이 ~30자로 줄어든다. 앞부분 자체가 통째로 바뀐 경우(common=0)엔 이전처럼 전체를 반환하므로 "첫 단어 잘림 방지"(예: 안녕하세요 → 세요)도 유지된다.

### 5-2. 누적(starvation) 폭탄 (`LongUnpunctuatedRun_DoesNotStarve_CommitsAtFirstBoundary`)
한 청크 길이를 좁게 제한하려고 경계 탐색 범위를 `MIN_COMMIT_LENGTH`로 막았더니, 쉼표 없이 길게 말하는 구간(앞 ~58자에 경계 없음)에서 **자를 곳을 못 찾아 영영 커밋 못 하고 쌓이다가**, `\n`이 뜨자 누적분을 한 번에 발사(698자 폭탄)했다.

- **해결:** 경계 탐색 범위를 좁히지 않고 전체 remaining에서 마지막 경계를 찾는다. 청크 크기는 5-(a)의 6초 타임아웃이 자연스럽게 묶으므로 따로 상한을 두지 않는다.

> 이 알고리즘은 타이밍 의존적이다. 진단 로그를 매 poll마다 동기 I/O로 찍으면 로그를 찍는 만큼 폴링이 느려져 버그가 가려진다. 폭탄은 **빠른 연속 발화**에서만 재현되므로, 로그를 찍은 후 버그 재현이 안 될 경우, 영상 2배속 등으로 발화속도를 늘린다.

---

## 6. 메모리 누수 & 재탐색 (`GetLiveCaptionsText`)

- 매 poll마다 `FindFirst(Descendants)`로 트리를 훑으면 UIA peer가 계속 쌓여 메모리가 증가한다(+1.7MB/분). 그래서 `CaptionsTextBlock` 요소를 **한 번만 찾아 캐시**하고, 이후엔 `GetUpdatedCache`로 Name 속성만 새로고침한다(+0.5MB/분).
- 요소가 무효화되면(창 닫힘/재생성, Live Captions 재시작) 캐시를 비우고 다음 poll에 재탐색한다. 이때 무효화 갭 동안 캡션이 쌓였을 수 있으므로, 재탐색 직후엔 누적분을 발사하지 않고 현재 텍스트를 새 기준선으로 삼는다 (`_rebaselineOnNextPoll`).
- Live Captions는 UWP라 `Process.EnableRaisingEvents`/`WaitForExitAsync`로 재시작을 감지할 수 없어, WMI로 프로세스 종료를 감시한다(`MainWindow.Caption.cs`).

---

## 7. 상수/상태 빠른 참고

| 이름 | 의미 |
|---|---|
| `POLL_MS` (50) | 폴링 주기 |
| `DEBOUNCE_MS` (800) | 멈춤 판정 시간 (2차 발송) |
| `MIN_COMMIT_LENGTH` (100) | 부분 커밋 최소 가중 길이 (CJK=2) |
| `PREFIX_STABLE_COUNT` (3) | 앞부분 확정으로 보는 연속 안정 횟수 |
| `MAX_PARTIAL_MS` (6000) | 강제 flush 타임아웃 (청크 크기 상한 역할) |
| `FIRE_DEDUP_MS` (150) | 기계적 즉시 재발사 중복 차단 윈도우 |
| `_lastText` | 직전 poll에서 읽은 전체 텍스트 (변화 감지용) |
| `_firedLineCount` | 지금까지 발송 완료한 완성 줄 수 |
| `_lastPartialLine` | 맨 끝의 진행 중 줄 |
| `_committedText` | 현재 partial 중 이미 발송한 앞부분 (텍스트) |
| `_lastRemaining` | 직전 poll의 uncommitted remaining |
| `_stableCount` | 앞부분이 그대로인 채 뒤만 늘어난 연속 횟수 |
