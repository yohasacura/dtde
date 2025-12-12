using Dtde.Core.Metadata;

namespace Dtde.Core.Tests.Metadata;

public class DateRangeTests
{
    [Fact]
    public void Contains_ReturnsTrueForDateInRange()
    {
        // Arrange
        var range = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 4, 1));

        // Act & Assert
        Assert.True(range.Contains(new DateTime(2024, 2, 15)));
    }

    [Fact]
    public void Contains_ReturnsTrueForStartDate()
    {
        // Arrange
        var range = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 4, 1));

        // Act & Assert
        Assert.True(range.Contains(new DateTime(2024, 1, 1)));
    }

    [Fact]
    public void Contains_ReturnsFalseForEndDate()
    {
        // Arrange - End is exclusive
        var range = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 4, 1));

        // Act & Assert
        Assert.False(range.Contains(new DateTime(2024, 4, 1)));
    }

    [Fact]
    public void Contains_ReturnsFalseForDateBeforeRange()
    {
        // Arrange
        var range = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 4, 1));

        // Act & Assert
        Assert.False(range.Contains(new DateTime(2023, 12, 31)));
    }

    [Fact]
    public void Contains_ReturnsFalseForDateAfterRange()
    {
        // Arrange
        var range = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 4, 1));

        // Act & Assert
        Assert.False(range.Contains(new DateTime(2024, 4, 2)));
    }

    [Fact]
    public void Intersects_ReturnsTrueForOverlappingRanges()
    {
        // Arrange
        var range1 = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 4, 1));
        var range2 = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 3, 1),
            new DateTime(2024, 6, 1));

        // Act & Assert
        Assert.True(range1.Intersects(range2));
        Assert.True(range2.Intersects(range1));
    }

    [Fact]
    public void Intersects_ReturnsFalseForNonOverlappingRanges()
    {
        // Arrange
        var range1 = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 4, 1));
        var range2 = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 4, 1),
            new DateTime(2024, 7, 1));

        // Act & Assert - Adjacent ranges don't overlap
        Assert.False(range1.Intersects(range2));
    }

    [Fact]
    public void Intersects_ReturnsTrueForContainedRange()
    {
        // Arrange
        var outer = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31));
        var inner = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 3, 1),
            new DateTime(2024, 6, 1));

        // Act & Assert
        Assert.True(outer.Intersects(inner));
        Assert.True(inner.Intersects(outer));
    }

    [Fact]
    public void Intersection_ReturnsOverlappingPortion()
    {
        // Arrange
        var range1 = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 4, 1));
        var range2 = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 3, 1),
            new DateTime(2024, 6, 1));

        // Act
        var intersection = range1.Intersection(range2);

        // Assert
        Assert.NotNull(intersection);
        Assert.Equal(new DateTime(2024, 3, 1), intersection.Value.Start);
        Assert.Equal(new DateTime(2024, 4, 1), intersection.Value.End);
    }

    [Fact]
    public void Intersection_ReturnsNullForNonOverlapping()
    {
        // Arrange
        var range1 = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 4, 1));
        var range2 = new Dtde.Abstractions.Metadata.DateRange(
            new DateTime(2024, 4, 1),
            new DateTime(2024, 7, 1));

        // Act
        var intersection = range1.Intersection(range2);

        // Assert
        Assert.Null(intersection);
    }
}
