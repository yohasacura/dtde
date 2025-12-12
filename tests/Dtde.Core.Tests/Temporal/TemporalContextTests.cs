using Dtde.Core.Temporal;
using Dtde.Abstractions.Temporal;

namespace Dtde.Core.Tests.Temporal;

public class TemporalContextTests
{
    [Fact]
    public void TemporalContext_DefaultsToNullPoint()
    {
        // Arrange & Act
        var context = new TemporalContext();

        // Assert
        Assert.Null(context.CurrentPoint);
        Assert.False(context.IncludeHistory);
    }

    [Fact]
    public void SetTemporalContext_SetsCurrentPoint()
    {
        // Arrange
        var context = new TemporalContext();
        var expectedDate = new DateTime(2024, 6, 15);

        // Act
        context.SetTemporalContext(expectedDate);

        // Assert
        Assert.Equal(expectedDate, context.CurrentPoint);
    }

    [Fact]
    public void EnableHistoryMode_SetsIncludeHistory()
    {
        // Arrange
        var context = new TemporalContext();

        // Act
        context.EnableHistoryMode();

        // Assert
        Assert.True(context.IncludeHistory);
    }

    [Fact]
    public void ClearContext_ResetsAllSettings()
    {
        // Arrange
        var context = new TemporalContext();
        context.SetTemporalContext(DateTime.Now);
        context.EnableHistoryMode();

        // Act
        context.ClearContext();

        // Assert
        Assert.Null(context.CurrentPoint);
        Assert.False(context.IncludeHistory);
    }

    [Fact]
    public void SetTemporalContext_DisablesHistoryMode()
    {
        // Arrange
        var context = new TemporalContext();
        context.EnableHistoryMode();

        // Act
        context.SetTemporalContext(DateTime.Now);

        // Assert
        Assert.False(context.IncludeHistory);
    }
}
