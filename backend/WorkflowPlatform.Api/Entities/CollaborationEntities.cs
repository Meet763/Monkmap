namespace WorkflowPlatform.Api.Entities;

/// <summary>A user who was invited into another user's account. Its permissions live in MembershipPermissions.</summary>
public class AccountMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The account's owner (the user who created it). The account id IS this user id.</summary>
    public Guid AccountOwnerId { get; set; }

    public Guid UserId { get; set; }

    public Guid InvitedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<MembershipPermission> Grants { get; set; } = new();
}

/// <summary>A pending invitation. Only a hash of the secret token is stored. Permissions live in InvitationPermissions.</summary>
public class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountOwnerId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public Guid InvitedByUserId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<InvitationPermission> Grants { get; set; } = new();
}

/// <summary>Presence of this row means the workflow is locked. Deleting the row unlocks it.</summary>
public class WorkflowLock
{
    public Guid WorkflowId { get; set; }
    public Workflow? Workflow { get; set; }

    public Guid LockedByUserId { get; set; }
    public DateTime LockedAt { get; set; } = DateTime.UtcNow;
}

// ---- Permission catalog (editable in the database) ----

/// <summary>One permission the platform understands. Key must match a check in code.</summary>
public class PermissionDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>A named bundle of permissions offered when inviting someone (Viewer, Editor, Admin...).</summary>
public class PermissionPreset
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public List<PermissionPresetItem> Items { get; set; } = new();
}

/// <summary>One permission inside a preset.</summary>
public class PermissionPresetItem
{
    public int PresetId { get; set; }
    public PermissionPreset? Preset { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
}

/// <summary>A permission granted to a member. One row per permission.</summary>
public class MembershipPermission
{
    public Guid MembershipId { get; set; }
    public AccountMembership? Membership { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
}

/// <summary>A permission the invitation will grant when accepted. One row per permission.</summary>
public class InvitationPermission
{
    public Guid InvitationId { get; set; }
    public Invitation? Invitation { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
}
