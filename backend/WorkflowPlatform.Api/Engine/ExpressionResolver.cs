using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkflowPlatform.Api.Engine;

/// <summary>
/// Minimal expression engine (MVP). Supports templates like:
///   {{$json.user.id}}                -> field from the current node's first input
///   {{$node.NodeId.data.value}}      -> field from a previous node's first output item
/// Anything unmatched resolves to an empty string.
/// </summary>
public static class ExpressionResolver
{
    private static readonly Regex Token = new(@"\{\{\s*(.*?)\s*\}\}", RegexOptions.Compiled);

    public static string Resolve(
        string template,
        JsonElement currentInput,
        IReadOnlyDictionary<string, JsonElement> nodeOutputs)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("{{")) return template;

        return Token.Replace(template, m =>
        {
            var expr = m.Groups[1].Value.Trim();
            var value = Evaluate(expr, currentInput, nodeOutputs);
            return value is null ? "" : JsonHelpers.AsString(value.Value);
        });
    }

    private static JsonElement? Evaluate(
        string expr,
        JsonElement currentInput,
        IReadOnlyDictionary<string, JsonElement> nodeOutputs)
    {
        if (expr.StartsWith("$json"))
        {
            var path = expr.Length > 5 ? expr[5..].TrimStart('.') : "";
            return path.Length == 0 ? currentInput : JsonHelpers.ResolvePath(currentInput, path);
        }

        if (expr.StartsWith("$node"))
        {
            // $node.NodeId.rest  or  $node["NodeId"].rest
            var rest = expr[5..].TrimStart('.');
            string nodeId;
            string path;

            if (rest.StartsWith("[\"") || rest.StartsWith("['"))
            {
                var end = rest.IndexOf(rest[1], 2);
                if (end < 0) return null;
                nodeId = rest[2..end];
                path = rest[(end + 1)..].TrimStart(']').TrimStart('.');
            }
            else
            {
                var dot = rest.IndexOf('.');
                nodeId = dot < 0 ? rest : rest[..dot];
                path = dot < 0 ? "" : rest[(dot + 1)..];
            }

            if (!nodeOutputs.TryGetValue(nodeId, out var output)) return null;
            return path.Length == 0 ? output : JsonHelpers.ResolvePath(output, path);
        }

        return null;
    }
}
