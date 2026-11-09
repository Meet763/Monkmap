using System.Text;
using System.Text.Json;

namespace WorkflowPlatform.Api.Engine.Nodes;

/// <summary>Makes an HTTP request and emits the response. URL/body support {{ }} expressions.</summary>
public class HttpRequestNode : INode
{
    public NodeDefinition Definition => new()
    {
        Type = "HttpRequest",
        DisplayName = "HTTP Request",
        Group = "Logic",
        Icon = "globe",
        OutputPorts = new[] { "main" },
        Parameters = new()
        {
            new NodeParameter
            {
                Name = "method", DisplayName = "Method", Type = "options",
                Options = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }, Default = "GET"
            },
            new NodeParameter
            {
                Name = "url", DisplayName = "URL", Type = "string", Required = true,
                Placeholder = "https://api.example.com/items/{{$json.id}}"
            },
            new NodeParameter
            {
                Name = "headers", DisplayName = "Headers (JSON object)", Type = "json",
                Default = "{}", Placeholder = "{ \"Authorization\": \"Bearer ...\" }"
            },
            new NodeParameter
            {
                Name = "body", DisplayName = "Body (JSON, for POST/PUT/PATCH)", Type = "json",
                Default = "", Placeholder = "{ \"key\": \"value\" }"
            }
        }
    };

    public async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx)
    {
        var method = ctx.GetString("method", "GET").ToUpperInvariant();
        var url = ctx.ResolveString("url");
        if (string.IsNullOrWhiteSpace(url))
            return NodeResult.Fail("URL is required.");

        var factory = ctx.Services.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("node-http");

        HttpRequestMessage request;
        try
        {
            request = new HttpRequestMessage(new HttpMethod(method), url);
        }
        catch (Exception ex)
        {
            return NodeResult.Fail($"Invalid request: {ex.Message}");
        }

        // Headers
        var headersRaw = ctx.ResolveString("headers", "{}");
        if (!string.IsNullOrWhiteSpace(headersRaw) && headersRaw != "{}")
        {
            try
            {
                using var hd = JsonDocument.Parse(headersRaw);
                foreach (var h in hd.RootElement.EnumerateObject())
                    request.Headers.TryAddWithoutValidation(h.Name, JsonHelpers.AsString(h.Value));
            }
            catch (JsonException ex)
            {
                return NodeResult.Fail($"Invalid headers JSON: {ex.Message}");
            }
        }

        // Body
        var bodyRaw = ctx.ResolveString("body");
        if (!string.IsNullOrWhiteSpace(bodyRaw) &&
            method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent(bodyRaw, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await client.SendAsync(request, ctx.CancellationToken);
            var text = await response.Content.ReadAsStringAsync(ctx.CancellationToken);

            JsonElement bodyElement;
            try { bodyElement = JsonHelpers.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text); }
            catch { bodyElement = JsonHelpers.FromObject(text); }

            var output = JsonHelpers.FromObject(new
            {
                statusCode = (int)response.StatusCode,
                ok = response.IsSuccessStatusCode,
                body = bodyElement
            });

            if (!response.IsSuccessStatusCode)
                return new NodeResult
                {
                    Success = false,
                    Error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                    OutputItems = new List<DataItem> { new(output) }
                };

            return NodeResult.Ok(output);
        }
        catch (TaskCanceledException)
        {
            return NodeResult.Fail("Request timed out or was canceled.");
        }
        catch (Exception ex)
        {
            return NodeResult.Fail($"Request failed: {ex.Message}");
        }
    }
}
