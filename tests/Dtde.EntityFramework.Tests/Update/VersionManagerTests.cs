using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.EntityFramework.Update;
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

        var newVersion = versionManager.CreateVersion(original, newEffectiveDate);

        Assert.NotSame(original, newVersion);
        Assert.Equal("C001", newVersion.ContractNumber);
        Assert.Equal("Acme Corp", newVersion.CustomerName);
        Assert.Equal(10000m, newVersion.Amount);
        Assert.Equal(newEffectiveDate, newVersion.ValidFrom);
        Assert.Null(newVersion.ValidTo);
    }

    [Fact(DisplayName = "TerminateVersion sets ValidTo to termination date")]
    public void TerminateVersion_SetsValidTo_ToTerminationDate()
    {
        var versionManager = new VersionManager(_registry);
        var entity = new TestContract
        {
            Id = 1,
            ContractNumber = "C001",
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = null
        };
        var terminationDate = new DateTime(2024, 12, 31);

        versionManager.TerminateVersion(entity, terminationDate);

        Assert.Equal(terminationDate, entity.ValidTo);
    }

    [Fact(DisplayName = "TerminateVersion throws when termination date is before ValidFrom")]
    public void TerminateVersion_ThrowsArgumentException_WhenTerminationBeforeValidFrom()
    {
        var versionManager = new VersionManager(_registry);
        var entity = new TestContract
        {
            Id = 1,
            ValidFrom = new DateTime(2024, 6, 1),
            ValidTo = null
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            versionManager.TerminateVersion(entity, new DateTime(2024, 1, 1)));

        Assert.Contains("cannot be before", exception.Message);
    }

    [Fact(DisplayName = "InitializeValidity sets ValidFrom and clears ValidTo")]
    public void InitializeValidity_SetsValidFrom_AndClearsValidTo()
    {
        var versionManager = new VersionManager(_registry);
        var entity = new TestContract
        {
            Id = 1,
            ContractNumber = "C001"
        };
        var effectiveDate = new DateTime(2024, 3, 15);

        versionManager.InitializeValidity(entity, effectiveDate);

        Assert.Equal(effectiveDate, entity.ValidFrom);
        Assert.Null(entity.ValidTo);
    }

    [Fact(DisplayName = "GetValidityPeriod returns correct validity dates")]
    public void GetValidityPeriod_ReturnsCorrectDates()
    {
        var versionManager = new VersionManager(_registry);
        var validFrom = new DateTime(2024, 1, 1);
        var validTo = new DateTime(2024, 12, 31);
        var entity = new TestContract
        {
            Id = 1,
            ValidFrom = validFrom,
            ValidTo = validTo
        };

        var (resultValidFrom, resultValidTo) = versionManager.GetValidityPeriod(entity);

        Assert.Equal(validFrom, resultValidFrom);
        Assert.Equal(validTo, resultValidTo);
    }

    [Fact(DisplayName = "GetValidityPeriod returns null ValidTo for open-ended entities")]
    public void GetValidityPeriod_ReturnsNullValidTo_ForOpenEndedEntity()
    {
        var versionManager = new VersionManager(_registry);
        var entity = new TestContract
        {
            Id = 1,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = null
        };

        var (_, validTo) = versionManager.GetValidityPeriod(entity);

        Assert.Null(validTo);
    }

    [Theory(DisplayName = "RangesOverlap correctly detects overlapping ranges")]
    [InlineData("2024-01-01", "2024-06-30", "2024-03-01", "2024-09-30", true)]
    [InlineData("2024-01-01", "2024-06-30", "2024-07-01", "2024-12-31", false)]
    [InlineData("2024-01-01", "2024-12-31", "2024-06-01", "2024-06-30", true)]
    [InlineData("2024-01-01", null, "2024-06-01", "2024-12-31", true)]
    [InlineData("2024-01-01", "2024-06-30", "2024-07-01", null, false)]
    public void RangesOverlap_DetectsOverlap_Correctly(
        string start1Str, string? end1Str,
        string start2Str, string? end2Str,
        bool expectedOverlap)
    {
        var start1 = DateTime.Parse(start1Str, CultureInfo.InvariantCulture);
        var end1 = end1Str is not null ? DateTime.Parse(end1Str, CultureInfo.InvariantCulture) : (DateTime?)null;
        var start2 = DateTime.Parse(start2Str, CultureInfo.InvariantCulture);
        var end2 = end2Str is not null ? DateTime.Parse(end2Str, CultureInfo.InvariantCulture) : (DateTime?)null;

        var result = VersionManager.RangesOverlap(start1, end1, start2, end2);

        Assert.Equal(expectedOverlap, result);
    }

    [Fact(DisplayName = "CreateVersion throws for entity without temporal configuration")]
    public void CreateVersion_ThrowsInvalidOperationException_ForNonTemporalEntity()
    {
        var emptyRegistry = new MetadataRegistry();
        var versionManager = new VersionManager(emptyRegistry);
        var entity = new TestContract { Id = 1 };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            versionManager.CreateVersion(entity, DateTime.Now));

        Assert.Contains("not configured with temporal validity", exception.Message);
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
