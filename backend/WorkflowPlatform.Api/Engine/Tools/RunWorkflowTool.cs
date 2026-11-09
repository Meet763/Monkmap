using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Entities;

namespace WorkflowPlatform.Api.Engine.Tools;

/// <summary>
/// Lets the agent run one of the user's own saved workflows and read its result. This is how the
/// agent "executes an action" — the target workflow can contain HTTP calls, DB writes, integration
/// actions, etc. The sub-workflow runs synchronously (inline) so the agent gets the output back.
/// </summary>
public sealed class RunWorkflowTool : IAgentTool
{
    public string Name => "run_workflow";

    public string Description =>
        "Run one of the user's saved workflows by its id and return its final output. " +
        "Use this to perform an action that is implemented as a workflow.";

    public JsonElement ParametersSchema => JsonHelpers.FromObject(new
    {
        type = "object",
        properties = new
        {
            workflowId = new { type = "string", description = "The GUID id of the workflow to run" },
            input = new { type = "object", description = "Optional input data passed to the workflow's trigger" }
        },
        required = new[] { "workflowId" }
    });

    public async Task<string> InvokeAsync(JsonElement args, AgentToolContext ctx)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("workflowId", out var idEl) ||
            !Guid.TryParse(JsonHelpers.AsString(idEl), out var workflowId))
            return Err("workflowId must be a valid GUID.");

        if (ctx.CurrentWorkflowId == workflowId)
            return Err("A workflow cannot run itself (self-recursion blocked).");

        var db = ctx.Services.GetRequiredService<AppDbContext>();

        var workflow = await db.Workflows
            .FirstOrDefaultAsync(w => w.Id == workflowId &&
                                      (ctx.OwnerUserId == null || w.UserId == ctx.OwnerUserId),
                                 ctx.CancellationToken);
        if (workflow is null)
            return Err("Workflow not found (or not owned by this user).");
        if (workflow.ActiveVersionId is null)
            return Err("That workflow has no active version to run.");
        if (await db.WorkflowLocks.AnyAsync(l => l.WorkflowId == workflowId, ctx.CancellationToken))
            return Err("That workflow is locked, so it cannot run right now.");

        string? inputJson = args.TryGetProperty("input", out var inEl) &&
                            inEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? inEl.GetRawText()
            : null;

        var execution = new Execution
        {
            WorkflowId = workflow.Id,
            WorkflowVersionId = workflow.ActiveVersionId.Value,
            Status = ExecutionStatus.Queued,
            TriggerType = TriggerType.Manual,
            TriggerDataJson = inputJson
        };
        db.Executions.Add(execution);
        await db.SaveChangesAsync(ctx.CancellationToken);

        // Run inline so we can return the result to the agent.
        var executor = ctx.Services.GetRequiredService<WorkflowExecutor>();
        await executor.RunAsync(execution.Id, ctx.CancellationToken);

        // Fresh reads (the executor wrote these rows in a different DbContext scope).
        var final = await db.Executions.AsNoTracking()
            .Where(e => e.Id == execution.Id)
            .Select(e => new { e.Status, e.Error })
            .FirstAsync(ctx.CancellationToken);

        var lastOutput = await db.NodeExecutions.AsNoTracking()
            .Where(n => n.ExecutionId == execution.Id && n.OutputJson != null)
            .OrderByDescending(n => n.FinishedAt)
            .Select(n => n.OutputJson)
            .FirstOrDefaultAsync(ctx.CancellationToken);

        JsonElement output;
        try { output = JsonHelpers.Parse(string.IsNullOrWhiteSpace(lastOutput) ? "null" : lastOutput!); }
        catch { output = JsonHelpers.FromObject(lastOutput ?? ""); }

        return JsonSerializer.Serialize(new
        {
            executionId = execution.Id,
            status = final.Status.ToString(),
            error = final.Error,
            output
        });
    }

    private static string Err(string message) => JsonSerializer.Serialize(new { error = message });
}
