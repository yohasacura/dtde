using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;
using Dtde.Core.Transactions;
using Dtde.EntityFramework.Diagnostics;
using Dtde.EntityFramework.Query;
using Dtde.EntityFramework.Update;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;
using System.Data.Common;

namespace Dtde.EntityFramework.Infrastructure;

/// <summary>
/// Interceptor that provides completely transparent sharding support for EF Core.
/// </summary>
/// <remarks>
/// <para>
/// This interceptor makes sharding completely invisible to application code.
/// Developers can use standard EF Core patterns and the interceptor automatically
/// handles cross-shard transactions when entities span multiple shards.
/// </para>
/// <para>
/// Supported patterns (all work transparently):
/// </para>
/// <list type="bullet">
/// <item><description>Regular SaveChanges - auto-promoted to cross-shard when needed</description></item>
/// <item><description>Explicit transactions - wrapped in cross-shard transaction automatically</description></item>
/// <item><description>Multiple SaveChanges in one transaction - all changes coordinated</description></item>
/// </list>
/// <example>
/// <code>
/// // All these patterns work transparently with sharding:
///
/// // Pattern 1: Simple SaveChanges
/// context.Add(entity1);  // Goes to shard-2024
/// context.Add(entity2);  // Goes to shard-2025
/// await context.SaveChangesAsync();  // Automatically uses cross-shard 2PC
///
/// // Pattern 2: Explicit transaction
/// await using var transaction = await context.Database.BeginTransactionAsync();
/// context.Add(entity1);  // Goes to shard-2024
/// await context.SaveChangesAsync();
/// context.Add(entity2);  // Goes to shard-2025
/// await context.SaveChangesAsync();
/// await transaction.CommitAsync();  // All changes committed atomically via 2PC
///
/// // Pattern 3: Standard EF Core repository pattern - just works!
/// </code>
/// </example>
/// </remarks>
public sealed class TransparentShardingInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransparentShardingInterceptor> _logger;

    // Track active transparent shard sessions per DbContext instance
    private readonly ConcurrentDictionary<int, TransparentShardSession> _activeSessions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TransparentShardingInterceptor"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    /// <param name="logger">The logger.</param>
    public TransparentShardingInterceptor(
        IServiceProvider serviceProvider,
        ILogger<TransparentShardingInterceptor> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region SaveChanges Interception

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
        var contextId = context.ContextId.InstanceId.GetHashCode();

        // Check if we have an active transparent session (explicit transaction)
        if (_activeSessions.TryGetValue(contextId, out var session))
        {
            // Route changes through our cross-shard session
            var savedCount = await session.SaveChangesAsync(context, cancellationToken)
                .ConfigureAwait(false);

            return InterceptionResult<int>.SuppressWithResult(savedCount);
        }

        // No explicit transaction - analyze and handle if cross-shard
        var shardAnalysis = AnalyzeChangesForSharding(context);

        if (!shardAnalysis.RequiresCrossShardTransaction)
        {
            // Single shard or no sharding - let normal SaveChanges proceed
            return result;
        }

        // Multiple shards - handle with automatic cross-shard transaction
        var savedCountCrossShard = await HandleCrossShardSaveAsync(context, shardAnalysis, cancellationToken)
            .ConfigureAwait(false);

        return InterceptionResult<int>.SuppressWithResult(savedCountCrossShard);
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
        var contextId = context.ContextId.InstanceId.GetHashCode();

        // Check if we have an active transparent session (explicit transaction)
        if (_activeSessions.TryGetValue(contextId, out var session))
        {
            // Route changes through our cross-shard session
            var savedCount = session.SaveChangesAsync(context, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return InterceptionResult<int>.SuppressWithResult(savedCount);
        }

        var shardAnalysis = AnalyzeChangesForSharding(context);

        if (!shardAnalysis.RequiresCrossShardTransaction)
        {
            return result;
        }

        // Multiple shards - handle with cross-shard transaction (sync wrapper)
        var savedCountCrossShard = HandleCrossShardSaveAsync(context, shardAnalysis, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return InterceptionResult<int>.SuppressWithResult(savedCountCrossShard);
    }

    #endregion

    #region Transaction Interception

    /// <inheritdoc />
    public async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is null)
        {
            return result;
        }

        var context = eventData.Context;
        var contextId = context.ContextId.InstanceId.GetHashCode();

        // Create a transparent shard session that will coordinate cross-shard operations
        var session = await CreateShardSessionAsync(context, eventData.IsolationLevel, cancellationToken)
            .ConfigureAwait(false);

        if (session is not null)
        {
            _activeSessions[contextId] = session;
            LogMessages.TransparentTransactionStarted(_logger);
        }

        // Let the original transaction start - we'll coordinate behind the scenes
        return result;
    }

    /// <inheritdoc />
    public InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is null)
        {
            return result;
        }

        var context = eventData.Context;
        var contextId = context.ContextId.InstanceId.GetHashCode();

        var session = CreateShardSessionAsync(context, eventData.IsolationLevel, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (session is not null)
        {
            _activeSessions[contextId] = session;
            LogMessages.TransparentTransactionStarted(_logger);
        }

        return result;
    }

    /// <inheritdoc />
    public DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result)
    {
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is null)
        {
            return result;
        }

        var contextId = eventData.Context.ContextId.InstanceId.GetHashCode();

        if (_activeSessions.TryGetValue(contextId, out var session))
        {
            // Commit via our cross-shard coordinator
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            LogMessages.TransparentTransactionCommitted(_logger, session.ShardCount);
        }

        // Let the original transaction commit as well (for non-sharded entities)
        return result;
    }

    /// <inheritdoc />
    public InterceptionResult TransactionCommitting(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is null)
        {
            return result;
        }

        var contextId = eventData.Context.ContextId.InstanceId.GetHashCode();

        if (_activeSessions.TryGetValue(contextId, out var session))
        {
            session.CommitAsync(CancellationToken.None).GetAwaiter().GetResult();
            LogMessages.TransparentTransactionCommitted(_logger, session.ShardCount);
        }

        return result;
    }

    /// <inheritdoc />
    public void TransactionCommitted(
        DbTransaction transaction,
        TransactionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        CleanupSession(eventData.Context);
    }

    /// <inheritdoc />
    public async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        await Task.CompletedTask;
        CleanupSession(eventData.Context);
    }

    /// <inheritdoc />
    public async ValueTask<InterceptionResult> TransactionRollingBackAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is null)
        {
            return result;
        }

        var contextId = eventData.Context.ContextId.InstanceId.GetHashCode();

        if (_activeSessions.TryGetValue(contextId, out var session))
        {
            await session.RollbackAsync(cancellationToken).ConfigureAwait(false);
            LogMessages.TransparentTransactionRolledBack(_logger, session.ShardCount);
        }

        return result;
    }

    /// <inheritdoc />
    public InterceptionResult TransactionRollingBack(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is null)
        {
            return result;
        }

        var contextId = eventData.Context.ContextId.InstanceId.GetHashCode();

        if (_activeSessions.TryGetValue(contextId, out var session))
        {
            session.RollbackAsync(CancellationToken.None).GetAwaiter().GetResult();
            LogMessages.TransparentTransactionRolledBack(_logger, session.ShardCount);
        }

        return result;
    }

    /// <inheritdoc />
    public void TransactionRolledBack(
        DbTransaction transaction,
        TransactionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        CleanupSession(eventData.Context);
    }

    /// <inheritdoc />
    public async Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        await Task.CompletedTask;
        CleanupSession(eventData.Context);
    }

    /// <inheritdoc />
    public void TransactionFailed(
        DbTransaction transaction,
        TransactionErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        CleanupSession(eventData.Context);
    }

    /// <inheritdoc />
    public async Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        await Task.CompletedTask;
        CleanupSession(eventData.Context);
    }

    /// <inheritdoc />
    public DbTransaction TransactionUsed(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result)
    {
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return result;
    }

    #endregion

    #region Private Helpers

    private void CleanupSession(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var contextId = context.ContextId.InstanceId.GetHashCode();
        if (_activeSessions.TryRemove(contextId, out var session))
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private async Task<TransparentShardSession?> CreateShardSessionAsync(
        DbContext context,
        System.Data.IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        // Try to get services from the context's scoped service provider first
        // This is necessary because many DTDE services are registered as scoped
        IServiceProvider serviceProvider;
        IServiceScope? scope = null;
        var disposeScope = true; // Track whether we should dispose scope on exit

        try
        {
            var contextServiceProvider = ((IInfrastructure<IServiceProvider>)context).Instance;

            // Test if we can resolve a scoped service from the context's provider
            var testService = contextServiceProvider.GetService<ICrossShardTransactionCoordinator>();
            if (testService != null)
            {
                serviceProvider = contextServiceProvider;
            }
            else
            {
                // Fallback to creating a new scope from the root provider
                scope = _serviceProvider.CreateScope();
                serviceProvider = scope.ServiceProvider;
            }
        }
#pragma warning disable CA1031 // Catch general exception to handle DI resolution failures gracefully
        catch
#pragma warning restore CA1031
        {
            // Fallback to creating a new scope from the root provider
            scope = _serviceProvider.CreateScope();
            serviceProvider = scope.ServiceProvider;
        }

        try
        {
            var coordinator = serviceProvider.GetService<ICrossShardTransactionCoordinator>();
            var contextFactory = serviceProvider.GetService<IShardContextFactory>();
            var metadataRegistry = serviceProvider.GetService<IMetadataRegistry>();
            var shardRegistry = serviceProvider.GetService<IShardRegistry>();
            var writeRouter = serviceProvider.GetService<ShardWriteRouter>();

            if (coordinator is null || contextFactory is null || metadataRegistry is null ||
                shardRegistry is null || writeRouter is null)
            {
                return null;
            }

            // Convert EF Core isolation level to our cross-shard isolation level
            var crossShardIsolationLevel = ConvertIsolationLevel(isolationLevel);

            var options = new CrossShardTransactionOptions
            {
                IsolationLevel = crossShardIsolationLevel
            };

            var transaction = await coordinator.BeginTransactionAsync(options, cancellationToken)
                .ConfigureAwait(false);

            // Success path: scope ownership transfers to the session
            // The session will manage the service lifetime
            disposeScope = false;
            return new TransparentShardSession(
                transaction,
                metadataRegistry,
                shardRegistry,
                writeRouter,
                _logger);
        }
        finally
        {
            // Dispose scope on any exit path except when successfully creating a session
            if (disposeScope)
            {
                scope?.Dispose();
            }
        }
    }

    private static CrossShardIsolationLevel ConvertIsolationLevel(System.Data.IsolationLevel isolationLevel)
    {
        return isolationLevel switch
        {
            System.Data.IsolationLevel.ReadUncommitted => CrossShardIsolationLevel.ReadCommitted, // Map to ReadCommitted
            System.Data.IsolationLevel.ReadCommitted => CrossShardIsolationLevel.ReadCommitted,
            System.Data.IsolationLevel.RepeatableRead => CrossShardIsolationLevel.RepeatableRead,
            System.Data.IsolationLevel.Serializable => CrossShardIsolationLevel.Serializable,
            System.Data.IsolationLevel.Snapshot => CrossShardIsolationLevel.Snapshot,
            _ => CrossShardIsolationLevel.ReadCommitted
        };
    }

    private ShardAnalysisResult AnalyzeChangesForSharding(DbContext context)
    {
        var metadataRegistry = _serviceProvider.GetService<IMetadataRegistry>();
        var shardRegistry = _serviceProvider.GetService<IShardRegistry>();

        if (metadataRegistry is null || shardRegistry is null)
        {
            return new ShardAnalysisResult { RequiresCrossShardTransaction = false };
        }

        // ShardWriteRouter is scoped, so we try to get it from the context's service provider
        // or create a scope to resolve it properly
        ShardWriteRouter? writeRouter = null;
        IServiceScope? scope = null;

        try
        {
            // Try to get from context's internal service provider first
            var contextServiceProvider = ((IInfrastructure<IServiceProvider>)context).Instance;
            writeRouter = contextServiceProvider.GetService<ShardWriteRouter>();

            if (writeRouter is null)
            {
                // Fall back to creating a scope from root provider
                scope = _serviceProvider.CreateScope();
                writeRouter = scope.ServiceProvider.GetService<ShardWriteRouter>();
            }

            if (writeRouter is null)
            {
                return new ShardAnalysisResult { RequiresCrossShardTransaction = false };
            }

            return AnalyzeChangesForShardingCore(context, metadataRegistry, shardRegistry, writeRouter);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private static ShardAnalysisResult AnalyzeChangesForShardingCore(
        DbContext context,
        IMetadataRegistry metadataRegistry,
        IShardRegistry shardRegistry,
        ShardWriteRouter writeRouter)
    {
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

        // If we have more than one shard group, we need cross-shard transaction
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

            // Fall back to sequential saves
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
                switch (entry.State)
                {
                    case EntityState.Added:
                        shardContext.Add(entry.Entity);
                        break;
                    case EntityState.Modified:
                        shardContext.Update(entry.Entity);
                        break;
                    case EntityState.Deleted:
                        shardContext.Remove(entry.Entity);
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
            switch (entry.State)
            {
                case EntityState.Added:
                    participant.Context.Add(entry.Entity);
                    break;
                case EntityState.Modified:
                    participant.Context.Update(entry.Entity);
                    break;
                case EntityState.Deleted:
                    participant.Context.Remove(entry.Entity);
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

    #endregion

    #region Nested Types

    private sealed class ShardAnalysisResult
    {
        public bool RequiresCrossShardTransaction { get; init; }
        public Dictionary<string, List<EntityEntry>> ShardGroups { get; init; } = [];
        public int TotalEntryCount { get; init; }
    }

    /// <summary>
    /// Manages a transparent cross-shard session during an explicit EF Core transaction.
    /// </summary>
    private sealed class TransparentShardSession : IAsyncDisposable
    {
        private readonly ICrossShardTransaction _transaction;
        private readonly IMetadataRegistry _metadataRegistry;
        private readonly IShardRegistry _shardRegistry;
        private readonly ShardWriteRouter _writeRouter;
        private readonly ILogger _logger;
        private readonly HashSet<string> _touchedShards = [];
        private bool _disposed;

        public int ShardCount => _touchedShards.Count;

        public TransparentShardSession(
            ICrossShardTransaction transaction,
            IMetadataRegistry metadataRegistry,
            IShardRegistry shardRegistry,
            ShardWriteRouter writeRouter,
            ILogger logger)
        {
            _transaction = transaction;
            _metadataRegistry = metadataRegistry;
            _shardRegistry = shardRegistry;
            _writeRouter = writeRouter;
            _logger = logger;
        }

        public async Task<int> SaveChangesAsync(DbContext sourceContext, CancellationToken cancellationToken)
        {
            var entries = sourceContext.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList();

            if (entries.Count == 0)
            {
                return 0;
            }

            var crossShardTx = (CrossShardTransaction)_transaction;
            var savedCount = 0;

            // Group entries by target shard
            var shardGroups = new Dictionary<string, List<EntityEntry>>();

            foreach (var entry in entries)
            {
                var entityType = entry.Entity.GetType();
                var metadata = _metadataRegistry.GetEntityMetadata(entityType);

                string targetShardId;
                if (metadata?.ShardingConfiguration is null)
                {
                    // Non-sharded entity - use default hot shard
                    targetShardId = _shardRegistry.GetAllShards()
                        .FirstOrDefault(s => s.Tier == ShardTier.Hot)?.ShardId ?? "_default_";
                }
                else
                {
                    var targetShard = DetermineTargetShardForEntry(entry, _writeRouter);
                    targetShardId = targetShard.ShardId;
                }

                if (!shardGroups.TryGetValue(targetShardId, out var list))
                {
                    list = [];
                    shardGroups[targetShardId] = list;
                }

                list.Add(entry);
                _touchedShards.Add(targetShardId);
            }

            // Save to each shard
            foreach (var (shardId, shardEntries) in shardGroups)
            {
                if (shardId == "_default_")
                {
                    continue;
                }

                var participant = await crossShardTx
                    .GetOrCreateParticipantAsync(shardId, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var entry in shardEntries)
                {
                    switch (entry.State)
                    {
                        case EntityState.Added:
                            participant.Context.Add(entry.Entity);
                            break;
                        case EntityState.Modified:
                            participant.Context.Update(entry.Entity);
                            break;
                        case EntityState.Deleted:
                            participant.Context.Remove(entry.Entity);
                            break;
                    }
                }

                savedCount += shardEntries.Count;
            }

            // Clear the source context's change tracker - we've processed these
            sourceContext.ChangeTracker.Clear();

            return savedCount;
        }

        private static IShardMetadata DetermineTargetShardForEntry(EntityEntry entry, ShardWriteRouter writeRouter)
        {
            var method = typeof(ShardWriteRouter).GetMethod(nameof(ShardWriteRouter.DetermineTargetShard))!
                .MakeGenericMethod(entry.Entity.GetType());

            return (IShardMetadata)method.Invoke(writeRouter, [entry.Entity])!;
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return;
            }

            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return;
            }

            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    #endregion
}
