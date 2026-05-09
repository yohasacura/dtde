using Dtde.Abstractions.Metadata;

using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Diagnostics;

/// <summary>
/// Source-generated <see cref="LoggerMessage"/> definitions for the EF Core layer.
/// Reserves event IDs <c>1000-9999</c>:
/// <list type="bullet">
///   <item><description><c>1000-1999</c> — query execution and shard routing.</description></item>
///   <item><description><c>2000-2999</c> — write routing.</description></item>
///   <item><description><c>3000-3999</c> — context factory lifecycle.</description></item>
///   <item><description><c>4000-4999</c> — temporal version operations.</description></item>
///   <item><description><c>6000-6999</c> — batch and entity-transfer operations.</description></item>
///   <item><description><c>7000-7999</c> — automatic cross-shard detection and transparent sessions.</description></item>
/// </list>
/// Cross-shard transaction lifecycle events (<c>10000-10199</c>) are emitted by
/// <see cref="Dtde.Core.Transactions.CrossShardTransactionCoordinator"/> via its
/// own dedicated <c>TransactionLogMessages</c>; do not duplicate them here.
/// </summary>
internal static partial class LogMessages
{
    // ShardedQueryExecutor messages
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "No shards found for entity type {EntityType}")]
    public static partial void NoShardsFound(ILogger logger, string entityType);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Executing query across {ShardCount} shards for {EntityType}")]
    public static partial void ExecutingQueryAcrossShards(ILogger logger, int shardCount, string entityType);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Query on shard {ShardId} returned {ResultCount} results")]
    public static partial void ShardQueryResults(ILogger logger, string shardId, int resultCount);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Error executing query on shard {ShardId}")]
    public static partial void ShardQueryError(ILogger logger, Exception ex, string shardId);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Debug,
        Message = "Executing query on shard {ShardId} (Mode: {StorageMode}, Table: {TableName})")]
    public static partial void ExecutingQueryOnShard(ILogger logger, string shardId, ShardStorageMode storageMode, string? tableName);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Debug,
        Message = "Querying table {TableName} for shard {ShardId}")]
    public static partial void QueryingTable(ILogger logger, string tableName, string shardId);

    // ShardWriteRouter messages
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Debug,
        Message = "Routing entity {EntityType} to shard {ShardId}")]
    public static partial void RoutingWriteToShard(ILogger logger, string entityType, string shardId);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Debug,
        Message = "Entity type {EntityType} is not sharded, using default hot shard")]
    public static partial void EntityNotSharded(ILogger logger, string entityType);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "No active hot shard found for date {Date}, using default")]
    public static partial void NoActiveHotShardForDate(ILogger logger, DateTime date);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Debug,
        Message = "Multiple shards available for date {Date}, selecting first hot shard")]
    public static partial void MultipleShardsForDate(ILogger logger, DateTime date);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Warning,
        Message = "Cannot write to inactive shard {ShardId}")]
    public static partial void CannotWriteToInactiveShard(ILogger logger, string shardId);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Warning,
        Message = "Cannot write to archive shard {ShardId}")]
    public static partial void CannotWriteToArchiveShard(ILogger logger, string shardId);

    [LoggerMessage(
        EventId = 2007,
        Level = LogLevel.Warning,
        Message = "Entity ValidFrom {ValidFrom} is outside shard {ShardId} date range [{Start}, {End})")]
    public static partial void DateOutsideShardRange(ILogger logger, DateTime validFrom, string shardId, DateTime start, DateTime end);

    // ShardContextFactory messages
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Debug,
        Message = "Creating DbContext for shard {ShardId} with mode {StorageMode}")]
    public static partial void CreatingContextForShard(ILogger logger, string shardId, ShardStorageMode storageMode);

    // NullShardContextFactory messages
    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Debug,
        Message = "NullShardContextFactory returning null context for shard {ShardId}")]
    public static partial void NullContextForShard(ILogger logger, string shardId);

    // DtdeUpdateProcessor messages
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Debug,
        Message = "Creating new version of {EntityType} effective from {EffectiveDate}")]
    public static partial void CreatingNewVersion(ILogger logger, string entityType, DateTime effectiveDate);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Information,
        Message = "Created new version of {EntityType} on shard {ShardId}")]
    public static partial void CreatedNewVersion(ILogger logger, string entityType, string shardId);

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Debug,
        Message = "Terminating {EntityType} as of {TerminationDate}")]
    public static partial void TerminatingEntity(ILogger logger, string entityType, DateTime terminationDate);

    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Information,
        Message = "Terminated {EntityType} on shard {ShardId}")]
    public static partial void TerminatedEntity(ILogger logger, string entityType, string shardId);

    [LoggerMessage(
        EventId = 4005,
        Level = LogLevel.Debug,
        Message = "Saving new {EntityType} effective from {EffectiveFrom}")]
    public static partial void SavingNewEntity(ILogger logger, string entityType, DateTime effectiveFrom);

    [LoggerMessage(
        EventId = 4006,
        Level = LogLevel.Information,
        Message = "Saved new {EntityType} to shard {ShardId}")]
    public static partial void SavedNewEntity(ILogger logger, string entityType, string shardId);

    [LoggerMessage(
        EventId = 4007,
        Level = LogLevel.Warning,
        Message = "Saving versions to different shards: {TerminatedShardId} and {NewShardId}. Consider enabling cross-shard transaction support.")]
    public static partial void SavingToDifferentShards(ILogger logger, string terminatedShardId, string newShardId);

    [LoggerMessage(
        EventId = 4008,
        Level = LogLevel.Information,
        Message = "Cross-shard transaction completed successfully between shards {ShardId1} and {ShardId2}")]
    public static partial void CrossShardTransactionCompleted(ILogger logger, string shardId1, string shardId2);

    // Cross-shard transaction lifecycle events live in
    // Dtde.Core.Transactions.TransactionLogMessages (event IDs 10000-10199).
    // The 5000 range is intentionally reserved for any future EF-Core-layer
    // cross-shard concerns that don't belong in the coordinator.

    // Batch operation messages
    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Debug,
        Message = "Starting batch operation '{Operation}' with {Count} entities")]
    public static partial void BatchOperationStarted(ILogger logger, string operation, int count);

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Information,
        Message = "Batch operation '{Operation}' completed successfully for {Count} entities")]
    public static partial void BatchOperationCompleted(ILogger logger, string operation, int count);

    [LoggerMessage(
        EventId = 6003,
        Level = LogLevel.Information,
        Message = "Batch '{Operation}' completed across {ShardCount} shards using cross-shard transaction ({EntityCount} entities)")]
    public static partial void BatchCrossShardCompleted(ILogger logger, string operation, int entityCount, int shardCount);

    [LoggerMessage(
        EventId = 6004,
        Level = LogLevel.Warning,
        Message = "Batch operation spans {ShardCount} shards but no transaction coordinator is registered. Operations will not be atomic.")]
    public static partial void BatchWithoutCoordinator(ILogger logger, int shardCount);

    // Entity transfer messages
    [LoggerMessage(
        EventId = 6010,
        Level = LogLevel.Debug,
        Message = "Transferring {EntityType} from shard {SourceShardId} to {TargetShardId}")]
    public static partial void TransferringEntity(ILogger logger, string entityType, string sourceShardId, string targetShardId);

    [LoggerMessage(
        EventId = 6011,
        Level = LogLevel.Information,
        Message = "{EntityType} transferred successfully from shard {SourceShardId} to {TargetShardId}")]
    public static partial void EntityTransferred(ILogger logger, string entityType, string sourceShardId, string targetShardId);

    [LoggerMessage(
        EventId = 6012,
        Level = LogLevel.Warning,
        Message = "Entity transfer from {SourceShardId} to {TargetShardId} without transaction coordinator. Operation may not be atomic.")]
    public static partial void TransferWithoutCoordinator(ILogger logger, string sourceShardId, string targetShardId);

    // Automatic cross-shard detection messages
    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Debug,
        Message = "Auto-promoting SaveChanges to cross-shard transaction: {EntityCount} entities across {ShardCount} shards")]
    public static partial void AutoPromotingToCrossShard(ILogger logger, int entityCount, int shardCount);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Information,
        Message = "Auto cross-shard SaveChanges completed: {SavedCount} entities across {ShardCount} shards")]
    public static partial void AutoCrossShardCompleted(ILogger logger, int savedCount, int shardCount);

    [LoggerMessage(
        EventId = 7003,
        Level = LogLevel.Warning,
        Message = "SaveChanges spans {ShardCount} shards but no cross-shard coordinator is registered. Falling back to sequential saves.")]
    public static partial void CrossShardWithoutCoordinator(ILogger logger, int shardCount);

    [LoggerMessage(
        EventId = 7004,
        Level = LogLevel.Debug,
        Message = "SaveChanges analysis: {EntityCount} entities, {ShardedCount} sharded, targeting {ShardCount} shard(s)")]
    public static partial void SaveChangesAnalysis(ILogger logger, int entityCount, int shardedCount, int shardCount);

    [LoggerMessage(
        EventId = 7005,
        Level = LogLevel.Debug,
        Message = "Explicit transaction detected. Skipping automatic cross-shard handling - user is managing transactions manually.")]
    public static partial void ExplicitTransactionDetected(ILogger logger);

    [LoggerMessage(
        EventId = 7006,
        Level = LogLevel.Warning,
        Message = "Cross-shard operation detected within explicit transaction. This may cause data inconsistency. Consider using ICrossShardTransactionCoordinator directly for multi-shard operations.")]
    public static partial void CrossShardInExplicitTransaction(ILogger logger);

    // Transparent sharding messages
    [LoggerMessage(
        EventId = 7010,
        Level = LogLevel.Debug,
        Message = "Started transparent cross-shard session for explicit transaction")]
    public static partial void TransparentTransactionStarted(ILogger logger);

    [LoggerMessage(
        EventId = 7011,
        Level = LogLevel.Information,
        Message = "Transparent transaction committed successfully across {ShardCount} shard(s)")]
    public static partial void TransparentTransactionCommitted(ILogger logger, int shardCount);

    [LoggerMessage(
        EventId = 7012,
        Level = LogLevel.Information,
        Message = "Transparent transaction rolled back across {ShardCount} shard(s)")]
    public static partial void TransparentTransactionRolledBack(ILogger logger, int shardCount);

    [LoggerMessage(
        EventId = 7013,
        Level = LogLevel.Debug,
        Message = "Transparent session saving {EntityCount} entities to shard {ShardId}")]
    public static partial void TransparentSessionSaving(ILogger logger, int entityCount, string shardId);
}
