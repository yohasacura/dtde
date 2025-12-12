using Dtde.Abstractions.Metadata;
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.Combined.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.Combined.Data;

/// <summary>
/// DbContext demonstrating combined sharding strategies.
/// Shows different sharding approaches for different entity types:
/// - Account: Property-based sharding by Region
/// - AccountTransaction: Date-based sharding by TransactionDate (monthly)
/// - RegulatoryDocument: Property-based sharding by DocumentType
/// - ComplianceAudit: Hash-based sharding by EntityReference
/// </summary>
public class CombinedDbContext : DtdeDbContext
{
    public CombinedDbContext(DbContextOptions<CombinedDbContext> options)
        : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountTransaction> Transactions => Set<AccountTransaction>();
    public DbSet<RegulatoryDocument> RegulatoryDocuments => Set<RegulatoryDocument>();
    public DbSet<ComplianceAudit> ComplianceAudits => Set<ComplianceAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Account: Property-based sharding by Region for data residency compliance
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AccountNumber).IsUnique();
            entity.HasIndex(e => e.Region);
            entity.HasIndex(e => e.HolderId);
            entity.Property(e => e.Region).IsRequired().HasMaxLength(20);
            entity.Property(e => e.AccountNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.AccountType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.Property(e => e.Balance).HasPrecision(18, 2);
            entity.Property(e => e.HolderId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);

            // Shard by Region for regulatory data residency
            entity.ShardBy(e => e.Region);
        });

        // AccountTransaction: Date-based sharding for time-series data
        modelBuilder.Entity<AccountTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TransactionDate);
            entity.HasIndex(e => e.AccountNumber);
            entity.HasIndex(e => new { e.AccountNumber, e.TransactionDate });
            entity.Property(e => e.AccountNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.TransactionType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.BalanceBefore).HasPrecision(18, 2);
            entity.Property(e => e.BalanceAfter).HasPrecision(18, 2);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.CounterpartyAccount).HasMaxLength(50);
            entity.Property(e => e.Reference).HasMaxLength(100);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);

            // Shard by date (monthly) for efficient time-range queries
            entity.ShardByDate(e => e.TransactionDate, DateShardInterval.Month);
        });

        // RegulatoryDocument: Property-based sharding by DocumentType
        modelBuilder.Entity<RegulatoryDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DocumentId).IsUnique();
            entity.HasIndex(e => e.DocumentType);
            entity.HasIndex(e => new { e.Region, e.Jurisdiction });
            entity.Property(e => e.DocumentType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DocumentId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Region).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Jurisdiction).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ContentHash).IsRequired().HasMaxLength(128);
            entity.Property(e => e.ContentUrl).HasMaxLength(500);

            // Shard by DocumentType for logical grouping
            entity.ShardBy(e => e.DocumentType);
        });

        // ComplianceAudit: Hash-based sharding for even distribution
        modelBuilder.Entity<ComplianceAudit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EntityReference);
            entity.HasIndex(e => e.PerformedAt);
            entity.HasIndex(e => new { e.EntityType, e.EntityReference });
            entity.Property(e => e.EntityReference).IsRequired().HasMaxLength(200);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.AuditType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PerformedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.SessionId).HasMaxLength(100);
            entity.Property(e => e.ReviewedBy).HasMaxLength(100);

            // Shard by hash for even distribution across shards
            entity.ShardByHash(e => e.EntityReference, shardCount: 8);
        });
    }
}
