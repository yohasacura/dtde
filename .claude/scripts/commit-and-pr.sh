#!/usr/bin/env bash
#
# DTDE v1.0 polish pass — commit, push, and create the PR.
#
# Run this from the repo root (NOT from inside .claude/worktrees/...).
# The Claude Code harness blocked the agent from completing the workflow
# itself because pushing to a default branch is a high-severity action.
#
# Usage:
#   bash .claude/scripts/commit-and-pr.sh           # commit + push + open PR
#   bash .claude/scripts/commit-and-pr.sh --merge   # also merge the PR after CI passes
#
# Pre-flight: from this worktree, the agent has already created commit 1
# (build infrastructure baseline) and partially staged commit 2 (refactor).
# This script picks up where the agent left off.

set -euo pipefail

if [[ ! -f Dtde.sln ]]; then
  echo "ERROR: run this from the repo root (where Dtde.sln lives)." >&2
  exit 1
fi

BRANCH="$(git rev-parse --abbrev-ref HEAD)"
if [[ "$BRANCH" == "main" || "$BRANCH" == "master" ]]; then
  echo "ERROR: refusing to commit on $BRANCH. Switch to a feature branch first." >&2
  exit 1
fi

echo "=== Branch: $BRANCH ==="
echo "=== Initial git status ==="
git status --short | head -20
echo "..."
echo

# --------------------------------------------------------------------------
# Commit 2 — naming consolidation (refactor!)
# --------------------------------------------------------------------------
echo "=== Commit 2/6: refactor!  (Validity → Temporal, drop API aliases) ==="
git add \
  src/Dtde.Abstractions/Metadata/IEntityMetadata.cs \
  src/Dtde.Abstractions/Metadata/IValidityConfiguration.cs \
  src/Dtde.Abstractions/Temporal/ITemporalConfiguration.cs \
  src/Dtde.Core/Metadata/EntityMetadata.cs \
  src/Dtde.Core/Metadata/MetadataRegistry.cs \
  src/Dtde.Core/Metadata/ValidityConfiguration.cs \
  src/Dtde.Core/Temporal/TemporalConfiguration.cs \
  src/Dtde.Core/Sharding/PropertyBasedShardingStrategy.cs \
  src/Dtde.Core/Sharding/HashShardingStrategy.cs \
  src/Dtde.Core/Sharding/DateRangeShardingStrategy.cs \
  src/Dtde.Core/Transactions/CrossShardTransaction.cs \
  src/Dtde.Core/Transactions/CrossShardTransactionCoordinator.cs \
  src/Dtde.Core/Transactions/TransactionLogMessages.cs \
  src/Dtde.EntityFramework/DtdeDbContext.cs \
  src/Dtde.EntityFramework/Diagnostics/LogMessages.cs \
  src/Dtde.EntityFramework/Extensions/EntityTypeBuilderExtensions.cs \
  src/Dtde.EntityFramework/Configuration/ShardingBuilder.cs \
  src/Dtde.EntityFramework/Configuration/ManualShardingConfiguration.cs \
  src/Dtde.EntityFramework/Update/VersionManager.cs \
  src/Dtde.EntityFramework/Update/ShardWriteRouter.cs \
  src/Dtde.EntityFramework/Update/DtdeUpdateProcessor.cs \
  src/Dtde.EntityFramework/Query/DtdeExpressionRewriter.cs \
  src/Dtde.EntityFramework/Query/IShardContextFactory.cs \
  src/Dtde.EntityFramework/Infrastructure/ShardAwareSaveChangesInterceptor.cs \
  src/Dtde.EntityFramework/Infrastructure/TransparentShardingInterceptor.cs

git commit -m "$(cat <<'EOF'
refactor!: consolidate Validity → Temporal naming and remove obsolete API

BREAKING CHANGES (acceptable pre-1.0; no shipped predecessor):

Type renames:
- IValidityConfiguration → ITemporalConfiguration
  (moved Dtde.Abstractions.Metadata → Dtde.Abstractions.Temporal)
- ValidityConfiguration  → TemporalConfiguration
  (moved Dtde.Core.Metadata → Dtde.Core.Temporal)

