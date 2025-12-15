namespace Dtde.Abstractions.Exceptions;

/// <summary>
/// Exception thrown when a shard cannot be found for a given criteria.
/// </summary>
public class ShardNotFoundException : DtdeException
{
    /// <summary>
    /// Creates a new ShardNotFoundException with a default message.
    /// </summary>
    public ShardNotFoundException() : base("The requested shard was not found.")
    {
    }

    /// <summary>
    /// Creates a new ShardNotFoundException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ShardNotFoundException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new ShardNotFoundException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ShardNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Base exception for all DTDE-specific exceptions.
/// </summary>
public class DtdeException : Exception
{
    /// <summary>
    /// Creates a new DtdeException with a default message.
    /// </summary>
    public DtdeException() : base("A DTDE operation failed.")
    {
    }

    /// <summary>
    /// Creates a new DtdeException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public DtdeException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new DtdeException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DtdeException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when metadata configuration is invalid.
/// </summary>
public class MetadataConfigurationException : DtdeException
{
    /// <summary>
    /// Creates a new MetadataConfigurationException with a default message.
    /// </summary>
    public MetadataConfigurationException() : base("Metadata configuration is invalid.")
    {
    }

    /// <summary>
    /// Creates a new MetadataConfigurationException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public MetadataConfigurationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new MetadataConfigurationException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public MetadataConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a temporal operation fails.
/// </summary>
public class TemporalOperationException : DtdeException
{
    /// <summary>
    /// Creates a new TemporalOperationException with a default message.
    /// </summary>
    public TemporalOperationException() : base("A temporal operation failed.")
    {
    }

    /// <summary>
    /// Creates a new TemporalOperationException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TemporalOperationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new TemporalOperationException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TemporalOperationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a version conflict is detected.
/// </summary>
public class VersionConflictException : DtdeException
{
    /// <summary>
    /// Gets the entity type involved in the conflict.
    /// </summary>
    public Type? EntityType { get; }

    /// <summary>
    /// Gets the entity key involved in the conflict.
    /// </summary>
    public object? EntityKey { get; }

    /// <summary>
    /// Creates a new VersionConflictException with a default message.
    /// </summary>
    public VersionConflictException() : base("A version conflict was detected.")
    {
    }

    /// <summary>
    /// Creates a new VersionConflictException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public VersionConflictException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new VersionConflictException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public VersionConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new VersionConflictException with entity details.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="entityType">The entity type involved.</param>
    /// <param name="entityKey">The entity key involved.</param>
    public VersionConflictException(string message, Type entityType, object? entityKey) : base(message)
    {
        EntityType = entityType;
        EntityKey = entityKey;
    }
}

/// <summary>
/// Exception thrown when a cross-shard transaction fails.
/// </summary>
public class CrossShardTransactionException : DtdeException
{
    /// <summary>
    /// Gets the transaction ID if available.
    /// </summary>
    public string? TransactionId { get; }

    /// <summary>
    /// Gets the shards involved in the transaction.
    /// </summary>
    public IReadOnlyCollection<string>? InvolvedShards { get; }

    /// <summary>
    /// Gets the shards that failed during the transaction.
    /// </summary>
    public IReadOnlyCollection<string>? FailedShards { get; }

    /// <summary>
    /// Creates a new CrossShardTransactionException with a default message.
    /// </summary>
    public CrossShardTransactionException() : base("A cross-shard transaction failed.")
    {
    }

    /// <summary>
    /// Creates a new CrossShardTransactionException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public CrossShardTransactionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new CrossShardTransactionException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CrossShardTransactionException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new CrossShardTransactionException with transaction details.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="transactionId">The transaction ID.</param>
    /// <param name="involvedShards">The shards involved.</param>
    /// <param name="failedShards">The shards that failed.</param>
    public CrossShardTransactionException(
        string message,
        string? transactionId,
        IReadOnlyCollection<string>? involvedShards = null,
        IReadOnlyCollection<string>? failedShards = null) : base(message)
    {
        TransactionId = transactionId;
        InvolvedShards = involvedShards;
        FailedShards = failedShards;
    }

