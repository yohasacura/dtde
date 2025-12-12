using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.DateSharding.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.DateSharding.Data;

/// <summary>
/// DbContext demonstrating date-based sharding.
/// 
/// Entities are partitioned by date:
/// - Transactions: Monthly partitions (Transactions_2024_01, etc.)
/// - AuditLogs: Daily partitions (AuditLogs_2024_01_15, etc.)
/// - Metrics: Quarterly partitions for longer retention
/// </summary>
public class DateShardingDbContext : DtdeDbContext
{
    public DateShardingDbContext(DbContextOptions<DateShardingDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Transactions - sharded by TransactionDate (monthly).
    /// </summary>
    public DbSet<Transaction> Transactions => Set<Transaction>();

    /// <summary>
    /// Audit logs - sharded by Timestamp (daily).
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// Metrics - sharded by Timestamp (quarterly).
    /// </summary>
    public DbSet<MetricDataPoint> MetricDataPoints => Set<MetricDataPoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Transaction entity
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransactionRef).HasMaxLength(50).IsRequired();
            entity.Property(e => e.AccountNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.BalanceAfter).HasPrecision(18, 2);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.Merchant).HasMaxLength(200);

            entity.HasIndex(e => e.TransactionRef);
            entity.HasIndex(e => e.AccountNumber);
            entity.HasIndex(e => e.TransactionDate);

            // DTDE: Configure sharding by date with monthly interval
            entity.ShardByDate(t => t.TransactionDate);
        });

        // Configure AuditLog entity
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(100);
            entity.Property(e => e.EntityId).HasMaxLength(100);
            entity.Property(e => e.OldValues).HasMaxLength(4000);
            entity.Property(e => e.NewValues).HasMaxLength(4000);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.CorrelationId).HasMaxLength(100);

            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CorrelationId);

            // DTDE: Configure sharding by date with daily interval
            entity.ShardByDate(a => a.Timestamp);
        });

        // Configure MetricDataPoint entity
        modelBuilder.Entity<MetricDataPoint>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MetricName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Tags).HasMaxLength(1000);
            entity.Property(e => e.Source).HasMaxLength(100);
            entity.Property(e => e.Unit).HasMaxLength(50);

            entity.HasIndex(e => new { e.MetricName, e.Timestamp });

            // DTDE: Configure sharding by date
            entity.ShardByDate(m => m.Timestamp);
        });
    }
}
