using System.Text.Json;

namespace WorkflowPlatform.Api.Engine.Tools;

/// <summary>Context handed to a tool when the AI agent invokes it.</summary>
public sealed class AgentToolContext
{
    public required IServiceProvider Services { get; init; }
    public required ILogger Logger { get; init; }
    public CancellationToken CancellationToken { get; init; }

    /// <summary>Owner of the workflow the agent is running inside (for scoping lookups).</summary>
    public Guid? OwnerUserId { get; init; }

    /// <summary>The workflow currently running (used to block direct self-recursion).</summary>
    public Guid? CurrentWorkflowId { get; init; }
}

/// <summary>
/// A capability the AI agent can call. Add a tool = implement this and register it in Program.cs;
/// it then becomes available to every agent automatically (and later, as an MCP tool too).
/// </summary>
public interface IAgentTool
{
    /// <summary>Unique function name the model will call (snake_case, no spaces).</summary>
    string Name { get; }

    /// <summary>Plain-English description so the model knows when to use it.</summary>
    string Description { get; }

    /// <summary>JSON Schema (an object) describing the tool's input arguments.</summary>
    JsonElement ParametersSchema { get; }

    /// <summary>Run the tool with the model-supplied arguments; return a string result for the model.</summary>
    Task<string> InvokeAsync(JsonElement args, AgentToolContext ctx);
}
