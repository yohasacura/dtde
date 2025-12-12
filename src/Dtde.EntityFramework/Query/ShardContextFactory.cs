using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Query;

/// <summary>
/// Default implementation of <see cref="IShardContextFactory"/> that creates DbContext instances
/// for specific shards using their configured connection strings or table names.
/// </summary>
/// <typeparam name="TContext">The DbContext type to create.</typeparam>
/// <example>
/// <code>
/// // Database-level sharding (separate databases):
/// var factory = new ShardContextFactory&lt;AppDbContext&gt;(
///     shardRegistry,
///     logger,
///     options => new AppDbContext(options),
///     (builder, connStr) => builder.UseSqlServer(connStr));
///
/// // Table-level sharding (same database, different tables):
/// // Uses the shared connection string with per-shard table names
/// </code>
/// </example>
public sealed class ShardContextFactory<TContext> : IShardContextFactory where TContext : DbContext
{
    private readonly IShardRegistry _shardRegistry;
    private readonly ILogger<ShardContextFactory<TContext>> _logger;
    private readonly Func<DbContextOptions<TContext>, TContext> _contextFactory;
    private readonly Action<DbContextOptionsBuilder<TContext>, string>? _configureProvider;
    private readonly string? _sharedConnectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardContextFactory{TContext}"/> class.
    /// </summary>
    /// <param name="shardRegistry">The shard registry.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="contextFactory">The factory function to create context instances.</param>
    /// <param name="configureProvider">Optional action to configure the database provider for each connection string.</param>
    /// <param name="sharedConnectionString">Optional shared connection string for table-level sharding.</param>
    public ShardContextFactory(
        IShardRegistry shardRegistry,
        ILogger<ShardContextFactory<TContext>> logger,
        Func<DbContextOptions<TContext>, TContext> contextFactory,
        Action<DbContextOptionsBuilder<TContext>, string>? configureProvider = null,
        string? sharedConnectionString = null)
    {
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _configureProvider = configureProvider;
        _sharedConnectionString = sharedConnectionString;
    }

    /// <inheritdoc />
    public Task<DbContext> CreateContextAsync(string shardId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var shard = _shardRegistry.GetShard(shardId)
            ?? throw new InvalidOperationException($"Shard '{shardId}' not found in registry.");

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

        // Configure the provider using the provided action, or fall back to default behavior
        if (_configureProvider is not null)
        {
            _configureProvider(optionsBuilder, connectionString);
        }
        else
        {
            optionsBuilder.EnableSensitiveDataLogging(false);
        }

        var context = _contextFactory(optionsBuilder.Options);
        return Task.FromResult<DbContext>(context);
    }

    /// <inheritdoc />
    public string GetConnectionString(string shardId)
    {
        var shard = _shardRegistry.GetShard(shardId)
            ?? throw new InvalidOperationException($"Shard '{shardId}' not found in registry.");

        return ResolveConnectionString(shard);
    }

    /// <inheritdoc />
    public string? GetTableName(string shardId)
    {
        var shard = _shardRegistry.GetShard(shardId);
        return shard?.TableName;
    }

    /// <inheritdoc />
    public string? GetSchemaName(string shardId)
    {
        var shard = _shardRegistry.GetShard(shardId);
        return shard?.SchemaName;
    }

    /// <summary>
    /// Resolves the connection string for a shard based on its storage mode.
    /// </summary>
    /// <param name="shard">The shard metadata.</param>
    /// <returns>The connection string to use.</returns>
    private string ResolveConnectionString(IShardMetadata shard)
    {
        return shard.StorageMode switch
        {
            ShardStorageMode.Tables or ShardStorageMode.Manual =>
                // Table-level sharding uses shared connection string
                _sharedConnectionString
                ?? shard.ConnectionString
                ?? throw new InvalidOperationException(
                    $"Shard '{shard.ShardId}' requires a connection string for table-level sharding."),

            ShardStorageMode.Databases =>
                // Database-level sharding uses per-shard connection string
                shard.ConnectionString
                ?? throw new InvalidOperationException(
                    $"Shard '{shard.ShardId}' does not have a connection string configured for database-level sharding."),

            _ => throw new InvalidOperationException($"Unknown storage mode: {shard.StorageMode}")
        };
    }
}
