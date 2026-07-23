using System.Text.Json;

namespace Lumen.Core.Tools;

/// <summary>
/// One callable tool exposed to the Realtime model.
/// Parameters is a JSON Schema object serialized as a string.
/// </summary>
public sealed class ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ParametersJsonSchema { get; init; }
    public required Func<JsonElement, CancellationToken, Task<string>> Handler { get; init; }
}
