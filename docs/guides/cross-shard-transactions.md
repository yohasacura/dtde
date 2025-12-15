# Cross-Shard Transactions

This guide explains how to use DTDE's cross-shard transaction support to perform atomic operations across multiple database shards.

!!! info "When to Use Cross-Shard Transactions"
    Cross-shard transactions are needed when you must ensure data consistency across multiple shards. For example:

    - Transferring funds between accounts in different regions
    - Creating related entities that span multiple shards
    - Migrating data between shards atomically

---

## Overview

DTDE provides a two-phase commit (2PC) implementation for coordinating transactions across multiple database shards. This ensures ACID guarantees even when data is distributed.

### Key Components

| Component | Description |
|-----------|-------------|
| `ICrossShardTransactionCoordinator` | Manages transaction lifecycle and coordination |
| `ICrossShardTransaction` | Represents an active cross-shard transaction |
| `CrossShardTransactionOptions` | Configuration for transaction behavior |
| `TransparentShardingInterceptor` | EF Core interceptor for automatic transaction handling |
| `ITransactionParticipant` | Represents a shard participant in the 2PC protocol |
| `ShardTransactionParticipant` | Concrete implementation managing per-shard database transactions |

---

## Understanding Two-Phase Commit (2PC)

The two-phase commit protocol ensures that all shards either commit or rollback together, maintaining atomicity across distributed databases. Understanding this protocol helps you design better cross-shard operations and troubleshoot issues.

### How 2PC Works

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           COORDINATOR                                        │
│                   (CrossShardTransactionCoordinator)                        │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
        ┌───────────────────────────┼───────────────────────────┐
        │                           │                           │
        ▼                           ▼                           ▼
┌───────────────┐           ┌───────────────┐           ┌───────────────┐
│  Participant  │           │  Participant  │           │  Participant  │
│   (Shard-EU)  │           │   (Shard-US)  │           │  (Shard-APAC) │
└───────────────┘           └───────────────┘           └───────────────┘
```

#### Phase 1: Prepare (Voting Phase)

In the prepare phase, the coordinator asks each participant if it can commit:

```
┌────────────┐                                    ┌────────────┐
│ Coordinator│                                    │Participants│
└─────┬──────┘                                    └─────┬──────┘
      │                                                 │
      │  1. Execute pending operations                  │
      │─────────────────────────────────────────────────▶
      │                                                 │
      │  2. SaveChangesAsync() - validates & acquires   │
      │     locks but does NOT commit                   │
      │─────────────────────────────────────────────────▶
      │                                                 │
      │  3. Return vote: Prepared / Abort / ReadOnly    │
      │◀─────────────────────────────────────────────────
      │                                                 │
```

**What happens during Prepare:**

1. **Operation Execution**: All pending operations queued on each participant are executed
2. **SaveChanges (without commit)**: EF Core's `SaveChangesAsync()` is called, which:
   - Validates all entity changes
   - Generates SQL statements
   - Acquires database locks on affected rows
   - Writes changes to the transaction log
3. **Vote Response**: Each participant returns one of:
   - `Prepared` - Ready to commit, locks acquired
   - `Abort` - Cannot commit (constraint violation, conflict, etc.)
   - `ReadOnly` - No changes to commit (optimization)

```csharp
// Inside ShardTransactionParticipant.PrepareAsync()
public async Task<ParticipantVote> PrepareAsync(CancellationToken cancellationToken)
{
    // Execute any pending operations first
    await ExecutePendingOperationsAsync(cancellationToken);

    // Check if we have any changes
    if (!_context.ChangeTracker.HasChanges())
    {
        _vote = ParticipantVote.ReadOnly;
        return _vote;
    }

    // Save changes but don't commit the transaction yet
    // This validates the changes and acquires locks
    await _context.SaveChangesAsync(cancellationToken);

    _vote = ParticipantVote.Prepared;
    return _vote;
}
```

#### Phase 2: Commit or Abort (Decision Phase)

Based on the votes, the coordinator makes a global decision:

**If ALL participants vote Prepared or ReadOnly → COMMIT**

```
┌────────────┐                                    ┌────────────┐
│ Coordinator│                                    │Participants│
└─────┬──────┘                                    └─────┬──────┘
      │                                                 │
      │  All voted Prepared ✓                           │
      │                                                 │
      │  1. Send COMMIT to all participants             │
      │─────────────────────────────────────────────────▶
      │                                                 │
      │  2. Each participant commits its DB transaction │
      │─────────────────────────────────────────────────▶
      │                                                 │
      │  3. Acknowledge commit complete                 │
      │◀─────────────────────────────────────────────────
      │                                                 │
      │  Transaction State → Committed ✓                │
```

**If ANY participant votes Abort → ROLLBACK**

```
┌────────────┐                                    ┌────────────┐
│ Coordinator│                                    │Participants│
└─────┬──────┘                                    └─────┬──────┘
      │                                                 │
      │  Shard-US voted Abort ✗                         │
      │                                                 │
      │  1. Send ROLLBACK to all participants           │
      │─────────────────────────────────────────────────▶
      │                                                 │
      │  2. Each participant rolls back its transaction │
      │─────────────────────────────────────────────────▶
      │                                                 │
      │  3. Acknowledge rollback complete               │
      │◀─────────────────────────────────────────────────
      │                                                 │
      │  Transaction State → RolledBack                 │
```

### Participant Votes Explained

| Vote | Meaning | When It Occurs |
|------|---------|----------------|
| `Pending` | Participant hasn't voted yet | Initial state before prepare |
| `Prepared` | Ready to commit, locks held | SaveChanges succeeded, waiting for commit |
| `Abort` | Cannot commit | Constraint violation, deadlock, or error |
| `ReadOnly` | No changes needed | Participant was enlisted but had no modifications |

### The Critical Window

!!! danger "Understanding the In-Doubt State"

    Between Phase 1 (Prepare) and Phase 2 (Commit), participants hold database locks and are waiting for the coordinator's decision. This is called the **in-doubt** or **prepared** state.

    ```
    ┌─────────────────────────────────────────────────────────────┐
    │                    CRITICAL WINDOW                          │
    │                                                             │
    │  Phase 1 Complete ──────────────────────▶ Phase 2 Start     │
    │                                                             │
    │  • All participants holding locks                           │
    │  • Waiting for coordinator decision                         │
    │  • If coordinator fails here → IN-DOUBT transaction         │
    │  • Locks may block other transactions                       │
    └─────────────────────────────────────────────────────────────┘
    ```

    This is why cross-shard transactions should be kept short and timeouts configured appropriately.

---

## Basic Usage

### Using the Transaction Coordinator

The simplest way to execute cross-shard operations is using `ExecuteInTransactionAsync`:

```csharp
public class AccountService
{
    private readonly ICrossShardTransactionCoordinator _coordinator;
    private readonly AppDbContext _context;

