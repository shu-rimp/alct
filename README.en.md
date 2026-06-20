<h1 align="center">ALCT</h1>

<p align="center">
  <img src="src/assets/alct.png" alt="ALCT" width="120" />
</p>
<p align="center">
  ALCT is a real-time translation overlay built for online games.<br/> It is currently optimized for 'Apex Legends'.<br/>
  In fast-moving matches, it translates your teammates' voice and chat — and your own chat — quickly and accurately.
</p>

<p align="center">
  <a href="README.md">한국어</a> | <b>English</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6" alt="platform" />
  <img src="https://img.shields.io/badge/.NET-8-512BD4" alt="dotnet" />
  <img src="https://img.shields.io/badge/license-Apache_2.0-green" alt="license" />
</p>

> **Built for Korean gamers.** It translates foreign text **into Korean** only, and the app UI is in Korean too. (Other languages are under consideration.)

---

## Overview

ALCT reads the text (or speech) on your screen and shows a Korean translation as a transparent overlay on top of the game. It **never interferes with the game process** — it works the same way as plain utility overlays like Discord or OBS.

**1. Lightweight** — Resource-heavy work is offloaded externally (translation: service APIs / OCR: a dedicated OCR server) for gamers who are sensitive down to a single frame, so it puts almost no load on game performance.

<details>
<summary>📊 Resource usage measurements (expand)</summary>
Test environment: AMD Ryzen 7 9800X3D 8-Core Processor (8C/16T) · RAM 32GB · NVIDIA GeForce RTX 4070 Ti · Windows 11 Pro 25H2
<br/>

**All features on (ALCT + Live Captions)** — 60 min of continuous-speech voice translation + chat translation + input translation

Over a 1-hour measurement: CPU ≈1% avg, GPU ≈0%, memory ≈600 MB

<p align="center">
  <img src="src/assets/usage-chart.png" alt="ALCT resource usage chart (all features on)" width="560" />
</p>

> The figures above were measured with every feature enabled; in typical use they are lower.<br/>
> The mid-graph memory drop is normal .NET garbage-collector behavior reclaiming accumulated unused memory at once, showing that memory stays within a stable range with no leak even over long sessions.

**Voice translation off (ALCT alone)** — drops further to CPU ≈0.4% avg, memory ≈200 MB.

<p align="center">
  <img src="src/assets/usage-chart-2.png" alt="ALCT resource usage chart (voice translation off)" width="560" />
</p>

