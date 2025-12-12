namespace Dtde.Samples.DateSharding.Entities;

/// <summary>
/// Audit log entity sharded by timestamp.
/// Uses daily partitioning for high-volume logging scenarios.
/// </summary>
public class AuditLog
{
    /// <summary>
    /// Unique log entry identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Timestamp - SHARD KEY.
    /// Used to route to daily tables.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User who performed the action.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Action performed.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Entity type affected.
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Entity ID affected.
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Old values (JSON).
    /// </summary>
    public string? OldValues { get; set; }

    /// <summary>
    /// New values (JSON).
    /// </summary>
    public string? NewValues { get; set; }

    /// <summary>
    /// IP address of the client.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public string? CorrelationId { get; set; }
}
