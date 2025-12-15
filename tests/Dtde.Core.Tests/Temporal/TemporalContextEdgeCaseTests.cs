using Dtde.Core.Temporal;

namespace Dtde.Core.Tests.Temporal;

/// <summary>
/// Additional edge case tests for <see cref="TemporalContext"/>.
/// </summary>
public class TemporalContextEdgeCaseTests
{
    [Fact]
    public void Constructor_WithDefaultPoint_SetsCurrentPoint()
    {
        var expectedDate = new DateTime(2024, 6, 15);
        var context = new TemporalContext(expectedDate);

        Assert.Equal(expectedDate, context.CurrentPoint);
        Assert.False(context.IncludeHistory);
    }

    [Fact]
    public void SetTemporalContext_OverwritesPreviousPoint()
    {
        var context = new TemporalContext();
        var firstDate = new DateTime(2024, 1, 1);
        var secondDate = new DateTime(2024, 6, 15);

        context.SetTemporalContext(firstDate);
        Assert.Equal(firstDate, context.CurrentPoint);

        context.SetTemporalContext(secondDate);
        Assert.Equal(secondDate, context.CurrentPoint);
    }

    [Fact]
    public void SetTemporalContext_WithMinValue_Works()
    {
        var context = new TemporalContext();

        context.SetTemporalContext(DateTime.MinValue);

        Assert.Equal(DateTime.MinValue, context.CurrentPoint);
    }

    [Fact]
    public void SetTemporalContext_WithMaxValue_Works()
    {
        var context = new TemporalContext();

        context.SetTemporalContext(DateTime.MaxValue);

        Assert.Equal(DateTime.MaxValue, context.CurrentPoint);
    }

    [Fact]
    public void EnableHistoryMode_CanBeCalledMultipleTimes()
    {
        var context = new TemporalContext();

        context.EnableHistoryMode();
        Assert.True(context.IncludeHistory);

        context.EnableHistoryMode();
        Assert.True(context.IncludeHistory);
    }

    [Fact]
    public void EnableHistoryMode_ClearsCurrentPoint()
    {
        var context = new TemporalContext();
        context.SetTemporalContext(new DateTime(2024, 6, 15));

        context.EnableHistoryMode();

        Assert.Null(context.CurrentPoint);
        Assert.True(context.IncludeHistory);
    }

    [Fact]
    public void ClearContext_CanBeCalledOnFreshContext()
    {
        var context = new TemporalContext();

        context.ClearContext();

        Assert.Null(context.CurrentPoint);
        Assert.False(context.IncludeHistory);
    }

    [Fact]
    public void ClearContext_CanBeCalledMultipleTimes()
    {
        var context = new TemporalContext();
        context.SetTemporalContext(DateTime.Now);

        context.ClearContext();
        context.ClearContext();

        Assert.Null(context.CurrentPoint);
    }

    [Fact]
    public void ContextStates_SwitchingBetweenModes()
    {
        var context = new TemporalContext();
        var testDate = new DateTime(2024, 6, 15);

        context.SetTemporalContext(testDate);
        Assert.Equal(testDate, context.CurrentPoint);
        Assert.False(context.IncludeHistory);

        context.EnableHistoryMode();
        Assert.Null(context.CurrentPoint);
        Assert.True(context.IncludeHistory);

        context.SetTemporalContext(testDate);
        Assert.Equal(testDate, context.CurrentPoint);
        Assert.False(context.IncludeHistory);

        context.ClearContext();
        Assert.Null(context.CurrentPoint);
        Assert.False(context.IncludeHistory);
    }

    [Fact]
    public void Constructor_WithDefaultPoint_LeavesHistoryModeDisabled()
    {
        var context = new TemporalContext(DateTime.Now);

        Assert.False(context.IncludeHistory);
    }

    [Fact]
    public void SetTemporalContext_WithUtcDate_Works()
    {
        var context = new TemporalContext();
        var utcDate = DateTime.UtcNow;

        context.SetTemporalContext(utcDate);

        Assert.Equal(utcDate, context.CurrentPoint);
        Assert.Equal(DateTimeKind.Utc, context.CurrentPoint!.Value.Kind);
    }

    [Fact]
    public void SetTemporalContext_WithLocalDate_Works()
    {
        var context = new TemporalContext();
        var localDate = DateTime.Now;

        context.SetTemporalContext(localDate);

        Assert.Equal(localDate, context.CurrentPoint);
        Assert.Equal(DateTimeKind.Local, context.CurrentPoint!.Value.Kind);
    }

    [Fact]
    public void SetTemporalContext_PreservesDateTimeKind()
    {
        var context = new TemporalContext();
        var specifiedDate = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);

        context.SetTemporalContext(specifiedDate);

        Assert.Equal(DateTimeKind.Unspecified, context.CurrentPoint!.Value.Kind);
    }

    [Fact]
    public void CurrentPoint_IsNullableDateTime()
    {
        var context = new TemporalContext();

        DateTime? nullablePoint = context.CurrentPoint;

        Assert.Null(nullablePoint);
    }

    [Fact]
    public void IncludeHistory_IsBooleanProperty()
    {
        var context = new TemporalContext();

        bool includeHistory = context.IncludeHistory;

        Assert.False(includeHistory);
    }
}
