using System.Text.Json;

namespace WorkflowPlatform.Api.Engine;

/// <summary>An item flowing between nodes. We use JsonElement so any JSON shape works.</summary>
public record DataItem(JsonElement Json);

/// <summary>What a node receives when it executes.</summary>
public class NodeExecutionContext
{
    public required GraphNode Node { get; init; }
    public required IReadOnlyList<DataItem> InputItems { get; init; }
    public required IServiceProvider Services { get; init; }
    public required ILogger Logger { get; init; }
    public CancellationToken CancellationToken { get; init; }

    /// <summary>Owner of the workflow being run (for user-scoped operations, e.g. agent tools).</summary>
    public Guid? OwnerUserId { get; init; }

    /// <summary>Id of the workflow being run (used to block self-recursion in sub-workflow tools).</summary>
    public Guid? WorkflowId { get; init; }

    /// <summary>First output item of every already-executed node, keyed by node id.</summary>
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement> NodeOutputs { get; init; }
        = new Dictionary<string, System.Text.Json.JsonElement>();

    /// <summary>Read a string parameter and resolve any {{ }} expressions in it.</summary>
    public string ResolveString(string key, string fallback = "")
    {
        var raw = GetString(key, fallback);
        return ExpressionResolver.Resolve(raw, FirstInput, NodeOutputs);
    }

    public JsonElement? GetParam(string key) =>
        Node.Parameters.TryGetValue(key, out var v) ? v : null;

    public string GetString(string key, string fallback = "")
    {
        if (Node.Parameters.TryGetValue(key, out var v))
        {
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? fallback,
                JsonValueKind.Number => v.ToString(),
                JsonValueKind.True or JsonValueKind.False => v.GetBoolean().ToString(),
                JsonValueKind.Null or JsonValueKind.Undefined => fallback,
                _ => v.GetRawText()
            };
        }
        return fallback;
    }

    /// <summary>First input item (the common single-item case), or an empty object.</summary>
    public JsonElement FirstInput =>
        InputItems.Count > 0 ? InputItems[0].Json : JsonHelpers.EmptyObject();
}

/// <summary>The result a node produces.</summary>
public class NodeResult
{
    public bool Success { get; init; } = true;
    public IReadOnlyList<DataItem> OutputItems { get; init; } = Array.Empty<DataItem>();
    public string? Error { get; init; }

    /// <summary>Which output port to follow (branching). Default "main".</summary>
    public string OutputPort { get; init; } = "main";

    public static NodeResult Ok(IEnumerable<DataItem> items, string port = "main") =>
        new() { Success = true, OutputItems = items.ToList(), OutputPort = port };

    public static NodeResult Ok(JsonElement item, string port = "main") =>
        new() { Success = true, OutputItems = new List<DataItem> { new(item) }, OutputPort = port };

    public static NodeResult Fail(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>Blueprint used by the frontend to render a node and its parameter form.</summary>
public class NodeDefinition
{
    public string Type { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Group { get; set; } = "General"; // Triggers, Logic, Apps...
    public string Icon { get; set; } = "box";
    public bool IsTrigger { get; set; }
    public string[] OutputPorts { get; set; } = { "main" };
    public List<NodeParameter> Parameters { get; set; } = new();
}

public class NodeParameter
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = "string"; // string, number, boolean, json, options
    public bool Required { get; set; }
    public string? Default { get; set; }
    public string[]? Options { get; set; }
    public string? Placeholder { get; set; }
}

/// <summary>Every node type implements this.</summary>
public interface INode
{
    NodeDefinition Definition { get; }
    Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx);
}
