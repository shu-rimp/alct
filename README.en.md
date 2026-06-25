<h1 align="center">ALCT</h1>

<p align="center">
  <img src="src/assets/alct.png" alt="ALCT" width="120" />
</p>
<p align="center">
  <a href="README.md">한국어</a> | <b>English</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6" alt="platform" />
  <img src="https://img.shields.io/badge/.NET-8-512BD4" alt="dotnet" />
  <img src="https://img.shields.io/badge/license-Apache_2.0-green" alt="license" />
</p>

<p align="center">
  ALCT is a <b>real-time translation overlay</b> that translates foreign text and speech into Korean and displays it over your screen.<br/> It's optimized in particular for online gaming, and is currently tuned around 'Apex Legends'.<br/>
  Without any tedious steps, it translates your teammates' voice and chat, plus your own chat, quickly and easily.
</p>


> ALCT translates foreign languages (Japanese · Simplified Chinese · English) **into Korean**. (Support for other languages is under consideration.)


---

## Overview

ALCT reads the text (or speech) on your screen and shows a Korean translation as a transparent overlay on top of the game. It **never interferes with the game process** — it works the same way as plain utility overlays like Discord or OBS.

**1. Lightweight** — For gamers who are sensitive down to a single frame, load is kept to a minimum: translation is handled by external translation-service APIs, and chat OCR is designed to run lightly on-device only at the moment you press the hotkey.

<details>
<summary>Resource usage measurements (expand)</summary>
Test environment: AMD Ryzen 7 9800X3D 8-Core Processor (8C/16T) · RAM 32GB · NVIDIA GeForce RTX 4070 Ti · Windows 11 Pro 25H2
<br/>

Recorded over 60 minutes of continuous speech, triggering chat every 15 s and input every 30 s. GPU usage is about 0% in both cases.

- **All features on (ALCT + Live Captions)** — continuous-speech voice translation + chat translation + input translation: CPU ≈2% avg, memory ≈720 MB
- **Voice translation off (ALCT alone)** — drops further to CPU ≈0.2% avg, memory ≈320 MB.

<p align="center">
  <img src="src/assets/usage-chart.png" alt="ALCT 60-min resource usage chart (voice on/off comparison)" width="640" />
</p>

> The figures above were measured with every feature enabled; in typical use they are lower.<br/>
> The mid-graph memory drop on the 'voice mode ON' chart is normal behavior that appears when Live Captions reclaims its own memory. It shows that memory stays within a stable range with no leak even over long sessions.<br/>
> *CPU/memory figures may vary depending on the measuring PC's specifications.
</details><br/>

**2. Easy** — To keep you as immersed in the game as possible, tedious steps and UI are kept to a minimum, with a range of customization options for convenience. A guided onboarding on first launch helps you learn how to use it naturally.

<details>
<summary>Example screens (expand)</summary>

<br/>

A user-flow–based onboarding, plus an intuitive UI for translation language/engine, hotkeys, overlay editing, and more.

<table>
<tr>
<td align="center"><img src="src/assets/settings-1.png" alt="Translation settings" width="330" /></td>
<td align="center"><img src="src/assets/settings-2.png" alt="Display settings" width="330" /></td>
</tr>
</table>

</details><br/>

**3. Accurate** — A built-in game glossary pre-translates frequently used terms, applied identically across all translation engines. The glossary is kept up to date automatically with no separate app update. With the Gemini or DeepL engine, even tricky inputs — slang or romanized Japanese (e.g. `yorosiku`) — are translated at high quality.

<details>
<summary>Translation output example by engine (expand)</summary>

<br/>

A translation output example of the default engine (MyMemory) and Gemini. The gap widens on tricky inputs such as romanized Japanese (`ima no yaba sugiru www`) or slang.

<p align="center">
  <img src="src/assets/translation-quality-compare.png" alt="Translation quality comparison (MyMemory vs Gemini)" width="560" />
</p>

</details><br/>


## Features

ALCT provides three features.

### 🎙️ Voice translation

Enable the `Real-time voice translation` toggle and it automatically generates Korean subtitles whenever speech is detected. It groups speech into natural conversational units to translate, and reliably provides subtitles even for fast, continuous speech such as a podcast.

> This feature uses **Windows 11 Live Captions**, so it's only available on **Windows 11 22H2 or later**.

<img src="https://github.com/user-attachments/assets/ca5178f4-59cf-4c02-af7e-37e74c02474d" alt="Voice translation demo" width="640" />

### 💬 Chat translation `default: Ctrl+T`

Press the hotkey to capture the chat region, and the translation is shown as an overlay.

<img src="https://github.com/user-attachments/assets/85581f77-732c-4588-ab84-7126cb104d10" alt="Chat translation demo" width="640" />

### ⌨️ Input translation `default: Ctrl+G`

