using Dtde.Core.Metadata;

namespace Dtde.Core.Tests.Metadata;

/// <summary>
/// Tests for <see cref="EntityMetadata"/> and <see cref="EntityMetadataBuilder{TEntity}"/>.
/// </summary>
public class EntityMetadataTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesMetadata()
    {
        var metadata = new EntityMetadata(
            typeof(TestEntity),
            "TestEntities",
            "dbo");

        Assert.Equal(typeof(TestEntity), metadata.ClrType);
        Assert.Equal("TestEntities", metadata.TableName);
        Assert.Equal("dbo", metadata.SchemaName);
        Assert.Null(metadata.PrimaryKey);
        Assert.Null(metadata.Validity);
        Assert.Null(metadata.Sharding);
        Assert.False(metadata.IsTemporal);
        Assert.False(metadata.IsSharded);
    }

    [Fact]
    public void Constructor_ThrowsForNullClrType()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EntityMetadata(null!, "Table", "dbo"));
    }

    [Fact]
    public void Constructor_ThrowsForNullTableName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EntityMetadata(typeof(TestEntity), null!, "dbo"));
    }

    [Fact]
    public void Constructor_ThrowsForNullSchemaName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EntityMetadata(typeof(TestEntity), "Table", null!));
    }

    [Fact]
    public void IsTemporal_ReturnsTrueWhenValidityConfigured()
    {
        var validity = ValidityConfiguration.Create<TestTemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var metadata = new EntityMetadata(
            typeof(TestTemporalEntity),
            "TestTemporalEntities",
            "dbo",
            validity: validity);

        Assert.True(metadata.IsTemporal);
    }

    [Fact]
    public void IsSharded_ReturnsTrueWhenShardingConfigured()
    {
        var sharding = ShardingConfiguration.Create<TestShardedEntity, string>(
            e => e.Region,
            Dtde.Abstractions.Metadata.ShardStorageMode.Tables,
            new Dtde.Core.Sharding.PropertyBasedShardingStrategy());

        var metadata = new EntityMetadata(
            typeof(TestShardedEntity),
            "TestShardedEntities",
            "dbo",
            sharding: sharding);

        Assert.True(metadata.IsSharded);
    }

    [Fact]
    public void Builder_DefaultsTableNameToTypeName()
    {
        var builder = new EntityMetadataBuilder<TestEntity>();

        var metadata = builder.Build();

        Assert.Equal("TestEntity", metadata.TableName);
    }

    [Fact]
    public void Builder_ToTable_SetsTableName()
    {
        var builder = new EntityMetadataBuilder<TestEntity>();

        var metadata = builder
            .ToTable("CustomTableName")
            .Build();

        Assert.Equal("CustomTableName", metadata.TableName);
    }

    [Fact]
    public void Builder_InSchema_SetsSchemaName()
    {
        var builder = new EntityMetadataBuilder<TestEntity>();

        var metadata = builder
            .InSchema("custom_schema")
            .Build();

        Assert.Equal("custom_schema", metadata.SchemaName);
    }

    [Fact]
    public void Builder_DefaultSchema_IsDbo()
    {
        var builder = new EntityMetadataBuilder<TestEntity>();

        var metadata = builder.Build();

        Assert.Equal("dbo", metadata.SchemaName);
    }

    [Fact]
    public void Builder_HasKey_SetsPrimaryKey()
    {
        var builder = new EntityMetadataBuilder<TestEntity>();

        var metadata = builder
            .HasKey(e => e.Id)
            .Build();

        Assert.NotNull(metadata.PrimaryKey);
        Assert.Equal("Id", metadata.PrimaryKey!.PropertyName);
    }

    [Fact]
    public void Builder_HasValidity_WithExpressions_SetsValidityConfiguration()
    {
        var builder = new EntityMetadataBuilder<TestTemporalEntity>();

        var metadata = builder
            .HasValidity(e => e.ValidFrom, e => e.ValidTo)
            .Build();

        Assert.NotNull(metadata.Validity);
        Assert.True(metadata.IsTemporal);
        Assert.Equal("ValidFrom", metadata.Validity!.ValidFromProperty.PropertyName);
        Assert.Equal("ValidTo", metadata.Validity!.ValidToProperty!.PropertyName);
    }

    [Fact]
    public void Builder_HasValidity_WithOnlyValidFrom_SetsOpenEndedValidity()
    {
        var builder = new EntityMetadataBuilder<TestTemporalEntity>();

        var metadata = builder
            .HasValidity(e => e.ValidFrom)
            .Build();

        Assert.NotNull(metadata.Validity);
        Assert.True(metadata.Validity!.IsOpenEnded);
        Assert.Null(metadata.Validity.ValidToProperty);
    }

    [Fact]
    public void Builder_HasTemporalValidity_WithPropertyNames_SetsValidity()
    {
        var builder = new EntityMetadataBuilder<TestTemporalEntity>();

        var metadata = builder
            .HasTemporalValidity("ValidFrom", "ValidTo")
            .Build();

        Assert.NotNull(metadata.Validity);
        Assert.Equal("ValidFrom", metadata.Validity!.ValidFromProperty.PropertyName);
        Assert.Equal("ValidTo", metadata.Validity!.ValidToProperty!.PropertyName);
    }

    [Fact]
    public void Builder_HasTemporalValidity_WithOnlyValidFrom_SetsOpenEndedValidity()
    {
        var builder = new EntityMetadataBuilder<TestTemporalEntity>();

        var metadata = builder
            .HasTemporalValidity("ValidFrom")
            .Build();

        Assert.NotNull(metadata.Validity);
        Assert.True(metadata.Validity!.IsOpenEnded);
    }

    [Fact]
    public void Builder_WithSharding_SetsShardingConfiguration()
    {
        var sharding = ShardingConfiguration.Create<TestShardedEntity, string>(
            e => e.Region,
            Dtde.Abstractions.Metadata.ShardStorageMode.Tables,
            new Dtde.Core.Sharding.PropertyBasedShardingStrategy());

        var builder = new EntityMetadataBuilder<TestShardedEntity>();

        var metadata = builder
            .WithSharding(sharding)
            .Build();

        Assert.NotNull(metadata.Sharding);
        Assert.True(metadata.IsSharded);
    }

    [Fact]
    public void Builder_FluentApi_AllowsMethodChaining()
    {
        var sharding = ShardingConfiguration.Create<TestTemporalShardedEntity, string>(
            e => e.Region,
            Dtde.Abstractions.Metadata.ShardStorageMode.Tables,
            new Dtde.Core.Sharding.PropertyBasedShardingStrategy());

        var builder = new EntityMetadataBuilder<TestTemporalShardedEntity>();

        var metadata = builder
            .ToTable("Entities")
            .InSchema("app")
            .HasKey(e => e.Id)
            .HasValidity(e => e.ValidFrom, e => e.ValidTo)
            .WithSharding(sharding)
            .Build();

        Assert.Equal("Entities", metadata.TableName);
        Assert.Equal("app", metadata.SchemaName);
        Assert.NotNull(metadata.PrimaryKey);
        Assert.NotNull(metadata.Validity);
        Assert.NotNull(metadata.Sharding);
    }

    [Fact]
    public void Builder_AutoDetectsPrimaryKey_WhenIdPropertyExists()
    {
        var builder = new EntityMetadataBuilder<TestEntity>();

        var metadata = builder.Build();

        Assert.NotNull(metadata.PrimaryKey);
        Assert.Equal("Id", metadata.PrimaryKey!.PropertyName);
    }

    [Fact]
    public void Builder_AutoDetectsPrimaryKey_WhenEntityNameIdPropertyExists()
    {
        var builder = new EntityMetadataBuilder<Order>();

        var metadata = builder.Build();

        Assert.NotNull(metadata.PrimaryKey);
        Assert.Equal("OrderId", metadata.PrimaryKey!.PropertyName);
    }

    [Fact]
    public void Builder_ExplicitKey_OverridesAutoDetection()
    {
        var builder = new EntityMetadataBuilder<TestEntity>();

        var metadata = builder
            .HasKey(e => e.Name)
            .Build();

        Assert.NotNull(metadata.PrimaryKey);
        Assert.Equal("Name", metadata.PrimaryKey!.PropertyName);
    }

    [Fact]
    public void Builder_ReturnsCorrectClrType()
    {
        var builder = new EntityMetadataBuilder<TestEntity>();

        var metadata = builder.Build();

        Assert.Equal(typeof(TestEntity), metadata.ClrType);
    }

    [Fact]
    public void Builder_HasValidity_WithNonNullableValidTo_Works()
    {
        var builder = new EntityMetadataBuilder<TestNonNullableTemporalEntity>();

        var metadata = builder
            .HasValidity(e => e.ValidFrom, e => e.ValidTo)
            .Build();

        Assert.NotNull(metadata.Validity);
        Assert.False(metadata.Validity!.IsOpenEnded);
    }

    private class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class TestTemporalEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
    }

    private class TestNonNullableTemporalEntity
    {
        public int Id { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
    }

    private class TestShardedEntity
    {
        public int Id { get; set; }
        public string Region { get; set; } = string.Empty;
    }

    private class TestTemporalShardedEntity
    {
        public int Id { get; set; }
        public string Region { get; set; } = string.Empty;
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
    }

    private class Order
    {
        public int OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
    }
}
