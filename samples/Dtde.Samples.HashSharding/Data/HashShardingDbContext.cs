using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.HashSharding.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.HashSharding.Data;

/// <summary>
/// DbContext for hash-sharded user data.
/// </summary>
public class HashShardingDbContext : DtdeDbContext
{
    public HashShardingDbContext(DbContextOptions<HashShardingDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserActivity> UserActivities => Set<UserActivity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AvatarUrl).HasMaxLength(500);
            entity.Property(e => e.Bio).HasMaxLength(2000);
            entity.Property(e => e.Location).HasMaxLength(200);

            // Configure hash-based sharding for even distribution
            entity.ShardByHash(u => u.UserId, shardCount: 8);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionToken).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.SessionToken).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DeviceType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);

            // Co-locate with UserProfile via same shard key
            entity.ShardByHash(s => s.UserId, shardCount: 8);
        });

        modelBuilder.Entity<UserActivity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.UserId, e.Timestamp });
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ActivityType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ResourceType).HasMaxLength(100);
            entity.Property(e => e.ResourceId).HasMaxLength(100);
            entity.Property(e => e.SessionId).HasMaxLength(256);

            // Co-locate with UserProfile via same shard key
            entity.ShardByHash(a => a.UserId, shardCount: 8);
        });
    }
}
