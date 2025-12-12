using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Temporal;
using Dtde.Core.Metadata;
using Dtde.Core.Temporal;

namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// DTDE configuration options.
/// </summary>
public sealed class DtdeOptions
{
    private readonly List<IShardMetadata> _shards = [];

    /// <summary>
    /// Gets the list of configured shards.
    /// </summary>
    public IList<IShardMetadata> Shards => _shards;

    /// <summary>
    /// Adds a shard to the configuration.
    /// </summary>
    /// <param name="shard">The shard to add.</param>
    public void AddShard(IShardMetadata shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        _shards.Add(shard);
    }

    /// <summary>
    /// Adds multiple shards to the configuration.
    /// </summary>
    /// <param name="shards">The shards to add.</param>
    public void AddShards(IEnumerable<IShardMetadata> shards)
    {
        ArgumentNullException.ThrowIfNull(shards);
        _shards.AddRange(shards);
    }

    /// <summary>
    /// Gets or sets the default temporal context provider.
    /// </summary>
    public Func<DateTime>? DefaultTemporalContextProvider { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of shards to query in parallel.
    /// </summary>
    public int MaxParallelShards { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether diagnostics are enabled.
    /// </summary>
    public bool EnableDiagnostics { get; set; }

    /// <summary>
    /// Gets or sets whether test mode is enabled (single shard, no distribution).
    /// </summary>
    public bool EnableTestMode { get; set; }

    /// <summary>
    /// Gets or sets the metadata registry.
    /// </summary>
    public IMetadataRegistry MetadataRegistry { get; set; } = new MetadataRegistry();

    /// <summary>
    /// Gets or sets the shard registry.
    /// </summary>
    public IShardRegistry ShardRegistry { get; set; } = new ShardRegistry();

    /// <summary>
    /// Gets or sets the temporal context.
    /// </summary>
    public ITemporalContext TemporalContext { get; set; } = new TemporalContext();
}
