# DTDE — Distributed Temporal Data Engine

> **Project context for Claude Code.** Keep this file in sync with reality;
> it is auto-loaded into every session.

## What this is

DTDE is a production-grade NuGet library that adds **transparent horizontal
sharding** and **bi-temporal versioning** to Entity Framework Core
applications. Application developers write standard LINQ; DTDE handles routing,
partition pruning, point-in-time reads, and two-phase cross-shard transactions.

The shipping NuGet is **`Dtde.EntityFramework`** (recommended for app
developers). `Dtde.Core` and `Dtde.Abstractions` are also published for
provider authors and advanced extensibility.

## Repository layout

```
src/
  Dtde.Abstractions/    Public interfaces / contracts. Reference for custom providers.
  Dtde.Core/            Sharding strategies, temporal context, transaction coordinator.
  Dtde.EntityFramework/ EF Core integration: DtdeDbContext, interceptors, query rewriter.
tests/
  Dtde.Core.Tests/             Unit (~292 tests).
  Dtde.EntityFramework.Tests/  EF integration (~90 tests).
  Dtde.Integration.Tests/      End-to-end (~21 tests).
samples/                Six runnable Web API samples; one per sharding strategy.
benchmarks/Dtde.Benchmarks/    BenchmarkDotNet harness.
docs/                   MkDocs source for https://yohasacura.github.io/dtde/.
.github/                CI, CodeQL, Dependabot, release-drafter, NuGet publish.
```

## Toolchain

- **SDK**: .NET 10 latest (uses `rollForward: latestMajor`); `global.json` pins floor.
- **Targets**: `net8.0;net9.0;net10.0` for libraries; `net9.0` for samples/tests/bench.
- **Central package management**: all versions live in `Directory.Packages.props`.
- **Analyzers**: `latest-recommended` + StyleCop, errors-on by default.
- **Source Link** + **deterministic builds** are configured but only active when
  `CI=true` or `GITHUB_ACTIONS=true` (so local debugging keeps source paths).

## Build & test

```bash
# Build everything
dotnet build -c Release

# Run the full suite (~403 tests)
dotnet test -c Release

# Per-project
dotnet test tests/Dtde.Core.Tests/Dtde.Core.Tests.csproj -c Release
dotnet test tests/Dtde.EntityFramework.Tests/Dtde.EntityFramework.Tests.csproj -c Release
dotnet test tests/Dtde.Integration.Tests/Dtde.Integration.Tests.csproj -c Release

# Benchmarks
cd benchmarks/Dtde.Benchmarks && dotnet run -c Release

# Pack (with API-compat validation against a published baseline)
dotnet pack -c Release -p:PackageValidationBaselineVersion=1.0.0
```

> **Sample projects fail with `CA1873` if `TreatWarningsAsErrors` is in
> effect.** That rule is suppressed for samples in `samples/Directory.Build.props`
> — keep it there. Samples favour readability over strict library-grade
> analysis.

## Code conventions

