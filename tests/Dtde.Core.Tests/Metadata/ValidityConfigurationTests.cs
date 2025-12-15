using Dtde.Core.Metadata;

namespace Dtde.Core.Tests.Metadata;

/// <summary>
/// Tests for <see cref="ValidityConfiguration"/>.
/// </summary>
public class ValidityConfigurationTests
{
    [Fact]
    public void Constructor_WithValidProperties_CreatesConfiguration()
    {
        var validFromProp = PropertyMetadata.FromExpression<TestEntity, DateTime>(e => e.ValidFrom);
        var validToProp = PropertyMetadata.FromExpression<TestEntity, DateTime?>(e => e.ValidTo);

        var config = new ValidityConfiguration(validFromProp, validToProp);

        Assert.Same(validFromProp, config.ValidFromProperty);
        Assert.Same(validToProp, config.ValidToProperty);
        Assert.False(config.IsOpenEnded);
    }

    [Fact]
    public void Constructor_ThrowsForNullValidFromProperty()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ValidityConfiguration(null!));
    }

    [Fact]
    public void Constructor_WithOnlyValidFrom_IsOpenEnded()
    {
        var validFromProp = PropertyMetadata.FromExpression<TestEntity, DateTime>(e => e.ValidFrom);

        var config = new ValidityConfiguration(validFromProp);

        Assert.True(config.IsOpenEnded);
        Assert.Null(config.ValidToProperty);
    }

    [Fact]
    public void OpenEndedValue_DefaultsToMaxValue()
    {
        var validFromProp = PropertyMetadata.FromExpression<TestEntity, DateTime>(e => e.ValidFrom);
        var config = new ValidityConfiguration(validFromProp);

        Assert.Equal(DateTime.MaxValue, config.OpenEndedValue);
    }

    [Fact]
    public void OpenEndedValue_CanBeCustomized()
    {
        var validFromProp = PropertyMetadata.FromExpression<TestEntity, DateTime>(e => e.ValidFrom);
        var customEndDate = new DateTime(9999, 12, 31);

        var config = new ValidityConfiguration(validFromProp)
        {
            OpenEndedValue = customEndDate
        };

        Assert.Equal(customEndDate, config.OpenEndedValue);
    }

    [Fact]
    public void Create_WithExpressions_CreatesValidConfiguration()
    {
        var config = ValidityConfiguration.Create<TestEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        Assert.Equal("ValidFrom", config.ValidFromProperty.PropertyName);
        Assert.Equal("ValidTo", config.ValidToProperty!.PropertyName);
    }

    [Fact]
    public void Create_WithExpressions_NoValidTo_IsOpenEnded()
    {
        var config = ValidityConfiguration.Create<TestEntity>(
            e => e.ValidFrom);

        Assert.True(config.IsOpenEnded);
    }

    [Fact]
    public void Create_WithNonNullableValidTo_Works()
    {
        var config = ValidityConfiguration.Create<TestEntityNonNullableValidTo>(
            e => e.ValidFrom,
            e => e.ValidTo);

        Assert.False(config.IsOpenEnded);
        Assert.Equal(typeof(DateTime), config.ValidToProperty!.PropertyType);
    }

    [Fact]
    public void Create_WithPropertyNames_CreatesConfiguration()
    {
        var config = ValidityConfiguration.Create<TestEntity>("ValidFrom", "ValidTo");

        Assert.Equal("ValidFrom", config.ValidFromProperty.PropertyName);
        Assert.Equal("ValidTo", config.ValidToProperty!.PropertyName);
    }

    [Fact]
    public void Create_WithPropertyNames_NoValidTo_IsOpenEnded()
    {
        var config = ValidityConfiguration.Create<TestEntity>("ValidFrom", null);

        Assert.True(config.IsOpenEnded);
    }

    [Fact]
    public void Create_WithPropertyNames_EmptyValidTo_IsOpenEnded()
    {
        var config = ValidityConfiguration.Create<TestEntity>("ValidFrom", "");

        Assert.True(config.IsOpenEnded);
    }

    [Fact]
    public void Create_WithInvalidValidFromProperty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ValidityConfiguration.Create<TestEntity>("NonExistent", null));
    }

    [Fact]
    public void Create_WithInvalidValidToProperty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ValidityConfiguration.Create<TestEntity>("ValidFrom", "NonExistent"));
    }

    [Fact]
    public void Constructor_WithNonDateTimeValidFrom_ThrowsArgumentException()
    {
        var invalidProp = PropertyMetadata.FromExpression<TestEntity, string>(e => e.Name);

        Assert.Throws<ArgumentException>(() =>
            new ValidityConfiguration(invalidProp));
    }

    [Fact]
    public void Constructor_WithNonDateTimeValidTo_ThrowsArgumentException()
    {
        var validFromProp = PropertyMetadata.FromExpression<TestEntity, DateTime>(e => e.ValidFrom);
        var invalidValidTo = PropertyMetadata.FromExpression<TestEntity, string>(e => e.Name);

        Assert.Throws<ArgumentException>(() =>
            new ValidityConfiguration(validFromProp, invalidValidTo));
    }

    [Fact]
    public void BuildPredicate_FiltersCorrectly_ForValidEntity()
    {
        var config = ValidityConfiguration.Create<TestEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TestEntity>(testDate);

        var validEntity = new TestEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        Assert.True(predicate.Compile()(validEntity));
    }

    [Fact]
    public void BuildPredicate_FiltersCorrectly_ForExpiredEntity()
    {
        var config = ValidityConfiguration.Create<TestEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TestEntity>(testDate);

        var expiredEntity = new TestEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 5, 31)
        };

        Assert.False(predicate.Compile()(expiredEntity));
    }

    [Fact]
    public void BuildPredicate_FiltersCorrectly_ForFutureEntity()
    {
        var config = ValidityConfiguration.Create<TestEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TestEntity>(testDate);

        var futureEntity = new TestEntity
        {
            ValidFrom = new DateTime(2024, 7, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        Assert.False(predicate.Compile()(futureEntity));
    }

    [Fact]
    public void BuildPredicate_HandlesNullValidTo_AsOpenEnded()
    {
        var config = ValidityConfiguration.Create<TestEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TestEntity>(testDate);

        var openEndedEntity = new TestEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = null
        };

        Assert.True(predicate.Compile()(openEndedEntity));
    }

    [Fact]
    public void BuildPredicate_IncludesEntity_OnExactValidFromDate()
    {
        var config = ValidityConfiguration.Create<TestEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TestEntity>(testDate);

        var entity = new TestEntity
        {
            ValidFrom = new DateTime(2024, 6, 15),
            ValidTo = new DateTime(2024, 12, 31)
        };

        Assert.True(predicate.Compile()(entity));
    }

    [Fact]
    public void BuildPredicate_ExcludesEntity_OnExactValidToDate()
    {
        var config = ValidityConfiguration.Create<TestEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TestEntity>(testDate);

        var entity = new TestEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 6, 15)
        };

        Assert.False(predicate.Compile()(entity));
    }

    [Fact]
    public void BuildPredicate_WithNonNullableValidTo_Works()
    {
        var config = ValidityConfiguration.Create<TestEntityNonNullableValidTo>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TestEntityNonNullableValidTo>(testDate);

        var validEntity = new TestEntityNonNullableValidTo
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        Assert.True(predicate.Compile()(validEntity));
    }

    [Fact]
    public void BuildPredicate_OpenEnded_ChecksOnlyValidFrom()
    {
        var config = ValidityConfiguration.Create<TestEntity>("ValidFrom", null);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TestEntity>(testDate);

        var entity = new TestEntity
        {
            ValidFrom = new DateTime(2024, 1, 1)
        };

        Assert.True(predicate.Compile()(entity));
    }

    [Fact]
    public void BuildPredicate_OpenEnded_ExcludesFutureEntities()
    {
        var config = ValidityConfiguration.Create<TestEntity>("ValidFrom", null);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TestEntity>(testDate);

        var futureEntity = new TestEntity
        {
            ValidFrom = new DateTime(2024, 7, 1)
        };

        Assert.False(predicate.Compile()(futureEntity));
    }

    [Fact]
    public void BuildPredicate_WithMinDateTime_Works()
    {
        var config = ValidityConfiguration.Create<TestEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = DateTime.MinValue;
        var predicate = config.BuildPredicate<TestEntity>(testDate);

        var entity = new TestEntity
        {
            ValidFrom = DateTime.MinValue,
            ValidTo = DateTime.MaxValue
        };

        Assert.True(predicate.Compile()(entity));
    }

    [Fact]
    public void BuildPredicate_WithMaxDateTime_Works()
    {
        var config = ValidityConfiguration.Create<TestEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = DateTime.MaxValue.AddTicks(-1);
        var predicate = config.BuildPredicate<TestEntity>(testDate);

        var entity = new TestEntity
        {
            ValidFrom = DateTime.MinValue,
            ValidTo = null
        };

        Assert.True(predicate.Compile()(entity));
    }

    private class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
    }

    private class TestEntityNonNullableValidTo
    {
        public int Id { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
    }
}
