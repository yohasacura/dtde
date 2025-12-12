# DTDE Development Plan - Configuration & API

[← Back to Update Engine](05-update-engine.md) | [Next: Testing Strategy →](07-testing-strategy.md)

---

## 1. Developer Experience Goals

The DTDE API is designed with these principles:

1. **Familiar Patterns**: Use standard EF Core conventions wherever possible
2. **Progressive Complexity**: Simple use cases require minimal configuration
3. **Property Agnostic**: No assumptions about field names (sharding or temporal)
4. **Sharding First**: Sharding is the primary feature; temporal is opt-in
5. **Discoverable**: IntelliSense-friendly with XML documentation
6. **Fail Fast**: Validate configuration at startup, not runtime

---

## 2. Complete Usage Examples

### 2.1 Sharding Only (No Temporal)

The most common use case - transparent sharding with standard EF Core behavior:

```csharp
namespace MyApp.Domain;

/// <summary>
/// A regular entity with no temporal properties.
/// Sharded by region for performance.
/// </summary>
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty; // Shard key
    public DateTime CreatedAt { get; set; }
}
```

```csharp
namespace MyApp.Data;

public class AppDbContext : DtdeDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Simple property-based sharding
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.Id);
            
            // Shard by Region (table sharding)
            entity.ShardBy(c => c.Region)
                  .WithStorageMode(ShardStorageMode.Tables);
            // Creates: Customers_EU, Customers_US, Customers_APAC
        });
    }
}
```

### 2.2 Entity Definition with Temporal

```csharp
namespace MyApp.Domain;

/// <summary>
/// A contract with temporal validity.
/// Property names are domain-specific, not DTDE-specific.
/// </summary>
public class Contract
{
    public int Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    
    // Temporal properties - can be any DateTime property names
    public DateTime EffectiveDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    
    // Navigation
    public List<ContractLine> Lines { get; set; } = new();
}

public class ContractLine
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal LineAmount { get; set; }
    
    // Child validity (must be within parent validity)
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    
    public Contract Contract { get; set; } = null!;
}
```

### 2.3 DbContext Configuration (Sharding + Temporal)

```csharp
namespace MyApp.Data;

public class AppDbContext : DtdeDbContext
{
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractLine> ContractLines => Set<ContractLine>();
    
    public AppDbContext(DbContextOptions<AppDbContext> options) 
        : base(options) { }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure Contract with sharding and temporal
        modelBuilder.Entity<Contract>(entity =>
        {
            entity.HasKey(c => c.Id);
            
            // Sharding by year (table sharding)
            entity.ShardByDate(c => c.EffectiveDate, DateInterval.Year)
                  .WithStorageMode(ShardStorageMode.Tables);
            // Creates: Contracts_2023, Contracts_2024, etc.
            
            // Optional: Temporal configuration
            entity.HasTemporalValidity(
                validFrom: c => c.EffectiveDate,
                validTo: c => c.ExpirationDate);
            
            // Standard EF Core configuration
            entity.HasMany(c => c.Lines)
                .WithOne(l => l.Contract)
                .HasForeignKey(l => l.ContractId);
        });
        
        // Configure ContractLine with temporal (inherits parent sharding)
        modelBuilder.Entity<ContractLine>(entity =>
        {
            entity.HasKey(l => l.Id);
            
            // Different property names - fully supported
            entity.HasTemporalValidity(
                validFrom: l => l.StartDate,
                validTo: l => l.EndDate);
        });
    }
}
```

### 2.4 Service Registration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Option 1: Table sharding (same database)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.UseDtde(dtde =>
    {
        dtde.SetMaxParallelShards(10);
        dtde.EnableDiagnostics();
    });
});

// Option 2: Database sharding (multiple databases)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.UseDtde(dtde =>
    {
        dtde.ConfigureEntity<Contract>(entity =>
        {
            entity.ShardByDate(c => c.EffectiveDate, DateInterval.Year)
                  .WithStorageMode(ShardStorageMode.Databases)
                  .AddDatabase("2023", "Server=shard1;Database=Contracts2023;...")
                  .AddDatabase("2024", "Server=shard2;Database=Contracts2024;...");
        });
    });
});

