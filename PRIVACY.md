# 개인정보 처리방침 · Privacy Policy

이 문서는 ALCT가 데이터를 어떻게 처리하는지 설명합니다. 안티치트 고지·일반 면책·상표 등 이용약관은 [README의 이용약관 및 면책 조항](README.md#이용약관-및-면책-조항)을 참고하세요.

This document explains how ALCT handles your data. For the anti-cheat notice, general disclaimer, and trademark terms, see the [Terms of Use & Disclaimer in the README](README.en.md#terms-of-use--disclaimer).

---

## 한국어

ALCT는 비영리 오픈소스 개인 프로젝트이며, 다음과 같이 데이터를 처리합니다.

### 1. 무저장 원칙

ALCT는 사용자의 음성·텍스트 데이터를 수집·축적하거나 저장하지 않습니다. 단, 기능 동작을 위해 다음과 같은 외부 전송이 발생합니다.

- **화면 캡처(채팅 번역)**: 캡처 이미지는 텍스트 추출(OCR)을 위해 개발자의 릴레이 서버로 HTTPS 암호화하여 전송되며, 서버는 오픈소스 OCR 엔진([RapidOCR](https://github.com/RapidAI/RapidOCR))을 실행합니다. 응답 후 추출한 텍스트와 원본 이미지를 저장하지 않고 폐기합니다. RapidOCR은 추론 전용 OCR 라이브러리로, 서버 내부에서 로컬로 실행되어 입력 이미지를 외부로 전송하지 않고 모델 학습에도 쓰이지 않습니다. [서버 코드](https://github.com/shu-rimp/alct-server) 또한 오픈소스로 공개되어 있어 이러한 무저장 동작을 누구나 직접 확인할 수 있습니다.
- **번역 요청**: 추출되거나 입력된 텍스트는 번역을 위해 제3자 번역 서비스(DeepL, Gemini, MyMemory 등)로 전송됩니다.
- **입력창 번역**: 사용자가 직접 번역을 요청한 텍스트(클립보드)만 처리합니다.
- **기타 서버 통신(데이터 미전송)**: OCR 처리 외에 클라이언트가 개발자의 서버와 통신하는 경우는 ① 최신 용어집 다운로드, ② 자동 업데이트 트리거를 위한 최신 버전 정보 확인 두 가지입니다. 두 동작 모두 서버로부터 내려받기만 하며, 사용자 데이터를 전송하지 않습니다.

### 2. 번역 API 키 (BYOK, Bring Your Own Key)

번역 요청은 개발자 서버를 거치지 않고 클라이언트에서 각 번역 서비스로 직접 전송됩니다. DeepL·Gemini처럼 키가 필요한 엔진은 **사용자 본인이 발급·등록한 API 키**를 사용하며(기본 엔진 MyMemory는 키 없이 동작), 등록한 API 키는 사용자의 로컬 PC에 윈도우 표준 암호화 방식(DPAPI)으로 저장됩니다. 따라서 번역 데이터에 대한 처리 책임과 약관 관계는 사용자와 각 번역 서비스 제공자 사이에 성립하며, 개발자는 해당 데이터 경로에 관여하지 않습니다.

### 3. 번역 서비스의 데이터 사용

전송된 텍스트는 각 서비스의 개인정보 처리방침에 따라 처리됩니다. **무료 요금제의 경우 입력 내용이 서비스 품질 개선·모델 학습에 사용되거나(일부 서비스는 사람의 검토를 포함), 번역 메모리에 보관될 수 있습니다.** 데이터가 학습 등에 사용되는 것을 원치 않는 경우, 데이터 미사용을 보장하는 유료 요금제 키 사용을 권장합니다.

특히 기본 엔진 MyMemory는 **전송된 문장을 번역 메모리에 보관**합니다. 다만 이 메모리는 저장 문장을 열람·열거하는 형태가 아니라, 입력과 거의 동일한 문장에만 결과를 반환하는 **퍼지 매칭(fuzzy matching)** 방식이어서, 제3자가 무관한 질의로 타인의 문장을 회수하기는 구조적으로 어렵습니다. 그럼에도 데이터 외부 보관 자체가 꺼려지거나 민감한 내용을 번역하는 경우에는, 데이터 미사용을 보장하는 유료 키 사용을 권장합니다.

또한 MyMemory는 일일 번역 한도 상향을 원하는 사용자에 한해 **이메일**을 받습니다. 번역 엔진 설정에서 이메일을 입력하면 번역 요청 시 해당 이메일이 MyMemory로 함께 전송되어 MyMemory의 개인정보 처리방침에 따라 처리되며, 입력한 이메일은 사용자의 로컬 PC에 저장됩니다. 이메일 입력은 **선택 사항**으로, 입력하지 않아도 기본 한도 내에서 사용할 수 있습니다.

### 4. 음성 번역

음성 번역은 Windows 11 라이브 캡션(Live Captions)이 변환한 텍스트만 참조하며, 오디오 스트림을 직접 녹음·도청하거나 텍스트를 저장하지 않습니다.

### 5. 인게임 제3자 대화에 대한 책임

인게임 채팅 특성상 사용자 본인 외 '동의하지 않은 제3자'의 대화 내용이 위 경로를 통해 번역 서비스로 전송될 수 있습니다. 대부분 개인을 식별하기 어려운 게임 내 대화이나, 제3자가 실명·연락처 등 민감한 개인정보(PII)를 발설하여 발생하는 노출 리스크는 개발자가 통제할 수 없으며, 본 프로그램의 사적 이용에 따른 책임은 사용자 본인에게 있습니다.

---

## English

ALCT is a non-commercial, open-source personal project and handles your data as follows.

### 1. No-storage principle

ALCT does not collect, accumulate, or store your voice or text data. However, the following external transmissions occur for the features to work:

- **Screen capture (chat translation)**: the captured image is transmitted to the developer's relay server over HTTPS (encrypted in transit) for text extraction (OCR), where the server runs an open-source OCR engine ([RapidOCR](https://github.com/RapidAI/RapidOCR)). After responding, it discards the extracted text and the original image without storing them. RapidOCR is an inference-only OCR library that runs locally inside the server, never transmits input images externally, and is not used for model training. The [server code](https://github.com/shu-rimp/alct-server) is open source as well, so anyone can verify this no-storage behavior directly.
- **Translation requests**: extracted or entered text is sent to a third-party translation service (DeepL, Gemini, MyMemory, etc.) for translation.
- **Input translation**: only the text you explicitly request to translate (from the clipboard) is processed.
- **Other server communication (no data sent)**: aside from OCR, the client communicates with the developer's server only in two cases — ① downloading the latest glossary, and ② checking the latest version info to trigger auto-updates. Both only download from the server and send no user data.

### 2. Translation API key (BYOK, Bring Your Own Key)

Translation requests are sent directly from the client to each translation service, not through the developer's server. Engines that require a key, such as DeepL and Gemini, use **an API key you issue and register yourself** (the default engine, MyMemory, works without a key), and any registered API key is stored on your local PC using the Windows-standard encryption mechanism (DPAPI). Accordingly, responsibility for the translation data and the contractual relationship governing it exist between you and each translation provider; the developer is not part of that data path.

### 3. Translation services' use of data

Transmitted text is processed under each service's privacy policy. **On free tiers, your input may be used to improve service quality or train models (some services include human review), or be retained in a translation memory.** If you don't want your data used for training and the like, we recommend using a paid-tier key that guarantees no data use.

In particular, the default engine MyMemory **retains submitted sentences in its translation memory**. That memory is not browsable or enumerable, though — it works via **fuzzy matching**, returning only segments nearly identical to your input, so it is structurally unlikely for a third party to retrieve someone else's sentence with an unrelated query. Even so, if you'd rather your data not be stored externally at all, or you're translating sensitive content, we recommend a paid key from a service that guarantees no data use.

Additionally, MyMemory accepts an **email** to raise the daily translation limit. If you enter an email in the settings, that email is sent to MyMemory together with translation requests and is processed under MyMemory's privacy policy; the entered email is also stored on your local PC. Entering an email is **optional** — you can use the service within the default limit without it.

### 4. Voice translation

Voice translation only references the text converted by Windows 11 Live Captions; it does not directly record or eavesdrop on the audio stream, nor store the text.

### 5. Responsibility for third-party in-game conversations

By the nature of in-game chat, conversations from third parties other than yourself who have not consented may be transmitted to translation services through the paths above. Most such content is hard to attribute to an identifiable individual, but the exposure risk arising from a third party volunteering sensitive personal information (PII) such as a real name or contact details is beyond the developer's control, and responsibility for your private use of this program rests with you.
