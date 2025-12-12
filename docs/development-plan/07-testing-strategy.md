# DTDE Development Plan - Testing Strategy

[← Back to Configuration & API](06-configuration-api.md) | [Next: Implementation Phases →](08-implementation-phases.md)

---

## 1. Testing Philosophy

Following DDD and .NET best practices from the instructions:

- **Test Naming Convention**: `MethodName_Condition_ExpectedResult()`
- **Minimum Coverage**: 85% for Domain and Application layers
- **Test Categories**: Unit, Integration, Acceptance, Performance

---

## 2. Test Project Structure

```
tests/
├── Dtde.Core.Tests/                    # Domain layer unit tests
│   ├── Metadata/
│   │   ├── EntityMetadataTests.cs
│   │   ├── ValidityConfigurationTests.cs
│   │   ├── ShardMetadataTests.cs
│   │   └── MetadataRegistryTests.cs
│   ├── Sharding/
│   │   ├── DateRangeShardingStrategyTests.cs
│   │   ├── HashShardingStrategyTests.cs
│   │   └── CompositeShardingStrategyTests.cs
│   └── Temporal/
│       └── TemporalContextTests.cs
│
├── Dtde.EntityFramework.Tests/         # EF Core integration unit tests
│   ├── Configuration/
│   │   ├── DtdeOptionsBuilderTests.cs
│   │   └── FluentApiExtensionTests.cs
│   ├── Query/
│   │   ├── ExpressionRewriterTests.cs
│   │   ├── ShardQueryPlannerTests.cs
│   │   ├── QueryExecutorTests.cs
│   │   └── ResultMergerTests.cs
│   └── Update/
│       ├── UpdateProcessorTests.cs
│       ├── VersionManagerTests.cs
│       └── ShardWriteRouterTests.cs
│
├── Dtde.Integration.Tests/             # End-to-end integration tests
│   ├── Fixtures/
│   │   ├── TestDatabaseFixture.cs
│   │   ├── MultiShardFixture.cs
│   │   └── TestEntities.cs
│   ├── Query/
│   │   ├── TemporalQueryIntegrationTests.cs
│   │   ├── ShardedQueryIntegrationTests.cs
│   │   └── PaginationIntegrationTests.cs
│   ├── Update/
│   │   ├── VersionBumpIntegrationTests.cs
│   │   ├── CrossShardUpdateIntegrationTests.cs
│   │   └── ConcurrencyIntegrationTests.cs
│   └── Scenarios/
│       ├── ContractManagementScenarioTests.cs
│       └── HighVolumeScenarioTests.cs
│
└── Dtde.Benchmarks/                    # Performance benchmarks
    ├── QueryBenchmarks.cs
    ├── UpdateBenchmarks.cs
    └── ShardResolutionBenchmarks.cs
```

---

## 3. Unit Test Specifications

### 3.1 Metadata Tests

