using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Dtos;
using WorkflowPlatform.Api.Services;

namespace WorkflowPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/executions")]
public class ExecutionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentAccount _account;

    public ExecutionsController(AppDbContext db, CurrentAccount account)
    {
        _db = db;
        _account = account;
    }

    [HttpGet]
    public async Task<ActionResult<List<ExecutionSummaryDto>>> List([FromQuery] Guid? workflowId)
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.WorkflowsView);

        var query = _db.Executions
            .Where(e => e.Workflow!.UserId == acct.AccountId);
        if (workflowId is not null)
            query = query.Where(e => e.WorkflowId == workflowId);

        var items = await query
            .OrderByDescending(e => e.StartedAt)
            .Take(100)
            .Select(e => new ExecutionSummaryDto(
                e.Id, e.WorkflowId, e.Status.ToString(), e.TriggerType.ToString(),
                e.StartedAt, e.FinishedAt, e.Error))
            .ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ExecutionDetailDto>> Get(Guid id)
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.WorkflowsView);

        var execution = await _db.Executions
            .Include(e => e.NodeExecutions)
            .Include(e => e.Workflow)
            .FirstOrDefaultAsync(e => e.Id == id && e.Workflow!.UserId == acct.AccountId);
        if (execution is null) return NotFound();

        var nodes = execution.NodeExecutions
            .OrderBy(n => n.StartedAt)
            .Select(n => new NodeExecutionDto(
                n.Id, n.NodeId, n.NodeType, n.Status.ToString(),
                n.InputJson, n.OutputJson, n.Error, n.StartedAt, n.FinishedAt))
            .ToList();

        return Ok(new ExecutionDetailDto(
            execution.Id, execution.WorkflowId, execution.Status.ToString(),
            execution.TriggerType.ToString(), execution.StartedAt, execution.FinishedAt,
            execution.Error, nodes));
    }
}
