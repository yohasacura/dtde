using Dtde.Core.Metadata;

namespace Dtde.Core.Tests.Metadata;

/// <summary>
/// Tests for <see cref="PropertyMetadata"/>.
/// </summary>
public class PropertyMetadataTests
{
    [Fact]
    public void Constructor_WithValidPropertyInfo_CreatesMetadata()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;

        var metadata = new PropertyMetadata(propertyInfo);

        Assert.Equal("Name", metadata.PropertyName);
        Assert.Equal(typeof(string), metadata.PropertyType);
        Assert.Equal("Name", metadata.ColumnName);
        Assert.Same(propertyInfo, metadata.PropertyInfo);
    }

    [Fact]
    public void Constructor_WithNullPropertyInfo_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new PropertyMetadata(null!));
    }

    [Fact]
    public void Constructor_WithCustomColumnName_UsesCustomColumnName()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;

        var metadata = new PropertyMetadata(propertyInfo, "entity_name");

        Assert.Equal("Name", metadata.PropertyName);
        Assert.Equal("entity_name", metadata.ColumnName);
    }

    [Fact]
    public void FromExpression_WithValidExpression_CreatesMetadata()
    {
        var metadata = PropertyMetadata.FromExpression<TestEntity, string>(e => e.Name);

        Assert.Equal("Name", metadata.PropertyName);
        Assert.Equal(typeof(string), metadata.PropertyType);
    }

    [Fact]
    public void FromExpression_WithNullExpression_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PropertyMetadata.FromExpression<TestEntity, string>(null!));
    }

    [Fact]
    public void FromExpression_WithCustomColumnName_UsesCustomColumnName()
    {
        var metadata = PropertyMetadata.FromExpression<TestEntity, string>(e => e.Name, "custom_column");

        Assert.Equal("Name", metadata.PropertyName);
        Assert.Equal("custom_column", metadata.ColumnName);
    }

    [Fact]
    public void FromExpression_WithValueTypeProperty_CreatesMetadata()
    {
        var metadata = PropertyMetadata.FromExpression<TestEntity, int>(e => e.Id);

        Assert.Equal("Id", metadata.PropertyName);
        Assert.Equal(typeof(int), metadata.PropertyType);
    }

    [Fact]
    public void FromExpression_WithNullableProperty_CreatesMetadata()
    {
        var metadata = PropertyMetadata.FromExpression<TestEntity, DateTime?>(e => e.NullableDate);

        Assert.Equal("NullableDate", metadata.PropertyName);
        Assert.Equal(typeof(DateTime?), metadata.PropertyType);
    }

    [Fact]
    public void FromExpression_WithInvalidExpression_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            PropertyMetadata.FromExpression<TestEntity, int>(e => e.Id + 1));
    }

    [Fact]
    public void GetValue_ReturnsPropertyValue()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity { Name = "Test Value" };

        var result = metadata.GetValue(entity);

        Assert.Equal("Test Value", result);
    }

    [Fact]
    public void GetValue_WithNullEntity_ThrowsArgumentNullException()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;
        var metadata = new PropertyMetadata(propertyInfo);

        Assert.Throws<ArgumentNullException>(() => metadata.GetValue(null!));
    }

    [Fact]
    public void GetValue_WithNullPropertyValue_ReturnsNull()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity { Name = null! };

        var result = metadata.GetValue(entity);

        Assert.Null(result);
    }

    [Fact]
    public void GetValue_WithValueTypeProperty_ReturnsValue()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Id))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity { Id = 42 };

        var result = metadata.GetValue(entity);

        Assert.Equal(42, result);
    }

    [Fact]
    public void GetValue_WithNullableNullValue_ReturnsNull()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.NullableDate))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity { NullableDate = null };

        var result = metadata.GetValue(entity);

        Assert.Null(result);
    }

    [Fact]
    public void GetValue_WithNullableNonNullValue_ReturnsValue()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.NullableDate))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var expectedDate = new DateTime(2024, 6, 15);
        var entity = new TestEntity { NullableDate = expectedDate };

        var result = metadata.GetValue(entity);

        Assert.Equal(expectedDate, result);
    }

    [Fact]
    public void SetValue_SetsPropertyValue()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity();

        metadata.SetValue(entity, "New Value");

        Assert.Equal("New Value", entity.Name);
    }

    [Fact]
    public void SetValue_WithNullEntity_ThrowsArgumentNullException()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;
        var metadata = new PropertyMetadata(propertyInfo);

        Assert.Throws<ArgumentNullException>(() => metadata.SetValue(null!, "Value"));
    }

    [Fact]
    public void SetValue_WithNullValue_SetsNullValue()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity { Name = "Initial" };

        metadata.SetValue(entity, null);

        Assert.Null(entity.Name);
    }

    [Fact]
    public void SetValue_WithValueTypeProperty_SetsValue()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Id))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity();

        metadata.SetValue(entity, 99);

        Assert.Equal(99, entity.Id);
    }

    [Fact]
    public void SetValue_WithNullableProperty_SetsValue()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.NullableDate))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity();
        var expectedDate = new DateTime(2024, 6, 15);

        metadata.SetValue(entity, expectedDate);

        Assert.Equal(expectedDate, entity.NullableDate);
    }

    [Fact]
    public void SetValue_WithNullableProperty_SetsNullValue()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.NullableDate))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity { NullableDate = DateTime.Now };

        metadata.SetValue(entity, null);

        Assert.Null(entity.NullableDate);
    }

    [Fact]
    public void SetValue_OnReadOnlyProperty_ThrowsInvalidOperationException()
    {
        var propertyInfo = typeof(TestEntityWithReadOnly).GetProperty(nameof(TestEntityWithReadOnly.ReadOnlyProp))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntityWithReadOnly();

        Assert.Throws<InvalidOperationException>(() => metadata.SetValue(entity, "Value"));
    }

    [Fact]
    public void GetValue_IsFastDueToCompiledGetter()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity { Name = "Test" };

        for (var i = 0; i < 10000; i++)
        {
            _ = metadata.GetValue(entity);
        }

        Assert.True(true);
    }

    [Fact]
    public void SetValue_IsFastDueToCompiledSetter()
    {
        var propertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;
        var metadata = new PropertyMetadata(propertyInfo);
        var entity = new TestEntity();

        for (var i = 0; i < 10000; i++)
        {
            metadata.SetValue(entity, $"Value{i}");
        }

        Assert.True(true);
    }

    [Fact]
    public void FromExpression_WithBoxedConversion_CreatesMetadata()
    {
        var metadata = PropertyMetadata.FromExpression<TestEntity, object>(e => e.Id);

        Assert.Equal("Id", metadata.PropertyName);
        Assert.Equal(typeof(int), metadata.PropertyType);
    }

    [Fact]
    public void PropertyInfo_IsSetCorrectly()
    {
        var expectedPropertyInfo = typeof(TestEntity).GetProperty(nameof(TestEntity.Name))!;
        var metadata = new PropertyMetadata(expectedPropertyInfo);

        Assert.Same(expectedPropertyInfo, metadata.PropertyInfo);
    }

    private class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime? NullableDate { get; set; }
    }

    private class TestEntityWithReadOnly
    {
        public string ReadOnlyProp { get; } = "ReadOnly";
    }
}
