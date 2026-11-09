namespace WorkflowPlatform.Api.Engine;

/// <summary>Maps a node type string to its INode implementation. Add a node = register it here.</summary>
public class NodeRegistry
{
    private readonly Dictionary<string, INode> _nodes;

    public NodeRegistry(IEnumerable<INode> nodes)
    {
        _nodes = nodes.ToDictionary(n => n.Definition.Type, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGet(string type, out INode node) => _nodes.TryGetValue(type, out node!);

    public INode Get(string type) =>
        _nodes.TryGetValue(type, out var n)
            ? n
            : throw new InvalidOperationException($"Unknown node type '{type}'.");

    public IReadOnlyCollection<NodeDefinition> Definitions =>
        _nodes.Values.Select(n => n.Definition).ToList();
}
