namespace Dtde.Samples.HashSharding.Entities;

/// <summary>
/// Represents user activity/event tracking.
/// Uses hash sharding by UserId to co-locate with user data.
/// </summary>
public class UserActivity
{
    public long Id { get; set; }

    /// <summary>
    /// User identifier - enables co-location with UserProfile and UserSession.
    /// Configured via fluent API in DbContext.
    /// </summary>
    public required string UserId { get; set; }

    public required string ActivityType { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Metadata { get; set; }
    public string? SessionId { get; set; }
}
