using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Entities;

namespace WorkflowPlatform.Api.Services;

/// <summary>
/// Reads and writes permissions from the database: the catalog, presets, and who holds what.
/// Everything here is data-driven, so a change made in SQL Server takes effect on the next request.
/// </summary>
public sealed class PermissionCatalog
{
    private readonly AppDbContext _db;

    public PermissionCatalog(AppDbContext db) => _db = db;

    public Task<List<PermissionDefinition>> AllDefinitionsAsync() =>
        _db.Permissions.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Key).ToListAsync();

    public async Task<string[]> AllKeysAsync() =>
        (await AllDefinitionsAsync()).Select(p => p.Key).ToArray();

    /// <summary>Keep only keys that exist in the catalog, in catalog order, without duplicates.</summary>
    public async Task<string[]> NormalizeAsync(IEnumerable<string>? requested)
    {
        if (requested is null) return Array.Empty<string>();
        var wanted = requested.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (await AllKeysAsync()).Where(wanted.Contains).ToArray();
    }

    public async Task<List<PresetView>> PresetsAsync()
    {
        var presets = await _db.PermissionPresets.AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(p => new { p.Name, p.Description, Keys = p.Items.Select(i => i.PermissionKey) })
            .ToListAsync();
        var catalog = (await AllKeysAsync()).ToList();
        return presets.Select(p => new PresetView(p.Name, p.Description,
            catalog.Where(k => p.Keys.Contains(k)).ToArray())).ToList();
    }

    /// <summary>Permission keys of a preset, looked up by name (case-insensitive). Null if unknown.</summary>
    public async Task<string[]?> PresetKeysAsync(string presetName)
    {
        var preset = (await PresetsAsync()).FirstOrDefault(p =>
            p.Name.Equals(presetName.Trim(), StringComparison.OrdinalIgnoreCase));
        return preset?.Permissions;
    }

    public async Task<Dictionary<Guid, string[]>> KeysForMembershipsAsync(IEnumerable<Guid> membershipIds)
    {
        var ids = membershipIds.ToList();
        var grants = await _db.MembershipPermissions.AsNoTracking()
            .Where(g => ids.Contains(g.MembershipId))
            .ToListAsync();
        var catalog = (await AllKeysAsync()).ToList();
        return ids.ToDictionary(
            id => id,
            id => catalog.Where(k => grants.Any(g => g.MembershipId == id && g.PermissionKey == k)).ToArray());
    }

    public async Task<string[]> KeysForInvitationAsync(Guid invitationId)
    {
        var keys = await _db.InvitationPermissions.AsNoTracking()
            .Where(g => g.InvitationId == invitationId)
            .Select(g => g.PermissionKey)
            .ToListAsync();
        var catalog = await AllKeysAsync();
        return catalog.Where(keys.Contains).ToArray();
    }

    /// <summary>Replaces a membership's permissions with exactly these keys.</summary>
    public async Task ReplaceMembershipKeysAsync(Guid membershipId, IEnumerable<string> keys)
    {
        var wanted = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = await _db.MembershipPermissions
            .Where(g => g.MembershipId == membershipId).ToListAsync();

        // Only touch the differences, so unchanged rows are not deleted and re-inserted.
        foreach (var grant in existing.Where(g => !wanted.Contains(g.PermissionKey)))
            _db.MembershipPermissions.Remove(grant);
        var have = existing.Select(g => g.PermissionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in wanted.Where(k => !have.Contains(k)))
            _db.MembershipPermissions.Add(new MembershipPermission { MembershipId = membershipId, PermissionKey = key });
    }

    /// <summary>Replaces an invitation's permissions with exactly these keys.</summary>
    public async Task ReplaceInvitationKeysAsync(Guid invitationId, IEnumerable<string> keys)
    {
        var wanted = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = await _db.InvitationPermissions
            .Where(g => g.InvitationId == invitationId).ToListAsync();

        foreach (var grant in existing.Where(g => !wanted.Contains(g.PermissionKey)))
            _db.InvitationPermissions.Remove(grant);
        var have = existing.Select(g => g.PermissionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in wanted.Where(k => !have.Contains(k)))
            _db.InvitationPermissions.Add(new InvitationPermission { InvitationId = invitationId, PermissionKey = key });
    }
}

public sealed record PresetView(string Name, string Description, string[] Permissions);
