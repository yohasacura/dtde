# DTDE Development Plan - Update Engine

[← Back to Query Engine](04-query-engine.md) | [Next: Configuration & API →](06-configuration-api.md)

---

## 1. Update Engine Overview

The Update Engine transforms standard EF Core change tracking operations into temporal versioning operations. When the application calls `SaveChanges()`, DTDE intercepts temporal entities and applies version-bump semantics across the appropriate shards.

### 1.1 Update Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Application Code                                      │
│  var entity = await db.Contracts.FindAsync(id);                             │
│  entity.Amount = newAmount;                                                  │
│  await db.SaveChangesAsync();                                                │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  1. ChangeTracker Analysis                                                   │
│     • Identify temporal entities with changes                                │
│     • Group by operation type (Add/Modify/Delete)                            │
│     • Extract current and original values                                    │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  2. Version Manager                                                          │
│     • For Added: Validate no overlapping version exists                      │
│     • For Modified: Create version bump (close old, create new)              │
│     • For Deleted: Close validity period (soft delete)                       │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  3. Shard Write Router                                                       │
│     • Determine target shard for new version                                 │
│     • Determine source shard for old version                                 │
│     • Handle cross-shard version bumps                                       │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  4. Distributed Write Executor                                               │
│     • Execute invalidation on source shard                                   │
│     • Execute insertion on target shard                                      │
│     • Handle consistency (best-effort or transactional)                      │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Update Processor Interface

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Processes EF Core change tracking entries for temporal entities.
/// </summary>
public interface IDtdeUpdateProcessor
{
    /// <summary>
    /// Processes temporal entity updates from the ChangeTracker.
    /// </summary>
    /// <param name="context">The source DbContext.</param>
    /// <param name="entries">The change tracking entries to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of affected entities.</returns>
    Task<int> ProcessUpdatesAsync(
        DbContext context,
        IReadOnlyList<EntityEntry> entries,
        CancellationToken cancellationToken = default);
}
```

---

## 3. Update Processor Implementation

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Processes temporal entity updates with version-bump semantics.
/// </summary>
public sealed class DtdeUpdateProcessor : IDtdeUpdateProcessor
{
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly IVersionManager _versionManager;
    private readonly IShardWriteRouter _shardWriteRouter;
    private readonly IDistributedWriteExecutor _writeExecutor;
    private readonly IDtdeDiagnostics _diagnostics;
    private readonly ILogger<DtdeUpdateProcessor> _logger;
    
    public DtdeUpdateProcessor(
        IMetadataRegistry metadataRegistry,
        IVersionManager versionManager,
        IShardWriteRouter shardWriteRouter,
        IDistributedWriteExecutor writeExecutor,
        IDtdeDiagnostics diagnostics,
        ILogger<DtdeUpdateProcessor> logger)
    {
        _metadataRegistry = metadataRegistry;
        _versionManager = versionManager;
        _shardWriteRouter = shardWriteRouter;
        _writeExecutor = writeExecutor;
        _diagnostics = diagnostics;
        _logger = logger;
    }
    
    public async Task<int> ProcessUpdatesAsync(
        DbContext context,
        IReadOnlyList<EntityEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = Stopwatch.StartNew();
        var affectedCount = 0;
        
        _logger.LogInformation(
            "[{CorrelationId}] Processing {Count} temporal entity updates",
            correlationId,
            entries.Count);
        
        // Group entries by entity type and operation
        var operations = new List<VersionOperation>();
        
        foreach (var entry in entries)
        {
            var entityType = entry.Entity.GetType();
            var metadata = _metadataRegistry.GetEntityMetadata(entityType);
            
            if (metadata?.IsTemporal != true)
            {
                _logger.LogWarning(
                    "[{CorrelationId}] Entity {EntityType} is not temporal, skipping",
                    correlationId,
                    entityType.Name);
                continue;
            }
            
            var operation = entry.State switch
            {
                EntityState.Added => CreateAddOperation(entry, metadata, correlationId),
                EntityState.Modified => CreateModifyOperation(entry, metadata, correlationId),
                EntityState.Deleted => CreateDeleteOperation(entry, metadata, correlationId),
                _ => null
            };
            
            if (operation is not null)
            {
                operations.Add(operation);
            }
        }
        
        // Process operations through version manager
        var processedOperations = await _versionManager.ProcessOperationsAsync(
            operations,
            cancellationToken);
        
        // Route to shards
        var writeCommands = await _shardWriteRouter.RouteWritesAsync(
            processedOperations,
            cancellationToken);
        
        // Execute writes
        affectedCount = await _writeExecutor.ExecuteAsync(
            writeCommands,
            correlationId,
            cancellationToken);
        
        // Detach processed entries (they've been handled by DTDE)
        foreach (var entry in entries)
        {
            entry.State = EntityState.Detached;
        }
        
        stopwatch.Stop();
        
        _logger.LogInformation(
            "[{CorrelationId}] Completed processing {Count} updates in {Duration}ms",
            correlationId,
            affectedCount,
            stopwatch.ElapsedMilliseconds);
        
        return affectedCount;
    }
    
    private VersionOperation CreateAddOperation(
        EntityEntry entry,
        EntityMetadata metadata,
        string correlationId)
    {
        var validity = metadata.Validity!;
        var entity = entry.Entity;
        
        var validFrom = (DateTime)validity.ValidFromProperty.GetValue(entity)!;
        var validTo = validity.ValidToProperty is not null
            ? (DateTime?)validity.ValidToProperty.GetValue(entity)
            : null;
        
        _logger.LogDebug(
            "[{CorrelationId}] Creating ADD operation for {EntityType}, ValidFrom={ValidFrom}",
            correlationId,
            metadata.ClrType.Name,
            validFrom);
        
        return new VersionOperation
        {
            OperationType = VersionOperationType.Create,
            EntityMetadata = metadata,
            Entity = entity,
            PrimaryKeyValue = metadata.PrimaryKey.GetValue(entity),
            NewValidFrom = validFrom,
            NewValidTo = validTo ?? validity.OpenEndedValue
        };
    }
    
    private VersionOperation CreateModifyOperation(
        EntityEntry entry,
        EntityMetadata metadata,
        string correlationId)
    {
        var validity = metadata.Validity!;
        var entity = entry.Entity;
        
        // Get original values (the version being superseded)
        var originalValidFrom = (DateTime)entry.OriginalValues[validity.ValidFromProperty.PropertyName]!;
        var originalValidTo = validity.ValidToProperty is not null
            ? (DateTime?)entry.OriginalValues[validity.ValidToProperty.PropertyName]
            : null;
        
        // New version starts now (or use a configured timestamp provider)
        var versionBumpDate = DateTime.UtcNow;
        
        _logger.LogDebug(
            "[{CorrelationId}] Creating MODIFY operation for {EntityType}, bumping at {BumpDate}",
            correlationId,
            metadata.ClrType.Name,
            versionBumpDate);
        
        return new VersionOperation
        {
            OperationType = VersionOperationType.VersionBump,
            EntityMetadata = metadata,
            Entity = entity,
            PrimaryKeyValue = metadata.PrimaryKey.GetValue(entity),
            OriginalValidFrom = originalValidFrom,
            OriginalValidTo = originalValidTo ?? validity.OpenEndedValue,
            NewValidFrom = versionBumpDate,
            NewValidTo = originalValidTo ?? validity.OpenEndedValue,
            VersionBumpDate = versionBumpDate
        };
    }
    
    private VersionOperation CreateDeleteOperation(
        EntityEntry entry,
        EntityMetadata metadata,
        string correlationId)
    {
        var validity = metadata.Validity!;
        var entity = entry.Entity;
        
        var originalValidFrom = (DateTime)entry.OriginalValues[validity.ValidFromProperty.PropertyName]!;
        var closeDate = DateTime.UtcNow;
        
        _logger.LogDebug(
            "[{CorrelationId}] Creating DELETE (close) operation for {EntityType}, closing at {CloseDate}",
            correlationId,
            metadata.ClrType.Name,
            closeDate);
        
        return new VersionOperation
        {
            OperationType = VersionOperationType.Close,
            EntityMetadata = metadata,
            Entity = entity,
            PrimaryKeyValue = metadata.PrimaryKey.GetValue(entity),
            OriginalValidFrom = originalValidFrom,
            CloseDate = closeDate
        };
    }
}
```

