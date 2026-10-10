# Lumen Desktop — Setup & Run (Iteration 2)

## What this build does

Talk → live transcript in a small glass window → Lumen answers in voice, sees
the desktop, and acts through UI Automation or pointer/keyboard fallbacks.
Barge-in works: speak over Lumen and its audio stops immediately. OpenAI uses
on-demand screenshots; Gemini can stream ambient screen frames at up to 1 fps.
The Alice bridge remains stubbed behind its interface.

## Prerequisites

- Windows 11, .NET 8+ SDK (`dotnet --list-sdks` to confirm) — ✔ verified
- An OpenAI API key with Realtime API access and/or a Gemini API key with Live
  API access
- A microphone and speakers (default devices are used)

## 1. Set your API key (once, per user)

```powershell
setx OPENAI_API_KEY "sk-..."
# and/or
setx GEMINI_API_KEY "..."
```

Lumen reads the per-user environment store directly, so it can see values
written by `setx` without requiring a new terminal.

## 2. Build & run

```powershell
cd <path-to-your-clone>\Lumen-Desktop
dotnet build
$env:LUMEN_PROVIDER = "openai" # or "gemini"
dotnet run --project src\Lumen.App
```

A small translucent window appears (top-most, drag anywhere to move,
bottom-right slider changes transparency).

## 3. First test script

1. Click **start** — status shows `connecting…` then `session ready`.
2. Say: *"Open notepad."* → transcript shows your words, `⚙ launch_app`
   appears, Notepad opens, Lumen confirms in voice.
3. Say, while Lumen is talking: *"Stop — open Chrome instead."* →
   playback cuts instantly (barge-in) and Chrome launches.
4. Say: *"What windows are open?"* → `⚙ list_windows`, spoken summary.
5. Say: *"What is on my screen?"* → Lumen captures or reads the current
   desktop, depending on the selected provider.

## Troubleshooting

- **"set OPENAI_API_KEY and restart"** (or the Gemini equivalent) — step 1
  was skipped or the saved value is empty. You can also put the key in
  `openai.key` or `gemini.key` at the repository root; both files are ignored
  by Git.
- **`api error: …` in the status line** — the raw server message; model
  access or protocol issues show up here. The input-transcription model is
  configured separately (`RealtimeClientOptions.InputTranscriptionModel`),
  so if that name is rejected, voice + tools still work — only the "you"
  lines go missing.
- **No sound in/out** — the app uses the Windows *default* mic/speaker;
  check Sound settings.
- **Latency feels high** — the main tuning knobs are reasoning effort (already
  `low`), mic chunk size (50 ms), and speaker buffer (100 ms).

## Project map

```
src/Lumen.Core   — portable brain: Realtime client, tool registry, bridge stub
src/Lumen.App    — Windows body: WPF glass UI, NAudio loop, Win32 actions
docs/REGISTER.md — QTrellis product register (read before changing anything)
docs/decisions/  — ADRs
```
