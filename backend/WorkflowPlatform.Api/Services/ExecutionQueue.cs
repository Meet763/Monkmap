using System.Threading.Channels;
using WorkflowPlatform.Api.Engine;

namespace WorkflowPlatform.Api.Services;

/// <summary>Abstraction over the execution queue. Swap for RabbitMQ/Redis later (plan §6.3).</summary>
public interface IExecutionQueue
{
    ValueTask EnqueueAsync(Guid executionId, CancellationToken ct = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct);
}

/// <summary>Phase-1 in-process queue using System.Threading.Channels.</summary>
public class InProcessExecutionQueue : IExecutionQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false });

    public ValueTask EnqueueAsync(Guid executionId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(executionId, ct);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Background worker that drains the queue and runs the executor (plan §6.3 / §7).</summary>
public class ExecutionBackgroundService : BackgroundService
{
    private readonly IExecutionQueue _queue;
    private readonly IServiceProvider _services;
    private readonly ILogger<ExecutionBackgroundService> _logger;

    public ExecutionBackgroundService(
        IExecutionQueue queue,
        IServiceProvider services,
        ILogger<ExecutionBackgroundService> logger)
    {
        _queue = queue;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var executionId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                var executor = _services.GetRequiredService<WorkflowExecutor>();
                await executor.RunAsync(executionId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Execution {Id} crashed", executionId);
            }
        }
    }
}