---

## 4. Version Operation Model

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Represents a temporal versioning operation.
/// </summary>
public sealed class VersionOperation
{
    /// <summary>
    /// Gets the operation type.
    /// </summary>
    public VersionOperationType OperationType { get; init; }
    
    /// <summary>
    /// Gets the entity metadata.
    /// </summary>
    public EntityMetadata EntityMetadata { get; init; } = null!;
    
    /// <summary>
    /// Gets the entity instance.
    /// </summary>
    public object Entity { get; init; } = null!;
    
    /// <summary>
    /// Gets the primary key value.
    /// </summary>
    public object? PrimaryKeyValue { get; init; }
    
    /// <summary>
    /// Gets the original validity start (for Modify/Delete).
    /// </summary>
    public DateTime? OriginalValidFrom { get; init; }
    
    /// <summary>
    /// Gets the original validity end (for Modify/Delete).
    /// </summary>
    public DateTime? OriginalValidTo { get; init; }
    
    /// <summary>
    /// Gets the new validity start (for Create/Modify).
    /// </summary>
    public DateTime? NewValidFrom { get; init; }
    
    /// <summary>
    /// Gets the new validity end (for Create/Modify).
    /// </summary>
    public DateTime? NewValidTo { get; init; }
    
    /// <summary>
    /// Gets the version bump date (for Modify).
    /// </summary>
    public DateTime? VersionBumpDate { get; init; }
    