// Option 3: Manual tables (sqlproj scenario)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.UseDtde(dtde =>
    {
        dtde.ConfigureEntity<Contract>(entity =>
        {
            entity.UseManualSharding(config =>
            {
                config.AddTable("dbo.Contracts_2023", c => c.EffectiveDate.Year == 2023);
                config.AddTable("dbo.Contracts_2024", c => c.EffectiveDate.Year == 2024);
                config.MigrationsEnabled = false; // No auto-creation
            });
        });
    });
});
```

### 2.5 Query Examples

```csharp
public class ContractService
{
    private readonly AppDbContext _db;
    
    public ContractService(AppDbContext db)
    {
        _db = db;
    }
    
    // Standard query - DTDE routes to appropriate shards
    public async Task<List<Contract>> GetContractsAsync()
    {
        return await _db.Contracts
            .OrderBy(c => c.ContractNumber)
            .ToListAsync();
        // Queries all shards, merges results
    }
    
    // Query with predicate - DTDE optimizes shard selection
    public async Task<List<Contract>> GetContractsByYearAsync(int year)
    {
        return await _db.Contracts
            .Where(c => c.EffectiveDate.Year == year)
            .ToListAsync();
        // Only queries Contracts_2024 shard (if year == 2024)
    }
    
    // Query contracts valid today (temporal entities)
    public async Task<List<Contract>> GetActiveContractsAsync()
    {
        return await _db.Contracts
            .ValidAt(DateTime.Today)
            .OrderBy(c => c.ContractNumber)
            .ToListAsync();
    }
    
    // Query contracts valid at a specific date
    public async Task<Contract?> GetContractAtDateAsync(int id, DateTime asOfDate)
    {
        return await _db.Contracts
            .ValidAt(asOfDate)
            .FirstOrDefaultAsync(c => c.Id == id);
    }
    
    // Query with pagination (automatic cross-shard pagination)
    public async Task<List<Contract>> GetContractsPageAsync(
        int page, 
        int pageSize, 
        DateTime? asOfDate = null)
    {
        var query = _db.Contracts.AsQueryable();
        
        if (asOfDate.HasValue)
        {
            query = query.ValidAt(asOfDate.Value);
        }
        
        return await query
            .OrderBy(c => c.ContractNumber)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }
    
    // Query all versions of a contract (temporal entities)
    public async Task<List<Contract>> GetContractHistoryAsync(int id)
    {
        return await _db.Contracts
            .AllVersions()
            .Where(c => c.Id == id)
            .OrderBy(c => c.EffectiveDate)
            .ToListAsync();
    }
    
    // Query with includes
    public async Task<Contract?> GetContractWithLinesAsync(int id, DateTime asOfDate)
    {
        return await _db.Contracts
            .ValidAt(asOfDate)
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == id);
    }
    
    // Query valid in a range
    public async Task<List<Contract>> GetContractsInRangeAsync(
        DateTime from, 
        DateTime to)
    {
        return await _db.Contracts
            .ValidBetween(from, to)
            .ToListAsync();
    }
}
```

### 2.6 Write Examples

```csharp
public class ContractService
{
    // Create new contract - routed to correct shard automatically
    public async Task<Contract> CreateContractAsync(CreateContractDto dto)
    {
        var contract = new Contract
        {
            ContractNumber = dto.ContractNumber,
            Amount = dto.Amount,
            CustomerName = dto.CustomerName,
            EffectiveDate = dto.EffectiveDate,
            ExpirationDate = dto.ExpirationDate
        };
        
        _db.Contracts.Add(contract);
        await _db.SaveChangesAsync();
        // DTDE routes to Contracts_2024 (or appropriate shard) based on EffectiveDate
        
        return contract;
    }
    
    // Update contract - standard EF behavior for non-temporal entities
    public async Task<Contract> UpdateContractAsync(int id, UpdateContractDto dto)
    {
        var contract = await _db.Contracts
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException($"Contract {id} not found");
        
        contract.Amount = dto.Amount;
        contract.CustomerName = dto.CustomerName;
        
        await _db.SaveChangesAsync();
        // Standard EF update behavior
        
        return contract;
    }
    
