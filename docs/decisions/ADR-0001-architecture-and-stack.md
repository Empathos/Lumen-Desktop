# ADR-0001 — Architecture and Stack

Status: accepted · Date: 2026-07-18 · Product: Lumen Desktop (LD-)

## Context

Lumen Desktop is a voice-driven Windows 11 desktop copilot: the user speaks,
a minimal always-on-top UI shows a live transcript, and the assistant acts on
the desktop (launch apps, click, type, manage windows) while responding in
voice and asking clarifying questions when intent is ambiguous. Priorities:
lean harness, speed first, adaptability.

## Decisions

### 1. Voice/orchestration model: gpt-realtime-2.1 (OpenAI Realtime API)
Current recommended Realtime model (July 2026). Supports parallel tool
calling, image input, adjustable reasoning effort (start `low`), and had p95
latency reduced ≥25% in the 2.1 release. Transport: WebSocket (native
desktop app, not browser). `gpt-realtime-2.1-mini` to be benchmarked as a
cheaper/faster alternative once the harness exists.

### 2. Single-model design: the Realtime model does the seeing
No second vision/computer-use model in v1. The Realtime session receives
observations directly and acts via tools. Mitigation for click-accuracy risk:
the action layer sits behind an interface, so a dedicated computer-use model
can be slotted in later without touching the rest.

### 3. UIA-first action engine, screenshots as fallback
Primary observation channel is a compact text element map from Windows UI
Automation (names, roles, bounding boxes) — small, fast, lets the model act
by element reference instead of pixel-guessing. Raw screenshots are the
fallback for apps with poor accessibility trees.

### 4. Stack: C# / .NET everywhere; WPF (or WinUI 3) on Windows
Chosen over Rust and Electron. Rationale: the latency bottleneck is the API
round-trip, not the harness language; C# is the first-class consumer of the
UIA COM API; native acrylic/liquid-glass UI; mature WASAPI audio and OpenAI
SDK ecosystems; fastest iteration velocity. Rust would add complexity to
optimize the one part of the system that is already free. Electron would wrap
a non-portable engine in a portable shell while costing memory and latency.

### 5. Two-layer split: portable core, thin OS adapter
The core (Realtime session management, conversation state, tool routing,
snapshot history, persona registry) is a plain .NET library, OS-agnostic.
The adapter (SendInput, UIA, screen capture, window management, UI) is
per-OS. Porting to macOS/Linux later means one new adapter + small UI, not a
rebuild. The core never calls a Windows API directly.

### 6. Alice bridge: one local WebSocket, three channels, advisory-only v1
Alice (agent running in OpenClaw under WSL on the same host) integrates via
a single localhost WebSocket bridge with three message types:
- **Observer** — harness streams transcript + action log out to Alice.
- **Injector** — Alice pushes context items / instruction updates into the
  Realtime session out-of-band.
- **Tool** — the Realtime model can call `consult_alice`; routed
  request/response over the same pipe.
Each channel behind its own feature flag. Alice is advisory-only in v1; no
veto/steering of in-flight actions (deferred until it earns its complexity).

### 7. UI: minimal transcript window
Small, scrollable, liquid-glass/acrylic with adjustable transparency.
Controls limited to start, stop, mute — small but noticeable. Nothing else.

### 8. Named windows / personas
Windows can be assigned names ("Alice", "persona specialist") kept in a
session persona registry. Q&A over a named window uses an ask-time capture;
a history of snapshots is retained with configurable retention.

### 9. Session context
Cumulative within a session, kept deliberately lean; exact shape to be
determined by iteration.

## Consequences

Windows-first, one language across future platforms, model-swappable action
layer, and a harness whose early iterations focus on latency tuning. Known
risks are registered as RISK- rows in docs/REGISTER.md.
