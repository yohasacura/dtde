namespace Dtde.Abstractions.Transactions;

/// <summary>
/// Represents the state of a cross-shard transaction.
/// </summary>
public enum TransactionState
{
    /// <summary>
    /// Transaction has not started.
    /// </summary>
    None,

    /// <summary>
    /// Transaction is actively collecting operations.
    /// </summary>
    Active,

    /// <summary>
    /// Prepare phase is in progress (2PC Phase 1).
    /// </summary>
    Preparing,

    /// <summary>
    /// All participants have voted to commit (2PC Phase 1 complete).
    /// </summary>
    Prepared,

    /// <summary>
    /// Commit phase is in progress (2PC Phase 2).
    /// </summary>
    Committing,

    /// <summary>
    /// Transaction has been successfully committed.
    /// </summary>
    Committed,

    /// <summary>
    /// Rollback is in progress.
    /// </summary>
    RollingBack,

    /// <summary>
    /// Transaction has been rolled back.
    /// </summary>
    RolledBack,

    /// <summary>
    /// Transaction failed and requires recovery.
    /// </summary>
    Failed
}

/// <summary>
/// Represents the isolation level for cross-shard transactions.
/// </summary>
public enum CrossShardIsolationLevel
{
    /// <summary>
    /// Read committed isolation level.
    /// Default and recommended for most scenarios.
    /// </summary>
    ReadCommitted,

    /// <summary>
    /// Repeatable read isolation level.
    /// Provides stronger consistency but may reduce concurrency.
    /// </summary>
    RepeatableRead,

    /// <summary>
    /// Serializable isolation level.
    /// Strongest consistency but lowest concurrency.
    /// </summary>
    Serializable,

    /// <summary>
    /// Snapshot isolation using row versioning.
    /// Provides consistent reads without blocking writers.
    /// </summary>
    Snapshot
}

/// <summary>
/// Represents the vote from a transaction participant during the prepare phase.
/// </summary>
public enum ParticipantVote
{
    /// <summary>
    /// Participant has not yet voted.
    /// </summary>
    Pending,

    /// <summary>
    /// Participant is prepared and ready to commit.
    /// </summary>
    Prepared,

    /// <summary>
    /// Participant cannot commit due to an error or conflict.
    /// </summary>
    Abort,

    /// <summary>
    /// Participant vote indicates read-only operation that doesn't need commit.
    /// </summary>
    ReadOnly
}
