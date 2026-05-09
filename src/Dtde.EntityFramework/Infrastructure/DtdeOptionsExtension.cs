using System.Globalization;

using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Configuration;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.EntityFramework.Infrastructure;

/// <summary>
/// EF Core options extension that carries DTDE configuration plus the
/// active shard (when this DbContext represents a single shard).
/// </summary>
/// <remarks>
/// One <see cref="DtdeOptionsExtension"/> instance lives on the parent
/// DbContext (with <see cref="ActiveShard"/> = <see langword="null"/>) and
/// one is cloned per shard with that shard's metadata. The model customizer
/// and model cache key factory read <see cref="ActiveShard"/> to build a
/// distinct EF model per shard with shard-specific table names — scoped to
/// the active shard's <see cref="IShardMetadata.GroupName"/> so entities
/// bound to other groups are excluded from the per-shard model.
/// </remarks>
public sealed class DtdeOptionsExtension : IDbContextOptionsExtension
{
    private DtdeOptions _options = new();
    private IShardMetadata? _activeShard;
    private ExtensionInfo? _info;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public DtdeOptionsExtension()
    {
    }

    private DtdeOptionsExtension(DtdeOptionsExtension copyFrom)
    {
        _options = copyFrom._options;
        _activeShard = copyFrom._activeShard;
    }

    /// <summary>
    /// Gets the DTDE options.
    /// </summary>
    public DtdeOptions Options => _options;

    /// <summary>
    /// Gets the active shard for this DbContext, or <see langword="null"/>
    /// if this is the parent (unsharded) context. Per-shard contexts have a
    /// non-null value, and DTDE rewrites the EF model accordingly so that
    /// <c>Customers</c> becomes <c>Customers_EU</c>, etc. — and so that
    /// entities bound to other groups are excluded from the model.
    /// </summary>
    public IShardMetadata? ActiveShard => _activeShard;

    /// <summary>
    /// Convenience: the active shard's id (within its group), or
    /// <see langword="null"/> for the parent context.
    /// </summary>
    public string? ActiveShardId => _activeShard?.ShardId;

    /// <summary>
    /// Convenience: the active shard's group name, or <see langword="null"/>
    /// for the parent context.
    /// </summary>
    public string? ActiveShardGroup => _activeShard?.GroupName;

    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>
    /// Returns a clone with the supplied options.
    /// </summary>
    public DtdeOptionsExtension WithOptions(DtdeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new DtdeOptionsExtension(this) { _options = options };
    }

    /// <summary>
    /// Returns a clone tagged with the given active shard. Used by
    /// <see cref="Dtde.EntityFramework.Query.PerShardContextFactory{TContext}"/>
    /// when materialising a per-shard DbContext.
    /// </summary>
    public DtdeOptionsExtension WithActiveShard(IShardMetadata? shard)
    {
        return new DtdeOptionsExtension(this) { _activeShard = shard };
    }

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services)
    {
        // Only register *type-based* services here. Per-options data
        // (registries, the options object itself) flows through the extension
        // at runtime via FindExtension<DtdeOptionsExtension>(). Keeping
        // ApplyServices stable across configurations lets EF Core reuse its
        // internal service-provider cache and avoids the
        // ManyServiceProvidersCreatedWarning when many DbContexts are
        // constructed (test suites, tenant-per-request, etc.).
        services.AddSingleton<IModelCacheKeyFactory, DtdeModelCacheKeyFactory>();
        services.AddSingleton<IModelCustomizer, DtdeShardModelCustomizer>();
    }

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        _options.MetadataRegistry.Validate();
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(DtdeOptionsExtension extension) : base(extension)
        {
        }

        private new DtdeOptionsExtension Extension => (DtdeOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment =>
            Extension._activeShard is null
                ? "using DTDE "
                : $"using DTDE shard '{Extension._activeShard.GroupName}/{Extension._activeShard.ShardId}' ";

        // ApplyServices doesn't depend on per-options data anymore, so all
        // DTDE-using DbContexts share a single internal EF service provider
        // (just keyed by group + shard id — different shards still need their
        // own model cache, but the underlying DI services are identical).
        public override int GetServiceProviderHashCode()
        {
            if (Extension._activeShard is null)
            {
                return 0;
            }

            return HashCode.Combine(
                Extension._activeShard.GroupName,
                Extension._activeShard.ShardId);
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            if (other is not ExtensionInfo info)
            {
                return false;
            }

            var a = Extension._activeShard;
            var b = info.Extension._activeShard;

            if (a is null && b is null)
            {
                return true;
            }

            if (a is null || b is null)
            {
                return false;
            }

            return string.Equals(a.GroupName, b.GroupName, StringComparison.Ordinal)
                && string.Equals(a.ShardId, b.ShardId, StringComparison.Ordinal);
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["DTDE:Enabled"] = "true";
            debugInfo["DTDE:ShardCount"] = Extension._options.ShardRegistry.GetAllShards().Count
                .ToString(CultureInfo.InvariantCulture);
            debugInfo["DTDE:GroupCount"] = Extension._options.ShardGroupRegistry.Groups.Count
                .ToString(CultureInfo.InvariantCulture);
            debugInfo["DTDE:EntityCount"] = Extension._options.MetadataRegistry.GetAllEntityMetadata().Count
                .ToString(CultureInfo.InvariantCulture);
            if (Extension._activeShard is not null)
            {
                debugInfo["DTDE:ActiveShardGroup"] = Extension._activeShard.GroupName;
                debugInfo["DTDE:ActiveShardId"] = Extension._activeShard.ShardId;
            }
        }
    }
}