    /// <summary>
    /// Gets the close date (for Delete/soft delete).
    /// </summary>
    public DateTime? CloseDate { get; init; }
}

/// <summary>
/// Types of version operations.
/// </summary>
public enum VersionOperationType
{
    /// <summary>
    /// Create a new version (first version of entity).
    /// </summary>
    Create,
    
    /// <summary>
    /// Version bump (close current, create new).
    /// </summary>
    VersionBump,
    
    /// <summary>
    /// Close an existing version (soft delete).
    /// </summary>
    Close
}
```

---

## 5. Version Manager

### 5.1 Interface

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Manages temporal version operations.
/// </summary>
public interface IVersionManager
{
    /// <summary>
    /// Processes version operations and generates write commands.
    /// </summary>
    /// <param name="operations">The version operations to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processed operations with resolved version details.</returns>
    Task<IReadOnlyList<ProcessedVersionOperation>> ProcessOperationsAsync(
        IReadOnlyList<VersionOperation> operations,
        CancellationToken cancellationToken = default);
}
```

### 5.2 Implementation

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Processes temporal versioning logic.
/// </summary>
public sealed class VersionManager : IVersionManager
{
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly IShardRegistry _shardRegistry;
    private readonly ILogger<VersionManager> _logger;
    
    public VersionManager(
        IMetadataRegistry metadataRegistry,
        IShardRegistry shardRegistry,
        ILogger<VersionManager> logger)
    {
        _metadataRegistry = metadataRegistry;
        _shardRegistry = shardRegistry;
        _logger = logger;
    }
    
    public async Task<IReadOnlyList<ProcessedVersionOperation>> ProcessOperationsAsync(
        IReadOnlyList<VersionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        var processed = new List<ProcessedVersionOperation>();
        
        foreach (var operation in operations)
        {
            var result = operation.OperationType switch
            {
                VersionOperationType.Create => ProcessCreate(operation),
                VersionOperationType.VersionBump => await ProcessVersionBumpAsync(operation, cancellationToken),
                VersionOperationType.Close => ProcessClose(operation),
                _ => throw new InvalidOperationException($"Unknown operation type: {operation.OperationType}")
            };
            
            processed.Add(result);
        }
        
        return processed;
    }
    
