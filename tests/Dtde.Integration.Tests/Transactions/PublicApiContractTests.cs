using System.Data.Common;

using Dtde.Core.Transactions;
using Dtde.EntityFramework;
using Dtde.EntityFramework.Extensions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Dtde.Integration.Tests.Transactions;

/// <summary>
/// Regression tests for public-API contracts that broke external consumers
/// before they were caught by the sample audit:
/// <list type="bullet">
///   <item><description><c>ShardTransactionParticipant.Context</c> and <c>.Transaction</c> are publicly accessible — the documented pattern <c>participant.Context.Set&lt;T&gt;().Add(...)</c> must work without InternalsVisibleTo.</description></item>
///   <item><description><c>EnsureAllShardsCreatedAsync</c> populates the metadata registry so the sharded query executor sees per-entity sharding configuration even in apps that don't explicitly call <c>DtdeDbContext.MetadataRegistry</c>.</description></item>
/// </list>
/// </summary>
public class PublicApiContractTests : IAsyncLifetime
{
    private readonly List<DbConnection> _connections = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var c in _connections)
        {
            await c.DisposeAsync();
        }
    }

    [Fact(DisplayName = "ShardTransactionParticipant.Context is publicly accessible from application code")]
    public async Task ParticipantContext_IsPubliclyAccessible()
    {
        var (sp, scope, ctx, euAnchor) = await BuildSingleShardContextAsync();
        await using (sp)
        await using (scope)
        {
            await using (var tx = await ctx.BeginCrossShardTransactionAsync())
            {
                var crossShardTx = (CrossShardTransaction)tx;
                var shard = ctx.ShardRegistry.GetShard("EU")!;
                var participant = await crossShardTx.GetOrCreateParticipantAsync(shard);

                // The documented pattern. This must work without
                // InternalsVisibleTo (i.e. the property must be public).
                DbContext participantContext = participant.Context;
                Assert.NotNull(participantContext);

                participantContext.Set<ContractTestEntity>().Add(new ContractTestEntity
                {
                    Id = 1,
                    Region = "EU",
                    Note = "from-context",
                });
                await participantContext.SaveChangesAsync();

                // The Transaction property is also public (for advanced
                // scenarios — custom bulk loaders need GetDbTransaction()).
                Assert.NotNull(participant.Transaction);

                await tx.CommitAsync();
            }

            Assert.Equal(1, await CountRowsAsync(euAnchor, "ContractTestEntities"));
        }
    }

    [Fact(DisplayName = "EnsureAllShardsCreatedAsync triggers metadata-registry backfill")]
    public async Task EnsureAllShardsCreatedAsync_TriggersMetadataBackfill()
    {
        var (sp, scope, ctx, _) = await BuildSingleShardContextAsync();
        await using (sp)
        await using (scope)
        {
            // EnsureAllShardsCreatedAsync was called in BuildSingleShardContextAsync.
            // Verify the metadata registry has the sharding configuration already —
            // without us touching DtdeDbContext.MetadataRegistry directly.
            var extension = AccessorExtensions.GetService<IDbContextOptions>(ctx)
                .FindExtension<Dtde.EntityFramework.Infrastructure.DtdeOptionsExtension>()!;
            var registry = extension.Options.MetadataRegistry;

            var metadata = registry.GetEntityMetadata<ContractTestEntity>();
            Assert.NotNull(metadata);
            Assert.NotNull(metadata.ShardingConfiguration);
            Assert.Equal(Dtde.Abstractions.Metadata.ShardingStrategyType.PropertyValue,
                metadata.ShardingConfiguration!.StrategyType);
        }
    }

    private async Task<(ServiceProvider sp, AsyncServiceScope scope, ContractTestDbContext ctx, SqliteConnection euAnchor)>
        BuildSingleShardContextAsync()
    {
        var dbId = Guid.NewGuid().ToString("N");
        var euConn = $"Data Source=eu_contract_{dbId};Mode=Memory;Cache=Shared";
        var euAnchor = new SqliteConnection(euConn);
        await euAnchor.OpenAsync();
        _connections.Add(euAnchor);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDtdeDbContext<ContractTestDbContext>(
            (db, conn) => db.UseSqlite(conn ?? "Data Source=:memory:"),
            dtde => dtde.AddShard("EU", euConn));

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ContractTestDbContext>();
        await ctx.EnsureAllShardsCreatedAsync();

        return (sp, scope, ctx, euAnchor);
    }

#pragma warning disable CA2100
    private static async Task<long> CountRowsAsync(DbConnection connection, string tableName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\"";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
#pragma warning restore CA2100
}

#pragma warning disable CA1062
public class ContractTestEntity
{
    public int Id { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

public class ContractTestDbContext : DtdeDbContext
{
    public DbSet<ContractTestEntity> ContractTestEntities => Set<ContractTestEntity>();

    public ContractTestDbContext(DbContextOptions<ContractTestDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ContractTestEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Region).HasMaxLength(10).IsRequired();
            e.Property(x => x.Note).HasMaxLength(100);
            e.ShardBy(x => x.Region);
        });
    }
}
#pragma warning restore CA1062
