namespace WorkflowPlatform.Api.Entities;

/// <summary>
/// A connected external account (e.g. a user's Google account). Stores the OAuth tokens
/// ENCRYPTED. One row = one user connected to one provider account. Any workflow or AI agent
/// belonging to that user can reuse this connection without re-authenticating.
/// </summary>
public class Connection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Provider key, e.g. "google".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Human label, usually the account email.</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>Encrypted access token (never stored in plaintext).</summary>
    public string AccessTokenEnc { get; set; } = string.Empty;

    /// <summary>Encrypted refresh token (null if the provider didn't issue one).</summary>
    public string? RefreshTokenEnc { get; set; }

    /// <summary>When the access token expires (UTC). Null = unknown/never.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>JSON array of granted scopes.</summary>
    public string ScopesJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
