# DTDE - Distributed Temporal Data Engine

<div class="grid cards" markdown>

-   :material-rocket-launch:{ .lg .middle } **Get Started in 5 Minutes**

    ---

    Install DTDE and run your first sharded query in under 5 minutes.

    [:octicons-arrow-right-24: Quickstart](guides/quickstart.md)

-   :material-book-open-variant:{ .lg .middle } **Complete Guide**

    ---

    Learn all the concepts and features with step-by-step tutorials.

    [:octicons-arrow-right-24: Getting Started](guides/getting-started.md)

-   :material-api:{ .lg .middle } **API Reference**

    ---

    Comprehensive documentation of all classes, methods, and configuration options.

    [:octicons-arrow-right-24: API Docs](wiki/api-reference.md)

-   :material-github:{ .lg .middle } **Open Source**

    ---

    MIT licensed. Contribute on GitHub and help make DTDE better.

    [:octicons-arrow-right-24: GitHub](https://github.com/yohasacura/dtde)

</div>

---

## What is DTDE?

**DTDE** is a NuGet package that provides **transparent horizontal sharding** and **optional temporal versioning** for Entity Framework Core.

```csharp
// Write standard EF Core LINQ - DTDE handles distribution transparently
var customers = await db.Customers
    .Where(c => c.Region == "EU")
    .ToListAsync();

// Query data at a specific point in time
var historicalOrders = await db.ValidAt<Order>(new DateTime(2024, 1, 15))
    .Where(o => o.Status == "Completed")
    .ToListAsync();
```

## Key Features

| Feature | Description |
|---------|-------------|
| :material-database-cog: **Transparent Sharding** | Distribute data across tables or databases invisibly |
| :material-clock-outline: **Temporal Versioning** | Track entity history with point-in-time queries |
| :material-swap-horizontal: **Cross-Shard Transactions** | ACID transactions across multiple database shards |
| :material-cog: **Property Agnostic** | Use ANY property names for sharding and temporal boundaries |
| :material-microsoft-visual-studio: **EF Core Native** | Works with standard LINQ - no special query syntax |
| :material-test-tube: **Fully Tested** | 400+ unit and integration tests |

## Installation

=== "Package Manager"

    ```powershell
    Install-Package Dtde.EntityFramework
    ```

=== ".NET CLI"

    ```bash
    dotnet add package Dtde.EntityFramework
    ```

=== "PackageReference"

    ```xml
    <PackageReference Include="Dtde.EntityFramework" Version="1.0.0" />
    ```

## Quick Example

```csharp
public class AppDbContext : DtdeDbContext
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options
            .UseSqlServer(connectionString)
            .UseDtde(dtde => dtde
                .ConfigureEntity<Order>(e => e
                    .HasTemporalValidity("ValidFrom", "ValidTo"))
                .AddShard(s => s
                    .WithId("2024")
                    .WithDateRange(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31))
                    .WithConnectionString(conn2024)));
    }
}
```

## Community

- :fontawesome-brands-github: [GitHub Repository](https://github.com/yohasacura/dtde)
- :material-bug: [Report Issues](https://github.com/yohasacura/dtde/issues)
- :material-chat: [Discussions](https://github.com/yohasacura/dtde/discussions)

## License

DTDE is released under the [MIT License](https://github.com/yohasacura/dtde/blob/master/LICENSE).
