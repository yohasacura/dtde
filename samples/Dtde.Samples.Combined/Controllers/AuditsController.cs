using Dtde.Samples.Combined.Data;
using Dtde.Samples.Combined.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.Combined.Controllers;

/// <summary>
/// API controller demonstrating hash-based sharding for compliance audits.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuditsController : ControllerBase
{
    private readonly CombinedDbContext _context;
    private readonly ILogger<AuditsController> _logger;

    public AuditsController(
        CombinedDbContext context,
        ILogger<AuditsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get audit records for a specific entity (single shard via hash).
    /// </summary>
    [HttpGet("entity/{entityReference}")]
    public async Task<ActionResult<IEnumerable<AuditDto>>> GetEntityAudits(
        string entityReference,
        [FromQuery] string? auditType,
        [FromQuery] int limit = 50)
    {
        var shardIndex = GetShardIndex(entityReference);
        _logger.LogInformation(
            "Fetching audits for entity {EntityReference} from shard {ShardIndex} (hash-based routing)",
            entityReference, shardIndex);

        var query = _context.ComplianceAudits
            .Where(a => a.EntityReference == entityReference);

        if (!string.IsNullOrEmpty(auditType))
            query = query.Where(a => a.AuditType == auditType);

        var audits = await query
            .OrderByDescending(a => a.PerformedAt)
            .Take(limit)
            .Select(a => new AuditDto
            {
                Id = a.Id,
                EntityReference = a.EntityReference,
                EntityType = a.EntityType,
                AuditType = a.AuditType,
                PerformedBy = a.PerformedBy,
                PerformedAt = a.PerformedAt,
                RequiresReview = a.RequiresReview,
                ReviewedAt = a.ReviewedAt
            })
            .ToListAsync();

        return Ok(audits);
    }

    /// <summary>
    /// Get audit details with change data.
    /// </summary>
    [HttpGet("{auditId}")]
    public async Task<ActionResult<AuditDetailDto>> GetAudit(long auditId)
    {
        var audit = await _context.ComplianceAudits
            .Where(a => a.Id == auditId)
            .FirstOrDefaultAsync();

        if (audit == null)
            return NotFound();

        return Ok(new AuditDetailDto
        {
            Id = audit.Id,
            EntityReference = audit.EntityReference,
            EntityType = audit.EntityType,
            AuditType = audit.AuditType,
            PerformedBy = audit.PerformedBy,
            PerformedAt = audit.PerformedAt,
            OldValues = audit.OldValues,
            NewValues = audit.NewValues,
            Reason = audit.Reason,
            IpAddress = audit.IpAddress,
            SessionId = audit.SessionId,
            RequiresReview = audit.RequiresReview,
            ReviewedAt = audit.ReviewedAt,
            ReviewedBy = audit.ReviewedBy,
            ShardInfo = new AuditShardInfo
            {
                ShardIndex = GetShardIndex(audit.EntityReference),
                ShardId = $"audit-shard-{GetShardIndex(audit.EntityReference)}",
                TableName = $"ComplianceAudits_Shard{GetShardIndex(audit.EntityReference)}"
            }
        });
    }

    /// <summary>
    /// Create an audit record.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AuditDto>> CreateAudit(CreateAuditRequest request)
    {
        var shardIndex = GetShardIndex(request.EntityReference);
        _logger.LogInformation(
            "Creating audit for entity {EntityReference} in shard {ShardIndex}",
            request.EntityReference, shardIndex);

        var audit = new ComplianceAudit
        {
            EntityReference = request.EntityReference,
            EntityType = request.EntityType,
            AuditType = request.AuditType,
            PerformedBy = request.PerformedBy,
            PerformedAt = DateTime.UtcNow,
            OldValues = request.OldValues,
            NewValues = request.NewValues,
            Reason = request.Reason,
            IpAddress = request.IpAddress,
            SessionId = request.SessionId,
            RequiresReview = request.RequiresReview ?? false
        };

        _context.ComplianceAudits.Add(audit);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAudit), new { auditId = audit.Id }, new AuditDto
        {
            Id = audit.Id,
            EntityReference = audit.EntityReference,
            EntityType = audit.EntityType,
            AuditType = audit.AuditType,
            PerformedBy = audit.PerformedBy,
            PerformedAt = audit.PerformedAt,
            RequiresReview = audit.RequiresReview
        });
    }

    /// <summary>
    /// Get audits requiring review (fan-out across all hash shards).
    /// </summary>
    [HttpGet("pending-review")]
    public async Task<ActionResult<IEnumerable<AuditDto>>> GetPendingReviewAudits(
        [FromQuery] int limit = 100)
    {
        _logger.LogInformation(
            "Fetching audits requiring review (fan-out across 4 hash shards)");

        var audits = await _context.ComplianceAudits
            .Where(a => a.RequiresReview && a.ReviewedAt == null)
            .OrderByDescending(a => a.PerformedAt)
            .Take(limit)
            .Select(a => new AuditDto
            {
                Id = a.Id,
                EntityReference = a.EntityReference,
                EntityType = a.EntityType,
                AuditType = a.AuditType,
                PerformedBy = a.PerformedBy,
                PerformedAt = a.PerformedAt,
                RequiresReview = a.RequiresReview
            })
            .ToListAsync();

        return Ok(audits);
    }

    /// <summary>
    /// Mark an audit as reviewed.
    /// </summary>
    [HttpPost("{auditId}/review")]
    public async Task<ActionResult> ReviewAudit(long auditId, ReviewAuditRequest request)
    {
        var audit = await _context.ComplianceAudits
            .FirstOrDefaultAsync(a => a.Id == auditId);

        if (audit == null)
            return NotFound();

        audit.ReviewedAt = DateTime.UtcNow;
        audit.ReviewedBy = request.ReviewedBy;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Audit {AuditId} reviewed by {ReviewedBy}",
            auditId, request.ReviewedBy);

        return NoContent();
    }

    /// <summary>
    /// Get recent audits (fan-out).
    /// </summary>
    [HttpGet("recent")]
    public async Task<ActionResult<IEnumerable<AuditDto>>> GetRecentAudits(
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 100,
        [FromQuery] string? entityType = null,
        [FromQuery] string? auditType = null)
    {
        var since = DateTime.UtcNow.AddHours(-hours);

        _logger.LogInformation(
            "Fetching recent audits since {Since} (fan-out across hash shards)",
            since);

        var query = _context.ComplianceAudits
            .Where(a => a.PerformedAt >= since);

        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(a => a.EntityType == entityType);

        if (!string.IsNullOrEmpty(auditType))
            query = query.Where(a => a.AuditType == auditType);

        var audits = await query
            .OrderByDescending(a => a.PerformedAt)
            .Take(limit)
            .Select(a => new AuditDto
            {
                Id = a.Id,
                EntityReference = a.EntityReference,
                EntityType = a.EntityType,
                AuditType = a.AuditType,
                PerformedBy = a.PerformedBy,
                PerformedAt = a.PerformedAt,
                RequiresReview = a.RequiresReview,
                ReviewedAt = a.ReviewedAt
            })
            .ToListAsync();

        return Ok(audits);
    }

    /// <summary>
    /// Get shard distribution statistics.
    /// </summary>
    [HttpGet("stats/distribution")]
    public async Task<ActionResult<ShardDistributionStats>> GetShardDistribution()
    {
        _logger.LogInformation("Calculating hash shard distribution statistics");

        var allAudits = await _context.ComplianceAudits
            .Select(a => a.EntityReference)
            .ToListAsync();

        var distribution = allAudits
            .GroupBy(GetShardIndex)
            .Select(g => new ShardStat
            {
                ShardIndex = g.Key,
                ShardId = $"audit-shard-{g.Key}",
                AuditCount = g.Count()
            })
            .OrderBy(s => s.ShardIndex)
            .ToList();

        var counts = distribution.Select(d => (double)d.AuditCount).ToList();
        var mean = counts.Count > 0 ? counts.Average() : 0;
        var variance = counts.Count > 0
            ? counts.Sum(c => Math.Pow(c - mean, 2)) / counts.Count
            : 0;
        var stdDev = Math.Sqrt(variance);

        return Ok(new ShardDistributionStats
        {
            TotalAudits = allAudits.Count,
            ShardCount = 4,
            Distribution = distribution,
            StandardDeviation = stdDev,
            DistributionQuality = stdDev < mean * 0.1 ? "Excellent" :
                                  stdDev < mean * 0.25 ? "Good" :
                                  stdDev < mean * 0.5 ? "Fair" : "Poor"
        });
    }

    /// <summary>
    /// Lookup which shard an entity would be routed to.
    /// </summary>
    [HttpGet("shard-lookup/{entityReference}")]
    public ActionResult<ShardLookupResult> GetShardForEntity(string entityReference)
    {
        var shardIndex = GetShardIndex(entityReference);

        return Ok(new ShardLookupResult
        {
            EntityReference = entityReference,
            ShardIndex = shardIndex,
            ShardId = $"audit-shard-{shardIndex}",
            TableName = $"ComplianceAudits_Shard{shardIndex}",
            HashValue = entityReference.GetHashCode()
        });
    }

    private static int GetShardIndex(string entityReference)
    {
        const int shardCount = 4;
        var hash = entityReference.GetHashCode();
        return Math.Abs(hash % shardCount);
    }
}

