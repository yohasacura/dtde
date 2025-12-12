using Dtde.Core.Metadata;

namespace Dtde.EntityFramework.Tests.Configuration;

public class ValidityConfigurationTests
{
    [Fact(DisplayName = "BuildPredicate creates correct predicate for point-in-time query")]
    public void BuildPredicate_CreatesCorrectPredicate_ForPointInTimeQuery()
    {
        var config = ValidityConfiguration.Create<TemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TemporalEntity>(testDate);

        var validEntity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };
        Assert.True(predicate.Compile()(validEntity));

        var futureEntity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 7, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };
        Assert.False(predicate.Compile()(futureEntity));

        var expiredEntity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 5, 31)
        };
        Assert.False(predicate.Compile()(expiredEntity));
    }

    [Fact(DisplayName = "BuildPredicate handles null ValidTo as open-ended")]
    public void BuildPredicate_HandlesNullValidTo_AsOpenEnded()
    {
        var config = ValidityConfiguration.Create<TemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TemporalEntity>(testDate);

        var openEndedEntity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = null
        };

        Assert.True(predicate.Compile()(openEndedEntity));
    }

    [Fact(DisplayName = "Create with property names builds correct configuration")]
    public void Create_WithPropertyNames_BuildsCorrectConfiguration()
    {
        var config = ValidityConfiguration.Create<TemporalEntity>("ValidFrom", "ValidTo");

        Assert.Equal("ValidFrom", config.ValidFromProperty.PropertyName);
        Assert.Equal("ValidTo", config.ValidToProperty!.PropertyName);
        Assert.False(config.IsOpenEnded);
    }

    [Fact(DisplayName = "Create with only ValidFrom creates open-ended configuration")]
    public void Create_WithOnlyValidFrom_CreatesOpenEndedConfiguration()
    {
        var config = ValidityConfiguration.Create<TemporalEntity>("ValidFrom");

        Assert.Equal("ValidFrom", config.ValidFromProperty.PropertyName);
        Assert.Null(config.ValidToProperty);
        Assert.True(config.IsOpenEnded);
    }

    [Fact(DisplayName = "Create throws for non-existent property")]
    public void Create_ThrowsForNonExistentProperty()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ValidityConfiguration.Create<TemporalEntity>("NonExistent", null));

        Assert.Contains("Property", exception.Message);
        Assert.Contains("not found", exception.Message);
    }

    [Fact(DisplayName = "Create with expressions builds correct configuration")]
    public void Create_WithExpressions_BuildsCorrectConfiguration()
    {
        var config = ValidityConfiguration.Create<TemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        Assert.Equal("ValidFrom", config.ValidFromProperty.PropertyName);
        Assert.Equal("ValidTo", config.ValidToProperty!.PropertyName);
    }

    [Fact(DisplayName = "BuildPredicate with exact boundary date includes entity")]
    public void BuildPredicate_WithExactBoundaryDate_IncludesEntity()
    {
        var config = ValidityConfiguration.Create<TemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var entity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        var predicateAtStart = config.BuildPredicate<TemporalEntity>(new DateTime(2024, 1, 1));

        Assert.True(predicateAtStart.Compile()(entity));
    }

    [Fact(DisplayName = "BuildPredicate excludes entity at exact ValidTo date")]
    public void BuildPredicate_ExcludesEntity_AtExactValidToDate()
    {
        var config = ValidityConfiguration.Create<TemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var entity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 6, 15)
        };

        var predicateAtEnd = config.BuildPredicate<TemporalEntity>(new DateTime(2024, 6, 15));

        Assert.False(predicateAtEnd.Compile()(entity));
    }
}

public class TemporalEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
