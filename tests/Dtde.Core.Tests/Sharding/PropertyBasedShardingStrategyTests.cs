using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.Core.Sharding;
using System.Linq.Expressions;
using System.Reflection;

namespace Dtde.Core.Tests.Sharding;

/// <summary>
/// Tests for <see cref="PropertyBasedShardingStrategy"/>.
/// </summary>
public class PropertyBasedShardingStrategyTests
{
    private readonly PropertyBasedShardingStrategy _strategy;

    public PropertyBasedShardingStrategyTests()
    {
        _strategy = new PropertyBasedShardingStrategy();
    }

    [Fact]
    public void StrategyType_ReturnsPropertyValue()
    {
        Assert.Equal(ShardingStrategyType.PropertyValue, _strategy.StrategyType);
    }

    [Fact]
    public void ResolveShards_WithMatchingKeyValue_ReturnsMatchingShard()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        var usShard = CreateShard("Customers_US", "US", ShardTier.Hot);
        var apacShard = CreateShard("Customers_APAC", "APAC", ShardTier.Hot);

        var allShards = new List<IShardMetadata> { euShard, usShard, apacShard };
        var registry = new TestShardRegistry(allShards);

        var entityMetadata = CreateEntityMetadata("Region");
        var predicates = new Dictionary<string, object?> { { "Region", "US" } };

