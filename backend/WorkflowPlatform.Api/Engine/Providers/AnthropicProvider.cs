using System.Text;
using System.Text.Json;

namespace WorkflowPlatform.Api.Engine.Providers;

/// <summary>
/// Speaks Anthropic's Messages API (POST {baseUrl}/messages), including tool use. The shape
/// differs from OpenAI: the system prompt is a top-level field, tool calls are "tool_use" content
/// blocks, and tool results are sent back as "tool_result" blocks inside a user turn.
/// </summary>
public sealed class AnthropicProvider : IChatModelProvider
{
    public string Name => "Anthropic (Claude)";
    public IReadOnlyCollection<string> Keys => new[] { "anthropic", "claude" };
    public string DefaultBaseUrl => "https://api.anthropic.com/v1";

    public HttpRequestMessage BuildRequest(string baseUrl, string? apiKey, ChatCompletionRequest req)
    {
        var systemText = string.Join("\n\n",
            req.Messages.Where(m => m.Role == ChatRole.System && !string.IsNullOrWhiteSpace(m.Content))
                        .Select(m => m.Content));

        var messages = new List<Dictionary<string, object?>>();
        foreach (var m in req.Messages)
        {
            switch (m.Role)
            {
                case ChatRole.User:
                    messages.Add(new()
                    {
                        ["role"] = "user",
                        ["content"] = new object[] { new { type = "text", text = m.Content ?? "" } }
                    });
                    break;

                case ChatRole.Assistant:
                    var blocks = new List<object>();
                    if (!string.IsNullOrWhiteSpace(m.Content))
                        blocks.Add(new { type = "text", text = m.Content });
                    if (m.ToolCalls is { Count: > 0 })
                        foreach (var tc in m.ToolCalls)
                            blocks.Add(new
                            {
                                type = "tool_use",
                                id = tc.Id,
                                name = tc.Name,
                                input = SafeParse(tc.ArgumentsJson)
                            });
                    messages.Add(new() { ["role"] = "assistant", ["content"] = blocks });
                    break;

                case ChatRole.Tool:
                    // Anthropic requires tool results inside a user turn.
                    messages.Add(new()
                    {
                        ["role"] = "user",
                        ["content"] = new object[]
                        {
                            new { type = "tool_result", tool_use_id = m.ToolCallId ?? "", content = m.Content ?? "" }
                        }
                    });
                    break;
            }
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = req.Model,
            ["max_tokens"] = req.MaxTokens,
            ["temperature"] = req.Temperature,
            ["messages"] = messages
        };
        if (!string.IsNullOrWhiteSpace(systemText))
            body["system"] = systemText;
        if (req.Tools.Count > 0)
            body["tools"] = req.Tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                input_schema = t.Parameters
            }).ToArray();

        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        return request;
    }

    public ChatCompletionResult ParseResult(JsonElement response)
    {
        var sb = new StringBuilder();
        var calls = new List<ToolCall>();

        if (response.ValueKind == JsonValueKind.Object &&
            response.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var typeEl)) continue;
                var type = typeEl.GetString();
                if (type == "text" && block.TryGetProperty("text", out var textEl))
                    sb.Append(textEl.GetString());
                else if (type == "tool_use")
                {
                    var id = block.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    var name = block.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                    var input = block.TryGetProperty("input", out var inEl) ? inEl.GetRawText() : "{}";
                    if (!string.IsNullOrEmpty(name))
                        calls.Add(new ToolCall(id, name, input));
                }
            }
        }

        return new ChatCompletionResult(sb.ToString(), calls);
    }

    private static JsonElement SafeParse(string json)
    {
        try { return JsonHelpers.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch { return JsonHelpers.EmptyObject(); }
    }
}