    // Update contract with versioning (temporal entity with opt-in versioning)
    public async Task<Contract> UpdateContractWithVersionAsync(int id, UpdateContractDto dto)
    {
        var contract = await _db.Contracts
            .ValidAt(DateTime.Today)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException($"Contract {id} not found");
        
        contract.Amount = dto.Amount;
        contract.CustomerName = dto.CustomerName;
        
        await _db.SaveChangesWithVersioningAsync();
        // Creates new version: old ExpirationDate = now, new EffectiveDate = now
        
        return contract;
    }
    
    // Delete contract - standard EF behavior
    public async Task DeleteContractAsync(int id)
    {
        var contract = await _db.Contracts
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException($"Contract {id} not found");
        
        _db.Contracts.Remove(contract);
        await _db.SaveChangesAsync();
        // Standard hard delete
    }
}
```

---

## 3. Shard Configuration

### 3.1 JSON Configuration Format (Database Sharding)

```json
{
  "$schema": "https://dtde.io/schemas/shards.json",
  "storageMode": "Databases",
  "shards": [
    {
      "id": "Shard2023Archive",
      "name": "2023 Archive",
      "connectionString": "Server=archive;Database=Contracts2023;...",
      "predicate": "EffectiveDate.Year == 2023",
      "isReadOnly": true,
      "priority": 100
    },
    {
      "id": "Shard2024",
      "name": "2024 Data",
      "connectionString": "Server=primary;Database=Contracts2024;...",
      "predicate": "EffectiveDate.Year == 2024",
      "priority": 1
    }
  ],
  "defaults": {
    "maxParallelShards": 10,
    "connectionTimeout": "00:00:30",
    "queryTimeout": "00:01:00"
  }
}
```

### 3.2 Table Sharding Configuration (Same Database)

```json
{
  "$schema": "https://dtde.io/schemas/shards.json",
  "storageMode": "Tables",
  "entities": {
    "Customer": {
      "shardKey": "Region",
      "strategy": "PropertyValue",
      "tablePattern": "Customers_{ShardKey}"
    },
    "Order": {
      "shardKey": "OrderDate",
      "strategy": "DateRange",
      "interval": "Year",
      "tablePattern": "Orders_{Year}"
    }
  }
}
```

### 3.3 Manual Table Configuration (sqlproj)

```json
{
  "$schema": "https://dtde.io/schemas/shards.json",
  "storageMode": "Manual",
  "migrationsEnabled": false,
  "entities": {
    "Contract": {
      "tables": [
        { "name": "dbo.Contracts_2022", "predicate": "EffectiveDate.Year == 2022" },
        { "name": "dbo.Contracts_2023", "predicate": "EffectiveDate.Year == 2023" },
        { "name": "dbo.Contracts_2024", "predicate": "EffectiveDate.Year == 2024" }
      ]
    }
  }
}
```

### 3.4 Configuration Classes

```csharp
namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Root configuration for DTDE.
/// </summary>
public sealed class DtdeConfiguration
{
    /// <summary>
    /// Gets or sets the default storage mode.
    /// </summary>
    public ShardStorageMode StorageMode { get; set; } = ShardStorageMode.Tables;
    
    /// <summary>
    /// Gets or sets whether migrations are enabled.
    /// </summary>
    public bool MigrationsEnabled { get; set; } = true;
    
    /// <summary>
    /// Gets or sets the shard configurations (for Database mode).
    /// </summary>
    public List<ShardConfig> Shards { get; set; } = new();
    
    /// <summary>
    /// Gets or sets entity-specific configurations.
    /// </summary>
    public Dictionary<string, EntityShardConfig> Entities { get; set; } = new();
    
    /// <summary>
    /// Gets or sets default settings.
    /// </summary>
    public DefaultSettings Defaults { get; set; } = new();
}

/// <summary>
/// Configuration for a single shard.
/// </summary>
public sealed class ShardConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ConnectionString { get; set; }
    public string? TableName { get; set; }
    public string? Predicate { get; set; }
    public bool IsReadOnly { get; set; }
    public int Priority { get; set; } = 100;
}