    public AccountService(
        ICrossShardTransactionCoordinator coordinator,
        AppDbContext context)
    {
        _coordinator = coordinator;
        _context = context;
    }

    public async Task TransferFundsAsync(
        int fromAccountId,
        int toAccountId,
        decimal amount)
    {
        await _coordinator.ExecuteInTransactionAsync(async transaction =>
        {
            // Get source account (may be in shard EU)
            var fromAccount = await _context.Accounts
                .FirstAsync(a => a.Id == fromAccountId);

            // Get destination account (may be in shard US)
            var toAccount = await _context.Accounts
                .FirstAsync(a => a.Id == toAccountId);

            // Perform the transfer
            fromAccount.Balance -= amount;
            toAccount.Balance += amount;

            // SaveChanges coordinates with cross-shard transaction
            await _context.SaveChangesAsync();
        });
    }
}
```

### Manual Transaction Control

For more control, use `BeginTransactionAsync`:

```csharp
await using var transaction = await _coordinator.BeginTransactionAsync();

try
{
    // Explicitly enlist shards
    await transaction.EnlistAsync("shard-eu");
    await transaction.EnlistAsync("shard-us");

    // Perform operations
    var euAccount = await GetAccountFromShard("shard-eu", accountId1);
    var usAccount = await GetAccountFromShard("shard-us", accountId2);

    euAccount.Balance -= 1000;
    usAccount.Balance += 1000;

    await SaveToShard("shard-eu", euAccount);
    await SaveToShard("shard-us", usAccount);

    // Commit all shards atomically
    await transaction.CommitAsync();
}
catch
{
    // Rollback on any failure
    await transaction.RollbackAsync();
    throw;
}
```

---

## Configuration

### Service Registration

Cross-shard transactions are automatically registered when using `UseDtde()`:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseDtde(dtde =>
    {
        // Shards are automatically enrolled in cross-shard transactions
        dtde.AddShard(s => s
            .WithId("shard-eu")
            .WithConnectionString(euConnection));

        dtde.AddShard(s => s
            .WithId("shard-us")
            .WithConnectionString(usConnection));
    });
});

// ICrossShardTransactionCoordinator is automatically registered
```

### Transaction Options

Configure transaction behavior with `CrossShardTransactionOptions`:

```csharp
var options = new CrossShardTransactionOptions
{
    // Timeout for the entire transaction
    Timeout = TimeSpan.FromMinutes(2),

    // Isolation level
    IsolationLevel = CrossShardIsolationLevel.ReadCommitted,

    // Retry configuration
    EnableRetry = true,
    MaxRetryAttempts = 3,
    RetryDelay = TimeSpan.FromMilliseconds(100),
    UseExponentialBackoff = true,
    MaxRetryDelay = TimeSpan.FromSeconds(10),

    // Optional transaction name for logging/diagnostics
    TransactionName = "FundsTransfer",

    // Enable recovery for long-running transactions
    EnableRecovery = false
};

await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    // Operations here
}, options);
```

### Preset Configurations

DTDE provides preset configurations for common scenarios:

```csharp
// Default settings - balanced for most use cases
var options = CrossShardTransactionOptions.Default;

// Short-lived transactions - quick timeout, fewer retries
var shortLived = CrossShardTransactionOptions.ShortLived;
// Timeout: 10 seconds, MaxRetryAttempts: 2

// Long-running transactions - extended timeout, recovery enabled
var longRunning = CrossShardTransactionOptions.LongRunning;
// Timeout: 5 minutes, MaxRetryAttempts: 5, EnableRecovery: true
```

---

## Transaction States and Lifecycle

Understanding the complete transaction lifecycle helps you handle all possible scenarios correctly.

### Complete State Machine

A cross-shard transaction progresses through these states:

```
                                    ┌──────────┐
                                    │   None   │
                                    └────┬─────┘
                                         │ BeginTransactionAsync()
                                         ▼
                                    ┌──────────┐
                              ┌─────│  Active  │─────┐
                              │     └────┬─────┘     │
                              │          │           │
                         RollbackAsync() │      Exception or
                              │          │      Timeout
                              │          │           │
                              │          │ CommitAsync()
                              │          ▼           │
                              │     ┌──────────┐     │
                              │     │Preparing │─────┤
                              │     └────┬─────┘     │
                              │          │           │
                              │     All Prepared     │
                              │          │           │
                              │          ▼           │
                              │     ┌──────────┐     │
                              │     │ Prepared │─────┤
                              │     └────┬─────┘     │
                              │          │           │
                              │          │           │
                              │          ▼           │
                              │     ┌──────────┐     │
                              │     │Committing│─────┤
                              │     └────┬─────┘     │
                              │          │           │
                              │     Success│    Partial Failure
                              │          │           │
                              ▼          ▼           ▼
                        ┌───────────┐ ┌─────────┐ ┌────────┐
                        │RollingBack│ │Committed│ │ Failed │
                        └─────┬─────┘ └─────────┘ └────────┘
                              │
                              ▼
                        ┌───────────┐
                        │RolledBack │
                        └───────────┘
```

### State Descriptions

| State | Description | Valid Operations |
|-------|-------------|------------------|
| `None` | Transaction has not started | Begin transaction |
| `Active` | Transaction is open and collecting operations | Enlist shards, perform operations, commit, rollback |
| `Preparing` | Phase 1 in progress - asking participants to prepare | Wait (automatic) |
| `Prepared` | All participants voted to commit | Proceed to commit (automatic) |
| `Committing` | Phase 2 in progress - committing all participants | Wait (automatic) |
| `Committed` | All shards committed successfully | None (terminal state) |
| `RollingBack` | Rollback in progress across all shards | Wait (automatic) |
| `RolledBack` | Transaction was rolled back | None (terminal state) |
| `Failed` | Transaction failed during commit (in-doubt) | Manual recovery may be needed |

### State Transitions in Code

```csharp
await using var transaction = await _coordinator.BeginTransactionAsync();
// State: Active

await transaction.EnlistAsync("shard-eu");
await transaction.EnlistAsync("shard-us");
// State: Still Active (enlisting doesn't change state)

// Perform operations...
participant.Context.Update(entity);

await transaction.CommitAsync();
// State progression:
//   Active → Preparing → Prepared → Committing → Committed
//                ↓           ↓           ↓
//           (if any abort)  (if failure) (if partial failure)
//                ↓           ↓           ↓
//           RollingBack → RolledBack   Failed

Console.WriteLine(transaction.State); // Committed (or RolledBack/Failed)
```

