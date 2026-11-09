using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WorkflowPlatform.Api.Engine.Providers;

/// <summary>
/// Speaks the OpenAI Chat Completions API (POST {baseUrl}/chat/completions), including its
/// tool-calling format. Covers OpenAI and — because they copy the same wire format — Ollama,
/// LM Studio, vLLM, llama.cpp server, LocalAI, Together, Groq, OpenRouter and most in-house
/// inference servers. For those, the user just sets a custom Base URL and model name.
/// </summary>
public sealed class OpenAiCompatibleProvider : IChatModelProvider
{
    public string Name => "OpenAI / Compatible";

    public IReadOnlyCollection<string> Keys =>
        new[] { "openai", "compatible", "custom", "ollama", "selfhosted", "self-hosted", "local" };

    public string DefaultBaseUrl => "https://api.openai.com/v1";

    public HttpRequestMessage BuildRequest(string baseUrl, string? apiKey, ChatCompletionRequest req)
    {
        var messages = new List<Dictionary<string, object?>>();
        foreach (var m in req.Messages)
        {
            switch (m.Role)
            {
                case ChatRole.System:
                    messages.Add(new() { ["role"] = "system", ["content"] = m.Content ?? "" });
                    break;
                case ChatRole.User:
                    messages.Add(new() { ["role"] = "user", ["content"] = m.Content ?? "" });
                    break;
                case ChatRole.Assistant:
                    var am = new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = m.Content };
                    if (m.ToolCalls is { Count: > 0 })
                        am["tool_calls"] = m.ToolCalls.Select(tc => new
                        {
                            id = tc.Id,
                            type = "function",
                            function = new { name = tc.Name, arguments = tc.ArgumentsJson }
                        }).ToArray();
                    messages.Add(am);
                    break;
                case ChatRole.Tool:
                    messages.Add(new()
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = m.ToolCallId ?? "",
                        ["content"] = m.Content ?? ""
                    });
                    break;
            }
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = req.Model,
            ["messages"] = messages,
            ["temperature"] = req.Temperature,
            ["max_tokens"] = req.MaxTokens
        };
        if (req.Tools.Count > 0)
        {
            body["tools"] = req.Tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.Parameters }
            }).ToArray();
            body["tool_choice"] = "auto";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    public ChatCompletionResult ParseResult(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message))
        {
            return new ChatCompletionResult("", Array.Empty<ToolCall>());
        }

        string? text = message.TryGetProperty("content", out var content) &&
                       content.ValueKind == JsonValueKind.String
            ? content.GetString()
            : null;

        var calls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCalls) &&
            toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCalls.EnumerateArray())
            {
                var id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                if (!tc.TryGetProperty("function", out var fn)) continue;
                var name = fn.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                var args = fn.TryGetProperty("arguments", out var aEl) ? JsonHelpers.AsString(aEl) : "{}";
                if (!string.IsNullOrEmpty(name))
                    calls.Add(new ToolCall(id, name, args));
            }
        }

        return new ChatCompletionResult(text, calls);
    }
}