```csharp
namespace Dtde.Core.Tests.Metadata;

public class ValidityConfigurationTests
{
    [Fact(DisplayName = "ValidityConfiguration with both properties creates correct predicate")]
    public void BuildPredicate_WithBothProperties_CreatesCorrectPredicate()
    {
        // Arrange
        var validFrom = CreatePropertyMetadata<TestEntity>("EffectiveDate");
        var validTo = CreatePropertyMetadata<TestEntity>("ExpirationDate");
        var config = new ValidityConfiguration(validFrom, validTo);
        var targetDate = new DateTime(2024, 6, 15);
        
        // Act
        var predicate = config.BuildPredicate<TestEntity>(targetDate);
        var compiled = predicate.Compile();
        
        // Assert
        var validEntity = new TestEntity 
        { 
            EffectiveDate = new DateTime(2024, 1, 1), 
            ExpirationDate = new DateTime(2024, 12, 31) 
        };
        var invalidEntity = new TestEntity 
        { 
            EffectiveDate = new DateTime(2025, 1, 1), 
            ExpirationDate = new DateTime(2025, 12, 31) 
        };
        
        compiled(validEntity).Should().BeTrue();
        compiled(invalidEntity).Should().BeFalse();
    }
    
    [Fact(DisplayName = "ValidityConfiguration with only start property allows open-ended validity")]
    public void Constructor_WithOnlyStartProperty_AllowsOpenEndedValidity()
    {
        // Arrange
        var validFrom = CreatePropertyMetadata<TestEntity>("EffectiveDate");
        
        // Act
        var config = new ValidityConfiguration(validFrom, validToProperty: null);
        
        // Assert
        config.ValidFromProperty.Should().NotBeNull();
        config.ValidToProperty.Should().BeNull();
        config.IsOpenEnded.Should().BeTrue();
    }
    
    [Fact(DisplayName = "ValidityConfiguration BuildPredicate handles null end date correctly")]
    public void BuildPredicate_WithOpenEnded_HandlesNullEndDate()
    {
        // Arrange
        var validFrom = CreatePropertyMetadata<TestEntity>("EffectiveDate");
        var config = new ValidityConfiguration(validFrom);
        var targetDate = new DateTime(2024, 6, 15);
        
        // Act
        var predicate = config.BuildPredicate<TestEntity>(targetDate);
        var compiled = predicate.Compile();
        
        // Assert
        var entity = new TestEntity { EffectiveDate = new DateTime(2024, 1, 1) };
        compiled(entity).Should().BeTrue();
    }
}

public class MetadataRegistryTests
{
    [Fact(DisplayName = "MetadataRegistry GetEntityMetadata returns configured entity")]
    public void GetEntityMetadata_ConfiguredEntity_ReturnsMetadata()
    {
        // Arrange
        var registry = CreateRegistryWithEntity<TestEntity>();
        
        // Act
        var metadata = registry.GetEntityMetadata<TestEntity>();
        
        // Assert
        metadata.Should().NotBeNull();
        metadata!.ClrType.Should().Be(typeof(TestEntity));
    }
    
    [Fact(DisplayName = "MetadataRegistry GetEntityMetadata returns null for unconfigured entity")]
    public void GetEntityMetadata_UnconfiguredEntity_ReturnsNull()
    {
        // Arrange
        var registry = CreateEmptyRegistry();
        
        // Act
        var metadata = registry.GetEntityMetadata<UnconfiguredEntity>();
        
        // Assert
        metadata.Should().BeNull();
    }
    
    [Fact(DisplayName = "MetadataRegistry Validate fails for missing primary key")]
    public void Validate_MissingPrimaryKey_ReturnsError()
    {
        // Arrange
        var registry = CreateRegistryWithInvalidEntity();
        
        // Act
        var result = registry.Validate();
        
        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("primary key"));
    }
    
    [Fact(DisplayName = "MetadataRegistry Validate fails for overlapping shard ranges")]
    public void Validate_OverlappingShardRanges_ReturnsError()
    {
        // Arrange
        var registry = CreateRegistryWithOverlappingShards();
        
        // Act
        var result = registry.Validate();
        
        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("overlapping"));
    }
}
```

### 3.2 Sharding Strategy Tests