### Handling Each State

```csharp
public async Task ProcessWithStateHandling()
{
    await using var transaction = await _coordinator.BeginTransactionAsync();

    try
    {
        await transaction.EnlistAsync("shard-eu");
        await transaction.EnlistAsync("shard-us");

        // ... operations ...

        await transaction.CommitAsync();

        // Check final state
        switch (transaction.State)
        {
            case TransactionState.Committed:
                _logger.LogInformation("Transaction {Id} committed successfully",
                    transaction.TransactionId);
                break;

            case TransactionState.Failed:
                // This shouldn't happen if CommitAsync didn't throw,
                // but handle defensively
                _logger.LogError("Transaction {Id} in failed state",
                    transaction.TransactionId);
                break;
        }
    }
    catch (TransactionPrepareException ex)
    {
        // State: RollingBack → RolledBack
        _logger.LogWarning("Prepare failed on shard {Shard}: {Message}",
            ex.FailedShardId, ex.Message);
    }
    catch (TransactionCommitException ex)
    {
        // State: Failed (some committed, some didn't)
        _logger.LogError(
            "CRITICAL: Partial commit! Committed: [{Committed}], Failed: [{Failed}]",
            string.Join(", ", ex.CommittedShards ?? []),
            string.Join(", ", ex.FailedShards ?? []));
    }
    catch (TransactionTimeoutException ex)
    {
        // State: Failed
        _logger.LogError("Transaction timed out after {Timeout}", ex.Timeout);
    }
}
```

---

## Transparent Integration

DTDE's transparent sharding makes cross-shard transactions work automatically without requiring explicit transaction management in most cases.

### How Transparent Sharding Works Internally

The `TransparentShardingInterceptor` intercepts EF Core's save operations and automatically promotes to cross-shard transactions when needed:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         YOUR APPLICATION CODE                                │
│                                                                             │
│   context.Accounts.Add(new Account { Region = "EU" });                      │
│   context.Accounts.Add(new Account { Region = "US" });                      │
│   await context.SaveChangesAsync();                                         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                    TransparentShardingInterceptor                           │
│                                                                             │
│  1. SavingChangesAsync() intercepted                                        │
│                                                                             │
│  2. AnalyzeChangesForSharding()                                             │
│     ├── Get all Added/Modified/Deleted entities                             │
│     ├── For each entity, determine target shard                             │
│     └── Group entities by shard                                             │
│                                                                             │
│  3. Decision:                                                               │
│     ├── Single shard? → Let normal SaveChanges proceed                      │
│     └── Multiple shards? → HandleCrossShardSaveAsync()                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ Multiple shards detected
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                      HandleCrossShardSaveAsync()                            │
│                                                                             │
│  1. Get ICrossShardTransactionCoordinator                                   │
│                                                                             │
│  2. ExecuteInTransactionAsync():                                            │
│     ├── For each shard group:                                               │
│     │   ├── GetOrCreateParticipantAsync(shardId)                            │
│     │   ├── Copy entities to participant's context                          │
│     │   └── (participant.Context.Add/Update/Remove)                         │
│     └── Commit handled by coordinator (2PC)                                 │
│                                                                             │
│  3. Clear source context's ChangeTracker                                    │
│                                                                             │
│  4. Return total saved count                                                │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Shard Analysis Process

When you call `SaveChangesAsync()`, the interceptor analyzes your changes:

```csharp
// This is what happens internally
private ShardAnalysisResult AnalyzeChangesForSharding(DbContext context)
{
    // 1. Get all entities with pending changes
    var entries = context.ChangeTracker.Entries()
        .Where(e => e.State is Added or Modified or Deleted);

    // 2. Group by target shard
    var shardGroups = new Dictionary<string, List<EntityEntry>>();

    foreach (var entry in entries)
    {
        var metadata = _metadataRegistry.GetEntityMetadata(entry.Entity.GetType());

        if (metadata?.ShardingConfiguration is null)
        {
            // Non-sharded entity → default shard
            AddToGroup(shardGroups, "_default_", entry);
        }
        else
        {
            // Sharded entity → calculate target shard
            var targetShard = _writeRouter.DetermineTargetShard(entry.Entity);
            AddToGroup(shardGroups, targetShard.ShardId, entry);
        }
    }

    // 3. Determine if cross-shard transaction needed
    return new ShardAnalysisResult
    {
        RequiresCrossShardTransaction = shardGroups.Keys.Count > 1,
        ShardGroups = shardGroups
    };
}
```

### Automatic Transaction Enrollment

The `TransparentShardingInterceptor` automatically handles cross-shard transactions during `SaveChanges`:

```csharp
// DTDE automatically detects when entities span multiple shards
_context.Accounts.Add(new Account { Region = "EU", Balance = 5000 });
_context.Accounts.Add(new Account { Region = "US", Balance = 3000 });

// If these entities go to different shards, DTDE automatically
// coordinates a cross-shard transaction
await _context.SaveChangesAsync();
```

**What happens behind the scenes:**

1. **Interception**: `SavingChangesAsync` is intercepted
2. **Analysis**: Entities are grouped by target shard (EU → shard-eu, US → shard-us)
3. **Detection**: Multiple shards detected → cross-shard transaction needed
4. **Coordination**: `ExecuteInTransactionAsync` is called automatically
5. **Distribution**: Each entity is routed to its participant's context
6. **2PC Execution**: Prepare all → Commit all
7. **Cleanup**: Source context's ChangeTracker is cleared

### Explicit Transaction Integration

When you start an explicit EF Core transaction, the interceptor creates a `TransparentShardSession` to coordinate:

```csharp
await using var transaction = await _context.Database.BeginTransactionAsync();

// Behind the scenes:
// 1. TransactionStartingAsync() intercepted
// 2. TransparentShardSession created with new cross-shard transaction
// 3. Session tracks all shards touched during this transaction

_context.Accounts.Add(new Account { Region = "EU" });
await _context.SaveChangesAsync();
// → Routed through session to shard-eu participant

_context.Accounts.Add(new Account { Region = "US" });
await _context.SaveChangesAsync();
// → Routed through session to shard-us participant

await transaction.CommitAsync();
// Behind the scenes:
// 1. TransactionCommittingAsync() intercepted
// 2. Session.CommitAsync() called
// 3. Cross-shard 2PC executed for both shards
// 4. Original transaction commit proceeds
```

### Session Lifecycle

