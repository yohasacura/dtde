# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

> **Breaking release in progress.** This entry collapses several redundant
> configuration paths from v1.0.0 into a single canonical setup, and turns
> sharding from a metadata-only concept into actually-routes-data behaviour.
> The migration is straightforward (5-10 lines per project); see "Migrating
> from 1.0.0" below. The next published version number is to be decided.

### Samples and documentation refresh

Two new samples covering the new feature areas, plus a full pass over
the docs.

**New samples:**

- **`Dtde.Samples.Transactions`** — atomic cross-shard transfer,
  savepoint partial rollback, read-after-write inside a transaction,
  `FileBasedTransactionLog` + `RecoverAsync` on startup. Four endpoints,
  one per concept.
- **`Dtde.Samples.BulkOperations`** — `BulkInsertAsync`,
  `BulkUpdateAsync`, `BulkDeleteAsync`, `ExecuteStreamingAsync`, plus a
  custom `IBulkInsertProvider` that demonstrates the plug-in shape for
  `SqlBulkCopy` / PG `COPY` / etc.

**New guides:**

- **[Bulk operations](docs/guides/bulk-operations.md)** — every bulk
  API, custom provider chains, streaming, transaction interaction.
- **[Transaction log and recovery](docs/guides/transaction-log-and-recovery.md)** —
  durable lifecycle log, the 2PC recovery rule, operational hygiene.

**Rewritten guides:**

- **[Cross-shard transactions](docs/guides/cross-shard-transactions.md)** —
  full coverage of the public surface: `BeginCrossShardTransactionAsync`,
  savepoints, read-after-write, isolation levels, retry, recovery.
- **[Sharding guide](docs/guides/sharding-guide.md)** — strategies,
  storage modes, shard groups, qualified ids, routing rules.
- **[Migration guide](docs/guides/migration-guide.md)** — fresh
  five-step path from EF Core to DTDE; covers groups, transactions,
  recovery, bulk migration of existing data.

**Refreshed wiki:**

- **[Architecture](docs/wiki/architecture.md)** — current three-project
  layering, key abstractions, query/write/transaction request flows.
- **[API reference](docs/wiki/api-reference.md)** — every public type
  catalogued in one place.
- **[Configuration](docs/wiki/configuration.md)** — every option on
  `DtdeOptionsBuilder`, JSON shard-config schema.
- **[Troubleshooting](docs/wiki/troubleshooting.md)** — current error
  catalogue with concrete fixes.

`docs/index.md` and `docs/guides/index.md` now reflect the full
shipping surface.

### Bulk + query depth

Three more capabilities completing the read/write story.

**1. Streaming fan-out queries.**
`IShardedQueryExecutor.ExecuteStreamingAsync<TEntity>` returns
`IAsyncEnumerable<TEntity>`. Per-shard streams are concurrent producers
into a bounded `Channel<TEntity>`; the consumer pulls in arrival order
with constant memory regardless of total result-set size. The default
buffer is `shardCount × 64`; tweak via the `bufferSize` parameter.

**2. Pluggable bulk-insert providers.**
New `IBulkInsertProvider` abstraction. The shipped
`DefaultBulkInsertProvider` does the standard `AddRangeAsync` +
`SaveChangesAsync` path; provider-specific implementations (SQL Server
`SqlBulkCopy`, PostgreSQL `COPY`, Oracle direct-path) plug in via DI:

```csharp
services.AddSingleton<IBulkInsertProvider, MySqlServerBulkInsertProvider>();
services.AddDtdeDbContext<AppDbContext>(...);
```

The `BulkInsertProviderChain` resolves them in registration order with
the default at the tail; the first provider whose `CanHandle(context)`
returns `true` for a given per-shard `DbContext` wins. Used by
`BulkInsertAsync` for every shard touched.

**3. `BulkUpdateAsync` across shards.** Multi-EF surface:
- `net8.0` / `net9.0` — `Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>>` (the EF Core 7+ shape).
- `net10.0` — `Action<UpdateSettersBuilder<T>>` (the EF Core 10 shape).

