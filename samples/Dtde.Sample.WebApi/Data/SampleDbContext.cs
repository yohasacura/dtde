using Dtde.EntityFramework;
using Dtde.Sample.WebApi.Entities;
using Microsoft.EntityFrameworkCore;

// This sample demonstrates mixed usage of:
// - Temporal entities (Contract, ContractLineItem) with DTDE temporal versioning
// - Regular entities (Customer, AuditLog) with standard EF Core behavior

namespace Dtde.Sample.WebApi.Data;

/// <summary>
/// Sample DbContext demonstrating DTDE integration.
/// </summary>
public class SampleDbContext : DtdeDbContext
{
    public SampleDbContext(DbContextOptions<SampleDbContext> options)
        : base(options)
    {
    }

    // Temporal entities (configured with HasTemporalValidity)
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractLineItem> ContractLineItems => Set<ContractLineItem>();

    // Regular EF Core entities (no temporal filtering)
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Contract>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ContractNumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.CustomerName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.CreatedBy).HasMaxLength(100);
            entity.Property(e => e.ModifiedBy).HasMaxLength(100);

            entity.HasIndex(e => e.ContractNumber);
            entity.HasIndex(e => e.ValidFrom);
        });

        modelBuilder.Entity<ContractLineItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);

            entity.HasOne(e => e.Contract)
                .WithMany(c => c.LineItems)
                .HasForeignKey(e => e.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ContractId);
        });

        // Regular entities - standard EF Core configuration (no temporal)
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(100);
            entity.Property(e => e.Details).HasMaxLength(2000);
            entity.HasIndex(e => e.Timestamp);
        });
    }
}
