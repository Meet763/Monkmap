using System.Net.Http.Headers;
using System.Text.Json;
using WorkflowPlatform.Api.Engine;

namespace WorkflowPlatform.Api.Integrations;

/// <summary>
/// Google OAuth 2.0 provider. Reads client id/secret from configuration:
///   Integrations:Google:ClientId / :ClientSecret
/// Create them at https://console.cloud.google.com → APIs &amp; Services → Credentials
/// (OAuth client, type "Web application"), and add the redirect URI shown in appsettings.
/// </summary>
public sealed class GoogleOAuthProvider : IOAuthProvider
{
    private readonly IHttpClientFactory _http;
    private readonly string? _clientId;
    private readonly string? _clientSecret;

    public GoogleOAuthProvider(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        _clientId = Real(config["Integrations:Google:ClientId"]);
        _clientSecret = Real(config["Integrations:Google:ClientSecret"]);
    }

    /// <summary>Placeholder values from appsettings.json mean "not set", so the provider shows as unconfigured.</summary>
    private static string? Real(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("__SET_IN_USER_SECRETS__") ? null : value;

    public string Key => "google";
    public string DisplayName => "Google";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret);

    public IReadOnlyList<string> DefaultScopes => new[]
    {
        "openid", "email", "profile",
        "https://www.googleapis.com/auth/calendar",
        "https://www.googleapis.com/auth/gmail.send",
        "https://www.googleapis.com/auth/spreadsheets",
        "https://www.googleapis.com/auth/drive.readonly"
    };

    public string BuildAuthorizationUrl(string redirectUri, string state, IEnumerable<string> scopes)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', scopes),
            ["access_type"] = "offline",      // ask for a refresh token
            ["prompt"] = "consent",           // force refresh token on re-consent
            ["include_granted_scopes"] = "true",
            ["state"] = state
        };
        var qs = string.Join('&', query.Where(kv => kv.Value is not null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));
        return $"https://accounts.google.com/o/oauth2/v2/auth?{qs}";
    }

    public async Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _clientId ?? "",
            ["client_secret"] = _clientSecret ?? "",
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };
        return await PostTokenAsync(form, ct);
    }

    public async Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = _clientId ?? "",
            ["client_secret"] = _clientSecret ?? "",
            ["grant_type"] = "refresh_token"
        };
        var tokens = await PostTokenAsync(form, ct);
        // Google often omits refresh_token on refresh — keep the existing one.
        return tokens with { RefreshToken = tokens.RefreshToken ?? refreshToken };
    }

    public async Task<OAuthAccount> GetAccountAsync(string accessToken, CancellationToken ct)
    {
        var client = _http.CreateClient("node-http");
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var res = await client.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) return new OAuthAccount("", "Google account");
        var json = JsonHelpers.Parse(text);
        var email = json.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
        var id = json.TryGetProperty("id", out var i) ? i.GetString() ?? "" : email;
        return new OAuthAccount(id, string.IsNullOrEmpty(email) ? "Google account" : email);
    }

    private async Task<OAuthTokens> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var client = _http.CreateClient("node-http");
        using var res = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google token error ({(int)res.StatusCode}): {text}");

        var json = JsonHelpers.Parse(text);
        var access = json.GetProperty("access_token").GetString() ?? "";
        var refresh = json.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var scope = json.TryGetProperty("scope", out var s) ? s.GetString() : null;
        DateTime? expires = json.TryGetProperty("expires_in", out var ex) && ex.TryGetInt32(out var secs)
            ? DateTime.UtcNow.AddSeconds(secs)
            : null;
        return new OAuthTokens(access, refresh, expires, scope);
    }
}
