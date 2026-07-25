# Lumen Desktop

A voice-driven copilot that lives on your Windows 11 desktop. You talk; Lumen
answers in voice, sees the screen, and acts — launching apps, clicking,
typing, scrolling — while a small liquid-glass window shows the live
transcript. Interrupt her mid-sentence and she stops instantly.

## What she can do

- **Realtime voice loop** — streamed conversation with live transcription and
  instant local barge-in (speak over her; playback cuts with zero server
  round-trip).
- **Sight** — on-demand full-screen capture (OpenAI provider) or continuous
  ~1 fps ambient video (Gemini provider), plus a compact UI Automation element
  map of the focused window so she acts by element reference, not pixel
  guessing.
- **Hands** — a tiered action lane, most-invisible first:
  1. UIA patterns (invoke/toggle/select/expand) — background, no cursor
  2. Direct value set & scroll (`ValuePattern`/`ScrollPattern`) — fill fields
     and scroll lists with no cursor and no keyboard focus
  3. Posted window messages — background clicks while you're using the mouse
  4. **Cursor loan** — when only a real click will do, she waits for your hand
     to rest, borrows the cursor for the instant of the action, and puts it
     back where it was; if you stay busy she declines and tells you
- **Visible presence** — her own teal pointer glides and pulses wherever she
  acts, so her actions are attributable without stealing your cursor.

## Two brains, one body

Both providers implement the same `IVoiceSession` contract for A/B testing:

| | OpenAI (`run.ps1`) | Gemini (`run-gemini.ps1`) |
|---|---|---|
| Model | `gpt-realtime-2.1` | `gemini-3.1-flash-live-preview` |
| Sight | on-demand screenshots | streaming video ≤1 fps |
| Transport | WebSocket (Realtime API) | WebSocket (Live API) |

## Architecture

Two-layer split (see `docs/decisions/ADR-0001-architecture-and-stack.md`):

```
src/Lumen.Core   portable brain — voice sessions, tool registry, adapters,
                 Alice bridge (stub); never touches an OS API
src/Lumen.App    Windows body — WPF glass UI, NAudio loop, UIA reader,
                 SendInput injection, screen capture, agent cursor
docs/REGISTER.md QTrellis product register — read before changing anything
```

Porting to another OS means one new adapter and a small UI, not a rebuild.

## Quickstart

Prereqs: Windows 11, .NET 8 SDK, a mic and speakers (default devices).

1. Put an OpenAI API key in `openai.key` at the repo root (or set
   `OPENAI_API_KEY`), and/or a Gemini key in `gemini.key` / `GEMINI_API_KEY`.
   Key files are git-ignored.
2. Build and run:

   ```powershell
   dotnet build
   $env:LUMEN_PROVIDER = "openai" # or "gemini"
   dotnet run --project src\Lumen.App
   ```

3. Click **start** in the glass window and say *"Open notepad."* Speak over
   her reply to test barge-in. Status problems are appended to
   `%LOCALAPPDATA%\Lumen\status.log`.

See `SETUP.md` for the full first-test script and troubleshooting.

## Status

Iteration 2. Voice, sight, and the tiered action lane are live under both
providers. The Alice bridge (observer/injector/consult channels to a sibling
agent) is interface-stubbed. The product register in `docs/REGISTER.md`
tracks every item, risk, and idea — QTrellis discipline, product code `LD-`.
