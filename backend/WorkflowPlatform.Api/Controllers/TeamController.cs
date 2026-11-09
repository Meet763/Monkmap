using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Dtos;
using WorkflowPlatform.Api.Entities;
using WorkflowPlatform.Api.Services;

namespace WorkflowPlatform.Api.Controllers;

/// <summary>
/// Accounts, members, and invitations. Permissions come from the database catalog and presets.
/// Members get exactly the permissions the owner (or another manager) gives them, and nobody can
/// give permissions they do not have themselves.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
public class TeamController : ControllerBase
{
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;
    private readonly CurrentAccount _account;
    private readonly PermissionCatalog _catalog;
    private readonly IConfiguration _config;

    public TeamController(AppDbContext db, CurrentAccount account, PermissionCatalog catalog, IConfiguration config)
    {
        _db = db;
        _account = account;
        _catalog = catalog;
        _config = config;
    }

    /// <summary>The permission catalog and presets, for building the UI. Editable in the database.</summary>
    [HttpGet("permissions")]
    public async Task<IActionResult> Catalog()
    {
        var defs = await _catalog.AllDefinitionsAsync();
        var presets = await _catalog.PresetsAsync();
        return Ok(new
        {
            permissions = defs.Select(d => new { key = d.Key, label = d.Label, description = d.Description }),
            presets = presets.Select(p => new { name = p.Name, description = p.Description, permissions = p.Permissions })
        });
    }

    /// <summary>Accounts the caller can work in: their own, plus every account they were invited into.</summary>
    [HttpGet("accounts")]
    public async Task<IActionResult> MyAccounts()
    {
        var me = User.GetUserId();
        var myUser = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == me);

        var result = new List<object>
        {
            new { accountId = me, name = $"{DisplayName(myUser)}'s workspace", isOwner = true,
                  permissions = await _catalog.AllKeysAsync() }
        };

        var memberships = await _db.AccountMemberships.AsNoTracking()
            .Where(m => m.UserId == me).ToListAsync();
        var ownerIds = memberships.Select(m => m.AccountOwnerId).ToList();
        var owners = await _db.Users.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        var keys = await _catalog.KeysForMembershipsAsync(memberships.Select(m => m.Id));

