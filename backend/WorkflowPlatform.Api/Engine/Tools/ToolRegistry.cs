using WorkflowPlatform.Api.Integrations;

namespace WorkflowPlatform.Api.Engine.Tools;

/// <summary>
/// Maps a tool name to its IAgentTool. Includes directly-registered tools (http_request,
/// run_workflow) plus one auto-generated tool per integration action (Google Calendar, Gmail...).
/// Add a tool = register an IAgentTool, or add an IIntegrationAction and it appears here too.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools, IntegrationActionRegistry actions)
    {
        _tools = new Dictionary<string, IAgentTool>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tools)
            _tools[t.Name] = t;
        foreach (var action in actions.All)
        {
            var tool = new IntegrationActionTool(action);
            _tools[tool.Name] = tool;
        }
    }

    public IReadOnlyCollection<IAgentTool> All => _tools.Values.ToList();

    public bool TryGet(string name, out IAgentTool tool) => _tools.TryGetValue(name, out tool!);
}
