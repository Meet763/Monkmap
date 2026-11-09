using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WorkflowPlatform.Api.Hubs;

/// <summary>Clients join a group named by executionId to receive live updates for that run.</summary>
[Authorize]
public class ExecutionHub : Hub
{
    public Task Subscribe(string executionId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, executionId);

    public Task Unsubscribe(string executionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, executionId);
}

public interface IExecutionNotifier
{
    Task ExecutionStarted(Guid executionId);
    Task NodeStarted(Guid executionId, string nodeId);
    Task NodeCompleted(Guid executionId, string nodeId, string status, object? outputPreview);
    Task ExecutionFinished(Guid executionId, string status, string? error);
}

public class SignalRExecutionNotifier : IExecutionNotifier
{
    private readonly IHubContext<ExecutionHub> _hub;
    public SignalRExecutionNotifier(IHubContext<ExecutionHub> hub) => _hub = hub;

    private IClientProxy Group(Guid id) => _hub.Clients.Group(id.ToString());

    public Task ExecutionStarted(Guid executionId) =>
        Group(executionId).SendAsync("ExecutionStarted", executionId);

    public Task NodeStarted(Guid executionId, string nodeId) =>
        Group(executionId).SendAsync("NodeStarted", executionId, nodeId);

    public Task NodeCompleted(Guid executionId, string nodeId, string status, object? outputPreview) =>
        Group(executionId).SendAsync("NodeCompleted", executionId, nodeId, status, outputPreview);

    public Task ExecutionFinished(Guid executionId, string status, string? error) =>
        Group(executionId).SendAsync("ExecutionFinished", executionId, status, error);
}