        var result = _strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Single(result);
        Assert.Equal("Customers_US", result[0].ShardId);
    }

    [Fact]
    public void ResolveShards_WithNoMatchingKey_ReturnsAllShards()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        var usShard = CreateShard("Customers_US", "US", ShardTier.Hot);

        var allShards = new List<IShardMetadata> { euShard, usShard };
        var registry = new TestShardRegistry(allShards);

        var entityMetadata = CreateEntityMetadata("Region");
        var predicates = new Dictionary<string, object?> { { "Region", "UNKNOWN" } };

        var result = _strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ResolveShards_WithNoPredicate_ReturnsAllShards()
    {
        var shard1 = CreateShard("Shard1", "A", ShardTier.Hot, priority: 2);
        var shard2 = CreateShard("Shard2", "B", ShardTier.Hot, priority: 1);

        var allShards = new List<IShardMetadata> { shard1, shard2 };
        var registry = new TestShardRegistry(allShards);

        var entityMetadata = CreateEntityMetadata("Key");
        var predicates = new Dictionary<string, object?>();

        var result = _strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Equal(2, result.Count);
        Assert.Equal("Shard2", result[0].ShardId);
    }

    [Fact]
    public void ResolveShards_WithCaseInsensitiveMatch_ReturnsMatchingShard()
    {
        var euShard = CreateShard("Customers_EU", "eu", ShardTier.Hot);
        var allShards = new List<IShardMetadata> { euShard };
        var registry = new TestShardRegistry(allShards);

        var entityMetadata = CreateEntityMetadata("Region");
        var predicates = new Dictionary<string, object?> { { "Region", "EU" } };

        var result = _strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Single(result);
        Assert.Equal("Customers_EU", result[0].ShardId);
    }

    [Fact]
    public void ResolveWriteShard_WithValidKeyValue_ReturnsMatchingShard()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        var usShard = CreateShard("Customers_US", "US", ShardTier.Hot);
        var allShards = new List<IShardMetadata> { euShard, usShard };
        var registry = new TestShardRegistry(allShards);

        var entity = new TestCustomer { Region = "EU" };
        var entityMetadata = CreateEntityMetadata("Region", entity.GetType());

        var result = _strategy.ResolveWriteShard(entityMetadata, registry, entity);

        Assert.Equal("Customers_EU", result.ShardId);
    }

    [Fact]
    public void ResolveWriteShard_WithNoMatchingKey_ThrowsShardNotFoundException()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        var allShards = new List<IShardMetadata> { euShard };
        var registry = new TestShardRegistry(allShards);

        var entity = new TestCustomer { Region = "UNKNOWN" };
        var entityMetadata = CreateEntityMetadata("Region", entity.GetType());

        Assert.Throws<ShardNotFoundException>(() =>
            _strategy.ResolveWriteShard(entityMetadata, registry, entity));
    }

    [Fact]
    public void ResolveWriteShard_WithNullKeyValue_ThrowsShardNotFoundException()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        var registry = new TestShardRegistry(new List<IShardMetadata> { euShard });

        var entity = new TestCustomer { Region = null! };
        var entityMetadata = CreateEntityMetadata("Region", entity.GetType());

        Assert.Throws<ShardNotFoundException>(() =>
            _strategy.ResolveWriteShard(entityMetadata, registry, entity));
    }

    [Fact]
    public void ResolveWriteShard_WithDefaultShard_UsesDefaultWhenNoMatch()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        var defaultShard = CreateShardWithEmptyKey("Customers_Default", ShardTier.Hot);
        var allShards = new List<IShardMetadata> { euShard, defaultShard };
        var registry = new TestShardRegistry(allShards);

        var entity = new TestCustomer { Region = "UNKNOWN" };
        var entityMetadata = CreateEntityMetadata("Region", entity.GetType());

        var result = _strategy.ResolveWriteShard(entityMetadata, registry, entity);

        Assert.Equal("Customers_Default", result.ShardId);
    }

    private static IShardMetadata CreateShard(
        string shardId,
        string shardKeyValue,
        ShardTier tier,
        int priority = 0)
    {
        return new ShardMetadataBuilder()
            .WithId(shardId)
            .WithName(shardId)
            .WithConnectionString($"Server=localhost;Database={shardId}")
            .WithTier(tier)
            .WithPriority(priority)
            .WithShardKeyValue(shardKeyValue)
            .Build();
    }

    private static IShardMetadata CreateShardWithEmptyKey(
        string shardId,
        ShardTier tier,
        int priority = 0)
    {
        return new ShardMetadataBuilder()
            .WithId(shardId)
            .WithName(shardId)
            .WithConnectionString($"Server=localhost;Database={shardId}")
            .WithTier(tier)
            .WithPriority(priority)
            .Build();
    }

    private static IEntityMetadata CreateEntityMetadata(string shardKeyPropertyName, Type? entityType = null)
    {
        entityType ??= typeof(TestCustomer);
        return new TestEntityMetadata(entityType, shardKeyPropertyName);
    }

    /// <summary>
    /// Test double for IShardRegistry.
    /// </summary>
    private sealed class TestShardRegistry : IShardRegistry
    {
        private readonly IReadOnlyList<IShardMetadata> _shards;

        public TestShardRegistry(IReadOnlyList<IShardMetadata> shards)
        {
            _shards = shards.OrderBy(s => s.Priority).ToList();
        }

        public IReadOnlyList<IShardMetadata> GetAllShards() => _shards;

        public IShardMetadata? GetShard(string shardId) =>
            _shards.FirstOrDefault(s => s.ShardId == shardId);

        public IReadOnlyList<IShardMetadata> GetShardsByTier(ShardTier tier) =>
            _shards.Where(s => s.Tier == tier).ToList();

        public IReadOnlyList<IShardMetadata> GetWritableShards() =>
            _shards.Where(s => !s.IsReadOnly).ToList();

        public IReadOnlyList<IShardMetadata> GetShardsForDateRange(DateTime startDate, DateTime endDate) =>
            _shards;
    }

    /// <summary>
    /// Test double for IEntityMetadata.
    /// </summary>
    private sealed class TestEntityMetadata : IEntityMetadata
    {
        private readonly TestShardingConfiguration _shardingConfig;

        public TestEntityMetadata(Type entityType, string shardKeyPropertyName)
        {
            ClrType = entityType;
            _shardingConfig = new TestShardingConfiguration(entityType, shardKeyPropertyName);
        }

        public Type ClrType { get; }
        public string TableName => ClrType.Name;
        public string SchemaName => "dbo";
        public IPropertyMetadata? PrimaryKey => null;
        public IValidityConfiguration? Validity => null;
        public IShardingConfiguration? Sharding => _shardingConfig;
        public bool IsTemporal => false;
        public bool IsSharded => true;
    }

    /// <summary>
    /// Test double for IShardingConfiguration.
    /// </summary>
    private sealed class TestShardingConfiguration : IShardingConfiguration
    {
        public TestShardingConfiguration(Type entityType, string shardKeyPropertyName)
        {
            var propInfo = entityType.GetProperty(shardKeyPropertyName);
            ShardKeyProperties = propInfo is not null
                ? new List<IPropertyMetadata> { new TestPropertyMetadata(propInfo) }
                : new List<IPropertyMetadata>();
        }

        public ShardingStrategyType StrategyType => ShardingStrategyType.PropertyValue;
        public ShardStorageMode StorageMode => ShardStorageMode.Tables;
        public LambdaExpression? ShardKeyExpression => null;
        public IReadOnlyList<IPropertyMetadata> ShardKeyProperties { get; }
        public IShardingStrategy Strategy => new PropertyBasedShardingStrategy();
        public bool MigrationsEnabled => false;
        public string? TableNamePattern => null;
        public DateShardInterval? DateInterval => null;
    }

    /// <summary>
    /// Test double for IPropertyMetadata.
    /// </summary>
    private sealed class TestPropertyMetadata : IPropertyMetadata
    {
        private readonly PropertyInfo _propertyInfo;

        public TestPropertyMetadata(PropertyInfo propertyInfo)
        {
            _propertyInfo = propertyInfo;
        }

        public string PropertyName => _propertyInfo.Name;
        public Type PropertyType => _propertyInfo.PropertyType;
        public string ColumnName => _propertyInfo.Name;
        public PropertyInfo PropertyInfo => _propertyInfo;

        public object? GetValue(object entity) => _propertyInfo.GetValue(entity);
        public void SetValue(object entity, object? value) => _propertyInfo.SetValue(entity, value);
    }

    private class TestCustomer
    {
        public int Id { get; set; }
        public string Region { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
