using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Dtde.EntityFramework.Infrastructure;

/// <summary>
/// EF Core model customizer that runs after the user's <c>OnModelCreating</c>.
/// When the DbContext represents a single shard (i.e.
/// <see cref="DtdeOptionsExtension.ActiveShard"/> is non-null), it rewrites
/// the table name of every DTDE-sharded entity bound to that shard's group
/// to its per-shard form (default <c>{Table}_{ShardId}</c>; configurable via
/// <see cref="ShardingBuilder{TEntity}.WithTablePattern(string)"/>).
/// </summary>
/// <remarks>
/// Entities bound to a different group are excluded from the per-shard model
/// (via <c>modelBuilder.Ignore</c>) so that <c>EnsureCreatedAsync</c> /
/// <c>CreateTablesAsync</c> running against a per-shard DbContext only
/// provisions tables for entities that actually live in that shard. Entities
/// that aren't sharded at all are also excluded — they live on the parent
/// context.
/// </remarks>
internal sealed class DtdeShardModelCustomizer : ModelCustomizer
{
    /// <summary>
    /// Default per-shard table-name pattern. Tokens:
    /// <list type="bullet">
    ///   <item><description><c>{Table}</c> — the entity's base table name.</description></item>
    ///   <item><description><c>{Schema}</c> — the entity's schema, or <c>"dbo"</c> if unset.</description></item>
    ///   <item><description><c>{ShardId}</c> — the active shard's id.</description></item>
    /// </list>
    /// </summary>
    public const string DefaultPattern = "{Table}_{ShardId}";

    public DtdeShardModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        // 1) Run the user's OnModelCreating first.
        base.Customize(modelBuilder, context);

        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DtdeOptionsExtension>();

        if (extension is null)
        {
            return;
        }

        var activeShard = extension.ActiveShard;

        if (activeShard is null)
        {
            // Parent context: validate that every sharded entity's group
            // annotation references a registered group. Misconfiguration
            // surfaces here, at first DbContext use, rather than as an obscure
            // "Cannot create a DbSet" error inside a per-shard query later.
            ValidateEntityShardGroups(modelBuilder, extension.Options.ShardGroupRegistry);
            return;
        }

        var activeGroup = activeShard.GroupName;
        var shardId = activeShard.ShardId;
        var shardMode = activeShard.StorageMode;

        // Snapshot entity types up front: we may call Ignore which mutates the
        // model, and EF Core's GetEntityTypes() enumerator doesn't tolerate
        // concurrent modification.
        var entityTypes = modelBuilder.Model.GetEntityTypes().ToList();

        foreach (var entityType in entityTypes)
        {
            var isSharded = entityType.FindAnnotation(DtdeAnnotationNames.IsSharded)?.Value as bool? ?? false;
            if (!isSharded)
            {
                // Non-sharded entities live on the parent context only. Drop
                // them from the per-shard model so EnsureCreatedAsync /
                // CreateTablesAsync doesn't materialise duplicate tables on
                // each shard.
                modelBuilder.Ignore(entityType.ClrType);
                continue;
            }

            var entityGroup = entityType.FindAnnotation(DtdeAnnotationNames.ShardGroupName)?.Value as string
                ?? IShardGroupRegistry.DefaultGroupName;

            if (!string.Equals(entityGroup, activeGroup, StringComparison.Ordinal))
            {
                // This entity lives in a different group; not in this shard.
                modelBuilder.Ignore(entityType.ClrType);
                continue;
            }

            // Database-mode shards have their own database with the standard
            // table layout — the per-shard table-name rewrite only applies to
            // table-mode (and manual-mode) sharding inside a shared database.
            if (shardMode == ShardStorageMode.Databases)
            {
                continue;
            }

            var pattern = entityType.FindAnnotation(DtdeAnnotationNames.TableNamePattern)?.Value as string
                ?? DefaultPattern;

            var baseTable = entityType.GetTableName() ?? entityType.ClrType.Name;
            var schema = entityType.GetSchema() ?? "dbo";

            var perShardTable = pattern
                .Replace("{Table}", baseTable, StringComparison.Ordinal)
                .Replace("{Schema}", schema, StringComparison.Ordinal)
                .Replace("{ShardId}", shardId, StringComparison.Ordinal);

            entityType.SetTableName(perShardTable);
        }
    }

    private static void ValidateEntityShardGroups(
        ModelBuilder modelBuilder,
        IShardGroupRegistry groupRegistry)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var isSharded = entityType.FindAnnotation(DtdeAnnotationNames.IsSharded)?.Value as bool? ?? false;
            if (!isSharded)
            {
                continue;
            }

            var groupName = entityType.FindAnnotation(DtdeAnnotationNames.ShardGroupName)?.Value as string
                ?? IShardGroupRegistry.DefaultGroupName;

            if (groupRegistry.FindGroup(groupName) is null)
            {
                throw new InvalidOperationException(
                    $"Entity '{entityType.ClrType.Name}' is bound to shard group '{groupName}', " +
                    $"but no such group is registered. Add it with " +
                    $"dtde.AddShardGroup(\"{groupName}\", ...) in your AddDtdeDbContext call, " +
                    "or remove the UseShardGroup(...) call on the entity to fall back to the " +
                    "default group.");
            }
        }
    }
}