    private ProcessedVersionOperation ProcessCreate(VersionOperation operation)
    {
        var metadata = operation.EntityMetadata;
        var validity = metadata.Validity!;
        
        // Set validity properties on the entity
        validity.ValidFromProperty.SetValue(operation.Entity, operation.NewValidFrom);
        
        if (validity.ValidToProperty is not null)
        {
            validity.ValidToProperty.SetValue(operation.Entity, operation.NewValidTo);
        }
        
        return new ProcessedVersionOperation
        {
            SourceOperation = operation,
            WriteCommands = new[]
            {
                new WriteCommand
                {
                    CommandType = WriteCommandType.Insert,
                    EntityMetadata = metadata,
                    Entity = operation.Entity,
                    PrimaryKeyValue = operation.PrimaryKeyValue
                }
            }
        };
    }
    
    private async Task<ProcessedVersionOperation> ProcessVersionBumpAsync(
        VersionOperation operation,
        CancellationToken cancellationToken)
    {
        var metadata = operation.EntityMetadata;
        var validity = metadata.Validity!;
        var bumpDate = operation.VersionBumpDate!.Value;
        
        // Create commands for both invalidation and new version
        var commands = new List<WriteCommand>();
        
        // Command 1: Invalidate old version
        // UPDATE SET {ValidToProperty} = @bumpDate WHERE PK = @pk AND {ValidFromProperty} = @originalValidFrom
        commands.Add(new WriteCommand
        {
            CommandType = WriteCommandType.Invalidate,
            EntityMetadata = metadata,
            PrimaryKeyValue = operation.PrimaryKeyValue,
            InvalidationDate = bumpDate,
            OriginalValidFrom = operation.OriginalValidFrom
        });
        
        // Command 2: Insert new version
        // Clone entity with new validity period
        var newEntity = CloneEntity(operation.Entity, metadata);
        validity.ValidFromProperty.SetValue(newEntity, bumpDate);
        
        if (validity.ValidToProperty is not null)
        {
            validity.ValidToProperty.SetValue(newEntity, operation.NewValidTo);
        }
        
        commands.Add(new WriteCommand
        {
            CommandType = WriteCommandType.Insert,
            EntityMetadata = metadata,
            Entity = newEntity,
            PrimaryKeyValue = operation.PrimaryKeyValue
        });
        
        return new ProcessedVersionOperation
        {
            SourceOperation = operation,
            WriteCommands = commands
        };
    }
    
    private ProcessedVersionOperation ProcessClose(VersionOperation operation)
    {
        var metadata = operation.EntityMetadata;
        var closeDate = operation.CloseDate!.Value;
        
        return new ProcessedVersionOperation
        {
            SourceOperation = operation,
            WriteCommands = new[]
            {
                new WriteCommand
                {
                    CommandType = WriteCommandType.Invalidate,
                    EntityMetadata = metadata,
                    PrimaryKeyValue = operation.PrimaryKeyValue,
                    InvalidationDate = closeDate,
                    OriginalValidFrom = operation.OriginalValidFrom
                }
            }
        };
    }
    
    private object CloneEntity(object source, EntityMetadata metadata)
    {
        var clone = Activator.CreateInstance(metadata.ClrType)!;
        
        // Copy all property values
        foreach (var property in metadata.ClrType.GetProperties()
            .Where(p => p.CanRead && p.CanWrite))
        {
            var value = property.GetValue(source);
            property.SetValue(clone, value);
        }
        
        return clone;
    }
}
```

### 5.3 Processed Operation and Write Command

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Result of version operation processing.
/// </summary>
public sealed class ProcessedVersionOperation
{
    /// <summary>
    /// Gets the source operation.
    /// </summary>
    public VersionOperation SourceOperation { get; init; } = null!;
    
    /// <summary>
    /// Gets the write commands to execute.
    /// </summary>
    public IReadOnlyList<WriteCommand> WriteCommands { get; init; } = Array.Empty<WriteCommand>();
}

/// <summary>
/// A database write command for a shard.
/// </summary>
public sealed class WriteCommand
{
    /// <summary>
    /// Gets the command type.
    /// </summary>
    public WriteCommandType CommandType { get; init; }
    
    /// <summary>
    /// Gets the entity metadata.
    /// </summary>
    public EntityMetadata EntityMetadata { get; init; } = null!;
    
    /// <summary>
    /// Gets the entity instance (for Insert).
    /// </summary>
    public object? Entity { get; init; }
    
    /// <summary>
    /// Gets the primary key value.
    /// </summary>
    public object? PrimaryKeyValue { get; init; }
    
    /// <summary>
    /// Gets the invalidation date (for Invalidate).
    /// </summary>
    public DateTime? InvalidationDate { get; init; }
    
    /// <summary>
    /// Gets the original validity start (for locating version to invalidate).
    /// </summary>
    public DateTime? OriginalValidFrom { get; init; }
    
    /// <summary>
    /// Gets the target shard (resolved by router).
    /// </summary>
    public ShardMetadata? TargetShard { get; set; }
}

/// <summary>
/// Types of write commands.
/// </summary>
public enum WriteCommandType
{
    /// <summary>
    /// Insert a new entity version.
    /// </summary>
    Insert,
    
    /// <summary>
    /// Invalidate (close) an existing version.
    /// </summary>
    Invalidate
}
```