API surface trimmed by 50% on IEntityMetadata. Dropped four redundant
alias members in favour of a single canonical name each:
- kept ClrType (dropped EntityType alias)
- kept PrimaryKey (dropped KeyProperty alias)
- kept TemporalConfiguration (dropped Validity alias)
- kept ShardingConfiguration (dropped Sharding alias)

Removed [Obsolete] legacy methods from EntityTypeBuilderExtensions:
- HasValidity → use HasTemporalValidity
- UseSharding → use ShardBy / ShardByDate / ShardByHash
- UseCompositeSharding → use ShardBy with co-located entities

Helper builder classes extracted out of EntityTypeBuilderExtensions.cs
into their own files under Dtde.EntityFramework.Configuration/:
- ShardingBuilder<T>
- ManualShardingConfiguration<T>
- ManualTableMapping<T>

Logging deduplicated. Cross-shard transaction lifecycle events live
exclusively in Dtde.Core.Transactions.TransactionLogMessages
(event IDs 10000-10199); the EF layer's LogMessages covers everything
else (1000-9999). Removed 12 dead duplicate entries from the EF layer.

CA1873 fix — source-generated logger methods take enum parameters
directly instead of .ToString() at the call site (TransactionState,
CrossShardIsolationLevel).

Domain language clarified:
- "Temporal" is the feature (queries, configuration, namespace).
- "Validity" is the mechanism (the ValidFrom/ValidTo property names).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"

# --------------------------------------------------------------------------
# Commit 3 — tests aligned with rename
# --------------------------------------------------------------------------
echo "=== Commit 3/6: test  (align tests with renamed types) ==="
git add \
  tests/Dtde.Core.Tests/Metadata/TemporalConfigurationTests.cs \
  tests/Dtde.Core.Tests/Metadata/ValidityConfigurationTests.cs \
  tests/Dtde.EntityFramework.Tests/Configuration/TemporalConfigurationTests.cs \
  tests/Dtde.EntityFramework.Tests/Configuration/ValidityConfigurationTests.cs \
  tests/Dtde.Core.Tests/Metadata/EntityMetadataTests.cs \
  tests/Dtde.Core.Tests/Metadata/MetadataRegistryTests.cs \
  tests/Dtde.Core.Tests/Metadata/RelationMetadataTests.cs \
  tests/Dtde.Core.Tests/Sharding/PropertyBasedShardingStrategyTests.cs \
  tests/Dtde.Core.Tests/Sharding/HashShardingStrategyTests.cs \
  tests/Dtde.Core.Tests/Sharding/DateRangeShardingStrategyTests.cs \
  tests/Dtde.EntityFramework.Tests/Configuration/DtdeOptionsBuilderTests.cs \
  tests/Dtde.EntityFramework.Tests/Metadata/EntityMetadataBuilderTests.cs \
  tests/Dtde.Integration.Tests/Context/DtdeDbContextIntegrationTests.cs \
  tests/Dtde.Integration.Tests/Temporal/TemporalQueryIntegrationTests.cs

git commit -m "$(cat <<'EOF'
test: align tests with Validity → Temporal rename

- ValidityConfigurationTests.cs renamed to TemporalConfigurationTests.cs
  (both Core and EntityFramework test projects).
- All test fixtures implementing IEntityMetadata updated to the trimmed
  API surface (ClrType / PrimaryKey / TemporalConfiguration /
  ShardingConfiguration only).
- Call sites updated: metadata.Validity → metadata.TemporalConfiguration,
  metadata.Sharding → metadata.ShardingConfiguration, etc.
- 403 / 403 tests still passing (Core 292, EntityFramework 90,
  Integration 21).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"

# --------------------------------------------------------------------------
# Commit 4 — Claude Code AI infrastructure
# --------------------------------------------------------------------------
echo "=== Commit 4/6: feat  (Claude Code AI infrastructure) ==="
git add \
  CLAUDE.md \
  .claude/README.md \
  .claude/settings.json \
  .claude/agents/ \
  .claude/commands/ \
  .claude/skills/ \
  .claude/scripts/ \
  .mcp.json

git commit -m "$(cat <<'EOF'
feat: add Claude Code AI infrastructure

Project-scoped configuration for the Claude Code CLI. Everything in
.claude/ and .mcp.json is committed and shared with the team.

