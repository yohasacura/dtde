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
/// The cache key includes the active shard's group, local id, and storage
/// mode. Two shards with the same local id in different groups (for example,
/// shard <c>"0"</c> in group <c>hash8</c> versus shard <c>"0"</c> in group
/// <c>hash3</c>) produce different per-shard models — they map different
/// entities to their per-shard tables — and so they must cache to distinct
/// keys.
/// </remarks>
internal sealed class DtdeModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DtdeOptionsExtension>();

        var shard = extension?.ActiveShard;

        return new DtdeModelCacheKey(
            context.GetType(),
            shard?.GroupName,
            shard?.ShardId,
            shard?.StorageMode,
            designTime);
    }
}

/// <summary>
/// Composite cache key used by <see cref="DtdeModelCacheKeyFactory"/>.
/// </summary>
internal readonly record struct DtdeModelCacheKey(
    Type ContextType,
    string? ActiveShardGroup,
    string? ActiveShardId,
    ShardStorageMode? StorageMode,
    bool DesignTime);