Selected at compile time via `#if NET10_0_OR_GREATER`. Fans the update
out across every shard in the entity's group; auto-routes through an
ambient cross-shard transaction when one is active.

**Bulk operations now flow through ambient transactions.**
`BulkInsertAsync`, `BulkUpdateAsync`, `BulkDeleteAsync` detect
`ICrossShardTransactionCoordinator.CurrentTransaction` and route every
per-shard call through that transaction's participants — making
bulk-then-rollback semantics work as expected. Outside a transaction,
the existing single-shard fast path / 2PC behaviour is preserved.

**Bonus correctness fixes (uncovered by the bulk + transaction
interaction):**

- `CommitAllParticipantsAsync` now commits *every* prepared and
  read-only participant. The previous code skipped read-only
  participants, but a read-only participant in a cross-shard
  transaction may still hold an open local transaction with work that
  was committed via earlier `SaveChangesAsync` calls (typical of bulk
  paths). Skipping their commit silently rolled the work back.
- `CrossShardTransaction` now implements idempotent `DisposeAsync` and
  exposes an `IsDisposed` flag. The coordinator's
  `CurrentTransaction` checks `IsDisposed` so a stale ambient
  transaction left in a caller's `AsyncLocal` slot — from a
  previously-disposed `await using` scope — no longer trips nested-tx
  detection on subsequent `BeginTransactionAsync` calls.

### Transactions: depth + production-grade recovery

Three big additions completing the cross-shard transaction story.

**1. Savepoints (within-shard partial rollback).**
`ITransactionParticipant` gains `CreateSavepointAsync`,
`RollbackToSavepointAsync`, and `ReleaseSavepointAsync`. They wrap EF
Core's `IDbContextTransaction` savepoint methods, so on relational
providers you can try a sub-operation and back it out without rolling
the whole cross-shard transaction. Non-relational providers (in-memory)
gracefully no-op via `IDbContextTransaction.SupportsSavepoints`.

**2. Read-after-write within an ambient transaction.**
The `ShardedQueryExecutor` now consults
`ICrossShardTransactionCoordinator.CurrentTransaction`. When a
cross-shard transaction is active, queries against any shard reuse that
shard's open participant context — making writes earlier in the
transaction visible to subsequent reads. Shards not yet enlisted are
auto-enlisted at first touch, so the entire scope inside
`BeginCrossShardTransactionAsync` is transactional.

**3. Crash-recovery transaction log.**
New `ITransactionLog` abstraction with two shipped implementations:
- `InMemoryTransactionLog` — default, no persistence (still useful
  because it surfaces in-doubt transactions during a single process
  lifetime if `RecoverAsync` is called).
- `FileBasedTransactionLog` — JSON-lines append-only file. Survives
  process restarts; tolerant of corrupted trailing lines from a
  mid-write crash. Suitable for single-node deployments and integration
  tests.

The coordinator now records lifecycle events through the log
(transaction-started / participant-enlisted / participant-prepared /
transaction-committed / transaction-rolled-back). On a coordinator
restart, `coordinator.RecoverAsync()` replays the log:
- transactions where every enlisted participant logged a "prepared"
  vote are resolved as **committed** (the global decision was already
  made before the crash — classic 2PC recovery rule);
- transactions with partial prepares are resolved as **rolled back**.
The decision is recorded so the log no longer flags those transactions
as in-doubt. Application code can plug in a custom `ITransactionLog`
backed by Postgres / Redis / etc. for multi-coordinator production
deployments.

**Bonus bug fix: `ShardTransactionParticipant.CommitAsync` no longer
skips committing when a participant votes "ReadOnly".** Previously a
participant with no fresh `ChangeTracker` work would short-circuit the
local transaction commit, which silently rolled back any work committed
via savepoints or earlier `SaveChangesAsync` calls inside the same
transaction. The participant now always commits its open transaction
and lets the relational provider release the locks naturally.

### Cross-shard transactions and bulk operations

Three things landed for production-grade write paths.

