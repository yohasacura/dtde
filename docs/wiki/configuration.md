# Configuration Reference

Complete reference for all DTDE configuration options.

## Table of Contents

- [Service Configuration](#service-configuration)
- [DTDE Options](#dtde-options)
- [Shard Configuration](#shard-configuration)
- [Entity Configuration](#entity-configuration)
- [JSON Configuration](#json-configuration)
- [Environment Variables](#environment-variables)

---

## Service Configuration

### Basic Setup

```csharp
services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseDtde();  // Default configuration
});
```

### Advanced Setup

```csharp
services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseDtde(dtde =>
    {
        // Performance settings
        dtde.SetMaxParallelShards(10);

        // Debugging
        dtde.EnableDiagnostics();

        // Testing
        dtde.EnableTestMode();

        // Default temporal context
        dtde.SetDefaultTemporalContext(() => DateTime.UtcNow);

        // Shard definitions
        dtde.AddShard(s => s.WithId("shard1")...);
        dtde.AddShardsFromConfig("shards.json");
    });
});
```

---

## DTDE Options

### DtdeOptions Class

```csharp
public sealed class DtdeOptions
{
    /// <summary>
    /// Gets the list of configured shards.
    /// </summary>
    public IList<IShardMetadata> Shards { get; }

    /// <summary>
    /// Gets or sets the default temporal context provider.
    /// </summary>
    public Func<DateTime>? DefaultTemporalContextProvider { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of shards to query in parallel.
    /// Default: 10
    /// </summary>
    public int MaxParallelShards { get; set; }

    /// <summary>
    /// Gets or sets whether diagnostics are enabled.
    /// Default: false
    /// </summary>
    public bool EnableDiagnostics { get; set; }

    /// <summary>
    /// Gets or sets whether test mode is enabled.
    /// Default: false
    /// </summary>
    public bool EnableTestMode { get; set; }

    /// <summary>
    /// Gets or sets the metadata registry.
    /// </summary>
    public IMetadataRegistry MetadataRegistry { get; set; }

    /// <summary>
    /// Gets or sets the shard registry.
    /// </summary>
    public IShardRegistry ShardRegistry { get; set; }

    /// <summary>
    /// Gets or sets the temporal context.
    /// </summary>
    public ITemporalContext TemporalContext { get; set; }
}
```

### Option Details

#### MaxParallelShards

Controls how many shards are queried simultaneously.

| Value | Behavior |
|-------|----------|
| 1 | Sequential execution |
| 5-10 | Balanced parallelism (recommended) |
| 20+ | High parallelism (CPU-intensive) |

```csharp
dtde.SetMaxParallelShards(10);
```

**Considerations:**
- Higher values increase memory usage
- Database connection pool limits apply
- CPU cores affect optimal parallelism

#### EnableDiagnostics

Enables detailed logging of DTDE operations.

```csharp
dtde.EnableDiagnostics();
```

**Logged Information:**
- Shard resolution decisions
- Query execution per shard
- Timing information
- Result merge operations

#### EnableTestMode

Disables sharding for testing purposes.

```csharp
dtde.EnableTestMode();
```

**Behavior:**
- All queries go to a single shard
- Simplifies unit testing
- Useful for development

---

## Shard Configuration

### Programmatic Configuration

#### Using Builder Pattern

```csharp
dtde.AddShard(s => s
    .WithId("shard-eu")
    .WithName("European Data")
    .WithShardKeyValue("EU")
    .WithTable("Customers_EU", "dbo")
    .WithTier(ShardTier.Hot)
    .WithPriority(100));
```

#### Using Static Factory Methods

```csharp
// Table shard
var tableShard = ShardMetadata.ForTable(
    shardId: "shard-2024",
    tableName: "Orders_2024",
    shardKeyValue: "2024",
    schemaName: "dbo");

// Database shard
var dbShard = ShardMetadata.ForDatabase(
    shardId: "shard-eu",
    name: "EU Database",
    connectionString: "Server=eu.db.com;...",
    shardKeyValue: "EU");

dtde.AddShard(tableShard);
dtde.AddShard(dbShard);
```

### Shard Properties

| Property | Type | Description | Required |
|----------|------|-------------|----------|
| `ShardId` | string | Unique identifier | Yes |
| `Name` | string | Display name | Yes |
| `StorageMode` | enum | Tables or Databases | Yes |
| `TableName` | string | Table name (table mode) | Conditional |
| `SchemaName` | string | Schema name (default: "dbo") | No |
| `ConnectionString` | string | Connection (database mode) | Conditional |
| `ShardKeyValue` | string | Key value this shard handles | No |
| `DateRange` | DateRange | Date range coverage | No |
| `KeyRange` | KeyRange | Numeric key range | No |
| `Tier` | ShardTier | Storage tier | No |
| `IsReadOnly` | bool | Read-only flag | No |
| `Priority` | int | Query priority (default: 100) | No |

### Storage Tiers

```csharp
public enum ShardTier
{
    Hot,        // Active data, SSD/fast storage
    Warm,       // Less active, standard storage
    Cold,       // Archived, slow storage
    Archive     // Long-term, cheapest storage
}
```

**Use Cases:**

| Tier | Example Use | Storage Recommendation |
|------|-------------|----------------------|
| Hot | Current year data | SSD, premium tier |
| Warm | Last 1-2 years | Standard SSD |
| Cold | 3-5 years old | HDD, standard tier |
| Archive | 5+ years | Archive storage |

---

## Entity Configuration

### Sharding Configuration

#### Property-Based

```csharp
modelBuilder.Entity<Customer>(entity =>
{
    entity.ShardBy(c => c.Region)
          .WithStorageMode(ShardStorageMode.Tables);
});
```

#### Hash-Based

```csharp
modelBuilder.Entity<Customer>(entity =>
{
    entity.ShardByHash(c => c.Id, shardCount: 8)
          .WithStorageMode(ShardStorageMode.Tables);
});
```

#### Date-Based

```csharp
modelBuilder.Entity<Order>(entity =>
{
    entity.ShardByDate(o => o.CreatedAt, DateInterval.Year)
          .WithStorageMode(ShardStorageMode.Tables);
});
```

### Temporal Configuration

#### Basic Temporal

```csharp
entity.HasTemporalValidity(
    validFrom: e => e.EffectiveDate,
    validTo: e => e.ExpirationDate);
```

#### With Versioning Mode

```csharp
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithVersioningMode(VersioningMode.SoftVersion);
```

#### With History Table

```csharp
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithVersioningMode(VersioningMode.AuditTrail)
      .WithHistoryTable("ContractHistory");
```

#### With Open-Ended Value

```csharp
entity.HasTemporalValidity(e => e.ValidFrom, e => e.ValidTo)
      .WithOpenEndedValue(DateTime.MaxValue);
```

---

## JSON Configuration

### Shard Configuration File

Create `shards.json`:

```json
{
  "shards": [
    {
      "shardId": "shard-2024-hot",
      "name": "2024 Active Data",
      "tableName": "Orders_2024",
      "schemaName": "dbo",
      "shardKeyValue": "2024",
      "tier": "Hot",
      "priority": 100,
      "isReadOnly": false,
      "dateRangeStart": "2024-01-01T00:00:00",
      "dateRangeEnd": "2024-12-31T23:59:59"
    },
    {
      "shardId": "shard-2023-warm",
      "name": "2023 Historical Data",
      "tableName": "Orders_2023",
      "schemaName": "dbo",
      "shardKeyValue": "2023",
      "tier": "Warm",
      "priority": 50,
      "isReadOnly": true,
      "dateRangeStart": "2023-01-01T00:00:00",
      "dateRangeEnd": "2023-12-31T23:59:59"
    }
  ]
}
```

### Loading Configuration

```csharp
options.UseDtde(dtde =>
{
    dtde.AddShardsFromConfig("shards.json");
});
```

### Database Sharding JSON

```json
{
  "shards": [
    {
      "shardId": "shard-eu",
      "name": "European Database",
      "connectionString": "Server=eu.sql.com;Database=App;...",
      "shardKeyValue": "EU",
      "tier": "Hot",
      "priority": 100
    },
    {
      "shardId": "shard-us",
      "name": "US Database",
      "connectionString": "Server=us.sql.com;Database=App;...",
      "shardKeyValue": "US",
      "tier": "Hot",
      "priority": 100
    }
  ]
}
```

### JSON Schema Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `shardId` | string | Yes | Unique shard identifier |
| `name` | string | No | Display name |
| `tableName` | string | No* | Table name (table sharding) |
| `schemaName` | string | No | Schema (default: "dbo") |
| `connectionString` | string | No* | Connection (database sharding) |
| `shardKeyValue` | string | No | Key value for routing |
| `tier` | string | No | Hot, Warm, Cold, Archive |
| `priority` | int | No | Query priority (default: 100) |
| `isReadOnly` | bool | No | Read-only flag |
| `dateRangeStart` | datetime | No | Start of date range |
| `dateRangeEnd` | datetime | No | End of date range |

*Either `tableName` or `connectionString` required based on storage mode.

---

## Environment Variables

### Connection String Override

```csharp
var connectionString = Environment.GetEnvironmentVariable("DTDE_CONNECTION_STRING")
    ?? configuration.GetConnectionString("Default");
```

### Shard Configuration Path

```csharp
var shardConfigPath = Environment.GetEnvironmentVariable("DTDE_SHARD_CONFIG")
    ?? "shards.json";

dtde.AddShardsFromConfig(shardConfigPath);
```

### Environment-Specific Configuration

```csharp
// appsettings.Development.json
{
  "Dtde": {
    "MaxParallelShards": 4,
    "EnableDiagnostics": true
  }
}

// appsettings.Production.json
{
  "Dtde": {
    "MaxParallelShards": 20,
    "EnableDiagnostics": false
  }
}
```

```csharp
var dtdeConfig = configuration.GetSection("Dtde");

options.UseDtde(dtde =>
{
    dtde.SetMaxParallelShards(dtdeConfig.GetValue<int>("MaxParallelShards", 10));

    if (dtdeConfig.GetValue<bool>("EnableDiagnostics"))
    {
        dtde.EnableDiagnostics();
    }
});
```

---

## Configuration Validation

### Startup Validation

DTDE validates configuration at startup:

```csharp
// Throws if shard IDs are duplicated
dtde.AddShard(s => s.WithId("shard1")...);
dtde.AddShard(s => s.WithId("shard1")...); // Error!

// Throws if required properties missing
dtde.AddShard(s => s.WithTable("MyTable")); // Error: No ID
```

### Runtime Validation

```csharp
// Check shard exists before query
var shardExists = context.ShardRegistry.GetShard("my-shard") != null;

// Validate entity has sharding configured
var hasSharding = context.MetadataRegistry.GetEntityMetadata<Customer>()
    ?.ShardingConfiguration != null;
```

---

## Complete Example

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.UseDtde(dtde =>
    {
        // Performance
        dtde.SetMaxParallelShards(
            builder.Configuration.GetValue<int>("Dtde:MaxParallelShards", 10));

        // Diagnostics (development only)
        if (builder.Environment.IsDevelopment())
        {
            dtde.EnableDiagnostics();
        }

        // Load shards from config
        var shardConfig = builder.Configuration.GetValue<string>("Dtde:ShardConfigPath");
        if (!string.IsNullOrEmpty(shardConfig))
        {
            dtde.AddShardsFromConfig(shardConfig);
        }

        // Programmatic shards
        dtde.AddShard(s => s
            .WithId("default")
            .WithTable("Entities", "dbo")
            .WithTier(ShardTier.Hot)
            .WithPriority(0));
    });
});
```

---

## Next Steps

- [Classes Reference](classes-reference.md) - Detailed class documentation
- [Troubleshooting](troubleshooting.md) - Common issues and solutions
- [API Reference](api-reference.md) - Complete API documentation

---

[← Back to Wiki](index.md) | [Classes Reference →](classes-reference.md)
