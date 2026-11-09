using System.Text.Json;

namespace WorkflowPlatform.Api.Integrations;

/// <summary>What an action gets to run: an already-valid access token + an HTTP factory.</summary>
public sealed class IntegrationActionContext
{
    public required string AccessToken { get; init; }
    public required IHttpClientFactory HttpClientFactory { get; init; }
    public required ILogger Logger { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// One reusable action on a provider (e.g. google.calendar.createEvent). Define it once; it is
/// then surfaced three ways with no extra work: as a workflow node, as an AI-agent tool, and
/// (later) as an MCP tool. The action only makes the API call — token/refresh is handled for it.
/// </summary>
public interface IIntegrationAction
{
    /// <summary>Unique key, dotted, e.g. "google.calendar.createEvent".</summary>
    string Key { get; }

    /// <summary>Provider key this action belongs to, e.g. "google".</summary>
    string Provider { get; }

    string DisplayName { get; }
    string Description { get; }

    /// <summary>JSON Schema (object) for the action's input.</summary>
    JsonElement InputSchema { get; }

    Task<JsonElement> ExecuteAsync(IntegrationActionContext ctx, JsonElement input);
}
