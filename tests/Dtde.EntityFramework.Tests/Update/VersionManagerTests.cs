using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.EntityFramework.Update;
using FluentAssertions;
using System.Globalization;

namespace Dtde.EntityFramework.Tests.Update;

public class VersionManagerTests
{
    private readonly MetadataRegistry _registry;

    public VersionManagerTests()
    {
        _registry = new MetadataRegistry();
        var builder = new EntityMetadataBuilder<TestContract>();
        builder.HasTemporalValidity(nameof(TestContract.ValidFrom), nameof(TestContract.ValidTo));
        _registry.RegisterEntity(builder.Build());
    }

    [Fact(DisplayName = "CreateVersion creates a cloned entity with new ValidFrom date")]
    public void CreateVersion_ClonesEntity_WithNewValidFromDate()
    {
        // Arrange
        var versionManager = new VersionManager(_registry);
        var original = new TestContract
        {
            Id = 1,
            ContractNumber = "C001",
            CustomerName = "Acme Corp",
            Amount = 10000m,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = null
        };
        var newEffectiveDate = new DateTime(2024, 6, 1);

        // Act
        var newVersion = versionManager.CreateVersion(original, newEffectiveDate);

        // Assert
        newVersion.Should().NotBeSameAs(original);
        newVersion.ContractNumber.Should().Be("C001");
        newVersion.CustomerName.Should().Be("Acme Corp");
        newVersion.Amount.Should().Be(10000m);
        newVersion.ValidFrom.Should().Be(newEffectiveDate);
        newVersion.ValidTo.Should().BeNull();
    }

    [Fact(DisplayName = "TerminateVersion sets ValidTo to termination date")]
    public void TerminateVersion_SetsValidTo_ToTerminationDate()
    {
        // Arrange
        var versionManager = new VersionManager(_registry);
        var entity = new TestContract
        {
            Id = 1,
            ContractNumber = "C001",
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = null
        };
        var terminationDate = new DateTime(2024, 12, 31);

        // Act
        versionManager.TerminateVersion(entity, terminationDate);

        // Assert
        entity.ValidTo.Should().Be(terminationDate);
    }

    [Fact(DisplayName = "TerminateVersion throws when termination date is before ValidFrom")]
    public void TerminateVersion_ThrowsArgumentException_WhenTerminationBeforeValidFrom()
    {
        // Arrange
        var versionManager = new VersionManager(_registry);
        var entity = new TestContract
        {
            Id = 1,
            ValidFrom = new DateTime(2024, 6, 1),
            ValidTo = null
        };

        // Act
        var act = () => versionManager.TerminateVersion(entity, new DateTime(2024, 1, 1));

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be before*");
    }

    [Fact(DisplayName = "InitializeValidity sets ValidFrom and clears ValidTo")]
    public void InitializeValidity_SetsValidFrom_AndClearsValidTo()
    {
        // Arrange
        var versionManager = new VersionManager(_registry);
        var entity = new TestContract
        {
            Id = 1,
            ContractNumber = "C001"
        };
        var effectiveDate = new DateTime(2024, 3, 15);

        // Act
        versionManager.InitializeValidity(entity, effectiveDate);

        // Assert
        entity.ValidFrom.Should().Be(effectiveDate);
        entity.ValidTo.Should().BeNull();
    }

    [Fact(DisplayName = "GetValidityPeriod returns correct validity dates")]
    public void GetValidityPeriod_ReturnsCorrectDates()
    {
        // Arrange
        var versionManager = new VersionManager(_registry);
        var validFrom = new DateTime(2024, 1, 1);
        var validTo = new DateTime(2024, 12, 31);
        var entity = new TestContract
        {
            Id = 1,
            ValidFrom = validFrom,
            ValidTo = validTo
        };

        // Act
        var (resultValidFrom, resultValidTo) = versionManager.GetValidityPeriod(entity);

        // Assert
        resultValidFrom.Should().Be(validFrom);
        resultValidTo.Should().Be(validTo);
    }

    [Fact(DisplayName = "GetValidityPeriod returns null ValidTo for open-ended entities")]
    public void GetValidityPeriod_ReturnsNullValidTo_ForOpenEndedEntity()
    {
        // Arrange
        var versionManager = new VersionManager(_registry);
        var entity = new TestContract
        {
            Id = 1,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = null
        };

        // Act
        var (_, validTo) = versionManager.GetValidityPeriod(entity);

        // Assert
        validTo.Should().BeNull();
    }

    [Theory(DisplayName = "RangesOverlap correctly detects overlapping ranges")]
    [InlineData("2024-01-01", "2024-06-30", "2024-03-01", "2024-09-30", true)]  // Partial overlap
    [InlineData("2024-01-01", "2024-06-30", "2024-07-01", "2024-12-31", false)] // No overlap
    [InlineData("2024-01-01", "2024-12-31", "2024-06-01", "2024-06-30", true)]  // Contained
    [InlineData("2024-01-01", null, "2024-06-01", "2024-12-31", true)]          // Open-ended start
    [InlineData("2024-01-01", "2024-06-30", "2024-07-01", null, false)]         // Open-ended end, no overlap
    public void RangesOverlap_DetectsOverlap_Correctly(
        string start1Str, string? end1Str,
        string start2Str, string? end2Str,
        bool expectedOverlap)
    {
        // Arrange
        var start1 = DateTime.Parse(start1Str, CultureInfo.InvariantCulture);
        var end1 = end1Str is not null ? DateTime.Parse(end1Str, CultureInfo.InvariantCulture) : (DateTime?)null;
        var start2 = DateTime.Parse(start2Str, CultureInfo.InvariantCulture);
        var end2 = end2Str is not null ? DateTime.Parse(end2Str, CultureInfo.InvariantCulture) : (DateTime?)null;

        // Act
        var result = VersionManager.RangesOverlap(start1, end1, start2, end2);

        // Assert
        result.Should().Be(expectedOverlap);
    }

    [Fact(DisplayName = "CreateVersion throws for entity without temporal configuration")]
    public void CreateVersion_ThrowsInvalidOperationException_ForNonTemporalEntity()
    {
        // Arrange
        var emptyRegistry = new MetadataRegistry();
        var versionManager = new VersionManager(emptyRegistry);
        var entity = new TestContract { Id = 1 };

        // Act
        var act = () => versionManager.CreateVersion(entity, DateTime.Now);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not configured with temporal validity*");
    }
}

/// <summary>
/// Test entity for version manager tests.
/// </summary>
public class TestContract
{
    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
}