/// <summary>
/// Entity-specific sharding configuration.
/// </summary>
public sealed class EntityShardConfig
{
    public string ShardKey { get; set; } = string.Empty;
    public string Strategy { get; set; } = "PropertyValue";
    public string? TablePattern { get; set; }
    public List<ManualTableConfig>? Tables { get; set; }
}

/// <summary>
/// Manual table configuration (sqlproj scenario).
/// </summary>
public sealed class ManualTableConfig
{
    public string Name { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
}

/// <summary>
/// Default settings.
/// </summary>
public sealed class DefaultSettings
{
    public int MaxParallelShards { get; set; } = 10;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromMinutes(1);
}
```

---

## 4. Fluent API Reference

### 4.1 Entity Configuration Methods

```csharp
namespace Dtde.EntityFramework.Extensions;

public static class EntityTypeBuilderExtensions
{
    /// <summary>
    /// Configures temporal validity for the entity.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="validFrom">Expression selecting the validity start property.</param>
    /// <param name="validTo">Optional expression selecting the validity end property.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Property names are fully configurable. Common patterns include:
    /// <list type="bullet">
    ///   <item><c>ValidFrom/ValidTo</c> - Standard naming</item>
    ///   <item><c>EffectiveDate/ExpirationDate</c> - Business naming</item>
    ///   <item><c>StartDate/EndDate</c> - Calendar naming</item>
    ///   <item><c>CreatedAt</c> (no end) - Open-ended validity</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// // With both properties
    /// entity.HasValidity(e => e.EffectiveDate, e => e.ExpirationDate);
    /// 
    /// // Open-ended (perpetual until explicitly closed)
    /// entity.HasValidity(e => e.StartDate);
    /// </code>
    /// </example>
    public static EntityTypeBuilder<TEntity> HasValidity<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, DateTime>> validFrom,
        Expression<Func<TEntity, DateTime?>>? validTo = null)
        where TEntity : class;
    
    /// <summary>
    /// Configures sharding for the entity.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The shard key type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="shardKey">Expression selecting the shard key property.</param>
    /// <param name="strategy">The sharding strategy (default: DateRange).</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// // Date-based sharding
    /// entity.UseSharding(e => e.TransactionDate, ShardingStrategyType.DateRange);
    /// 
    /// // Hash-based sharding
    /// entity.UseSharding(e => e.CustomerId, ShardingStrategyType.Hash);
    /// 
    /// // Range-based sharding
    /// entity.UseSharding(e => e.AccountNumber, ShardingStrategyType.Range);
    /// </code>
    /// </example>
    public static EntityTypeBuilder<TEntity> UseSharding<TEntity, TKey>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TKey>> shardKey,
        ShardingStrategyType strategy = ShardingStrategyType.DateRange)
        where TEntity : class;
    
    /// <summary>
    /// Configures composite sharding with multiple keys.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="shardKeys">Expressions selecting the shard key properties.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// entity.UseCompositeSharding(
    ///     e => e.Year,
    ///     e => e.RegionId);
    /// </code>
    /// </example>
    public static EntityTypeBuilder<TEntity> UseCompositeSharding<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        params Expression<Func<TEntity, object>>[] shardKeys)
        where TEntity : class;
    
    /// <summary>
    /// Configures temporal containment rules for parent-child relationships.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="rule">The containment rule to apply.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// // Child validity must be within parent validity
    /// entity.HasTemporalContainment(TemporalContainmentRule.ChildWithinParent);
    /// </code>
    /// </example>
    public static EntityTypeBuilder<TEntity> HasTemporalContainment<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        TemporalContainmentRule rule)
        where TEntity : class;
}
```

### 4.2 Query Extension Methods

```csharp
namespace Dtde.EntityFramework.Extensions;

