using System.ComponentModel.DataAnnotations;

namespace WorkflowPlatform.Api.Dtos;

// ---- Auth ----
public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password,
    [Required] string FirstName,
    [Required] string LastName);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record AuthResponse(string Token, UserDto User);

public record UserDto(
    Guid Id, string Email, string FirstName, string LastName,
    string DisplayName, string? AvatarUrl);

public record UpdateProfileRequest(string FirstName, string LastName, string? AvatarUrl);

// ---- Workflows ----
public record CreateWorkflowRequest(string Name, string? Description);

public record UpdateWorkflowRequest(string Name, string? Description, string? GraphJson);

public record WorkflowSummaryDto(
    Guid Id, string Name, string? Description, bool IsActive,
    DateTime CreatedAt, DateTime UpdatedAt,
    bool IsLocked, string? LockedBy, DateTime? LockedAt);

public record WorkflowDetailDto(
    Guid Id, string Name, string? Description, bool IsActive,
    Guid? ActiveVersionId, string GraphJson,
    DateTime CreatedAt, DateTime UpdatedAt,
    bool IsLocked, string? LockedBy, DateTime? LockedAt);

// ---- Team / invitations ----
public record CreateInvitationRequest(string Email, string? Preset, string[]? Permissions);

public record UpdateMemberRequest(string[] Permissions);

public record AcceptInvitationRequest(string Token);

public record RunWorkflowRequest(string? TriggerDataJson);

public record RunWorkflowResponse(Guid ExecutionId);

// ---- Executions ----
public record ExecutionSummaryDto(
    Guid Id, Guid WorkflowId, string Status, string TriggerType,
    DateTime? StartedAt, DateTime? FinishedAt, string? Error);

public record NodeExecutionDto(
    Guid Id, string NodeId, string NodeType, string Status,
    string? InputJson, string? OutputJson, string? Error,
    DateTime? StartedAt, DateTime? FinishedAt);

public record ExecutionDetailDto(
    Guid Id, Guid WorkflowId, string Status, string TriggerType,
    DateTime? StartedAt, DateTime? FinishedAt, string? Error,
    List<NodeExecutionDto> Nodes);
