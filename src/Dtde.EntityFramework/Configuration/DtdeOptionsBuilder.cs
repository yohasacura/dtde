using System.Text.Json;

using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;

namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Builder for configuring DTDE options with fluent API.
/// </summary>
public sealed class DtdeOptionsBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly List<IShardMetadata> _shards = [];
    private readonly MetadataRegistry _metadataRegistry = new();
    private Func<DateTime>? _defaultTemporalContextProvider;
    private int _maxParallelShards = 10;
    private bool _enableDiagnostics;
    private bool _enableTestMode;

    /// <summary>
    /// Configures entity metadata for a specific type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to configure.</typeparam>
    /// <param name="configure">Action to configure the entity metadata.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder ConfigureEntity<TEntity>(Action<EntityMetadataBuilder<TEntity>> configure) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new EntityMetadataBuilder<TEntity>();
        configure(builder);
        var metadata = builder.Build();
        _metadataRegistry.RegisterEntity(metadata);

        return this;
    }

    /// <summary>
    /// Adds a pre-built shard metadata.
    /// </summary>
    /// <param name="shard">The shard metadata to add.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder AddShard(IShardMetadata shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        _shards.Add(shard);
        return this;
    }

    /// <summary>
    /// Adds shards from a JSON configuration file.
    /// </summary>
    /// <param name="configPath">Path to the shard configuration file.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder AddShardsFromConfig(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Shard configuration file not found: {configPath}", configPath);
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<ShardConfigurationFile>(json, JsonOptions);

        if (config?.Shards is not null)
        {
            foreach (var shardConfig in config.Shards)
            {
                var builder = new ShardMetadataBuilder()
                    .WithId(shardConfig.ShardId)
                    .WithName(shardConfig.Name ?? shardConfig.ShardId)
                    .WithConnectionString(shardConfig.ConnectionString)
                    .WithTier(Enum.TryParse<ShardTier>(shardConfig.Tier, true, out var tier) ? tier : ShardTier.Hot)
                    .WithPriority(shardConfig.Priority);

                if (shardConfig.IsReadOnly)
                {
                    builder.AsReadOnly();
                }

                if (shardConfig.DateRangeStart.HasValue && shardConfig.DateRangeEnd.HasValue)
                {
                    builder.WithDateRange(shardConfig.DateRangeStart.Value, shardConfig.DateRangeEnd.Value);
                }

                _shards.Add(builder.Build());
            }
        }

        return this;
    }

    /// <summary>
    /// Adds a single shard configuration.
    /// </summary>
    /// <param name="configure">Action to configure the shard.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder AddShard(Action<ShardMetadataBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new ShardMetadataBuilder();
        configure(builder);
        _shards.Add(builder.Build());

        return this;
    }

    /// <summary>
    /// Adds multiple shards from an existing collection.
    /// </summary>
    /// <param name="shards">The shards to add.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder AddShards(IEnumerable<IShardMetadata> shards)
    {
        ArgumentNullException.ThrowIfNull(shards);
        _shards.AddRange(shards);
        return this;
    }

    /// <summary>
    /// Sets the default temporal context provider.
    /// </summary>
    /// <param name="provider">Function returning the default temporal point.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder SetDefaultTemporalContext(Func<DateTime> provider)
    {
        _defaultTemporalContextProvider = provider;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of shards to query in parallel.
    /// </summary>
    /// <param name="maxParallel">Maximum parallel shard queries.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder SetMaxParallelShards(int maxParallel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallel, 1);
        _maxParallelShards = maxParallel;
        return this;
    }

    /// <summary>
    /// Enables diagnostic logging and events.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder EnableDiagnostics()
    {
        _enableDiagnostics = true;
        return this;
    }

    /// <summary>
    /// Enables test mode (single shard, no distribution).
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder EnableTestMode()
    {
        _enableTestMode = true;
        return this;
    }

    /// <summary>
    /// Builds the DTDE options.
    /// </summary>
    /// <returns>The configured options.</returns>
    internal DtdeOptions Build()
    {
        var options = new DtdeOptions
        {
            MetadataRegistry = _metadataRegistry,
            DefaultTemporalContextProvider = _defaultTemporalContextProvider,
            MaxParallelShards = _maxParallelShards,
            EnableDiagnostics = _enableDiagnostics,
            EnableTestMode = _enableTestMode
        };

        options.AddShards(_shards);

        return options;
    }
}

/// <summary>
/// JSON configuration file structure for shards.
/// </summary>
internal sealed class ShardConfigurationFile
{
    public List<ShardConfigurationEntry>? Shards { get; set; }
}

/// <summary>
/// Individual shard configuration entry in JSON.
/// </summary>
internal sealed class ShardConfigurationEntry
{
    public string ShardId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public DateTime? DateRangeStart { get; set; }
    public DateTime? DateRangeEnd { get; set; }
    public string? Tier { get; set; }
    public bool IsReadOnly { get; set; }
    public int Priority { get; set; } = 100;
}
