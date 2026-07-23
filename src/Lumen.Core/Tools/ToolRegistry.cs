using System.Collections.Concurrent;
using System.Text.Json;

namespace Lumen.Core.Tools;

/// <summary>
/// Holds tool definitions and dispatches tool calls coming from the model.
/// Dispatch is fire-per-call and safe to run in parallel (the model may issue
/// parallel tool calls).
/// </summary>
public sealed class ToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools = new();

    public void Register(ToolDefinition tool) => _tools[tool.Name] = tool;

    public IReadOnlyCollection<ToolDefinition> All => _tools.Values.ToArray();

    public async Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken ct)
    {
        if (!_tools.TryGetValue(name, out var tool))
            return JsonSerializer.Serialize(new { error = $"unknown tool '{name}'" });

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            return await tool.Handler(doc.RootElement.Clone(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