    /// <summary>
    /// Creates a new CrossShardTransactionException with full details.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="transactionId">The transaction ID.</param>
    /// <param name="involvedShards">The shards involved.</param>
    /// <param name="failedShards">The shards that failed.</param>
    /// <param name="innerException">The inner exception.</param>
    public CrossShardTransactionException(
        string message,
        string? transactionId,
        IReadOnlyCollection<string>? involvedShards,
        IReadOnlyCollection<string>? failedShards,
        Exception innerException) : base(message, innerException)
    {
        TransactionId = transactionId;
        InvolvedShards = involvedShards;
        FailedShards = failedShards;
    }
}

/// <summary>
/// Exception thrown when a transaction prepare phase fails.
/// </summary>
public class TransactionPrepareException : CrossShardTransactionException
{
    /// <summary>
    /// Gets the shard that failed to prepare.
    /// </summary>
    public string? FailedShardId { get; }

    /// <summary>
    /// Creates a new TransactionPrepareException with a default message.
    /// </summary>
    public TransactionPrepareException() : base("Transaction prepare phase failed.")
    {
    }

    /// <summary>
    /// Creates a new TransactionPrepareException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TransactionPrepareException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new TransactionPrepareException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TransactionPrepareException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new TransactionPrepareException with shard details.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="transactionId">The transaction ID.</param>
    /// <param name="failedShardId">The shard that failed.</param>
    /// <param name="innerException">The inner exception.</param>
    public TransactionPrepareException(
        string message,
        string transactionId,
        string failedShardId,
        Exception? innerException = null) : base(message, transactionId, null, [failedShardId], innerException!)
    {
        FailedShardId = failedShardId;
    }
}

/// <summary>
/// Exception thrown when a transaction commit phase fails after prepare succeeded.
/// This indicates an in-doubt transaction that may require manual intervention.
/// </summary>
public class TransactionCommitException : CrossShardTransactionException
{
    /// <summary>
    /// Gets the shards that successfully committed.
    /// </summary>
    public IReadOnlyCollection<string>? CommittedShards { get; }

    /// <summary>
    /// Creates a new TransactionCommitException with a default message.
    /// </summary>
    public TransactionCommitException() : base("Transaction commit phase failed. The transaction may be in-doubt.")
    {
    }

    /// <summary>
    /// Creates a new TransactionCommitException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TransactionCommitException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new TransactionCommitException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TransactionCommitException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new TransactionCommitException with detailed commit status.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="transactionId">The transaction ID.</param>
    /// <param name="committedShards">Shards that successfully committed.</param>
    /// <param name="failedShards">Shards that failed to commit.</param>
    /// <param name="innerException">The inner exception.</param>
    public TransactionCommitException(
        string message,
        string transactionId,
        IReadOnlyCollection<string>? committedShards,
        IReadOnlyCollection<string>? failedShards,
        Exception? innerException = null)
        : base(message, transactionId, committedShards?.Concat(failedShards ?? []).ToList(), failedShards, innerException!)
    {
        CommittedShards = committedShards;
    }
}

/// <summary>
/// Exception thrown when a transaction times out.
/// </summary>
public class TransactionTimeoutException : CrossShardTransactionException
{
    /// <summary>
    /// Gets the configured timeout duration.
    /// </summary>
    public TimeSpan Timeout { get; }

    /// <summary>
    /// Creates a new TransactionTimeoutException with a default message.
    /// </summary>
    public TransactionTimeoutException() : base("The cross-shard transaction timed out.")
    {
    }

    /// <summary>
    /// Creates a new TransactionTimeoutException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TransactionTimeoutException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new TransactionTimeoutException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TransactionTimeoutException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new TransactionTimeoutException with timeout details.
    /// </summary>
    /// <param name="transactionId">The transaction ID.</param>
    /// <param name="timeout">The configured timeout.</param>
    public TransactionTimeoutException(string transactionId, TimeSpan timeout)
        : base($"Transaction '{transactionId}' timed out after {timeout.TotalSeconds:F1} seconds.")
    {
        Timeout = timeout;
    }
}
