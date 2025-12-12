using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dtde.EntityFramework.Query;

/// <summary>
/// A no-op shard context factory for single-database scenarios.
/// Returns the same context for all shard requests.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
public sealed class NullShardContextFactory<TContext> : IShardContextFactory where TContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IShardRegistry _shardRegistry;
    private readonly ILogger<NullShardContextFactory<TContext>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NullShardContextFactory{TContext}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="shardRegistry">The shard registry.</param>
    /// <param name="logger">The logger.</param>
    public NullShardContextFactory(
        IServiceProvider serviceProvider,
        IShardRegistry shardRegistry,
        ILogger<NullShardContextFactory<TContext>> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<DbContext> CreateContextAsync(string shardId, CancellationToken cancellationToken = default)
    {
        LogMessages.NullContextForShard(_logger, shardId);

        // Return the main context from DI - sharding is not enabled
        var context = _serviceProvider.GetService(typeof(TContext)) as DbContext
            ?? throw new InvalidOperationException($"No DbContext of type {typeof(TContext).Name} registered in DI.");

        return Task.FromResult(context);
    }

    /// <inheritdoc />
    public Task<DbContext> CreateContextAsync(IShardMetadata shard, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shard);
        return CreateContextAsync(shard.ShardId, cancellationToken);
    }

    /// <inheritdoc />
    public string GetConnectionString(string shardId)
    {
        var shard = _shardRegistry.GetShard(shardId);
        return shard?.ConnectionString ?? string.Empty;
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
}
