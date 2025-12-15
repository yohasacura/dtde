using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;
using Dtde.Core.Transactions;
using Dtde.EntityFramework.Diagnostics;
using Dtde.EntityFramework.Query;
using Dtde.EntityFramework.Update;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Infrastructure;

/// <summary>
/// Interceptor that automatically promotes regular SaveChanges operations to cross-shard
/// transactions when entities span multiple shards.
/// </summary>
/// <remarks>
/// <para>
/// This interceptor analyzes the change tracker before SaveChanges is called and determines
/// if the operation spans multiple shards. If so, it automatically routes the changes to
/// the appropriate shards using a cross-shard transaction for atomicity.
/// </para>
/// <para>
/// Enable this interceptor to allow developers to use regular EF Core patterns while
/// automatically getting cross-shard transaction support:
/// </para>
/// <example>
/// <code>
/// // Regular EF Core code - cross-shard is handled automatically!
/// context.Add(entity1);  // Goes to shard-2024
/// context.Add(entity2);  // Goes to shard-2025
/// context.Update(entity3);  // Goes to shard-2023
/// await context.SaveChangesAsync();  // Automatically uses cross-shard transaction
/// </code>
/// </example>
/// </remarks>
public sealed class ShardAwareSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ShardAwareSaveChangesInterceptor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardAwareSaveChangesInterceptor"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="logger">The logger.</param>
    public ShardAwareSaveChangesInterceptor(
        IServiceProvider serviceProvider,
        ILogger<ShardAwareSaveChangesInterceptor> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is null)
        {
            return result;
        }

        var context = eventData.Context;

        // If there's an existing transaction, don't interfere - the user is managing transactions manually
        if (context.Database.CurrentTransaction is not null)
        {
            LogMessages.ExplicitTransactionDetected(_logger);

            // Still analyze to warn if this is a cross-shard operation
            var analysisForWarning = AnalyzeChangesForSharding(context);
            if (analysisForWarning.RequiresCrossShardTransaction)
            {
                LogMessages.CrossShardInExplicitTransaction(_logger);
            }

            return result;
        }

        var shardAnalysis = AnalyzeChangesForSharding(context);

        if (!shardAnalysis.RequiresCrossShardTransaction)
        {
            // Single shard or no sharding - let normal SaveChanges proceed
            return result;
        }

        // Multiple shards detected - handle with cross-shard transaction
        var savedCount = await HandleCrossShardSaveAsync(context, shardAnalysis, cancellationToken)
            .ConfigureAwait(false);

        // Return the result and suppress the original SaveChanges
        return InterceptionResult<int>.SuppressWithResult(savedCount);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is null)
        {
            return result;
        }

        var context = eventData.Context;

        // If there's an existing transaction, don't interfere - the user is managing transactions manually
        if (context.Database.CurrentTransaction is not null)
        {
            LogMessages.ExplicitTransactionDetected(_logger);

            // Still analyze to warn if this is a cross-shard operation
            var analysisForWarning = AnalyzeChangesForSharding(context);
            if (analysisForWarning.RequiresCrossShardTransaction)
            {
                LogMessages.CrossShardInExplicitTransaction(_logger);
            }

            return result;
        }

        var shardAnalysis = AnalyzeChangesForSharding(context);

        if (!shardAnalysis.RequiresCrossShardTransaction)
        {
            return result;
        }

        // Multiple shards detected - handle with cross-shard transaction (sync version)
        var savedCount = HandleCrossShardSaveAsync(context, shardAnalysis, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return InterceptionResult<int>.SuppressWithResult(savedCount);
    }

    private ShardAnalysisResult AnalyzeChangesForSharding(DbContext context)
    {
        var metadataRegistry = _serviceProvider.GetService<IMetadataRegistry>();
        var shardRegistry = _serviceProvider.GetService<IShardRegistry>();

        if (metadataRegistry is null || shardRegistry is null)
        {
            return new ShardAnalysisResult { RequiresCrossShardTransaction = false };
        }

        var writeRouter = _serviceProvider.GetService<ShardWriteRouter>();
        if (writeRouter is null)
        {
            return new ShardAnalysisResult { RequiresCrossShardTransaction = false };
        }

        var entriesByState = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entriesByState.Count == 0)
        {
            return new ShardAnalysisResult { RequiresCrossShardTransaction = false };
        }

        var shardGroups = new Dictionary<string, List<EntityEntry>>();
        var hasShardedEntities = false;

        foreach (var entry in entriesByState)
        {
            var entityType = entry.Entity.GetType();
            var metadata = metadataRegistry.GetEntityMetadata(entityType);

            if (metadata?.ShardingConfiguration is null)
            {
                // Non-sharded entity - group under "default"
                AddToShardGroup(shardGroups, "_default_", entry);
                continue;
            }

            hasShardedEntities = true;

            // Determine the target shard for this entity
            var targetShard = DetermineTargetShardForEntry(entry, writeRouter);
            AddToShardGroup(shardGroups, targetShard.ShardId, entry);
        }

        // If we have more than one shard group (excluding default) or mixed sharded/non-sharded,
        // we need cross-shard transaction
        var shardedGroupCount = shardGroups.Keys.Count(k => k != "_default_");
        var hasDefaultGroup = shardGroups.ContainsKey("_default_");

        var requiresCrossShard = hasShardedEntities &&
            (shardedGroupCount > 1 || (shardedGroupCount >= 1 && hasDefaultGroup));

        return new ShardAnalysisResult
        {
            RequiresCrossShardTransaction = requiresCrossShard,
            ShardGroups = shardGroups,
            TotalEntryCount = entriesByState.Count
        };
    }

    private static IShardMetadata DetermineTargetShardForEntry(EntityEntry entry, ShardWriteRouter writeRouter)
    {
        // Use reflection to call the generic method
        var method = typeof(ShardWriteRouter).GetMethod(nameof(ShardWriteRouter.DetermineTargetShard))!
            .MakeGenericMethod(entry.Entity.GetType());

        return (IShardMetadata)method.Invoke(writeRouter, [entry.Entity])!;
    }

    private static void AddToShardGroup(
        Dictionary<string, List<EntityEntry>> groups,
        string shardId,
        EntityEntry entry)
    {
        if (!groups.TryGetValue(shardId, out var list))
        {
            list = [];
            groups[shardId] = list;
        }

        list.Add(entry);
    }

    private async Task<int> HandleCrossShardSaveAsync(
        DbContext sourceContext,
        ShardAnalysisResult analysis,
        CancellationToken cancellationToken)
    {
        var coordinator = _serviceProvider.GetService<ICrossShardTransactionCoordinator>();
        var contextFactory = _serviceProvider.GetService<IShardContextFactory>();

        if (coordinator is null || contextFactory is null)
        {
            LogMessages.CrossShardWithoutCoordinator(_logger, analysis.ShardGroups.Count);

            // Fall back to sequential saves if no coordinator
            return await SaveSequentiallyAsync(sourceContext, analysis, contextFactory, cancellationToken)
                .ConfigureAwait(false);
        }

        LogMessages.AutoPromotingToCrossShard(_logger, analysis.TotalEntryCount, analysis.ShardGroups.Count);

        var savedCount = 0;

        await coordinator.ExecuteInTransactionAsync(
            async transaction =>
            {
                var crossShardTx = (CrossShardTransaction)transaction;

                foreach (var (shardId, entries) in analysis.ShardGroups)
                {
                    if (shardId == "_default_")
                    {
                        // Non-sharded entities go to the source context's default shard
                        // or we need to determine a default location
                        var defaultShardId = GetDefaultShardId();
                        if (defaultShardId is not null)
                        {
                            await SaveEntriesToShardAsync(crossShardTx, defaultShardId, entries, cancellationToken)
                                .ConfigureAwait(false);
                            savedCount += entries.Count;
                        }
                    }
                    else
                    {
                        await SaveEntriesToShardAsync(crossShardTx, shardId, entries, cancellationToken)
                            .ConfigureAwait(false);
                        savedCount += entries.Count;
                    }
                }
            },
            CrossShardTransactionOptions.Default,
            cancellationToken).ConfigureAwait(false);

        // Clear the change tracker since we've handled all changes
        sourceContext.ChangeTracker.Clear();

        LogMessages.AutoCrossShardCompleted(_logger, savedCount, analysis.ShardGroups.Count);

        return savedCount;
    }

    private async Task<int> SaveSequentiallyAsync(
        DbContext sourceContext,
        ShardAnalysisResult analysis,
        IShardContextFactory? contextFactory,
        CancellationToken cancellationToken)
    {
        if (contextFactory is null)
        {
            // No context factory - can't route to shards, let original save proceed
            return 0;
        }

        var savedCount = 0;

        foreach (var (shardId, entries) in analysis.ShardGroups)
        {
            var targetShardId = shardId == "_default_" ? GetDefaultShardId() : shardId;
            if (targetShardId is null)
            {
                continue;
            }

            await using var shardContext = await contextFactory
                .CreateContextAsync(targetShardId, cancellationToken)
                .ConfigureAwait(false);

            foreach (var entry in entries)
            {
                var entity = entry.Entity;
                switch (entry.State)
                {
                    case EntityState.Added:
                        shardContext.Add(entity);
                        break;
                    case EntityState.Modified:
                        shardContext.Update(entity);
                        break;
                    case EntityState.Deleted:
                        shardContext.Remove(entity);
                        break;
                }
            }

            savedCount += await shardContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        sourceContext.ChangeTracker.Clear();
        return savedCount;
    }

    private static async Task SaveEntriesToShardAsync(
        CrossShardTransaction transaction,
        string shardId,
        List<EntityEntry> entries,
        CancellationToken cancellationToken)
    {
        var participant = await transaction.GetOrCreateParticipantAsync(shardId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var entry in entries)
        {
            var entity = entry.Entity;
            switch (entry.State)
            {
                case EntityState.Added:
                    participant.Context.Add(entity);
                    break;
                case EntityState.Modified:
                    participant.Context.Update(entity);
                    break;
                case EntityState.Deleted:
                    participant.Context.Remove(entity);
                    break;
            }
        }
    }

    private string? GetDefaultShardId()
    {
        var shardRegistry = _serviceProvider.GetService<IShardRegistry>();
        return shardRegistry?.GetAllShards()
            .FirstOrDefault(s => s.Tier == ShardTier.Hot)?.ShardId;
    }

    private sealed class ShardAnalysisResult
    {
        public bool RequiresCrossShardTransaction { get; init; }
        public Dictionary<string, List<EntityEntry>> ShardGroups { get; init; } = [];
        public int TotalEntryCount { get; init; }
    }
}