```
┌────────────────────────────────────────────────────────────────┐
│                  TransparentShardSession                       │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Created: BeginTransactionAsync()                              │
│     └── Wraps ICrossShardTransaction                           │
│                                                                │
│  SaveChangesAsync() calls:                                     │
│     ├── Routes entities to correct shard participants          │
│     ├── Tracks touched shards                                  │
│     └── Clears source ChangeTracker                            │
│                                                                │
│  CommitAsync():                                                │
│     └── Delegates to wrapped transaction's CommitAsync()       │
│                                                                │
│  RollbackAsync():                                              │
│     └── Delegates to wrapped transaction's RollbackAsync()     │
│                                                                │
│  Disposed: Transaction ends (commit/rollback/dispose)          │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

---

## Error Handling

Understanding the exception hierarchy and recovery mechanisms is crucial for building robust cross-shard applications.

### Exception Hierarchy

```
DtdeException (base)
    │
    └── CrossShardTransactionException
            │
            ├── TransactionPrepareException   (Phase 1 failure)
            │
            ├── TransactionCommitException    (Phase 2 failure - IN-DOUBT!)
            │
            └── TransactionTimeoutException   (Timeout exceeded)
```

### Exception Details

#### CrossShardTransactionException

Base exception for all cross-shard transaction failures:

```csharp
public class CrossShardTransactionException : DtdeException
{
    public string? TransactionId { get; }           // e.g., "XS-20241215143052-7f3a2b1c"
    public IReadOnlyCollection<string>? InvolvedShards { get; }  // All shards in transaction
    public IReadOnlyCollection<string>? FailedShards { get; }    // Shards that failed
}
```

#### TransactionPrepareException

Thrown when Phase 1 (Prepare) fails - **safe to retry**:

```csharp
public class TransactionPrepareException : CrossShardTransactionException
{
    public string? FailedShardId { get; }  // The shard that voted Abort
}

// Example: Constraint violation during prepare
try
{
    await _coordinator.ExecuteInTransactionAsync(async tx =>
    {
        await tx.EnlistAsync("shard-eu");

        // This entity violates a unique constraint in shard-eu
        participant.Context.Add(duplicateEntity);

        // PrepareAsync will fail when SaveChanges detects the violation
    });
}
catch (TransactionPrepareException ex)
{
    // Safe! No data was committed anywhere
    _logger.LogWarning(
        "Prepare failed on shard '{Shard}' for transaction {TxId}. " +
        "No data was modified. Safe to retry.",
        ex.FailedShardId,
        ex.TransactionId);
}
```

#### TransactionCommitException

Thrown when Phase 2 (Commit) fails - **CRITICAL: may have partial commits**:

```csharp
public class TransactionCommitException : CrossShardTransactionException
{
    public IReadOnlyCollection<string>? CommittedShards { get; }  // Successfully committed
    // FailedShards inherited - failed to commit
}

// Example: Network failure during commit phase
try
{
    await _coordinator.ExecuteInTransactionAsync(async tx =>
    {
        await tx.EnlistAsync("shard-eu");
        await tx.EnlistAsync("shard-us");

        // ... operations ...

        // Both prepare successfully, but during commit:
        // - shard-eu commits successfully
        // - shard-us network failure!
    });
}
catch (TransactionCommitException ex)
{
    // CRITICAL! Data inconsistency possible
    _logger.LogCritical(
        "PARTIAL COMMIT DETECTED for transaction {TxId}!\n" +
        "  Committed shards: [{Committed}]\n" +
        "  Failed shards: [{Failed}]\n" +
        "  Manual intervention may be required.",
        ex.TransactionId,
        string.Join(", ", ex.CommittedShards ?? []),
        string.Join(", ", ex.FailedShards ?? []));

    // Trigger alerts, create incident ticket, etc.
    await _alertService.RaisePartialCommitAlert(ex);
}
```

#### TransactionTimeoutException

Thrown when the transaction exceeds its configured timeout:

```csharp
public class TransactionTimeoutException : CrossShardTransactionException
{
    public TimeSpan Timeout { get; }  // The configured timeout duration
}

// Example: Long-running operation
var options = new CrossShardTransactionOptions
{
    Timeout = TimeSpan.FromSeconds(10)
};

try
{
    await _coordinator.ExecuteInTransactionAsync(async tx =>
    {
        await tx.EnlistAsync("shard-eu");

        // This takes too long...
        await Task.Delay(TimeSpan.FromSeconds(15));

    }, options);
}
catch (TransactionTimeoutException ex)
{
    _logger.LogWarning(
        "Transaction {TxId} timed out after {Timeout}. " +
        "Transaction was rolled back.",
        ex.TransactionId,
        ex.Timeout);
}
```

### Automatic Rollback

Transactions are automatically rolled back on exceptions:

```csharp
try
{
    await _coordinator.ExecuteInTransactionAsync(async tx =>
    {
        await tx.EnlistAsync("shard-eu");
        // Modify EU data...

        await tx.EnlistAsync("shard-us");
        // This throws!
        throw new InvalidOperationException("Something went wrong");
    });
}
catch (InvalidOperationException ex)
{
    // Transaction was automatically rolled back
    // EU changes were NOT persisted
    _logger.LogError(ex, "Transfer failed, all changes rolled back");
}
```

### Retry Mechanism

DTDE includes built-in retry logic for transient failures:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          RETRY FLOW                                          │
│                                                                             │
│  Attempt 1 ─────▶ Transient Error? ─────▶ Yes ─────▶ Wait (RetryDelay)     │
│                         │                                    │              │
│                         │ No                                 │              │
│                         ▼                                    ▼              │
│                    Throw/Return              Attempt 2 ─────▶ ...          │
│                                                                             │
│  Exponential Backoff (if enabled):                                          │
│    Attempt 1: 100ms                                                         │
│    Attempt 2: 200ms                                                         │
│    Attempt 3: 400ms (capped at MaxRetryDelay)                               │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Transient errors that trigger retry:**

- `TimeoutException`
- `OperationCanceledException`
- Exceptions containing "deadlock", "timeout", or "connection" in message

```csharp
// Internal retry logic
private async Task ExecuteWithRetryAsync(Func<Task> action, CrossShardTransactionOptions options)
{
    var attempts = 0;
    var delay = options.RetryDelay;

    while (true)
    {
        attempts++;

        try
        {
            await action();
            return;
        }
        catch (Exception ex) when (IsTransientError(ex) && attempts < options.MaxRetryAttempts)
        {
            _logger.LogWarning(
                "Transient error on attempt {Attempt}/{Max}. Retrying in {Delay}ms...",
                attempts, options.MaxRetryAttempts, delay.TotalMilliseconds);

            await Task.Delay(delay);

            if (options.UseExponentialBackoff)
            {
                delay = TimeSpan.FromMilliseconds(
                    Math.Min(delay.TotalMilliseconds * 2, options.MaxRetryDelay.TotalMilliseconds));
            }
        }
    }
}

