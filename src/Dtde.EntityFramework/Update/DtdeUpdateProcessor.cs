using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;
using Dtde.Core.Transactions;
using Dtde.EntityFramework.Diagnostics;
using Dtde.EntityFramework.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Update;

/// <summary>
/// Default implementation of <see cref="IDtdeUpdateProcessor"/> that handles
/// temporal versioning and shard routing for entity updates.
/// Supports cross-shard transactions for operations spanning multiple shards.
/// </summary>
public sealed class DtdeUpdateProcessor : IDtdeUpdateProcessor
{
    private readonly VersionManager _versionManager;
    private readonly ShardWriteRouter _writeRouter;
    private readonly IShardContextFactory _contextFactory;
    private readonly ICrossShardTransactionCoordinator? _transactionCoordinator;
    private readonly ILogger<DtdeUpdateProcessor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DtdeUpdateProcessor"/> class.
    /// </summary>
    /// <param name="versionManager">The version manager.</param>
    /// <param name="writeRouter">The write router.</param>
    /// <param name="contextFactory">The context factory.</param>
    /// <param name="logger">The logger.</param>
    public DtdeUpdateProcessor(
        VersionManager versionManager,
        ShardWriteRouter writeRouter,
        IShardContextFactory contextFactory,
        ILogger<DtdeUpdateProcessor> logger)
        : this(versionManager, writeRouter, contextFactory, null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DtdeUpdateProcessor"/> class
    /// with cross-shard transaction support.
    /// </summary>
    /// <param name="versionManager">The version manager.</param>
    /// <param name="writeRouter">The write router.</param>
    /// <param name="contextFactory">The context factory.</param>
    /// <param name="transactionCoordinator">The cross-shard transaction coordinator.</param>
    /// <param name="logger">The logger.</param>
    public DtdeUpdateProcessor(
        VersionManager versionManager,
        ShardWriteRouter writeRouter,
        IShardContextFactory contextFactory,
        ICrossShardTransactionCoordinator? transactionCoordinator,
        ILogger<DtdeUpdateProcessor> logger)
    {
        _versionManager = versionManager ?? throw new ArgumentNullException(nameof(versionManager));
        _writeRouter = writeRouter ?? throw new ArgumentNullException(nameof(writeRouter));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _transactionCoordinator = transactionCoordinator;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TEntity> CreateNewVersionAsync<TEntity>(
        TEntity currentEntity,
        Action<TEntity> changes,
        DateTime effectiveDate,
        CancellationToken cancellationToken = default) where TEntity : class, new()
    {
        ArgumentNullException.ThrowIfNull(currentEntity);
        ArgumentNullException.ThrowIfNull(changes);

        LogMessages.CreatingNewVersion(_logger, typeof(TEntity).Name, effectiveDate);

        // 1. Get current validity period
        var (currentValidFrom, _) = _versionManager.GetValidityPeriod(currentEntity);

        if (effectiveDate <= currentValidFrom)
        {
            throw new InvalidOperationException(
                $"New version effective date {effectiveDate} must be after current version's ValidFrom {currentValidFrom}.");
        }

        // 2. Terminate the current version
        _versionManager.TerminateVersion(currentEntity, effectiveDate.AddTicks(-1));

        // 3. Create new version
        var newVersion = _versionManager.CreateVersion(currentEntity, effectiveDate);

        // 4. Apply changes to new version
        changes(newVersion);

        // 5. Determine target shard for new version
        var targetShard = _writeRouter.DetermineTargetShard(newVersion);

        if (!_writeRouter.CanWriteToShard(newVersion, targetShard))
        {
            throw new InvalidOperationException(
                $"Cannot write new version to shard '{targetShard.ShardId}'.");
        }

        // 6. Save both versions
        await SaveVersionsAsync(currentEntity, newVersion, targetShard, cancellationToken).ConfigureAwait(false);

        LogMessages.CreatedNewVersion(_logger, typeof(TEntity).Name, targetShard.ShardId);

        return newVersion;
    }

    /// <inheritdoc />
    public async Task<TEntity> CreateNewVersionAsync<TEntity>(
        TEntity currentEntity,
        Action<TEntity> changes,
        DateTime effectiveDate,
        ICrossShardTransaction transaction,
        CancellationToken cancellationToken = default) where TEntity : class, new()
    {
        ArgumentNullException.ThrowIfNull(currentEntity);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction is not CrossShardTransaction crossShardTransaction)
        {
            throw new ArgumentException(
                "Transaction must be a CrossShardTransaction instance.",
                nameof(transaction));
        }

        LogMessages.CreatingNewVersion(_logger, typeof(TEntity).Name, effectiveDate);

        // 1. Get current validity period
        var (currentValidFrom, _) = _versionManager.GetValidityPeriod(currentEntity);

        if (effectiveDate <= currentValidFrom)
        {
            throw new InvalidOperationException(
                $"New version effective date {effectiveDate} must be after current version's ValidFrom {currentValidFrom}.");
        }

        // 2. Terminate the current version
        _versionManager.TerminateVersion(currentEntity, effectiveDate.AddTicks(-1));

        // 3. Create new version
        var newVersion = _versionManager.CreateVersion(currentEntity, effectiveDate);

        // 4. Apply changes to new version
        changes(newVersion);

        // 5. Determine target shards
        var newVersionShard = _writeRouter.DetermineTargetShard(newVersion);
        var terminatedShard = _writeRouter.DetermineTargetShard(currentEntity);

        if (!_writeRouter.CanWriteToShard(newVersion, newVersionShard))
        {
            throw new InvalidOperationException(
                $"Cannot write new version to shard '{newVersionShard.ShardId}'.");
        }

        // 6. Enlist shards and prepare operations within the transaction
        var terminatedParticipant = await crossShardTransaction
            .GetOrCreateParticipantAsync(terminatedShard.ShardId, cancellationToken)
            .ConfigureAwait(false);
        terminatedParticipant.Context.Update(currentEntity);

        if (newVersionShard.ShardId != terminatedShard.ShardId)
        {
            var newVersionParticipant = await crossShardTransaction
                .GetOrCreateParticipantAsync(newVersionShard.ShardId, cancellationToken)
                .ConfigureAwait(false);
            newVersionParticipant.Context.Add(newVersion);

            LogMessages.SavingToDifferentShards(_logger, terminatedShard.ShardId, newVersionShard.ShardId);
        }
        else
        {
            terminatedParticipant.Context.Add(newVersion);
        }

        LogMessages.CreatedNewVersion(_logger, typeof(TEntity).Name, newVersionShard.ShardId);

        return newVersion;
    }

    /// <inheritdoc />
    public async Task TerminateVersionAsync<TEntity>(
        TEntity entity,
        DateTime terminationDate,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        LogMessages.TerminatingEntity(_logger, typeof(TEntity).Name, terminationDate);

        _versionManager.TerminateVersion(entity, terminationDate);

        var targetShard = _writeRouter.DetermineTargetShard(entity);
        await using var context = await _contextFactory.CreateContextAsync(targetShard.ShardId, cancellationToken).ConfigureAwait(false);

        context.Update(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogMessages.TerminatedEntity(_logger, typeof(TEntity).Name, targetShard.ShardId);
    }

    /// <inheritdoc />
    public async Task SaveNewEntityAsync<TEntity>(
        TEntity entity,
        DateTime effectiveFrom,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        LogMessages.SavingNewEntity(_logger, typeof(TEntity).Name, effectiveFrom);

        // Initialize temporal properties
        _versionManager.InitializeValidity(entity, effectiveFrom);

        // Determine target shard
        var targetShard = _writeRouter.DetermineTargetShard(entity);

        if (!_writeRouter.CanWriteToShard(entity, targetShard))
        {
            throw new InvalidOperationException(
                $"Cannot write entity to shard '{targetShard.ShardId}'.");
        }

        // Save to shard
        await using var context = await _contextFactory.CreateContextAsync(targetShard.ShardId, cancellationToken).ConfigureAwait(false);

        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogMessages.SavedNewEntity(_logger, typeof(TEntity).Name, targetShard.ShardId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TEntity>> CreateNewVersionsAsync<TEntity>(
        IEnumerable<(TEntity CurrentEntity, Action<TEntity> Changes, DateTime EffectiveDate)> updates,
        CancellationToken cancellationToken = default) where TEntity : class, new()
    {
        ArgumentNullException.ThrowIfNull(updates);

        var updateList = updates.ToList();
        if (updateList.Count == 0)
        {
            return [];
        }

        LogMessages.BatchOperationStarted(_logger, "CreateNewVersions", updateList.Count);

        // Prepare all version pairs and determine their shards
        var versionPairs = new List<(TEntity Terminated, TEntity NewVersion, IShardMetadata TerminatedShard, IShardMetadata NewVersionShard)>();

        foreach (var (currentEntity, changes, effectiveDate) in updateList)
        {
            var (currentValidFrom, _) = _versionManager.GetValidityPeriod(currentEntity);
            if (effectiveDate <= currentValidFrom)
            {
                throw new InvalidOperationException(
                    $"New version effective date {effectiveDate} must be after current version's ValidFrom {currentValidFrom}.");
            }

            _versionManager.TerminateVersion(currentEntity, effectiveDate.AddTicks(-1));
            var newVersion = _versionManager.CreateVersion(currentEntity, effectiveDate);
            changes(newVersion);

            var terminatedShard = _writeRouter.DetermineTargetShard(currentEntity);
            var newVersionShard = _writeRouter.DetermineTargetShard(newVersion);

            if (!_writeRouter.CanWriteToShard(newVersion, newVersionShard))
            {
                throw new InvalidOperationException(
                    $"Cannot write new version to shard '{newVersionShard.ShardId}'.");
            }

            versionPairs.Add((currentEntity, newVersion, terminatedShard, newVersionShard));
        }

        // Group by shard combinations to determine if cross-shard transaction is needed
        var allShardIds = versionPairs
            .SelectMany(p => new[] { p.TerminatedShard.ShardId, p.NewVersionShard.ShardId })
            .Distinct()
            .ToList();

        if (allShardIds.Count == 1)
        {
            // All operations on single shard - use regular transaction
            await using var context = await _contextFactory
                .CreateContextAsync(allShardIds[0], cancellationToken)
                .ConfigureAwait(false);

            foreach (var (terminated, newVersion, _, _) in versionPairs)
            {
                context.Update(terminated);
                context.Add(newVersion);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (_transactionCoordinator is not null)
        {
            // Multiple shards - use cross-shard transaction
            await _transactionCoordinator.ExecuteInTransactionAsync(
                async transaction =>
                {
                    var crossShardTx = (CrossShardTransaction)transaction;

                    foreach (var (terminated, newVersion, terminatedShard, newVersionShard) in versionPairs)
                    {
                        var terminatedParticipant = await crossShardTx
                            .GetOrCreateParticipantAsync(terminatedShard.ShardId, cancellationToken)
                            .ConfigureAwait(false);
                        terminatedParticipant.Context.Update(terminated);

                        var newVersionParticipant = await crossShardTx
                            .GetOrCreateParticipantAsync(newVersionShard.ShardId, cancellationToken)
                            .ConfigureAwait(false);
                        newVersionParticipant.Context.Add(newVersion);
                    }
                },
                CrossShardTransactionOptions.Default,
                cancellationToken).ConfigureAwait(false);

            LogMessages.BatchCrossShardCompleted(_logger, "CreateNewVersions", updateList.Count, allShardIds.Count);
        }
        else
        {
            // No coordinator - sequential saves with warning
            LogMessages.BatchWithoutCoordinator(_logger, allShardIds.Count);

            foreach (var (terminated, newVersion, terminatedShard, newVersionShard) in versionPairs)
            {
                await SaveVersionsWithoutCoordinatorAsync(terminated, newVersion, terminatedShard, newVersionShard, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        LogMessages.BatchOperationCompleted(_logger, "CreateNewVersions", updateList.Count);
        return versionPairs.Select(p => p.NewVersion).ToList();
    }

    /// <inheritdoc />
    public async Task TerminateVersionsAsync<TEntity>(
        IEnumerable<TEntity> entities,
        DateTime terminationDate,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entities);

        var entityList = entities.ToList();
        if (entityList.Count == 0)
        {
            return;
        }

        LogMessages.BatchOperationStarted(_logger, "TerminateVersions", entityList.Count);

        // Terminate all entities and group by shard
        var shardGroups = new Dictionary<string, List<TEntity>>();

        foreach (var entity in entityList)
        {
            _versionManager.TerminateVersion(entity, terminationDate);
            var targetShard = _writeRouter.DetermineTargetShard(entity);

            if (!shardGroups.TryGetValue(targetShard.ShardId, out var group))
            {
                group = [];
                shardGroups[targetShard.ShardId] = group;
            }

            group.Add(entity);
        }

        if (shardGroups.Count == 1)
        {
            // All entities on single shard
            var (shardId, entitiesToTerminate) = shardGroups.First();
            await using var context = await _contextFactory
                .CreateContextAsync(shardId, cancellationToken)
                .ConfigureAwait(false);

            foreach (var entity in entitiesToTerminate)
            {
                context.Update(entity);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (_transactionCoordinator is not null)
        {
            // Multiple shards - use cross-shard transaction
            await _transactionCoordinator.ExecuteInTransactionAsync(
                async transaction =>
                {
                    var crossShardTx = (CrossShardTransaction)transaction;

                    foreach (var (shardId, entitiesToTerminate) in shardGroups)
                    {
                        var participant = await crossShardTx
                            .GetOrCreateParticipantAsync(shardId, cancellationToken)
                            .ConfigureAwait(false);

                        foreach (var entity in entitiesToTerminate)
                        {
                            participant.Context.Update(entity);
                        }
                    }
                },
                CrossShardTransactionOptions.ShortLived,
                cancellationToken).ConfigureAwait(false);

            LogMessages.BatchCrossShardCompleted(_logger, "TerminateVersions", entityList.Count, shardGroups.Count);
        }
        else
        {
            // No coordinator - sequential saves
            LogMessages.BatchWithoutCoordinator(_logger, shardGroups.Count);

            foreach (var (shardId, entitiesToTerminate) in shardGroups)
            {
                await using var context = await _contextFactory
                    .CreateContextAsync(shardId, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var entity in entitiesToTerminate)
                {
                    context.Update(entity);
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        LogMessages.BatchOperationCompleted(_logger, "TerminateVersions", entityList.Count);
    }

    /// <inheritdoc />
    public async Task SaveNewEntitiesAsync<TEntity>(
        IEnumerable<(TEntity Entity, DateTime EffectiveFrom)> entities,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entities);

        var entityList = entities.ToList();
        if (entityList.Count == 0)
        {
            return;
        }

        LogMessages.BatchOperationStarted(_logger, "SaveNewEntities", entityList.Count);

        // Initialize temporal properties and group by shard
        var shardGroups = new Dictionary<string, List<TEntity>>();

        foreach (var (entity, effectiveFrom) in entityList)
        {
            _versionManager.InitializeValidity(entity, effectiveFrom);
            var targetShard = _writeRouter.DetermineTargetShard(entity);

            if (!_writeRouter.CanWriteToShard(entity, targetShard))
            {
                throw new InvalidOperationException(
                    $"Cannot write entity to shard '{targetShard.ShardId}'.");
            }

            if (!shardGroups.TryGetValue(targetShard.ShardId, out var group))
            {
                group = [];
                shardGroups[targetShard.ShardId] = group;
            }

            group.Add(entity);
        }

        if (shardGroups.Count == 1)
        {
            // All entities on single shard
            var (shardId, entitiesToSave) = shardGroups.First();
            await using var context = await _contextFactory
                .CreateContextAsync(shardId, cancellationToken)
                .ConfigureAwait(false);

            foreach (var entity in entitiesToSave)
            {
                context.Add(entity);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (_transactionCoordinator is not null)
        {
            // Multiple shards - use cross-shard transaction
            await _transactionCoordinator.ExecuteInTransactionAsync(
                async transaction =>
                {
                    var crossShardTx = (CrossShardTransaction)transaction;

                    foreach (var (shardId, entitiesToSave) in shardGroups)
                    {
                        var participant = await crossShardTx
                            .GetOrCreateParticipantAsync(shardId, cancellationToken)
                            .ConfigureAwait(false);

                        foreach (var entity in entitiesToSave)
                        {
                            participant.Context.Add(entity);
                        }
                    }
                },
                CrossShardTransactionOptions.ShortLived,
                cancellationToken).ConfigureAwait(false);

            LogMessages.BatchCrossShardCompleted(_logger, "SaveNewEntities", entityList.Count, shardGroups.Count);
        }
        else
        {
            // No coordinator - sequential saves
            LogMessages.BatchWithoutCoordinator(_logger, shardGroups.Count);

            foreach (var (shardId, entitiesToSave) in shardGroups)
            {
                await using var context = await _contextFactory
                    .CreateContextAsync(shardId, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var entity in entitiesToSave)
                {
                    context.Add(entity);
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        LogMessages.BatchOperationCompleted(_logger, "SaveNewEntities", entityList.Count);
    }

    /// <inheritdoc />
    public async Task<TEntity> TransferEntityAsync<TEntity>(
        TEntity entity,
        string targetShardId,
        DateTime effectiveDate,
        CancellationToken cancellationToken = default) where TEntity : class, new()
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(targetShardId);

        var sourceShardId = _writeRouter.DetermineTargetShard(entity).ShardId;

        if (sourceShardId == targetShardId)
        {
            // Same shard - just create a new version
            return await CreateNewVersionAsync(entity, _ => { }, effectiveDate, cancellationToken)
                .ConfigureAwait(false);
        }

        LogMessages.TransferringEntity(_logger, typeof(TEntity).Name, sourceShardId, targetShardId);

        // Validate target shard exists
        var targetShard = _writeRouter.ShardRegistry.GetShard(targetShardId)
            ?? throw new InvalidOperationException($"Target shard '{targetShardId}' not found.");

        // Terminate the current version
        var (currentValidFrom, _) = _versionManager.GetValidityPeriod(entity);
        if (effectiveDate <= currentValidFrom)
        {
            throw new InvalidOperationException(
                $"Transfer effective date {effectiveDate} must be after entity's ValidFrom {currentValidFrom}.");
        }

        _versionManager.TerminateVersion(entity, effectiveDate.AddTicks(-1));

        // Create new version (this will be placed on the target shard)
        var newVersion = _versionManager.CreateVersion(entity, effectiveDate);

        // Validate we can write to target shard
        if (!_writeRouter.CanWriteToShard(newVersion, targetShard))
        {
            throw new InvalidOperationException(
                $"Cannot write entity to target shard '{targetShardId}'.");
        }

        if (_transactionCoordinator is not null)
        {
            // Use cross-shard transaction for atomic transfer
            await _transactionCoordinator.ExecuteInTransactionAsync(
                async transaction =>
                {
                    var crossShardTx = (CrossShardTransaction)transaction;

                    // Update source shard (terminate)
                    var sourceParticipant = await crossShardTx
                        .GetOrCreateParticipantAsync(sourceShardId, cancellationToken)
                        .ConfigureAwait(false);
                    sourceParticipant.Context.Update(entity);

                    // Add to target shard (new version)
                    var targetParticipant = await crossShardTx
                        .GetOrCreateParticipantAsync(targetShardId, cancellationToken)
                        .ConfigureAwait(false);
                    targetParticipant.Context.Add(newVersion);
                },
                CrossShardTransactionOptions.ShortLived,
                cancellationToken).ConfigureAwait(false);

            LogMessages.EntityTransferred(_logger, typeof(TEntity).Name, sourceShardId, targetShardId);
        }
        else
        {
            // No coordinator - sequential saves with warning
            LogMessages.TransferWithoutCoordinator(_logger, sourceShardId, targetShardId);

            await using var sourceContext = await _contextFactory
                .CreateContextAsync(sourceShardId, cancellationToken)
                .ConfigureAwait(false);
            sourceContext.Update(entity);
            await sourceContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await using var targetContext = await _contextFactory
                .CreateContextAsync(targetShardId, cancellationToken)
                .ConfigureAwait(false);
            targetContext.Add(newVersion);
            await targetContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return newVersion;
    }

    private async Task SaveVersionsWithoutCoordinatorAsync<TEntity>(
        TEntity terminatedVersion,
        TEntity newVersion,
        IShardMetadata terminatedShard,
        IShardMetadata newVersionShard,
        CancellationToken cancellationToken) where TEntity : class
    {
        await using var terminatedContext = await _contextFactory
            .CreateContextAsync(terminatedShard.ShardId, cancellationToken)
            .ConfigureAwait(false);
        terminatedContext.Update(terminatedVersion);
        await terminatedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await using var newContext = await _contextFactory
            .CreateContextAsync(newVersionShard.ShardId, cancellationToken)
            .ConfigureAwait(false);
        newContext.Add(newVersion);
        await newContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveVersionsAsync<TEntity>(
        TEntity terminatedVersion,
        TEntity newVersion,
        IShardMetadata newVersionShard,
        CancellationToken cancellationToken) where TEntity : class
    {
        // Determine shard for terminated version (might be different from new version)
        var terminatedShard = _writeRouter.DetermineTargetShard(terminatedVersion);

        if (terminatedShard.ShardId == newVersionShard.ShardId)
        {
            // Both versions go to the same shard - single transaction
            await using var context = await _contextFactory.CreateContextAsync(
                newVersionShard.ShardId,
                cancellationToken).ConfigureAwait(false);

            context.Update(terminatedVersion);
            context.Add(newVersion);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (_transactionCoordinator is not null)
        {
            // Use cross-shard transaction coordinator for atomic operation
            await _transactionCoordinator.ExecuteInTransactionAsync(
                async transaction =>
                {
                    var crossShardTx = (CrossShardTransaction)transaction;

                    var terminatedParticipant = await crossShardTx
                        .GetOrCreateParticipantAsync(terminatedShard.ShardId, cancellationToken)
                        .ConfigureAwait(false);
                    terminatedParticipant.Context.Update(terminatedVersion);

                    var newVersionParticipant = await crossShardTx
                        .GetOrCreateParticipantAsync(newVersionShard.ShardId, cancellationToken)
                        .ConfigureAwait(false);
                    newVersionParticipant.Context.Add(newVersion);
                },
                CrossShardTransactionOptions.ShortLived,
                cancellationToken).ConfigureAwait(false);

            LogMessages.CrossShardTransactionCompleted(
                _logger,
                terminatedShard.ShardId,
                newVersionShard.ShardId);
        }
        else
        {
            // No transaction coordinator - fall back to sequential saves with warning
            LogMessages.SavingToDifferentShards(_logger, terminatedShard.ShardId, newVersionShard.ShardId);

            await using var terminatedContext = await _contextFactory.CreateContextAsync(
                terminatedShard.ShardId,
                cancellationToken).ConfigureAwait(false);
            terminatedContext.Update(terminatedVersion);
            await terminatedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await using var newContext = await _contextFactory.CreateContextAsync(
                newVersionShard.ShardId,
                cancellationToken).ConfigureAwait(false);
            newContext.Add(newVersion);
            await newContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
