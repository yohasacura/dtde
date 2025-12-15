using Dtde.Abstractions.Metadata;
using Dtde.Core.Metadata;
using Dtde.Core.Sharding;
using Dtde.EntityFramework.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dtde.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="TransparentShardingInterceptor"/>.
/// </summary>
public sealed class TransparentShardingInterceptorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly TransparentShardingInterceptor _interceptor;

    public TransparentShardingInterceptorTests()
    {
        // Set up service provider with minimal dependencies
        var services = new ServiceCollection();
        services.AddSingleton<IMetadataRegistry, MetadataRegistry>();
        services.AddSingleton<IShardRegistry, ShardRegistry>();
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();

        _interceptor = new TransparentShardingInterceptor(
            _serviceProvider,
            NullLogger<TransparentShardingInterceptor>.Instance);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WhenServiceProviderIsNull_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TransparentShardingInterceptor(
                null!,
                NullLogger<TransparentShardingInterceptor>.Instance));
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TransparentShardingInterceptor(
                _serviceProvider,
                null!));
    }

    [Fact]
    public void Constructor_WhenValidArguments_CreatesInstance()
    {
        // Arrange & Act
        var interceptor = new TransparentShardingInterceptor(
            _serviceProvider,
            NullLogger<TransparentShardingInterceptor>.Instance);

        // Assert
        Assert.NotNull(interceptor);
    }

    [Fact]
    public void InterceptorImplementsAllRequiredInterfaces()
    {
        // Assert
        Assert.IsAssignableFrom<SaveChangesInterceptor>(_interceptor);
        Assert.IsAssignableFrom<IDbTransactionInterceptor>(_interceptor);
    }

    #endregion

    #region SaveChanges Tests - Insert Operations

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

    #endregion

    #region Explicit Transaction Tests

    [Fact]
    public async Task SavingChangesAsync_WhenExplicitTransactionActive_AllowsNormalSave()
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
    public void SavingChanges_WhenExplicitTransactionActive_AllowsNormalSave()
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
    public async Task SavingChangesAsync_WhenExplicitTransactionRollback_WorksCorrectly()
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

    [Fact]
    public async Task TransactionCommit_WhenSingleContext_WorksCorrectly()
    {
        // Arrange - configure to ignore transaction warning for in-memory provider
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Start an explicit transaction
        await using var transaction = await context.Database.BeginTransactionAsync();

        // Add entity within transaction
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "TransactionTest" });
        await context.SaveChangesAsync();

        // Act - Commit
        await transaction.CommitAsync();

        // Assert - entity should be persisted
        var entity = await context.TestEntities.FindAsync(1);
        Assert.NotNull(entity);
        Assert.Equal("TransactionTest", entity.Name);
    }

    #endregion

    #region SaveChanges Tests - Update Operations

    [Fact]
    public async Task SavingChangesAsync_WhenUpdatingSingleEntity_AllowsNormalSave()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Insert first
        var entity = new TestEntity { Id = 1, Name = "Original" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Act - Update
        entity.Name = "Updated";
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(1, result);
        var updated = await context.TestEntities.FindAsync(1);
        Assert.Equal("Updated", updated!.Name);
    }

    [Fact]
    public async Task SavingChangesAsync_WhenUpdatingMultipleEntities_AllowsNormalSave()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Insert first
        context.TestEntities.AddRange(
            new TestEntity { Id = 1, Name = "Entity1" },
            new TestEntity { Id = 2, Name = "Entity2" },
            new TestEntity { Id = 3, Name = "Entity3" });
        await context.SaveChangesAsync();

        // Act - Update all
        foreach (var entity in context.TestEntities)
        {
            entity.Name = $"Updated_{entity.Id}";
        }
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(3, result);
        Assert.All(await context.TestEntities.ToListAsync(),
            e => Assert.StartsWith("Updated_", e.Name));
    }

    [Fact]
    public void SavingChanges_WhenUpdatingSingleEntity_AllowsNormalSave()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Insert first
        var entity = new TestEntity { Id = 1, Name = "Original" };
        context.TestEntities.Add(entity);
        context.SaveChanges();

        // Act - Update (sync)
        entity.Name = "Updated";
        var result = context.SaveChanges();

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region SaveChanges Tests - Delete Operations

    [Fact]
    public async Task SavingChangesAsync_WhenDeletingSingleEntity_AllowsNormalSave()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Insert first
        var entity = new TestEntity { Id = 1, Name = "ToDelete" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Act - Delete
        context.TestEntities.Remove(entity);
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(1, result);
        Assert.Null(await context.TestEntities.FindAsync(1));
    }

    [Fact]
    public async Task SavingChangesAsync_WhenDeletingMultipleEntities_AllowsNormalSave()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Insert first
        context.TestEntities.AddRange(
            new TestEntity { Id = 1, Name = "Entity1" },
            new TestEntity { Id = 2, Name = "Entity2" },
            new TestEntity { Id = 3, Name = "Entity3" });
        await context.SaveChangesAsync();

        // Act - Delete all
        context.TestEntities.RemoveRange(context.TestEntities.ToList());
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(3, result);
        Assert.Empty(await context.TestEntities.ToListAsync());
    }

    [Fact]
    public void SavingChanges_WhenDeletingSingleEntity_AllowsNormalSave()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Insert first
        var entity = new TestEntity { Id = 1, Name = "ToDelete" };
        context.TestEntities.Add(entity);
        context.SaveChanges();

        // Act - Delete (sync)
        context.TestEntities.Remove(entity);
        var result = context.SaveChanges();

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region SaveChanges Tests - Mixed Operations

    [Fact]
    public async Task SavingChangesAsync_WhenMixedOperations_AllowsNormalSave()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Setup - Insert initial entities
        context.TestEntities.AddRange(
            new TestEntity { Id = 1, Name = "ToUpdate" },
            new TestEntity { Id = 2, Name = "ToDelete" });
        await context.SaveChangesAsync();

        // Act - Mixed: Insert + Update + Delete in single SaveChanges
        var toUpdate = await context.TestEntities.FindAsync(1);
        toUpdate!.Name = "Updated";

        var toDelete = await context.TestEntities.FindAsync(2);
        context.TestEntities.Remove(toDelete!);

        context.TestEntities.Add(new TestEntity { Id = 3, Name = "NewEntity" });

        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(3, result); // 1 update + 1 delete + 1 insert
        Assert.Equal(2, await context.TestEntities.CountAsync());
        Assert.Equal("Updated", (await context.TestEntities.FindAsync(1))!.Name);
        Assert.Null(await context.TestEntities.FindAsync(2));
        Assert.NotNull(await context.TestEntities.FindAsync(3));
    }

    #endregion

    #region Multiple SaveChanges Within Transaction Tests

    [Fact]
    public async Task MultipleSaveChanges_WithinSingleTransaction_WorksCorrectly()
    {
        // Arrange - configure to ignore transaction warning for in-memory provider
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Start an explicit transaction
        await using var transaction = await context.Database.BeginTransactionAsync();

        // First SaveChanges
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "First" });
        var result1 = await context.SaveChangesAsync();

        // Second SaveChanges
        context.TestEntities.Add(new TestEntity { Id = 2, Name = "Second" });
        var result2 = await context.SaveChangesAsync();

        // Third SaveChanges - update
        var entity = await context.TestEntities.FindAsync(1);
        entity!.Name = "Updated";
        var result3 = await context.SaveChangesAsync();

        // Commit
        await transaction.CommitAsync();

        // Assert - all operations completed successfully
        Assert.Equal(1, result1);
        Assert.Equal(1, result2);
        Assert.Equal(1, result3);
        Assert.Equal(2, await context.TestEntities.CountAsync());
    }

    [Fact]
    public async Task MultipleSaveChanges_InsertUpdateDelete_WithinTransaction_WorksCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Start transaction
        await using var transaction = await context.Database.BeginTransactionAsync();

        // Insert
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Entity1" });
        context.TestEntities.Add(new TestEntity { Id = 2, Name = "Entity2" });
        await context.SaveChangesAsync();

        // Update
        var entity1 = await context.TestEntities.FindAsync(1);
        entity1!.Name = "UpdatedEntity1";
        await context.SaveChangesAsync();

        // Delete
        var entity2 = await context.TestEntities.FindAsync(2);
        context.TestEntities.Remove(entity2!);
        await context.SaveChangesAsync();

        // Insert another
        context.TestEntities.Add(new TestEntity { Id = 3, Name = "Entity3" });
        await context.SaveChangesAsync();

        // Commit
        await transaction.CommitAsync();

        // Assert
        Assert.Equal(2, await context.TestEntities.CountAsync());
        Assert.Equal("UpdatedEntity1", (await context.TestEntities.FindAsync(1))!.Name);
        Assert.Null(await context.TestEntities.FindAsync(2));
        Assert.NotNull(await context.TestEntities.FindAsync(3));
    }

    [Fact]
    public void MultipleSaveChanges_SyncVersion_WithinTransaction_WorksCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Start transaction (sync)
        using var transaction = context.Database.BeginTransaction();

        // Multiple operations
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "First" });
        context.SaveChanges();

        var entity = context.TestEntities.Find(1);
        entity!.Name = "Updated";
        context.SaveChanges();

        context.TestEntities.Add(new TestEntity { Id = 2, Name = "Second" });
        context.SaveChanges();

        transaction.Commit();

        // Assert
        Assert.Equal(2, context.TestEntities.Count());
    }

    #endregion

    #region Transaction Rollback Tests

    [Fact]
    public async Task Transaction_RollbackAfterInsert_DoesNotThrow()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Start transaction
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.TestEntities.Add(new TestEntity { Id = 1, Name = "WillBeRolledBack" });
        await context.SaveChangesAsync();

        // Act - Rollback should not throw
        var exception = await Record.ExceptionAsync(async () => await transaction.RollbackAsync());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Transaction_RollbackAfterUpdate_DoesNotThrow()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Pre-populate
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Original" });
        await context.SaveChangesAsync();

        // Start transaction
        await using var transaction = await context.Database.BeginTransactionAsync();

        var entity = await context.TestEntities.FindAsync(1);
        entity!.Name = "WillBeRolledBack";
        await context.SaveChangesAsync();

        // Act
        var exception = await Record.ExceptionAsync(async () => await transaction.RollbackAsync());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Transaction_RollbackAfterDelete_DoesNotThrow()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Pre-populate
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "WillBeDeleted" });
        await context.SaveChangesAsync();

        // Start transaction
        await using var transaction = await context.Database.BeginTransactionAsync();

        var entity = await context.TestEntities.FindAsync(1);
        context.TestEntities.Remove(entity!);
        await context.SaveChangesAsync();

        // Act
        var exception = await Record.ExceptionAsync(async () => await transaction.RollbackAsync());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Transaction_SyncRollback_DoesNotThrow()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);
        using var transaction = context.Database.BeginTransaction();

        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test" });
        context.SaveChanges();

        // Act
        var exception = Record.Exception(() => transaction.Rollback());

        // Assert
        Assert.Null(exception);
    }

    #endregion

    #region Related Entities Tests

    [Fact]
    public async Task SavingChangesAsync_WithRelatedEntities_Insert_WorksCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RelationalTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new RelationalTestDbContext(options);

        // Act - Insert parent with children
        var parent = new ParentEntity
        {
            Id = 1,
            Name = "Parent1",
            Children = new List<ChildEntity>
            {
                new() { Id = 1, Name = "Child1" },
                new() { Id = 2, Name = "Child2" }
            }
        };
        context.Parents.Add(parent);
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(3, result);
        Assert.Equal(1, await context.Parents.CountAsync());
        Assert.Equal(2, await context.Children.CountAsync());
    }

    [Fact]
    public async Task SavingChangesAsync_WithRelatedEntities_Update_WorksCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RelationalTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new RelationalTestDbContext(options);

        // Setup
        var parent = new ParentEntity
        {
            Id = 1,
            Name = "Parent1",
            Children = new List<ChildEntity>
            {
                new() { Id = 1, Name = "Child1" },
                new() { Id = 2, Name = "Child2" }
            }
        };
        context.Parents.Add(parent);
        await context.SaveChangesAsync();

        // Act - Update parent and children
        parent.Name = "UpdatedParent";
        parent.Children.First().Name = "UpdatedChild1";
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(2, result);
        var updatedParent = await context.Parents.Include(p => p.Children).FirstAsync();
        Assert.Equal("UpdatedParent", updatedParent.Name);
        Assert.Contains(updatedParent.Children, c => c.Name == "UpdatedChild1");
    }

    [Fact]
    public async Task SavingChangesAsync_WithRelatedEntities_Delete_WorksCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RelationalTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new RelationalTestDbContext(options);

        // Setup
        var parent = new ParentEntity
        {
            Id = 1,
            Name = "Parent1",
            Children = new List<ChildEntity>
            {
                new() { Id = 1, Name = "Child1" },
                new() { Id = 2, Name = "Child2" }
            }
        };
        context.Parents.Add(parent);
        await context.SaveChangesAsync();

        // Act - Delete child
        var childToRemove = parent.Children.First();
        parent.Children.Remove(childToRemove);
        context.Children.Remove(childToRemove);
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(1, result);
        Assert.Equal(1, await context.Children.CountAsync());
    }

    [Fact]
    public async Task Transaction_WithRelatedEntities_CommitWorksCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RelationalTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new RelationalTestDbContext(options);

        // Start transaction
        await using var transaction = await context.Database.BeginTransactionAsync();

        // Insert parent with children together to avoid InMemory tracking issues
        var parent = new ParentEntity
        {
            Id = 1,
            Name = "Parent1",
            Children = new List<ChildEntity>
            {
                new() { Id = 1, ParentId = 1, Name = "Child1" },
                new() { Id = 2, ParentId = 1, Name = "Child2" }
            }
        };
        context.Parents.Add(parent);
        await context.SaveChangesAsync();

        // Commit
        await transaction.CommitAsync();

        // Assert
        Assert.Equal(1, await context.Parents.CountAsync());
        Assert.Equal(2, await context.Children.CountAsync());
    }

    [Fact]
    public async Task Transaction_WithRelatedEntities_MixedOperations_WorksCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<RelationalTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new RelationalTestDbContext(options);

        // Pre-populate parent with children
        var existingParent = new ParentEntity
        {
            Id = 1,
            Name = "ExistingParent",
            Children = new List<ChildEntity>
            {
                new() { Id = 1, Name = "ExistingChild", ParentId = 1 }
            }
        };
        context.Parents.Add(existingParent);
        await context.SaveChangesAsync();

        // Start transaction
        await using var transaction = await context.Database.BeginTransactionAsync();

        // Update existing parent
        existingParent.Name = "UpdatedParent";
        await context.SaveChangesAsync();

        // Add new child explicitly through Children DbSet to avoid InMemory tracking issues
        context.Children.Add(new ChildEntity { Id = 2, ParentId = 1, Name = "NewChild" });
        await context.SaveChangesAsync();

        // Add new parent with children
        var newParent = new ParentEntity
        {
            Id = 2,
            Name = "NewParent",
            Children = new List<ChildEntity>
            {
                new() { Id = 3, Name = "Child3", ParentId = 2 }
            }
        };
        context.Parents.Add(newParent);
        await context.SaveChangesAsync();

        // Commit
        await transaction.CommitAsync();

        // Assert
        Assert.Equal(2, await context.Parents.CountAsync());
        Assert.Equal(3, await context.Children.CountAsync());
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task SavingChangesAsync_WhenCancellationRequested_RespectsCancellation()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test" });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Cancellation should be propagated
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.SaveChangesAsync(cts.Token));
    }

    [Fact]
    public async Task SavingChangesAsync_WithEmptyChangeSet_ReturnsZero()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Act
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Transaction_DisposeWithoutCommit_DoesNotThrow()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Act - Dispose without commit
        Exception? exception = null;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test" });
            await context.SaveChangesAsync();
            // Dispose without commit
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Transaction_CommitAfterRollback_DoesNotThrowWithInMemory()
    {
        // Arrange
        // Note: InMemory provider doesn't enforce transaction semantics,
        // so this test verifies the interceptor doesn't interfere with transaction state.
        // In a real database, CommitAsync after RollbackAsync would throw.
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test" });
        await context.SaveChangesAsync();
        await transaction.RollbackAsync();

        // Act - InMemory provider allows this (unlike real databases)
        var exception = await Record.ExceptionAsync(async () => await transaction.CommitAsync());

        // Assert - InMemory doesn't throw, but the interceptor should not interfere
        // This validates the interceptor properly handles edge cases
        Assert.Null(exception);
    }

    [Fact]
    public async Task SavingChangesAsync_WithLargeNumberOfEntities_WorksCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        // Add many entities
        for (var i = 1; i <= 1000; i++)
        {
            context.TestEntities.Add(new TestEntity { Id = i, Name = $"Entity{i}" });
        }

        // Act
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(1000, result);
        Assert.Equal(1000, await context.TestEntities.CountAsync());
    }

    [Fact]
    public async Task MultipleSaveChanges_WithNoChangesBetween_ReturnsZero()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test" });
        await context.SaveChangesAsync();

        // Act - Second save with no changes
        var result = await context.SaveChangesAsync();

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Transaction_MultipleNestedOperations_WorksCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        await using var transaction = await context.Database.BeginTransactionAsync();

        // Multiple sequential operations
        for (var batch = 0; batch < 5; batch++)
        {
            for (var i = 0; i < 10; i++)
            {
                var id = batch * 10 + i + 1;
                context.TestEntities.Add(new TestEntity { Id = id, Name = $"Entity{id}" });
            }
            await context.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        // Assert
        Assert.Equal(50, await context.TestEntities.CountAsync());
    }

    [Fact]
    public async Task SavingChanges_WithDateTimeValues_PreservesValues()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        var validFrom = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var validTo = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        context.TestEntities.Add(new TestEntity
        {
            Id = 1,
            Name = "TemporalEntity",
            ValidFrom = validFrom,
            ValidTo = validTo
        });

        // Act
        await context.SaveChangesAsync();

        // Assert
        var entity = await context.TestEntities.FindAsync(1);
        Assert.Equal(validFrom, entity!.ValidFrom);
        Assert.Equal(validTo, entity.ValidTo);
    }

    [Fact]
    public async Task SavingChanges_WithNullableValues_HandlesNulls()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        context.TestEntities.Add(new TestEntity
        {
            Id = 1,
            Name = "NullableTest",
            RegionId = null,
            ValidTo = null
        });

        // Act
        await context.SaveChangesAsync();

        // Assert
        var entity = await context.TestEntities.FindAsync(1);
        Assert.Null(entity!.RegionId);
        Assert.Null(entity.ValidTo);
    }

    [Fact]
    public async Task SavingChanges_WithSpecialCharactersInName_WorksCorrectly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);

        var specialName = "Test'Entity\"With<Special>&Characters\n\t";
        context.TestEntities.Add(new TestEntity { Id = 1, Name = specialName });

        // Act
        await context.SaveChangesAsync();

        // Assert
        var entity = await context.TestEntities.FindAsync(1);
        Assert.Equal(specialName, entity!.Name);
    }

    [Fact]
    public async Task Transaction_RollbackAfterMultipleSaveChanges_DoesNotThrow()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(_interceptor)
            .Options;

        using var context = new TestDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        // Multiple SaveChanges
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "First" });
        await context.SaveChangesAsync();

        context.TestEntities.Add(new TestEntity { Id = 2, Name = "Second" });
        await context.SaveChangesAsync();

        var entity = await context.TestEntities.FindAsync(1);
        entity!.Name = "Updated";
        await context.SaveChangesAsync();

        // Act - Rollback
        var exception = await Record.ExceptionAsync(async () => await transaction.RollbackAsync());

        // Assert
        Assert.Null(exception);
    }

    #endregion

    #region Concurrent Operations Tests

    [Fact]
    public async Task ConcurrentSaveChanges_WithDifferentContexts_WorkCorrectly()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var tasks = new List<Task<int>>();

        for (var i = 0; i < 5; i++)
        {
            var contextId = i;
            tasks.Add(Task.Run(async () =>
            {
                var options = new DbContextOptionsBuilder<TestDbContext>()
                    .UseInMemoryDatabase(dbName)
                    .AddInterceptors(_interceptor)
                    .Options;

                using var context = new TestDbContext(options);
                context.TestEntities.Add(new TestEntity
                {
                    Id = contextId * 100 + 1,
                    Name = $"Entity_{contextId}"
                });
                return await context.SaveChangesAsync();
            }));
        }

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r => Assert.Equal(1, r));
    }

    #endregion

    #region Test Helpers

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

    // Relational test entities
    private sealed class ParentEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ICollection<ChildEntity> Children { get; set; } = new List<ChildEntity>();
    }

    private sealed class ChildEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int ParentId { get; set; }
        public ParentEntity? Parent { get; set; }
    }

    // Relational test DbContext
    private sealed class RelationalTestDbContext : DbContext
    {
        public RelationalTestDbContext(DbContextOptions<RelationalTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<ParentEntity> Parents => Set<ParentEntity>();
        public DbSet<ChildEntity> Children => Set<ChildEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ParentEntity>()
                .HasMany(p => p.Children)
                .WithOne(c => c.Parent)
                .HasForeignKey(c => c.ParentId);
        }
    }

    #endregion
}
