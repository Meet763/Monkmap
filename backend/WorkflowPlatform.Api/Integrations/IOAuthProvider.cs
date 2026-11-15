namespace WorkflowPlatform.Api.Integrations;

/// <summary>Tokens returned by an OAuth provider after code exchange or refresh.</summary>
public sealed record OAuthTokens(string AccessToken, string? RefreshToken, DateTime? ExpiresAtUtc, string? Scope);

/// <summary>Minimal account identity used to label a connection.</summary>
public sealed record OAuthAccount(string Id, string Name);

/// <summary>
/// One selectable sub-service of a provider (e.g. Google → Calendar, Gmail, Sheets, Drive).
/// A user can connect only the services they need; each maps to a set of OAuth scopes.
/// </summary>
public sealed record ProviderService(string Key, string DisplayName, IReadOnlyList<string> Scopes);

/// <summary>
/// One external identity provider (Google, Slack, Microsoft...). Implement this + register it to
/// add a new provider — nothing else in the platform needs to change. This is what makes the
/// integration system "open": the connect/callback/refresh flow is generic.
/// </summary>
public interface IOAuthProvider
{
    /// <summary>Lower-case key, e.g. "google".</summary>
    string Key { get; }

    string DisplayName { get; }

    /// <summary>Scopes requested by default when the user connects.</summary>
    IReadOnlyList<string> DefaultScopes { get; }

    /// <summary>
    /// The individually selectable services this provider exposes (Calendar, Gmail...). Lets the
    /// user connect only some of them. Empty = provider is all-or-nothing (uses DefaultScopes).
    /// </summary>
    IReadOnlyList<ProviderService> Services { get; }

    /// <summary>True when client id/secret are configured; false shows a clear "not configured" error.</summary>
    bool IsConfigured { get; }

    string BuildAuthorizationUrl(string redirectUri, string state, IEnumerable<string> scopes);

    Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct);

    Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct);

    Task<OAuthAccount> GetAccountAsync(string accessToken, CancellationToken ct);
}