- CLAUDE.md (root): project context that auto-loads into every Claude
  session — build commands, layout, conventions, public-API workflow,
  domain language rules, and tracked follow-ups.
- .claude/settings.json: curated permissions allowlist for read-only
  dotnet/git/gh; explicit denylist for git push --force, dotnet nuget
  push, secret reads, etc.
- .claude/agents/dotnet-library-reviewer.md: production-NuGet review
  checklist subagent.
- .claude/agents/nuget-packager.md: pack + inspect + validate subagent
  (explicitly never calls dotnet nuget push).
- .claude/commands/: /dtde-build, /dtde-pack, /dtde-bench,
  /dtde-verify-build slash commands.
- .claude/skills/add-sharding-strategy/SKILL.md: contributor skill for
  adding a new sharding strategy without breaking contracts.
- .mcp.json: registers the official Microsoft Learn MCP for first-class
  .NET / EF Core docs lookup.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"

# --------------------------------------------------------------------------
# Commit 5 — sample cleanup + per-sample READMEs
# --------------------------------------------------------------------------
echo "=== Commit 5/6: chore(samples)  (middleware cleanup, null-checks, READMEs) ==="
git add samples/

git commit -m "$(cat <<'EOF'
chore(samples): clean middleware boilerplate, normalize null-checks, add READMEs

- Removed unused app.UseAuthorization() from three sample Program.cs
  files (no auth middleware was registered anyway).
- Standardized null-check style: == null / != null → is null /
  is not null outside expression trees. Kept == null / != null inside
  EF Core LINQ Where(...) clauses (CS8122: 'is' patterns are not
  permitted in expression trees).
- Six new per-sample README.md files, one per sample, each describing
  what the sample shows, how to run it, and what to try.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"

# --------------------------------------------------------------------------
# Commit 6 — documentation upgrade
# --------------------------------------------------------------------------
echo "=== Commit 6/6: docs  (align all documentation with shipped v1.0 API) ==="
git add \
  README.md \
  docs/wiki/classes-reference.md \
  docs/wiki/troubleshooting.md \
  docs/development-plan/ \
  CHANGELOG.md

git commit -m "$(cat <<'EOF'
docs: align all documentation with shipped v1.0 API surface

- README.md: fixed inaccurate references (DateInterval → DateShardInterval,
  removed non-existent ThenByDate example, replaced with UseManualSharding,
  corrected test count to 403).
- docs/wiki/classes-reference.md: IEntityMetadata properties table
  rewritten; new ITemporalConfiguration section.
- docs/wiki/troubleshooting.md: example uses metadata.ClrType /
  IsTemporal / IsSharded.
- docs/development-plan/: all 8 design-plan documents updated to reflect
  the shipped API. Section 3.1 of 03-ef-core-integration.md and the
  fluent-API section of 06-configuration-api.md were rewritten to use
  ShardBy* / HasTemporalValidity / UseManualSharding instead of the
  obsolete HasValidity / UseSharding / UseCompositeSharding.
- CHANGELOG.md: comprehensive Unreleased entry covering both pre-merge
  passes (Added: analyzers, Claude Code infra, cross-shard transactions;
  Changed: naming consolidation, obsolete-method removal, log dedup,
  helper extraction; Fixed: CA1873, merge marker, scoped DI).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"

# --------------------------------------------------------------------------
# Verify nothing was missed
# --------------------------------------------------------------------------
echo
echo "=== Verifying nothing was left out ==="
REMAINING="$(git status --short | grep -vE '^\?\? \.claude/worktrees/' | wc -l)"
if [[ "$REMAINING" -gt 0 ]]; then
  echo "WARNING: $REMAINING files still uncommitted:"
  git status --short
  echo
  read -rp "Continue with push anyway? [y/N] " ans
  [[ "${ans:-N}" != "y" ]] && { echo "Aborted."; exit 1; }
fi

# --------------------------------------------------------------------------
# Push and open the PR
# --------------------------------------------------------------------------
echo
echo "=== Pushing branch to origin ==="
git push -u origin "$BRANCH"

