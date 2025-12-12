using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.MultiTenant.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.MultiTenant.Data;

/// <summary>
/// DbContext for multi-tenant data with tenant-based sharding.
/// All tenant-specific entities use ShardBy(TenantId) for complete tenant isolation.
/// </summary>
public class MultiTenantDbContext : DtdeDbContext
{
    public MultiTenantDbContext(DbContextOptions<MultiTenantDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTask> Tasks => Set<ProjectTask>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenant entity - master lookup, not sharded (small table)
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId).IsUnique();
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Plan).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Domain).HasMaxLength(200);
        });

        // Project entity - sharded by TenantId for tenant isolation
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.Name });
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.OwnerId).HasMaxLength(100);

            // Shard by TenantId - all projects for a tenant in same shard
            entity.ShardBy(e => e.TenantId);
        });

        // ProjectTask entity - sharded by TenantId (co-located with Projects)
        modelBuilder.Entity<ProjectTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.ProjectId });
            entity.HasIndex(e => new { e.TenantId, e.AssigneeId });
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Description).HasMaxLength(5000);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Priority).IsRequired().HasMaxLength(50);
            entity.Property(e => e.AssigneeId).HasMaxLength(100);
            entity.Property(e => e.ReporterId).HasMaxLength(100);

            // Shard by TenantId - tasks co-located with projects
            entity.ShardBy(e => e.TenantId);
        });

        // TaskComment entity - sharded by TenantId (co-located with Tasks)
        modelBuilder.Entity<TaskComment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.TaskId });
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.AuthorId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Content).IsRequired().HasMaxLength(10000);

            // Shard by TenantId - comments co-located with tasks and projects
            entity.ShardBy(e => e.TenantId);
        });
    }
}
