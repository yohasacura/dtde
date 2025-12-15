using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;

namespace Dtde.Core.Tests.Metadata;

/// <summary>
/// Tests for <see cref="MetadataRegistry"/>.
/// </summary>
public class MetadataRegistryTests
{
    [Fact]
    public void Constructor_WithShardRegistry_SetsShardRegistry()
    {
        var shardRegistry = new ShardRegistry();
        var registry = new MetadataRegistry(shardRegistry);

        Assert.Same(shardRegistry, registry.ShardRegistry);
    }

    [Fact]
    public void Constructor_WithNullShardRegistry_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MetadataRegistry(null!));
    }

    [Fact]
    public void DefaultConstructor_CreatesEmptyShardRegistry()
    {
        var registry = new MetadataRegistry();

        Assert.NotNull(registry.ShardRegistry);
        Assert.Empty(registry.ShardRegistry.GetAllShards());
    }

    [Fact]
    public void RegisterEntity_StoresMetadata()
    {
        var registry = new MetadataRegistry();
        var metadata = CreateEntityMetadata<TestEntity>();

        registry.RegisterEntity(metadata);
        var result = registry.GetEntityMetadata<TestEntity>();

        Assert.NotNull(result);
        Assert.Equal(typeof(TestEntity), result.ClrType);
    }

    [Fact]
    public void RegisterEntity_ThrowsForNullMetadata()
    {
        var registry = new MetadataRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.RegisterEntity(null!));
    }

    [Fact]
    public void RegisterEntity_OverwritesPreviousMetadata()
    {
        var registry = new MetadataRegistry();
        var metadata1 = CreateEntityMetadata<TestEntity>("Table1");
        var metadata2 = CreateEntityMetadata<TestEntity>("Table2");

        registry.RegisterEntity(metadata1);
        registry.RegisterEntity(metadata2);
        var result = registry.GetEntityMetadata<TestEntity>();

        Assert.NotNull(result);
        Assert.Equal("Table2", result.TableName);
    }

    [Fact]
    public void GetEntityMetadata_Generic_ReturnsCorrectMetadata()
    {
        var registry = new MetadataRegistry();
        var metadata = CreateEntityMetadata<TestEntity>();
        registry.RegisterEntity(metadata);

        var result = registry.GetEntityMetadata<TestEntity>();

        Assert.NotNull(result);
        Assert.Equal(typeof(TestEntity), result.ClrType);
    }

    [Fact]
    public void GetEntityMetadata_Generic_ReturnsNullForUnregisteredEntity()
    {
        var registry = new MetadataRegistry();

        var result = registry.GetEntityMetadata<TestEntity>();

        Assert.Null(result);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA2263:Prefer generic overload when type is known", Justification = "Testing Type-based overload specifically")]
    public void GetEntityMetadata_ByType_ReturnsCorrectMetadata()
    {
        var registry = new MetadataRegistry();
        var metadata = CreateEntityMetadata<TestEntity>();
        registry.RegisterEntity(metadata);

        var result = registry.GetEntityMetadata(typeof(TestEntity));

        Assert.NotNull(result);
        Assert.Equal(typeof(TestEntity), result.ClrType);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA2263:Prefer generic overload when type is known", Justification = "Testing Type-based overload specifically")]
    public void GetEntityMetadata_ByType_ThrowsForNullType()
    {
        var registry = new MetadataRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.GetEntityMetadata(null!));
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA2263:Prefer generic overload when type is known", Justification = "Testing Type-based overload specifically")]
    public void GetEntityMetadata_ByType_ReturnsNullForUnregisteredEntity()
    {
        var registry = new MetadataRegistry();

        var result = registry.GetEntityMetadata(typeof(TestEntity));

        Assert.Null(result);
    }

    [Fact]
    public void GetAllEntityMetadata_ReturnsAllRegisteredEntities()
    {
        var registry = new MetadataRegistry();
        registry.RegisterEntity(CreateEntityMetadata<TestEntity>());
        registry.RegisterEntity(CreateEntityMetadata<AnotherTestEntity>());

        var result = registry.GetAllEntityMetadata();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetAllEntityMetadata_ReturnsEmptyListWhenNoEntitiesRegistered()
    {
        var registry = new MetadataRegistry();

        var result = registry.GetAllEntityMetadata();

        Assert.Empty(result);
    }

    [Fact]
    public void RegisterRelation_StoresRelationForBothEntities()
    {
        var registry = new MetadataRegistry();
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var childMetadata = CreateEntityMetadata<ChildEntity>();
        registry.RegisterEntity(parentMetadata);
        registry.RegisterEntity(childMetadata);

        var relation = CreateRelation(parentMetadata, childMetadata);
        registry.RegisterRelation(relation);

        var parentRelations = registry.GetRelations(typeof(ParentEntity));
        var childRelations = registry.GetRelations(typeof(ChildEntity));

        Assert.Single(parentRelations);
        Assert.Single(childRelations);
    }

    [Fact]
    public void RegisterRelation_ThrowsForNullRelation()
    {
        var registry = new MetadataRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.RegisterRelation(null!));
    }

    [Fact]
    public void GetRelations_ReturnsEmptyListForUnregisteredEntity()
    {
        var registry = new MetadataRegistry();

        var result = registry.GetRelations(typeof(TestEntity));

        Assert.Empty(result);
    }

    [Fact]
    public void GetRelations_ThrowsForNullEntityType()
    {
        var registry = new MetadataRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.GetRelations(null!));
    }

    [Fact]
    public void GetRelations_ReturnsMultipleRelationsForEntity()
    {
        var registry = new MetadataRegistry();
        var parentMetadata = CreateEntityMetadata<ParentEntity>();
        var child1Metadata = CreateEntityMetadata<ChildEntity>();
        var child2Metadata = CreateEntityMetadata<AnotherTestEntity>();
        registry.RegisterEntity(parentMetadata);
        registry.RegisterEntity(child1Metadata);
        registry.RegisterEntity(child2Metadata);

        registry.RegisterRelation(CreateRelation(parentMetadata, child1Metadata));
        registry.RegisterRelation(CreateRelation(parentMetadata, child2Metadata));

        var relations = registry.GetRelations(typeof(ParentEntity));

        Assert.Equal(2, relations.Count);
    }

    [Fact]
    public void Validate_ReturnsSuccessForValidConfiguration()
    {
        var registry = new MetadataRegistry();
        var metadata = CreateValidEntityMetadata<TestEntity>();
        registry.RegisterEntity(metadata);

        var result = registry.Validate();

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_ReturnsErrorForMissingPrimaryKey()
    {
        var registry = new MetadataRegistry();
        var metadata = CreateEntityMetadataWithoutPrimaryKey<TestEntity>();
        registry.RegisterEntity(metadata);

        var result = registry.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("primary key"));
    }

    [Fact]
    public void Validate_ReturnsErrorForTemporalEntityWithoutValidFrom()
    {
        var registry = new MetadataRegistry();
        var metadata = CreateTemporalEntityMetadataWithoutValidFrom<TestEntity>();
        registry.RegisterEntity(metadata);

        var result = registry.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ValidFrom"));
    }

    [Fact]
    public void Validate_ReturnsErrorForShardedEntityWithoutShardKey()
    {
        var registry = new MetadataRegistry();
        var metadata = CreateShardedEntityMetadataWithoutShardKey<TestEntity>();
        registry.RegisterEntity(metadata);

        var result = registry.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("shard key"));
    }

    [Fact]
    public void Validate_ReturnsWarningForOverlappingDateShards()
    {
        var shards = new[]
        {
            new ShardMetadataBuilder()
                .WithId("Shard1")
                .WithName("Shard 1")
                .WithConnectionString("cs1")
                .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2024, 6, 1))
                .Build(),
            new ShardMetadataBuilder()
                .WithId("Shard2")
                .WithName("Shard 2")
                .WithConnectionString("cs2")
                .WithDateRange(new DateTime(2024, 5, 1), new DateTime(2024, 12, 1))
                .Build()
        };
        var shardRegistry = new ShardRegistry(shards);
        var registry = new MetadataRegistry(shardRegistry);

        var result = registry.Validate();

        Assert.Contains(result.Warnings, w => w.Contains("overlapping"));
    }

    [Fact]
    public void Validate_ReturnsErrorForTemporalRelationWithNonTemporalParent()
    {
        var registry = new MetadataRegistry();
        var parentMetadata = CreateNonTemporalEntityMetadata<ParentEntity>();
        var childMetadata = CreateTemporalEntityMetadata<ChildEntity>();
        registry.RegisterEntity(parentMetadata);
        registry.RegisterEntity(childMetadata);

        var relation = CreateRelationWithTemporalContainment(parentMetadata, childMetadata);
        registry.RegisterRelation(relation);

        var result = registry.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("temporal containment") && e.Contains("parent"));
    }

    [Fact]
    public void Validate_ReturnsErrorForTemporalRelationWithNonTemporalChild()
    {
        var registry = new MetadataRegistry();
        var parentMetadata = CreateTemporalEntityMetadata<ParentEntity>();
        var childMetadata = CreateNonTemporalEntityMetadata<ChildEntity>();
        registry.RegisterEntity(parentMetadata);
        registry.RegisterEntity(childMetadata);

        var relation = CreateRelationWithTemporalContainment(parentMetadata, childMetadata);
        registry.RegisterRelation(relation);

        var result = registry.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("temporal containment") && e.Contains("child"));
    }

    private static IEntityMetadata CreateEntityMetadata<T>(string tableName = null!) where T : class
    {
        return new TestEntityMetadata(typeof(T), tableName ?? typeof(T).Name);
    }

    private static IEntityMetadata CreateValidEntityMetadata<T>() where T : class
    {
        return new ValidEntityMetadata(typeof(T));
    }

    private static IEntityMetadata CreateEntityMetadataWithoutPrimaryKey<T>() where T : class
    {
        return new NoPrimaryKeyEntityMetadata(typeof(T));
    }

    private static IEntityMetadata CreateTemporalEntityMetadataWithoutValidFrom<T>() where T : class
    {
        return new InvalidTemporalEntityMetadata(typeof(T));
    }

    private static IEntityMetadata CreateShardedEntityMetadataWithoutShardKey<T>() where T : class
    {
        return new InvalidShardedEntityMetadata(typeof(T));
    }

    private static IEntityMetadata CreateTemporalEntityMetadata<T>() where T : class
    {
        return new TemporalEntityMetadata(typeof(T));
    }

    private static IEntityMetadata CreateNonTemporalEntityMetadata<T>() where T : class
    {
        return new NonTemporalEntityMetadata(typeof(T));
    }

    private static IRelationMetadata CreateRelation(IEntityMetadata parent, IEntityMetadata child)
    {
        return new TestRelationMetadata(parent, child, TemporalContainmentRule.None);
    }

    private static IRelationMetadata CreateRelationWithTemporalContainment(IEntityMetadata parent, IEntityMetadata child)
    {
        return new TestRelationMetadata(parent, child, TemporalContainmentRule.ChildWithinParent);
    }

    private sealed class TestEntityMetadata : IEntityMetadata
    {
        public TestEntityMetadata(Type clrType, string tableName)
        {
            ClrType = clrType;
            TableName = tableName;
        }

        public Type ClrType { get; }
        public string TableName { get; }
        public string SchemaName => "dbo";
        public IPropertyMetadata? PrimaryKey => null;
        public IValidityConfiguration? Validity => null;
        public IShardingConfiguration? Sharding => null;
        public bool IsTemporal => false;
        public bool IsSharded => false;
    }

    private sealed class ValidEntityMetadata : IEntityMetadata
    {
        public ValidEntityMetadata(Type clrType)
        {
            ClrType = clrType;
            PrimaryKey = new TestPropertyMetadata("Id", typeof(int));
        }

        public Type ClrType { get; }
        public string TableName => ClrType.Name;
        public string SchemaName => "dbo";
        public IPropertyMetadata? PrimaryKey { get; }
        public IValidityConfiguration? Validity => null;
        public IShardingConfiguration? Sharding => null;
        public bool IsTemporal => false;
        public bool IsSharded => false;
    }

    private sealed class NoPrimaryKeyEntityMetadata : IEntityMetadata
    {
        public NoPrimaryKeyEntityMetadata(Type clrType)
        {
            ClrType = clrType;
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

    private sealed class InvalidTemporalEntityMetadata : IEntityMetadata
    {
        public InvalidTemporalEntityMetadata(Type clrType)
        {
            ClrType = clrType;
            PrimaryKey = new TestPropertyMetadata("Id", typeof(int));
            Validity = new InvalidValidityConfiguration();
        }

        public Type ClrType { get; }
        public string TableName => ClrType.Name;
        public string SchemaName => "dbo";
        public IPropertyMetadata? PrimaryKey { get; }
        public IValidityConfiguration? Validity { get; }
        public IShardingConfiguration? Sharding => null;
        public bool IsTemporal => true;
        public bool IsSharded => false;
    }

    private sealed class InvalidShardedEntityMetadata : IEntityMetadata
    {
        public InvalidShardedEntityMetadata(Type clrType)
        {
            ClrType = clrType;
            PrimaryKey = new TestPropertyMetadata("Id", typeof(int));
            Sharding = new InvalidShardingConfiguration();
        }

        public Type ClrType { get; }
        public string TableName => ClrType.Name;
        public string SchemaName => "dbo";
        public IPropertyMetadata? PrimaryKey { get; }
        public IValidityConfiguration? Validity => null;
        public IShardingConfiguration? Sharding { get; }
        public bool IsTemporal => false;
        public bool IsSharded => true;
    }

    private sealed class TemporalEntityMetadata : IEntityMetadata
    {
        public TemporalEntityMetadata(Type clrType)
        {
            ClrType = clrType;
            PrimaryKey = new TestPropertyMetadata("Id", typeof(int));
            Validity = new ValidValidityConfiguration();
        }

        public Type ClrType { get; }
        public string TableName => ClrType.Name;
        public string SchemaName => "dbo";
        public IPropertyMetadata? PrimaryKey { get; }
        public IValidityConfiguration? Validity { get; }
        public IShardingConfiguration? Sharding => null;
        public bool IsTemporal => true;
        public bool IsSharded => false;
    }

    private sealed class NonTemporalEntityMetadata : IEntityMetadata
    {
        public NonTemporalEntityMetadata(Type clrType)
        {
            ClrType = clrType;
            PrimaryKey = new TestPropertyMetadata("Id", typeof(int));
        }

        public Type ClrType { get; }
        public string TableName => ClrType.Name;
        public string SchemaName => "dbo";
        public IPropertyMetadata? PrimaryKey { get; }
        public IValidityConfiguration? Validity => null;
        public IShardingConfiguration? Sharding => null;
        public bool IsTemporal => false;
        public bool IsSharded => false;
    }

    private sealed class InvalidValidityConfiguration : IValidityConfiguration
    {
        public IPropertyMetadata ValidFromProperty => null!;
        public IPropertyMetadata? ValidToProperty => null;
        public bool IsOpenEnded => true;
        public DateTime OpenEndedValue => DateTime.MaxValue;
        public System.Linq.Expressions.Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(DateTime temporalContext) => throw new NotImplementedException();
    }

    private sealed class ValidValidityConfiguration : IValidityConfiguration
    {
        public IPropertyMetadata ValidFromProperty { get; } = new TestPropertyMetadata("ValidFrom", typeof(DateTime));
        public IPropertyMetadata? ValidToProperty => null;
        public bool IsOpenEnded => true;
        public DateTime OpenEndedValue => DateTime.MaxValue;
        public System.Linq.Expressions.Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(DateTime temporalContext) => throw new NotImplementedException();
    }

    private sealed class InvalidShardingConfiguration : IShardingConfiguration
    {
        public ShardingStrategyType StrategyType => ShardingStrategyType.PropertyValue;
        public ShardStorageMode StorageMode => ShardStorageMode.Tables;
        public System.Linq.Expressions.LambdaExpression? ShardKeyExpression => null;
        public IReadOnlyList<IPropertyMetadata> ShardKeyProperties => [];
        public IShardingStrategy Strategy => null!;
        public bool MigrationsEnabled => false;
        public string? TableNamePattern => null;
        public DateShardInterval? DateInterval => null;
    }

    private sealed class TestPropertyMetadata : IPropertyMetadata
    {
        public TestPropertyMetadata(string name, Type type)
        {
            PropertyName = name;
            PropertyType = type;
            ColumnName = name;
        }

        public string PropertyName { get; }
        public Type PropertyType { get; }
        public string ColumnName { get; }
        public System.Reflection.PropertyInfo PropertyInfo => null!;
        public object? GetValue(object entity) => throw new NotImplementedException();
        public void SetValue(object entity, object? value) => throw new NotImplementedException();
    }

    private sealed class TestRelationMetadata : IRelationMetadata
    {
        public TestRelationMetadata(IEntityMetadata parent, IEntityMetadata child, TemporalContainmentRule containment)
        {
            ParentEntity = parent;
            ChildEntity = child;
            ContainmentRule = containment;
            ParentKey = new TestPropertyMetadata("Id", typeof(int));
            ChildForeignKey = new TestPropertyMetadata("ParentId", typeof(int));
        }

        public IEntityMetadata ParentEntity { get; }
        public IEntityMetadata ChildEntity { get; }
        public RelationType RelationType => RelationType.OneToMany;
        public IPropertyMetadata ParentKey { get; }
        public IPropertyMetadata ChildForeignKey { get; }
        public TemporalContainmentRule ContainmentRule { get; }
    }

    private class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class AnotherTestEntity
    {
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    private class ParentEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class ChildEntity
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
