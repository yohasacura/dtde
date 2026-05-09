using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Infrastructure;
using Dtde.EntityFramework.Query;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

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
    /// registered shard. Use during application startup, sample bootstrap, or
    /// integration tests to make sure every shard's tables / databases exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In <strong>database-mode</strong>, each per-shard context targets its
    /// own database connection — so this creates the DB and its tables.
    /// </para>
    /// <para>
    /// In <strong>table-mode</strong>, all per-shard contexts share the parent
    /// connection. Each per-shard context's model has the entity mapped to a
    /// shard-specific table name (e.g. <c>Customers_EU</c>), so
    /// <c>EnsureCreatedAsync</c> creates those tables alongside the parent's
    /// non-sharded tables.
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

        // Read shard list from the extension (single source of truth) rather
        // than the EF-Core internal service provider.
        var extension = context.GetService<IDbContextOptions>().FindExtension<DtdeOptionsExtension>()
            ?? throw new InvalidOperationException(
                "DTDE is not configured on this DbContext. Did you call AddDtdeDbContext or UseDtde?");
        var shardRegistry = extension.Options.ShardRegistry;

        var contextFactory = context.GetService<IShardContextFactory>()
            ?? throw new InvalidOperationException(
                "No IShardContextFactory is registered. Use AddDtdeDbContext to wire one automatically.");

        foreach (var shard in shardRegistry.GetAllShards())
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