private static bool IsTransientError(Exception ex)
{
    return ex is TimeoutException
        || ex is OperationCanceledException
        || ex.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase);
}
```

### Handling Partial Failures

If a commit fails on one shard after succeeding on another, DTDE marks the transaction as Failed:

```csharp
try
{
    await _coordinator.ExecuteInTransactionAsync(async tx =>
    {
        await tx.EnlistAsync("shard-eu");
        await tx.EnlistAsync("shard-us");

        // Operations...

        // During CommitAsync, if EU commits but US fails:
        // 1. EU is already committed (cannot rollback!)
        // 2. US failed to commit
        // 3. TransactionCommitException thrown with details
    });
}
catch (TransactionCommitException ex)
{
    _logger.LogError(ex,
        "Cross-shard transaction failed. TransactionId: {TxId}, " +
        "Committed: [{Committed}], Failed: [{Failed}]",
        ex.TransactionId,
        string.Join(", ", ex.CommittedShards ?? []),
        string.Join(", ", ex.FailedShards ?? []));

    // This is an IN-DOUBT transaction
    // You may need compensating transactions or manual intervention
}
```

### Recovery Strategies

#### Strategy 1: Compensating Transactions

```csharp
catch (TransactionCommitException ex)
{
    if (ex.CommittedShards?.Contains("shard-eu") == true &&
        ex.FailedShards?.Contains("shard-us") == true)
    {
        // EU committed but US didn't - create compensating transaction
        await _coordinator.ExecuteInTransactionAsync(async tx =>
        {
            await tx.EnlistAsync("shard-eu");

            // Reverse the EU changes
            var euAccount = await GetAccount("shard-eu", fromAccountId);
            euAccount.Balance += transferAmount; // Reverse the debit

            // Log the compensation
            participant.Context.Add(new CompensationLog
            {
                OriginalTransactionId = ex.TransactionId,
                Reason = "US shard commit failed",
                CompensatedAt = DateTime.UtcNow
            });
        });
    }
}
```

#### Strategy 2: Outbox Pattern

Avoid cross-shard transactions by using an outbox:

```csharp
// Instead of cross-shard transaction, use outbox
public async Task TransferWithOutbox(int fromId, int toId, decimal amount)
{
    // Step 1: Debit source account and write to outbox (single shard)
    await _coordinator.ExecuteInTransactionAsync(async tx =>
    {
        await tx.EnlistAsync("shard-eu");

        var fromAccount = await GetAccount(fromId);
        fromAccount.Balance -= amount;

        // Outbox message - will be processed later
        participant.Context.Add(new OutboxMessage
        {
            Type = "CreditAccount",
            Payload = JsonSerializer.Serialize(new { toId, amount }),
            CreatedAt = DateTime.UtcNow
        });
    });

    // Step 2: Background processor reads outbox and credits destination
    // This is eventually consistent but avoids 2PC risks
}
```

#### Strategy 3: Saga Pattern

For complex workflows, implement a saga:

```csharp
public class TransferSaga
{
    public async Task Execute(TransferRequest request)
    {
        var sagaId = Guid.NewGuid().ToString();

        try
        {
            // Step 1: Reserve funds in source
            await ReserveFunds(request.FromAccountId, request.Amount, sagaId);

            // Step 2: Credit destination
            await CreditAccount(request.ToAccountId, request.Amount, sagaId);

            // Step 3: Confirm reservation (debit source)
            await ConfirmReservation(request.FromAccountId, sagaId);
        }
        catch
        {
            // Compensate all completed steps
            await CompensateSaga(sagaId);
            throw;
        }
    }
}
```

---

## Best Practices

### 1. Keep Transactions Short

```csharp
// ✅ Good - minimize time in transaction
await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    // Quick read and update
    var account = await _context.Accounts.FindAsync(id);
    account.Balance += amount;
    await _context.SaveChangesAsync();
});

// ❌ Bad - long-running operations in transaction
await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    await SomeSlowExternalApiCall(); // Don't do this!
    var account = await _context.Accounts.FindAsync(id);
    account.Balance += amount;
    await _context.SaveChangesAsync();
});
```

### 2. Limit Number of Shards

```csharp
// ✅ Good - minimize shards involved
await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    await tx.EnlistAsync("shard-eu");
    // Operations on EU only
});

// ⚠️ Caution - many shards increases failure probability
await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    await tx.EnlistAsync("shard-1");
    await tx.EnlistAsync("shard-2");
    await tx.EnlistAsync("shard-3");
    await tx.EnlistAsync("shard-4");
    // More shards = higher chance of partial failure
});
```

### 3. Use Idempotent Operations

```csharp
// ✅ Good - idempotent (can safely retry)
await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    var transfer = await _context.Transfers
        .FirstOrDefaultAsync(t => t.IdempotencyKey == key);

    if (transfer != null) return; // Already processed

    // Process transfer...
});

// ❌ Bad - not idempotent
await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    account.Balance += amount; // Retries would add multiple times!
});
```

### 4. Handle Timeout Appropriately

```csharp
var options = new CrossShardTransactionOptions
{
    Timeout = TimeSpan.FromSeconds(30)
};

try
{
    await _coordinator.ExecuteInTransactionAsync(async tx =>
    {
        // Operations...
    }, options);
}
catch (TimeoutException)
{
    _logger.LogWarning("Transaction timed out - check for deadlocks");
    // Consider: longer timeout, smaller transaction, or async processing
}
```

---

## Isolation Levels

DTDE supports multiple isolation levels for cross-shard transactions. Choosing the right level is crucial for balancing consistency and performance.

### Understanding Isolation Levels

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     ISOLATION LEVEL SPECTRUM                                 │
│                                                                             │
│  Lower Isolation                                          Higher Isolation   │
│  (More Concurrent)                                        (More Consistent) │
│                                                                             │
│  ReadUncommitted ◀────────────────────────────────────────▶ Serializable   │
│        │                    │                    │                │         │
│        ▼                    ▼                    ▼                ▼         │
│   Dirty Reads          Phantom         Non-Repeatable        No            │
│   Allowed              Reads OK        Reads OK              Anomalies     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Detailed Isolation Level Comparison

| Level | Dirty Reads | Non-Repeatable Reads | Phantom Reads | Locking | Performance |
|-------|-------------|---------------------|---------------|---------|-------------|
| `ReadUncommitted` | ✅ Allowed | ✅ Allowed | ✅ Allowed | Minimal | ⚡ Fastest |
| `ReadCommitted` | ❌ Prevented | ✅ Allowed | ✅ Allowed | Row-level read | 🚀 Fast |
| `RepeatableRead` | ❌ Prevented | ❌ Prevented | ✅ Allowed | Row-level held | 🔄 Moderate |
| `Serializable` | ❌ Prevented | ❌ Prevented | ❌ Prevented | Range locks | 🐢 Slowest |
| `Snapshot` | ❌ Prevented | ❌ Prevented | ❌ Prevented | Row versioning | 🚀 Fast reads |

### Anomalies Explained

#### Dirty Read

Reading uncommitted changes from another transaction:

```csharp
// Transaction A                    // Transaction B
account.Balance = 1000;
await context.SaveChangesAsync();   // Balance = 1000 (uncommitted)
                                    var bal = account.Balance; // Reads 1000!
