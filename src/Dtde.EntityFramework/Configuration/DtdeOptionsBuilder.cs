using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;

namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Fluent builder for configuring DTDE inside <c>UseDtde(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// All entity configuration (sharding, temporal validity) lives in
/// <c>DbContext.OnModelCreating</c> — it is not configured here. This builder
/// only declares the available shards and global runtime options.
/// </para>
/// <para>
/// The shard-registration overloads, in order of preference for everyday use:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <see cref="AddShards(string[])"/> — bulk table-mode for the simple case
///     (single database, multiple per-shard tables).
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="AddShard(string, string)"/> — database-mode shorthand:
///     supply the shard id and a connection string.
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="AddShard(string)"/> — single table-mode shard shorthand.
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="AddShard(System.Action{ShardMetadataBuilder})"/> — full fluent
///     control: tier, read-only, date range, key range, custom name and
///     priority.
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="AddShardsFromConfig(string)"/> — load shard definitions from a
///     JSON file (operations-team-friendly).
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class DtdeOptionsBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly List<IShardMetadata> _shards = [];
    private Func<DateTime>? _defaultTemporalContextProvider;
    private int _maxParallelShards = 10;
    private bool _enableDiagnostics;
    private bool _enableTestMode;

    // ------------------------------------------------------------------
    //  Shard registration — shorthand overloads (the common path)
    // ------------------------------------------------------------------

    /// <summary>
    /// Adds a table-mode shard for the given shard-key value. The shard's
    /// connection string is inherited from the parent <c>DbContextOptions</c>;
    /// the table name is derived from the entity's
    /// <c>WithTablePattern</c> setting (or the entity's table name + shard id).
    /// </summary>
    /// <param name="shardId">The shard identifier; also used as the shard-key value.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// dtde.AddShard("EU");
    /// </code>
    /// </example>
    public DtdeOptionsBuilder AddShard(string shardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        _shards.Add(new ShardMetadataBuilder()
            .WithId(shardId)
            .WithName(shardId)
            .WithShardKeyValue(shardId)
            .WithStorageMode(ShardStorageMode.Tables)
            .Build());
        return this;
    }

    /// <summary>
    /// Adds a database-mode shard for the given shard-key value with its own
    /// connection string.
    /// </summary>
    /// <param name="shardId">The shard identifier; also used as the shard-key value.</param>
    /// <param name="connectionString">The connection string for this shard's database.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// dtde.AddShard("EU", "Server=eu-db;Database=Customers;...");
    /// </code>
    /// </example>
    public DtdeOptionsBuilder AddShard(string shardId, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _shards.Add(new ShardMetadataBuilder()
            .WithId(shardId)
            .WithName(shardId)
            .WithShardKeyValue(shardId)
            .WithConnectionString(connectionString)
            .Build());
        return this;
    }

    /// <summary>
    /// Adds multiple table-mode shards in one call. Each shard id doubles as
    /// the shard-key value. The connection string is inherited from the parent
    /// <c>DbContextOptions</c>.
    /// </summary>
    /// <param name="shardIds">The shard identifiers.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// dtde.AddShards("EU", "US", "APAC");
    /// </code>
    /// </example>
    public DtdeOptionsBuilder AddShards(params string[] shardIds)
    {
        ArgumentNullException.ThrowIfNull(shardIds);

        foreach (var shardId in shardIds)
        {
            AddShard(shardId);
        }
        return this;
    }

    // ------------------------------------------------------------------
    //  Shard registration — full control
    // ------------------------------------------------------------------

    /// <summary>
    /// Adds a shard configured via the full fluent <see cref="ShardMetadataBuilder"/>.
    /// Use this when you need to set tier, priority, read-only, date/key ranges,
    /// or any other advanced option.
    /// </summary>
    /// <param name="configure">Callback that configures the shard.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// dtde.AddShard(s => s
    ///     .WithId("2024-archive")
    ///     .WithConnectionString(archiveConnectionString)
    ///     .WithTier(ShardTier.Cold)
    ///     .AsReadOnly());
    /// </code>
    /// </example>
    public DtdeOptionsBuilder AddShard(Action<ShardMetadataBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new ShardMetadataBuilder();
        configure(builder);
        _shards.Add(builder.Build());

        return this;
    }

    /// <summary>
    /// Loads shards from a JSON configuration file. The file's schema is
    /// documented in <c>docs/wiki/configuration.md</c>.
    /// </summary>
    /// <param name="configPath">Path to the JSON file.</param>
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

    // ------------------------------------------------------------------
    //  Global runtime options
    // ------------------------------------------------------------------

    /// <summary>
    /// Sets the function returning the default "now" used by temporal queries
    /// when no explicit point-in-time is supplied. Defaults to <see cref="DateTime.UtcNow"/>.
    /// </summary>
    /// <param name="provider">The "now" provider.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder SetDefaultTemporalContext(Func<DateTime> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _defaultTemporalContextProvider = provider;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of shards DTDE will query in parallel
    /// when fanning out a query.
    /// </summary>
    /// <param name="maxParallel">Maximum parallel shard queries. Must be positive.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder SetMaxParallelShards(int maxParallel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallel, 1);
        _maxParallelShards = maxParallel;
        return this;
    }

    /// <summary>
    /// Enables verbose diagnostic logging for shard routing and query execution.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder EnableDiagnostics()
    {
        _enableDiagnostics = true;
        return this;
    }

    /// <summary>
    /// Enables test mode (single-shard fallback, no fan-out). Use only in
    /// integration tests where you don't want to spin up multiple databases.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder EnableTestMode()
    {
        _enableTestMode = true;
        return this;
    }

    // ------------------------------------------------------------------
    //  Build
    // ------------------------------------------------------------------

    internal DtdeOptions Build()
    {
        var options = new DtdeOptions
        {
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
/// JSON file shape consumed by <see cref="DtdeOptionsBuilder.AddShardsFromConfig(string)"/>.
/// </summary>
internal sealed class ShardConfigurationFile
{
    public List<ShardConfigurationEntry>? Shards { get; set; }
}

/// <summary>
/// Single shard entry in a shard-configuration JSON file.
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