- **File-scoped namespaces** everywhere.
- **Nullable reference types** are required (`<Nullable>enable</Nullable>`).
- **XML docs are mandatory on public APIs** (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`).
- **Argument validation:**
  - Constructors with field assignment: `_field = value ?? throw new ArgumentNullException(nameof(value));`
  - Static / extension methods: `ArgumentNullException.ThrowIfNull(arg);`
  - Numeric guards: `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arg);`
- **Logging:** all hot-path logging uses `LoggerMessage` source generators in a
  `*LogMessages.cs` file. Pass enums directly, never `.ToString()` them at the
  call site (CA1873).
- **Event ID allocation:**
  - `1000-9999` — `Dtde.EntityFramework.Diagnostics.LogMessages` (queries, writes, batch, transparent).
  - `10000-10199` — `Dtde.Core.Transactions.TransactionLogMessages` (cross-shard transaction lifecycle).
  - Do not duplicate transaction lifecycle events in the EF layer.

## Public API surface

The user-facing entry points consumers most often touch:

- **`DtdeDbContext`** — base class for the application's `DbContext`.
- **`UseDtde(...)`** extension on `DbContextOptionsBuilder`.
- **`ShardBy / ShardByDate / ShardByHash / UseManualSharding`** extensions on
  `EntityTypeBuilder<T>`. Each `ShardBy*` returns a `ShardingBuilder<T>` for
  fluent chaining (`.WithStorageMode(...)`, `.WithTablePattern(...)`,
  `.AddDatabase(...)`).
- **`HasTemporalValidity / HasTemporalContainment`** for bi-temporal entities.
- **`ValidAt<T>(date)` / `AllVersions<T>() / ValidBetween<T>(...)`** on `DtdeDbContext`.
- **`ICrossShardTransactionCoordinator`** for two-phase transactions across shards.

> Helper builders (`ShardingBuilder<T>`, `ManualShardingConfiguration<T>`,
> `ManualTableMapping<T>`) live in `Dtde.EntityFramework.Configuration`.

### Public-API surface tracking

`Microsoft.CodeAnalysis.PublicApiAnalyzers` is wired up. Every src project has
two adjacent files:

- `PublicAPI.Shipped.txt` — the API committed in a released package version.
- `PublicAPI.Unshipped.txt` — additions / removals not yet shipped.

Both are empty in pre-1.0; the analyzer surfaces RS0016 / RS0017 warnings for
every undeclared public symbol. **Before tagging v1.0.0**, run the analyzer's
code fix in your IDE (or `dotnet format analyzers --diagnostics RS0016 RS0037`)
to populate `PublicAPI.Shipped.txt`. After that, RS0016 must be zero — any new
public symbol must first land in `PublicAPI.Unshipped.txt`.

### Banned APIs

`src/BannedSymbols.txt` lists APIs that DTDE source code may **not** call (via
`Microsoft.CodeAnalysis.BannedApiAnalyzers`). The current bans are:

- `String.GetHashCode()` and `Object.GetHashCode()` — process-randomized,
  unsafe for sharding.
- `DateTime.Now` / `DateTimeOffset.Now` — UTC only.
- Culture-sensitive overloads of `String.Equals/IndexOf/StartsWith/EndsWith/Compare`
  — pass a `StringComparison` explicitly.
- `Thread.Sleep` and `Task.Wait` / `Task.Result` — blocking patterns.
- `Activator.CreateInstance(Type)` — bypasses trim/AOT analysis.

## Architectural rules

- **Three-project layering must hold.** `Abstractions` has no project deps
  besides `Microsoft.Extensions.Logging.Abstractions`. `Core` may reference
  `Abstractions` and `Microsoft.EntityFrameworkCore` (no relational provider).
  `EntityFramework` references both plus `Microsoft.EntityFrameworkCore.Relational`.
- **Internals visible:** `Abstractions → Core → EntityFramework` and tests, via
  `<InternalsVisibleTo>` items in each csproj.
- **Trim/AOT:** *not advertised yet.* DTDE relies on `Expression.Property` and
  `Type.GetProperty` for dynamic entity-shape introspection. Do not flip
  `<IsTrimmable>` or `<IsAotCompatible>` to `true` without first annotating
  every reflection call site with `DynamicallyAccessedMembers`.

## Naming

- Sharding: `ShardBy*` for primary configuration, `WithStorageMode/WithTablePattern/AddDatabase` for chained options.
- Temporal: `HasTemporal*` (matches EF Core `Has*` configuration convention).
- Manual: `UseManualSharding` (matches EF `Use*` extension naming).
- Don't introduce a fourth verb prefix without discussing it.

### Domain language

DTDE uses **two terms** consistently — keep them separate:

- **"Temporal"** is the *feature*: temporal versioning, temporal queries, the
  temporal context. Configuration types and namespaces use this name
  (`ITemporalConfiguration`, `Dtde.Abstractions.Temporal`,
  `Dtde.Core.Temporal.TemporalConfiguration`).
- **"Validity"** is the *mechanism* — the `ValidFrom` / `ValidTo` properties on
  the entity itself. This name only appears on property-level identifiers
  (`ValidFromProperty`, `ValidToProperty`, `IsOpenEnded`, `OpenEndedValue`).

So: an entity *has temporal versioning*, configured via *validity properties*.
Don't mix the terms — `IValidityConfiguration` would be wrong; `TemporalProperty`
would be wrong. The configuration object is `ITemporalConfiguration`; the
properties it carries are validity properties.

## Testing rules

- xUnit; method names use `MethodName_StateUnderTest_ExpectedBehavior`.
- The `CA1707` (no underscores) rule is suppressed for tests.
- Integration tests must hit a real EF Core provider (SQLite or InMemory). Do
  not mock `DbContext` directly — fixtures in `tests/*/Fixtures` show the
  expected pattern.

## Working in this repo

- Prefer **smaller, reviewable commits** over big sweeps. The commit history is
  used by `release-drafter`.
- **Conventional commits** are encouraged but not enforced (`feat:`, `fix:`,
  `docs:`, `chore:`).
- Don't push to `main` directly. Open a PR; CI gates merge.
- The full review workflow (`/ultrareview`) is user-triggered.

## Useful Claude Code skills/commands in this repo

- `/init` — regenerate this file from current state.
- `/security-review` — run before any PR that touches sharding or transactions.
- `/simplify` — sanity check after a large refactor.
- Custom: `/dtde-build` (in `.claude/commands/dtde-build.md`) wraps the full build+test flow.

## Known follow-ups (not blockers for v1.0)

- **Trim/AOT readiness.** Reflection call sites are annotated with
  `RequiresUnreferencedCode` / `DynamicallyAccessedMembers` where straightforward
  (see `TemporalConfiguration`, `EntityMetadataBuilder`). To advertise full
  trim safety, every dynamic property access path must be annotated end-to-end,
  then flip `IsTrimmable` / `IsAotCompatible` to `true` in `src/Directory.Build.props`.
- **Populate `PublicAPI.Shipped.txt`** before tagging v1.0.0 (see
  "Public-API surface tracking" above). Run the RS0016 code fix in an IDE.
- **Strong-name signing.** Plumbing exists; supply a key with
  `-p:SignAssembly=true -p:AssemblyOriginatorKeyFile=path/to/key.snk`.
- **Multi-database benchmarks.** Currently SQLite-only. Add SQL Server and
  PostgreSQL paths to the harness when the hardware budget allows.