await transaction.RollbackAsync();  // Oops, rolled back!
                                    // Transaction B now has WRONG data
```

#### Non-Repeatable Read

Same query returns different results within one transaction:

```csharp
// Transaction A                    // Transaction B
var bal1 = account.Balance; // 500
                                    account.Balance = 1000;
                                    await context.SaveChangesAsync();
                                    await transaction.CommitAsync();
var bal2 = account.Balance; // 1000 (different!)
```

#### Phantom Read

New rows appear in a repeated query:

```csharp
// Transaction A                    // Transaction B
var count1 = await context.Accounts
    .Where(a => a.Region == "EU")
    .CountAsync(); // Returns 10
                                    context.Add(new Account { Region = "EU" });
                                    await context.SaveChangesAsync();
                                    await transaction.CommitAsync();
var count2 = await context.Accounts
    .Where(a => a.Region == "EU")
    .CountAsync(); // Returns 11 (phantom!)
```

### Choosing the Right Level

| Scenario | Recommended Level | Rationale |
|----------|-------------------|-----------|
| Read-heavy analytics | `ReadUncommitted` | Stale data acceptable, maximum throughput |
| General CRUD operations | `ReadCommitted` | **Default** - good balance |
| Financial reports | `RepeatableRead` | Need consistent reads within report |
| Money transfers | `Serializable` | Must prevent all anomalies |
| Long-running reads | `Snapshot` | Consistent view without blocking writers |

### Configuration Examples

```csharp
// ReadCommitted (Default) - General purpose
var defaultOptions = new CrossShardTransactionOptions
{
    IsolationLevel = CrossShardIsolationLevel.ReadCommitted
};

// Serializable - Critical financial operations
var financialOptions = new CrossShardTransactionOptions
{
    IsolationLevel = CrossShardIsolationLevel.Serializable,
    Timeout = TimeSpan.FromSeconds(30) // Shorter timeout due to more locking
};

await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    // All participants will use Serializable isolation
    await tx.EnlistAsync("shard-eu");
    await tx.EnlistAsync("shard-us");

    // Range locks prevent phantom reads
    // Row locks prevent non-repeatable reads
    // Uncommitted changes invisible
}, financialOptions);
```

### Snapshot Isolation Deep Dive

Snapshot isolation uses row versioning instead of locks:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      SNAPSHOT ISOLATION                                      │
│                                                                             │
│  Transaction A starts at T1                                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ Sees consistent snapshot of data as of T1                           │    │
│  │ Even if other transactions commit changes after T1                  │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  Timeline:                                                                  │
│  T1: Transaction A starts (snapshot taken)                                  │
│  T2: Transaction B commits changes to Account X                             │
│  T3: Transaction A reads Account X → sees T1 version (not T2!)              │
│  T4: Transaction A commits                                                  │
│                                                                             │
│  Benefits:                                                                  │
│  ✓ Readers don't block writers                                              │
│  ✓ Writers don't block readers                                              │
│  ✓ Consistent view throughout transaction                                   │
│                                                                             │
│  Caveats:                                                                   │
│  ⚠ Write conflicts detected at commit time                                  │
│  ⚠ Requires database support (SQL Server, PostgreSQL)                       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

```csharp
var snapshotOptions = new CrossShardTransactionOptions
{
    IsolationLevel = CrossShardIsolationLevel.Snapshot
};

await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    // Long-running report - won't block other operations
    var euAccounts = await GetAllAccounts("shard-eu");
    var usAccounts = await GetAllAccounts("shard-us");

    // Even if accounts are updated during this report,
    // we see a consistent snapshot from when we started

    var report = GenerateReport(euAccounts, usAccounts);

}, snapshotOptions);
```

### Per-Shard Isolation Level Mapping

DTDE maps its isolation levels to database-specific levels:

```csharp
// Internal mapping to System.Data.IsolationLevel
private static IsolationLevel MapIsolationLevel(CrossShardIsolationLevel level) => level switch
{
    CrossShardIsolationLevel.ReadCommitted => IsolationLevel.ReadCommitted,
    CrossShardIsolationLevel.RepeatableRead => IsolationLevel.RepeatableRead,
    CrossShardIsolationLevel.Serializable => IsolationLevel.Serializable,
    CrossShardIsolationLevel.Snapshot => IsolationLevel.Snapshot,
    _ => IsolationLevel.ReadCommitted
};
```

!!! note "Database Compatibility"

    - **SQL Server**: Supports all isolation levels. Snapshot requires enabling at database level.
    - **PostgreSQL**: Maps ReadCommitted and Serializable. RepeatableRead becomes Serializable.
    - **MySQL/InnoDB**: Supports all except Snapshot (uses RepeatableRead by default).
    - **SQLite**: Only Serializable available (no row-level locking).

---

## Monitoring and Diagnostics

Effective monitoring is essential for maintaining healthy cross-shard transaction performance.

### Transaction ID Format

Transaction IDs are designed for easy correlation and debugging:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      TRANSACTION ID FORMAT                                   │
│                                                                             │
│  Without name:  XS-{timestamp}-{uniqueId}                                   │
│  Example:       XS-20241215143052-7f3a2b1c                                  │
│                 │  │              │                                         │
│                 │  │              └── 8-char unique ID (from GUID)          │
│                 │  └── yyyyMMddHHmmss timestamp                             │
│                 └── Cross-Shard prefix                                      │
│                                                                             │
│  With name:     XS-{name}-{timestamp}-{uniqueId}                            │
│  Example:       XS-FundsTransfer-20241215143052-7f3a2b1c                    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

```csharp
// Named transactions for easier identification
var options = new CrossShardTransactionOptions
{
    TransactionName = "FundsTransfer"
};

