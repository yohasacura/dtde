namespace Dtde.Samples.Combined.Entities;

/// <summary>
/// Represents a financial account with temporal versioning and regional sharding.
/// Demonstrates combining both features for regulatory compliance.
/// Sharding configured via fluent API using ShardBy(Region).
/// </summary>
public class Account
{
    public long Id { get; set; }

    /// <summary>
    /// Region determines the shard for regulatory data residency.
    /// </summary>
    public required string Region { get; set; }

    public required string AccountNumber { get; set; }
    public required string AccountType { get; set; } // Checking, Savings, Investment
    public required string Currency { get; set; }
    public decimal Balance { get; set; }
    public required string HolderId { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public required string Status { get; set; } // Active, Frozen, Closed
    
    // Temporal tracking (automatic)
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
