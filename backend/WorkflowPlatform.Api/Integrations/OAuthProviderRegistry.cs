namespace WorkflowPlatform.Api.Integrations;

/// <summary>Maps a provider key ("google", "slack"...) to its IOAuthProvider.</summary>
public sealed class OAuthProviderRegistry
{
    private readonly Dictionary<string, IOAuthProvider> _providers;

    public OAuthProviderRegistry(IEnumerable<IOAuthProvider> providers) =>
        _providers = providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IOAuthProvider> All => _providers.Values.ToList();

    public bool TryGet(string key, out IOAuthProvider provider) => _providers.TryGetValue(key, out provider!);
}
