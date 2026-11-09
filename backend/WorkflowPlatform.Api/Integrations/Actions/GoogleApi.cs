using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WorkflowPlatform.Api.Engine;

namespace WorkflowPlatform.Api.Integrations.Actions;

/// <summary>Shared helpers for calling Google REST APIs with a bearer token.</summary>
internal static class GoogleApi
{
    public static async Task<JsonElement> CallAsync(
        IntegrationActionContext ctx, HttpMethod method, string url, object? body = null)
    {
        var client = ctx.HttpClientFactory.CreateClient("node-http");
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AccessToken);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var res = await client.SendAsync(req, ctx.CancellationToken);
        var text = await res.Content.ReadAsStringAsync(ctx.CancellationToken);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google API {(int)res.StatusCode}: {text}");

        return JsonHelpers.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    public static string Str(JsonElement input, string key, string fallback = "") =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(key, out var v)
            ? JsonHelpers.AsString(v)
            : fallback;
}
