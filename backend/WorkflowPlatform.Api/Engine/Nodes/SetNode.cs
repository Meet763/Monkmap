using System.Text.Json;

namespace WorkflowPlatform.Api.Engine.Nodes;

/// <summary>
/// Builds an output object from user-defined fields. Each field value may contain
/// {{ }} expressions resolved against the input / previous nodes.
/// Parameter "fields" is a JSON object: { "name": "{{$json.firstName}}", "count": "5" }
/// If "keepInput" is true, input fields are merged first.
/// </summary>
public class SetNode : INode
{
    public NodeDefinition Definition => new()
    {
        Type = "Set",
        DisplayName = "Set / Edit Fields",
        Group = "Logic",
        Icon = "pencil",
        OutputPorts = new[] { "main" },
        Parameters = new()
        {
            new NodeParameter
            {
                Name = "fields", DisplayName = "Fields (JSON object)", Type = "json",
                Default = "{\n  \"message\": \"Hello {{$json.name}}\"\n}",
                Placeholder = "{ \"key\": \"value or {{$json.field}}\" }"
            },
            new NodeParameter
            {
                Name = "keepInput", DisplayName = "Keep input fields", Type = "boolean",
                Default = "false"
            }
        }
    };

    public Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx)
    {
        var result = new Dictionary<string, object?>();

        var keepInput = ctx.GetString("keepInput", "false")
            .Equals("true", StringComparison.OrdinalIgnoreCase);
        if (keepInput && ctx.FirstInput.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in ctx.FirstInput.EnumerateObject())
                result[prop.Name] = JsonHelpers.AsString(prop.Value);
        }

        var fieldsRaw = ctx.GetString("fields", "{}");
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(fieldsRaw) ? "{}" : fieldsRaw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var template = JsonHelpers.AsString(prop.Value);
                    result[prop.Name] = ExpressionResolver.Resolve(template, ctx.FirstInput, ctx.NodeOutputs);
                }
            }
        }
        catch (JsonException ex)
        {
            return Task.FromResult(NodeResult.Fail($"Invalid 'fields' JSON: {ex.Message}"));
        }

        return Task.FromResult(NodeResult.Ok(JsonHelpers.FromObject(result)));
    }
}
