using Dtde.Abstractions.Metadata;

namespace Dtde.Abstractions.Transactions;

/// <summary>
/// Represents a cross-shard transaction that coordinates operations across multiple shards.
/// </summary>
/// <remarks>
/// Cross-shard transactions use a two-phase commit (2PC) protocol to ensure
/// atomicity across shard boundaries. Use this interface when operations
/// must succeed or fail together across multiple shards.
/// </remarks>
/// <example>
/// <code>
/// await using var transaction = await coordinator.BeginTransactionAsync();
///
/// await transaction.EnlistAsync("shard-2024");
/// await transaction.EnlistAsync("shard-2025");
///
/// // Perform operations...
///
/// await transaction.CommitAsync();
/// </code>
/// </example>
public interface ICrossShardTransaction : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique identifier for this transaction.
    /// </summary>
    public string TransactionId { get; }

    /// <summary>
    /// Gets the current state of the transaction.
    /// </summary>
    public TransactionState State { get; }

    /// <summary>
    /// Gets the isolation level for this transaction.
    /// </summary>
    public CrossShardIsolationLevel IsolationLevel { get; }

    /// <summary>
    /// Gets the transaction timeout duration.
    /// </summary>
    public TimeSpan Timeout { get; }

    /// <summary>
    /// Gets the timestamp when the transaction was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Gets the shards currently enlisted in this transaction.
    /// </summary>
    public IReadOnlyCollection<string> EnlistedShards { get; }

    /// <summary>
    /// Enlists a shard in this transaction.
    /// </summary>
    /// <param name="shardId">The shard identifier to enlist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the enlist operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the transaction is not in Active state.</exception>
    public Task EnlistAsync(string shardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enlists a shard using shard metadata.
    /// </summary>
    /// <param name="shard">The shard metadata.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the enlist operation.</returns>
    public Task EnlistAsync(IShardMetadata shard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the transaction across all enlisted shards.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the commit operation.</returns>
    /// <exception cref="Dtde.Abstractions.Exceptions.CrossShardTransactionException">
    /// Thrown if the transaction cannot be committed.
    /// </exception>
    public Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the transaction across all enlisted shards.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the rollback operation.</returns>
    public Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the participant context for a specific shard.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <returns>The participant context, or null if not enlisted.</returns>
    public ITransactionParticipant? GetParticipant(string shardId);
}
