using System.Globalization;
using System.Text.Json;
using WorkflowPlatform.Api.Engine.Providers;
using WorkflowPlatform.Api.Engine.Tools;

namespace WorkflowPlatform.Api.Engine.Nodes;

/// <summary>
/// Calls a chat/LLM model as a workflow step. Two modes:
///   • Chat  — one-shot: prompt in, text out.
///   • Agent — the model is given tools (http_request, run_workflow, ...) and loops: it decides
///             which tools to call, we run them, feed results back, and repeat until it answers
///             or hits the iteration cap. This is how the agent "decides and acts".
///
/// Works with hosted providers (OpenAI, Anthropic) and self-hosted / in-house models via an
/// OpenAI-compatible Base URL. Prompts support {{ }} expressions.
///
/// Output: { text, model, provider, mode, steps }.
///
/// MVP note: the API key is stored on the node (workflow graph), like the Database node. Move it
/// to the encrypted credentials store before exposing this to untrusted users — see plan §5 / §13.
/// </summary>
public sealed class AiAgentNode : INode
{
    public NodeDefinition Definition => new()
    {
        Type = "AiAgent",
        DisplayName = "AI Agent",
        Group = "Apps",
        Icon = "sparkles",
        OutputPorts = new[] { "main" },
        Parameters = new()
        {
            new NodeParameter
            {
                Name = "mode", DisplayName = "Mode", Type = "options",
                Options = new[] { "Chat", "Agent" }, Default = "Chat"
            },
            new NodeParameter
            {
                Name = "provider", DisplayName = "Provider", Type = "options",
                Options = new[] { "OpenAI", "Anthropic", "Ollama", "Custom" },
                Default = "OpenAI"
            },
            new NodeParameter
            {
                Name = "baseUrl", DisplayName = "Base URL (blank = provider default)", Type = "string",
                Placeholder = "http://localhost:11434/v1  (your in-house / self-hosted endpoint)"
            },
            new NodeParameter
            {
                Name = "model", DisplayName = "Model", Type = "string", Required = true,
                Placeholder = "gpt-4o  /  claude-3-5-sonnet-latest  /  llama3  /  my-finetuned-model"
            },
            new NodeParameter
            {
                Name = "apiKey", DisplayName = "API key (blank for local models)", Type = "secret",
                Placeholder = "sk-..."
            },
            new NodeParameter
            {
                Name = "system", DisplayName = "System prompt", Type = "text",
                Placeholder = "You are a helpful assistant."
            },
            new NodeParameter
            {
                Name = "prompt", DisplayName = "User prompt", Type = "text", Required = true,
                Placeholder = "Summarise this: {{$json.text}}"
            },
            new NodeParameter
            {
                Name = "temperature", DisplayName = "Temperature", Type = "number", Default = "0.7"
            },
            new NodeParameter
            {
                Name = "maxTokens", DisplayName = "Max tokens", Type = "number", Default = "1024"
            },
            new NodeParameter
            {
                Name = "maxIterations", DisplayName = "Max tool steps (Agent mode)", Type = "number",
                Default = "6"
            }
        }
    };

