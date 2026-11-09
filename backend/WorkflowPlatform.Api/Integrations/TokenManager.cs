using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Entities;

namespace WorkflowPlatform.Api.Integrations;

/// <summary>Returns a valid access token for a connection, refreshing + persisting it if expired.</summary>
public interface ITokenManager
{
    Task<string> GetAccessTokenAsync(Connection connection, CancellationToken ct);
}

/// <summary>
/// The single place token refresh lives. Every integration action calls this, so no action ever
/// has to think about expiry or refresh logic.
/// </summary>
public sealed class TokenManager : ITokenManager
{
    private readonly AppDbContext _db;
    private readonly OAuthProviderRegistry _providers;
    private readonly CredentialProtector _protector;

    public TokenManager(AppDbContext db, OAuthProviderRegistry providers, CredentialProtector protector)
    {
        _db = db;
        _providers = providers;
        _protector = protector;
    }

    public async Task<string> GetAccessTokenAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = _protector.Unprotect(connection.AccessTokenEnc);

        // Still valid (with a 60s safety margin)?
        if (connection.ExpiresAt is null || connection.ExpiresAt > DateTime.UtcNow.AddSeconds(60))
            return accessToken;

        // Expired → refresh.
        if (string.IsNullOrEmpty(connection.RefreshTokenEnc))
            throw new InvalidOperationException("Access token expired and no refresh token is stored. Reconnect the account.");
        if (!_providers.TryGet(connection.Provider, out var provider))
            throw new InvalidOperationException($"No OAuth provider for '{connection.Provider}'.");

        var refreshToken = _protector.Unprotect(connection.RefreshTokenEnc);
        var tokens = await provider.RefreshAsync(refreshToken, ct);

        connection.AccessTokenEnc = _protector.Protect(tokens.AccessToken);
        if (!string.IsNullOrEmpty(tokens.RefreshToken))
            connection.RefreshTokenEnc = _protector.Protect(tokens.RefreshToken);
        connection.ExpiresAt = tokens.ExpiresAtUtc;
        connection.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return tokens.AccessToken;
    }
}
