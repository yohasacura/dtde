# DTDE Wiki

Welcome to the **Distributed Temporal Data Engine (DTDE)** Wiki! This section contains detailed API reference documentation, architecture guides, and comprehensive class documentation.

## 📖 Documentation Index

### Core Documentation

| Document | Description |
|----------|-------------|
| [**Architecture**](architecture.md) | System design, components, and data flow |
| [**API Reference**](api-reference.md) | Complete API documentation |
| [**Configuration**](configuration.md) | All configuration options explained |
| [**Classes Reference**](classes-reference.md) | Detailed class and interface documentation |
| [**Troubleshooting**](troubleshooting.md) | Common issues, FAQ, and solutions |

### Quick Links

- 🚀 [Quickstart Guide](../guides/quickstart.md)
- 📚 [Getting Started](../guides/getting-started.md)
- 🔀 [Sharding Guide](../guides/sharding-guide.md)
- ⏱️ [Temporal Guide](../guides/temporal-guide.md)

---

## Package Overview

### Dtde.EntityFramework

The main package providing EF Core integration:

```bash
dotnet add package Dtde.EntityFramework
```

**Key Namespaces:**
- `Dtde.EntityFramework` - Core DbContext and extensions
- `Dtde.EntityFramework.Configuration` - Options and builders
- `Dtde.EntityFramework.Query` - Query execution
- `Dtde.EntityFramework.Update` - Write operations

### Dtde.Core

Core domain model and utilities:

**Key Namespaces:**
- `Dtde.Core.Metadata` - Shard and entity metadata
- `Dtde.Core.Sharding` - Sharding strategies
- `Dtde.Core.Temporal` - Temporal context

### Dtde.Abstractions

Interfaces and contracts:

**Key Namespaces:**
- `Dtde.Abstractions.Metadata` - Metadata interfaces
- `Dtde.Abstractions.Temporal` - Temporal interfaces

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                     Application Code                        │
│         Standard EF Core LINQ Queries                       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                   Dtde.EntityFramework                       │
│  ┌─────────────┐  ┌─────────────┐  ┌───────────────────┐   │
│  │ DtdeDbContext│  │ Query Engine │  │  Update Engine    │   │
│  │             │  │ (Sharded)   │  │  (Write Router)   │   │
│  └─────────────┘  └─────────────┘  └───────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
┌─────────────────────────┐     ┌─────────────────────────┐
│   Table Sharding        │     │   Database Sharding     │
│   (Same Database)       │     │   (Multiple Databases)  │
│  ┌─────┐┌─────┐┌─────┐ │     │  ┌─────┐┌─────┐┌─────┐ │
│  │Tbl_1││Tbl_2││Tbl_3│ │     │  │DB_1 ││DB_2 ││DB_3 │ │
│  └─────┘└─────┘└─────┘ │     │  └─────┘└─────┘└─────┘ │
└─────────────────────────┘     └─────────────────────────┘
```

---

## Key Concepts

### 1. Transparent Sharding

DTDE intercepts EF Core queries and transparently routes them to multiple shards:

```csharp
// You write standard EF Core:
var customers = await db.Customers.Where(c => c.Region == "EU").ToListAsync();

// DTDE handles:
// - Shard resolution
// - Parallel execution
// - Result merging
```

### 2. Property-Agnostic Design

No hardcoded property names. Configure any property for sharding or temporal:

```csharp
// Any property can be a shard key
entity.ShardBy(c => c.Region);
entity.ShardBy(o => o.Year);
entity.ShardBy(p => p.Category);

// Any DateTime properties for temporal
entity.HasTemporalValidity(c => c.EffectiveDate, c => c.ExpirationDate);
entity.HasTemporalValidity(p => p.StartDate, p => p.EndDate);
```

### 3. Optional Temporal Versioning

Temporal is opt-in. Entities can use:
- Sharding only (default)
- Temporal only
- Both together
- Neither (standard EF Core)

---

## Version History

| Version | Date | Notes |
|---------|------|-------|
| 1.0.0 | - | Initial release |

---

## Contributing

See the [development plan](../development-plan/01-overview.md) for technical details and contribution guidelines.

---

[← Back to Documentation](../README.md) | [Architecture →](architecture.md)
