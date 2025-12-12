using Dtde.Samples.Combined.Data;
using Dtde.Samples.Combined.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.Combined.Controllers;

/// <summary>
/// API controller demonstrating manual sharding for regulatory documents.
/// Documents are stored in pre-created tables based on document type.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private static readonly string[] DocumentShards = ["doc-policy", "doc-guideline", "doc-report"];

    private readonly CombinedDbContext _context;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        CombinedDbContext context,
        ILogger<DocumentsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get documents by type (single manual shard access).
    /// </summary>
    [HttpGet("type/{documentType}")]
    public async Task<ActionResult<IEnumerable<DocumentDto>>> GetDocumentsByType(
        string documentType,
        [FromQuery] string? region,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var shardId = $"doc-{documentType.ToLower()}";
        var tableName = documentType.ToLower() switch
        {
            "policy" => "Documents_Policies",
            "guideline" => "Documents_Guidelines",
            "report" => "Documents_Reports",
            _ => "unknown"
        };

        _logger.LogInformation(
            "Fetching {DocumentType} documents (manual shard: {ShardId}, table: {TableName})",
            documentType, shardId, tableName);

        var query = _context.RegulatoryDocuments
            .Where(d => d.DocumentType.ToLower() == documentType.ToLower());

        if (!string.IsNullOrEmpty(region))
            query = query.Where(d => d.Region == region);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(d => d.Status == status);

        var documents = await query
            .OrderByDescending(d => d.EffectiveDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DocumentDto
            {
                Id = d.Id,
                DocumentId = d.DocumentId,
                DocumentType = d.DocumentType,
                Title = d.Title,
                Region = d.Region,
                Jurisdiction = d.Jurisdiction,
                EffectiveDate = d.EffectiveDate,
                ExpirationDate = d.ExpirationDate,
                Status = d.Status
            })
            .ToListAsync();

        return Ok(documents);
    }

    /// <summary>
    /// Get a specific document.
    /// </summary>
    [HttpGet("{documentId}")]
    public async Task<ActionResult<DocumentDetailDto>> GetDocument(string documentId)
    {
        var document = await _context.RegulatoryDocuments
            .Where(d => d.DocumentId == documentId)
            .FirstOrDefaultAsync();

        if (document == null)
            return NotFound();

        return Ok(new DocumentDetailDto
        {
            Id = document.Id,
            DocumentId = document.DocumentId,
            DocumentType = document.DocumentType,
            Title = document.Title,
            Region = document.Region,
            Jurisdiction = document.Jurisdiction,
            EffectiveDate = document.EffectiveDate,
            ExpirationDate = document.ExpirationDate,
            Status = document.Status,
            ContentHash = document.ContentHash,
            ContentUrl = document.ContentUrl,
            CreatedAt = document.CreatedAt,
            LastReviewedAt = document.LastReviewedAt,
            ShardInfo = new DocumentShardInfo
            {
                ShardId = $"doc-{document.DocumentType.ToLower()}",
                TableName = document.DocumentType.ToLower() switch
                {
                    "policy" => "Documents_Policies",
                    "guideline" => "Documents_Guidelines",
                    "report" => "Documents_Reports",
                    _ => "unknown"
                },
                ShardingStrategy = "Manual"
            }
        });
    }

    /// <summary>
    /// Create a new regulatory document.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<DocumentDto>> CreateDocument(CreateDocumentRequest request)
    {
        var validTypes = new[] { "policy", "guideline", "report" };
        if (!validTypes.Contains(request.DocumentType.ToLower()))
        {
            return BadRequest($"Invalid document type. Valid types: {string.Join(", ", validTypes)}");
        }

        var shardId = $"doc-{request.DocumentType.ToLower()}";
        _logger.LogInformation(
            "Creating {DocumentType} document (manual shard: {ShardId})",
            request.DocumentType, shardId);

        var document = new RegulatoryDocument
        {
            DocumentType = request.DocumentType,
            DocumentId = $"DOC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            Title = request.Title,
            Region = request.Region,
            Jurisdiction = request.Jurisdiction,
            EffectiveDate = request.EffectiveDate,
            ExpirationDate = request.ExpirationDate,
            Status = "Draft",
            ContentHash = ComputeContentHash(request.Title),
            ContentUrl = request.ContentUrl,
            CreatedAt = DateTime.UtcNow
        };

        _context.RegulatoryDocuments.Add(document);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetDocument), new { documentId = document.DocumentId }, new DocumentDto
        {
            Id = document.Id,
            DocumentId = document.DocumentId,
            DocumentType = document.DocumentType,
            Title = document.Title,
            Region = document.Region,
            Jurisdiction = document.Jurisdiction,
            EffectiveDate = document.EffectiveDate,
            ExpirationDate = document.ExpirationDate,
            Status = document.Status
        });
    }

    /// <summary>
    /// Update document status.
    /// </summary>
    [HttpPatch("{documentId}/status")]
    public async Task<ActionResult> UpdateDocumentStatus(
        string documentId, UpdateStatusRequest request)
    {
        var document = await _context.RegulatoryDocuments
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);

        if (document == null)
            return NotFound();

        var oldStatus = document.Status;
        document.Status = request.Status;

        if (request.Status == "Active" && oldStatus == "Draft")
        {
            document.LastReviewedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Updated document {DocumentId} status from {OldStatus} to {NewStatus}",
            documentId, oldStatus, request.Status);

        return NoContent();
    }

    /// <summary>
    /// Search documents across all document types (fan-out).
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<DocumentSearchResult>> SearchDocuments(
        [FromQuery] string? query,
        [FromQuery] string? region,
        [FromQuery] string? jurisdiction,
        [FromQuery] DateTime? effectiveAfter,
        [FromQuery] int limit = 50)
    {
        _logger.LogInformation(
            "Searching documents across all manual shards (fan-out to Policy, Guideline, Report tables)");

        var dbQuery = _context.RegulatoryDocuments.AsQueryable();

        if (!string.IsNullOrEmpty(query))
            dbQuery = dbQuery.Where(d => d.Title.Contains(query));

        if (!string.IsNullOrEmpty(region))
            dbQuery = dbQuery.Where(d => d.Region == region);

        if (!string.IsNullOrEmpty(jurisdiction))
            dbQuery = dbQuery.Where(d => d.Jurisdiction == jurisdiction);

        if (effectiveAfter.HasValue)
            dbQuery = dbQuery.Where(d => d.EffectiveDate >= effectiveAfter.Value);

        var documents = await dbQuery
            .OrderByDescending(d => d.EffectiveDate)
            .Take(limit)
            .Select(d => new DocumentDto
            {
                Id = d.Id,
                DocumentId = d.DocumentId,
                DocumentType = d.DocumentType,
                Title = d.Title,
                Region = d.Region,
                Jurisdiction = d.Jurisdiction,
                EffectiveDate = d.EffectiveDate,
                ExpirationDate = d.ExpirationDate,
                Status = d.Status
            })
            .ToListAsync();

        return Ok(new DocumentSearchResult
        {
            Query = query,
            TotalResults = documents.Count,
            ShardsSearched = DocumentShards,
            Documents = documents
        });
    }

    /// <summary>
    /// Get document distribution by type.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<DocumentStats>> GetDocumentStats()
    {
        var stats = await _context.RegulatoryDocuments
            .GroupBy(d => d.DocumentType)
            .Select(g => new TypeStats
            {
                DocumentType = g.Key,
                Count = g.Count(),
                Active = g.Count(d => d.Status == "Active"),
                Draft = g.Count(d => d.Status == "Draft"),
                Expired = g.Count(d => d.Status == "Expired")
            })
            .ToListAsync();

        return Ok(new DocumentStats
        {
            TotalDocuments = stats.Sum(s => s.Count),
            TypeStats = stats.Select(s => new
            {
                s.DocumentType,
                s.Count,
                s.Active,
                s.Draft,
                s.Expired,
                ShardId = $"doc-{s.DocumentType.ToLower()}"
            })
        });
    }

    private static string ComputeContentHash(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

// DTOs
public record DocumentDto
{
    public long Id { get; init; }
    public string DocumentId { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Jurisdiction { get; init; } = string.Empty;
    public DateTime EffectiveDate { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public string Status { get; init; } = string.Empty;
}

public record DocumentDetailDto : DocumentDto
{
    public string ContentHash { get; init; } = string.Empty;
    public string? ContentUrl { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastReviewedAt { get; init; }
    public DocumentShardInfo? ShardInfo { get; init; }
}

public record DocumentShardInfo
{
    public string ShardId { get; init; } = string.Empty;
    public string TableName { get; init; } = string.Empty;
    public string ShardingStrategy { get; init; } = string.Empty;
}

public record CreateDocumentRequest
{
    public required string DocumentType { get; init; }
    public required string Title { get; init; }
    public required string Region { get; init; }
    public required string Jurisdiction { get; init; }
    public required DateTime EffectiveDate { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public string? ContentUrl { get; init; }
}

public record UpdateStatusRequest
{
    public required string Status { get; init; }
}

public record DocumentSearchResult
{
    public string? Query { get; init; }
    public int TotalResults { get; init; }
    public IEnumerable<string> ShardsSearched { get; init; } = [];
    public IEnumerable<DocumentDto> Documents { get; init; } = [];
}

public record DocumentStats
{
    public int TotalDocuments { get; init; }
    public object TypeStats { get; init; } = new object();
}

public record TypeStats
{
    public string DocumentType { get; init; } = string.Empty;
    public int Count { get; init; }
    public int Active { get; init; }
    public int Draft { get; init; }
    public int Expired { get; init; }
}
