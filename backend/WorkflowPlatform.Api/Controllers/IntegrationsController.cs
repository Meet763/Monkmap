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
        var connectedProviders = await _db.Connections
            .Where(c => c.UserId == userId)
            .Select(c => c.Provider)
            .ToListAsync();

        var result = _providers.All.Select(p => new
        {
            key = p.Key,
            displayName = p.DisplayName,
            isConfigured = p.IsConfigured,
            isConnected = connectedProviders.Contains(p.Key),
            actions = _actions.ForProvider(p.Key)
                .Select(a => new { key = a.Key, displayName = a.DisplayName, description = a.Description })
        });

        return Ok(result);
    }

    /// <summary>Return the URL the browser should open to start the OAuth consent flow.</summary>
    [Authorize]
    [HttpGet("{provider}/connect-url")]
    public IActionResult ConnectUrl(string provider)
    {
        if (!_providers.TryGet(provider, out var p))
            return NotFound(new { message = $"Unknown provider '{provider}'." });
        if (!p.IsConfigured)
            return BadRequest(new { message = $"{p.DisplayName} is not configured. Set Integrations:{p.DisplayName}:ClientId/ClientSecret." });

        var userId = User.GetUserId();
        var state = _protector.ProtectState(JsonSerializer.Serialize(new { userId, provider = p.Key }));
        var url = p.BuildAuthorizationUrl(RedirectUriFor(p.Key), state, p.DefaultScopes);
        return Ok(new { url });
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
