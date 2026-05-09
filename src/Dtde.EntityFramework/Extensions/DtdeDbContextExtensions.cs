using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;
using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Infrastructure;
using Dtde.EntityFramework.Query;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.EntityFramework.Extensions;

/// <summary>
/// Extension methods on <see cref="DtdeDbContext"/> for shard lifecycle
/// operations.
/// </summary>
public static class DtdeDbContextExtensions
{
    /// <summary>
    /// Calls <see cref="DatabaseFacade.EnsureCreatedAsync(CancellationToken)"/>
    /// on the parent context and then on a fresh per-shard context for every
    /// registered shard, walking each <see cref="IShardGroup"/> in turn. Use
    /// during application startup, sample bootstrap, or integration tests to
    /// make sure every shard's tables / databases exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In <strong>database-mode</strong>, each per-shard context targets its
    /// own database connection — so this creates the DB and its tables.
    /// </para>
    /// <para>
    /// In <strong>table-mode</strong>, all per-shard contexts share the parent
    /// connection. Each per-shard context's model has the in-group entity
    /// mapped to a shard-specific table name (e.g. <c>Customers_EU</c>), and
    /// out-of-group entities removed from the model — so
    /// <c>CreateTablesAsync</c> only creates tables that actually belong on
    /// that shard.
    /// </para>
    /// </remarks>
    /// <param name="context">The parent DTDE-aware DbContext.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task that completes when every shard has been provisioned.</returns>
    public static async Task EnsureAllShardsCreatedAsync(
        this DtdeDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Parent first: creates the base tables for non-sharded entities and,
        // in table-mode, the file/database that holds the shard tables.
        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        // Read shards via the group registry so each shard is provisioned in
        // the context of its group (per-shard model has only that group's
        // entities; out-of-group entities are excluded by the model
        // customizer).
        var extension = context.GetService<IDbContextOptions>().FindExtension<DtdeOptionsExtension>()
            ?? throw new InvalidOperationException(
                "DTDE is not configured on this DbContext. Did you call AddDtdeDbContext or UseDtde?");

        var groupRegistry = extension.Options.ShardGroupRegistry;

        var contextFactory = context.GetService<IShardContextFactory>()
            ?? throw new InvalidOperationException(
                "No IShardContextFactory is registered. Use AddDtdeDbContext to wire one automatically.");

        foreach (var group in groupRegistry.Groups)
        {
            foreach (var shard in group.Shards)
            {
                await using var shardContext = await contextFactory
                    .CreateContextAsync(shard, cancellationToken)
                    .ConfigureAwait(false);

                // Database-mode shards: each has its own underlying database;
                // EnsureCreated does the right thing.
                //
                // Table-mode shards (and Manual): the database already exists from
                // the parent context, so EnsureCreatedAsync would no-op. We force
                // table creation via the relational creator's CreateTablesAsync,
                // which honours the per-shard model (with the shard-specific
                // table names baked in by DtdeShardModelCustomizer).
                if (shard.StorageMode == ShardStorageMode.Databases)
                {
                    await shardContext.Database
                        .EnsureCreatedAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    if (shardContext.GetService<IDatabaseCreator>() is IRelationalDatabaseCreator relationalCreator)
                    {
                        await relationalCreator
                            .CreateTablesAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        // Non-relational (in-memory) provider: fall back to EnsureCreated.
                        await shardContext.Database
                            .EnsureCreatedAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Begins a cross-shard transaction with default options. The returned
    /// <see cref="ICrossShardTransaction"/> coordinates a two-phase-commit
    /// (2PC) across enlisted shards; with exactly one enlisted participant it
    /// degrades to a plain EF Core local transaction (single-shard fast
    /// path).
    /// </summary>
    /// <param name="context">The parent DTDE-aware DbContext.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A new cross-shard transaction. Always wrap it in
    /// <c>await using</c> — disposing without committing rolls back any
    /// enlisted participants.</returns>
    /// <example>
    /// <code>
    /// await using var tx = await db.BeginCrossShardTransactionAsync();
    /// var euCtx = (await tx.GetOrCreateParticipantAsync(euShard)).Context;
    /// var usCtx = (await tx.GetOrCreateParticipantAsync(usShard)).Context;
    ///
    /// euCtx.Set&lt;Customer&gt;().Add(new Customer { Region = "EU", ... });
    /// usCtx.Set&lt;Customer&gt;().Add(new Customer { Region = "US", ... });
    ///
    /// await tx.CommitAsync();
    /// </code>
    /// </example>
    public static Task<ICrossShardTransaction> BeginCrossShardTransactionAsync(
        this DtdeDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return BeginCrossShardTransactionAsync(context, CrossShardTransactionOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Begins a cross-shard transaction with the supplied options (isolation
    /// level, timeout, retry policy, etc.).
    /// </summary>
    /// <param name="context">The parent DTDE-aware DbContext.</param>
    /// <param name="options">The transaction options.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A new cross-shard transaction.</returns>
    public static Task<ICrossShardTransaction> BeginCrossShardTransactionAsync(
        this DtdeDbContext context,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var coordinator = context.GetService<ICrossShardTransactionCoordinator>()
            ?? throw new InvalidOperationException(
                "No ICrossShardTransactionCoordinator is registered. Cross-shard transactions " +
                "are wired automatically by AddDtdeDbContext when transparent sharding is enabled. " +
                "If you opted out (enableTransparentSharding: false), register the coordinator " +
                "manually.");

        return coordinator.BeginTransactionAsync(options, cancellationToken);
    }
}
