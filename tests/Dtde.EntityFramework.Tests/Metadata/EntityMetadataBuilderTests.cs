using Dtde.Core.Metadata;

namespace Dtde.EntityFramework.Tests.Metadata;

public class EntityMetadataBuilderTests
{
    [Fact(DisplayName = "HasTemporalValidity configures validity properties by name")]
    public void HasTemporalValidity_ConfiguresValidityProperties_ByName()
    {
        var builder = new EntityMetadataBuilder<OrderEntity>();

        builder.HasTemporalValidity(
            validFrom: nameof(OrderEntity.EffectiveDate),
            validTo: nameof(OrderEntity.ExpirationDate));
        var metadata = builder.Build();

        Assert.NotNull(metadata.Validity);
        Assert.Equal("EffectiveDate", metadata.Validity!.ValidFromProperty.PropertyName);
        Assert.Equal("ExpirationDate", metadata.Validity!.ValidToProperty!.PropertyName);
    }

    [Fact(DisplayName = "HasValidity configures validity properties by expression")]
    public void HasValidity_ConfiguresValidityProperties_ByExpression()
    {
        var builder = new EntityMetadataBuilder<OrderEntity>();

        builder.HasValidity(
            validFromSelector: e => e.EffectiveDate,
            validToSelector: e => e.ExpirationDate);
        var metadata = builder.Build();

        Assert.NotNull(metadata.Validity);
        Assert.Equal("EffectiveDate", metadata.Validity!.ValidFromProperty.PropertyName);
        Assert.Equal("ExpirationDate", metadata.Validity!.ValidToProperty!.PropertyName);
    }

    [Fact(DisplayName = "HasKey configures primary key property")]
    public void HasKey_ConfiguresPrimaryKeyProperty()
    {
        var builder = new EntityMetadataBuilder<OrderEntity>();

        builder.HasKey(e => e.OrderId);
        var metadata = builder.Build();

        Assert.NotNull(metadata.PrimaryKey);
        Assert.Equal("OrderId", metadata.PrimaryKey!.PropertyName);
    }

    [Fact(DisplayName = "Build returns EntityMetadata with correct entity type")]
    public void Build_ReturnsEntityMetadata_WithCorrectEntityType()
    {
        var builder = new EntityMetadataBuilder<OrderEntity>();

        var metadata = builder.Build();

        Assert.Equal(typeof(OrderEntity), metadata.ClrType);
    }

    [Fact(DisplayName = "Multiple configurations can be chained")]
    public void MultipleConfigurations_CanBeChained()
    {
        var builder = new EntityMetadataBuilder<OrderEntity>();

        builder
            .HasKey(e => e.OrderId)
            .HasValidity(e => e.EffectiveDate, e => e.ExpirationDate);

        var metadata = builder.Build();

        Assert.NotNull(metadata.PrimaryKey);
        Assert.NotNull(metadata.Validity);
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