```csharp
namespace Dtde.Core.Tests.Sharding;

public class DateRangeShardingStrategyTests
{
    [Fact(DisplayName = "DateRangeStrategy with temporal context returns intersecting shards")]
    public void ResolveShards_WithTemporalContext_ReturnsIntersectingShards()
    {
        // Arrange
        var strategy = new DateRangeShardingStrategy();
        var registry = CreateShardRegistryWithQuarterlyShards();
        var entity = CreateEntityMetadataWithDateSharding();
        var predicates = new Dictionary<string, object?>();
        var targetDate = new DateTime(2024, 3, 15); // Q1 2024
        
        // Act
        var shards = strategy.ResolveShards(entity, registry, predicates, targetDate);
        
        // Assert
        shards.Should().HaveCount(1);
        shards[0].ShardId.Should().Be("Shard2024Q1");
    }
    
    [Fact(DisplayName = "DateRangeStrategy without temporal context returns all shards")]
    public void ResolveShards_WithoutTemporalContext_ReturnsAllShards()
    {
        // Arrange
        var strategy = new DateRangeShardingStrategy();
        var registry = CreateShardRegistryWithQuarterlyShards();
        var entity = CreateEntityMetadataWithDateSharding();
        var predicates = new Dictionary<string, object?>();
        
        // Act
        var shards = strategy.ResolveShards(entity, registry, predicates, temporalContext: null);
        
        // Assert
        shards.Should().HaveCount(4); // All quarterly shards
    }
    
    [Fact(DisplayName = "DateRangeStrategy write operation returns correct shard")]
    public void ResolveWriteShard_WithEntity_ReturnsCorrectShard()
    {
        // Arrange
        var strategy = new DateRangeShardingStrategy();
        var registry = CreateShardRegistryWithQuarterlyShards();
        var entity = CreateEntityMetadataWithDateSharding();
        var instance = new TestEntity { EffectiveDate = new DateTime(2024, 5, 1) }; // Q2 2024
        
        // Act
        var shard = strategy.ResolveWriteShard(entity, registry, instance);
        
        // Assert
        shard.ShardId.Should().Be("Shard2024Q2");
    }
}

public class HashShardingStrategyTests
{
    [Fact(DisplayName = "HashStrategy with key predicate returns single shard")]
    public void ResolveShards_WithKeyPredicate_ReturnsSingleShard()
    {
        // Arrange
        var strategy = new HashShardingStrategy(numberOfShards: 4);
        var registry = CreateHashedShardRegistry(4);
        var entity = CreateEntityMetadataWithHashSharding();
        var predicates = new Dictionary<string, object?> { ["CustomerId"] = 12345 };
        
        // Act
        var shards = strategy.ResolveShards(entity, registry, predicates, temporalContext: null);
        
        // Assert
        shards.Should().HaveCount(1);
    }
    
    [Fact(DisplayName = "HashStrategy without key predicate returns all shards")]
    public void ResolveShards_WithoutKeyPredicate_ReturnsAllShards()
    {
        // Arrange
        var strategy = new HashShardingStrategy(numberOfShards: 4);
        var registry = CreateHashedShardRegistry(4);
        var entity = CreateEntityMetadataWithHashSharding();
        var predicates = new Dictionary<string, object?>();
        
        // Act
        var shards = strategy.ResolveShards(entity, registry, predicates, temporalContext: null);
        
        // Assert
        shards.Should().HaveCount(4);
    }
}
```

### 3.3 Expression Rewriter Tests

```csharp
namespace Dtde.EntityFramework.Tests.Query;

public class DtdeExpressionRewriterTests
{
    [Fact(DisplayName = "Rewrite with ValidAt injects temporal predicate")]
    public void Rewrite_WithValidAt_InjectsTemporalPredicate()
    {
        // Arrange
        var rewriter = CreateRewriter();
        var query = CreateTestQuery().ValidAt(new DateTime(2024, 6, 15));
        var temporalContext = CreateEmptyTemporalContext();
        
        // Act
        var result = rewriter.Rewrite(query.Expression, temporalContext);
        
        // Assert
        result.TemporalFiltersApplied.Should().BeTrue();
        result.QueryDefinition.EffectiveTemporalPoint.Should().Be(new DateTime(2024, 6, 15));
    }
    
    [Fact(DisplayName = "Rewrite with WithVersions skips temporal filter")]
    public void Rewrite_WithWithVersions_SkipsTemporalFilter()
    {
        // Arrange
        var rewriter = CreateRewriter();
        var query = CreateTestQuery().WithVersions();
        var temporalContext = CreateTemporalContext(new DateTime(2024, 6, 15));
        
        // Act
        var result = rewriter.Rewrite(query.Expression, temporalContext);
        
        // Assert
        result.TemporalFiltersApplied.Should().BeFalse();
        result.QueryDefinition.IncludeHistory.Should().BeTrue();
    }
    
    [Fact(DisplayName = "Rewrite with context temporal point uses context value")]
    public void Rewrite_WithContextTemporalPoint_UsesContextValue()
    {
        // Arrange
        var rewriter = CreateRewriter();
        var query = CreateTestQuery(); // No ValidAt
        var temporalContext = CreateTemporalContext(new DateTime(2024, 6, 15));
        
        // Act
        var result = rewriter.Rewrite(query.Expression, temporalContext);
        
        // Assert
        result.TemporalFiltersApplied.Should().BeTrue();
        result.QueryDefinition.EffectiveTemporalPoint.Should().Be(new DateTime(2024, 6, 15));
    }
    
    [Fact(DisplayName = "Rewrite with query temporal point overrides context")]
    public void Rewrite_WithQueryTemporalPoint_OverridesContext()
    {
        // Arrange
        var rewriter = CreateRewriter();
        var query = CreateTestQuery().ValidAt(new DateTime(2024, 1, 1));
        var temporalContext = CreateTemporalContext(new DateTime(2024, 6, 15));
        
        // Act
        var result = rewriter.Rewrite(query.Expression, temporalContext);
        
        // Assert
        result.QueryDefinition.EffectiveTemporalPoint.Should().Be(new DateTime(2024, 1, 1));
    }
    
    [Fact(DisplayName = "Rewrite for non-temporal entity does not inject predicate")]
    public void Rewrite_NonTemporalEntity_NoPredicateInjected()
    {
        // Arrange
        var rewriter = CreateRewriter();
        var query = CreateNonTemporalQuery();
        var temporalContext = CreateTemporalContext(new DateTime(2024, 6, 15));
        
        // Act
        var result = rewriter.Rewrite(query.Expression, temporalContext);
        
        // Assert
        result.TemporalFiltersApplied.Should().BeFalse();
    }
    
    [Fact(DisplayName = "Rewrite with custom property names uses configured names")]
    public void Rewrite_CustomPropertyNames_UsesConfiguredNames()
    {
        // Arrange
        var rewriter = CreateRewriterWithCustomProperties();
        var query = CreateQueryWithCustomEntity();
        var temporalContext = CreateTemporalContext(new DateTime(2024, 6, 15));
        
        // Act
        var result = rewriter.Rewrite(query.Expression, temporalContext);
        
        // Assert
        // Verify the expression uses "StartDate" and "EndDate" properties
        var expressionString = result.RewrittenExpression.ToString();
        expressionString.Should().Contain("StartDate");
        expressionString.Should().Contain("EndDate");
    }
}
```

