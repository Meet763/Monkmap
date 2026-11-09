using System.Text.Json;

namespace WorkflowPlatform.Api.Engine;

public static class JsonHelpers
{
    public static JsonElement EmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    public static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static JsonElement FromObject(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return Parse(json);
    }

    /// <summary>Resolve a dotted path like "data.user.id" against a JSON element.</summary>
    public static JsonElement? ResolvePath(JsonElement root, string path)
    {
        var current = root;
        foreach (var rawSegment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0) continue;

            if (current.ValueKind == JsonValueKind.Object &&
                current.TryGetProperty(segment, out var next))
            {
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array &&
                     int.TryParse(segment, out var idx) &&
                     idx >= 0 && idx < current.GetArrayLength())
            {
                current = current[idx];
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    public static string AsString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.Number => el.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => el.GetRawText()
    };
}
