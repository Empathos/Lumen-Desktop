# Lumen Desktop — Setup & Run (Iteration 1: walking skeleton)

## What this build does

Talk → live transcript in a small glass window → Lumen answers in voice and
can act on the desktop with three tools: `launch_app`, `focus_window`,
`list_windows`. Barge-in works: speak over Lumen and its audio stops
immediately. Everything else (UIA element maps, clicking/typing, Alice
bridge) is stubbed behind interfaces for iteration 2.

## Prerequisites

- Windows 11, .NET 8+ SDK (`dotnet --list-sdks` to confirm) — ✔ verified
- An OpenAI API key with Realtime API access
- A microphone and speakers (default devices are used)

## 1. Set your API key (once, per user)

```powershell
setx OPENAI_API_KEY "sk-..."
```

Open a **new** terminal afterward — `setx` doesn't affect the current one.

## 2. Build & run

```powershell
cd Q:\Lumen-Desktop
dotnet build
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

## Troubleshooting

- **"set OPENAI_API_KEY and restart"** — step 1 was skipped or the terminal
  is stale.
- **`api error: …` in the status line** — the raw server message; model
  access or protocol issues show up here. The input-transcription model is
  configured separately (`RealtimeClientOptions.InputTranscriptionModel`),
  so if that name is rejected, voice + tools still work — only the "you"
  lines go missing.
- **No sound in/out** — the app uses the Windows *default* mic/speaker;
  check Sound settings.
- **Latency feels high** — expected tuning knobs for iteration 2:
  reasoning effort (already `low`), mic chunk size (50 ms), speaker buffer
  (100 ms).

## Project map

```
src/Lumen.Core   — portable brain: Realtime client, tool registry, bridge stub
src/Lumen.App    — Windows body: WPF glass UI, NAudio loop, Win32 actions
docs/REGISTER.md — QTrellis product register (read before changing anything)
docs/decisions/  — ADRs
```
