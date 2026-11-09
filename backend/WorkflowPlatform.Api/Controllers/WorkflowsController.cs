using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Dtos;
using WorkflowPlatform.Api.Entities;
using WorkflowPlatform.Api.Services;

namespace WorkflowPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workflows")]
public class WorkflowsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IExecutionQueue _queue;
    private readonly CurrentAccount _account;
    private readonly WorkflowAccess _access;

    public WorkflowsController(AppDbContext db, IExecutionQueue queue, CurrentAccount account, WorkflowAccess access)
    {
        _db = db;
        _queue = queue;
        _account = account;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<List<WorkflowSummaryDto>>> List()
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.WorkflowsView);

        var workflows = await _db.Workflows.AsNoTracking()
            .Where(w => w.UserId == acct.AccountId)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync();

        var locks = await _access.GetLocksAsync(workflows.Select(w => w.Id));
        var items = workflows.Select(w =>
        {
            var lockInfo = locks.GetValueOrDefault(w.Id);
            return new WorkflowSummaryDto(
                w.Id, w.Name, w.Description, w.IsActive, w.CreatedAt, w.UpdatedAt,
                lockInfo is not null, lockInfo?.LockedBy, lockInfo?.LockedAt);
        }).ToList();
        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<WorkflowDetailDto>> Create(CreateWorkflowRequest req)
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.WorkflowsCreate);

        var workflow = new Workflow
        {
            UserId = acct.AccountId,
            Name = string.IsNullOrWhiteSpace(req.Name) ? "Untitled Workflow" : req.Name.Trim(),
            Description = req.Description
        };
        var version = new WorkflowVersion
        {
            WorkflowId = workflow.Id,
            VersionNumber = 1,
            GraphJson = "{\"nodes\":[],\"connections\":[]}"
        };
        workflow.Versions.Add(version);
        workflow.ActiveVersionId = version.Id;

        _db.Workflows.Add(workflow);
        await _db.SaveChangesAsync();

        return Ok(await BuildDetail(workflow.Id));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkflowDetailDto>> Get(Guid id)
    {
        var detail = await BuildDetail(id);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkflowDetailDto>> Update(Guid id, UpdateWorkflowRequest req)
    {
        await _access.EnsureChangeAllowedAsync(id, Permissions.WorkflowsEdit);
        var workflow = await _access.FindVisibleAsync(id, includeVersions: true);
        if (workflow is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name)) workflow.Name = req.Name.Trim();
        workflow.Description = req.Description;
        workflow.UpdatedAt = DateTime.UtcNow;

        // Saving a new graph creates a new version (plan §5).
        if (!string.IsNullOrWhiteSpace(req.GraphJson))
        {
            var nextVersion = (workflow.Versions.Count == 0
                ? 0
                : workflow.Versions.Max(v => v.VersionNumber)) + 1;
            var version = new WorkflowVersion
            {
                WorkflowId = workflow.Id,
                VersionNumber = nextVersion,
                GraphJson = req.GraphJson!
            };
            _db.WorkflowVersions.Add(version);
            workflow.ActiveVersionId = version.Id;
        }

        await _db.SaveChangesAsync();
        return Ok(await BuildDetail(id));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _access.EnsureChangeAllowedAsync(id, Permissions.WorkflowsDelete);
        var workflow = await _access.FindVisibleAsync(id);
        if (workflow is null) return NotFound();
        _db.Workflows.Remove(workflow);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> SetActive(Guid id, [FromQuery] bool active = true)
    {
        await _access.EnsureChangeAllowedAsync(id, Permissions.WorkflowsEdit);
        var workflow = await _access.FindVisibleAsync(id);
        if (workflow is null) return NotFound();
        workflow.IsActive = active;
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { workflow.IsActive });
    }

    [HttpPost("{id:guid}/run")]
    public async Task<ActionResult<RunWorkflowResponse>> Run(Guid id, RunWorkflowRequest? req)
    {
        await _access.EnsureChangeAllowedAsync(id, Permissions.WorkflowsRun);
        var workflow = await _access.FindVisibleAsync(id);
        if (workflow is null) return NotFound();
        if (workflow.ActiveVersionId is null)
            return BadRequest(new { message = "Workflow has no saved version to run." });

        var execution = new Execution
        {
            WorkflowId = workflow.Id,
            WorkflowVersionId = workflow.ActiveVersionId.Value,
            Status = ExecutionStatus.Queued,
            TriggerType = TriggerType.Manual,
            TriggerDataJson = req?.TriggerDataJson
        };
        _db.Executions.Add(execution);
        await _db.SaveChangesAsync();

        await _queue.EnqueueAsync(execution.Id);
        return Ok(new RunWorkflowResponse(execution.Id));
    }

    /// <summary>Freezes the workflow. Anyone with edit permission may lock it.</summary>
    [HttpPost("{id:guid}/lock")]
    public async Task<ActionResult<WorkflowDetailDto>> Lock(Guid id)
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.WorkflowsEdit);
        var workflow = await _access.FindVisibleAsync(id);
        if (workflow is null) return NotFound();

        if (!await _access.IsLockedAsync(id))
        {
            _db.WorkflowLocks.Add(new WorkflowLock
            {
                WorkflowId = id,
                LockedByUserId = acct.UserId,
                LockedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        return Ok(await BuildDetail(id));
    }

    /// <summary>Unfreezes the workflow. Requires edit permission, the same as locking.</summary>
    [HttpPost("{id:guid}/unlock")]
    public async Task<ActionResult<WorkflowDetailDto>> Unlock(Guid id)
    {
        var acct = await _account.GetAsync();
        acct.Require(Permissions.WorkflowsEdit);
        var workflow = await _access.FindVisibleAsync(id);
        if (workflow is null) return NotFound();

        var existing = await _db.WorkflowLocks.FirstOrDefaultAsync(l => l.WorkflowId == id);
        if (existing is not null)
        {
            _db.WorkflowLocks.Remove(existing);
            await _db.SaveChangesAsync();
        }
        return Ok(await BuildDetail(id));
    }

    private async Task<WorkflowDetailDto?> BuildDetail(Guid id)
    {
        var workflow = await _access.FindVisibleAsync(id, includeVersions: true);
        if (workflow is null) return null;

        var active = workflow.Versions.FirstOrDefault(v => v.Id == workflow.ActiveVersionId)
                     ?? workflow.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

        var locks = await _access.GetLocksAsync(new[] { id });
        var lockInfo = locks.GetValueOrDefault(id);

        return new WorkflowDetailDto(
            workflow.Id, workflow.Name, workflow.Description, workflow.IsActive,
            workflow.ActiveVersionId,
            active?.GraphJson ?? "{\"nodes\":[],\"connections\":[]}",
            workflow.CreatedAt, workflow.UpdatedAt,
            lockInfo is not null, lockInfo?.LockedBy, lockInfo?.LockedAt);
    }
}
