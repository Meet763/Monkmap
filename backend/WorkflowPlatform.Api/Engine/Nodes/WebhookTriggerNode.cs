namespace WorkflowPlatform.Api.Engine.Nodes;

/// <summary>
/// Exposes a workflow as an HTTP API endpoint. The webhook controller receives a request at
/// /api/webhooks/{workflowId}/{path?}, builds a { method, path, query, headers, body } payload,
/// and starts a run seeded with it. This node simply emits that payload to downstream nodes,
/// which read it with {{$json.body.field}}, {{$json.query.x}}, etc.
///
/// The optional token lives in the node parameters (the workflow graph) for the MVP — same
/// posture as the Database / AI nodes. Leave it blank for open local testing.
/// </summary>
public class WebhookTriggerNode : INode
{
    public NodeDefinition Definition => new()
    {
        Type = "WebhookTrigger",
        DisplayName = "Webhook Trigger",
        Group = "Triggers",
        Icon = "webhook",
        IsTrigger = true,
        OutputPorts = new[] { "main" },
        Parameters = new()
        {
            new NodeParameter
            {
                Name = "method", DisplayName = "HTTP method", Type = "options",
                Options = new[] { "ANY", "GET", "POST", "PUT", "PATCH", "DELETE" }, Default = "POST"
            },
            new NodeParameter
            {
                Name = "path", DisplayName = "Path (optional)", Type = "string",
                Placeholder = "order-created"
            },
            new NodeParameter
            {
                Name = "token", DisplayName = "Secret token (optional)", Type = "secret",
                Placeholder = "blank = open for testing"
            }
        }
    };

    public Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx)
    {
        var item = ctx.InputItems.Count > 0 ? ctx.InputItems[0].Json : JsonHelpers.EmptyObject();
        return Task.FromResult(NodeResult.Ok(item));
    }
}
