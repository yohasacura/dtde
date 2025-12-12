using Dtde.Samples.DateSharding.Data;
using Dtde.Samples.DateSharding.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.DateSharding.Controllers;

/// <summary>
/// API controller demonstrating daily-sharded audit log queries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuditLogsController : ControllerBase
{
    private readonly DateShardingDbContext _context;
    private readonly ILogger<AuditLogsController> _logger;

    public AuditLogsController(
        DateShardingDbContext context,
        ILogger<AuditLogsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get audit logs for a specific day (single shard, most efficient).
    /// </summary>
    [HttpGet("day/{date}")]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> GetLogsForDay(
        DateTime date,
        [FromQuery] string? userId,
        [FromQuery] string? action,
        [FromQuery] int limit = 100)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        _logger.LogInformation("Querying audit logs for {Date} (single daily shard)", date.Date);

        var query = _context.AuditLogs
            .Where(l => l.Timestamp >= startOfDay && l.Timestamp < endOfDay);

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(l => l.UserId == userId);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(l => l.Action == action);

        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .Select(l => new AuditLogDto
            {
                Id = l.Id,
                Timestamp = l.Timestamp,
                UserId = l.UserId,
                Action = l.Action,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                CorrelationId = l.CorrelationId
            })
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>
    /// Get recent audit logs (today, queries hot shard).
    /// </summary>
    [HttpGet("recent")]
    public async Task<ActionResult<IEnumerable<AuditLogDto>>> GetRecentLogs(
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 100)
    {
        var since = DateTime.UtcNow.AddHours(-hours);

        _logger.LogInformation("Querying recent audit logs since {Since}", since);

        var logs = await _context.AuditLogs
            .Where(l => l.Timestamp >= since)
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .Select(l => new AuditLogDto
            {
                Id = l.Id,
                Timestamp = l.Timestamp,
                UserId = l.UserId,
                Action = l.Action,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                CorrelationId = l.CorrelationId
            })
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>
    /// Search audit logs across a date range.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<AuditLogSearchResult>> SearchLogs(
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] string? userId,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? correlationId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        _logger.LogInformation(
            "Searching audit logs from {From} to {To}",
            fromDate, toDate);

        var query = _context.AuditLogs
            .Where(l => l.Timestamp >= fromDate && l.Timestamp <= toDate);

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(l => l.UserId == userId);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(l => l.Action.Contains(action));

        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(l => l.EntityType == entityType);

        if (!string.IsNullOrEmpty(correlationId))
            query = query.Where(l => l.CorrelationId == correlationId);

        var totalCount = await query.CountAsync();

        var logs = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AuditLogDto
            {
                Id = l.Id,
                Timestamp = l.Timestamp,
                UserId = l.UserId,
                Action = l.Action,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                CorrelationId = l.CorrelationId
            })
            .ToListAsync();

        return Ok(new AuditLogSearchResult
        {
            Logs = logs,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            DaysSearched = (int)(toDate - fromDate).TotalDays + 1
        });
    }

    /// <summary>
    /// Create an audit log entry.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AuditLogDto>> CreateAuditLog(CreateAuditLogRequest request)
    {
        var timestamp = request.Timestamp ?? DateTime.UtcNow;

        var auditLog = new AuditLog
        {
            Timestamp = timestamp,
            UserId = request.UserId,
            Action = request.Action,
            EntityType = request.EntityType ?? string.Empty,
            EntityId = request.EntityId ?? string.Empty,
            OldValues = request.OldValues,
            NewValues = request.NewValues,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
            CorrelationId = request.CorrelationId ?? Guid.NewGuid().ToString()
        };

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetLogsForDay),
            new { date = timestamp.Date },
            new AuditLogDto
            {
                Id = auditLog.Id,
                Timestamp = auditLog.Timestamp,
                UserId = auditLog.UserId,
                Action = auditLog.Action,
                EntityType = auditLog.EntityType,
                EntityId = auditLog.EntityId,
                CorrelationId = auditLog.CorrelationId
            });
    }

    /// <summary>
    /// Get audit statistics by day.
    /// </summary>
    [HttpGet("stats/daily")]
    public async Task<ActionResult<IEnumerable<DailyAuditStats>>> GetDailyStats(
        [FromQuery] int days = 30)
    {
        var since = DateTime.UtcNow.Date.AddDays(-days);

        var stats = await _context.AuditLogs
            .Where(l => l.Timestamp >= since)
            .GroupBy(l => l.Timestamp.Date)
            .Select(g => new DailyAuditStats
            {
                Date = g.Key,
                TotalLogs = g.Count(),
                UniqueUsers = g.Select(l => l.UserId).Distinct().Count(),
                TopAction = g.GroupBy(l => l.Action)
                             .OrderByDescending(a => a.Count())
                             .Select(a => a.Key)
                             .FirstOrDefault() ?? "None"
            })
            .OrderByDescending(s => s.Date)
            .ToListAsync();

        return Ok(stats);
    }
}

// DTOs
public record AuditLogDto
{
    public long Id { get; init; }
    public DateTime Timestamp { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? CorrelationId { get; init; }
}

public record CreateAuditLogRequest
{
    public required string UserId { get; init; }
    public required string Action { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? CorrelationId { get; init; }
    public DateTime? Timestamp { get; init; }
}

public record AuditLogSearchResult
{
    public IEnumerable<AuditLogDto> Logs { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int DaysSearched { get; init; }
}

public record DailyAuditStats
{
    public DateTime Date { get; init; }
    public int TotalLogs { get; init; }
    public int UniqueUsers { get; init; }
    public string TopAction { get; init; } = string.Empty;
}
