# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Cross-Shard Transactions**
  - Two-phase commit (2PC) protocol for ACID transactions across multiple shards
  - `ICrossShardTransactionCoordinator` interface for managing transaction lifecycle
  - `ICrossShardTransaction` interface representing active transactions
  - `CrossShardTransactionOptions` with preset configurations (Default, ShortLived, LongRunning)
  - `TransparentShardingInterceptor` for automatic EF Core integration
  - Multiple isolation levels: ReadUncommitted, ReadCommitted, RepeatableRead, Serializable, Snapshot
  - Transaction states: Active, Preparing, Committed, RolledBack, Failed
  - Automatic rollback on failures with comprehensive error handling

- **Documentation**
  - Cross-shard transactions guide with usage examples
  - Updated API reference with transaction classes
  - Updated architecture documentation with 2PC protocol diagrams

### Changed

- Improved `TransparentShardingInterceptor` to use scoped service resolution
- Updated test count to 403 tests (292 Core + 21 Integration + 90 EntityFramework)

### Fixed

- Fixed scoped service resolution in `TransparentShardingInterceptor` for proper DI lifecycle

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