    public async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx)
    {
        var providerKey = ctx.GetString("provider", "OpenAI");
        var model = ctx.ResolveString("model");
        var prompt = ctx.ResolveString("prompt");

        if (string.IsNullOrWhiteSpace(model)) return NodeResult.Fail("Model is required.");
        if (string.IsNullOrWhiteSpace(prompt)) return NodeResult.Fail("User prompt is required.");

        var registry = ctx.Services.GetRequiredService<ChatModelProviderRegistry>();
        if (!registry.TryGet(providerKey, out var provider))
            return NodeResult.Fail($"Unknown provider '{providerKey}'.");

        var baseUrl = ctx.ResolveString("baseUrl");
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = provider.DefaultBaseUrl;

        var apiKey = ctx.GetString("apiKey");
        var system = ctx.ResolveString("system");
        var temperature = ParseDouble(ctx.GetString("temperature", "0.7"), 0.7);
        var maxTokens = ParseInt(ctx.GetString("maxTokens", "1024"), 1024);
        var isAgent = ctx.GetString("mode", "Chat").Equals("Agent", StringComparison.OrdinalIgnoreCase);

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(system)) messages.Add(ChatMessage.System(system));
        messages.Add(ChatMessage.User(prompt));

        var client = ctx.Services.GetRequiredService<IHttpClientFactory>().CreateClient("node-ai");

        // ---- Chat mode: single call, no tools ----
        if (!isAgent)
        {
            var req = new ChatCompletionRequest(model, messages, Array.Empty<ToolSpec>(), temperature, maxTokens);
            var (ok, raw, err) = await CallAsync(client, provider, baseUrl, apiKey, req, ctx.CancellationToken);
            if (!ok) return new NodeResult { Success = false, Error = err, OutputItems = new List<DataItem> { new(raw) } };

            var result = provider.ParseResult(raw);
            return NodeResult.Ok(JsonHelpers.FromObject(new
            {
                text = result.Text ?? "",
                model,
                provider = provider.Name,
                mode = "chat"
            }));
        }

        // ---- Agent mode: tool-calling loop ----
        var toolRegistry = ctx.Services.GetRequiredService<ToolRegistry>();
        var tools = toolRegistry.All;
        var toolSpecs = tools.Select(t => new ToolSpec(t.Name, t.Description, t.ParametersSchema)).ToList();

        var toolCtx = new AgentToolContext
        {
            Services = ctx.Services,
            Logger = ctx.Logger,
            CancellationToken = ctx.CancellationToken,
            OwnerUserId = ctx.OwnerUserId,
            CurrentWorkflowId = ctx.WorkflowId
        };

        var maxIterations = Math.Clamp(ParseInt(ctx.GetString("maxIterations", "6"), 6), 1, 20);
        var steps = new List<object>();
        string finalText = "";

        for (var i = 0; i < maxIterations; i++)
        {
            var req = new ChatCompletionRequest(model, messages, toolSpecs, temperature, maxTokens);
            var (ok, raw, err) = await CallAsync(client, provider, baseUrl, apiKey, req, ctx.CancellationToken);
            if (!ok) return new NodeResult { Success = false, Error = err, OutputItems = new List<DataItem> { new(raw) } };

            var result = provider.ParseResult(raw);
            finalText = result.Text ?? finalText;

            if (!result.WantsTools)
                break; // model gave its final answer

            // Record the assistant's tool-call turn, then run each tool and feed results back.
            messages.Add(ChatMessage.Assistant(result.Text, result.ToolCalls));
            foreach (var call in result.ToolCalls)
            {
                var argsEl = SafeParse(call.ArgumentsJson);
                string toolResult;
                if (toolRegistry.TryGet(call.Name, out var tool))
                {
                    try { toolResult = await tool.InvokeAsync(argsEl, toolCtx); }
                    catch (Exception ex) { toolResult = $"{{\"error\":\"tool threw: {ex.Message}\"}}"; }
                }
                else
                {
                    toolResult = $"{{\"error\":\"unknown tool '{call.Name}'\"}}";
                }

                steps.Add(new { tool = call.Name, args = argsEl, result = Trim(toolResult) });
                messages.Add(ChatMessage.ToolResult(call.Id, call.Name, toolResult));
            }
        }

        return NodeResult.Ok(JsonHelpers.FromObject(new
        {
            text = finalText,
            model,
            provider = provider.Name,
            mode = "agent",
            steps
        }));
    }

    private static async Task<(bool ok, JsonElement raw, string? error)> CallAsync(
        HttpClient client, IChatModelProvider provider, string baseUrl, string? apiKey,
        ChatCompletionRequest req, CancellationToken ct)
    {
        try
        {
            using var request = provider.BuildRequest(baseUrl, apiKey, req);
            using var response = await client.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);

            JsonElement raw;
            try { raw = JsonHelpers.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text); }
            catch { raw = JsonHelpers.FromObject(text); }

            if (!response.IsSuccessStatusCode)
                return (false, raw, $"Model API error: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            return (true, raw, null);
        }
        catch (TaskCanceledException)
        {
            return (false, JsonHelpers.EmptyObject(), "Model request timed out or was canceled.");
        }
        catch (Exception ex)
        {
            return (false, JsonHelpers.EmptyObject(), $"Model request failed: {ex.Message}");
        }
    }

    private static JsonElement SafeParse(string json)
    {
        try { return JsonHelpers.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch { return JsonHelpers.EmptyObject(); }
    }

    private static string Trim(string s) => s.Length > 2000 ? s[..2000] + "…" : s;

    private static double ParseDouble(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static int ParseInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
