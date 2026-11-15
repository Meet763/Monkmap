using WorkflowPlatform.Api.Engine.Nodes;
using WorkflowPlatform.Api.Integrations;

namespace WorkflowPlatform.Api.Engine;

/// <summary>
/// Maps a node type string to its INode implementation. Add a node = register it here.
///
/// On top of the statically-registered nodes, every integration action (Gmail, Calendar...) is
/// surfaced as its own palette node with type "app.&lt;actionKey&gt;". These all route to the single
/// <see cref="IntegrationActionNode"/>, which reads the action from the node type.
/// </summary>
public class NodeRegistry
{
    private readonly Dictionary<string, INode> _nodes;
    private readonly IntegrationActionRegistry _actions;
    private readonly INode? _appHost;          // the IntegrationActionNode that runs every app.* type
    private readonly List<NodeDefinition> _appDefs;
    private readonly HashSet<string> _appTypes; // generated app-node types (app.<provider>.<service>)

    public NodeRegistry(IEnumerable<INode> nodes, IntegrationActionRegistry actions)
    {
        _nodes = nodes.ToDictionary(n => n.Definition.Type, StringComparer.OrdinalIgnoreCase);
        _actions = actions;
        _nodes.TryGetValue("IntegrationAction", out _appHost);
        _appDefs = IntegrationActionNode.BuildServiceDefinitions(_actions.All).ToList();
        _appTypes = _appDefs.Select(d => d.Type).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string type, out INode node)
    {
        if (_nodes.TryGetValue(type, out node!)) return true;

        if (_appHost is not null && IsAppType(type))
        {
            node = _appHost;
            return true;
        }

        node = null!;
        return false;
    }

    /// <summary>A generated app node, or a back-compat single-action "app.&lt;actionKey&gt;" node.</summary>
    private bool IsAppType(string type)
    {
        if (_appTypes.Contains(type)) return true;
        return type.StartsWith(IntegrationActionNode.AppPrefix, StringComparison.OrdinalIgnoreCase) &&
               _actions.TryGet(type[IntegrationActionNode.AppPrefix.Length..], out _);
    }

    public INode Get(string type) =>
        TryGet(type, out var n)
            ? n
            : throw new InvalidOperationException($"Unknown node type '{type}'.");

    /// <summary>Static node blueprints + one generated blueprint per integration action.</summary>
    public IReadOnlyCollection<NodeDefinition> Definitions =>
        _nodes.Values.Select(n => n.Definition).Concat(_appDefs).ToList();
}
