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
    string ShardId { get; }

    /// <summary>
    /// Gets the current vote status of this participant.
    /// </summary>
    ParticipantVote Vote { get; }

    /// <summary>
    /// Gets whether this participant has any pending changes.
    /// </summary>
    bool HasPendingChanges { get; }

    /// <summary>
    /// Gets the number of pending operations for this participant.
    /// </summary>
    int PendingOperationCount { get; }

    /// <summary>
    /// Prepares the participant for commit (2PC Phase 1).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The participant's vote.</returns>
    Task<ParticipantVote> PrepareAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the participant's changes (2PC Phase 2 - Commit).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the commit operation.</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the participant's changes (2PC Phase 2 - Abort).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the rollback operation.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
