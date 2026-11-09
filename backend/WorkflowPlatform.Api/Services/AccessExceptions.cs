namespace WorkflowPlatform.Api.Services;

/// <summary>The user is not allowed to do this. Mapped to HTTP 403.</summary>
public sealed class AccessDeniedException : Exception
{
    public AccessDeniedException(string message = "You have no access to do this.") : base(message) { }
}

/// <summary>The workflow is locked, so changes are refused. Mapped to HTTP 423 (Locked).</summary>
public sealed class WorkflowLockedException : Exception
{
    public WorkflowLockedException() : base("This workflow is locked. Unlock it to make changes.") { }
}
