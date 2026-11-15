using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Entities;
using WorkflowPlatform.Api.Integrations;
using WorkflowPlatform.Api.Services;

namespace WorkflowPlatform.Api.Controllers;

/// <summary>
/// Drives the integration system: lists available providers + actions, starts the OAuth
/// "connect" flow, and handles the provider's callback.
/// </summary>
[ApiController]
[Route("api/integrations")]
public class IntegrationsController : ControllerBase
{
    private readonly OAuthProviderRegistry _providers;
    private readonly IntegrationActionRegistry _actions;
    private readonly CredentialProtector _protector;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public IntegrationsController(
        OAuthProviderRegistry providers, IntegrationActionRegistry actions,
        CredentialProtector protector, AppDbContext db, IConfiguration config)
    {
        _providers = providers;
        _actions = actions;
        _protector = protector;
        _db = db;
        _config = config;
    }

    /// <summary>List providers (configured? connected?) and each one's reusable actions.</summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var userId = User.GetUserId();
        var connections = await _db.Connections
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Provider, c.ScopesJson })
            .ToListAsync();

        var result = _providers.All.Select(p =>
        {
            var connection = connections.FirstOrDefault(c => c.Provider == p.Key);
            var grantedScopes = connection is null
                ? new HashSet<string>()
                : new HashSet<string>(DeserializeScopes(connection.ScopesJson));

            // A service counts as connected when every scope it needs has been granted.
            var services = p.Services.Select(s => new
            {
                key = s.Key,
                displayName = s.DisplayName,
                connected = connection is not null && s.Scopes.All(grantedScopes.Contains)
            }).ToList();

            return new
            {
                key = p.Key,
                displayName = p.DisplayName,
                isConfigured = p.IsConfigured,
                isConnected = connection is not null,
                services,
                connectedServiceCount = services.Count(s => s.connected),
                serviceCount = services.Count,
                actions = _actions.ForProvider(p.Key)
                    .Select(a => new { key = a.Key, displayName = a.DisplayName, description = a.Description })
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// Return the URL the browser should open to start the OAuth consent flow. The optional
    /// <paramref name="services"/> query (comma-separated service keys, e.g. "calendar,gmail")
    /// requests only those services' scopes; omit it to request everything.
    /// </summary>
    [Authorize]
    [HttpGet("{provider}/connect-url")]
    public IActionResult ConnectUrl(string provider, [FromQuery] string? services)
    {
        if (!_providers.TryGet(provider, out var p))
            return NotFound(new { message = $"Unknown provider '{provider}'." });
        if (!p.IsConfigured)
            return BadRequest(new { message = $"{p.DisplayName} is not configured. Set Integrations:{p.DisplayName}:ClientId/ClientSecret." });

        var scopes = ScopesForSelection(p, services);
        if (scopes.Count == 0)
            return BadRequest(new { message = "Select at least one service to connect." });

        var userId = User.GetUserId();
        var state = _protector.ProtectState(JsonSerializer.Serialize(new { userId, provider = p.Key }));
        var url = p.BuildAuthorizationUrl(RedirectUriFor(p.Key), state, scopes);
        return Ok(new { url });
    }

    /// <summary>
    /// Base identity scopes (always kept) plus the scopes of the selected services. A null/empty
    /// selection means "all services".
    /// </summary>
    private static IReadOnlyList<string> ScopesForSelection(IOAuthProvider p, string? services)
    {
        var selected = (services ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // No services defined on the provider → fall back to its default scopes.
        if (p.Services.Count == 0) return p.DefaultScopes;

        var allServiceScopes = p.Services.SelectMany(s => s.Scopes).ToHashSet();
        var chosenServiceScopes = p.Services
            .Where(s => selected.Count == 0 || selected.Contains(s.Key))
            .SelectMany(s => s.Scopes)
            .ToHashSet();

        // Keep base scopes (not owned by any service) + the chosen services' scopes.
        return p.DefaultScopes
            .Where(sc => !allServiceScopes.Contains(sc) || chosenServiceScopes.Contains(sc))
            .ToList();
    }

    private static string[] DeserializeScopes(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>OAuth callback. The browser lands here from the provider; identity is in state.</summary>
    [AllowAnonymous]
    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string? code,
        [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error)) return RedirectToFrontend(provider, $"error={Uri.EscapeDataString(error)}");
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return RedirectToFrontend(provider, "error=missing_code");
        if (!_providers.TryGet(provider, out var p))
            return RedirectToFrontend(provider, "error=unknown_provider");

        Guid userId;
        try
        {
            var json = JsonHelpersSafe(_protector.UnprotectState(state));
            userId = Guid.Parse(json.GetProperty("userId").GetString()!);
        }
        catch { return RedirectToFrontend(provider, "error=bad_state"); }

        OAuthTokens tokens;
        OAuthAccount account;
        try
        {
            tokens = await p.ExchangeCodeAsync(code, RedirectUriFor(p.Key), ct);
            account = await p.GetAccountAsync(tokens.AccessToken, ct);
        }
        catch { return RedirectToFrontend(provider, "error=token_exchange_failed"); }

        var scopes = tokens.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? p.DefaultScopes.ToArray();

        // One connection per user+provider (reconnect updates it).
        var connection = await _db.Connections
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Provider == p.Key, ct);
        if (connection is null)
        {
            connection = new Connection { UserId = userId, Provider = p.Key };
            _db.Connections.Add(connection);
        }

        connection.AccountName = account.Name;
        connection.AccessTokenEnc = _protector.Protect(tokens.AccessToken);
        if (!string.IsNullOrEmpty(tokens.RefreshToken))
            connection.RefreshTokenEnc = _protector.Protect(tokens.RefreshToken);
        connection.ExpiresAt = tokens.ExpiresAtUtc;
        connection.ScopesJson = JsonSerializer.Serialize(scopes);
        connection.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return RedirectToFrontend(provider, "connected=1");
    }

    private string RedirectUriFor(string provider)
    {
        var configured = _config[$"Integrations:{char.ToUpper(provider[0]) + provider[1..]}:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return $"{Request.Scheme}://{Request.Host}/api/integrations/{provider}/callback";
    }

    private IActionResult RedirectToFrontend(string provider, string query)
    {
        var frontend = _config["Integrations:FrontendUrl"] ?? "http://localhost:5173";
        return Redirect($"{frontend.TrimEnd('/')}/integrations?provider={provider}&{query}");
    }

    private static JsonElement JsonHelpersSafe(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
