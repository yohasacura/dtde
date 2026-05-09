using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;
using Dtde.Samples.Transactions.Entities;

using Microsoft.EntityFrameworkCore;

namespace Dtde.Samples.Transactions.Data;

public class TransactionsDbContext : DtdeDbContext
{
    public DbSet<Account> Accounts => Set<Account>();

    public TransactionsDbContext(DbContextOptions<TransactionsDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Region).HasMaxLength(10).IsRequired();
            entity.Property(a => a.Balance).HasColumnType("decimal(18,2)");
            entity.ShardBy(a => a.Region);
        });
    }
}