echo
echo "=== Creating PR ==="
PR_URL="$(gh pr create \
  --title "v1.0 polish: production-grade NuGet package + AI infra + docs alignment" \
  --body "$(cat <<'BODY'
## Summary

Production-grade polish pass on DTDE bringing the repo to v1.0-ship readiness across **build infrastructure, API surface, analyzers, samples, benchmarks, documentation, and Claude Code AI infrastructure**.

- ✅ **0 errors, 0 warnings** on a clean `dotnet build -c Release --no-incremental` (net8.0/net9.0/net10.0).
- ✅ **403 / 403 tests passing** (Core 292, EntityFramework 90, Integration 21).
- ✅ **All 3 NuGet packages produce cleanly** with full metadata + symbol packages.
- ✅ **All 6 samples boot cleanly**; benchmark harness verified.

## Highlights

### Naming consolidation (breaking, pre-1.0)

- `IValidityConfiguration` → `ITemporalConfiguration` (namespace `Dtde.Abstractions.Temporal`).
- `ValidityConfiguration` → `TemporalConfiguration` (namespace `Dtde.Core.Temporal`).
- `IEntityMetadata` API trimmed 50% — dropped four alias members; canonical names only (`ClrType` / `PrimaryKey` / `TemporalConfiguration` / `ShardingConfiguration`).
- Obsolete extensions removed (`HasValidity`, `UseSharding`, `UseCompositeSharding`) — replaced by `HasTemporalValidity` + `ShardBy*` + `UseManualSharding`.

### Production-grade analyzer infrastructure

- `Microsoft.CodeAnalysis.PublicApiAnalyzers` wired up with v1.0 baselines (270 / 454 / 681 entries per project).
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` with `src/BannedSymbols.txt` (13 risky APIs banned).
- Source Link, deterministic builds (CI-only), package validation hookup, full csproj metadata.

### Claude Code AI infrastructure

- Root `CLAUDE.md` with build commands, layout, conventions, and follow-up tracker.
- `.claude/settings.json` (curated allow/deny), two subagents (`dotnet-library-reviewer`, `nuget-packager`), four slash commands (`/dtde-build`, `/dtde-pack`, `/dtde-bench`, `/dtde-verify-build`), one skill (`add-sharding-strategy`).
- `.mcp.json` registers the Microsoft Learn MCP.

### Samples + benchmarks + docs

- Six new per-sample READMEs. Sample middleware cleaned (unused `UseAuthorization` removed). Null-check style normalized.
- All documentation aligned with the shipped API: README, wiki/classes-reference, wiki/troubleshooting, all 8 development-plan files, CHANGELOG.

## Test plan

- [x] `dotnet build -c Release --no-incremental` — 0 errors, 0 warnings
- [x] `dotnet test -c Release --no-build` — 403 / 403 passing
- [x] `dotnet pack Dtde.sln -c Release -o ./nupkg` — all 6 artefacts produce
- [x] Smoke-test all 6 samples boot cleanly
- [x] Benchmark harness runs through full BenchmarkDotNet lifecycle

## Migration notes for v1.0 ship

- Run the `RS0016` IDE code fix to populate per-TFM `PublicAPI.Shipped.txt` and remove the `RS00xx` `<NoWarn>` suppression in `Directory.Build.props` (currently silenced because cross-TFM symbol differences make RS0017 false positives unavoidable without per-TFM bookkeeping).
- Optionally: enable strong-name signing via `-p:SignAssembly=true -p:AssemblyOriginatorKeyFile=...` (plumbing exists in `src/Directory.Build.props`).
- Optionally: annotate every reflection call site with `DynamicallyAccessedMembers` and flip `IsTrimmable` / `IsAotCompatible` to `true`.
BODY
)" \
  --base main \
  --head "$BRANCH")"

echo
echo "=== PR created: $PR_URL ==="
echo

# --------------------------------------------------------------------------
# Optionally merge
# --------------------------------------------------------------------------
if [[ "${1:-}" == "--merge" ]]; then
  echo "=== Waiting for CI before merge ==="
  gh pr checks --watch "$PR_URL" || {
    echo "CI did not pass. Refusing to merge." >&2
    exit 1
  }
  echo
  echo "=== Merging PR ==="
  gh pr merge "$PR_URL" --merge --delete-branch
else
  echo "PR is ready for review. Open it in your browser:"
  echo "  $PR_URL"
  echo
  echo "Re-run with --merge to wait for CI and merge automatically once green:"
  echo "  bash .claude/scripts/commit-and-pr.sh --merge"
fi