<br/>
(The CPU/GPU usage units for this data are percentages (%), so they may vary depending on the user's PC specifications.)
</details><br/>

**2. Easy** — Every feature works via a hotkey or automatically. To keep you immersed in the game, tedious steps and UI are kept to a minimum, with a range of customization options for convenience. A guided onboarding on first launch helps you learn how to use it naturally.

<details>
<summary>🖼️ Example screens (expand)</summary>

<br/>

A user-flow–based onboarding, plus an intuitive UI for translation language/engine, hotkeys, overlay editing, and more.

<table>
<tr>
<td align="center"><img src="src/assets/settings-1.png" alt="Translation settings" width="330" /></td>
<td align="center"><img src="src/assets/settings-2.png" alt="Display settings" width="330" /></td>
</tr>
</table>

</details><br/>

**3. Accurate** — A built-in game glossary pre-translates frequently used terms, applied identically across all translation engines. The glossary is kept up to date via the server with no separate app update. With the Gemini or DeepL engine, even tricky inputs — slang or romanized Japanese (e.g. `yorosiku`) — are translated at high quality.

<details>
<summary>🖼️ Translation output example by engine (expand)</summary>

<br/>

A translation output example of the default engine (MyMemory) and Gemini. The gap widens on tricky inputs such as romanized Japanese (`ima no yaba sugiru www`) or slang.

<p align="center">
  <img src="src/assets/translation-quality-compare.png" alt="Translation quality comparison (MyMemory vs Gemini)" width="560" />
</p>

</details><br/>


**Supported languages**: Japanese · Simplified Chinese · English


## Features

ALCT provides three features.

### 🎙️ Voice translation

Reads the text converted by **Windows 11 Live Captions**, translates it into Korean, and shows it as a running subtitle overlay. It never records or intercepts the audio stream directly — it only references Live Captions. When enabled, it automatically generates Korean subtitles whenever speech is detected.

> Live Captions is only available on **Windows 11 22H2 or later**, so voice translation requires that version or above.

<video src="https://github.com/shu-rimp/alct/raw/main/src/assets/voice-translation-demo.mp4" controls width="640"></video>

### 💬 Chat translation `default: Ctrl+T`

Press the hotkey to capture the chat region → send it to the OCR server (text extraction) → translate → show the result as an overlay.

<video src="https://github.com/shu-rimp/alct/raw/main/src/assets/chat-translation-demo.mp4" controls width="640"></video>

### ⌨️ Input translation `default: Ctrl+G`

Copy the chat you've typed → press the hotkey and ALCT translates it into the target language and places it on your clipboard. Paste it with `Ctrl+V`.

> Translation runs only when you press the hotkey, so it doesn't affect your normal copy/paste.

<video src="https://github.com/shu-rimp/alct/raw/main/src/assets/input-translation-demo.mp4" controls width="640"></video>

---


## Install

Download the latest **`ALCT.exe`** from the [Releases page](https://github.com/shu-rimp/alct/releases/latest) and run it. (It's self-contained — no installer and no separate .NET install required.) A guided onboarding runs on first launch to introduce the features and help you set things up.

> **⚠️ SmartScreen warning.** ALCT is an unsigned, individually developed open-source binary, so Windows may show a *"Windows protected your PC"* warning. This is expected for unsigned apps. Click **More info → Run anyway** to launch it.


---

## How it works

```
Voice:  Windows Live Captions → UI Automation polling → translation API → subtitle overlay
Chat:   hotkey → screen capture (PNG) → OCR relay server → translation API → overlay
Input:  clipboard (Korean) → translation API → clipboard (translated) → you paste
```

- The **OCR relay server** runs on the open-source RapidOCR, extracting only text and discarding the image immediately. It runs on modest hardware, so responses may slow down as usage grows. (Voice and input translation don't go through the server.)
  > If you want self-hosting, please refer to the [server repository](https://github.com/shu-rimp/alct-server).
- **Translation** is sent directly from the client to each translation service API, not through the developer's server: MyMemory (default, no key needed), DeepL, Gemini. Engines that require a key use **your own API key (BYOK, Bring Your Own Key)**.
- For full details on data handling and privacy, see the [Privacy Policy (PRIVACY.md)](PRIVACY.md).

---

## Contributing 💫

Bug reports, feature ideas, and code contributions are all welcome — see the [contribution guide (CONTRIBUTING.md)](CONTRIBUTING.md). The **game glossary** is managed in the server repository, [alct-server](https://github.com/shu-rimp/alct-server).

---

## Tech stack

| Area | Tech | Version |
|---|---|---|
| Language | C# | 12 |
| Runtime | .NET (net8.0-windows) | 8 |
| Distribution | self-contained · win-x64 · single-file (runtime bundled) | — |
| UI | WPF + WPF-UI | 3.1.1 |
| Screen capture | System.Drawing.Common | 8.0.0 |
| API-key encryption | System.Security.Cryptography.ProtectedData (DPAPI) | 8.0.0 |
| System info | System.Management | 8.0.0 |
| Global hotkeys | RegisterHotKey (Win32 / P/Invoke) | — |
| Overlay | WS_EX_TRANSPARENT + WS_EX_LAYERED (Win32) | — |
| Tests | xUnit / Moq / Microsoft.NET.Test.Sdk | 2.9.3 / 4.20.72 / 18.6.0 |

---

## License

[Apache 2.0](LICENSE) © 2026 shu-rimp

---

## Terms of Use & Disclaimer

ALCT is a non-commercial, open-source personal project provided **"as is"** without any warranty. In short — and by installing and using it you agree to the full terms below:

- **Anti-cheat & game ToS** — ALCT never interferes with the game; it only reads the screen externally (no memory access, injection, hooking, synthetic input, or any action that grants a gameplay advantage). This does not guarantee immunity from any game's penalties, and **all responsibility for anything that may arise from using this program, including account penalties, rests with you.**
- **No-storage & BYOK** — Your data is not stored. Translation runs directly through your own API key, and the OCR server discards images immediately after extraction.
- **Sensitive content** — The default engine (MyMemory) retains submitted sentences in its translation memory. For sensitive content, we recommend a paid key that guarantees no data use.

<details>
<summary><b>Expand full Terms of Use & Disclaimer</b></summary>
<br>

### 1. Data handling & privacy

The full details of how ALCT handles your data are kept in a separate document → **[Privacy Policy (PRIVACY.md)](PRIVACY.md)**

### 2. Anti-cheat & game terms of service

ALCT is designed to never interfere with the game client — it reads the screen externally and only displays translations. Specifically, it does **not**:

- read/write the game process's memory
- inject DLLs or code into the game process
- hook the game's rendering (DirectX, etc.)
- inject synthetic keyboard/mouse input — every select/copy/paste is your own real key press
- monitor input via low-level keyboard hooks
- any action that could grant a gameplay advantage

The overlay that displays translations is an **independent top-level window** not injected into the game, working the same way as plain utility overlays like Discord or OBS.

That said, this design does **not** guarantee immunity from any game's anti-cheat system or terms of service. Anti-cheat detection policies are private, vary per game, and change frequently. Some games also restrict the **use of third-party overlay/screen-capture software itself in their terms, regardless of technical safety.** The developer is not responsible for any disadvantage, including account penalties, resulting from use of this program; whether to use it, and compliance with the relevant game's terms, rests entirely on your own judgment and responsibility.

### 3. General disclaimer

This software is provided **"as is"** without warranty of any kind, express or implied. The developer shall not be liable for any direct or indirect damages arising from the use of, or inability to use, this program.

### 4. Trademarks & copyright

ALCT is an unofficial third-party tool and is not affiliated with, sponsored by, or endorsed by Electronic Arts Inc. or Respawn Entertainment. "Apex Legends" and all related trademarks, along with the in-game footage and images used in this README and the onboarding demo videos, are the property of their respective owners.

</details>