Copy the chat you've typed, then press the hotkey and ALCT translates it into the target language and places it on your clipboard. Paste it with `Ctrl+V`.

> Translation runs only when you press the hotkey, so it doesn't affect your normal copy/paste.

<img src="https://github.com/user-attachments/assets/2c5eb9cb-c317-48f3-875a-b537392c3ee8" alt="Input translation demo" width="640" />

---


## Install

Download the latest **`ALCT.exe`** from the [Releases page](https://github.com/shu-rimp/alct/releases/latest) and run it. A guided onboarding runs on first launch to introduce the features and help you set things up.

> **⚠️ SmartScreen warning.** ALCT is an unsigned, individually developed open-source binary, so Windows may show a *"Windows protected your PC"* warning. This is expected for unsigned apps. Click **More info → Run anyway** to launch it.


---

## How it works

```
Voice:  Windows Live Captions → UI Automation polling → translation API → subtitle overlay
Chat:   hotkey → screen capture → on-device OCR → translation API → overlay
Input:  clipboard (Korean) → translation API → clipboard (translated) → you paste
```

- **OCR** is processed on-device, so the screen image is never transmitted externally.
- **Translation** uses **your own API key** and is sent directly from the client to the translation service API: MyMemory (default, no key needed), DeepL, Gemini.
- For full details on data handling and privacy, see the [Privacy Policy (PRIVACY.md)](PRIVACY.md).

---

## Contributing 💫

Bug reports, feature ideas, and glossary/code contributions are all welcome — see the [contribution guide](CONTRIBUTING.md).

---

## Tech stack

| Area | Tech | Version |
|---|---|---|
| Language | C# | 12 |
| Runtime | .NET (net8.0-windows) | 8 |
| Distribution | self-contained · win-x64 · single-file (runtime bundled) | — |
| UI | WPF + WPF-UI | 3.1.1 |
| Screen capture | System.Drawing.Common | 8.0.0 |
| OCR engine (default) | Windows built-in OCR (Snipping Tool) ※ | provided by OS |
| OCR engine (fallback) | RapidOcrNet + ONNX Runtime + SkiaSharp · PP-OCRv5 mobile | 2.0.0 |
| API-key encryption | System.Security.Cryptography.ProtectedData (DPAPI) | 8.0.0 |
| System info | System.Management | 8.0.0 |
| Global hotkeys | RegisterHotKey (Win32 / P/Invoke) | — |
| Overlay | WS_EX_TRANSPARENT + WS_EX_LAYERED (Win32) | — |
| Tests | xUnit / Moq / Microsoft.NET.Test.Sdk | 2.9.3 / 4.20.72 / 18.6.0 |

> ※ **Built-in OCR (Snipping Tool)** loads the Windows built-in OCR installed on the user's PC at runtime (not bundled or redistributed with the app). If it is not installed or fails to initialize, it automatically falls back to the fallback engine (RapidOcr). For details, see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

---

## License

[Apache 2.0](LICENSE) © 2026 shu-rimp

---

## Terms of Use & Disclaimer

ALCT is a non-commercial, open-source personal project provided **"as is"** without any warranty. By installing and using it you are deemed to agree to the terms below.

### Anti-cheat & game terms of service

ALCT is designed to never interfere with the game client — it reads the screen externally and only displays translations. Specifically, it does **not**:

- read/write the game process's memory
- inject DLLs or code into the game process
- hook the game's rendering (DirectX, etc.)
- inject synthetic keyboard/mouse input — every select/copy/paste is your own real key press
- monitor input via low-level keyboard hooks
- any action that could grant a gameplay advantage

The overlay that displays translations is an **independent top-level window** not injected into the game, working the same way as plain utility overlays like Discord or OBS.

That said, this design does **not** guarantee immunity from a particular game's anti-cheat system or terms of service. Anti-cheat detection policies are private, vary per game, and change frequently. Some games may also restrict the **use of third-party overlay/screen-capture software itself in their terms, regardless of technical safety.** The developer is not responsible for any disadvantage, including account penalties, resulting from use of this program; whether to use it, and compliance with the relevant game's terms, rests entirely on your own judgment and responsibility.

### Data · Disclaimer · Trademarks

- **Data** — No user data is stored. Translation is handled by each translation service using your own API key, and the developer is not part of that path. For details, see the [Privacy Policy (PRIVACY.md)](PRIVACY.md).
- **Disclaimer** — The developer shall not be liable for any damages arising from the use of, or inability to use, this software.
- **Trademarks** — ALCT is an unofficial third-party tool and is not affiliated with, sponsored by, or endorsed by Electronic Arts or Respawn. "Apex Legends" and all related trademarks, along with the in-game footage and images used in this README and the onboarding videos, are the property of their respective owners.