---

## 6. Shard Write Router

### 6.1 Interface

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Routes write commands to appropriate shards.
/// </summary>
public interface IShardWriteRouter
{
    /// <summary>
    /// Routes write commands to target shards.
    /// </summary>
    /// <param name="operations">The processed operations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Write commands with resolved target shards.</returns>
    Task<IReadOnlyList<WriteCommand>> RouteWritesAsync(
        IReadOnlyList<ProcessedVersionOperation> operations,
        CancellationToken cancellationToken = default);
}
```

### 6.2 Implementation

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Routes write commands based on sharding strategy.
/// </summary>
public sealed class ShardWriteRouter : IShardWriteRouter
{
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly ILogger<ShardWriteRouter> _logger;
    
    public ShardWriteRouter(
        IMetadataRegistry metadataRegistry,
        ILogger<ShardWriteRouter> logger)
    {
        _metadataRegistry = metadataRegistry;
        _logger = logger;
    }
    
    public Task<IReadOnlyList<WriteCommand>> RouteWritesAsync(
        IReadOnlyList<ProcessedVersionOperation> operations,
        CancellationToken cancellationToken = default)
    {
        var routedCommands = new List<WriteCommand>();
        
        foreach (var operation in operations)
        {
            foreach (var command in operation.WriteCommands)
            {
                var targetShard = ResolveTargetShard(command, operation.SourceOperation);
                command.TargetShard = targetShard;
                routedCommands.Add(command);
                
                _logger.LogDebug(
                    "Routed {CommandType} command for {EntityType} to shard {ShardId}",
                    command.CommandType,
                    command.EntityMetadata.ClrType.Name,
                    targetShard.ShardId);
            }
        }
        
        return Task.FromResult<IReadOnlyList<WriteCommand>>(routedCommands);
    }
    
    private ShardMetadata ResolveTargetShard(WriteCommand command, VersionOperation sourceOperation)
    {
        var metadata = command.EntityMetadata;
        
        if (!metadata.IsSharded)
        {
            // Non-sharded entity - use default shard
            return _metadataRegistry.ShardRegistry.GetAllShards()
                .First(s => !s.IsReadOnly);
        }
        
        var strategy = metadata.Sharding!.Strategy;
        
        return command.CommandType switch
        {
            WriteCommandType.Insert => ResolveInsertShard(command, strategy, metadata),
            WriteCommandType.Invalidate => ResolveInvalidateShard(command, sourceOperation, strategy, metadata),
            _ => throw new InvalidOperationException($"Unknown command type: {command.CommandType}")
        };
    }
    
    private ShardMetadata ResolveInsertShard(
        WriteCommand command,
        IShardingStrategy strategy,
        EntityMetadata metadata)
    {
        return strategy.ResolveWriteShard(
            metadata,
            _metadataRegistry.ShardRegistry,
            command.Entity!);
    }
    
    private ShardMetadata ResolveInvalidateShard(
        WriteCommand command,
        VersionOperation sourceOperation,
        IShardingStrategy strategy,
        EntityMetadata metadata)
    {
        // For invalidation, we need to find the shard containing the original version
        // Use the original validity start date for date-based sharding
        
        if (strategy.StrategyType == ShardingStrategyType.DateRange 
            && sourceOperation.OriginalValidFrom.HasValue)
        {
            // Find shard containing the original validity start
            var predicates = new Dictionary<string, object?>
            {
                [metadata.Validity!.ValidFromProperty.PropertyName] = sourceOperation.OriginalValidFrom
            };
            
            var shards = strategy.ResolveShards(
                metadata,
                _metadataRegistry.ShardRegistry,
                predicates,
                sourceOperation.OriginalValidFrom);
            
            if (shards.Count == 1)
            {
                return shards[0];
            }
        }
        
        // Fallback: query all shards to find the version
        // This is less efficient but handles edge cases
        return _metadataRegistry.ShardRegistry.GetAllShards()
            .First(s => !s.IsReadOnly);
    }
}
```

