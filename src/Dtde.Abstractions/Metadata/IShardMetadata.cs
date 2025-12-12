namespace Dtde.Abstractions.Metadata;

/// <summary>
/// Shard tier classification for data temperature.
/// Used to prioritize query execution and storage optimization.
/// </summary>
public enum ShardTier
{
    /// <summary>
    /// Recent data, frequently accessed. Prioritized in queries.
    /// Typically stored on faster storage.
    /// </summary>
    Hot,

    /// <summary>
    /// Historical data with moderate access frequency.
    /// May be stored on standard storage.
    /// </summary>
    Warm,

    /// <summary>
    /// Archived data, rarely accessed.
    /// May have slower storage and delayed query responses.
    /// </summary>
    Cold,

    /// <summary>
    /// Archive tier for long-term storage with minimal access.
    /// Typically read-only and may have significant latency.
    /// </summary>
    Archive
}

/// <summary>
/// Represents a date range for shard partitioning.
/// Uses inclusive start and exclusive end semantics.
/// </summary>
/// <param name="Start">The inclusive start date of the range.</param>
/// <param name="End">The exclusive end date of the range.</param>
public readonly record struct DateRange(DateTime Start, DateTime End)
{
    /// <summary>
    /// Determines if a date falls within this range.
    /// </summary>
    /// <param name="date">The date to check.</param>
    /// <returns>True if date is within [Start, End).</returns>
    public bool Contains(DateTime date) => date >= Start && date < End;

    /// <summary>
    /// Determines if this range intersects with another range.
    /// </summary>
    /// <param name="other">The other range to check.</param>
    /// <returns>True if ranges overlap.</returns>
    public bool Intersects(DateRange other) => Start < other.End && End > other.Start;

    /// <summary>
    /// Creates a new DateRange representing the intersection of this range with another.
    /// </summary>
    /// <param name="other">The other range to intersect with.</param>
    /// <returns>The intersection range, or null if ranges don't overlap.</returns>
    public DateRange? Intersection(DateRange other)
    {
        if (!Intersects(other))
        {
            return null;
        }

        return new DateRange(
            Start > other.Start ? Start : other.Start,
            End < other.End ? End : other.End);
    }
}

/// <summary>
/// Represents a key range for shard partitioning.
/// </summary>
public readonly record struct KeyRange
{
    /// <summary>
    /// Gets the inclusive minimum key value.
    /// </summary>
    public object? MinValue { get; init; }

    /// <summary>
    /// Gets the exclusive maximum key value.
    /// </summary>
    public object? MaxValue { get; init; }

    /// <summary>
    /// Gets the key type for comparison.
    /// </summary>
    public Type KeyType { get; init; }

    /// <summary>
    /// Creates a new KeyRange with the specified values.
    /// </summary>
    /// <param name="minValue">The inclusive minimum value.</param>
    /// <param name="maxValue">The exclusive maximum value.</param>
    /// <param name="keyType">The type of the key.</param>
    public KeyRange(object? minValue, object? maxValue, Type keyType)
    {
        MinValue = minValue;
        MaxValue = maxValue;
        KeyType = keyType;
    }
}

/// <summary>
/// Metadata for a single shard (table or database).
/// </summary>
public interface IShardMetadata
{
    /// <summary>
    /// Gets the unique shard identifier.
    /// </summary>
    /// <example>Orders_2024, Customers_EU, Shard_003</example>
    string ShardId { get; }

    /// <summary>
    /// Gets the display name for logging and diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the storage mode for this shard.
    /// </summary>
    ShardStorageMode StorageMode { get; }

    /// <summary>
    /// Gets the table name (for Table/Manual mode).
    /// </summary>
    string? TableName { get; }

    /// <summary>
    /// Gets the schema name (for Table/Manual mode).
    /// </summary>
    string? SchemaName { get; }

    /// <summary>
    /// Gets the connection string (for Database mode).
    /// </summary>
    string? ConnectionString { get; }

    /// <summary>
    /// Gets the shard key value that this shard handles.
    /// E.g., "EU", "US", "2024" for property-based sharding.
    /// </summary>
    string? ShardKeyValue { get; }

    /// <summary>
    /// Gets the optional date range this shard covers.
    /// </summary>
    DateRange? DateRange { get; }

    /// <summary>
    /// Gets the optional key range this shard covers.
    /// </summary>
    KeyRange? KeyRange { get; }

    /// <summary>
    /// Gets the shard tier for hot/warm/cold classification.
    /// </summary>
    ShardTier Tier { get; }

    /// <summary>
    /// Gets whether this shard is read-only (e.g., archive).
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Gets the priority for query ordering (lower = higher priority).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Gets whether this shard is active and available for queries/writes.
    /// </summary>
    bool IsActive => !IsReadOnly;
}
