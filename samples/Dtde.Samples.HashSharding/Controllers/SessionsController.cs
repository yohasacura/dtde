using Dtde.Samples.HashSharding.Data;
using Dtde.Samples.HashSharding.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.HashSharding.Controllers;

/// <summary>
/// API controller demonstrating session management with co-located data.
/// Sessions are stored in the same shard as their user for efficient queries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly HashShardingDbContext _context;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        HashShardingDbContext context,
        ILogger<SessionsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Create a new session for a user.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SessionCreatedDto>> CreateSession(CreateSessionRequest request)
    {
        var shardIndex = GetShardIndex(request.UserId);
        _logger.LogInformation(
            "Creating session for user {UserId} in shard {ShardIndex} (co-located with user profile)",
            request.UserId, shardIndex);

        // Verify user exists in same shard
        var userExists = await _context.UserProfiles
            .AnyAsync(u => u.UserId == request.UserId);

        if (!userExists)
            return NotFound($"User {request.UserId} not found");

        var session = new UserSession
        {
            UserId = request.UserId,
            SessionToken = Guid.NewGuid().ToString("N"),
            DeviceType = request.DeviceType,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(request.ExpiresInHours ?? 24)
        };

        _context.UserSessions.Add(session);

        // Update user's last login (same shard, efficient)
        var user = await _context.UserProfiles.FirstOrDefaultAsync(u => u.UserId == request.UserId);
        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
        }

        // Log the activity (same shard)
        _context.UserActivities.Add(new UserActivity
        {
            UserId = request.UserId,
            ActivityType = "session.created",
            ResourceType = "session",
            Timestamp = DateTime.UtcNow,
            Metadata = $"{{\"device\":\"{request.DeviceType}\",\"ip\":\"{request.IpAddress}\"}}"
        });

        await _context.SaveChangesAsync();

        return Ok(new SessionCreatedDto
        {
            SessionToken = session.SessionToken,
            UserId = session.UserId,
            ExpiresAt = session.ExpiresAt,
            ShardIndex = shardIndex
        });
    }

    /// <summary>
    /// Get sessions for a specific user.
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<IEnumerable<SessionDto>>> GetUserSessions(
        string userId,
        [FromQuery] bool includeExpired = false)
    {
        var shardIndex = GetShardIndex(userId);
        _logger.LogInformation(
            "Fetching sessions for user {UserId} from shard {ShardIndex}",
            userId, shardIndex);

        var query = _context.UserSessions
            .Where(s => s.UserId == userId);

        if (!includeExpired)
        {
            query = query.Where(s => s.ExpiresAt > DateTime.UtcNow && s.RevokedAt == null);
        }

        var sessions = await query
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SessionDto
            {
                Id = s.Id,
                SessionToken = s.SessionToken.Substring(0, 8) + "****",
                UserId = s.UserId,
                DeviceType = s.DeviceType,
                IpAddress = s.IpAddress,
                CreatedAt = s.CreatedAt,
                ExpiresAt = s.ExpiresAt,
                RevokedAt = s.RevokedAt,
                IsActive = s.ExpiresAt > DateTime.UtcNow && s.RevokedAt == null
            })
            .ToListAsync();

        return Ok(sessions);
    }

    /// <summary>
    /// Validate a session token.
    /// </summary>
    [HttpGet("validate/{sessionToken}")]
    public async Task<ActionResult<SessionValidationResult>> ValidateSession(string sessionToken)
    {
        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

        if (session == null)
        {
            return Ok(new SessionValidationResult
            {
                IsValid = false,
                Reason = "Session not found"
            });
        }

        if (session.RevokedAt != null)
        {
            return Ok(new SessionValidationResult
            {
                IsValid = false,
                Reason = "Session has been revoked",
                RevokedAt = session.RevokedAt
            });
        }

        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            return Ok(new SessionValidationResult
            {
                IsValid = false,
                Reason = "Session has expired",
                ExpiredAt = session.ExpiresAt
            });
        }

        return Ok(new SessionValidationResult
        {
            IsValid = true,
            UserId = session.UserId,
            ExpiresAt = session.ExpiresAt,
            ShardIndex = GetShardIndex(session.UserId)
        });
    }

    /// <summary>
    /// Revoke a specific session.
    /// </summary>
    [HttpDelete("{sessionToken}")]
    public async Task<ActionResult> RevokeSession(string sessionToken)
    {
        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

        if (session == null)
            return NotFound();

        session.RevokedAt = DateTime.UtcNow;

        // Log the activity (co-located)
        _context.UserActivities.Add(new UserActivity
        {
            UserId = session.UserId,
            ActivityType = "session.revoked",
            ResourceType = "session",
            Timestamp = DateTime.UtcNow,
            SessionId = sessionToken
        });

        await _context.SaveChangesAsync();

        _logger.LogInformation("Revoked session for user {UserId}", session.UserId);

        return NoContent();
    }

    /// <summary>
    /// Revoke all sessions for a user.
    /// </summary>
    [HttpDelete("user/{userId}/all")]
    public async Task<ActionResult<RevokeAllResult>> RevokeAllUserSessions(string userId)
    {
        var shardIndex = GetShardIndex(userId);
        _logger.LogInformation(
            "Revoking all sessions for user {UserId} in shard {ShardIndex}",
            userId, shardIndex);

        var sessions = await _context.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync();

        foreach (var session in sessions)
        {
            session.RevokedAt = DateTime.UtcNow;
        }

        // Log single activity for bulk revocation
        _context.UserActivities.Add(new UserActivity
        {
            UserId = userId,
            ActivityType = "session.revoked_all",
            ResourceType = "session",
            Timestamp = DateTime.UtcNow,
            Metadata = $"{{\"count\":{sessions.Count}}}"
        });

        await _context.SaveChangesAsync();

        return Ok(new RevokeAllResult
        {
            UserId = userId,
            RevokedCount = sessions.Count,
            ShardIndex = shardIndex
        });
    }

    /// <summary>
    /// Get session statistics across all shards.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<SessionStats>> GetSessionStats()
    {
        _logger.LogInformation("Fetching session statistics (fan-out across all shards)");

        var now = DateTime.UtcNow;

        var stats = await _context.UserSessions
            .GroupBy(s => 1)
            .Select(g => new SessionStats
            {
                TotalSessions = g.Count(),
                ActiveSessions = g.Count(s => s.ExpiresAt > now && s.RevokedAt == null),
                ExpiredSessions = g.Count(s => s.ExpiresAt <= now),
                RevokedSessions = g.Count(s => s.RevokedAt != null),
                UniqueUsers = g.Select(s => s.UserId).Distinct().Count(),
                SessionsByDevice = g.GroupBy(s => s.DeviceType)
                    .Select(d => new DeviceStats
                    {
                        DeviceType = d.Key,
                        Count = d.Count()
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync() ?? new SessionStats();

        return Ok(stats);
    }

    private static int GetShardIndex(string userId)
    {
        const int shardCount = 8;
        var hash = userId.GetHashCode();
        return Math.Abs(hash % shardCount);
    }
}

// DTOs
public record CreateSessionRequest
{
    public required string UserId { get; init; }
    public required string DeviceType { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public int? ExpiresInHours { get; init; }
}

public record SessionCreatedDto
{
    public string SessionToken { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public int ShardIndex { get; init; }
}

public record SessionDto
{
    public long Id { get; init; }
    public string SessionToken { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string DeviceType { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public bool IsActive { get; init; }
}

public record SessionValidationResult
{
    public bool IsValid { get; init; }
    public string? UserId { get; init; }
    public string? Reason { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public DateTime? ExpiredAt { get; init; }
    public int? ShardIndex { get; init; }
}

public record RevokeAllResult
{
    public string UserId { get; init; } = string.Empty;
    public int RevokedCount { get; init; }
    public int ShardIndex { get; init; }
}

public record SessionStats
{
    public int TotalSessions { get; init; }
    public int ActiveSessions { get; init; }
    public int ExpiredSessions { get; init; }
    public int RevokedSessions { get; init; }
    public int UniqueUsers { get; init; }
    public List<DeviceStats> SessionsByDevice { get; init; } = [];
}

public record DeviceStats
{
    public string DeviceType { get; init; } = string.Empty;
    public int Count { get; init; }
}