---

## 7. Distributed Write Executor

### 7.1 Interface

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Executes write commands across distributed shards.
/// </summary>
public interface IDistributedWriteExecutor
{
    /// <summary>
    /// Executes write commands.
    /// </summary>
    /// <param name="commands">The commands to execute.</param>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of affected entities.</returns>
    Task<int> ExecuteAsync(
        IReadOnlyList<WriteCommand> commands,
        string correlationId,
        CancellationToken cancellationToken = default);
}
```

### 7.2 Implementation

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Executes distributed writes with best-effort consistency.
/// </summary>
public sealed class DistributedWriteExecutor : IDistributedWriteExecutor
{
    private readonly DtdeOptions _options;
    private readonly IDtdeDiagnostics _diagnostics;
    private readonly ILogger<DistributedWriteExecutor> _logger;
    
    public DistributedWriteExecutor(
        DtdeOptions options,
        IDtdeDiagnostics diagnostics,
        ILogger<DistributedWriteExecutor> logger)
    {
        _options = options;
        _diagnostics = diagnostics;
        _logger = logger;
    }
    
    public async Task<int> ExecuteAsync(
        IReadOnlyList<WriteCommand> commands,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Group commands by shard
        var commandsByShard = commands
            .GroupBy(c => c.TargetShard!.ShardId)
            .ToDictionary(g => g.Key, g => g.ToList());
        
        _logger.LogInformation(
            "[{CorrelationId}] Executing {CommandCount} commands across {ShardCount} shards",
            correlationId,
            commands.Count,
            commandsByShard.Count);
        
        var affectedCount = 0;
        var failedShards = new List<(string ShardId, Exception Exception)>();
        
        // Execute commands shard by shard (sequential for consistency)
        // Or parallel with compensation in advanced scenarios
        foreach (var (shardId, shardCommands) in commandsByShard)
        {
            try
            {
                var shard = shardCommands[0].TargetShard!;
                var affected = await ExecuteShardCommandsAsync(
                    shard,
                    shardCommands,
                    correlationId,
                    cancellationToken);
                
                affectedCount += affected;
                
                // Emit events for diagnostics
                EmitDiagnosticEvents(shardCommands, shardId, correlationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[{CorrelationId}] Failed to execute commands on shard {ShardId}",
                    correlationId,
                    shardId);
                
                failedShards.Add((shardId, ex));
            }
        }
        
        // Handle failures
        if (failedShards.Count > 0)
        {
            if (_options.FailOnPartialWrite)
            {
                // TODO: Implement compensation/rollback
                throw new ShardOperationException(
                    $"Write failed on {failedShards.Count} shards",
                    failedShards[0].ShardId,
                    failedShards[0].Exception);
            }
            
            _logger.LogWarning(
                "[{CorrelationId}] Partial write: {SuccessCount} succeeded, {FailCount} failed",
                correlationId,
                commandsByShard.Count - failedShards.Count,
                failedShards.Count);
        }
        
        return affectedCount;
    }
    
    private async Task<int> ExecuteShardCommandsAsync(
        ShardMetadata shard,
        IReadOnlyList<WriteCommand> commands,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(shard.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        
        try
        {
            var affected = 0;
            
            foreach (var command in commands)
            {
                affected += command.CommandType switch
                {
                    WriteCommandType.Insert => await ExecuteInsertAsync(connection, transaction, command, cancellationToken),
                    WriteCommandType.Invalidate => await ExecuteInvalidateAsync(connection, transaction, command, cancellationToken),
                    _ => 0
                };
            }
            
            await transaction.CommitAsync(cancellationToken);
            
            _logger.LogDebug(
                "[{CorrelationId}] Committed {Count} commands to shard {ShardId}",
                correlationId,
                commands.Count,
                shard.ShardId);
            
            return affected;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
    
    private async Task<int> ExecuteInsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        WriteCommand command,
        CancellationToken cancellationToken)
    {
        var metadata = command.EntityMetadata;
        var entity = command.Entity!;
        
        // Build INSERT statement dynamically based on entity metadata
        var columns = new List<string>();
        var parameters = new DynamicParameters();
        
        foreach (var property in metadata.ClrType.GetProperties()
            .Where(p => p.CanRead && p.GetCustomAttribute<NotMappedAttribute>() is null))
        {
            var columnName = property.Name; // Could use column mapping from metadata
            var value = property.GetValue(entity);
            
            columns.Add(columnName);
            parameters.Add($"@{columnName}", value);
        }
        
        var sql = $"""
            INSERT INTO [{metadata.SchemaName}].[{metadata.TableName}] 
            ({string.Join(", ", columns.Select(c => $"[{c}]"))})
            VALUES ({string.Join(", ", columns.Select(c => $"@{c}"))})
            """;
        
        return await connection.ExecuteAsync(
            sql,
            parameters,
            transaction);
    }
    
    private async Task<int> ExecuteInvalidateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        WriteCommand command,
        CancellationToken cancellationToken)
    {
        var metadata = command.EntityMetadata;
        var validity = metadata.Validity!;
        
        // Build UPDATE statement to close validity period
        var sql = $"""
            UPDATE [{metadata.SchemaName}].[{metadata.TableName}]
            SET [{validity.ValidToProperty!.PropertyName}] = @InvalidationDate
            WHERE [{metadata.PrimaryKey.PropertyName}] = @PrimaryKey
              AND [{validity.ValidFromProperty.PropertyName}] = @OriginalValidFrom
              AND [{validity.ValidToProperty.PropertyName}] > @InvalidationDate
            """;
        
        var parameters = new DynamicParameters();
        parameters.Add("@InvalidationDate", command.InvalidationDate);
        parameters.Add("@PrimaryKey", command.PrimaryKeyValue);
        parameters.Add("@OriginalValidFrom", command.OriginalValidFrom);
        
        return await connection.ExecuteAsync(
            sql,
            parameters,
            transaction);
    }
    
    private void EmitDiagnosticEvents(
        IReadOnlyList<WriteCommand> commands,
        string shardId,
        string correlationId)
    {
        foreach (var command in commands)
        {
            if (command.CommandType == WriteCommandType.Insert)
            {
                var validity = command.EntityMetadata.Validity!;
                var validFrom = (DateTime)validity.ValidFromProperty.GetValue(command.Entity!)!;
                var validTo = validity.ValidToProperty is not null
                    ? (DateTime?)validity.ValidToProperty.GetValue(command.Entity!)
                    : null;
                
                _diagnostics.EmitVersionCreated(new VersionCreatedEvent(
                    DateTime.UtcNow,
                    correlationId,
                    command.EntityMetadata.ClrType,
                    command.PrimaryKeyValue!,
                    validFrom,
                    validTo,
                    shardId));
            }
            else if (command.CommandType == WriteCommandType.Invalidate)
            {
                _diagnostics.EmitVersionInvalidated(new VersionInvalidatedEvent(
                    DateTime.UtcNow,
                    correlationId,
                    command.EntityMetadata.ClrType,
                    command.PrimaryKeyValue!,
                    DateTime.MaxValue, // Original was open-ended
                    command.InvalidationDate!.Value,
                    shardId));
            }
        }
    }
}
```

