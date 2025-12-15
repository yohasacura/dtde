using Dtde.Abstractions.Temporal;

namespace Dtde.Core.Temporal;

/// <summary>
/// Implementation of temporal context for managing query time filtering.
/// </summary>
public sealed class TemporalContext : ITemporalContext
{
    private DateTime? _currentPoint;
    private bool _includeHistory;

    /// <inheritdoc />
    public DateTime? CurrentPoint => _currentPoint;

    /// <inheritdoc />
    public bool IncludeHistory => _includeHistory;

    /// <summary>
    /// Creates a new temporal context with no default filtering.
    /// </summary>
    public TemporalContext()
    {
    }

    /// <summary>
    /// Creates a new temporal context with a default temporal point.
    /// </summary>
    /// <param name="defaultPoint">The default point in time to filter by.</param>
    public TemporalContext(DateTime defaultPoint)
    {
        _currentPoint = defaultPoint;
    }

    /// <inheritdoc />
    public void SetTemporalContext(DateTime temporalPoint)
    {
        _currentPoint = temporalPoint;
        _includeHistory = false;
    }

    /// <inheritdoc />
    public void EnableHistoryMode()
    {
        _includeHistory = true;
        _currentPoint = null;
    }

    /// <inheritdoc />
    public void ClearContext()
    {
        _currentPoint = null;
        _includeHistory = false;
    }
}
