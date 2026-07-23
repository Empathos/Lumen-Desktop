namespace Lumen.Core.Bridge;

/// <summary>
/// Stub for the Alice bridge (LD-018..LD-021): one localhost WebSocket to the
/// OpenClaw gateway in WSL carrying three channels (observer out, injector in,
/// consult_alice request/response). Iteration 1 ships flags + surface only;
/// wire protocol lands in a later iteration. Advisory-only by design (LD-022 deferred).
/// </summary>
public sealed class AliceBridge
{
    public bool ObserverEnabled { get; set; }
    public bool InjectorEnabled { get; set; }
    public bool ToolEnabled { get; set; }

    /// <summary>Observer channel: called by the harness for every transcript/action event.</summary>
    public Task PublishAsync(string eventType, string payload, CancellationToken ct)
        => Task.CompletedTask; // no-op until bridge protocol is implemented

    /// <summary>Tool channel: consult_alice. Returns Alice's answer.</summary>
    public Task<string> ConsultAsync(string question, CancellationToken ct)
        => Task.FromResult("""{"error":"Alice bridge not yet connected"}""");
}
