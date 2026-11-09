namespace WorkflowPlatform.Api.Integrations;

/// <summary>Maps an action key to its IIntegrationAction. Add an action = register it in Program.cs.</summary>
public sealed class IntegrationActionRegistry
{
    private readonly Dictionary<string, IIntegrationAction> _actions;

    public IntegrationActionRegistry(IEnumerable<IIntegrationAction> actions) =>
        _actions = actions.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IIntegrationAction> All => _actions.Values.ToList();

    public IEnumerable<IIntegrationAction> ForProvider(string provider) =>
        _actions.Values.Where(a => a.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));

    public bool TryGet(string key, out IIntegrationAction action) => _actions.TryGetValue(key, out action!);
}
