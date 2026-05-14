using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.BulkOperations.Entities;

using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.BulkOperations.Data;

public class BulkOpsDbContext : DtdeDbContext
{
    public DbSet<AppEvent> AppEvents => Set<AppEvent>();

    public BulkOpsDbContext(DbContextOptions<BulkOpsDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Region).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Payload).HasMaxLength(500);
            entity.ShardBy(e => e.Region);
        });
    }
}
