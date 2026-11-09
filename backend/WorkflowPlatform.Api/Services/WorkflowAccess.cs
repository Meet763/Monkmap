using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Entities;

namespace WorkflowPlatform.Api.Services;

/// <summary>Lock details shown on workflow cards and the editor banner.</summary>
public sealed record LockInfo(string LockedBy, DateTime LockedAt);

/// <summary>
/// One place for the workflow rules: which workflows the caller can see, which changes need which
/// permission, and the lock check. Controllers, webhooks and agent tools all use this.
/// </summary>
public sealed class WorkflowAccess
{
    private readonly AppDbContext _db;
    private readonly CurrentAccount _account;

    public WorkflowAccess(AppDbContext db, CurrentAccount account)
    {
        _db = db;
        _account = account;
    }

    /// <summary>The workflow if it belongs to the current account and the caller may view it; otherwise null.</summary>
    public async Task<Workflow?> FindVisibleAsync(Guid id, bool includeVersions = false)
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.WorkflowsView);

        IQueryable<Workflow> query = _db.Workflows.Where(w => w.Id == id && w.UserId == acct.AccountId);
        if (includeVersions) query = query.Include(w => w.Versions);
        return await query.FirstOrDefaultAsync();
    }

    /// <summary>Throws if the caller lacks the permission, or if the workflow is locked.</summary>
    public async Task EnsureChangeAllowedAsync(Guid workflowId, string permission)
    {
        var acct = await _account.GetAsync();
        acct.Require(permission);
        if (await IsLockedAsync(workflowId)) throw new WorkflowLockedException();
    }

    public Task<bool> IsLockedAsync(Guid workflowId) =>
        _db.WorkflowLocks.AnyAsync(l => l.WorkflowId == workflowId);

    /// <summary>Lock details for many workflows at once (who locked it, and when).</summary>
    public async Task<Dictionary<Guid, LockInfo>> GetLocksAsync(IEnumerable<Guid> workflowIds)
    {
        var ids = workflowIds.ToList();
        var locks = await _db.WorkflowLocks.AsNoTracking()
            .Where(l => ids.Contains(l.WorkflowId))
            .ToListAsync();
        if (locks.Count == 0) return new();

        var userIds = locks.Select(l => l.LockedByUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName);

        return locks.ToDictionary(
            l => l.WorkflowId,
            l => new LockInfo(names.TryGetValue(l.LockedByUserId, out var n) ? n : "someone", l.LockedAt));
    }
}