### 3.4 Update Processor Tests

```csharp
namespace Dtde.EntityFramework.Tests.Update;

public class DtdeUpdateProcessorTests
{
    [Fact(DisplayName = "ProcessUpdatesAsync for Added entity creates insert command")]
    public void ProcessUpdatesAsync_AddedEntity_CreatesInsertCommand()
    {
        // Arrange
        var processor = CreateProcessor();
        var entry = CreateEntityEntry(EntityState.Added);
        
        // Act
        var result = await processor.ProcessUpdatesAsync(
            CreateMockContext(), 
            new[] { entry }, 
            CancellationToken.None);
        
        // Assert
        result.Should().Be(1);
        // Verify insert command was created
    }
    
    [Fact(DisplayName = "ProcessUpdatesAsync for Modified entity creates version bump commands")]
    public void ProcessUpdatesAsync_ModifiedEntity_CreatesVersionBumpCommands()
    {
        // Arrange
        var processor = CreateProcessor();
        var entry = CreateEntityEntry(EntityState.Modified);
        
        // Act
        var result = await processor.ProcessUpdatesAsync(
            CreateMockContext(), 
            new[] { entry }, 
            CancellationToken.None);
        
        // Assert
        result.Should().Be(2); // Invalidate old + Insert new
    }
    
    [Fact(DisplayName = "ProcessUpdatesAsync for Deleted entity creates close command")]
    public void ProcessUpdatesAsync_DeletedEntity_CreatesCloseCommand()
    {
        // Arrange
        var processor = CreateProcessor();
        var entry = CreateEntityEntry(EntityState.Deleted);
        
        // Act
        var result = await processor.ProcessUpdatesAsync(
            CreateMockContext(), 
            new[] { entry }, 
            CancellationToken.None);
        
        // Assert
        result.Should().Be(1);
        // Verify invalidate (close) command was created
    }
}
```

---

## 4. Integration Test Specifications

### 4.1 Test Database Fixture