**1. The cross-shard coordinator now actually applies the configured
isolation level.** Previously `CrossShardTransactionOptions.IsolationLevel`
was parsed but each participant's `BeginTransactionAsync` was called
without it, so every shard ran at the provider default. The coordinator's
context-factory delegate is now a `ShardParticipantFactory` that returns
both context *and* its open transaction — the EntityFramework layer
constructs that delegate so it can call the relational
`BeginTransactionAsync(IsolationLevel, CancellationToken)` overload
without leaking a relational reference into `Dtde.Core`. In-memory
providers fall back to the parameterless overload gracefully.

**2. Group-qualified participant ids.** `EnlistAsync(IShardMetadata)`
previously enlisted by the shard's *local* id; with shard groups, two
shards in different groups sharing the same local id (e.g. `"0"` in
`hash8` versus `"0"` in `hash3`) would alias to one participant. The
coordinator now uses `IShardMetadata.ToQualifiedId()` (`group::id`)
throughout, including the change-grouping dictionary in the transparent
SaveChanges interceptor.

**3. Single-shard fast path on commit.** A cross-shard transaction with
exactly one enlisted participant skips the prepare phase — the underlying
EF Core local transaction is already atomic, and 2PC adds nothing. Less
overhead, same correctness.

**New surface:**

- **`DtdeDbContext.BeginCrossShardTransactionAsync(...)`** — the public
  entry point for explicit cross-shard transactions. No need to inject
  `ICrossShardTransactionCoordinator` directly.
- **`DtdeDbContext.BulkInsertAsync(IEnumerable<TEntity>)`** — routes each
  entity to its target shard, batches per shard, and commits all shards
  together (2PC) when more than one is touched. Single-shard input takes
  the fast path. Provider-agnostic; for very large batches you can plug a
  per-shard `IShardContextFactory` into a provider-specific bulk path
  (`SqlBulkCopy`, PG `COPY`, etc.) without changing the public surface.
- **`DtdeDbContext.BulkDeleteAsync<TEntity>(predicate)`** — fans
  `ExecuteDelete` across every shard in the entity's group. Set-based, no
  `SELECT` round-trip, no change-tracker overhead.

`BulkUpdate` is intentionally not a public extension method in this
release: EF Core 7/8/9 use `SetPropertyCalls<T>` while EF Core 10 moved to
`UpdateSettersBuilder<T>`. To run a cross-shard set-based update today,
open `BeginCrossShardTransactionAsync` and call `ExecuteUpdateAsync` on
each participant's context — that path is provider-version-stable.

**MetadataRegistry now backfills sharding configuration from EF model
annotations** (previously only temporal annotations were lifted). The
write router and bulk operations rely on `IEntityMetadata.ShardingConfiguration`
to route inserts to the right shard; without the backfill, annotation-only
entities would have silently routed to the default-group hot shard. This
fixes the routing bug for any consumer that uses `OnModelCreating` to
declare `ShardBy*` (the recommended path) and didn't separately register
the entity in the explicit metadata registry.

### Per-entity shard groups

Different entities can now have different shard topologies inside the same
DbContext — eight hash buckets for users *and* three yearly buckets for
orders. Each entity binds to a named **shard group**; shard ids are unique
*within* a group, so `"0"` in a `hash8` group is a different physical shard
from `"0"` in a `hash3` group.

```csharp
// Program.cs
dtde => dtde
    .AddShardGroup("hash8", g => g.AddShards("0","1","2","3","4","5","6","7"))
    .AddShardGroup("years", g => g.AddShards("2023","2024","2025"));

// OnModelCreating
modelBuilder.Entity<UserProfile>().ShardByHash(u => u.UserId, 8).UseShardGroup("hash8");
modelBuilder.Entity<Order>().ShardByDate(o => o.OrderDate, DateShardInterval.Year).UseShardGroup("years");
```

The simple "all entities share one topology" case stays unchanged —
`dtde.AddShards("EU","US","APAC")` populates the implicit default group, and
entities that don't call `UseShardGroup` bind to it.

New types:
- **`IShardGroup`** + **`IShardGroupRegistry`** — the public abstractions.
- **`ShardGroup`** + **`ShardGroupRegistry`** — default implementations.
- **`GroupScopedShardRegistry`** — an `IShardRegistry` view over a single
  group, used by routing strategies and the write router so writes never
  escape the entity's declared group.
