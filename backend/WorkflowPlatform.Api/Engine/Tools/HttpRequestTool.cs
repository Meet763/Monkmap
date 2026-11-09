using System.Text;
using System.Text.Json;

namespace WorkflowPlatform.Api.Engine.Tools;

/// <summary>Lets the agent make an HTTP request to any URL and read the response.</summary>
public sealed class HttpRequestTool : IAgentTool
{
    public string Name => "http_request";

    public string Description =>
        "Make an HTTP request to a URL and return the status code and response body. " +
        "Use for calling external APIs or fetching data from the web.";

    public JsonElement ParametersSchema => JsonHelpers.FromObject(new
    {
        type = "object",
        properties = new
        {
            method = new { type = "string", @enum = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" } },
            url = new { type = "string", description = "Full URL including https://" },
            headers = new { type = "object", description = "Optional request headers as key/value pairs" },
            body = new { type = "string", description = "Optional request body (for POST/PUT/PATCH)" }
        },
        required = new[] { "url" }
    });

    public async Task<string> InvokeAsync(JsonElement args, AgentToolContext ctx)
    {
        var method = GetString(args, "method", "GET").ToUpperInvariant();
        var url = GetString(args, "url");
        if (string.IsNullOrWhiteSpace(url))
            return "{\"error\":\"url is required\"}";

        var factory = ctx.Services.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("node-http");

        HttpRequestMessage request;
        try { request = new HttpRequestMessage(new HttpMethod(method), url); }
        catch (Exception ex) { return Err($"invalid request: {ex.Message}"); }

        if (args.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
            foreach (var h in headers.EnumerateObject())
                request.Headers.TryAddWithoutValidation(h.Name, JsonHelpers.AsString(h.Value));

        var body = GetString(args, "body");
        if (!string.IsNullOrWhiteSpace(body) && method is "POST" or "PUT" or "PATCH")
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using var response = await client.SendAsync(request, ctx.CancellationToken);
            var text = await response.Content.ReadAsStringAsync(ctx.CancellationToken);
            // Trim very long bodies so we don't blow the model's context.
            if (text.Length > 8000) text = text[..8000] + "…(truncated)";
            return JsonSerializer.Serialize(new { status = (int)response.StatusCode, body = text });
        }
        catch (Exception ex)
        {
            return Err($"request failed: {ex.Message}");
        }
    }

    private static string GetString(JsonElement args, string key, string fallback = "") =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(key, out var v)
            ? JsonHelpers.AsString(v)
            : fallback;

    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });
}
