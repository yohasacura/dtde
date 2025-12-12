using Dtde.Samples.HashSharding.Data;
using Dtde.Samples.HashSharding.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.HashSharding.Controllers;

/// <summary>
/// API controller demonstrating hash-sharded user operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly HashShardingDbContext _context;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        HashShardingDbContext context,
        ILogger<UsersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get a user by their ID (direct shard access).
    /// </summary>
    [HttpGet("{userId}")]
    public async Task<ActionResult<UserProfileDto>> GetUser(string userId)
    {
        var shardIndex = GetShardIndex(userId);
        _logger.LogInformation("Fetching user {UserId} from shard {ShardIndex}", userId, shardIndex);

        var user = await _context.UserProfiles
            .Where(u => u.UserId == userId)
            .Select(u => new UserProfileDto
            {
                UserId = u.UserId,
                Email = u.Email,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                Bio = u.Bio,
                Location = u.Location,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                IsActive = u.IsActive
            })
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound();

        return Ok(user);
    }

    /// <summary>
    /// Create a new user profile.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<UserProfileDto>> CreateUser(CreateUserRequest request)
    {
        var shardIndex = GetShardIndex(request.UserId);
        _logger.LogInformation("Creating user {UserId} in shard {ShardIndex}", request.UserId, shardIndex);

        var user = new UserProfile
        {
            UserId = request.UserId,
            Email = request.Email,
            DisplayName = request.DisplayName,
            AvatarUrl = request.AvatarUrl,
            Bio = request.Bio,
            Location = request.Location,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.UserProfiles.Add(user);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetUser), new { userId = user.UserId }, new UserProfileDto
        {
            UserId = user.UserId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Bio = user.Bio,
            Location = user.Location,
            CreatedAt = user.CreatedAt,
            IsActive = user.IsActive
        });
    }

    /// <summary>
    /// Update user profile.
    /// </summary>
    [HttpPut("{userId}")]
    public async Task<ActionResult<UserProfileDto>> UpdateUser(string userId, UpdateUserRequest request)
    {
        var user = await _context.UserProfiles
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null)
            return NotFound();

        if (request.DisplayName != null)
            user.DisplayName = request.DisplayName;
        if (request.AvatarUrl != null)
            user.AvatarUrl = request.AvatarUrl;
        if (request.Bio != null)
            user.Bio = request.Bio;
        if (request.Location != null)
            user.Location = request.Location;

        await _context.SaveChangesAsync();

        return Ok(new UserProfileDto
        {
            UserId = user.UserId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Bio = user.Bio,
            Location = user.Location,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            IsActive = user.IsActive
        });
    }

    /// <summary>
    /// Get all users (fan-out across all shards).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserProfileDto>>> GetAllUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        _logger.LogInformation("Fetching all users across all shards (fan-out query)");

        var users = await _context.UserProfiles
            .OrderBy(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserProfileDto
            {
                UserId = u.UserId,
                Email = u.Email,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                Bio = u.Bio,
                Location = u.Location,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                IsActive = u.IsActive
            })
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>
    /// Search users by email domain (fan-out).
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<UserProfileDto>>> SearchUsers(
        [FromQuery] string? emailDomain,
        [FromQuery] string? location,
        [FromQuery] bool? isActive)
    {
        _logger.LogInformation(
            "Searching users with filters: EmailDomain={EmailDomain}, Location={Location}, IsActive={IsActive}",
            emailDomain, location, isActive);

        var query = _context.UserProfiles.AsQueryable();

        if (!string.IsNullOrEmpty(emailDomain))
            query = query.Where(u => u.Email.EndsWith("@" + emailDomain));

        if (!string.IsNullOrEmpty(location))
            query = query.Where(u => u.Location != null && u.Location.Contains(location));

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        var users = await query
            .Take(100)
            .Select(u => new UserProfileDto
            {
                UserId = u.UserId,
                Email = u.Email,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                Bio = u.Bio,
                Location = u.Location,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                IsActive = u.IsActive
            })
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>
    /// Get user data with co-located sessions (single shard access).
    /// </summary>
    [HttpGet("{userId}/full")]
    public async Task<ActionResult<UserWithSessionsDto>> GetUserWithSessions(string userId)
    {
        var shardIndex = GetShardIndex(userId);
        _logger.LogInformation(
            "Fetching user {UserId} with sessions from shard {ShardIndex} (co-located data)",
            userId, shardIndex);

        var user = await _context.UserProfiles
            .Where(u => u.UserId == userId)
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound();

        var sessions = await _context.UserSessions
            .Where(s => s.UserId == userId && s.ExpiresAt > DateTime.UtcNow && s.RevokedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new UserSessionDto
            {
                SessionToken = s.SessionToken.Substring(0, 8) + "****",
                DeviceType = s.DeviceType,
                CreatedAt = s.CreatedAt,
                ExpiresAt = s.ExpiresAt,
                IsActive = true
            })
            .ToListAsync();

        var recentActivity = await _context.UserActivities
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.Timestamp)
            .Take(10)
            .Select(a => new UserActivityDto
            {
                ActivityType = a.ActivityType,
                ResourceType = a.ResourceType,
                ResourceId = a.ResourceId,
                Timestamp = a.Timestamp
            })
            .ToListAsync();

        return Ok(new UserWithSessionsDto
        {
            User = new UserProfileDto
            {
                UserId = user.UserId,
                Email = user.Email,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl,
                Bio = user.Bio,
                Location = user.Location,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                IsActive = user.IsActive
            },
            ActiveSessions = sessions,
            RecentActivity = recentActivity,
            ShardIndex = shardIndex
        });
    }

    /// <summary>
    /// Get shard distribution statistics.
    /// </summary>
    [HttpGet("stats/distribution")]
    public async Task<ActionResult<ShardDistributionStats>> GetShardDistribution()
    {
        _logger.LogInformation("Calculating shard distribution statistics");

        var allUsers = await _context.UserProfiles
            .Select(u => u.UserId)
            .ToListAsync();

        var distribution = allUsers
            .GroupBy(userId => GetShardIndex(userId))
            .Select(g => new ShardStats
            {
                ShardIndex = g.Key,
                UserCount = g.Count()
            })
            .OrderBy(s => s.ShardIndex)
            .ToList();

        return Ok(new ShardDistributionStats
        {
            TotalUsers = allUsers.Count,
            ShardCount = 8,
            Distribution = distribution,
            StandardDeviation = CalculateStdDev(distribution.Select(d => (double)d.UserCount))
        });
    }

    /// <summary>
    /// Show which shard a user ID would be routed to.
    /// </summary>
    [HttpGet("shard-lookup/{userId}")]
    public ActionResult<ShardLookupResult> GetShardForUser(string userId)
    {
        var shardIndex = GetShardIndex(userId);

        return Ok(new ShardLookupResult
        {
            UserId = userId,
            ShardIndex = shardIndex,
            ShardId = $"hash-shard-{shardIndex}",
            HashValue = userId.GetHashCode()
        });
    }

    // Helper method to calculate shard index (same algorithm as DTDE)
    private static int GetShardIndex(string userId)
    {
        const int shardCount = 8;
        var hash = userId.GetHashCode();
        return Math.Abs(hash % shardCount);
    }

    private static double CalculateStdDev(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return 0;
        var avg = list.Average();
        var sumSquares = list.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumSquares / list.Count);
    }
}