public static class QueryableExtensions
{
    /// <summary>
    /// Filters entities to those valid at the specified date.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="date">The date to filter by.</param>
    /// <returns>A queryable filtered to valid entities.</returns>
    /// <remarks>
    /// The filter is applied using the validity properties configured via
    /// <see cref="EntityTypeBuilderExtensions.HasValidity{TEntity}"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var activeContracts = await db.Contracts
    ///     .ValidAt(DateTime.Today)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<TEntity> ValidAt<TEntity>(
        this IQueryable<TEntity> source,
        DateTime date)
        where TEntity : class;
    
    /// <summary>
    /// Includes all historical versions of entities.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <returns>A queryable including all versions.</returns>
    /// <remarks>
    /// When this method is called, temporal filtering is disabled and all
    /// versions of matching entities are returned.
    /// </remarks>
    /// <example>
    /// <code>
    /// var history = await db.Contracts
    ///     .WithVersions()
    ///     .Where(c => c.Id == contractId)
    ///     .OrderBy(c => c.EffectiveDate)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<TEntity> WithVersions<TEntity>(
        this IQueryable<TEntity> source)
        where TEntity : class;
    
    /// <summary>
    /// Filters entities to those valid within the specified date range.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="from">The range start date (inclusive).</param>
    /// <param name="to">The range end date (exclusive).</param>
    /// <returns>A queryable filtered to the date range.</returns>
    /// <remarks>
    /// Returns entities whose validity period intersects with the specified range.
    /// </remarks>
    /// <example>
    /// <code>
    /// var q1Contracts = await db.Contracts
    ///     .ValidBetween(new DateTime(2024, 1, 1), new DateTime(2024, 4, 1))
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<TEntity> ValidBetween<TEntity>(
        this IQueryable<TEntity> source,
        DateTime from,
        DateTime to)
        where TEntity : class;
    
    /// <summary>
    /// Provides a hint for shard routing (advanced usage).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="shardIds">The shard IDs to query.</param>
    /// <returns>A queryable targeting specific shards.</returns>
    /// <remarks>
    /// Use this method only when you have external knowledge about which
    /// shards contain the relevant data. In most cases, let DTDE determine
    /// the shards automatically.
    /// </remarks>
    /// <example>
    /// <code>
    /// var fromArchive = await db.Contracts
    ///     .ShardHint("Shard2023Archive")
    ///     .Where(c => c.Id == legacyId)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static IQueryable<TEntity> ShardHint<TEntity>(
        this IQueryable<TEntity> source,
        params string[] shardIds)
        where TEntity : class;
}
```

### 4.3 DbContext Methods

```csharp
namespace Dtde.EntityFramework.Context;

public abstract class DtdeDbContext : DbContext
{
    /// <summary>
    /// Sets the temporal context for all queries in this DbContext instance.
    /// </summary>
    /// <param name="date">The temporal point to filter by.</param>
    /// <remarks>
    /// When set, all queries on temporal entities will automatically be filtered
    /// to the specified date, unless overridden by <see cref="QueryableExtensions.ValidAt{TEntity}"/>
    /// or <see cref="QueryableExtensions.WithVersions{TEntity}"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// db.SetTemporalContext(DateTime.Today);
    /// var allQueries = await db.Contracts.ToListAsync(); // Auto-filtered to today
    /// </code>
    /// </example>
    public void SetTemporalContext(DateTime date);
    
    /// <summary>
    /// Clears the temporal context, disabling automatic filtering.
    /// </summary>
    public void ClearTemporalContext();
    
    /// <summary>
    /// Enables access to all historical versions (disables temporal filtering).
    /// </summary>
    /// <remarks>
    /// Equivalent to calling <see cref="QueryableExtensions.WithVersions{TEntity}"/>
    /// on each query.
    /// </remarks>
    public void EnableHistoricalAccess();
}
```

---

## 5. Options Builder Reference

```csharp
namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Builder for configuring DTDE options.
/// </summary>
public sealed class DtdeOptionsBuilder
{
    /// <summary>
    /// Adds shards from a JSON configuration file.
    /// </summary>
    /// <param name="configPath">Path to the JSON configuration file.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// options.UseDtde(dtde => dtde.AddShardsFromConfig("shards.json"));
    /// </code>
    /// </example>
    public DtdeOptionsBuilder AddShardsFromConfig(string configPath);
    
    /// <summary>
    /// Adds shards from configuration section.
    /// </summary>
    /// <param name="configuration">The configuration section.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// options.UseDtde(dtde => dtde.AddShardsFromConfiguration(
    ///     builder.Configuration.GetSection("Dtde:Shards")));
    /// </code>
    /// </example>
    public DtdeOptionsBuilder AddShardsFromConfiguration(IConfiguration configuration);
    