---

## 8. Consistency Strategies

### 8.1 Best-Effort Consistency (Default)

```csharp
/// <summary>
/// Best-effort consistency strategy.
/// Each shard is updated independently. Partial failures are logged.
/// </summary>
public sealed class BestEffortConsistencyStrategy : IConsistencyStrategy
{
    public async Task<WriteResult> ExecuteAsync(
        IReadOnlyList<ShardWriteGroup> groups,
        CancellationToken cancellationToken)
    {
        var results = new List<ShardWriteResult>();
        
        foreach (var group in groups)
        {
            try
            {
                await ExecuteShardWritesAsync(group, cancellationToken);
                results.Add(new ShardWriteResult(group.ShardId, success: true));
            }
            catch (Exception ex)
            {
                results.Add(new ShardWriteResult(group.ShardId, success: false, exception: ex));
            }
        }
        
        return new WriteResult(results);
    }
}
```

### 8.2 Outbox Pattern (Advanced)

```csharp
/// <summary>
/// Outbox-based consistency strategy.
/// Records operations in an outbox table for reliable delivery.
/// </summary>
public sealed class OutboxConsistencyStrategy : IConsistencyStrategy
{
    public async Task<WriteResult> ExecuteAsync(
        IReadOnlyList<ShardWriteGroup> groups,
        CancellationToken cancellationToken)
    {
        // 1. Write all operations to outbox in local transaction
        await WriteToOutboxAsync(groups, cancellationToken);
        
        // 2. Process outbox (can be async/background)
        var results = await ProcessOutboxAsync(groups, cancellationToken);
        
        return new WriteResult(results);
    }
}
```

