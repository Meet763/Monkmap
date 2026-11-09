using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Services;

namespace WorkflowPlatform.Api.Controllers;

/// <summary>Lists and removes the current user's connected accounts. Never returns tokens.</summary>
[ApiController]
[Authorize]
[Route("api/connections")]
public class ConnectionsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ConnectionsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var userId = User.GetUserId();
        var items = await _db.Connections
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Provider)
            .Select(c => new
            {
                c.Id,
                c.Provider,
                c.AccountName,
                c.ScopesJson,
                c.CreatedAt,
                expiresAt = c.ExpiresAt
            })
            .ToListAsync();

        var result = items.Select(c => new
        {
            c.Id,
            c.Provider,
            c.AccountName,
            scopes = Deserialize(c.ScopesJson),
            c.CreatedAt,
            c.expiresAt
        });
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = User.GetUserId();
        var connection = await _db.Connections.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (connection is null) return NotFound();
        _db.Connections.Remove(connection);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static string[] Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }
}
