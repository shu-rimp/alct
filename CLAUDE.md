# ALCT Client

C# .NET 8 WPF overlay for real-time game translation (WPF-UI 3.0, xUnit in `Tests/`).
- **Chat**: hotkey → screen capture → OCR server → translation → overlay
- **Voice**: Windows Live Captions → UI Automation polling (`CaptionMonitorService`) → translation → subtitle overlay
- **Input**: hotkey → clipboard Korean → translate → paste

## Layout
- `src/MainWindow.{Caption,Hotkeys,Onboarding,Overlays,Settings}.cs` — partial classes per concern
- `src/Core/` services, `src/Core/Translation/` engines (DeepL/Gemini/MyMemory/Langbly + glossary), `src/Views/{Modals,Overlays,Windows}/`
- `Views/Overlays/` is for overlay Windows only; reusable UserControls go to `Utils/`
- `GlobalUsings.cs`: ambiguous type references use aliases, never inline FQN
- User data in `%APPDATA%\ALCT\`: appsettings.json (encrypted API keys), usersettings.json, glossary cache, alct.log
- `BuildConstants.cs`: SERVER_URL/SERVER_TOKEN placeholders replaced by CI at build

## Gotchas (not obvious from code)
- `TranslationEngineFactory.Create()` wraps every engine in `GlossaryTranslationDecorator`; two engine slots: voice vs text
- MyMemory deletes inline Korean from responses — `<x>` segments are split out, preserved, reassembled
- Glossary terms must match what STT/OCR actually outputs (check `[Glossary]` log lines), not pronunciation — exception: ja homophone-kanji STT variants (切る/着る←キル) go in the `readings` section, matched by kana reading via NMeCab (`{n}` prefix = only right after a digit; `*` suffix = may end mid-word, default is morpheme-boundary end so にぱ won't cut 次にパスファインダー)
- NMeCab dictionary publishes as `IpaDic\` (~50MB) beside the exe; if missing, reading match silently degrades to surface-only (error logged once)
- Glossary load priority: server `/glossary` → `%APPDATA%` cache → embedded resource (rebuild needed after editing `assets/glossary_data.json`)
- Live Captions restart watched via WMI — `Process.EnableRaisingEvents`/`WaitForExitAsync` do NOT work for UWP
- Server connection errors are silent by design (no overlay)
- Running app locks the exe — builds fail with MSB3026/3027 until it's closed
- Tests inject `HttpClient` via `internal` constructors

## Code Style
camelCase variables, UPPER_SNAKE_CASE constants, one function = one responsibility, prefer LINQ.