await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    Console.WriteLine(tx.TransactionId);
    // Output: XS-FundsTransfer-20241215143052-7f3a2b1c
}, options);
```

### Logging Configuration

Enable detailed logging for troubleshooting:

```csharp
builder.Services.AddLogging(logging =>
{
    // Transaction coordinator logs
    logging.AddFilter("Dtde.Core.Transactions", LogLevel.Debug);

    // EF Core interceptor logs
    logging.AddFilter("Dtde.EntityFramework.Infrastructure", LogLevel.Debug);

    // For production, use Information level
    // logging.AddFilter("Dtde.Core.Transactions", LogLevel.Information);
});
```

### Log Messages Reference

| Log Level | Message | Meaning |
|-----------|---------|---------|
| Debug | `Beginning transaction {TxId} with isolation {Level}` | Transaction started |
| Debug | `Enlisted shard {ShardId} in transaction {TxId}` | Shard added to transaction |
| Debug | `Prepare phase completed for {TxId} with {Count} participants` | All shards voted Prepared |
| Information | `Transaction {TxId} committed with {Count} participants` | Success |
| Information | `Transaction {TxId} rolled back` | Rollback completed |
| Warning | `Retrying transaction (attempt {N}/{Max}) after {Delay}ms` | Transient failure, retrying |
| Warning | `Transaction {TxId} timed out in state {State}` | Timeout occurred |
| Error | `Transaction {TxId} failed: {Error}` | Transaction failed |
| Error | `Shard {ShardId} commit failed in transaction {TxId}` | Partial commit issue |
| Error | `Rollback failed for shard {ShardId} in transaction {TxId}` | Cleanup issue |

### Structured Logging Example

```csharp
public class MonitoredAccountService
{
    private readonly ICrossShardTransactionCoordinator _coordinator;
    private readonly ILogger<MonitoredAccountService> _logger;

    public async Task TransferWithMonitoring(TransferRequest request)
    {
        var options = new CrossShardTransactionOptions
        {
            TransactionName = $"Transfer-{request.FromAccount}-to-{request.ToAccount}"
        };

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["FromAccount"] = request.FromAccount,
            ["ToAccount"] = request.ToAccount,
            ["Amount"] = request.Amount,
            ["CorrelationId"] = request.CorrelationId
        });

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _coordinator.ExecuteInTransactionAsync(async tx =>
            {
                _logger.LogDebug(
                    "Starting transfer transaction {TxId}",
                    tx.TransactionId);

                // ... operations ...

                _logger.LogDebug(
                    "Enlisted shards: {Shards}",
                    string.Join(", ", tx.EnlistedShards));

            }, options);

            stopwatch.Stop();

            _logger.LogInformation(
                "Transfer completed successfully in {Duration}ms",
                stopwatch.ElapsedMilliseconds);
        }
        catch (TransactionPrepareException ex)
        {
            _logger.LogWarning(ex,
                "Transfer prepare failed on shard {Shard}",
                ex.FailedShardId);
            throw;
        }
        catch (TransactionCommitException ex)
        {
            _logger.LogCritical(ex,
                "PARTIAL COMMIT: Committed={Committed}, Failed={Failed}",
                string.Join(",", ex.CommittedShards ?? []),
                string.Join(",", ex.FailedShards ?? []));
            throw;
        }
    }
}
```

### Metrics to Track

For production monitoring, track these metrics:

```csharp
public class TransactionMetrics
{
    // Counters
    public int TransactionsStarted { get; set; }
    public int TransactionsCommitted { get; set; }
    public int TransactionsRolledBack { get; set; }
    public int TransactionsFailed { get; set; }
    public int TransactionsTimedOut { get; set; }
    public int RetriesPerformed { get; set; }

    // Histograms
    public TimeSpan AverageDuration { get; set; }
    public TimeSpan P95Duration { get; set; }
    public TimeSpan P99Duration { get; set; }

    // Gauges
    public int ActiveTransactions { get; set; }
    public int AverageShardsPerTransaction { get; set; }
}
```

### OpenTelemetry Integration

```csharp
// Example: Adding OpenTelemetry tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Dtde.Core.Transactions");
        tracing.AddSource("Dtde.EntityFramework.Infrastructure");
    });
```

### Health Checks

```csharp
public class CrossShardTransactionHealthCheck : IHealthCheck
{
    private readonly ICrossShardTransactionCoordinator _coordinator;
    private readonly IShardRegistry _shardRegistry;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // Quick test transaction
            var options = CrossShardTransactionOptions.ShortLived;
            options.TransactionName = "HealthCheck";

            await _coordinator.ExecuteInTransactionAsync(async tx =>
            {
                foreach (var shard in _shardRegistry.GetAllShards().Take(2))
                {
                    await tx.EnlistAsync(shard.ShardId, cancellationToken);
                }
                // No actual changes - just testing coordination
            }, options, cancellationToken);

            return HealthCheckResult.Healthy("Cross-shard transactions operational");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Cross-shard transaction test failed",
                ex);
        }
    }
}
```

---

## Performance Considerations

Understanding the performance characteristics helps you design efficient cross-shard operations.

### Latency Breakdown

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                 CROSS-SHARD TRANSACTION LATENCY                              │
│                                                                             │
│  Single-Shard Transaction:                                                  │
│  ├── Network RTT to DB: ~1-5ms                                              │
│  ├── DB Processing: ~1-10ms                                                 │
│  └── Total: ~2-15ms                                                         │
│                                                                             │
│  Cross-Shard Transaction (2 shards):                                        │
│  ├── Begin transaction: ~1ms                                                │
│  ├── Enlist shard 1: ~1-5ms (network + connection)                          │
│  ├── Enlist shard 2: ~1-5ms (network + connection)                          │
│  ├── Operations: varies                                                     │
│  ├── Phase 1 - Prepare:                                                     │
│  │   ├── SaveChanges shard 1: ~5-20ms (parallel)                            │
│  │   └── SaveChanges shard 2: ~5-20ms (parallel)                            │
│  │   └── Total (parallel): ~5-20ms                                          │
│  ├── Phase 2 - Commit:                                                      │
│  │   ├── Commit shard 1: ~1-5ms (sequential for safety)                     │
│  │   └── Commit shard 2: ~1-5ms                                             │
│  │   └── Total: ~2-10ms                                                     │
│  └── Total: ~15-50ms (vs ~2-15ms single-shard)                              │
│                                                                             │
│  Overhead: ~3-5x latency compared to single-shard                           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Throughput Impact

| Factor | Impact | Mitigation |
|--------|--------|------------|
| Lock duration | 2PC holds locks during prepare phase | Keep transactions short |
| Network round trips | Multiple RTTs for coordination | Co-locate related shards |
| Serialization | Higher isolation = more blocking | Use appropriate isolation level |
| Retry overhead | Failed transactions consume resources | Tune retry settings |

### Optimization Strategies

#### 1. Batch Related Operations

```csharp
// ❌ Inefficient - multiple cross-shard transactions
foreach (var transfer in transfers)
{
    await _coordinator.ExecuteInTransactionAsync(async tx =>
    {
        await ProcessTransfer(transfer);
    });
}

