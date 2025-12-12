namespace Dtde.Samples.Combined.Entities;

/// <summary>
/// Represents an audit record with hash-based sharding for even distribution.
/// Combines temporal awareness with hash sharding for compliance tracking.
/// Sharding configured via fluent API using ShardByHash(EntityReference).
/// </summary>
public class ComplianceAudit
{
    public long Id { get; set; }

    /// <summary>
    /// Entity reference determines the shard via hashing.
    /// </summary>
    public required string EntityReference { get; set; }

    public required string EntityType { get; set; } // Account, Transaction, Document
    public required string AuditType { get; set; } // Creation, Modification, Deletion, Access
    public required string PerformedBy { get; set; }
    public DateTime PerformedAt { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? Reason { get; set; }
    public string? IpAddress { get; set; }
    public string? SessionId { get; set; }
    public bool RequiresReview { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}
