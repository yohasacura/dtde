using Microsoft.Extensions.Logging;

namespace Dtde.Core.Transactions;

/// <summary>
/// High-performance logging messages for transaction operations using LoggerMessage source generation.
/// </summary>
internal static partial class TransactionLogMessages
{
    [LoggerMessage(
        EventId = 10001,
        Level = LogLevel.Debug,
        Message = "Beginning cross-shard transaction {TransactionId}")]
    public static partial void BeginningTransaction(ILogger logger, string transactionId);

    [LoggerMessage(
        EventId = 10002,
        Level = LogLevel.Debug,
        Message = "Enlisted shard {ShardId} in transaction {TransactionId}")]
    public static partial void EnlistedShard(ILogger logger, string shardId, string transactionId);

    [LoggerMessage(
        EventId = 10003,
        Level = LogLevel.Information,
        Message = "Cross-shard transaction {TransactionId} committed successfully across {ShardCount} shards")]
    public static partial void TransactionCommitted(ILogger logger, string transactionId, int shardCount);

    [LoggerMessage(
        EventId = 10004,
        Level = LogLevel.Error,
        Message = "Cross-shard transaction {TransactionId} failed during commit")]
    public static partial void TransactionCommitFailed(ILogger logger, Exception ex, string transactionId);

    [LoggerMessage(
        EventId = 10005,
        Level = LogLevel.Information,
        Message = "Cross-shard transaction {TransactionId} rolled back")]
    public static partial void TransactionRolledBack(ILogger logger, string transactionId);

    [LoggerMessage(
        EventId = 10006,
        Level = LogLevel.Warning,
        Message = "Error during automatic rollback of transaction {TransactionId}")]
    public static partial void RollbackError(ILogger logger, Exception ex, string transactionId);

    [LoggerMessage(
        EventId = 10007,
        Level = LogLevel.Debug,
        Message = "Transaction {TransactionId} prepare phase completed. All {Count} participants voted to commit.")]
    public static partial void PreparePhaseCompleted(ILogger logger, string transactionId, int count);

    [LoggerMessage(
        EventId = 10008,
        Level = LogLevel.Error,
        Message = "Failed to commit shard {ShardId} in transaction {TransactionId}")]
    public static partial void ShardCommitFailed(ILogger logger, Exception ex, string shardId, string transactionId);

    [LoggerMessage(
        EventId = 10009,
        Level = LogLevel.Warning,
        Message = "Failed to rollback shard {ShardId} in transaction {TransactionId}")]
    public static partial void ShardRollbackFailed(ILogger logger, Exception ex, string shardId, string transactionId);

    [LoggerMessage(
        EventId = 10010,
        Level = LogLevel.Warning,
        Message = "Cross-shard transaction {TransactionId} timed out in state {State}")]
    public static partial void TransactionTimedOut(ILogger logger, string transactionId, string state);

    [LoggerMessage(
        EventId = 10011,
        Level = LogLevel.Debug,
        Message = "Beginning cross-shard transaction {TransactionId} with isolation level {IsolationLevel}")]
    public static partial void BeginningTransactionWithOptions(ILogger logger, string transactionId, string isolationLevel);

    [LoggerMessage(
        EventId = 10012,
        Level = LogLevel.Error,
        Message = "Cross-shard transaction {TransactionId} failed")]
    public static partial void TransactionFailed(ILogger logger, Exception ex, string transactionId);

    [LoggerMessage(
        EventId = 10013,
        Level = LogLevel.Warning,
        Message = "Error during rollback of transaction {TransactionId}")]
    public static partial void TransactionRollbackError(ILogger logger, Exception ex, string transactionId);

    [LoggerMessage(
        EventId = 10014,
        Level = LogLevel.Information,
        Message = "Transaction recovery requested. No in-doubt transactions found.")]
    public static partial void NoInDoubtTransactions(ILogger logger);

    [LoggerMessage(
        EventId = 10015,
        Level = LogLevel.Warning,
        Message = "Transient error during cross-shard transaction (attempt {Attempt}/{MaxAttempts}). Retrying in {DelayMs}ms.")]
    public static partial void RetryingTransaction(ILogger logger, Exception ex, int attempt, int maxAttempts, double delayMs);
}