// DTOs
public record AuditDto
{
    public long Id { get; init; }
    public string EntityReference { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string AuditType { get; init; } = string.Empty;
    public string PerformedBy { get; init; } = string.Empty;
    public DateTime PerformedAt { get; init; }
    public bool RequiresReview { get; init; }
    public DateTime? ReviewedAt { get; init; }
}

public record AuditDetailDto : AuditDto
{
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public string? Reason { get; init; }
    public string? IpAddress { get; init; }
    public string? SessionId { get; init; }
    public string? ReviewedBy { get; init; }
    public AuditShardInfo? ShardInfo { get; init; }
}

public record AuditShardInfo
{
    public int ShardIndex { get; init; }
    public string ShardId { get; init; } = string.Empty;
    public string TableName { get; init; } = string.Empty;
}

public record CreateAuditRequest
{
    public required string EntityReference { get; init; }
    public required string EntityType { get; init; }
    public required string AuditType { get; init; }
    public required string PerformedBy { get; init; }
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public string? Reason { get; init; }
    public string? IpAddress { get; init; }
    public string? SessionId { get; init; }
    public bool? RequiresReview { get; init; }
}

public record ReviewAuditRequest
{
    public required string ReviewedBy { get; init; }
}

public record ShardDistributionStats
{
    public int TotalAudits { get; init; }
    public int ShardCount { get; init; }
    public IEnumerable<ShardStat> Distribution { get; init; } = [];
    public double StandardDeviation { get; init; }
    public string DistributionQuality { get; init; } = string.Empty;
}

public record ShardStat
{
    public int ShardIndex { get; init; }
    public string ShardId { get; init; } = string.Empty;
    public int AuditCount { get; init; }
}

public record ShardLookupResult
{
    public string EntityReference { get; init; } = string.Empty;
    public int ShardIndex { get; init; }
    public string ShardId { get; init; } = string.Empty;
    public string TableName { get; init; } = string.Empty;
    public int HashValue { get; init; }
}
