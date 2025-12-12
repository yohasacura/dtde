namespace Dtde.Samples.HashSharding.Entities;

/// <summary>
/// Represents a user profile.
/// Uses hash-based sharding by UserId for even distribution.
/// </summary>
public class UserProfile
{
    public long Id { get; set; }

    /// <summary>
    /// Unique user identifier - used as shard key.
    /// Hash sharding ensures even distribution regardless of user ID patterns.
    /// Configured via fluent API in DbContext.
    /// </summary>
    public required string UserId { get; set; }

    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string? Location { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? PreferencesJson { get; set; }
}
