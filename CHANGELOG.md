# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
