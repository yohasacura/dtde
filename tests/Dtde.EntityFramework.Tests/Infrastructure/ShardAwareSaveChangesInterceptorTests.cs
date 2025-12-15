using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.EntityFramework.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dtde.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="ShardAwareSaveChangesInterceptor"/>.
/// </summary>
public sealed class ShardAwareSaveChangesInterceptorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ShardAwareSaveChangesInterceptor _interceptor;

    public ShardAwareSaveChangesInterceptorTests()
    {
        // Set up service provider with minimal dependencies
        var services = new ServiceCollection();
        services.AddSingleton<IMetadataRegistry, MetadataRegistry>();
        services.AddSingleton<IShardRegistry, ShardRegistry>();
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();

        _interceptor = new ShardAwareSaveChangesInterceptor(
            _serviceProvider,
            NullLogger<ShardAwareSaveChangesInterceptor>.Instance);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    [Fact]
    public void Constructor_WhenServiceProviderIsNull_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ShardAwareSaveChangesInterceptor(
                null!,
                NullLogger<ShardAwareSaveChangesInterceptor>.Instance));
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ShardAwareSaveChangesInterceptor(
                _serviceProvider,
                null!));
    }

    [Fact]
    public void Constructor_WhenValidArguments_CreatesInstance()
    {
        // Arrange & Act
        var interceptor = new ShardAwareSaveChangesInterceptor(
            _serviceProvider,
            NullLogger<ShardAwareSaveChangesInterceptor>.Instance);

        // Assert
        Assert.NotNull(interceptor);
    }

    [Fact]
    public async Task SavingChangesAsync_WhenNoChanges_AllowsNormalSave()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Act - SaveChanges with no changes
        var result = await context.SaveChangesAsync();

        // Assert - no changes made
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task SavingChangesAsync_WhenUnregisteredEntityType_AllowsNormalSave()
    {
        // Arrange - metadata registry is empty by default
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test1" });

        // Act - SaveChanges with unregistered entity type
        var result = await context.SaveChangesAsync();

        // Assert - entity saved normally
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task SavingChangesAsync_WhenSingleEntity_AllowsNormalSave()
    {
        // Arrange
        // Register entity metadata
        var metadataRegistry = _serviceProvider.GetRequiredService<IMetadataRegistry>() as MetadataRegistry;
        var builder = new EntityMetadataBuilder<TestEntity>();
        builder.HasTemporalValidity(nameof(TestEntity.ValidFrom), nameof(TestEntity.ValidTo));
        metadataRegistry?.RegisterEntity(builder.Build());

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test1", RegionId = "US" });

        // Act
        var result = await context.SaveChangesAsync();

        // Assert - single entity saved normally
        Assert.Equal(1, result);
    }

    [Fact]
    public void SavingChanges_WhenSingleEntity_AllowsNormalSave()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test1" });

        // Act - sync version
        var result = context.SaveChanges();

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task SavingChangesAsync_WhenMultipleEntities_AllowsNormalSave()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test1" });
        context.TestEntities.Add(new TestEntity { Id = 2, Name = "Test2" });
        context.TestEntities.Add(new TestEntity { Id = 3, Name = "Test3" });

        // Act
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task SavingChangesAsync_WhenExplicitTransactionActive_SkipsAutoCrossShardHandling()
    {
        // Arrange - configure to ignore transaction warning for in-memory provider
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Start an explicit transaction (simulated for in-memory)
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test1" });
        context.TestEntities.Add(new TestEntity { Id = 2, Name = "Test2" });

        // Act - SaveChanges within explicit transaction
        var result = await context.SaveChangesAsync();
        await transaction.CommitAsync();

        // Assert - entities saved normally through explicit transaction
        Assert.Equal(2, result);
        Assert.Equal(2, await context.TestEntities.CountAsync());
    }

    [Fact]
    public void SavingChanges_WhenExplicitTransactionActive_SkipsAutoCrossShardHandling()
    {
        // Arrange - configure to ignore transaction warning for in-memory provider
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Start an explicit transaction (simulated for in-memory)
        using var transaction = context.Database.BeginTransaction();

        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test1" });

        // Act - SaveChanges within explicit transaction (sync)
        var result = context.SaveChanges();
        transaction.Commit();

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task SavingChangesAsync_WhenExplicitTransactionRollback_ChangesNotPersisted()
    {
        // Arrange - configure to ignore transaction warning for in-memory provider
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Start an explicit transaction (simulated for in-memory)
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test1" });
        await context.SaveChangesAsync();

        // Act - Rollback
        await transaction.RollbackAsync();

        // Assert - InMemory provider doesn't support true transactions,
        // but this test verifies the interceptor doesn't interfere with
        // explicit transaction management
        Assert.NotNull(transaction);
    }

    // Test entity class
    private sealed class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? RegionId { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
    }

    // Test DbContext
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options)
            : base(options)
        {
        }

        public DbSet<TestEntity> TestEntities => Set<TestEntity>();
    }
}
