using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.Core.Sharding;

namespace Dtde.Core.Tests.Sharding;

/// <summary>
/// Tests for <see cref="DateRangeShardingStrategy"/>.
/// </summary>
public class DateRangeShardingStrategyTests
{
    private readonly DateRangeShardingStrategy _strategy;

    public DateRangeShardingStrategyTests()
    {
        _strategy = new DateRangeShardingStrategy();
    }

    [Fact]
    public void StrategyType_ReturnsDateRange()
    {
        Assert.Equal(ShardingStrategyType.DateRange, _strategy.StrategyType);
    }

    [Fact]
    public void ResolveShards_WithNoTemporalContextAndNoPredicates_ReturnsAllShards()
    {
        var q1Shard = CreateDateRangeShard("Q1-2024", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var q2Shard = CreateDateRangeShard("Q2-2024", new DateTime(2024, 4, 1), new DateTime(2024, 7, 1));
        var registry = new ShardRegistry([q1Shard, q2Shard]);

        var entityMetadata = CreateTemporalEntityMetadata();
        var predicates = new Dictionary<string, object?>();

        var result = _strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ResolveShards_WithTemporalContext_ReturnsMatchingShard()
    {
        var q1Shard = CreateDateRangeShard("Q1-2024", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var q2Shard = CreateDateRangeShard("Q2-2024", new DateTime(2024, 4, 1), new DateTime(2024, 7, 1));
        var registry = new ShardRegistry([q1Shard, q2Shard]);

        var entityMetadata = CreateTemporalEntityMetadata();
        var predicates = new Dictionary<string, object?>();
        var temporalContext = new DateTime(2024, 2, 15);

        var result = _strategy.ResolveShards(entityMetadata, registry, predicates, temporalContext);

        Assert.Single(result);
        Assert.Equal("Q1-2024", result[0].ShardId);
    }

    [Fact]
    public void ResolveShards_WithTemporalContextOnBoundary_ReturnsCorrectShard()
    {
        var q1Shard = CreateDateRangeShard("Q1-2024", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var q2Shard = CreateDateRangeShard("Q2-2024", new DateTime(2024, 4, 1), new DateTime(2024, 7, 1));
        var registry = new ShardRegistry([q1Shard, q2Shard]);

        var entityMetadata = CreateTemporalEntityMetadata();
        var predicates = new Dictionary<string, object?>();
        var temporalContext = new DateTime(2024, 4, 1);

        var result = _strategy.ResolveShards(entityMetadata, registry, predicates, temporalContext);

        Assert.Single(result);
        Assert.Equal("Q2-2024", result[0].ShardId);
    }

    [Fact]
    public void ResolveShards_WithShardsWithoutDateRange_IncludesThemAlways()
    {
        var q1Shard = CreateDateRangeShard("Q1-2024", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var catchAllShard = CreateShardWithoutDateRange("CatchAll");
        var registry = new ShardRegistry([q1Shard, catchAllShard]);

        var entityMetadata = CreateTemporalEntityMetadata();
        var predicates = new Dictionary<string, object?>();
        var temporalContext = new DateTime(2024, 2, 15);

        var result = _strategy.ResolveShards(entityMetadata, registry, predicates, temporalContext);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.ShardId == "CatchAll");
    }

    [Fact]
    public void ResolveShards_OrdersByPriority()
    {
        var q1Shard = CreateDateRangeShard("Q1-2024", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1), priority: 2);
        var q2Shard = CreateDateRangeShard("Q2-2024", new DateTime(2024, 4, 1), new DateTime(2024, 7, 1), priority: 1);
        var registry = new ShardRegistry([q1Shard, q2Shard]);

        var entityMetadata = CreateTemporalEntityMetadata();
        var predicates = new Dictionary<string, object?>();

        var result = _strategy.ResolveShards(entityMetadata, registry, predicates, null);

        Assert.Equal("Q2-2024", result[0].ShardId);
        Assert.Equal("Q1-2024", result[1].ShardId);
    }

    [Fact]
    public void ResolveShards_ThrowsForNullEntity()
    {
        var registry = new ShardRegistry();
        var predicates = new Dictionary<string, object?>();

        Assert.Throws<ArgumentNullException>(() =>
            _strategy.ResolveShards(null!, registry, predicates, null));
    }

    [Fact]
    public void ResolveShards_ThrowsForNullRegistry()
    {
        var entityMetadata = CreateTemporalEntityMetadata();
        var predicates = new Dictionary<string, object?>();

        Assert.Throws<ArgumentNullException>(() =>
            _strategy.ResolveShards(entityMetadata, null!, predicates, null));
    }

    [Fact]
    public void ResolveShards_ThrowsForNullPredicates()
    {
        var registry = new ShardRegistry();
        var entityMetadata = CreateTemporalEntityMetadata();

        Assert.Throws<ArgumentNullException>(() =>
            _strategy.ResolveShards(entityMetadata, registry, null!, null));
    }

    [Fact]
    public void ResolveWriteShard_WithValidDate_ReturnsMatchingShard()
    {
        var q1Shard = CreateWritableDateRangeShard("Q1-2024", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var q2Shard = CreateWritableDateRangeShard("Q2-2024", new DateTime(2024, 4, 1), new DateTime(2024, 7, 1));
        var registry = new ShardRegistry([q1Shard, q2Shard]);

        var entity = new TestTemporalEntity { ValidFrom = new DateTime(2024, 2, 15) };
        var entityMetadata = CreateTemporalEntityMetadata(typeof(TestTemporalEntity));

        var result = _strategy.ResolveWriteShard(entityMetadata, registry, entity);

        Assert.Equal("Q1-2024", result.ShardId);
    }

    [Fact]
    public void ResolveWriteShard_WithDateOnBoundary_ReturnsCorrectShard()
    {
        var q1Shard = CreateWritableDateRangeShard("Q1-2024", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var q2Shard = CreateWritableDateRangeShard("Q2-2024", new DateTime(2024, 4, 1), new DateTime(2024, 7, 1));
        var registry = new ShardRegistry([q1Shard, q2Shard]);

        var entity = new TestTemporalEntity { ValidFrom = new DateTime(2024, 4, 1) };
        var entityMetadata = CreateTemporalEntityMetadata(typeof(TestTemporalEntity));

        var result = _strategy.ResolveWriteShard(entityMetadata, registry, entity);

        Assert.Equal("Q2-2024", result.ShardId);
    }

    [Fact]
    public void ResolveWriteShard_WithNoMatchingDateRange_UsesCatchAllShard()
    {
        var q1Shard = CreateWritableDateRangeShard("Q1-2024", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var catchAllShard = CreateWritableShardWithoutDateRange("CatchAll");
        var registry = new ShardRegistry([q1Shard, catchAllShard]);

        var entity = new TestTemporalEntity { ValidFrom = new DateTime(2025, 1, 1) };
        var entityMetadata = CreateTemporalEntityMetadata(typeof(TestTemporalEntity));

        var result = _strategy.ResolveWriteShard(entityMetadata, registry, entity);

        Assert.Equal("CatchAll", result.ShardId);
    }

    [Fact]
    public void ResolveWriteShard_WithNoMatchingShardAndNoCatchAll_ThrowsShardNotFoundException()
    {
        var q1Shard = CreateWritableDateRangeShard("Q1-2024", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var registry = new ShardRegistry([q1Shard]);

        var entity = new TestTemporalEntity { ValidFrom = new DateTime(2025, 1, 1) };
        var entityMetadata = CreateTemporalEntityMetadata(typeof(TestTemporalEntity));

        Assert.Throws<ShardNotFoundException>(() =>
            _strategy.ResolveWriteShard(entityMetadata, registry, entity));
    }

    [Fact]
    public void ResolveWriteShard_WithReadOnlyShards_OnlyConsidersWritableShards()
    {
        var readOnlyShard = CreateDateRangeShard("Q1-2024-Archive", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1), isReadOnly: true);
        var writableShard = CreateWritableDateRangeShard("Q1-2024-Active", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var registry = new ShardRegistry([readOnlyShard, writableShard]);

        var entity = new TestTemporalEntity { ValidFrom = new DateTime(2024, 2, 15) };
        var entityMetadata = CreateTemporalEntityMetadata(typeof(TestTemporalEntity));

        var result = _strategy.ResolveWriteShard(entityMetadata, registry, entity);

        Assert.Equal("Q1-2024-Active", result.ShardId);
    }

    [Fact]
    public void ResolveWriteShard_ThrowsForNullEntity()
    {
        var registry = new ShardRegistry();
        var entityMetadata = CreateTemporalEntityMetadata();

        Assert.Throws<ArgumentNullException>(() =>
            _strategy.ResolveWriteShard(entityMetadata, registry, null!));
    }

    [Fact]
    public void ResolveWriteShard_ThrowsForNullEntityMetadata()
    {
        var registry = new ShardRegistry();
        var entity = new TestTemporalEntity();

        Assert.Throws<ArgumentNullException>(() =>
            _strategy.ResolveWriteShard(null!, registry, entity));
    }

    [Fact]
    public void ResolveWriteShard_ThrowsForNullShardRegistry()
    {
        var entity = new TestTemporalEntity();
        var entityMetadata = CreateTemporalEntityMetadata();

        Assert.Throws<ArgumentNullException>(() =>
            _strategy.ResolveWriteShard(entityMetadata, null!, entity));
    }

    [Fact]
    public void ResolveWriteShard_WithEntityWithoutShardKeyOrValidity_ThrowsShardNotFoundException()
    {
        var shard = CreateWritableDateRangeShard("Shard1", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var registry = new ShardRegistry([shard]);

        var entity = new TestEntityWithoutTemporal { Id = 1, Name = "Test" };
        var entityMetadata = CreateNonTemporalEntityMetadata(typeof(TestEntityWithoutTemporal));

        Assert.Throws<ShardNotFoundException>(() =>
            _strategy.ResolveWriteShard(entityMetadata, registry, entity));
    }

    [Fact]
    public void ResolveWriteShard_WithNonDateTimeShardKey_ThrowsShardNotFoundException()
    {
        var shard = CreateWritableDateRangeShard("Shard1", new DateTime(2024, 1, 1), new DateTime(2024, 4, 1));
        var registry = new ShardRegistry([shard]);

        var entity = new TestEntityWithStringKey { StringKey = "ABC" };
        var entityMetadata = CreateStringKeyEntityMetadata(typeof(TestEntityWithStringKey));

        Assert.Throws<ShardNotFoundException>(() =>
            _strategy.ResolveWriteShard(entityMetadata, registry, entity));
    }

    private static IShardMetadata CreateDateRangeShard(
        string shardId,
        DateTime start,
        DateTime end,
        int priority = 100,
        bool isReadOnly = false)
    {
        var builder = new ShardMetadataBuilder()
            .WithId(shardId)
            .WithName(shardId)
            .WithConnectionString($"Server=localhost;Database={shardId}")
            .WithDateRange(start, end)
            .WithPriority(priority);

        if (isReadOnly)
        {
            builder.AsReadOnly();
        }

        return builder.Build();
    }

    private static IShardMetadata CreateWritableDateRangeShard(
        string shardId,
        DateTime start,
        DateTime end,
        int priority = 100)
    {
        return new ShardMetadataBuilder()
            .WithId(shardId)
            .WithName(shardId)
            .WithConnectionString($"Server=localhost;Database={shardId}")
            .WithDateRange(start, end)
            .WithPriority(priority)
            .Build();
    }

    private static IShardMetadata CreateShardWithoutDateRange(string shardId)
    {
        return new ShardMetadataBuilder()
            .WithId(shardId)
            .WithName(shardId)
            .WithConnectionString($"Server=localhost;Database={shardId}")
            .Build();
    }

    private static IShardMetadata CreateWritableShardWithoutDateRange(string shardId)
    {
        return new ShardMetadataBuilder()
            .WithId(shardId)
            .WithName(shardId)
            .WithConnectionString($"Server=localhost;Database={shardId}")
            .Build();
    }

    private static IEntityMetadata CreateTemporalEntityMetadata(Type? entityType = null)
    {
        entityType ??= typeof(TestTemporalEntity);
        return new TestTemporalEntityMetadata(entityType);
    }

    private static IEntityMetadata CreateNonTemporalEntityMetadata(Type entityType)
    {
        return new TestNonTemporalEntityMetadata(entityType);
    }

    private static IEntityMetadata CreateStringKeyEntityMetadata(Type entityType)
    {
        return new TestStringKeyEntityMetadata(entityType);
    }

    private sealed class TestTemporalEntityMetadata : IEntityMetadata
    {
        private readonly TestValidityConfiguration _validityConfig;

        public TestTemporalEntityMetadata(Type entityType)
        {
            ClrType = entityType;
            _validityConfig = new TestValidityConfiguration(entityType);
        }

        public Type ClrType { get; }
        public string TableName => ClrType.Name;
        public string SchemaName => "dbo";
        public IPropertyMetadata? PrimaryKey => null;
        public IValidityConfiguration? Validity => _validityConfig;
        public IShardingConfiguration? Sharding => null;
        public bool IsTemporal => true;
        public bool IsSharded => false;
    }

    private sealed class TestNonTemporalEntityMetadata : IEntityMetadata
    {
        public TestNonTemporalEntityMetadata(Type entityType)
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

    private sealed class TestStringKeyEntityMetadata : IEntityMetadata
    {
        private readonly TestStringShardingConfiguration _shardingConfig;

        public TestStringKeyEntityMetadata(Type entityType)
        {
            ClrType = entityType;
            _shardingConfig = new TestStringShardingConfiguration(entityType);
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

    private sealed class TestValidityConfiguration : IValidityConfiguration
    {
        private readonly IPropertyMetadata _validFromProperty;

        public TestValidityConfiguration(Type entityType)
        {
            var propInfo = entityType.GetProperty(nameof(TestTemporalEntity.ValidFrom))!;
            _validFromProperty = new TestPropertyMetadata(propInfo);
        }

        public IPropertyMetadata ValidFromProperty => _validFromProperty;
        public IPropertyMetadata? ValidToProperty => null;
        public bool IsOpenEnded => true;
        public DateTime OpenEndedValue => DateTime.MaxValue;

        public System.Linq.Expressions.Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(DateTime temporalContext)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class TestStringShardingConfiguration : IShardingConfiguration
    {
        public TestStringShardingConfiguration(Type entityType)
        {
            var propInfo = entityType.GetProperty(nameof(TestEntityWithStringKey.StringKey))!;
            ShardKeyProperties = [new TestPropertyMetadata(propInfo)];
        }

        public ShardingStrategyType StrategyType => ShardingStrategyType.DateRange;
        public ShardStorageMode StorageMode => ShardStorageMode.Tables;
        public System.Linq.Expressions.LambdaExpression? ShardKeyExpression => null;
        public IReadOnlyList<IPropertyMetadata> ShardKeyProperties { get; }
        public IShardingStrategy Strategy => new DateRangeShardingStrategy();
        public bool MigrationsEnabled => false;
        public string? TableNamePattern => null;
        public DateShardInterval? DateInterval => null;
    }

    private sealed class TestPropertyMetadata : IPropertyMetadata
    {
        private readonly System.Reflection.PropertyInfo _propertyInfo;

        public TestPropertyMetadata(System.Reflection.PropertyInfo propertyInfo)
        {
            _propertyInfo = propertyInfo;
        }

        public string PropertyName => _propertyInfo.Name;
        public Type PropertyType => _propertyInfo.PropertyType;
        public string ColumnName => _propertyInfo.Name;
        public System.Reflection.PropertyInfo PropertyInfo => _propertyInfo;

        public object? GetValue(object entity) => _propertyInfo.GetValue(entity);
        public void SetValue(object entity, object? value) => _propertyInfo.SetValue(entity, value);
    }

    private class TestTemporalEntity
    {
        public int Id { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
    }

    private class TestEntityWithoutTemporal
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class TestEntityWithStringKey
    {
        public int Id { get; set; }
        public string StringKey { get; set; } = string.Empty;
    }
}
