using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Engine;
using WorkflowPlatform.Api.Entities;
using WorkflowPlatform.Api.Services;

namespace WorkflowPlatform.Api.Controllers;

/// <summary>
/// Public HTTP endpoint that fronts a workflow's Webhook Trigger node. Any request to
/// /api/webhooks/{workflowId}/{path?} starts a run seeded with the request payload.
/// Returns 202 { executionId } (Option 1: async + poll); the caller polls
/// GET /api/executions/{id} for the result.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IExecutionQueue _queue;

    public WebhooksController(AppDbContext db, IExecutionQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE")]
    [Route("{workflowId:guid}/{*path}")]
    public async Task<IActionResult> Handle(Guid workflowId, string? path)
    {
        var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == workflowId);
        if (workflow is null || workflow.ActiveVersionId is null)
            return NotFound(new { message = "No active workflow for this webhook URL." });

        // A locked workflow fires nothing, including from webhooks.
        if (await _db.WorkflowLocks.AnyAsync(l => l.WorkflowId == workflowId))
            return StatusCode(423, new { message = "This workflow is locked. Unlock it to make changes." });

        var version = await _db.WorkflowVersions
            .FirstOrDefaultAsync(v => v.Id == workflow.ActiveVersionId.Value);
        if (version is null)
            return NotFound(new { message = "Active workflow version not found." });

        WorkflowGraph graph;
        try { graph = JsonSerializer.Deserialize<WorkflowGraph>(version.GraphJson) ?? new(); }
        catch { return StatusCode(500, new { message = "Workflow graph is invalid." }); }

        var hook = graph.Nodes.FirstOrDefault(n =>
            n.Type.Equals("WebhookTrigger", StringComparison.OrdinalIgnoreCase));
        if (hook is null)
            return NotFound(new { message = "This workflow has no Webhook Trigger node." });

        // Method check.
        var allowed = ParamString(hook, "method", "POST");
        if (!allowed.Equals("ANY", StringComparison.OrdinalIgnoreCase) &&
            !allowed.Equals(Request.Method, StringComparison.OrdinalIgnoreCase))
            return StatusCode(405, new { message = $"This webhook accepts {allowed}." });

        // Path check (only when the node defines one).
        var expectedPath = ParamString(hook, "path", "").Trim().Trim('/');
        var actualPath = (path ?? "").Trim().Trim('/');
        if (!string.IsNullOrEmpty(expectedPath) &&
            !expectedPath.Equals(actualPath, StringComparison.OrdinalIgnoreCase))
            return NotFound(new { message = "Webhook path does not match." });

        // Token check (only when the node defines one).
        var expectedToken = ParamString(hook, "token", "");
        if (!string.IsNullOrEmpty(expectedToken))
        {
            var provided = Request.Query["token"].ToString();
            if (string.IsNullOrEmpty(provided))
                provided = Request.Headers["X-Webhook-Token"].ToString();
            if (!string.Equals(provided, expectedToken, StringComparison.Ordinal))
                return Unauthorized(new { message = "Invalid or missing webhook token." });
        }

        var payload = await BuildPayloadAsync();

        var execution = new Execution
        {
            WorkflowId = workflow.Id,
            WorkflowVersionId = version.Id,
            Status = ExecutionStatus.Queued,
            TriggerType = TriggerType.Webhook,
            TriggerDataJson = payload
        };
        _db.Executions.Add(execution);
        await _db.SaveChangesAsync();
        await _queue.EnqueueAsync(execution.Id);

        return Accepted(new { executionId = execution.Id });
    }

    private async Task<string> BuildPayloadAsync()
    {
        string bodyText = "";
        try
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            bodyText = await reader.ReadToEndAsync();
        }
        catch { /* no readable body */ }

        JsonElement bodyElement;
        if (string.IsNullOrWhiteSpace(bodyText))
            bodyElement = JsonHelpers.EmptyObject();
        else
        {
            try { bodyElement = JsonHelpers.Parse(bodyText); }
            catch { bodyElement = JsonHelpers.FromObject(bodyText); }
        }

        var query = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

        var payload = new
        {
            method = Request.Method,
            path = Request.Path.ToString(),
            query,
            headers,
            body = bodyElement
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string ParamString(GraphNode node, string key, string fallback)
    {
        if (node.Parameters.TryGetValue(key, out var v))
        {
            var s = JsonHelpers.AsString(v);
            return string.IsNullOrEmpty(s) ? fallback : s;
        }
        return fallback;
    }
}
