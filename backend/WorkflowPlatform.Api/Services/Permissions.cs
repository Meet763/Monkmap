namespace WorkflowPlatform.Api.Services;

/// <summary>
/// Permission keys that code checks for. The list of permissions, their labels, and the presets
/// live in the database (Permissions, PermissionPresets, PermissionPresetItems), read through
/// <see cref="PermissionCatalog"/>. A key must exist in the Permissions table to be granted.
/// </summary>
public static class Permissions
{
    public const string WorkflowsView = "workflows.view";
    public const string WorkflowsCreate = "workflows.create";
    public const string WorkflowsEdit = "workflows.edit";
    public const string WorkflowsRun = "workflows.run";
    public const string WorkflowsDelete = "workflows.delete";
    public const string MembersManage = "members.manage";
}
