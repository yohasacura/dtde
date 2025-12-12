namespace Dtde.Samples.Combined.Entities;

/// <summary>
/// Represents a regulatory document stored in pre-created manual tables.
/// Manual sharding allows for custom table naming and legacy integration.
/// Sharding configured via fluent API using ShardBy(DocumentType).
/// </summary>
public class RegulatoryDocument
{
    public long Id { get; set; }

    /// <summary>
    /// Document type determines which manual table is used.
    /// </summary>
    public required string DocumentType { get; set; }

    public required string DocumentId { get; set; }
    public required string Title { get; set; }
    public required string Region { get; set; }
    public required string Jurisdiction { get; set; }
    public DateTime EffectiveDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public required string Status { get; set; } // Draft, Active, Superseded, Expired
    public required string ContentHash { get; set; }
    public string? ContentUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public string? Metadata { get; set; }
}
