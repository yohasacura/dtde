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
    /// Gets the per-shard <see cref="DbContext"/> bound to this
    /// participant. Application code writes through this context inside a
    /// cross-shard transaction (the documented usage pattern):
    /// <code>
    /// var participant = await tx.GetOrCreateParticipantAsync(shard);
    /// participant.Context.Set&lt;Customer&gt;().Add(customer);
    /// </code>
    /// </summary>
    public DbContext Context => _context;

    /// <summary>
    /// Gets the underlying EF Core local transaction. Exposed for advanced
    /// scenarios — custom bulk loaders can call
    /// <c>IDbContextTransaction.GetDbTransaction()</c> (from the relational
    /// extensions) to enlist into <c>SqlBulkCopy</c>, PG <c>COPY</c>, etc.
    /// </summary>
    public IDbContextTransaction? Transaction => _transaction;

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

        if (_vote != ParticipantVote.Prepared && _vote != ParticipantVote.ReadOnly)
        {
            throw new InvalidOperationException(
                $"Cannot commit participant '{ShardId}' in state '{_vote}'. " +
                "Must be in Prepared or ReadOnly state.");
        }

        // Always commit the underlying transaction, even for "read-only"
        // participants (no fresh change-tracker changes). The transaction
        // may still contain work committed via earlier SaveChangesAsync
        // calls or after a RollbackToSavepoint, and we must commit to
        // persist that work and release locks.
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

    /// <inheritdoc />
    public async Task CreateSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);

        if (_transaction is null || !_transaction.SupportsSavepoints)
        {
            // Non-relational / in-memory provider: savepoints aren't a thing
            // there. Treat as a no-op so the write path works uniformly across
            // providers. Caller can detect support via SupportsSavepoints.
            return;
        }

        await _transaction.CreateSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);

        if (_transaction is null || !_transaction.SupportsSavepoints)
        {
            return;
        }

        await _transaction.RollbackToSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

        // Rolling back to a savepoint may leave EF's change tracker out of
        // sync with the database (entries that were saved between the
        // savepoint and now are still tracked). Conservatively detach all
        // tracked entries so the next read pulls fresh state from the DB.
        _context.ChangeTracker.Clear();
    }

    /// <inheritdoc />
    public async Task ReleaseSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);

        if (_transaction is null || !_transaction.SupportsSavepoints)
        {
            return;
        }

        await _transaction.ReleaseSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether this participant's local transaction supports savepoints.
    /// False for in-memory providers and any other non-relational store.
    /// </summary>
    public bool SupportsSavepoints => _transaction?.SupportsSavepoints ?? false;

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
