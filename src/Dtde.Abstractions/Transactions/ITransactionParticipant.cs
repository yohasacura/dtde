namespace Dtde.Abstractions.Transactions;

/// <summary>
/// Represents a participant (shard) in a cross-shard transaction.
/// </summary>
/// <remarks>
/// Each shard enlisted in a cross-shard transaction is represented by a participant.
/// The participant manages the local transaction and participates in the 2PC protocol.
/// </remarks>
public interface ITransactionParticipant
{
    /// <summary>
    /// Gets the shard identifier for this participant.
    /// </summary>
    public string ShardId { get; }

    /// <summary>
    /// Gets the current vote status of this participant.
    /// </summary>
    public ParticipantVote Vote { get; }

    /// <summary>
    /// Gets whether this participant has any pending changes.
    /// </summary>
    public bool HasPendingChanges { get; }

    /// <summary>
    /// Gets the number of pending operations for this participant.
    /// </summary>
    public int PendingOperationCount { get; }

    /// <summary>
    /// Prepares the participant for commit (2PC Phase 1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The participant's vote.</returns>
    public Task<ParticipantVote> PrepareAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the participant's changes (2PC Phase 2 - Commit).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the commit operation.</returns>
    public Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the participant's changes (2PC Phase 2 - Abort).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the rollback operation.</returns>
    public Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a named savepoint within this participant's local transaction.
    /// A subsequent <see cref="RollbackToSavepointAsync"/> rolls the work
    /// back to the savepoint without rolling back the whole transaction.
    /// Useful for "try this, fall back" semantics inside a long-running
    /// cross-shard transaction.
    /// </summary>
    /// <param name="savepointName">The savepoint name. Must be unique within
    /// this participant's transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>
    /// Savepoints are a relational-provider feature; on non-relational
    /// providers (e.g. in-memory) the call is a no-op. EF Core surfaces them
    /// via <c>IDbContextTransaction.CreateSavepointAsync</c>.
    /// </remarks>
    public Task CreateSavepointAsync(string savepointName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls the participant's work back to a previously-created savepoint.
    /// The transaction stays open; only the work since the savepoint is
    /// undone.
    /// </summary>
    /// <param name="savepointName">The savepoint name passed to a prior
    /// <see cref="CreateSavepointAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a savepoint, discarding the ability to roll back to it.
    /// Optional — savepoints are also discarded when the transaction commits
    /// or rolls back, but releasing them eagerly can free server-side
    /// resources on long-running transactions.
    /// </summary>
    /// <param name="savepointName">The savepoint name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public Task ReleaseSavepointAsync(string savepointName, CancellationToken cancellationToken = default);
}
