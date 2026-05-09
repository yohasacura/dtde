---
name: add-sharding-strategy
description: Use when the user wants to add a new sharding strategy to DTDE (something that's neither property-based, hash-based, date-range, nor manual). Lays out the contract, where each piece lives, what tests are required, and what NOT to break.
---

# Adding a new sharding strategy to DTDE

A sharding strategy decides which **shard** owns a row given that row's
shard-key value (or a query's filter on the shard key). DTDE ships with four
built-ins: property-value, date-range, hash, and manual. New strategies
follow the same skeleton.

## Pieces you must touch

1. **`src/Dtde.Abstractions/Metadata/IShardingConfiguration.cs`** —
   add a new variant to `ShardingStrategyType`. Names match the existing
   pattern (`PropertyValue`, `DateRange`, `Hash`, `Composite`).

2. **`src/Dtde.Core/Sharding/<NewName>ShardingStrategy.cs`** — implement
   `IShardingStrategy`. Existing strategies are in the same folder. The
   class name should mirror the enum: `PropertyValue` → `PropertyBasedShardingStrategy`
   (legacy mismatch — new strategies should match exactly: `<Name>ShardingStrategy`).

3. **`src/Dtde.EntityFramework/Extensions/EntityTypeBuilderExtensions.cs`** —
   add `ShardBy<NewName>(...)` extension that:
   - Validates arguments with `ArgumentNullException.ThrowIfNull` /
     `ArgumentOutOfRangeException.ThrowIfNegativeOrZero`.
   - Sets `DtdeAnnotationNames.ShardKeyProperty`, `ShardingStrategy`, any
     strategy-specific annotations (e.g. `ShardCount` for hash), and
     `IsSharded = true`.
   - Returns `ShardingBuilder<TEntity>` for chaining (don't return the raw
     `EntityTypeBuilder` — consumers expect the fluent path).

4. **`src/Dtde.EntityFramework/Configuration/DtdeAnnotationNames.cs`** —
   if your strategy needs a new annotation key, add a `public const string`
   constant.

5. **Tests:**
   - `tests/Dtde.Core.Tests/Sharding/<NewName>ShardingStrategyTests.cs` —
     unit tests for the strategy: empty input, single shard, even
     distribution properties, edge values.
   - `tests/Dtde.EntityFramework.Tests/Extensions/EntityTypeBuilderExtensionsTests.cs`
     — assert the new `ShardBy<NewName>` extension sets the right annotations.

6. **Sample:** add a sample project under `samples/Dtde.Samples.<NewName>/`
   following the skeleton of `samples/Dtde.Samples.HashSharding/`.
   Keep the csproj minimal — common settings live in
   `samples/Directory.Build.props`.

7. **Docs:** add a section to the **"Sharding strategies"** part of
   `README.md` and a guide page under `docs/guides/`.

## Things you must NOT do

- Do not introduce a fourth verb prefix. New configuration uses `ShardBy*`,
  full stop.
- Do not bypass `DtdeAnnotationNames` — every annotation key goes through
  the constants file.
- Do not allocate event IDs in this layer; only the EF and transaction
  layers emit logs. If you need to log from Core, add to
  `Dtde.Core.Transactions.TransactionLogMessages` (or create a sibling
  file with its own `internal static partial class`) and reserve an
  event-ID range outside `1000-9999` and `10000-10199`.
- Do not break `IShardingStrategy.Equals` semantics — strategies are
  compared structurally by tests; override `Equals`/`GetHashCode` if you add
  fields beyond the base.

## Acceptance gate

```bash
/dtde-verify-build
```

Should print `403+N passed, 0 failed`. If hash distribution / chi-squared
checks are part of your tests, add an `[Trait("Category", "Statistical")]`
so they're easy to filter out.
