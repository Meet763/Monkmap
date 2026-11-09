using System.Text.Json;

namespace WorkflowPlatform.Api.Engine.Nodes;

/// <summary>Starts a workflow manually. Emits the trigger payload (or an empty object).</summary>
public class ManualTriggerNode : INode
{
    public NodeDefinition Definition => new()
    {
        Type = "ManualTrigger",
        DisplayName = "Manual Trigger",
        Group = "Triggers",
        Icon = "play",
        IsTrigger = true,
        OutputPorts = new[] { "main" },
        Parameters = new()
    };

    public Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx)
    {
        var item = ctx.InputItems.Count > 0 ? ctx.InputItems[0].Json : JsonHelpers.EmptyObject();
        return Task.FromResult(NodeResult.Ok(item));
    }
}