```csharp
namespace Dtde.Integration.Tests.Fixtures;

public class MultiShardFixture : IAsyncLifetime
{
    private readonly List<SqlConnection> _connections = new();
    
    public string Shard1ConnectionString { get; private set; } = null!;
    public string Shard2ConnectionString { get; private set; } = null!;
    public string Shard3ConnectionString { get; private set; } = null!;
    
    public async Task InitializeAsync()
    {
        // Create test databases for each shard
        Shard1ConnectionString = await CreateTestDatabaseAsync("DtdeTest_Shard1");
        Shard2ConnectionString = await CreateTestDatabaseAsync("DtdeTest_Shard2");
        Shard3ConnectionString = await CreateTestDatabaseAsync("DtdeTest_Shard3");
        
        // Apply migrations to each shard
        await ApplyMigrationsAsync(Shard1ConnectionString);
        await ApplyMigrationsAsync(Shard2ConnectionString);
        await ApplyMigrationsAsync(Shard3ConnectionString);
    }
    
    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }
        
        // Drop test databases
        await DropTestDatabaseAsync("DtdeTest_Shard1");
        await DropTestDatabaseAsync("DtdeTest_Shard2");
        await DropTestDatabaseAsync("DtdeTest_Shard3");
    }
    
    public TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(Shard1ConnectionString) // Default
            .UseDtde(dtde =>
            {
                dtde.AddShard(s => s
                    .WithId("Shard1")
                    .WithConnectionString(Shard1ConnectionString)
                    .WithDateRange(new DateTime(2023, 1, 1), new DateTime(2024, 1, 1)));
                
                dtde.AddShard(s => s
                    .WithId("Shard2")
                    .WithConnectionString(Shard2ConnectionString)
                    .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2025, 1, 1)));
                
                dtde.AddShard(s => s
                    .WithId("Shard3")
                    .WithConnectionString(Shard3ConnectionString)
                    .WithDateRange(new DateTime(2025, 1, 1), new DateTime(2100, 1, 1)));
            })
            .Options;
        
        return new TestDbContext(options);
    }
}
```

### 4.2 Temporal Query Integration Tests

```csharp
namespace Dtde.Integration.Tests.Query;

public class TemporalQueryIntegrationTests : IClassFixture<MultiShardFixture>
{
    private readonly MultiShardFixture _fixture;
    
    public TemporalQueryIntegrationTests(MultiShardFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact(DisplayName = "ValidAt query returns only valid entities")]
    public async Task ValidAt_Query_ReturnsOnlyValidEntities()
    {
        // Arrange
        await using var context = _fixture.CreateContext();
        await SeedTestDataAsync(context);
        var targetDate = new DateTime(2024, 6, 15);
        
        // Act
        var results = await context.Contracts
            .ValidAt(targetDate)
            .ToListAsync();
        
        // Assert
        results.Should().AllSatisfy(c =>
        {
            c.EffectiveDate.Should().BeLessThanOrEqualTo(targetDate);
            c.ExpirationDate.Should().BeGreaterThan(targetDate);
        });
    }
    
    [Fact(DisplayName = "ValidAt query resolves correct shards")]
    public async Task ValidAt_Query_ResolvesCorrectShards()
    {
        // Arrange
        await using var context = _fixture.CreateContext();
        await SeedTestDataAsync(context);
        var targetDate = new DateTime(2024, 6, 15);
        
        // Act & Assert (verify through diagnostics)
        var diagnosticEvents = new List<ShardResolvedEvent>();
        context.SubscribeToDiagnostics(e => diagnosticEvents.Add(e));
        
        await context.Contracts.ValidAt(targetDate).ToListAsync();
        
        diagnosticEvents.Should().HaveCount(1);
        diagnosticEvents[0].ShardIds.Should().Contain("Shard2");
    }
    
    [Fact(DisplayName = "WithVersions returns all versions")]
    public async Task WithVersions_ReturnsAllVersions()
    {
        // Arrange
        await using var context = _fixture.CreateContext();
        var contractId = await SeedVersionedContractAsync(context, versionCount: 3);
        
        // Act
        var versions = await context.Contracts
            .WithVersions()
            .Where(c => c.Id == contractId)
            .OrderBy(c => c.EffectiveDate)
            .ToListAsync();
        
        // Assert
        versions.Should().HaveCount(3);
    }
}
```

### 4.3 Sharded Query Integration Tests