// ✅ Efficient - single cross-shard transaction
await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    foreach (var transfer in transfers)
    {
        await ProcessTransfer(transfer);
    }
});
```

#### 2. Minimize Shard Span

```csharp
// Design entities to minimize cross-shard operations
public class Account
{
    public int Id { get; set; }
    public string Region { get; set; }  // Shard key

    // Keep related data in same shard
    public List<Transaction> Transactions { get; set; }
    public List<AuditLog> AuditLogs { get; set; }
}
```

#### 3. Use Read-Only Transactions Wisely

```csharp
// For read-only operations, skip 2PC overhead
if (isReadOnly)
{
    // Use direct queries to each shard (no transaction coordination)
    var euData = await QueryShard("shard-eu");
    var usData = await QueryShard("shard-us");
    return Merge(euData, usData);
}
else
{
    // Only use cross-shard transaction for writes
    await _coordinator.ExecuteInTransactionAsync(async tx =>
    {
        // ... write operations ...
    });
}
```

---

## Limitations

!!! warning "Important Limitations"
    Cross-shard transactions have inherent limitations due to distributed coordination. Review these carefully when designing your application.

### Network Partitions

If a network partition occurs during the prepare phase, participants hold locks waiting for the coordinator's decision:

```
┌─────────────┐     PARTITION     ┌─────────────┐
│ Coordinator │ ═══════╳═══════ │ Participant │
│             │                   │  (Prepared) │
│ Cannot reach│                   │  LOCKS HELD │
│ participant │                   │  Waiting... │
└─────────────┘                   └─────────────┘

Resolution: Transaction times out, participant rolls back
Risk: Extended lock holding blocks other transactions
```

!!! tip "Mitigation"
    Set appropriate timeouts and implement circuit breakers.

### Distributed Deadlocks

Cross-shard deadlocks are harder to detect than single-database deadlocks:

```
Transaction A:                    Transaction B:
Lock Row 1 in Shard-EU           Lock Row 1 in Shard-US
       ↓                                ↓
Wait for Row 1 in Shard-US  ←→  Wait for Row 1 in Shard-EU
       (DEADLOCK!)
```

!!! tip "Mitigation"
    - Use consistent lock ordering (always lock shards in same order)
    - Set transaction timeouts
    - Use `Snapshot` isolation to reduce locking

### Performance Overhead

Cross-shard transactions have 3-5x higher latency than single-shard:

- Multiple network round trips
- Lock holding during 2PC
- Coordination overhead

!!! tip "Mitigation"
    Design data model to minimize cross-shard transactions.

### No Nested Transactions

DTDE does not support nested cross-shard transactions:

```csharp
await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    // This will throw InvalidOperationException!
    await _coordinator.ExecuteInTransactionAsync(async innerTx =>
    {
        // Nested transaction not supported
    });
});
```

!!! tip "Mitigation"
    Restructure code to use single transaction scope.

### Recovery Limitations

Automatic recovery requires:

- `EnableRecovery = true` in options
- Persistent transaction log (not yet implemented)
- Manual intervention for in-doubt transactions

!!! tip "Mitigation"
    Use compensating transactions or saga pattern for critical workflows.

---

## Advanced Topics

### Working with Participants Directly

For advanced scenarios, you can work directly with transaction participants:

```csharp
await using var transaction = await _coordinator.BeginTransactionAsync();

// Get or create participant for a shard
var crossShardTx = (CrossShardTransaction)transaction;
var participant = await crossShardTx.GetOrCreateParticipantAsync("shard-eu");

// Access the participant's DbContext directly
participant.Context.Add(new Account { Region = "EU", Balance = 1000 });

// Queue operations for later execution
participant.EnqueueOperation(async ctx =>
{
    var account = await ctx.Set<Account>().FirstAsync();
    account.Balance += 500;
});

// Execute all queued operations
await participant.ExecutePendingOperationsAsync();

// Check participant state
Console.WriteLine($"Has changes: {participant.HasPendingChanges}");
Console.WriteLine($"Operation count: {participant.PendingOperationCount}");
Console.WriteLine($"Vote: {participant.Vote}"); // Pending until prepare

await transaction.CommitAsync();
Console.WriteLine($"Vote: {participant.Vote}"); // Prepared (after successful prepare)
```

### Custom Shard Context Factory

You can customize how DbContext instances are created for shards:

```csharp
public class CustomShardContextFactory : IShardContextFactory
{
    public async Task<DbContext> CreateContextAsync(
        string shardId,
        CancellationToken cancellationToken)
    {
        var connectionString = GetConnectionString(shardId);

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        // Add custom configuration per shard
        if (shardId.StartsWith("archive-"))
        {
            optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }

        return new AppDbContext(optionsBuilder.Options);
    }
}
```

### Ambient Transaction Context

The coordinator maintains an ambient transaction using `AsyncLocal`:

```csharp
// Check if there's an active transaction
if (_coordinator.HasActiveTransaction)
{
    var currentTx = _coordinator.CurrentTransaction;
    Console.WriteLine($"Active transaction: {currentTx?.TransactionId}");
}

// The ambient transaction flows across async calls
await _coordinator.ExecuteInTransactionAsync(async tx =>
{
    // CurrentTransaction is available throughout this scope
    await SomeNestedMethod();
});

private async Task SomeNestedMethod()
{
    // Can access the ambient transaction
    var tx = _coordinator.CurrentTransaction;
    Console.WriteLine($"In nested method, transaction: {tx?.TransactionId}");
}
```

### Timeout Handling Deep Dive

Timeouts are handled using `CancellationTokenSource`:

```csharp
// Inside CrossShardTransaction constructor
_timeoutCts = new CancellationTokenSource(Timeout);
_timeoutCts.Token.Register(() => OnTimeout());

// Timeout callback
private void OnTimeout()
{
    if (State is TransactionState.Committed or TransactionState.RolledBack)
        return;

    _logger.LogWarning("Transaction {Id} timed out in state {State}",
        TransactionId, State);

    State = TransactionState.Failed;
}

// During operations, the timeout is linked
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
    cancellationToken, _timeoutCts.Token);

await operation(linkedCts.Token);
```

---

## Next Steps

- [API Reference](../wiki/api-reference.md) - Complete API documentation
- [Architecture](../wiki/architecture.md) - Understanding the transaction coordinator
- [Troubleshooting](../wiki/troubleshooting.md) - Common issues and solutions

---

[← Back to Guides](index.md) | [Sharding Guide →](sharding-guide.md)
