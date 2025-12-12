using Dtde.Sample.WebApi.Data;
using Dtde.Sample.WebApi.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Sample.WebApi.Controllers;

/// <summary>
/// API controller for audit logs.
/// Demonstrates REGULAR EF Core usage (no temporal versioning).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuditLogsController : ControllerBase
{
    private readonly SampleDbContext _context;

    public AuditLogsController(SampleDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Gets audit logs with optional filtering.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditLog>>> GetAuditLogs(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? entityType = null,
        [FromQuery] int? entityId = null)
    {
        // Standard EF Core query with filtering
        var query = _context.AuditLogs.AsQueryable();

        if (from.HasValue)
        {
            query = query.Where(l => l.Timestamp >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(l => l.Timestamp <= to.Value);
        }

        if (!string.IsNullOrEmpty(entityType))
        {
            query = query.Where(l => l.EntityType == entityType);
        }

        if (entityId.HasValue)
        {
            query = query.Where(l => l.EntityId == entityId.Value);
        }

        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            .Take(100)
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>
    /// Gets audit logs for a specific entity.
    /// </summary>
    [HttpGet("entity/{entityType}/{entityId}")]
    public async Task<ActionResult<IEnumerable<AuditLog>>> GetEntityAuditLogs(
        string entityType,
        int entityId)
    {
        var logs = await _context.AuditLogs
            .Where(l => l.EntityType == entityType && l.EntityId == entityId)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();

        return Ok(logs);
    }
}
