using System.Linq.Expressions;

using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;
using Dtde.Core.Transactions;
using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Infrastructure;
using Dtde.EntityFramework.Query;
using Dtde.EntityFramework.Update;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// Bulk-operation extensions on <see cref="DtdeDbContext"/> that respect
/// shard routing and shard groups. Each operation:
/// <list type="bullet">
///   <item><description>Resolves the target shard(s) via the entity's shard group.</description></item>
///   <item><description>Groups work by shard so each shard sees one round-trip.</description></item>
///   <item><description>Uses a two-phase commit when more than one shard is touched, and a plain EF Core local transaction (single-shard fast path) when only one shard is touched.</description></item>
/// </list>
/// </summary>
public static class BulkOperationsExtensions
{
    /// <summary>
    /// Inserts every entity in <paramref name="entities"/>, routing each to its
    /// target shard. Entities whose computed target shard is the same are
    /// batched together and inserted with a single
    /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> call per
    /// shard.
    /// </summary>
    /// <typeparam name="TEntity">The entity type. Must be sharded via
    /// <c>ShardBy*</c> in <c>OnModelCreating</c>.</typeparam>
    /// <param name="context">The parent DTDE-aware DbContext.</param>
    /// <param name="entities">The entities to insert.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The total number of state-entries written across all touched shards.</returns>
    /// <remarks>
    /// <para>
    /// Cross-shard inserts are wrapped in a cross-shard transaction (2PC).
    /// All shards either commit together or roll back together — partial
    /// success is impossible (modulo the well-known 2PC in-doubt window
    /// during the commit phase).
    /// </para>
    /// <para>
    /// This is provider-agnostic. For very large inserts you may want a
    /// provider-specific bulk path (SqlBulkCopy, PostgreSQL <c>COPY</c>,
    /// etc.) — those plug in via <see cref="IShardContextFactory"/> at the
    /// per-shard level and aren't wired by this method.
    /// </para>
    /// </remarks>
    public static Task<int> BulkInsertAsync<TEntity>(
        this DtdeDbContext context,
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        // Force the lazy backfill so ShardWriteRouter sees the entity's
        // sharding configuration. Without this, OnModelCreating annotations
        // wouldn't reach the MetadataRegistry that the router consults, and
        // every entity would be misrouted to the default hot shard.
        _ = context.MetadataRegistry;

        var router = context.GetService<ShardWriteRouter>()
            ?? throw new InvalidOperationException(
                "No ShardWriteRouter is registered. Bulk operations need it for shard routing — " +
                "ensure AddDtdeDbContext was used to register DTDE services.");

        return BulkInsertCoreAsync(context, entities, router, cancellationToken);
    }

    private static async Task<int> BulkInsertCoreAsync<TEntity>(
        DtdeDbContext context,
        IEnumerable<TEntity> entities,
        ShardWriteRouter router,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        // Group entities by their target shard's qualified id, so two shards
        // sharing a local id across groups don't collide.
        var byShardKey = new Dictionary<string, (IShardMetadata Shard, List<TEntity> Entities)>(
            StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            ArgumentNullException.ThrowIfNull(entity);

            var shard = router.DetermineTargetShard(entity);
            if (!router.CanWriteToShard(entity, shard))
            {
                throw new InvalidOperationException(
                    $"Cannot write entity to shard '{shard.ShardId}'.");
            }

            var key = shard.ToQualifiedId();
            if (!byShardKey.TryGetValue(key, out var bucket))
            {
                bucket = (shard, []);
                byShardKey[key] = bucket;
            }

            bucket.Entities.Add(entity);
        }

        if (byShardKey.Count == 0)
        {
            return 0;
        }

        // If a cross-shard transaction is already active in this scope, run
        // inside it: every shard becomes a participant, and the outer scope
        // is responsible for committing. This keeps BulkInsertAsync
        // transactional from the user's point of view.
        var coordinator = context.GetService<ICrossShardTransactionCoordinator>();
        if (coordinator?.CurrentTransaction is CrossShardTransaction ambient)
        {
            return await InsertViaAmbientTransactionAsync(context, byShardKey, ambient, cancellationToken)
                .ConfigureAwait(false);
        }

        // Single-shard fast path: just open one per-shard context, pick a
        // bulk provider, and insert.
        if (byShardKey.Count == 1)
        {
            var (_, (shard, entityList)) = byShardKey.First();
            return await InsertOnSingleShardAsync(context, shard, entityList, cancellationToken)
                .ConfigureAwait(false);
        }

        // Cross-shard: run inside a cross-shard transaction so all-or-nothing
        // semantics hold across shard boundaries.
        if (coordinator is null)
        {
            throw new InvalidOperationException(
                "Cross-shard bulk insert requires ICrossShardTransactionCoordinator. " +
                "It is registered automatically by AddDtdeDbContext unless you opted out " +
                "(enableTransparentSharding: false).");
        }

        var savedCount = 0;
        await coordinator.ExecuteInTransactionAsync(
            async transaction =>
            {
                var crossShardTx = (CrossShardTransaction)transaction;
                savedCount = await InsertViaAmbientTransactionAsync(context, byShardKey, crossShardTx, cancellationToken)
                    .ConfigureAwait(false);
            },
            CrossShardTransactionOptions.Default,
            cancellationToken).ConfigureAwait(false);

        return savedCount;
    }