```csharp
namespace Dtde.Integration.Tests.Query;

public class ShardedQueryIntegrationTests : IClassFixture<MultiShardFixture>
{
    [Fact(DisplayName = "Query across multiple shards merges results")]
    public async Task Query_AcrossMultipleShards_MergesResults()
    {
        // Arrange
        await using var context = _fixture.CreateContext();
        await SeedDataAcrossAllShardsAsync(context);
        
        // Act - Query that spans all shards
        var results = await context.Contracts
            .WithVersions()
            .ToListAsync();
        
        // Assert
        results.Should().HaveCountGreaterThan(0);
        // Verify results from all shards are present
    }
    
    [Fact(DisplayName = "Query with pagination applies globally")]
    public async Task Query_WithPagination_AppliesGlobally()
    {
        // Arrange
        await using var context = _fixture.CreateContext();
        await SeedManyContractsAsync(context, count: 100);
        
        // Act
        var page1 = await context.Contracts
            .WithVersions()
            .OrderBy(c => c.ContractNumber)
            .Skip(0).Take(10)
            .ToListAsync();
        
        var page2 = await context.Contracts
            .WithVersions()
            .OrderBy(c => c.ContractNumber)
            .Skip(10).Take(10)
            .ToListAsync();
        
        // Assert
        page1.Should().HaveCount(10);
        page2.Should().HaveCount(10);
        page1.Should().NotIntersectWith(page2);
    }
    
    [Fact(DisplayName = "Query with ordering sorts globally")]
    public async Task Query_WithOrdering_SortsGlobally()
    {
        // Arrange
        await using var context = _fixture.CreateContext();
        await SeedDataAcrossAllShardsAsync(context);
        
        // Act
        var results = await context.Contracts
            .WithVersions()
            .OrderBy(c => c.ContractNumber)
            .ToListAsync();
        
        // Assert
        results.Should().BeInAscendingOrder(c => c.ContractNumber);
    }
}
```

### 4.4 Version Bump Integration Tests

```csharp
namespace Dtde.Integration.Tests.Update;

public class VersionBumpIntegrationTests : IClassFixture<MultiShardFixture>
{
    [Fact(DisplayName = "Update creates new version and closes old")]
    public async Task Update_CreatesNewVersion_ClosesOld()
    {
        // Arrange
        await using var context = _fixture.CreateContext();
        var contract = await SeedSingleContractAsync(context);
        var originalEffectiveDate = contract.EffectiveDate;
        
        // Act
        contract.Amount = 999.99m;
        await context.SaveChangesAsync();
        
        // Assert
        var versions = await context.Contracts
            .WithVersions()
            .Where(c => c.Id == contract.Id)
            .OrderBy(c => c.EffectiveDate)
            .ToListAsync();
        
        versions.Should().HaveCount(2);
        versions[0].ExpirationDate.Should().BeLessThan(DateTime.MaxValue);
        versions[1].Amount.Should().Be(999.99m);
    }
    
    [Fact(DisplayName = "Delete closes validity period")]
    public async Task Delete_ClosesValidityPeriod()
    {
        // Arrange
        await using var context = _fixture.CreateContext();
        var contract = await SeedSingleContractAsync(context);
        
        // Act
        context.Contracts.Remove(contract);
        await context.SaveChangesAsync();
        
        // Assert - Should not be valid today
        var activeContract = await context.Contracts
            .ValidAt(DateTime.Today)
            .FirstOrDefaultAsync(c => c.Id == contract.Id);
        
        activeContract.Should().BeNull();
        
        // But should be in history
        var historicalContract = await context.Contracts
            .WithVersions()
            .FirstOrDefaultAsync(c => c.Id == contract.Id);
        
        historicalContract.Should().NotBeNull();
        historicalContract!.ExpirationDate.Should().BeLessThanOrEqualTo(DateTime.UtcNow);
    }
}
```

---

## 5. Performance Benchmarks

### 5.1 Benchmark Configuration

