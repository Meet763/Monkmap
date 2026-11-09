using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Integrations;

namespace WorkflowPlatform.Api.Engine.Nodes;

/// <summary>
/// Runs a reusable integration action (e.g. Google Calendar create event) using a connected
/// account. Pick the action, paste the Connection id (from the Connections page), and provide the
/// input JSON. Token refresh and API calls are handled centrally — no integration code here.
/// </summary>
public sealed class IntegrationActionNode : INode
{
    private readonly IntegrationActionRegistry _actions;

    public IntegrationActionNode(IntegrationActionRegistry actions) => _actions = actions;

    public NodeDefinition Definition => new()
    {
        Type = "IntegrationAction",
        DisplayName = "Integration Action",
        Group = "Apps",
        Icon = "plug",
        OutputPorts = new[] { "main" },
        Parameters = new()
        {
            new NodeParameter
            {
                Name = "action", DisplayName = "Action", Type = "options",
                Options = _actions.All.Select(a => a.Key).DefaultIfEmpty("(none registered)").ToArray(),
                Default = _actions.All.Select(a => a.Key).FirstOrDefault()
            },
            new NodeParameter
            {
                Name = "connectionId", DisplayName = "Connection id", Type = "string", Required = true,
                Placeholder = "copy from the Connections page"
            },
            new NodeParameter
            {
                Name = "input", DisplayName = "Input (JSON)", Type = "json", Default = "{}",
                Placeholder = "{ \"summary\": \"Meeting\", \"start\": \"...\", \"end\": \"...\" }"
            }
        }
    };

    public async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx)
    {
        var actionKey = ctx.GetString("action");
        if (!_actions.TryGet(actionKey, out var action))
            return NodeResult.Fail($"Unknown action '{actionKey}'.");

        if (!Guid.TryParse(ctx.ResolveString("connectionId"), out var connectionId))
            return NodeResult.Fail("A valid Connection id is required.");

        var db = ctx.Services.GetRequiredService<AppDbContext>();
        var connection = await db.Connections.FirstOrDefaultAsync(
            c => c.Id == connectionId &&
                 (ctx.OwnerUserId == null || c.UserId == ctx.OwnerUserId),
            ctx.CancellationToken);
        if (connection is null)
            return NodeResult.Fail("Connection not found (or not owned by this user).");
        if (!connection.Provider.Equals(action.Provider, StringComparison.OrdinalIgnoreCase))
            return NodeResult.Fail($"This connection is '{connection.Provider}', but the action needs '{action.Provider}'.");

        // Parse the template FIRST, then resolve {{ }} inside each string value. Resolving the raw
        // text would paste values (quotes, newlines from AI output) straight into the JSON and break it.
        JsonElement input;
        try
        {
            var template = JsonNode.Parse(ctx.GetString("input", "{}") is { Length: > 0 } s ? s : "{}");
            input = JsonHelpers.Parse(ResolveStrings(template, ctx).ToJsonString());
        }
        catch (Exception ex) { return NodeResult.Fail($"Input is not valid JSON: {ex.Message}"); }

        try
        {
            var tokenManager = ctx.Services.GetRequiredService<ITokenManager>();
            var token = await tokenManager.GetAccessTokenAsync(connection, ctx.CancellationToken);

            var actionCtx = new IntegrationActionContext
            {
                AccessToken = token,
                HttpClientFactory = ctx.Services.GetRequiredService<IHttpClientFactory>(),
                Logger = ctx.Logger,
                CancellationToken = ctx.CancellationToken
            };
            var result = await action.ExecuteAsync(actionCtx, input);
            return NodeResult.Ok(result);
        }
        catch (Exception ex)
        {
            return NodeResult.Fail(ex.Message);
        }
    }

    /// <summary>Copy the JSON tree, replacing {{ }} expressions inside every string value only.</summary>
    private static JsonNode? ResolveStrings(JsonNode? node, NodeExecutionContext ctx)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var text):
                return JsonValue.Create(ExpressionResolver.Resolve(text, ctx.FirstInput, ctx.NodeOutputs));
            case JsonObject o:
                var obj = new JsonObject();
                foreach (var kv in o)
                    obj[kv.Key] = ResolveStrings(kv.Value, ctx);
                return obj;
            case JsonArray a:
                var arr = new JsonArray();
                foreach (var item in a)
                    arr.Add(ResolveStrings(item, ctx));
                return arr;
            default:
                return node?.DeepClone();
        }
    }
}
