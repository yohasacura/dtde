using Dtde.Core.Metadata;
using FluentAssertions;

namespace Dtde.EntityFramework.Tests.Configuration;

public class ValidityConfigurationTests
{
    [Fact(DisplayName = "BuildPredicate creates correct predicate for point-in-time query")]
    public void BuildPredicate_CreatesCorrectPredicate_ForPointInTimeQuery()
    {
        // Arrange
        var config = ValidityConfiguration.Create<TemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TemporalEntity>(testDate);

        // Act & Assert - Entity valid at test date
        var validEntity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };
        predicate.Compile()(validEntity).Should().BeTrue();

        // Entity not yet valid
        var futureEntity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 7, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };
        predicate.Compile()(futureEntity).Should().BeFalse();

        // Entity already expired
        var expiredEntity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 5, 31)
        };
        predicate.Compile()(expiredEntity).Should().BeFalse();
    }

    [Fact(DisplayName = "BuildPredicate handles null ValidTo as open-ended")]
    public void BuildPredicate_HandlesNullValidTo_AsOpenEnded()
    {
        // Arrange
        var config = ValidityConfiguration.Create<TemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var testDate = new DateTime(2024, 6, 15);
        var predicate = config.BuildPredicate<TemporalEntity>(testDate);

        // Act - Entity with null ValidTo (currently active)
        var openEndedEntity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = null
        };

        // Assert
        predicate.Compile()(openEndedEntity).Should().BeTrue();
    }

    [Fact(DisplayName = "Create with property names builds correct configuration")]
    public void Create_WithPropertyNames_BuildsCorrectConfiguration()
    {
        // Act
        var config = ValidityConfiguration.Create<TemporalEntity>("ValidFrom", "ValidTo");

        // Assert
        config.ValidFromProperty.PropertyName.Should().Be("ValidFrom");
        config.ValidToProperty!.PropertyName.Should().Be("ValidTo");
        config.IsOpenEnded.Should().BeFalse();
    }

    [Fact(DisplayName = "Create with only ValidFrom creates open-ended configuration")]
    public void Create_WithOnlyValidFrom_CreatesOpenEndedConfiguration()
    {
        // Act
        var config = ValidityConfiguration.Create<TemporalEntity>("ValidFrom");

        // Assert
        config.ValidFromProperty.PropertyName.Should().Be("ValidFrom");
        config.ValidToProperty.Should().BeNull();
        config.IsOpenEnded.Should().BeTrue();
    }

    [Fact(DisplayName = "Create throws for non-existent property")]
    public void Create_ThrowsForNonExistentProperty()
    {
        // Act
        var act = () => ValidityConfiguration.Create<TemporalEntity>("NonExistent", null);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Property*not found*");
    }

    [Fact(DisplayName = "Create with expressions builds correct configuration")]
    public void Create_WithExpressions_BuildsCorrectConfiguration()
    {
        // Act
        var config = ValidityConfiguration.Create<TemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        // Assert
        config.ValidFromProperty.PropertyName.Should().Be("ValidFrom");
        config.ValidToProperty!.PropertyName.Should().Be("ValidTo");
    }

    [Fact(DisplayName = "BuildPredicate with exact boundary date includes entity")]
    public void BuildPredicate_WithExactBoundaryDate_IncludesEntity()
    {
        // Arrange
        var config = ValidityConfiguration.Create<TemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var entity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        // Act - Query exactly at ValidFrom
        var predicateAtStart = config.BuildPredicate<TemporalEntity>(new DateTime(2024, 1, 1));

        // Assert
        predicateAtStart.Compile()(entity).Should().BeTrue();
    }

    [Fact(DisplayName = "BuildPredicate excludes entity at exact ValidTo date")]
    public void BuildPredicate_ExcludesEntity_AtExactValidToDate()
    {
        // Arrange
        var config = ValidityConfiguration.Create<TemporalEntity>(
            e => e.ValidFrom,
            e => e.ValidTo);

        var entity = new TemporalEntity
        {
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 6, 15)
        };

        // Act - Query exactly at ValidTo (should be excluded since ValidTo > testDate is false)
        var predicateAtEnd = config.BuildPredicate<TemporalEntity>(new DateTime(2024, 6, 15));

        // Assert
        predicateAtEnd.Compile()(entity).Should().BeFalse();
    }
}

public class TemporalEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