    /// <summary>
    /// Adds a single shard with fluent configuration.
    /// </summary>
    /// <param name="configure">Action to configure the shard.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// options.UseDtde(dtde => dtde
    ///     .AddShard(s => s
    ///         .WithId("ShardCurrent")
    ///         .WithConnectionString("Server=...;")
    ///         .WithDateRange(DateTime.Today, DateTime.MaxValue)
    ///         .WithTier(ShardTier.Hot)));
    /// </code>
    /// </example>
    public DtdeOptionsBuilder AddShard(Action<ShardMetadataBuilder> configure);
    
    /// <summary>
    /// Sets the default temporal context provider.
    /// </summary>
    /// <param name="provider">Function returning the default temporal point.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// The provider is called when a query is executed without an explicit
    /// <see cref="QueryableExtensions.ValidAt{TEntity}"/> call and no context-level
    /// temporal point is set.
    /// </remarks>
    /// <example>
    /// <code>
    /// options.UseDtde(dtde => dtde
    ///     .SetDefaultTemporalContext(() => DateTime.UtcNow.Date));
    /// </code>
    /// </example>
    public DtdeOptionsBuilder SetDefaultTemporalContext(Func<DateTime> provider);
    
    /// <summary>
    /// Sets the maximum number of shards to query in parallel.
    /// </summary>
    /// <param name="maxParallel">Maximum parallel shard queries (default: 10).</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder SetMaxParallelShards(int maxParallel);
    
    /// <summary>
    /// Sets the shard connection timeout.
    /// </summary>
    /// <param name="timeout">The connection timeout.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder SetConnectionTimeout(TimeSpan timeout);
    
    /// <summary>
    /// Sets the shard query timeout.
    /// </summary>
    /// <param name="timeout">The query timeout.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder SetQueryTimeout(TimeSpan timeout);
    
    /// <summary>
    /// Enables diagnostic logging and events.
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder EnableDiagnostics();
    
    /// <summary>
    /// Enables test mode (single shard, no distribution).
    /// </summary>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// In test mode, all data is stored in a single database. This simplifies
    /// testing and debugging without changing application code.
    /// </remarks>
    public DtdeOptionsBuilder EnableTestMode();
    
    /// <summary>
    /// Configures write consistency behavior.
    /// </summary>
    /// <param name="failOnPartial">If true, throws when any shard write fails.</param>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder SetWriteConsistency(bool failOnPartial = false);
    
    /// <summary>
    /// Registers a custom sharding strategy.
    /// </summary>
    /// <typeparam name="TStrategy">The strategy type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    public DtdeOptionsBuilder RegisterShardingStrategy<TStrategy>()
        where TStrategy : class, IShardingStrategy;
}
```

### 5.1 Shard Metadata Builder

```csharp
namespace Dtde.EntityFramework.Configuration;

/// <summary>
/// Builder for shard metadata.
/// </summary>
public sealed class ShardMetadataBuilder
{
    /// <summary>
    /// Sets the shard identifier.
    /// </summary>
    public ShardMetadataBuilder WithId(string shardId);
    
    /// <summary>
    /// Sets the shard display name.
    /// </summary>
    public ShardMetadataBuilder WithName(string name);
    
    /// <summary>
    /// Sets the connection string.
    /// </summary>
    public ShardMetadataBuilder WithConnectionString(string connectionString);
    
    /// <summary>
    /// Sets the date range this shard covers.
    /// </summary>
    public ShardMetadataBuilder WithDateRange(DateTime start, DateTime end);
    
    /// <summary>
    /// Sets the key range this shard covers.
    /// </summary>
    public ShardMetadataBuilder WithKeyRange<TKey>(TKey min, TKey max) where TKey : IComparable<TKey>;
    
    /// <summary>
    /// Sets the shard tier.
    /// </summary>
    public ShardMetadataBuilder WithTier(ShardTier tier);
    
    /// <summary>
    /// Marks the shard as read-only.
    /// </summary>
    public ShardMetadataBuilder AsReadOnly();
    
    /// <summary>
    /// Sets the query priority (lower = higher priority).
    /// </summary>
    public ShardMetadataBuilder WithPriority(int priority);
}
```

---

## 6. Diagnostics API

### 6.1 Diagnostic Events

```csharp
namespace Dtde.EntityFramework.Diagnostics;

