namespace Dtde.Samples.DateSharding.Entities;

/// <summary>
/// Metric data point sharded by timestamp.
/// Uses hourly partitioning for time-series metrics.
/// </summary>
public class MetricDataPoint
{
    /// <summary>
    /// Unique data point identifier.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Metric name/key.
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp - SHARD KEY.
    /// Used to route to time-based partitions.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Numeric value.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Tags for filtering (JSON).
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Source system.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Unit of measurement.
    /// </summary>
    public string? Unit { get; set; }
}