// DTOs
public record UserProfileDto
{
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
    public string? Bio { get; init; }
    public string? Location { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public bool IsActive { get; init; }
}

public record CreateUserRequest
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
    public string? Bio { get; init; }
    public string? Location { get; init; }
}

public record UpdateUserRequest
{
    public string? DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
    public string? Bio { get; init; }
    public string? Location { get; init; }
}

public record UserSessionDto
{
    public string SessionToken { get; init; } = string.Empty;
    public string DeviceType { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public bool IsActive { get; init; }
}

public record UserActivityDto
{
    public string ActivityType { get; init; } = string.Empty;
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public DateTime Timestamp { get; init; }
}

public record UserWithSessionsDto
{
    public required UserProfileDto User { get; init; }
    public IEnumerable<UserSessionDto> ActiveSessions { get; init; } = [];
    public IEnumerable<UserActivityDto> RecentActivity { get; init; } = [];
    public int ShardIndex { get; init; }
}

public record ShardDistributionStats
{
    public int TotalUsers { get; init; }
    public int ShardCount { get; init; }
    public IEnumerable<ShardStats> Distribution { get; init; } = [];
    public double StandardDeviation { get; init; }
}

public record ShardStats
{
    public int ShardIndex { get; init; }
    public int UserCount { get; init; }
}

public record ShardLookupResult
{
    public string UserId { get; init; } = string.Empty;
    public int ShardIndex { get; init; }
    public string ShardId { get; init; } = string.Empty;
    public int HashValue { get; init; }
}
