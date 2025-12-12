using Dtde.Core.Metadata;
using FluentAssertions;

namespace Dtde.EntityFramework.Tests.Metadata;

public class EntityMetadataBuilderTests
{
    [Fact(DisplayName = "HasTemporalValidity configures validity properties by name")]
    public void HasTemporalValidity_ConfiguresValidityProperties_ByName()
    {
        // Arrange
        var builder = new EntityMetadataBuilder<OrderEntity>();

        // Act
        builder.HasTemporalValidity(
            validFrom: nameof(OrderEntity.EffectiveDate),
            validTo: nameof(OrderEntity.ExpirationDate));
        var metadata = builder.Build();

        // Assert
        metadata.Validity.Should().NotBeNull();
        metadata.Validity!.ValidFromProperty.PropertyName.Should().Be("EffectiveDate");
        metadata.Validity!.ValidToProperty!.PropertyName.Should().Be("ExpirationDate");
    }

    [Fact(DisplayName = "HasValidity configures validity properties by expression")]
    public void HasValidity_ConfiguresValidityProperties_ByExpression()
    {
        // Arrange
        var builder = new EntityMetadataBuilder<OrderEntity>();

        // Act
        builder.HasValidity(
            validFromSelector: e => e.EffectiveDate,
            validToSelector: e => e.ExpirationDate);
        var metadata = builder.Build();

        // Assert
        metadata.Validity.Should().NotBeNull();
        metadata.Validity!.ValidFromProperty.PropertyName.Should().Be("EffectiveDate");
        metadata.Validity!.ValidToProperty!.PropertyName.Should().Be("ExpirationDate");
    }

    [Fact(DisplayName = "HasKey configures primary key property")]
    public void HasKey_ConfiguresPrimaryKeyProperty()
    {
        // Arrange
        var builder = new EntityMetadataBuilder<OrderEntity>();

        // Act
        builder.HasKey(e => e.OrderId);
        var metadata = builder.Build();

        // Assert
        metadata.PrimaryKey.Should().NotBeNull();
        metadata.PrimaryKey!.PropertyName.Should().Be("OrderId");
    }

    [Fact(DisplayName = "Build returns EntityMetadata with correct entity type")]
    public void Build_ReturnsEntityMetadata_WithCorrectEntityType()
    {
        // Arrange
        var builder = new EntityMetadataBuilder<OrderEntity>();

        // Act
        var metadata = builder.Build();

        // Assert
        metadata.ClrType.Should().Be<OrderEntity>();
    }

    [Fact(DisplayName = "Multiple configurations can be chained")]
    public void MultipleConfigurations_CanBeChained()
    {
        // Arrange
        var builder = new EntityMetadataBuilder<OrderEntity>();

        // Act
        builder
            .HasKey(e => e.OrderId)
            .HasValidity(e => e.EffectiveDate, e => e.ExpirationDate);

        var metadata = builder.Build();

        // Assert
        metadata.PrimaryKey.Should().NotBeNull();
        metadata.Validity.Should().NotBeNull();
    }
}

public class OrderEntity
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime EffectiveDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
}