- **`IShardMetadata.GroupName`** — every shard now carries its group name
  (defaults to the conventional `"__default__"`).
- **`IShardingConfiguration.ShardGroupName`** — every entity sharding config
  carries the bound group name.
- **`ShardingBuilder<T>.UseShardGroup(name)`** — fluent entity-side API.
- **`DtdeOptionsBuilder.AddShardGroup(name, configure)`** — fluent
  application-side API.
- **`ShardIdentityExtensions.ToQualifiedId()`** — converts a shard's
  `(GroupName, ShardId)` to a `"groupName::shardId"` string used as a
  globally unique identifier in cross-shard transaction bookkeeping.

The model customizer now also **excludes out-of-group entities** from
per-shard models, so `EnsureAllShardsCreatedAsync` only provisions tables
that actually belong on each shard. Two entities with overlapping local
shard ids (e.g. `"0"` in `hash8` vs `"0"` in `hash3`) no longer collide
during provisioning, routing, or fan-out.

**Startup validation:** if an entity declares `UseShardGroup("foo")` but
the application never registered a `foo` group, an `InvalidOperationException`
naming both the entity and the missing group is thrown the first time the
DbContext's model is built. Misspelt group names surface immediately
instead of as obscure DbSet errors at query time.

### Real per-shard execution (was metadata-only in v1.0)

In v1.0, DTDE made shard-routing *decisions* but the execution layer always
returned the same `DbContext` and the same `DbSet` regardless of which shard
was chosen. Net effect: sharding was a no-op — all rows ended up in one
table in one database, regardless of `ShardBy(...)` configuration.

This release fixes that:

- **`PerShardContextFactory<TContext>`** replaces `NullShardContextFactory` as
  the default registration. It materialises a fresh `DbContext` per shard
  with the right connection string and the right EF model.
- **`DtdeShardModelCustomizer`** runs after the user's `OnModelCreating` and,
  for table-mode shards, rewrites every sharded entity's table name to its
  per-shard form (default pattern `{Table}_{ShardId}`, configurable via
  `ShardingBuilder<T>.WithTablePattern(...)`).
