namespace WorkflowPlatform.Api.Engine.Providers;

/// <summary>Maps a Provider value from the node to its IChatModelProvider. Add a provider = register it in Program.cs.</summary>
public sealed class ChatModelProviderRegistry
{
    private readonly Dictionary<string, IChatModelProvider> _byKey;

    public ChatModelProviderRegistry(IEnumerable<IChatModelProvider> providers)
    {
        _byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers)
            foreach (var key in p.Keys)
                _byKey[key] = p;
    }

    public bool TryGet(string key, out IChatModelProvider provider) =>
        _byKey.TryGetValue(key, out provider!);
}
