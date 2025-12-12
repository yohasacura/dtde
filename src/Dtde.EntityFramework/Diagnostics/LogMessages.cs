using Dtde.Abstractions.Metadata;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Diagnostics;

/// <summary>
/// High-performance logging messages using LoggerMessage source generation.
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
        Message = "Saving versions to different shards: {TerminatedShardId} and {NewShardId}. Consider implementing distributed transaction support.")]
    public static partial void SavingToDifferentShards(ILogger logger, string terminatedShardId, string newShardId);
}