/// <summary>
/// Interface for observing DTDE events.
/// </summary>
public interface IDtdeEventObserver
{
    /// <summary>
    /// Called when a query is executed across shards.
    /// </summary>
    void OnQueryExecuted(QueryExecutedEvent @event);
    
    /// <summary>
    /// Called when shards are resolved for a query.
    /// </summary>
    void OnShardResolved(ShardResolvedEvent @event);
    
    /// <summary>
    /// Called when a new version is created.
    /// </summary>
    void OnVersionCreated(VersionCreatedEvent @event);
    
    /// <summary>
    /// Called when an existing version is invalidated.
    /// </summary>
    void OnVersionInvalidated(VersionInvalidatedEvent @event);
}

/// <summary>
/// Extension methods for diagnostic subscription.
/// </summary>
public static class DiagnosticsExtensions
{
    /// <summary>
    /// Subscribes to DTDE diagnostic events.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="observer">The event observer.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDtdeDiagnostics(new MyDiagnosticsObserver());
    /// </code>
    /// </example>
    public static IServiceCollection AddDtdeDiagnostics(
        this IServiceCollection services,
        IDtdeEventObserver observer);
    
    /// <summary>
    /// Subscribes to DTDE diagnostic events with logging.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDtdeLoggingDiagnostics(
        this IServiceCollection services);
}
```

### 6.2 Logging Integration

```csharp
// Automatic logging when EnableDiagnostics() is called
// Log output example:

// [DTDE] Query plan created: 3 shards, GlobalOrder=true, GlobalPage=true, Duration=5ms
// [DTDE] [abc12345] Executing query across 3 shards
// [DTDE] [abc12345] Shard Shard2024Q1 returned 150 rows in 45ms
// [DTDE] [abc12345] Shard Shard2024Q2 returned 200 rows in 38ms
// [DTDE] [abc12345] Shard ShardCurrent returned 50 rows in 22ms
// [DTDE] [abc12345] Query completed: 10 results (after pagination), 95ms total
```

---

## 7. Error Handling

### 7.1 Exception Hierarchy

```csharp
namespace Dtde.Core.Exceptions;

// Base exception
DtdeException
├── MetadataConfigurationException  // Configuration errors at startup
├── ShardNotFoundException          // Cannot find target shard
├── ShardOperationException         // Shard query/write failure
├── TemporalValidityException       // Validity constraint violations
└── VersionConflictException        // Concurrent modification conflicts
```

### 7.2 Error Handling Example

```csharp
try
{
    await _db.SaveChangesAsync();
}
catch (ShardOperationException ex)
{
    _logger.LogError(ex, "Failed to save to shard {ShardId}", ex.ShardId);
    // Handle partial failure
}
catch (VersionConflictException ex)
{
    _logger.LogWarning(ex, "Version conflict for entity {EntityType}", ex.EntityType.Name);
    // Reload and retry
}
```

---

## 8. Test Specifications

Following the `MethodName_Condition_ExpectedResult` pattern:

### 8.1 Configuration Tests

```csharp
// AddShardsFromConfig_ValidJson_LoadsAllShards
// AddShardsFromConfig_InvalidPath_ThrowsFileNotFoundException
// AddShard_FluentConfiguration_CreatesCorrectMetadata
// SetDefaultTemporalContext_WithProvider_AppliesToQueries
// EnableTestMode_Called_DisablesSharding
```

### 8.2 Fluent API Tests

```csharp
// HasValidity_BothProperties_StoresConfiguration
// HasValidity_OnlyStart_CreatesOpenEndedValidity
// UseSharding_DateRange_ConfiguresStrategy
// UseCompositeSharding_MultipleKeys_StoresAll
```

### 8.3 Query Extension Tests

```csharp
// ValidAt_WithDate_FiltersCorrectly
// WithVersions_Called_ReturnsAllVersions
// ValidBetween_WithRange_FiltersToRange
// ShardHint_WithIds_TargetsSpecificShards
```

---

## Next Steps

Continue to [07 - Testing Strategy](07-testing-strategy.md) for comprehensive test plan and quality assurance.
