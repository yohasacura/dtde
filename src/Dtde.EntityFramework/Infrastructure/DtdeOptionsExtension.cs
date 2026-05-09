using System.Globalization;

using Dtde.EntityFramework.Configuration;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.EntityFramework.Infrastructure;

/// <summary>
/// EF Core options extension that carries DTDE configuration plus the
/// active shard id (when this DbContext represents a single shard).
/// </summary>
/// <remarks>
/// One <see cref="DtdeOptionsExtension"/> instance lives on the parent
/// DbContext (with <see cref="ActiveShardId"/> = <see langword="null"/>) and
/// one is cloned per shard with that shard's id. The model customizer and
/// model cache key factory read <see cref="ActiveShardId"/> to build a
/// distinct EF model per shard with shard-specific table names.
/// </remarks>
public sealed class DtdeOptionsExtension : IDbContextOptionsExtension
{
    private DtdeOptions _options = new();
    private string? _activeShardId;
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
        _activeShardId = copyFrom._activeShardId;
    }

    /// <summary>
    /// Gets the DTDE options.
    /// </summary>
    public DtdeOptions Options => _options;

    /// <summary>
    /// Gets the active shard id for this DbContext, or <see langword="null"/>
    /// if this is the parent (unsharded) context. Per-shard contexts have a
    /// non-null value, and DTDE rewrites the EF model accordingly so that
    /// <c>Customers</c> becomes <c>Customers_EU</c>, etc.
    /// </summary>
    public string? ActiveShardId => _activeShardId;

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
    /// Returns a clone tagged with the given active shard id. Used by
    /// <see cref="Dtde.EntityFramework.Query.PerShardContextFactory{TContext}"/>
    /// when materialising a per-shard DbContext.
    /// </summary>
    public DtdeOptionsExtension WithActiveShardId(string? shardId)
    {
        return new DtdeOptionsExtension(this) { _activeShardId = shardId };
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
            Extension._activeShardId is null
                ? "using DTDE "
                : $"using DTDE shard '{Extension._activeShardId}' ";

        // ApplyServices doesn't depend on per-options data anymore, so all
        // DTDE-using DbContexts share a single internal EF service provider
        // (just keyed by ActiveShardId — different shards still need their
        // own model cache, but the underlying DI services are identical).
        public override int GetServiceProviderHashCode()
            => Extension._activeShardId?.GetHashCode(StringComparison.Ordinal) ?? 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo info
                && Extension._activeShardId == info.Extension._activeShardId;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["DTDE:Enabled"] = "true";
            debugInfo["DTDE:ShardCount"] = Extension._options.ShardRegistry.GetAllShards().Count
                .ToString(CultureInfo.InvariantCulture);
            debugInfo["DTDE:EntityCount"] = Extension._options.MetadataRegistry.GetAllEntityMetadata().Count
                .ToString(CultureInfo.InvariantCulture);
            if (Extension._activeShardId is not null)
            {
                debugInfo["DTDE:ActiveShardId"] = Extension._activeShardId;
            }
        }
    }
}
