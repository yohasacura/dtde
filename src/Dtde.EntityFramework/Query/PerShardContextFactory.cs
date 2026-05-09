using System.Diagnostics.CodeAnalysis;

using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Configuration;
using Dtde.EntityFramework.Diagnostics;
using Dtde.EntityFramework.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Query;

/// <summary>
/// Creates a fresh <see cref="DbContext"/> instance per shard, with the
/// active shard id baked into <see cref="DtdeOptionsExtension.ActiveShardId"/>.
/// EF Core's per-shard model cache (driven by
/// <see cref="DtdeModelCacheKeyFactory"/>) and
/// <see cref="DtdeShardModelCustomizer"/> together ensure that:
/// <list type="bullet">
///   <item><description>For database-mode shards, the per-shard context uses
///   the shard's own connection string (replacing whatever the user wired in
///   <c>configureProvider</c>).</description></item>
///   <item><description>For table-mode shards, the per-shard context maps the
///   sharded entity to its shard-specific table (e.g. <c>Customers_EU</c>) so
///   queries and writes hit the right rows.</description></item>
/// </list>
/// </summary>
/// <typeparam name="TContext">The application's <see cref="DtdeDbContext"/> subclass.</typeparam>
internal sealed class PerShardContextFactory<TContext> : IShardContextFactory
    where TContext : DbContext
{
    private readonly IShardRegistry _shardRegistry;
    private readonly Action<DbContextOptionsBuilder, string?> _configureProvider;
    private readonly DtdeOptions _dtdeOptions;
    private readonly ILogger<PerShardContextFactory<TContext>> _logger;
    private readonly Func<DbContextOptions<TContext>, TContext> _activator;
    private readonly string? _defaultConnectionString;

    public PerShardContextFactory(
        IShardRegistry shardRegistry,
        Action<DbContextOptionsBuilder, string?> configureProvider,
        DtdeOptions dtdeOptions,
        ILogger<PerShardContextFactory<TContext>> logger,
        string? defaultConnectionString = null)
    {
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
        _configureProvider = configureProvider ?? throw new ArgumentNullException(nameof(configureProvider));
        _dtdeOptions = dtdeOptions ?? throw new ArgumentNullException(nameof(dtdeOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultConnectionString = defaultConnectionString;

        _activator = BuildActivator();
    }

    /// <inheritdoc />
    public Task<DbContext> CreateContextAsync(string shardId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        var shard = _shardRegistry.GetShard(shardId)
            ?? throw new InvalidOperationException($"Shard '{shardId}' is not registered.");

        return CreateContextAsync(shard, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DbContext> CreateContextAsync(IShardMetadata shard, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(shard);

        LogMessages.CreatingContextForShard(_logger, shard.ShardId, shard.StorageMode);

        var connectionString = ResolveConnectionString(shard);

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        _configureProvider(optionsBuilder, connectionString);

        // Layer the shard-tagged DTDE extension on top so the model customizer
        // and cache key factory rewrite tables for this shard.
        var existing = optionsBuilder.Options.FindExtension<DtdeOptionsExtension>()
            ?? new DtdeOptionsExtension().WithOptions(_dtdeOptions);
        var taggedExtension = existing
            .WithOptions(_dtdeOptions)
            .WithActiveShardId(shard.ShardId);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(taggedExtension);

        var context = _activator(optionsBuilder.Options);
        return Task.FromResult<DbContext>(context);
    }

    /// <inheritdoc />
    public string GetConnectionString(string shardId)
    {
        var shard = _shardRegistry.GetShard(shardId)
            ?? throw new InvalidOperationException($"Shard '{shardId}' is not registered.");
        return ResolveConnectionString(shard) ?? string.Empty;
    }

    /// <inheritdoc />
    public string? GetTableName(string shardId)
        => _shardRegistry.GetShard(shardId)?.TableName;

    /// <inheritdoc />
    public string? GetSchemaName(string shardId)
        => _shardRegistry.GetShard(shardId)?.SchemaName;

    private string? ResolveConnectionString(IShardMetadata shard)
    {
        // Database-mode shards bring their own connection string; table-mode
        // shards inherit the framework's default (the parent DbContext's).
        return shard.StorageMode switch
        {
            ShardStorageMode.Databases => shard.ConnectionString
                ?? throw new InvalidOperationException(
                    $"Shard '{shard.ShardId}' is database-mode but has no connection string. " +
                    "Use AddShard(id, connectionString) or the full fluent overload."),

            ShardStorageMode.Tables or ShardStorageMode.Manual =>
                shard.ConnectionString ?? _defaultConnectionString,

            _ => throw new InvalidOperationException(
                $"Unsupported shard storage mode '{shard.StorageMode}' for shard '{shard.ShardId}'.")
        };
    }

    [SuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Activator.CreateInstance<TContext> is the documented EF Core extensibility point for context-with-options construction.")]
    private static Func<DbContextOptions<TContext>, TContext> BuildActivator()
    {
        var ctor = typeof(TContext).GetConstructor([typeof(DbContextOptions<TContext>)])
            ?? throw new InvalidOperationException(
                $"Type '{typeof(TContext).Name}' must declare a public constructor that takes a single " +
                "DbContextOptions<TContext> parameter for DTDE's per-shard factory to materialise it.");

        return options => (TContext)ctor.Invoke([options]);
    }
}
