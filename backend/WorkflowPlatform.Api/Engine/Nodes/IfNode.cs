using System.Globalization;
using System.Text.Json;

namespace WorkflowPlatform.Api.Engine.Nodes;

/// <summary>
/// Branches execution. Compares a (resolved) left value to a right value using an operator,
/// then routes the input item to the "true" or "false" output port.
/// </summary>
public class IfNode : INode
{
    public NodeDefinition Definition => new()
    {
        Type = "If",
        DisplayName = "IF",
        Group = "Logic",
        Icon = "git-branch",
        OutputPorts = new[] { "true", "false" },
        Parameters = new()
        {
            new NodeParameter
            {
                Name = "left", DisplayName = "Value 1", Type = "string",
                Placeholder = "{{$json.status}}"
            },
            new NodeParameter
            {
                Name = "operator", DisplayName = "Operator", Type = "options",
                Options = new[] { "equals", "notEquals", "contains", "greaterThan", "lessThan", "isEmpty", "isNotEmpty" },
                Default = "equals"
            },
            new NodeParameter
            {
                Name = "right", DisplayName = "Value 2", Type = "string",
                Placeholder = "active"
            }
        }
    };

    public Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx)
    {
        var left = ctx.ResolveString("left");
        var right = ctx.ResolveString("right");
        var op = ctx.GetString("operator", "equals");

        bool pass = op switch
        {
            "equals" => string.Equals(left, right, StringComparison.Ordinal),
            "notEquals" => !string.Equals(left, right, StringComparison.Ordinal),
            "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "greaterThan" => CompareNumbers(left, right) > 0,
            "lessThan" => CompareNumbers(left, right) < 0,
            "isEmpty" => string.IsNullOrEmpty(left),
            "isNotEmpty" => !string.IsNullOrEmpty(left),
            _ => false
        };

        var item = ctx.FirstInput;
        return Task.FromResult(NodeResult.Ok(item, pass ? "true" : "false"));
    }

    private static int CompareNumbers(string a, string b)
    {
        var okA = double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var da);
        var okB = double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var db);
        if (okA && okB) return da.CompareTo(db);
        return string.Compare(a, b, StringComparison.Ordinal);
    }
}