- **`DtdeModelCacheKeyFactory`** keys EF Core's model cache by `(context type,
  active shard id, storage mode)` so each shard gets its own cached model.
- **`DtdeDbContext.EnsureAllShardsCreatedAsync()`** provisions every
  shard's tables / databases — analogous to `EnsureCreatedAsync` but
  shard-aware.
- **Mixed mode** — per-shard tables spread across multiple databases — is
  supported via `dtde.AddTableShardInDatabase(id, connectionString)`.
  `primary.db` can host `Customers_EU` + `Customers_US`; `secondary.db` can
  host `Customers_APAC` and `Customers_LATAM`. The model customizer rewrites
  the tables, the per-shard factory routes the connection.
- **`ShardMetadataBuilder.WithConnectionString` is now order-independent.**
  Earlier it had a side-effect of flipping `_storageMode` to
  `ShardStorageMode.Databases`, which made the builder's behaviour depend on
  the order calls were chained in. That side-effect has been removed; storage
  mode is set explicitly via `WithStorageMode(...)`. The shorthand
  `dtde.AddShard(id, connStr)` keeps its original db-mode semantics by
  passing both calls internally.

End-to-end integration tests now verify all three modes route data correctly:
table-mode produces per-shard tables in a single SQLite file, database-mode
writes to the right per-shard databases, and mixed-mode produces per-shard
tables spread across multiple databases.

### Why

A first-time user of v1.0 had to choose between two DI registration helpers,
two entity-configuration paths (extension methods on `EntityTypeBuilder<T>` and
`DtdeOptionsBuilder.ConfigureEntity<T>`), three shard-registration overloads
plus an inline-on-builder option, and several `[Obsolete]` aliases for backward
compatibility. This pass picks one of each.

### Added

- **`dtde.AddShard(string id)`** — table-mode shard shorthand. The shard's id
  doubles as the shard-key value; the connection string is inherited from the
  parent `DbContextOptions`.
- **`dtde.AddShard(string id, string connectionString)`** — database-mode shard
  shorthand for the per-shard-database case.
- **`dtde.AddShards(params string[] ids)`** — bulk table-mode helper:
  `dtde.AddShards("EU", "US", "APAC")`.
- **`OnModelCreating` → registry bridge.** `DtdeDbContext` now lifts DTDE
  annotations declared in `OnModelCreating` (via `HasTemporalValidity`,
  `ShardBy*`, etc.) into the `MetadataRegistry` lazily on first use. There's no
  longer any need to *also* configure entities on `DtdeOptionsBuilder` — the EF
  Core extension methods are sufficient.

### Changed (BREAKING)

- **Single canonical DI entry**: `services.AddDtdeDbContext<TContext>(db, dtde)`
  is now the only public way to register DTDE.
- **Single canonical entity-configuration site**: `OnModelCreating`. The
  `DtdeOptionsBuilder.ConfigureEntity<TEntity>(...)` method has been removed
  (see migration below).
- **`EntityMetadataBuilder<TEntity>` is now `internal`.** It was an
  implementation detail of the now-removed `ConfigureEntity` path.
- **`UseDtde` simplified to one overload**: `UseDtde(Action<DtdeOptionsBuilder>)`.
  The no-arg overload and the `UseDtde(DtdeOptions)` overload have been removed.
- **Shard registration tightened**: `DtdeOptionsBuilder.AddShard(IShardMetadata)`
  and `AddShards(IEnumerable<IShardMetadata>)` removed. Use the new shorthand
  overloads or the existing `AddShard(Action<ShardMetadataBuilder>)` for full
  control.
- **`ShardingBuilder<T>.AddDatabase()`** removed. Database-per-shard
  registration moves to `dtde.AddShard(id, connectionString)` for separation
  of concerns (entity routing in `OnModelCreating`, shard infrastructure in
  `dtde => ...`).
- **`ShardMetadataBuilder.Build()` validation relaxed**: table-mode shards no
  longer require an explicit `WithTable(...)`. The table name is derived from
  the entity's pattern at runtime.
- **Removed `[Obsolete]` aliases** from v1.0:
  `services.AddCrossShardTransactionSupport()`, `AddAutoCrossShardSaveChanges()`,
  `optionsBuilder.UseAutoCrossShardSaveChanges()`. Use
  `services.AddDtdeDbContext<T>(...)` (which wires transparent sharding by default).
- **`AddDtde(...)` and `AddDtde<TContext>(...)` on `IServiceCollection`** are
  now `internal`. They were lower-level building blocks; `AddDtdeDbContext`
  composes them.

### Migrating from 1.0.0

```diff
- // v1.0
- builder.Services.AddDbContext<AppDbContext>(opts =>
- {
-     opts.UseSqlite(cs);
-     opts.UseDtde(dtde =>
-     {
-         dtde.ConfigureEntity<Contract>(e => e.HasTemporalValidity(
-             validFrom: nameof(Contract.ValidFrom),
-             validTo: nameof(Contract.ValidTo)));
-         dtde.AddShard(s => s.WithId("EU").WithShardKeyValue("EU").WithTier(ShardTier.Hot));
-         dtde.AddShard(s => s.WithId("US").WithShardKeyValue("US").WithTier(ShardTier.Hot));
-     });
- });

