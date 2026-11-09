using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkflowPlatform.Api.Engine;

/// <summary>Deserialized shape of WorkflowVersion.GraphJson.</summary>
public class WorkflowGraph
{
    [JsonPropertyName("nodes")]
    public List<GraphNode> Nodes { get; set; } = new();

    [JsonPropertyName("connections")]
    public List<GraphConnection> Connections { get; set; } = new();
}

public class GraphNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public NodePosition Position { get; set; } = new();

    /// <summary>User-configured parameters for this node.</summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, JsonElement> Parameters { get; set; } = new();
}

public class NodePosition
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
}

public class GraphConnection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    /// <summary>Output port on the source node, e.g. "main", "true", "false".</summary>
    [JsonPropertyName("sourceHandle")]
    public string? SourceHandle { get; set; }

    [JsonPropertyName("targetHandle")]
    public string? TargetHandle { get; set; }
}