---

## 9. Conflict Detection

```csharp
namespace Dtde.EntityFramework.Update;

/// <summary>
/// Detects and handles version conflicts.
/// </summary>
public interface IConflictDetector
{
    /// <summary>
    /// Checks if the operation would cause a conflict.
    /// </summary>
    /// <param name="operation">The operation to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Conflict information if detected.</returns>
    Task<VersionConflict?> DetectConflictAsync(
        VersionOperation operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a version conflict.
/// </summary>
public sealed class VersionConflict
{
    /// <summary>
    /// Gets the conflict type.
    /// </summary>
    public ConflictType Type { get; init; }
    
    /// <summary>
    /// Gets the existing version that conflicts.
    /// </summary>
    public object? ExistingEntity { get; init; }
    
    /// <summary>
    /// Gets a description of the conflict.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Types of version conflicts.
/// </summary>
public enum ConflictType
{
    /// <summary>
    /// Validity periods overlap.
    /// </summary>
    OverlappingValidity,
    
    /// <summary>
    /// Version was modified by another operation.
    /// </summary>
    ConcurrentModification,
    
    /// <summary>
    /// Version was already closed.
    /// </summary>
    AlreadyClosed
}
```

---

## 10. Test Specifications

Following the `MethodName_Condition_ExpectedResult` pattern:

### 10.1 Update Processor Tests

```csharp
// ProcessUpdatesAsync_AddedEntity_CreatesInsertCommand
// ProcessUpdatesAsync_ModifiedEntity_CreatesVersionBumpCommands
// ProcessUpdatesAsync_DeletedEntity_CreatesCloseCommand
// ProcessUpdatesAsync_NonTemporalEntity_SkipsProcessing
// ProcessUpdatesAsync_MultipleEntities_ProcessesAll
```

### 10.2 Version Manager Tests

```csharp
// ProcessOperationsAsync_Create_SetsValidityProperties
// ProcessOperationsAsync_VersionBump_GeneratesTwoCommands
// ProcessOperationsAsync_Close_GeneratesInvalidateCommand
// ProcessOperationsAsync_VersionBump_ClonesEntity
```

### 10.3 Shard Write Router Tests

```csharp
// RouteWritesAsync_InsertCommand_ResolvesFromStrategy
// RouteWritesAsync_InvalidateCommand_UsesOriginalValidFrom
// RouteWritesAsync_NonShardedEntity_UsesDefaultShard
// RouteWritesAsync_ReadOnlyShard_ExcludesFromInsert
```

### 10.4 Distributed Write Executor Tests

```csharp
// ExecuteAsync_SingleShard_ExecutesInTransaction
// ExecuteAsync_MultipleShards_ExecutesSequentially
// ExecuteAsync_ShardFailure_LogsAndContinues
// ExecuteAsync_AllFailed_ThrowsException
// ExecuteAsync_Invalidate_UpdatesCorrectRow
```

---

## Next Steps

Continue to [06 - Configuration & API](06-configuration-api.md) for developer-facing API and configuration details.
