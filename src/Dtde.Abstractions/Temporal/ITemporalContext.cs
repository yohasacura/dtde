namespace Dtde.Abstractions.Temporal;

/// <summary>
/// Represents the temporal context for queries.
/// Controls which point in time is used for filtering temporal entities.
/// </summary>
public interface ITemporalContext
{
    /// <summary>
    /// Gets the current temporal point for filtering.
    /// Null means no default temporal filter is applied.
    /// </summary>
    public DateTime? CurrentPoint { get; }

    /// <summary>
    /// Gets whether historical versions should be included in queries.
    /// When true, temporal filtering is disabled.
    /// </summary>
    public bool IncludeHistory { get; }

    /// <summary>
    /// Sets the temporal context to a specific point in time.
    /// </summary>
    /// <param name="temporalPoint">The point in time to filter by.</param>
    public void SetTemporalContext(DateTime temporalPoint);

    /// <summary>
    /// Enables history mode, including all versions in queries.
    /// </summary>
    public void EnableHistoryMode();

    /// <summary>
    /// Clears the temporal context, using default behavior.
    /// </summary>
    public void ClearContext();
}
