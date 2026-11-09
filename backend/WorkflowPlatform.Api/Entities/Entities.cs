namespace WorkflowPlatform.Api.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Workflow> Workflows { get; set; } = new();
}

public class Workflow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Name { get; set; } = "Untitled Workflow";
    public string? Description { get; set; }
    public bool IsActive { get; set; }

    public Guid? ActiveVersionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<WorkflowVersion> Versions { get; set; } = new();
    public List<Execution> Executions { get; set; } = new();
}

public class WorkflowVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Workflow? Workflow { get; set; }

    public int VersionNumber { get; set; }

    /// <summary>JSON: { "nodes": [...], "connections": [...] }</summary>
    public string GraphJson { get; set; } = "{\"nodes\":[],\"connections\":[]}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ExecutionStatus { Queued, Running, Succeeded, Failed, Canceled }
public enum NodeExecutionStatus { Pending, Running, Succeeded, Failed, Skipped }
public enum TriggerType { Manual, Webhook, Schedule }

public class Execution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public Workflow? Workflow { get; set; }
    public Guid WorkflowVersionId { get; set; }

    public ExecutionStatus Status { get; set; } = ExecutionStatus.Queued;
    public TriggerType TriggerType { get; set; } = TriggerType.Manual;

    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Error { get; set; }

    /// <summary>Optional initial trigger payload (JSON).</summary>
    public string? TriggerDataJson { get; set; }

    public List<NodeExecution> NodeExecutions { get; set; } = new();
}

public class NodeExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExecutionId { get; set; }
    public Execution? Execution { get; set; }

    public string NodeId { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public NodeExecutionStatus Status { get; set; } = NodeExecutionStatus.Pending;

    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? Error { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
