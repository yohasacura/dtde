using Dtde.Abstractions.Metadata;

namespace Dtde.Core.Metadata;

/// <summary>
/// Metadata for a single shard (table or database).
/// </summary>
public sealed class ShardMetadata : IShardMetadata
{
    /// <inheritdoc />
    public string ShardId { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public ShardStorageMode StorageMode { get; }

    /// <inheritdoc />
    public string? TableName { get; init; }

    /// <inheritdoc />
    public string? SchemaName { get; init; }

    /// <inheritdoc />
    public string? ConnectionString { get; init; }

    /// <inheritdoc />
    public string? ShardKeyValue { get; init; }

    /// <inheritdoc />
    public DateRange? DateRange { get; init; }

    /// <inheritdoc />
    public KeyRange? KeyRange { get; init; }

    /// <inheritdoc />
    public ShardTier Tier { get; init; } = ShardTier.Hot;

    /// <inheritdoc />
    public bool IsReadOnly { get; init; }

    /// <inheritdoc />
    public int Priority { get; init; } = 100;

    /// <summary>
    /// Creates a new ShardMetadata for table-based sharding.
    /// </summary>
    /// <param name="shardId">Unique identifier for the shard.</param>
    /// <param name="name">Display name for the shard.</param>
    /// <param name="storageMode">The storage mode for this shard.</param>
    public ShardMetadata(string shardId, string name, ShardStorageMode storageMode)
    {
        ShardId = shardId ?? throw new ArgumentNullException(nameof(shardId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        StorageMode = storageMode;
    }

    /// <summary>
    /// Creates a table shard with the specified table name.
    /// </summary>
    /// <param name="shardId">Unique identifier for the shard.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="shardKeyValue">The shard key value this shard handles.</param>
    /// <param name="schemaName">Optional schema name (defaults to "dbo").</param>
    /// <returns>A new table shard metadata.</returns>
    public static ShardMetadata ForTable(
        string shardId,
        string tableName,
        string? shardKeyValue = null,
        string schemaName = "dbo")
    {
        return new ShardMetadata(shardId, tableName, ShardStorageMode.Tables)
        {
            TableName = tableName,
            SchemaName = schemaName,
            ShardKeyValue = shardKeyValue
        };
    }

    /// <summary>
    /// Creates a database shard with the specified connection string.
    /// </summary>
    /// <param name="shardId">Unique identifier for the shard.</param>
    /// <param name="name">Display name for the shard.</param>
    /// <param name="connectionString">Connection string for the database.</param>
    /// <param name="shardKeyValue">The shard key value this shard handles.</param>
    /// <returns>A new database shard metadata.</returns>
    public static ShardMetadata ForDatabase(
        string shardId,
        string name,
        string connectionString,
        string? shardKeyValue = null)
    {
        return new ShardMetadata(shardId, name, ShardStorageMode.Databases)
        {
            ConnectionString = connectionString,
            ShardKeyValue = shardKeyValue
        };
    }

    /// <summary>
    /// Creates a manual (pre-created) table shard.
    /// </summary>
    /// <param name="shardId">Unique identifier for the shard.</param>
    /// <param name="tableName">The pre-created table name.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>A new manual shard metadata.</returns>
    public static ShardMetadata ForManualTable(
        string shardId,
        string tableName,
        string schemaName = "dbo")
    {
        return new ShardMetadata(shardId, tableName, ShardStorageMode.Manual)
        {
            TableName = tableName,
            SchemaName = schemaName
        };
    }
}

/// <summary>
/// Builder for creating ShardMetadata instances with fluent API.
/// </summary>
public sealed class ShardMetadataBuilder
{
    private string _shardId = string.Empty;
    private string _name = string.Empty;
    private ShardStorageMode _storageMode = ShardStorageMode.Tables;
    private string? _tableName;
    private string? _schemaName = "dbo";
    private string? _connectionString;
    private string? _shardKeyValue;
    private DateRange? _dateRange;
    private KeyRange? _keyRange;
    private ShardTier _tier = ShardTier.Hot;
    private bool _isReadOnly;
    private int _priority = 100;

    /// <summary>
    /// Sets the unique identifier for the shard.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithId(string shardId)
    {
        _shardId = shardId;
        return this;
    }

    /// <summary>
    /// Sets the display name for the shard.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    /// Sets the storage mode for the shard.
    /// </summary>
    /// <param name="mode">The storage mode.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithStorageMode(ShardStorageMode mode)
    {
        _storageMode = mode;
        return this;
    }

    /// <summary>
    /// Sets the table name for table-based sharding.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="schemaName">Optional schema name.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithTable(string tableName, string schemaName = "dbo")
    {
        _tableName = tableName;
        _schemaName = schemaName;
        // Only set storage mode if not explicitly set to Manual
        if (_storageMode != ShardStorageMode.Manual)
        {
            _storageMode = ShardStorageMode.Tables;
        }
        return this;
    }

    /// <summary>
    /// Sets the connection string for database sharding.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithConnectionString(string connectionString)
    {
        _connectionString = connectionString;
        _storageMode = ShardStorageMode.Databases;
        return this;
    }

    /// <summary>
    /// Sets the shard key value this shard handles.
    /// </summary>
    /// <param name="value">The shard key value.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithShardKeyValue(string value)
    {
        _shardKeyValue = value;
        return this;
    }

    /// <summary>
    /// Sets the date range for date-based sharding.
    /// </summary>
    /// <param name="start">The inclusive start date.</param>
    /// <param name="end">The exclusive end date.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithDateRange(DateTime start, DateTime end)
    {
        _dateRange = new DateRange(start, end);
        return this;
    }

    /// <summary>
    /// Sets the date range for date-based sharding.
    /// </summary>
    /// <param name="dateRange">The date range.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithDateRange(DateRange dateRange)
    {
        _dateRange = dateRange;
        return this;
    }

    /// <summary>
    /// Sets the key range for range-based sharding.
    /// </summary>
    /// <param name="keyRange">The key range.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithKeyRange(KeyRange keyRange)
    {
        _keyRange = keyRange;
        return this;
    }

    /// <summary>
    /// Sets the shard tier for hot/warm/cold classification.
    /// </summary>
    /// <param name="tier">The shard tier.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithTier(ShardTier tier)
    {
        _tier = tier;
        return this;
    }

    /// <summary>
    /// Marks the shard as read-only.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder AsReadOnly()
    {
        _isReadOnly = true;
        return this;
    }

    /// <summary>
    /// Sets the query priority for this shard (lower = higher priority).
    /// </summary>
    /// <param name="priority">The priority value.</param>
    /// <returns>The builder for chaining.</returns>
    public ShardMetadataBuilder WithPriority(int priority)
    {
        _priority = priority;
        return this;
    }

    /// <summary>
    /// Builds the ShardMetadata instance.
    /// </summary>
    /// <returns>The constructed ShardMetadata.</returns>
    public ShardMetadata Build()
    {
        if (string.IsNullOrWhiteSpace(_shardId))
        {
            throw new InvalidOperationException("ShardId is required.");
        }

        // Validate based on storage mode
        if (_storageMode == ShardStorageMode.Databases && string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("ConnectionString is required for database sharding.");
        }

        if ((_storageMode == ShardStorageMode.Tables || _storageMode == ShardStorageMode.Manual) 
            && string.IsNullOrWhiteSpace(_tableName))
        {
            throw new InvalidOperationException("TableName is required for table-based sharding.");
        }

        var name = string.IsNullOrWhiteSpace(_name) ? _shardId : _name;

        return new ShardMetadata(_shardId, name, _storageMode)
        {
            TableName = _tableName,
            SchemaName = _schemaName,
            ConnectionString = _connectionString,
            ShardKeyValue = _shardKeyValue,
            DateRange = _dateRange,
            KeyRange = _keyRange,
            Tier = _tier,
            IsReadOnly = _isReadOnly,
            Priority = _priority
        };
    }
}
