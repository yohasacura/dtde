namespace Dtde.Samples.HashSharding.Entities;

/// <summary>
/// Represents a session for a user.
/// Uses same shard key (UserId) to co-locate with UserProfile.
/// </summary>
public class UserSession
{
    public long Id { get; set; }

    /// <summary>
    /// User identifier - matches UserProfile shard key for co-location.
    /// Configured via fluent API in DbContext.
    /// </summary>
    public required string UserId { get; set; }

    public required string SessionToken { get; set; }
    public required string DeviceType { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;
}
