# ALCT Client

C# .NET 8 WPF overlay for real-time game translation (WPF-UI 3.1, xUnit in `Tests/`).
- **Chat**: hotkey → screen capture → on-device OCR → translation → overlay
- **Voice**: Windows Live Captions → UI Automation polling (`CaptionMonitorService`) → translation → subtitle overlay
- **Input**: user copies Korean → hotkey → translate → result written back to clipboard (user pastes manually; no auto-paste)

## Layout
- `src/MainWindow.{Caption,Hotkeys,Onboarding,Overlays,Settings}.cs` — partial classes per concern
- `src/Core/` services, `src/Core/Translation/` engines (DeepL/Gemini/MyMemory/Langbly + glossary), `src/Views/{Modals,Overlays,Windows}/`
- `Views/Overlays/` is for overlay Windows only; reusable UserControls go to `Utils/`
- `GlobalUsings.cs`: ambiguous type references use aliases, never inline FQN
- User data in `%APPDATA%\ALCT\`: appsettings.json (API keys DPAPI-encrypted; MyMemory email stored plaintext — not a bug), usersettings.json, glossary cache, alct.log
- `BuildConstants.cs`: SERVER_URL placeholder (GitHub Pages base for glossary/version.json) replaced by CI at build

## Gotchas (not obvious from code)
- `TranslationEngineFactory.Create()` wraps every engine in `GlossaryTranslationDecorator`; two engine slots: voice vs text
- `TranslationCoordinator` owns all translation state (per-engine credentials, voice/text engine selection + service instances, voice quota block); MainWindow/Settings/Onboarding mutate it via `UpdateCredential`/`SetVoiceEngine`/`SetTextEngine`, never raw fields
- MyMemory deletes inline Korean from responses — `<x>` segments are split out, preserved, reassembled
- Glossary terms must match what STT/OCR actually outputs (check `[Glossary]` log lines), not pronunciation
- Glossary load priority: GitHub Pages `/glossary.json` → `%APPDATA%` cache → embedded resource (rebuild needed after editing `assets/glossary_data.json`)
- Live Captions restart watched via WMI — `Process.EnableRaisingEvents`/`WaitForExitAsync` do NOT work for UWP
- No synthetic input anywhere (no `SendInput`/auto-paste) — anti-cheat constraint. Input translation only reads the clipboard and writes the result back; the user does all copy/paste with real keypresses. UIA can't read game-rendered (DirectX) text, so clipboard is the only viable path — don't reintroduce SendInput. `ClipboardHelper` (Utils) wraps STA clipboard read/write with retry on `ExternalException`.
- Server connection errors are silent by design (no overlay)
- Running app locks the exe — builds fail with MSB3026/3027 until it's closed
- Tests inject `HttpClient` via `internal` constructors

## Code Style
camelCase variables, UPPER_SNAKE_CASE constants, one function = one responsibility, prefer LINQ.
Log messages (`Logger.Info`/`Logger.Error`) are always English — UI strings stay Korean.