+ // After
+ builder.Services.AddDtdeDbContext<AppDbContext>(
+     (db, conn) => db.UseSqlite(conn ?? cs),
+     dtde => dtde.AddShards("EU", "US"));
+
+ // Plus, in AppDbContext.OnModelCreating:
+ modelBuilder.Entity<Contract>()
+     .HasTemporalValidity(c => c.ValidFrom, c => c.ValidTo);
+
+ // Once at startup (or before tests), provision the per-shard tables / DBs:
+ await db.EnsureAllShardsCreatedAsync();
```

Two things changed about the registration shape:

1. The first lambda now takes **two parameters** — `(db, connectionString)`.
   DTDE invokes it once for the parent context with `connectionString = null`
   (your code falls through to its default), and once per shard with that
   shard's connection string. For database-mode this is essential; for
   table-mode the per-shard call passes `null` too, so the same default
   wins.
2. **`EnsureAllShardsCreatedAsync()`** replaces the old habit of calling
   `db.Database.EnsureCreated()` once. The old call only created the parent's
   tables — per-shard tables / databases were never provisioned. The new
   helper walks every registered shard.

For shard tiers, read-only flags, custom shard names, etc., the full
`AddShard(s => s.WithId(...).WithTier(...).AsReadOnly())` form is unchanged.

### Earlier polish (also unreleased)

The configuration cleanup above lands on top of an internal polish pass that
also hasn't been published to NuGet yet:

#### Added

- **Production-grade NuGet packaging**
  - `Microsoft.CodeAnalysis.PublicApiAnalyzers` wired up with empty
    `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` baselines per project.
    Locks the public API surface for v1.0+ — RS0016 surfaces as a warning until
    the baseline is populated via the analyzer's IDE code fix.
  - `Microsoft.CodeAnalysis.BannedApiAnalyzers` with a security-focused
    `src/BannedSymbols.txt` (no `String.GetHashCode` for sharding, no
    `DateTime.Now`, no `Thread.Sleep`, etc.).
  - Package validation hookup via `EnablePackageValidation` (activated when
    `PackageValidationBaselineVersion` is supplied at pack time).
  - Deterministic-build / `ContinuousIntegrationBuild` flags now activate
    automatically under CI (`GITHUB_ACTIONS=true` or `CI=true`) so local
    debugger sessions still resolve original source paths.
  - Source Link, embed-untracked-sources, `.snupkg` symbol packages, full
    package metadata (`Title`, `Description`, `Tags`, `PackageReadmeFile`,
    `PackageIcon`, `RepositoryUrl`, `RepositoryType`).

- **Claude Code AI infrastructure**
  - Root [`CLAUDE.md`](./CLAUDE.md) describing build, layout, conventions,
    public-API workflow, and known follow-ups.
  - Project-scoped [`.claude/settings.json`](./.claude/settings.json) with a
    curated allow/deny list (allows `dotnet`, `git`, `gh` read paths;
    denies `git push --force`, `dotnet nuget push`, secret reads).
  - Two project-scoped subagents: `dotnet-library-reviewer`, `nuget-packager`.
  - Four project-scoped slash commands: `/dtde-build`, `/dtde-pack`,
    `/dtde-bench`, `/dtde-verify-build`.
  - Project-scoped skill `add-sharding-strategy` for onboarding contributors.
  - [`.mcp.json`](./.mcp.json) registering the official Microsoft Learn MCP.

- **Cross-Shard Transactions**
  - Two-phase commit (2PC) protocol for ACID transactions across multiple shards.
  - `ICrossShardTransactionCoordinator` and `ICrossShardTransaction` interfaces.
  - `CrossShardTransactionOptions` presets (Default, ShortLived, LongRunning).
  - `TransparentShardingInterceptor` for automatic EF Core integration.
  - Multiple isolation levels and a strict state machine
    (`Active → Preparing → Prepared → Committing → Committed`, with rollback paths).
  - Automatic rollback on failures with comprehensive error handling.

- **Documentation**
  - Cross-shard transactions guide with usage examples.
  - Updated API reference with transaction classes.
  - Updated architecture documentation with 2PC protocol diagrams.

### Changed

- **Naming consolidation (breaking — pre-1.0)**
  - Renamed `IValidityConfiguration` → `ITemporalConfiguration`; moved to
    `Dtde.Abstractions.Temporal` namespace.
  - Renamed concrete `ValidityConfiguration` → `TemporalConfiguration`; moved
    to `Dtde.Core.Temporal` namespace.
  - On `IEntityMetadata`, dropped four redundant alias members in favour of
    a single canonical name each: kept `ClrType` (dropped `EntityType`),
    `PrimaryKey` (dropped `KeyProperty`), `TemporalConfiguration` (dropped
    `Validity`), `ShardingConfiguration` (dropped `Sharding`).
- **Removed obsolete legacy methods** from
  `EntityTypeBuilderExtensions`: `HasValidity`, `UseSharding`,
  `UseCompositeSharding`. Use `HasTemporalValidity`, `ShardBy*`, and
  `UseManualSharding` instead.
- **Resolved duplicate cross-shard logging.** Cross-shard transaction
  lifecycle events live exclusively in
  `Dtde.Core.Transactions.TransactionLogMessages` (event IDs 10000–10199);
  the EF layer's `LogMessages` covers everything else (1000–9999).
- **CA1873 fix** — source-generated logger methods take enum parameters
  directly instead of `.ToString()` at the call site.
- **Helper builder classes extracted** from `EntityTypeBuilderExtensions.cs`
  into their own files under `Dtde.EntityFramework.Configuration/`
  (`ShardingBuilder<T>`, `ManualShardingConfiguration<T>`, `ManualTableMapping<T>`).
- Centralised every csproj's package metadata into
  `src/Directory.Build.props`; per-project `.csproj` files are now ~10
  lines of project-specific dependencies.
- Sample, test, and benchmark `Directory.Build.props` files extracted from
  individual csprojs; suppressions applied per tier.
- `dotnet format` style pass applied across the source tree.
- Improved `TransparentShardingInterceptor` to use scoped service resolution.
- Updated test count to 403 tests (292 Core + 21 Integration + 90 EntityFramework).

### Fixed

- 116 `CA1873` build errors in sample projects (suppressed at the sample tier
  in `samples/Directory.Build.props`).
- Merge conflict markers in `IShardContextFactory.cs` left over from a
  prior `dotnet format` run.
- Fixed scoped service resolution in `TransparentShardingInterceptor` for
  proper DI lifecycle.

## [1.0.0]

### Added

- **Core Packages**
  - `Dtde.Abstractions` - Core interfaces and contracts
  - `Dtde.Core` - Core implementations with sharding strategies (hash, range, date-based)
  - `Dtde.EntityFramework` - EF Core integration with transparent query routing

- **Multi-targeting Support**
  - .NET 8.0, 9.0, and 10.0 support
  - Framework-specific package versions for EF Core and Microsoft.Extensions

- **Sharding Strategies**
  - Hash-based sharding
  - Range-based sharding
  - Date-based sharding
  - Region-based sharding
  - Multi-tenant sharding

- **Temporal Versioning**
  - Point-in-time queries
  - Entity history tracking
  - Temporal boundaries with configurable property names

- **NuGet Package Best Practices**
  - Microsoft.SourceLink.GitHub for debugging and repository metadata
  - Package icon (128x128 PNG with transparent background)
  - Package-specific descriptions and tags
  - Symbol packages (.snupkg) generation
  - PackageReleaseNotes linking to CHANGELOG.md

- **Samples**
  - `Dtde.Sample.WebApi` - Basic web API sample
  - `Dtde.Samples.DateSharding` - Date-based sharding example
  - `Dtde.Samples.HashSharding` - Hash-based sharding example
  - `Dtde.Samples.RegionSharding` - Region-based sharding example
  - `Dtde.Samples.MultiTenant` - Multi-tenant sharding example
  - `Dtde.Samples.Combined` - Combined sharding strategies

- **Testing**
  - Comprehensive unit tests for Core and EntityFramework
  - Integration tests with in-memory database
  - Cross-shard transaction tests with 2PC verification
  - 403 tests passing

- **CI/CD**
  - GitHub Actions workflows for CI, CodeQL, benchmarks
  - NuGet publishing workflow with trusted publishing
  - Documentation deployment with MkDocs

- **Documentation**
  - MkDocs site with Material theme
  - Getting started guide
  - Sharding and temporal guides
  - API reference

- **Benchmarks**
  - BenchmarkDotNet performance tests
  - Automated benchmark tracking

- **Repository Governance**
  - Contributing guidelines
  - Security policy
  - Code of conduct
  - Issue and PR templates

### Changed

- Replaced FluentAssertions with xUnit native Assert (MIT license compliance)
- Replaced Moq with manual test doubles (MIT license compliance)
- Replaced Swashbuckle with native Microsoft.AspNetCore.OpenApi
- Updated BenchmarkDotNet to 0.15.0
- Updated Bogus to 35.6.5
