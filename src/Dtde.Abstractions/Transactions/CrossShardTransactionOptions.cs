namespace Dtde.Abstractions.Transactions;

/// <summary>
/// Configuration options for cross-shard transactions.
/// </summary>
public sealed class CrossShardTransactionOptions
{
    /// <summary>
    /// Gets or sets the default transaction timeout.
    /// </summary>
    public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the default isolation level.
    /// </summary>
    public static CrossShardIsolationLevel DefaultIsolationLevel { get; set; } = CrossShardIsolationLevel.ReadCommitted;

    /// <summary>
    /// Gets or sets the transaction timeout.
    /// </summary>
    /// <remarks>
    /// If the transaction does not complete within this duration,
    /// it will be automatically rolled back.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = DefaultTimeout;

    /// <summary>
    /// Gets or sets the isolation level for the transaction.
    /// </summary>
    public CrossShardIsolationLevel IsolationLevel { get; set; } = DefaultIsolationLevel;

    /// <summary>
    /// Gets or sets whether to enable automatic retry on transient failures.
    /// </summary>
    public bool EnableRetry { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the delay between retry attempts.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets whether to use exponential backoff for retries.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum delay between retries when using exponential backoff.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets a descriptive name for the transaction (for logging/diagnostics).
    /// </summary>
    public string? TransactionName { get; set; }

    /// <summary>
    /// Gets or sets whether to persist transaction state for recovery.
    /// </summary>
    /// <remarks>
    /// When enabled, transaction state is persisted to allow recovery
    /// of in-doubt transactions after a coordinator failure.
    /// </remarks>
    public bool EnableRecovery { get; set; }

    /// <summary>
    /// Creates a new instance with default values.
    /// </summary>
    public static CrossShardTransactionOptions Default => new();

    /// <summary>
    /// Creates options optimized for short-lived transactions.
    /// </summary>
    public static CrossShardTransactionOptions ShortLived => new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxRetryAttempts = 2,
        EnableRecovery = false
    };

    /// <summary>
    /// Creates options optimized for long-running transactions.
    /// </summary>
    public static CrossShardTransactionOptions LongRunning => new()
    {
        Timeout = TimeSpan.FromMinutes(5),
        MaxRetryAttempts = 5,
        EnableRecovery = true
    };
}
