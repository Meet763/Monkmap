using WorkflowPlatform.Api.Entities;

namespace WorkflowPlatform.Api.Data;

/// <summary>
/// Starting values for the permission catalog and presets. Inserted by the migration.
/// After that, change them directly in the database (tables: Permissions, PermissionPresets,
/// PermissionPresetItems). The app reads them at runtime, so no code change is needed to add
/// presets or change what a preset contains. A NEW permission key still needs code to enforce it.
/// </summary>
public static class PermissionSeed
{
    public static readonly PermissionDefinition[] Definitions =
    {
        new() { Key = "workflows.view",   Label = "View",         Description = "See workflows and open them read-only", SortOrder = 1 },
        new() { Key = "workflows.create", Label = "Create",       Description = "Create new workflows",                  SortOrder = 2 },
        new() { Key = "workflows.edit",   Label = "Edit & lock",  Description = "Save changes, turn Active on/off, lock and unlock", SortOrder = 3 },
        new() { Key = "workflows.run",    Label = "Run",          Description = "Run a workflow manually",               SortOrder = 4 },
        new() { Key = "workflows.delete", Label = "Delete",       Description = "Delete a workflow (only when unlocked)", SortOrder = 5 },
        new() { Key = "members.manage",   Label = "Manage team",  Description = "Invite, change and remove members",     SortOrder = 6 },
    };

    public static readonly PermissionPreset[] Presets =
    {
        new() { Id = 1, Name = "Viewer", Description = "Can only look", SortOrder = 1 },
        new() { Id = 2, Name = "Editor", Description = "Can build, edit, lock and run", SortOrder = 2 },
        new() { Id = 3, Name = "Admin",  Description = "Everything, including the team", SortOrder = 3 },
    };

    public static readonly PermissionPresetItem[] PresetItems =
    {
        new() { PresetId = 1, PermissionKey = "workflows.view" },

        new() { PresetId = 2, PermissionKey = "workflows.view" },
        new() { PresetId = 2, PermissionKey = "workflows.create" },
        new() { PresetId = 2, PermissionKey = "workflows.edit" },
        new() { PresetId = 2, PermissionKey = "workflows.run" },

        new() { PresetId = 3, PermissionKey = "workflows.view" },
        new() { PresetId = 3, PermissionKey = "workflows.create" },
        new() { PresetId = 3, PermissionKey = "workflows.edit" },
        new() { PresetId = 3, PermissionKey = "workflows.run" },
        new() { PresetId = 3, PermissionKey = "workflows.delete" },
        new() { PresetId = 3, PermissionKey = "members.manage" },
    };
}