        foreach (var m in memberships)
        {
            var owner = owners.GetValueOrDefault(m.AccountOwnerId);
            result.Add(new
            {
                accountId = m.AccountOwnerId,
                name = owner is null ? "Shared workspace" : $"{DisplayName(owner)}'s workspace",
                isOwner = false,
                permissions = keys[m.Id]
            });
        }
        return Ok(result);
    }

    [HttpGet("account/members")]
    public async Task<IActionResult> Members()
    {
        var acct = await _account.GetAsync();
        var owner = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == acct.AccountId);

        var memberships = await _db.AccountMemberships.AsNoTracking()
            .Where(m => m.AccountOwnerId == acct.AccountId).ToListAsync();
        var users = await _db.Users.AsNoTracking()
            .Where(u => memberships.Select(m => m.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);
        var keys = await _catalog.KeysForMembershipsAsync(memberships.Select(m => m.Id));

        var rows = new List<object>
        {
            new { userId = owner.Id, email = owner.Email, name = DisplayName(owner),
                  permissions = await _catalog.AllKeysAsync(), isOwner = true }
        };
        rows.AddRange(memberships.Select(m =>
        {
            var u = users.GetValueOrDefault(m.UserId);
            return (object)new
            {
                userId = m.UserId,
                email = u?.Email ?? "",
                name = u is null ? "Unknown user" : DisplayName(u),
                permissions = keys[m.Id],
                isOwner = false
            };
        }));
        return Ok(rows);
    }

    [HttpPut("account/members/{userId:guid}")]
    public async Task<IActionResult> UpdateMember(Guid userId, UpdateMemberRequest req)
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.MembersManage);

        var membership = await _db.AccountMemberships.FirstOrDefaultAsync(
            m => m.AccountOwnerId == acct.AccountId && m.UserId == userId);
        if (membership is null) return NotFound();

        await EnsureCanTouchAsync(acct, membership.Id);

        var newPerms = await _catalog.NormalizeAsync(req.Permissions);
        if (newPerms.Length == 0)
            return BadRequest(new { message = "Give the member at least one permission." });
        EnsureCanGrant(acct, newPerms);

        await _catalog.ReplaceMembershipKeysAsync(membership.Id, newPerms);
        await _db.SaveChangesAsync();
        return Ok(new { userId, permissions = newPerms });
    }

    [HttpDelete("account/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid userId)
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.MembersManage);

        var membership = await _db.AccountMemberships.FirstOrDefaultAsync(
            m => m.AccountOwnerId == acct.AccountId && m.UserId == userId);
        if (membership is null) return NotFound();

        await EnsureCanTouchAsync(acct, membership.Id);
        _db.AccountMemberships.Remove(membership); // its permission rows are removed by the database (cascade)
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("account/invitations")]
    public async Task<IActionResult> PendingInvitations()
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.MembersManage);

        var now = DateTime.UtcNow;
        var pending = await _db.Invitations.AsNoTracking()
            .Where(i => i.AccountOwnerId == acct.AccountId
                        && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        var rows = new List<object>();
        foreach (var i in pending)
        {
            rows.Add(new
            {
                id = i.Id,
                email = i.Email,
                permissions = await _catalog.KeysForInvitationAsync(i.Id),
                expiresAt = i.ExpiresAt,
                createdAt = i.CreatedAt
            });
        }
        return Ok(rows);
    }

    /// <summary>
    /// Creates an invitation and returns its link ONCE. The link contains the secret token; only its
    /// hash is stored, so it cannot be shown again. Email is not sent yet — the inviter shares the link.
    /// </summary>
    [HttpPost("account/invitations")]
    public async Task<IActionResult> CreateInvitation(CreateInvitationRequest req)
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.MembersManage);

        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(email))
            return BadRequest(new { message = "Enter a valid email address." });

        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == acct.UserId);
        if (me.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "You can't invite yourself." });

        var invitee = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        if (invitee is not null && await _db.AccountMemberships.AnyAsync(
                m => m.AccountOwnerId == acct.AccountId && m.UserId == invitee.Id))
            return Conflict(new { message = "This person is already a member. Change their permissions instead." });

        var now = DateTime.UtcNow;
        if (await _db.Invitations.AnyAsync(i => i.AccountOwnerId == acct.AccountId && i.Email == email
                                                && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now))
            return Conflict(new { message = "A pending invitation for this email already exists. Revoke it first." });

        var perms = await ResolvePermissionsAsync(req.Preset, req.Permissions);
        if (perms.Length == 0)
            return BadRequest(new { message = "Choose a preset or at least one permission." });
        EnsureCanGrant(acct, perms);

        var token = NewToken();
        var invitation = new Invitation
        {
            AccountOwnerId = acct.AccountId,
            Email = email,
            TokenHash = HashToken(token),
            InvitedByUserId = acct.UserId,
            ExpiresAt = now.Add(InviteLifetime),
            CreatedAt = now
        };
        _db.Invitations.Add(invitation);
        await _catalog.ReplaceInvitationKeysAsync(invitation.Id, perms);
        await _db.SaveChangesAsync();

        var frontend = (_config["Integrations:FrontendUrl"] ?? "http://localhost:5173").TrimEnd('/');
        return Ok(new
        {
            id = invitation.Id,
            email,
            permissions = perms,
            expiresAt = invitation.ExpiresAt,
            inviteLink = $"{frontend}/invite?token={token}"
        });
    }

    [HttpDelete("account/invitations/{id:guid}")]
    public async Task<IActionResult> RevokeInvitation(Guid id)
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.MembersManage);

        var invitation = await _db.Invitations.FirstOrDefaultAsync(
            i => i.Id == id && i.AccountOwnerId == acct.AccountId);
        if (invitation is null) return NotFound();

        invitation.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Accepts an invitation. The logged-in user's email must match the invited email.</summary>
    [HttpPost("invitations/accept")]
    public async Task<IActionResult> Accept(AcceptInvitationRequest req)
    {
        var userId = User.GetUserId();
        var me = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

        var hash = HashToken(req.Token ?? "");
        var invitation = await _db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash);
        if (invitation is null || invitation.RevokedAt is not null || invitation.AcceptedAt is not null)
            return NotFound(new { message = "This invitation is not valid anymore." });
        if (invitation.ExpiresAt < DateTime.UtcNow)
            return BadRequest(new { message = "This invitation has expired." });
        if (!me.Email.Equals(invitation.Email, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { message = "This invitation was sent to another email." });
        if (invitation.AccountOwnerId == userId)
            return BadRequest(new { message = "This is your own account." });

        var grantKeys = await _catalog.KeysForInvitationAsync(invitation.Id);

        var membership = await _db.AccountMemberships.FirstOrDefaultAsync(
            m => m.AccountOwnerId == invitation.AccountOwnerId && m.UserId == userId);
        if (membership is null)
        {
            membership = new AccountMembership
            {
                AccountOwnerId = invitation.AccountOwnerId,
                UserId = userId,
                InvitedByUserId = invitation.InvitedByUserId,
                CreatedAt = DateTime.UtcNow
            };
            _db.AccountMemberships.Add(membership);
        }
        await _catalog.ReplaceMembershipKeysAsync(membership.Id, grantKeys);

        invitation.AcceptedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { accountId = invitation.AccountOwnerId });
    }

    // ---- helpers ----

    /// <summary>A manager may not change or remove someone who holds permissions the manager lacks.</summary>
    private async Task EnsureCanTouchAsync(AccountInfo acct, Guid membershipId)
    {
        var theirs = (await _catalog.KeysForMembershipsAsync(new[] { membershipId }))[membershipId];
        if (!theirs.All(acct.Has))
            throw new AccessDeniedException("You can't change this member's permissions.");
    }

    /// <summary>Nobody can give permissions they do not have themselves.</summary>
    private static void EnsureCanGrant(AccountInfo acct, IEnumerable<string> perms)
    {
        if (!perms.All(acct.Has))
            throw new AccessDeniedException("You can't give permissions you don't have.");
    }

    private async Task<string[]> ResolvePermissionsAsync(string? preset, string[]? requested)
    {
        var fromList = await _catalog.NormalizeAsync(requested);
        if (fromList.Length > 0) return fromList;
        if (!string.IsNullOrWhiteSpace(preset))
        {
            var keys = await _catalog.PresetKeysAsync(preset);
            if (keys is not null) return keys;
        }
        return Array.Empty<string>();
    }

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string DisplayName(WorkflowPlatform.Api.Entities.User u) =>
        string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName;
}
