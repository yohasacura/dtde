# DTDE Documentation

Welcome to the **Distributed Temporal Data Engine (DTDE)** documentation!

DTDE is a NuGet package that provides **transparent horizontal sharding** and **optional temporal versioning** for Entity Framework Core.

---

## 📚 Documentation Structure

```
docs/
├── guides/                      # Getting Started & How-To Guides
│   ├── README.md               # Guide index
│   ├── quickstart.md           # 5-minute quickstart
│   ├── getting-started.md      # Complete setup guide
│   ├── sharding-guide.md       # Sharding strategies
│   ├── temporal-guide.md       # Temporal versioning
│   └── migration-guide.md      # Migrating existing apps
│
├── wiki/                        # Reference Documentation
│   ├── README.md               # Wiki index
│   ├── architecture.md         # System architecture
│   ├── api-reference.md        # Complete API reference
│   ├── configuration.md        # Configuration options
│   ├── classes-reference.md    # Class documentation
│   └── troubleshooting.md      # FAQ & troubleshooting
│
└── development-plan/            # Technical Design Documents
    ├── 00-revised-architecture.md
    ├── 01-overview.md
    └── ...
```

---

## 🚀 Quick Links

### Community & Governance

- [Contributing](../CONTRIBUTING.md)
- [Code of Conduct](../CODE_OF_CONDUCT.md)
- [Security Policy](../SECURITY.md)
- [Changelog](../CHANGELOG.md)

### Getting Started

| Guide | Description | Time |
|-------|-------------|------|
| [**Quickstart**](guides/quickstart.md) | Get up and running fast | 5 min |
| [**Getting Started**](guides/getting-started.md) | Complete introduction | 15 min |
| [**Migration Guide**](guides/migration-guide.md) | Migrate existing apps | 10 min |

### Feature Guides

| Guide | Description |
|-------|-------------|
| [**Sharding Guide**](guides/sharding-guide.md) | Property, hash, date, range sharding |
| [**Temporal Guide**](guides/temporal-guide.md) | Point-in-time queries, versioning |

### Reference

| Document | Description |
|----------|-------------|
| [**Architecture**](wiki/architecture.md) | System design & components |
| [**API Reference**](wiki/api-reference.md) | Complete API documentation |
| [**Configuration**](wiki/configuration.md) | All configuration options |
| [**Classes Reference**](wiki/classes-reference.md) | Detailed class docs |
| [**Troubleshooting**](wiki/troubleshooting.md) | FAQ & common issues |

---

## 🎯 What is DTDE?

DTDE makes distributed data look like a single EF Core DbSet:

```csharp
// You write standard EF Core LINQ:
var customers = await db.Customers
    .Where(c => c.Region == "EU")
    .OrderBy(c => c.Name)
    .ToListAsync();

// DTDE transparently:
// ✅ Routes to correct shard(s)
// ✅ Executes in parallel
// ✅ Merges results
```

### Key Features

| Feature | Description |
|---------|-------------|
| **Transparent Sharding** | Distribute data across tables or databases |
| **Property-Agnostic** | Use ANY property names for shard keys |
| **Multiple Strategies** | Property, hash, date, range, alphabetic |
| **Optional Temporal** | Point-in-time queries, version tracking |
| **Standard EF Core** | Write LINQ, DTDE handles distribution |

---

## 📦 Installation

```bash
dotnet add package Dtde.EntityFramework
```

### Minimum Requirements

- .NET 9.0 or later
- Entity Framework Core 9.0 or later
- SQL Server (Azure SQL, SQL Server 2019+)

---

## 🔧 Basic Usage

### 1. Create DbContext

```csharp
public class AppDbContext : DtdeDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>()
            .ShardBy(c => c.Region);
    }
}
```

### 2. Configure Services

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseDtde(dtde =>
    {
        dtde.AddShard(s => s.WithId("EU").WithShardKeyValue("EU"));
        dtde.AddShard(s => s.WithId("US").WithShardKeyValue("US"));
    });
});
```

### 3. Query Data

```csharp
// Cross-shard query
var all = await db.Customers.ToListAsync();

// Optimized single-shard query
var eu = await db.Customers.Where(c => c.Region == "EU").ToListAsync();
```

---

## 📖 Samples

Explore complete working examples:

| Sample | Description |
|--------|-------------|
| [Hash Sharding](/samples/Dtde.Samples.HashSharding/) | Even distribution by ID |
| [Date Sharding](/samples/Dtde.Samples.DateSharding/) | Time-based partitioning |
| [Region Sharding](/samples/Dtde.Samples.RegionSharding/) | Geographic distribution |
| [Multi-Tenant](/samples/Dtde.Samples.MultiTenant/) | SaaS multi-tenancy |
| [Combined](/samples/Dtde.Samples.Combined/) | Multiple strategies |
| [Web API](/samples/Dtde.Sample.WebApi/) | REST API integration |

---

## 🤝 Contributing

See the [Development Plan](development-plan/01-overview.md) for technical details and architecture decisions.

---

## 📜 License

This project is licensed under the MIT License.
