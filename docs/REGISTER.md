# Product Register — Lumen Desktop (LD-)

QTrellis register. Rules: ~/.claude/skills/qtrellis/SKILL.md
Stable = UCXM has no − and no ?. Scores are lazy and must carry reasons.

## Register

| ID | Item (≤5 words) | From | Status | UCXM | Proof | Note |
|----|-----------------|------|--------|------|-------|------|
| LD-001 | Realtime voice loop | — | spec | +0+0 | src/Lumen.Core/Realtime/RealtimeClient.cs | theme: Voice; U: whole product hinges on it; X: WS round-trip acceptable |
| LD-002 | Live streamed transcription | LD-001 | spec | ++0+ | src/Lumen.Core/Realtime/RealtimeClient.cs | C: free with realtime session; M: whisper-1 accepted+echoed by server (probe 2026-07-21) |
| LD-003 | Voice replies with barge-in | LD-001 | spec | +00? | src/Lumen.App/Audio/AudioLoop.cs | U: hard requirement; M: cancel timing on 2.1 unverified |
| LD-004 | Parallel tool calling | LD-001 | spec | +0+0 | src/Lumen.Core/Realtime/RealtimeClient.cs | U: enables consult-while-acting; X: docs confirm support |
| LD-005 | Clarifying questions on ambiguity | LD-001 | spec | | src/Lumen.Core/Realtime/RealtimeClientOptions.cs | ask, don't guess; in session instructions |
| LD-006 | Minimal liquid-glass transcript window | — | spec | +++0 | src/Lumen.App/MainWindow.xaml | theme: UI; U: only surface; C: one window; X: acrylic native |
| LD-007 | Adjustable window transparency | LD-006 | spec | 0+00 | src/Lumen.App/MainWindow.xaml | C: one slider |
| LD-008 | Start stop mute controls | LD-006 | spec | ++00 | src/Lumen.App/MainWindow.xaml | U: explicit requirement; C: three buttons |
| LD-009 | UIA-first action engine | — | spec | ++0? | src/Lumen.App/Desktop/UiaScreenReader.cs | theme: Actions; U: no pixel hunting; C: WPF ships UIA; M: id discipline untested |
| LD-010 | Screenshot fallback observation | LD-009 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | for poor accessibility trees |
| LD-011 | SendInput mouse and keyboard | LD-009 | spec | ++00 | src/Lumen.App/Desktop/InputInjector.cs | U: covers pattern-less apps; C: pure Win32, no deps |
| LD-012 | App launch, window management | LD-009 | spec | +0+? | src/Lumen.App/Desktop/WindowsDesktopActions.cs | U: first real actions; X: shell+Win32 native; M: app-name guessing untested; launch/focus/list only, dialogs pending |
| LD-013 | Named window personas | — | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | theme: Personas; session registry |
| LD-014 | Ask-time window Q&A | LD-013 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | capture at question time |
| LD-015 | Configurable snapshot history | LD-013 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | retention is a setting |
| LD-016 | Portable core, thin adapter | — | spec | ++00 | src/Lumen.Core/Adapters/IDesktopActions.cs | theme: Harness; U: adaptability requirement; C: discipline, not code |
| LD-017 | Cumulative lean session context | LD-016 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | shape TBD via iteration |
| LD-018 | Alice bridge, three channels | — | concept | | src/Lumen.Core/Bridge/AliceBridge.cs | theme: Alice; one localhost WebSocket to OpenClaw/WSL; surface stubbed |
| LD-019 | Observer stream to Alice | LD-018 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | transcript + action log out |
| LD-020 | Alice context injection channel | LD-018 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | out-of-band items/instructions in |
| LD-021 | consult_alice model tool | LD-018 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | request/response over same pipe |
| LD-022 | Alice veto of actions | LD-018 | deferred | | docs/decisions/ADR-0001-architecture-and-stack.md | advisory-only in v1 |
| RISK-001 | Poor UIA trees block actions | LD-009 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | mitigated by LD-010 fallback |
| RISK-002 | Model click accuracy insufficient | LD-009 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | action layer swappable to CU model |
| RISK-003 | API round-trip dominates latency | LD-001 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | reasoning effort low; lean harness |
| LD-023 | On-demand screenshot perception | LD-001 | spec | +0+? | src/Lumen.App/Desktop/ScreenCapture.cs | theme: Vision; U: she can finally see; X: GDI full-screen ok; M: coord mapping rough |
| LD-024 | Gemini streaming ambient sight | — | spec | +?+? | src/Lumen.Core/Realtime/GeminiLiveClient.cs | theme: Vision; U: continuous sight eases nav; C: 2nd provider; verified Gemini Live streams video ≤1fps |
| LD-025 | Provider-agnostic voice session | LD-001 | spec | ++00 | src/Lumen.Core/Realtime/IVoiceSession.cs | U: A/B brains without touching actions; C: one interface |
| RISK-004 | GDI misses composited windows | LD-023 | concept | | docs/decisions/ADR-0001-architecture-and-stack.md | full-screen ok; per-window needs DXGI/WGC upgrade |
| RISK-005 | Two live providers to maintain | LD-025 | concept | | src/Lumen.Core/Realtime/GeminiLiveClient.cs | pick winner after A/B, retire loser |
| LD-026 | Visible agent cursor overlay | LD-011 | spec | ++0? | src/Lumen.App/AgentCursor.cs | U: MouseMux feel, user keeps own cursor; C: one overlay window; M: n/a visual-only |
| LD-027 | Instant local barge-in | LD-003 | spec | ++0+ | src/Lumen.App/Audio/AudioLoop.cs | U: interrupt is critical on desktop; X: local detection, zero server round-trip |
| LD-028 | MouseMux cursor loan | LD-011 | spec | ++0? | src/Lumen.App/Desktop/InputInjector.cs | U: user keeps pointer, no tug-of-war; C: save/act/restore, no driver; M: click restore verified live; drag-collision guard (button-down = busy) retest pending |
| LD-029 | No-cursor background action lane | LD-009 | spec | ++0? | src/Lumen.App/Desktop/UiaScreenReader.cs | U: true-async for most actions, no driver; C: extends UIA reader; M: ValuePattern/Scroll/posted-click per-app reliability untested |

## Ideas

| ID | Item (≤5 words) | From | Status | UCXM | Proof | Note |
|----|-----------------|------|--------|------|-------|------|
| IDEA-001 | macOS and Linux adapters | LD-016 | idea | | | CGEvent/Accessibility; X11/Wayland |
| IDEA-002 | Benchmark gpt-realtime-2.1-mini | LD-001 | idea | | | ~3x cheaper; lighter reasoning |
| IDEA-003 | Split transcription to whisper | LD-001 | idea | | | gpt-realtime-whisper if ever needed |
| IDEA-004 | Avalonia cross-platform UI | LD-016 | idea | | | only if Mac/Linux happens |
| IDEA-005 | Personas converge with agents | LD-013 | idea | | | window as an agent's territory |
| IDEA-006 | RDP-loopback PiP own cursor | LD-011 | idea | | | UFO²-style; agent's own desktop+cursor, user undisturbed |
| IDEA-007 | MouseMux-style second cursor | LD-011 | promoted | | | → LD-028 (cursor loan); true driver-level multiplexing stays out of scope |
| IDEA-008 | DXGI desktop duplication capture | LD-023 | idea | | | GPU capture, per-window, lower latency than GDI |
