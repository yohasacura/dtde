using System.Collections.Concurrent;
using System.Data;

using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dtde.Core.Transactions;

/// <summary>
/// Represents a participant (shard) in a cross-shard transaction.
/// Manages the local database transaction and participates in the 2PC protocol.
/// </summary>
public sealed class ShardTransactionParticipant : ITransactionParticipant, IAsyncDisposable
{
    private readonly DbContext _context;
    private readonly IDbContextTransaction? _transaction;
    private readonly ConcurrentQueue<Func<DbContext, Task>> _pendingOperations = new();
    private ParticipantVote _vote = ParticipantVote.Pending;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardTransactionParticipant"/> class.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="context">The DbContext for this shard.</param>
    /// <param name="transaction">The database transaction.</param>
    internal ShardTransactionParticipant(string shardId, DbContext context, IDbContextTransaction? transaction)
    {
        ShardId = shardId ?? throw new ArgumentNullException(nameof(shardId));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _transaction = transaction;
    }

    /// <inheritdoc />
    public string ShardId { get; }

    /// <inheritdoc />
    public ParticipantVote Vote => _vote;

    /// <inheritdoc />
    public bool HasPendingChanges => _context.ChangeTracker.HasChanges();

    /// <inheritdoc />
    public int PendingOperationCount => _pendingOperations.Count + (_context.ChangeTracker.HasChanges() ? 1 : 0);

    /// <summary>
    /// Gets the DbContext for this participant.
    /// </summary>
    internal DbContext Context => _context;

    /// <summary>
    /// Gets the underlying database transaction.
    /// </summary>
    internal IDbContextTransaction? Transaction => _transaction;

    /// <summary>
    /// Enqueues an operation to be executed on this shard.
    /// </summary>
    /// <param name="operation">The operation to enqueue.</param>
    public void EnqueueOperation(Func<DbContext, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _pendingOperations.Enqueue(operation);
    }

    /// <summary>
    /// Executes all pending operations.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task ExecutePendingOperationsAsync(CancellationToken cancellationToken = default)
    {
        while (_pendingOperations.TryDequeue(out var operation))
        {
            await operation(_context).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<ParticipantVote> PrepareAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            // Execute any pending operations first
            await ExecutePendingOperationsAsync(cancellationToken).ConfigureAwait(false);

            // Check if we have any changes
            if (!_context.ChangeTracker.HasChanges())
            {
                _vote = ParticipantVote.ReadOnly;
                return _vote;
            }

            // Save changes but don't commit the transaction yet
            // This validates the changes and acquires locks
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _vote = ParticipantVote.Prepared;
            return _vote;
        }
        catch (Exception)
        {
            _vote = ParticipantVote.Abort;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_vote != ParticipantVote.Prepared)
        {
            if (_vote == ParticipantVote.ReadOnly)
            {
                // Nothing to commit for read-only participants
                return;
            }

            throw new InvalidOperationException(
                $"Cannot commit participant '{ShardId}' in state '{_vote}'. Must be in Prepared state.");
        }

        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_transaction is not null)
            {
                await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Best effort rollback - must not throw
        catch (Exception)
#pragma warning restore CA1031
        {
            // Best effort rollback - log but don't throw
        }
    }

    /// <summary>
    /// Disposes of the participant and its resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
        }

        await _context.DisposeAsync().ConfigureAwait(false);
    }
}
