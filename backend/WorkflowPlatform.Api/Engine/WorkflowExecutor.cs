using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Entities;
using WorkflowPlatform.Api.Hubs;

namespace WorkflowPlatform.Api.Engine;

/// <summary>Walks a workflow graph node-by-node, persisting results and pushing live updates.</summary>
public class WorkflowExecutor
{
    private readonly IServiceProvider _services;
    private readonly NodeRegistry _registry;
    private readonly IExecutionNotifier _notifier;
    private readonly ILogger<WorkflowExecutor> _logger;

    public WorkflowExecutor(
        IServiceProvider services,
        NodeRegistry registry,
        IExecutionNotifier notifier,
        ILogger<WorkflowExecutor> logger)
    {
        _services = services;
        _registry = registry;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task RunAsync(Guid executionId, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var execution = await db.Executions
            .Include(e => e.NodeExecutions)
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);
        if (execution is null) { _logger.LogWarning("Execution {Id} not found", executionId); return; }

        var version = await db.WorkflowVersions
            .FirstOrDefaultAsync(v => v.Id == execution.WorkflowVersionId, ct);
        if (version is null)
        {
            await FailExecution(db, execution, "Workflow version not found.", ct);
            return;
        }

        var ownerUserId = await db.Workflows
            .Where(w => w.Id == execution.WorkflowId)
            .Select(w => (Guid?)w.UserId)
            .FirstOrDefaultAsync(ct);

        execution.Status = ExecutionStatus.Running;
        execution.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await _notifier.ExecutionStarted(executionId);

        WorkflowGraph graph;
        try
        {
            graph = JsonSerializer.Deserialize<WorkflowGraph>(version.GraphJson) ?? new WorkflowGraph();
        }
        catch (Exception ex)
        {
            await FailExecution(db, execution, $"Invalid workflow graph: {ex.Message}", ct);
            return;
        }

        var nodesById = graph.Nodes.ToDictionary(n => n.Id);
        var start = graph.Nodes.FirstOrDefault(n =>
            _registry.TryGet(n.Type, out var def) && def.Definition.IsTrigger)
            ?? graph.Nodes.FirstOrDefault();

        if (start is null)
        {
            await FailExecution(db, execution, "Workflow has no nodes.", ct);
            return;
        }

        // Outputs collected per node (first item used for expression resolution).
        var nodeOutputs = new Dictionary<string, JsonElement>();
        var pendingInputs = new Dictionary<string, List<DataItem>>();
        var queued = new HashSet<string>();
        var executed = new HashSet<string>();
        var queue = new Queue<string>();

        // Seed the trigger with its payload.
        var triggerItem = string.IsNullOrWhiteSpace(execution.TriggerDataJson)
            ? JsonHelpers.EmptyObject()
            : SafeParse(execution.TriggerDataJson);
        pendingInputs[start.Id] = new List<DataItem> { new(triggerItem) };
        queue.Enqueue(start.Id);
        queued.Add(start.Id);

        var overallSuccess = true;
        string? firstError = null;

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var nodeId = queue.Dequeue();
            if (executed.Contains(nodeId)) continue;
            var node = nodesById[nodeId];
            executed.Add(nodeId);

            var inputs = pendingInputs.TryGetValue(nodeId, out var pi) ? pi : new List<DataItem>();

            var nodeExec = new NodeExecution
            {
                ExecutionId = execution.Id,
                NodeId = nodeId,
                NodeType = node.Type,
                Status = NodeExecutionStatus.Running,
                StartedAt = DateTime.UtcNow,
                InputJson = SerializeItems(inputs)
            };
            db.NodeExecutions.Add(nodeExec);
            await db.SaveChangesAsync(ct);
            await _notifier.NodeStarted(executionId, nodeId);

            NodeResult result;
            if (!_registry.TryGet(node.Type, out var impl))
            {
                result = NodeResult.Fail($"Unknown node type '{node.Type}'.");
            }
            else
            {
                var context = new NodeExecutionContext
                {
                    Node = node,
                    InputItems = inputs,
                    Services = scope.ServiceProvider,
                    Logger = _logger,
                    CancellationToken = ct,
                    NodeOutputs = nodeOutputs,
                    OwnerUserId = ownerUserId,
                    WorkflowId = execution.WorkflowId
                };
                try { result = await impl.ExecuteAsync(context); }
                catch (Exception ex) { result = NodeResult.Fail(ex.Message); }
            }

            nodeExec.FinishedAt = DateTime.UtcNow;
            nodeExec.OutputJson = SerializeItems(result.OutputItems);

            if (result.Success)
            {
                nodeExec.Status = NodeExecutionStatus.Succeeded;
                if (result.OutputItems.Count > 0)
                    nodeOutputs[nodeId] = result.OutputItems[0].Json;

                // Activate outgoing edges matching the chosen output port.
                foreach (var conn in graph.Connections.Where(c => c.Source == nodeId))
                {
                    var port = string.IsNullOrEmpty(conn.SourceHandle) ? "main" : conn.SourceHandle;
                    if (!string.Equals(port, result.OutputPort, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!nodesById.ContainsKey(conn.Target)) continue;

                    if (!pendingInputs.TryGetValue(conn.Target, out var list))
                        pendingInputs[conn.Target] = list = new List<DataItem>();
                    list.AddRange(result.OutputItems);

                    if (!queued.Contains(conn.Target) && !executed.Contains(conn.Target))
                    {
                        queue.Enqueue(conn.Target);
                        queued.Add(conn.Target);
                    }
                }
            }
            else
            {
                nodeExec.Status = NodeExecutionStatus.Failed;
                nodeExec.Error = result.Error;
                overallSuccess = false;
                firstError ??= $"Node '{nodeId}' failed: {result.Error}";
            }

            await db.SaveChangesAsync(ct);
            await _notifier.NodeCompleted(
                executionId, nodeId, nodeExec.Status.ToString(),
                result.OutputItems.Count > 0 ? result.OutputItems[0].Json : null);

            if (!result.Success) break; // stop-on-error policy (MVP)
        }

        execution.Status = overallSuccess ? ExecutionStatus.Succeeded : ExecutionStatus.Failed;
        execution.Error = firstError;
        execution.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await _notifier.ExecutionFinished(executionId, execution.Status.ToString(), firstError);
    }

    private async Task FailExecution(AppDbContext db, Execution e, string error, CancellationToken ct)
    {
        e.Status = ExecutionStatus.Failed;
        e.Error = error;
        e.FinishedAt = DateTime.UtcNow;
        e.StartedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await _notifier.ExecutionFinished(e.Id, e.Status.ToString(), error);
    }

    private static JsonElement SafeParse(string json)
    {
        try { return JsonHelpers.Parse(json); }
        catch { return JsonHelpers.EmptyObject(); }
    }

    private static string SerializeItems(IReadOnlyList<DataItem> items)
    {
        var arr = items.Select(i => i.Json).ToList();
        return JsonSerializer.Serialize(arr);
    }
}
