using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.Core.Sharding;
using System.Linq.Expressions;
using System.Reflection;

namespace Dtde.Core.Tests.Sharding;

/// <summary>
/// Tests for <see cref="HashShardingStrategy"/>.
/// </summary>
public class HashShardingStrategyTests
{
    [Fact]
    public void Constructor_WithValidShardCount_CreatesStrategy()
    {
        var strategy = new HashShardingStrategy(4);

        Assert.Equal(ShardingStrategyType.Hash, strategy.StrategyType);
    }

    [Fact]
    public void Constructor_WithZeroShardCount_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HashShardingStrategy(0));
    }

    [Fact]
    public void Constructor_WithNegativeShardCount_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HashShardingStrategy(-1));
    }

    [Fact]
    public void StrategyType_ReturnsHash()
    {
        var strategy = new HashShardingStrategy(4);

        Assert.Equal(ShardingStrategyType.Hash, strategy.StrategyType);
    }

    [Fact]
    public void ComputeShardIndex_ReturnsConsistentResultForSameValue()
    {
        var strategy = new HashShardingStrategy(4);

        var index1 = strategy.ComputeShardIndex("TestKey");
        var index2 = strategy.ComputeShardIndex("TestKey");

        Assert.Equal(index1, index2);
    }

    [Fact]
    public void ComputeShardIndex_ReturnsIndexWithinShardCount()
    {
        var strategy = new HashShardingStrategy(4);

        for (var i = 0; i < 100; i++)
        {
            var index = strategy.ComputeShardIndex($"Key{i}");
            Assert.InRange(index, 0, 3);
        }
    }

    [Fact]
    public void ComputeShardIndex_DistributesKeysAcrossShards()
    {
        var strategy = new HashShardingStrategy(4);
        var shardCounts = new int[4];

        for (var i = 0; i < 1000; i++)
        {
            var index = strategy.ComputeShardIndex(Guid.NewGuid());
            shardCounts[index]++;
        }

        Assert.All(shardCounts, count => Assert.True(count > 100, "Expected each shard to have > 100 keys"));
    }

    [Fact]
    public void ComputeShardIndex_ThrowsForNullKeyValue()
    {
        var strategy = new HashShardingStrategy(4);

        Assert.Throws<ArgumentNullException>(() => strategy.ComputeShardIndex(null!));
    }

    [Fact]
    public void ComputeShardIndex_HandlesNumericKeys()
    {
        var strategy = new HashShardingStrategy(4);

        var index1 = strategy.ComputeShardIndex(123);
        var index2 = strategy.ComputeShardIndex(123);

        Assert.Equal(index1, index2);
        Assert.InRange(index1, 0, 3);
    }

    [Fact]
    public void ComputeShardIndex_HandlesDifferentNumericKeysWithSameValue()
    {
        var strategy = new HashShardingStrategy(4);

        var intIndex = strategy.ComputeShardIndex(42);
        var longIndex = strategy.ComputeShardIndex(42L);

        Assert.InRange(intIndex, 0, 3);
        Assert.InRange(longIndex, 0, 3);
    }

    [Fact]
    public void ResolveShards_WithNoShardingConfig_ReturnsAllShards()
    {
        var strategy = new HashShardingStrategy(4);
        var shards = CreateShards(4);
        var registry = new ShardRegistry(shards);
        var entityMetadata = CreateEntityMetadataWithoutSharding();
        var predicates = new Dictionary<string, object?>();

        var result = strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void ResolveShards_WithNoShardKeyPredicates_ReturnsAllShards()
    {
        var strategy = new HashShardingStrategy(4);
        var shards = CreateShards(4);
        var registry = new ShardRegistry(shards);
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId");
        var predicates = new Dictionary<string, object?> { { "Name", "Test" } };

        var result = strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void ResolveShards_WithShardKeyPredicate_ReturnsSingleShard()
    {
        var strategy = new HashShardingStrategy(4);
        var shards = CreateShards(4);
        var registry = new ShardRegistry(shards);
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId");
        var predicates = new Dictionary<string, object?> { { "CustomerId", 12345 } };

        var result = strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Single(result);
    }

    [Fact]
    public void ResolveShards_WithNullShardKeyPredicate_ReturnsAllShards()
    {
        var strategy = new HashShardingStrategy(4);
        var shards = CreateShards(4);
        var registry = new ShardRegistry(shards);
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId");
        var predicates = new Dictionary<string, object?> { { "CustomerId", null } };

        var result = strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void ResolveShards_WithEmptyShardKeyProperties_ReturnsAllShards()
    {
        var strategy = new HashShardingStrategy(4);
        var shards = CreateShards(4);
        var registry = new ShardRegistry(shards);
        var entityMetadata = CreateEntityMetadataWithEmptyShardKeyProperties();
        var predicates = new Dictionary<string, object?> { { "CustomerId", 12345 } };

        var result = strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void ResolveShards_ThrowsForNullEntity()
    {
        var strategy = new HashShardingStrategy(4);
        var registry = new ShardRegistry();
        var predicates = new Dictionary<string, object?>();

        Assert.Throws<ArgumentNullException>(() =>
            strategy.ResolveShards(null!, registry, predicates, null));
    }

    [Fact]
    public void ResolveShards_ThrowsForNullRegistry()
    {
        var strategy = new HashShardingStrategy(4);
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId");
        var predicates = new Dictionary<string, object?>();

        Assert.Throws<ArgumentNullException>(() =>
            strategy.ResolveShards(entityMetadata, null!, predicates, null));
    }

    [Fact]
    public void ResolveShards_ThrowsForNullPredicates()
    {
        var strategy = new HashShardingStrategy(4);
        var registry = new ShardRegistry();
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId");

        Assert.Throws<ArgumentNullException>(() =>
            strategy.ResolveShards(entityMetadata, registry, null!, null));
    }

    [Fact]
    public void ResolveWriteShard_WithValidEntity_ReturnsShard()
    {
        var strategy = new HashShardingStrategy(4);
        var shards = CreateShards(4);
        var registry = new ShardRegistry(shards);
        var entity = new TestHashedEntity { CustomerId = 12345 };
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId", typeof(TestHashedEntity));

        var result = strategy.ResolveWriteShard(entityMetadata, registry, entity);

        Assert.NotNull(result);
    }

    [Fact]
    public void ResolveWriteShard_WithSameKeyValue_AlwaysReturnsSameShard()
    {
        var strategy = new HashShardingStrategy(4);
        var shards = CreateShards(4);
        var registry = new ShardRegistry(shards);
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId", typeof(TestHashedEntity));

        var entity1 = new TestHashedEntity { CustomerId = 12345 };
        var entity2 = new TestHashedEntity { CustomerId = 12345 };

        var result1 = strategy.ResolveWriteShard(entityMetadata, registry, entity1);
        var result2 = strategy.ResolveWriteShard(entityMetadata, registry, entity2);

        Assert.Equal(result1.ShardId, result2.ShardId);
    }

    [Fact]
    public void ResolveWriteShard_WithNoShardingConfig_ThrowsShardNotFoundException()
    {
        var strategy = new HashShardingStrategy(4);
        var shards = CreateShards(4);
        var registry = new ShardRegistry(shards);
        var entity = new TestHashedEntity { CustomerId = 12345 };
        var entityMetadata = CreateEntityMetadataWithoutSharding(typeof(TestHashedEntity));

        Assert.Throws<ShardNotFoundException>(() =>
            strategy.ResolveWriteShard(entityMetadata, registry, entity));
    }

    [Fact]
    public void ResolveWriteShard_WithEmptyShardKeyProperties_ThrowsShardNotFoundException()
    {
        var strategy = new HashShardingStrategy(4);
        var shards = CreateShards(4);
        var registry = new ShardRegistry(shards);
        var entity = new TestHashedEntity { CustomerId = 12345 };
        var entityMetadata = CreateEntityMetadataWithEmptyShardKeyProperties(typeof(TestHashedEntity));

        Assert.Throws<ShardNotFoundException>(() =>
            strategy.ResolveWriteShard(entityMetadata, registry, entity));
    }

    [Fact]
    public void ResolveWriteShard_WithNullKeyValue_ThrowsShardNotFoundException()
    {
        var strategy = new HashShardingStrategy(4);
        var shards = CreateShards(4);
        var registry = new ShardRegistry(shards);
        var entity = new TestHashedEntityWithNullableKey { CustomerId = null };
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId", typeof(TestHashedEntityWithNullableKey));

        Assert.Throws<ShardNotFoundException>(() =>
            strategy.ResolveWriteShard(entityMetadata, registry, entity));
    }

    [Fact]
    public void ResolveWriteShard_ThrowsForNullEntity()
    {
        var strategy = new HashShardingStrategy(4);
        var registry = new ShardRegistry();
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId");

        Assert.Throws<ArgumentNullException>(() =>
            strategy.ResolveWriteShard(entityMetadata, registry, null!));
    }

    [Fact]
    public void ResolveWriteShard_ThrowsForNullEntityMetadata()
    {
        var strategy = new HashShardingStrategy(4);
        var registry = new ShardRegistry();
        var entity = new TestHashedEntity();

        Assert.Throws<ArgumentNullException>(() =>
            strategy.ResolveWriteShard(null!, registry, entity));
    }

    [Fact]
    public void ResolveWriteShard_ThrowsForNullShardRegistry()
    {
        var strategy = new HashShardingStrategy(4);
        var entity = new TestHashedEntity();
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId");

        Assert.Throws<ArgumentNullException>(() =>
            strategy.ResolveWriteShard(entityMetadata, null!, entity));
    }

    [Fact]
    public void ResolveWriteShard_WithNoMatchingShard_ThrowsShardNotFoundException()
    {
        var strategy = new HashShardingStrategy(100);
        var shards = CreateShards(1);
        var registry = new ShardRegistry(shards);
        var entity = new TestHashedEntity { CustomerId = 12345 };
        var entityMetadata = CreateEntityMetadataWithHashSharding("CustomerId", typeof(TestHashedEntity));

        var shardIndex = strategy.ComputeShardIndex(entity.CustomerId);
        if (shardIndex != 0)
        {
            Assert.Throws<ShardNotFoundException>(() =>
                strategy.ResolveWriteShard(entityMetadata, registry, entity));
        }
    }

    private static IReadOnlyList<IShardMetadata> CreateShards(int count)
    {
        var shards = new List<IShardMetadata>();
        for (var i = 0; i < count; i++)
        {
            shards.Add(new ShardMetadataBuilder()
                .WithId($"Shard_{i}")
                .WithName($"Shard {i}")
                .WithConnectionString($"Server=localhost;Database=Shard{i}")
                .WithPriority(i)
                .Build());
        }
        return shards;
    }

    private static IEntityMetadata CreateEntityMetadataWithHashSharding(
        string shardKeyPropertyName,
        Type? entityType = null)
    {
        entityType ??= typeof(TestHashedEntity);
        return new TestHashShardingEntityMetadata(entityType, shardKeyPropertyName);
    }

    private static IEntityMetadata CreateEntityMetadataWithoutSharding(Type? entityType = null)
    {
        entityType ??= typeof(TestHashedEntity);
        return new TestNoShardingEntityMetadata(entityType);
    }

    private static IEntityMetadata CreateEntityMetadataWithEmptyShardKeyProperties(Type? entityType = null)
    {
        entityType ??= typeof(TestHashedEntity);
        return new TestEmptyShardKeyEntityMetadata(entityType);
    }

    private sealed class TestHashShardingEntityMetadata : IEntityMetadata
    {
        private readonly TestHashShardingConfiguration _shardingConfig;

        public TestHashShardingEntityMetadata(Type entityType, string shardKeyPropertyName)
        {
            ClrType = entityType;
            _shardingConfig = new TestHashShardingConfiguration(entityType, shardKeyPropertyName);
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

    private sealed class TestNoShardingEntityMetadata : IEntityMetadata
    {
        public TestNoShardingEntityMetadata(Type entityType)
        {
            ClrType = entityType;
        }

        public Type ClrType { get; }
        public string TableName => ClrType.Name;
        public string SchemaName => "dbo";
        public IPropertyMetadata? PrimaryKey => null;
        public IValidityConfiguration? Validity => null;
        public IShardingConfiguration? Sharding => null;
        public bool IsTemporal => false;
        public bool IsSharded => false;
    }

    private sealed class TestEmptyShardKeyEntityMetadata : IEntityMetadata
    {
        private readonly TestEmptyShardKeyConfiguration _shardingConfig;

        public TestEmptyShardKeyEntityMetadata(Type entityType)
        {
            ClrType = entityType;
            _shardingConfig = new TestEmptyShardKeyConfiguration();
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

    private sealed class TestHashShardingConfiguration : IShardingConfiguration
    {
        public TestHashShardingConfiguration(Type entityType, string shardKeyPropertyName)
        {
            var propInfo = entityType.GetProperty(shardKeyPropertyName);
            ShardKeyProperties = propInfo is not null
                ? new List<IPropertyMetadata> { new TestPropertyMetadata(propInfo) }
                : [];
        }

        public ShardingStrategyType StrategyType => ShardingStrategyType.Hash;
        public ShardStorageMode StorageMode => ShardStorageMode.Databases;
        public LambdaExpression? ShardKeyExpression => null;
        public IReadOnlyList<IPropertyMetadata> ShardKeyProperties { get; }
        public IShardingStrategy Strategy => new HashShardingStrategy(4);
        public bool MigrationsEnabled => false;
        public string? TableNamePattern => null;
        public DateShardInterval? DateInterval => null;
    }

    private sealed class TestEmptyShardKeyConfiguration : IShardingConfiguration
    {
        public ShardingStrategyType StrategyType => ShardingStrategyType.Hash;
        public ShardStorageMode StorageMode => ShardStorageMode.Databases;
        public LambdaExpression? ShardKeyExpression => null;
        public IReadOnlyList<IPropertyMetadata> ShardKeyProperties => [];
        public IShardingStrategy Strategy => new HashShardingStrategy(4);
        public bool MigrationsEnabled => false;
        public string? TableNamePattern => null;
        public DateShardInterval? DateInterval => null;
    }

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

    private class TestHashedEntity
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class TestHashedEntityWithNullableKey
    {
        public int Id { get; set; }
        public int? CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
