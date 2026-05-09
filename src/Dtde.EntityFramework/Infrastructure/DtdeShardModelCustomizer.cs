using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Dtde.EntityFramework.Infrastructure;

/// <summary>
/// EF Core model customizer that runs after the user's <c>OnModelCreating</c>.
/// When the DbContext represents a single shard (i.e.
/// <see cref="DtdeOptionsExtension.ActiveShardId"/> is non-null), it rewrites
/// the table name of every DTDE-sharded entity to its per-shard form
/// (default <c>{Table}_{ShardId}</c>; configurable via
/// <see cref="ShardingBuilder{TEntity}.WithTablePattern(string)"/>).
/// </summary>
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

        // 2) If this DbContext represents a single shard, rewrite tables.
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DtdeOptionsExtension>();
        var shardId = extension?.ActiveShardId;

        if (shardId is null)
        {
            return;
        }

        // Database-mode shards have their own database with the standard
        // table layout — the per-shard table-name rewrite only applies to
        // table-mode (and manual-mode) sharding inside a shared database.
        var shardMode = extension!.Options.ShardRegistry.GetShard(shardId)?.StorageMode
            ?? ShardStorageMode.Tables;
        if (shardMode == ShardStorageMode.Databases)
        {
            return;
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var isSharded = entityType.FindAnnotation(DtdeAnnotationNames.IsSharded)?.Value as bool? ?? false;
            if (!isSharded)
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
}
