using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.Core.Sharding;
using Moq;

namespace Dtde.Core.Tests.Sharding;

/// <summary>
/// Tests for <see cref="PropertyBasedShardingStrategy"/>.
/// </summary>
public class PropertyBasedShardingStrategyTests
{
    private readonly PropertyBasedShardingStrategy _strategy;
    private readonly Mock<IShardRegistry> _mockRegistry;

    public PropertyBasedShardingStrategyTests()
    {
        _strategy = new PropertyBasedShardingStrategy();
        _mockRegistry = new Mock<IShardRegistry>();
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
        _mockRegistry.Setup(r => r.GetAllShards()).Returns(allShards);

        var entityMetadata = CreateMockEntityMetadata("Region");
        var predicates = new Dictionary<string, object?> { { "Region", "US" } };

        var result = _strategy.ResolveShards(entityMetadata, _mockRegistry.Object, predicates, null);

        Assert.Single(result);
        Assert.Equal("Customers_US", result[0].ShardId);
    }

    [Fact]
    public void ResolveShards_WithNoMatchingKey_ReturnsAllShards()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        var usShard = CreateShard("Customers_US", "US", ShardTier.Hot);

        var allShards = new List<IShardMetadata> { euShard, usShard };
        _mockRegistry.Setup(r => r.GetAllShards()).Returns(allShards);

        var entityMetadata = CreateMockEntityMetadata("Region");
        var predicates = new Dictionary<string, object?> { { "Region", "UNKNOWN" } };

        var result = _strategy.ResolveShards(entityMetadata, _mockRegistry.Object, predicates, null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ResolveShards_WithNoPredicate_ReturnsAllShards()
    {
        var shard1 = CreateShard("Shard1", "A", ShardTier.Hot, priority: 2);
        var shard2 = CreateShard("Shard2", "B", ShardTier.Hot, priority: 1);

        var allShards = new List<IShardMetadata> { shard1, shard2 };
        _mockRegistry.Setup(r => r.GetAllShards()).Returns(allShards);

        var entityMetadata = CreateMockEntityMetadata("Key");
        var predicates = new Dictionary<string, object?>();

        var result = _strategy.ResolveShards(entityMetadata, _mockRegistry.Object, predicates, null);

        Assert.Equal(2, result.Count);
        Assert.Equal("Shard2", result[0].ShardId);
    }

    [Fact]
    public void ResolveShards_WithCaseInsensitiveMatch_ReturnsMatchingShard()
    {
        var euShard = CreateShard("Customers_EU", "eu", ShardTier.Hot);
        var allShards = new List<IShardMetadata> { euShard };
        _mockRegistry.Setup(r => r.GetAllShards()).Returns(allShards);

        var entityMetadata = CreateMockEntityMetadata("Region");
        var predicates = new Dictionary<string, object?> { { "Region", "EU" } };

        var result = _strategy.ResolveShards(entityMetadata, _mockRegistry.Object, predicates, null);

        Assert.Single(result);
        Assert.Equal("Customers_EU", result[0].ShardId);
    }

    [Fact]
    public void ResolveWriteShard_WithValidKeyValue_ReturnsMatchingShard()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        var usShard = CreateShard("Customers_US", "US", ShardTier.Hot);
        var allShards = new List<IShardMetadata> { euShard, usShard };

        _mockRegistry.Setup(r => r.GetWritableShards()).Returns(allShards);

        var entity = new TestCustomer { Region = "EU" };
        var entityMetadata = CreateMockEntityMetadata("Region", entity.GetType());

        var result = _strategy.ResolveWriteShard(entityMetadata, _mockRegistry.Object, entity);

        Assert.Equal("Customers_EU", result.ShardId);
    }

    [Fact]
    public void ResolveWriteShard_WithNoMatchingKey_ThrowsShardNotFoundException()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        var allShards = new List<IShardMetadata> { euShard };

        _mockRegistry.Setup(r => r.GetWritableShards()).Returns(allShards);

        var entity = new TestCustomer { Region = "UNKNOWN" };
        var entityMetadata = CreateMockEntityMetadata("Region", entity.GetType());

        Assert.Throws<ShardNotFoundException>(() =>
            _strategy.ResolveWriteShard(entityMetadata, _mockRegistry.Object, entity));
    }

    [Fact]
    public void ResolveWriteShard_WithNullKeyValue_ThrowsShardNotFoundException()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        _mockRegistry.Setup(r => r.GetWritableShards()).Returns(new List<IShardMetadata> { euShard });

        var entity = new TestCustomer { Region = null! };
        var entityMetadata = CreateMockEntityMetadata("Region", entity.GetType());

        Assert.Throws<ShardNotFoundException>(() =>
            _strategy.ResolveWriteShard(entityMetadata, _mockRegistry.Object, entity));
    }

    [Fact]
    public void ResolveWriteShard_WithDefaultShard_UsesDefaultWhenNoMatch()
    {
        var euShard = CreateShard("Customers_EU", "EU", ShardTier.Hot);
        var defaultShard = CreateShardWithEmptyKey("Customers_Default", ShardTier.Hot);
        var allShards = new List<IShardMetadata> { euShard, defaultShard };

        _mockRegistry.Setup(r => r.GetWritableShards()).Returns(allShards);

        var entity = new TestCustomer { Region = "UNKNOWN" };
        var entityMetadata = CreateMockEntityMetadata("Region", entity.GetType());

        var result = _strategy.ResolveWriteShard(entityMetadata, _mockRegistry.Object, entity);

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

    private static IEntityMetadata CreateMockEntityMetadata(string shardKeyPropertyName, Type? entityType = null)
    {
        var mockMetadata = new Mock<IEntityMetadata>();
        var mockShardingConfig = new Mock<IShardingConfiguration>();
        var mockPropertyMetadata = new Mock<IPropertyMetadata>();

        entityType ??= typeof(TestCustomer);

        mockPropertyMetadata.Setup(p => p.PropertyName).Returns(shardKeyPropertyName);
        mockPropertyMetadata.Setup(p => p.GetValue(It.IsAny<object>()))
            .Returns<object>(entity =>
            {
                var prop = entity.GetType().GetProperty(shardKeyPropertyName);
                return prop?.GetValue(entity);
            });

        mockShardingConfig.Setup(c => c.ShardKeyProperties)
            .Returns(new List<IPropertyMetadata> { mockPropertyMetadata.Object });

        mockMetadata.Setup(m => m.ClrType).Returns(entityType);
        mockMetadata.Setup(m => m.Sharding).Returns(mockShardingConfig.Object);
        mockMetadata.Setup(m => m.ShardingConfiguration).Returns(mockShardingConfig.Object);

        return mockMetadata.Object;
    }

    private class TestCustomer
    {
        public int Id { get; set; }
        public string Region { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
