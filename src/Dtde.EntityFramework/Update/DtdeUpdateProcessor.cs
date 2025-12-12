using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Diagnostics;
using Dtde.EntityFramework.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Update;

/// <summary>
/// Default implementation of <see cref="IDtdeUpdateProcessor"/> that handles
/// temporal versioning and shard routing for entity updates.
/// </summary>
public sealed class DtdeUpdateProcessor : IDtdeUpdateProcessor
{
    private readonly VersionManager _versionManager;
    private readonly ShardWriteRouter _writeRouter;
    private readonly IShardContextFactory _contextFactory;
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
    {
        _versionManager = versionManager ?? throw new ArgumentNullException(nameof(versionManager));
        _writeRouter = writeRouter ?? throw new ArgumentNullException(nameof(writeRouter));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
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
        else
        {
            // Different shards - need distributed transaction or two-phase commit
            // For simplicity, we save sequentially here
            // In production, consider using a distributed transaction coordinator

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