```csharp
namespace Dtde.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class QueryBenchmarks
{
    private MultiShardFixture _fixture = null!;
    private TestDbContext _context = null!;
    
    [GlobalSetup]
    public async Task Setup()
    {
        _fixture = new MultiShardFixture();
        await _fixture.InitializeAsync();
        _context = _fixture.CreateContext();
        
        // Seed 1M rows across shards
        await SeedLargeDatasetAsync(_context, rowCount: 1_000_000);
    }
    
    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _context.DisposeAsync();
        await _fixture.DisposeAsync();
    }
    
    [Benchmark(Description = "ValidAt query - single shard")]
    public async Task<List<Contract>> ValidAt_SingleShard()
    {
        return await _context.Contracts
            .ValidAt(new DateTime(2024, 6, 15))
            .Take(100)
            .ToListAsync();
    }
    
    [Benchmark(Description = "ValidAt query - multiple shards")]
    public async Task<List<Contract>> ValidAt_MultipleShards()
    {
        return await _context.Contracts
            .ValidBetween(new DateTime(2023, 1, 1), new DateTime(2025, 1, 1))
            .Take(100)
            .ToListAsync();
    }
    
    [Benchmark(Description = "WithVersions query - all shards")]
    public async Task<List<Contract>> WithVersions_AllShards()
    {
        return await _context.Contracts
            .WithVersions()
            .Take(100)
            .ToListAsync();
    }
    
    [Benchmark(Description = "Paginated query - page 10")]
    public async Task<List<Contract>> Paginated_Page10()
    {
        return await _context.Contracts
            .ValidAt(DateTime.Today)
            .OrderBy(c => c.ContractNumber)
            .Skip(90).Take(10)
            .ToListAsync();
    }
}
```

### 5.2 Performance Targets

| Benchmark | Target | Acceptable |
|-----------|--------|------------|
| Single shard ValidAt (100 rows) | < 50ms | < 100ms |
| Multi-shard ValidAt (100 rows) | < 100ms | < 200ms |
| WithVersions all shards (100 rows) | < 150ms | < 300ms |
| Paginated query page 10 | < 100ms | < 200ms |
| Version bump (single entity) | < 50ms | < 100ms |
| Bulk insert (1000 entities) | < 2s | < 5s |

---

## 6. Test Data Generators

```csharp
namespace Dtde.Integration.Tests.Fixtures;

public static class TestDataGenerator
{
    public static IEnumerable<Contract> GenerateContracts(
        int count,
        DateTime startDate,
        DateTime endDate)
    {
        var random = new Random(42); // Deterministic for reproducibility
        
        for (var i = 0; i < count; i++)
        {
            var effectiveDate = startDate.AddDays(random.Next((int)(endDate - startDate).TotalDays));
            var duration = TimeSpan.FromDays(random.Next(30, 365));
            
            yield return new Contract
            {
                ContractNumber = $"CONTRACT-{i:D8}",
                Amount = (decimal)(random.NextDouble() * 10000),
                CustomerName = $"Customer {i}",
                EffectiveDate = effectiveDate,
                ExpirationDate = effectiveDate + duration
            };
        }
    }
    
    public static async Task SeedLargeDatasetAsync(
        TestDbContext context,
        int rowCount)
    {
        var contracts = GenerateContracts(
            rowCount,
            new DateTime(2023, 1, 1),
            new DateTime(2025, 12, 31));
        
        foreach (var batch in contracts.Chunk(1000))
        {
            context.Contracts.AddRange(batch);
            await context.SaveChangesAsync();
        }
    }
}
```

---

## 7. Continuous Integration

### 7.1 Test Pipeline

```yaml
# .github/workflows/test.yml
name: Tests

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet test tests/Dtde.Core.Tests --configuration Release
      - run: dotnet test tests/Dtde.EntityFramework.Tests --configuration Release
  
  integration-tests:
    runs-on: ubuntu-latest
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          SA_PASSWORD: YourStrong@Passw0rd
          ACCEPT_EULA: Y
        ports:
          - 1433:1433
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet test tests/Dtde.Integration.Tests --configuration Release
  
  benchmarks:
    runs-on: ubuntu-latest
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - run: dotnet run --project tests/Dtde.Benchmarks --configuration Release
```

### 7.2 Coverage Requirements

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <CollectCoverage>true</CollectCoverage>
  <CoverletOutputFormat>cobertura</CoverletOutputFormat>
  <Threshold>85</Threshold>
  <ThresholdType>line,branch</ThresholdType>
  <ThresholdStat>total</ThresholdStat>
</PropertyGroup>
```

---

## 8. Quality Gates

| Gate | Threshold | Enforcement |
|------|-----------|-------------|
| Unit Test Coverage | ≥ 85% | CI pipeline failure |
| Integration Test Pass Rate | 100% | CI pipeline failure |
| Benchmark Regression | < 10% slower | PR review warning |
| Code Quality (SonarQube) | 0 critical/high issues | Merge blocked |

---

## Next Steps

Continue to [08 - Implementation Phases](08-implementation-phases.md) for milestone planning and delivery timeline.
