using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;

namespace WorkflowPlatform.Api.Services;

/// <summary>
/// The account a request is working in, and what the caller may do there.
/// AccountId is the account owner's user id. The owner always has every permission in the catalog.
/// </summary>
public sealed record AccountInfo(Guid UserId, Guid AccountId, IReadOnlySet<string> Permissions)
{
    public bool IsOwner => UserId == AccountId;

    public bool Has(string permission) => Permissions.Contains(permission);

    public void Require(string permission)
    {
        if (!Has(permission)) throw new AccessDeniedException();
    }
}

/// <summary>
/// Resolves the account for the current HTTP request from the X-Account-Id header.
/// No header (or the caller's own id) means "my own account", where the caller is the owner.
/// Any other id is allowed only when the caller is a member of that account.
/// </summary>
public sealed class CurrentAccount
{
    public const string HeaderName = "X-Account-Id";

    private readonly IHttpContextAccessor _http;
    private readonly AppDbContext _db;
    private readonly PermissionCatalog _catalog;
    private AccountInfo? _cached;

    public CurrentAccount(IHttpContextAccessor http, AppDbContext db, PermissionCatalog catalog)
    {
        _http = http;
        _db = db;
        _catalog = catalog;
    }

    public async Task<AccountInfo> GetAsync()
    {
        if (_cached is not null) return _cached;

        var httpContext = _http.HttpContext
            ?? throw new InvalidOperationException("No HTTP request in scope.");
        var userId = httpContext.User.GetUserId();
        var header = httpContext.Request.Headers[HeaderName].ToString().Trim();

        if (string.IsNullOrEmpty(header) || header.Equals(userId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            var all = await _catalog.AllKeysAsync();
            return _cached = new AccountInfo(userId, userId,
                new HashSet<string>(all, StringComparer.OrdinalIgnoreCase));
        }

        if (!Guid.TryParse(header, out var accountId))
            throw new AccessDeniedException("You don't have access to this account.");

        var membership = await _db.AccountMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.AccountOwnerId == accountId && m.UserId == userId);
        if (membership is null)
            throw new AccessDeniedException("You don't have access to this account.");

        var keys = (await _catalog.KeysForMembershipsAsync(new[] { membership.Id }))[membership.Id];
        return _cached = new AccountInfo(userId, accountId,
            new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase));
    }
}
