using Dtde.Abstractions.Metadata;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Dtde.EntityFramework.Infrastructure;

/// <summary>
/// EF Core model-cache key factory that captures DTDE's per-shard model
/// variations so each shard gets its own cached
/// <see cref="Microsoft.EntityFrameworkCore.Metadata.IModel"/> instance with
/// the right table mappings.
/// </summary>
/// <remarks>
/// Two shards with the same id may produce different models if they have
/// different storage modes — table-mode rewrites <c>Customers</c> →
/// <c>Customers_EU</c> while database-mode keeps the original table name.
/// The cache key therefore includes both the active shard id and the shard's
/// storage mode.
/// </remarks>
internal sealed class DtdeModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DtdeOptionsExtension>();

        var shardId = extension?.ActiveShardId;
        var storageMode = shardId is null
            ? (ShardStorageMode?)null
            : extension!.Options.ShardRegistry.GetShard(shardId)?.StorageMode;

        return new DtdeModelCacheKey(context.GetType(), shardId, storageMode, designTime);
    }
}

/// <summary>
/// Composite cache key used by <see cref="DtdeModelCacheKeyFactory"/>.
/// </summary>
internal readonly record struct DtdeModelCacheKey(
    Type ContextType,
    string? ActiveShardId,
    ShardStorageMode? StorageMode,
    bool DesignTime);