    private static async Task<int> InsertViaAmbientTransactionAsync<TEntity>(
        DtdeDbContext context,
        Dictionary<string, (IShardMetadata Shard, List<TEntity> Entities)> byShardKey,
        CrossShardTransaction transaction,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var providers = ResolveProviders(context);
        var savedCount = 0;

        foreach (var (_, (shard, entityList)) in byShardKey)
        {
            var participant = await transaction
                .GetOrCreateParticipantAsync(shard, cancellationToken)
                .ConfigureAwait(false);

            // Pick the right bulk path for this shard's provider.
            savedCount += await RunBulkInsertAsync(
                providers,
                participant.Context,
                entityList,
                cancellationToken).ConfigureAwait(false);
        }

        return savedCount;
    }

    private static async Task<int> InsertOnSingleShardAsync<TEntity>(
        DtdeDbContext context,
        IShardMetadata shard,
        IReadOnlyList<TEntity> entities,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var contextFactory = context.GetService<IShardContextFactory>()
            ?? throw new InvalidOperationException(
                "No IShardContextFactory is registered.");

        var providers = ResolveProviders(context);

        await using var shardContext = await contextFactory
            .CreateContextAsync(shard, cancellationToken)
            .ConfigureAwait(false);

        return await RunBulkInsertAsync(providers, shardContext, entities, cancellationToken)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<IBulkInsertProvider> ResolveProviders(DtdeDbContext context)
    {
        // Resolve via the single-service wrapper — EF Core's
        // context.GetService<T> resolves single types reliably, but
        // IEnumerable<T> resolution doesn't consistently fall through to
        // the application service provider.
        var chain = context.GetService<BulkInsertProviderChain>()
            ?? throw new InvalidOperationException(
                "No BulkInsertProviderChain is registered. Ensure AddDtdeDbContext " +
                "registered DTDE services.");
        return chain.Providers;
    }

    private static async Task<int> RunBulkInsertAsync<TEntity>(
        IReadOnlyList<IBulkInsertProvider> providers,
        DbContext context,
        IReadOnlyCollection<TEntity> entities,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (entities.Count == 0)
        {
            return 0;
        }

        foreach (var provider in providers)
        {
            if (provider.CanHandle(context))
            {
                return await provider
                    .BulkInsertAsync(context, entities, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // Defensive fallback — should never hit because the default
        // provider always claims.
        var set = context.Set<TEntity>();
        await set.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entities.Count;
    }

#if NET10_0_OR_GREATER
    /// <summary>
    /// Runs <c>ExecuteUpdate</c> on every shard in the entity's shard
    /// group, returning the sum of rows updated. Set-based; no
    /// <c>SELECT</c>, no change-tracker overhead. The
    /// <paramref name="setters"/> action receives EF Core 10's
    /// <c>UpdateSettersBuilder&lt;TEntity&gt;</c>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="context">The parent DTDE-aware DbContext.</param>
    /// <param name="filter">Filter expression scoping the update.</param>
    /// <param name="setters">Action that configures the property setters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The total number of rows updated.</returns>
    /// <remarks>
    /// Each shard's update commits independently. For atomic cross-shard
    /// semantics, wrap the call in a
    /// <see cref="DtdeDbContextExtensions.BeginCrossShardTransactionAsync(DtdeDbContext, CancellationToken)">cross-shard transaction</see>;
    /// when an ambient transaction is active, the per-shard executes flow
    /// through the ambient transaction's participants.
    /// </remarks>
    public static Task<int> BulkUpdateAsync<TEntity>(
        this DtdeDbContext context,
        Expression<Func<TEntity, bool>> filter,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<TEntity>> setters,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(setters);

        return FanOutBulkAsync<TEntity>(
            context,
            (set, ct) => set.Where(filter).ExecuteUpdateAsync(setters, ct),
            cancellationToken);
    }
#else
    /// <summary>
    /// Runs <c>ExecuteUpdate</c> on every shard in the entity's shard
    /// group, returning the sum of rows updated. Set-based; no
    /// <c>SELECT</c>, no change-tracker overhead. The
    /// <paramref name="setPropertyCalls"/> expression receives EF Core
    /// 7/8/9's <c>SetPropertyCalls&lt;TEntity&gt;</c>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="context">The parent DTDE-aware DbContext.</param>
    /// <param name="filter">Filter expression scoping the update.</param>
    /// <param name="setPropertyCalls">Property-update fluent expression
    /// (<c>p =&gt; p.SetProperty(x =&gt; x.Foo, value)...</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The total number of rows updated.</returns>
    /// <remarks>
    /// Each shard's update commits independently. For atomic cross-shard
    /// semantics, wrap the call in a
    /// <see cref="DtdeDbContextExtensions.BeginCrossShardTransactionAsync(DtdeDbContext, CancellationToken)">cross-shard transaction</see>;
    /// when an ambient transaction is active, the per-shard executes flow
    /// through the ambient transaction's participants.
    /// </remarks>
    public static Task<int> BulkUpdateAsync<TEntity>(
        this DtdeDbContext context,
        Expression<Func<TEntity, bool>> filter,
        Expression<Func<Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<TEntity>, Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<TEntity>>> setPropertyCalls,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(setPropertyCalls);

        return FanOutBulkAsync<TEntity>(
            context,
            (set, ct) => set.Where(filter).ExecuteUpdateAsync(setPropertyCalls, ct),
            cancellationToken);
    }
#endif

    /// <summary>
    /// Runs an <c>ExecuteDelete</c> on each shard in the entity's shard group
    /// and returns the sum of rows deleted. Set-based — the underlying
    /// provider issues a single <c>DELETE WHERE</c> per shard, no
    /// <c>SELECT</c> roundtrip, no change-tracker overhead.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="context">The parent DTDE-aware DbContext.</param>
    /// <param name="filter">Filter expression that scopes the delete.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The total number of rows deleted across all shards in the
    /// entity's group.</returns>
    /// <remarks>
    /// <para>
    /// The fan-out is scoped to the entity's
    /// <see cref="IShardingConfiguration.ShardGroupName">shard group</see>;
    /// non-sharded entities run only against the default group's hot shard.
    /// </para>
    /// <para>
    /// This method does not wrap the per-shard deletes in a cross-shard
    /// transaction — each shard's delete commits independently. For atomic
    /// cross-shard semantics, wrap the call in
    /// <see cref="DtdeDbContextExtensions.BeginCrossShardTransactionAsync(DtdeDbContext, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// Bulk update (<c>ExecuteUpdate</c>) is intentionally not surfaced as
    /// an extension method here because EF Core 7/8/9 use
    /// <c>SetPropertyCalls&lt;T&gt;</c> while EF Core 10 uses
    /// <c>UpdateSettersBuilder&lt;T&gt;</c>. To run a cross-shard update,
    /// open a <see cref="ICrossShardTransaction"/> and call
    /// <c>ExecuteUpdateAsync</c> on each per-shard <c>DbContext</c>'s set
    /// directly — that path is provider-version-stable.
    /// </para>
    /// </remarks>
    public static Task<int> BulkDeleteAsync<TEntity>(
        this DtdeDbContext context,
        Expression<Func<TEntity, bool>> filter,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(filter);

        return FanOutBulkAsync<TEntity>(
            context,
            (set, ct) => set.Where(filter).ExecuteDeleteAsync(ct),
            cancellationToken);
    }

    private static async Task<int> FanOutBulkAsync<TEntity>(
        DtdeDbContext context,
        Func<DbSet<TEntity>, CancellationToken, Task<int>> operation,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        // Force the lazy backfill so the entity's sharding annotations from
        // OnModelCreating reach the MetadataRegistry consulted below.
        _ = context.MetadataRegistry;

        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DtdeOptionsExtension>()
            ?? throw new InvalidOperationException(
                "DTDE is not configured on this DbContext. Did you call AddDtdeDbContext or UseDtde?");

        var groupRegistry = extension.Options.ShardGroupRegistry;
        var metadataRegistry = extension.Options.MetadataRegistry;

        // Resolve the entity's shard group. Falls through to the default
        // group when no explicit binding is registered.
        var entityMetadata = metadataRegistry.GetEntityMetadata<TEntity>();
        var groupName = entityMetadata?.ShardingConfiguration?.ShardGroupName
            ?? IShardGroupRegistry.DefaultGroupName;
        var group = groupRegistry.FindGroup(groupName)
            ?? throw new InvalidOperationException(
                $"Entity '{typeof(TEntity).Name}' is bound to shard group '{groupName}', " +
                "but no such group is registered.");

        var shards = group.Shards;
        if (shards.Count == 0)
        {
            return 0;
        }

        var contextFactory = context.GetService<IShardContextFactory>()
            ?? throw new InvalidOperationException(
                "No IShardContextFactory is registered.");

        var coordinator = context.GetService<ICrossShardTransactionCoordinator>();
        var ambient = coordinator?.CurrentTransaction as CrossShardTransaction;

        // Run sequentially: parallel ExecuteUpdate/Delete across shards is
        // possible but each opens its own connection, and the per-provider
        // contention story varies. Sequential is correct + boring; parallel
        // is a follow-up optimisation.
        var total = 0;
        foreach (var shard in shards)
        {
            DbContext shardContext;
            bool ownsContext;

            if (ambient is not null)
            {
                // Inside an ambient cross-shard transaction: route through
                // the participant so the bulk operation is part of the
                // transaction.
                var participant = await ambient.GetOrCreateParticipantAsync(shard, cancellationToken)
                    .ConfigureAwait(false);
                shardContext = participant.Context;
                ownsContext = false;
            }
            else
            {
                shardContext = await contextFactory.CreateContextAsync(shard, cancellationToken).ConfigureAwait(false);
                ownsContext = true;
            }

            try
            {
                var set = shardContext.Set<TEntity>();
                total += await operation(set, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (ownsContext)
                {
                    await shardContext.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        return total;
    }
}
