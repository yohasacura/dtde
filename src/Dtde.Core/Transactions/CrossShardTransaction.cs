using System.Collections.Concurrent;
using System.Data;

using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dtde.Core.Transactions;

/// <summary>
/// Represents a cross-shard transaction that coordinates operations across multiple shards.
/// Implements the two-phase commit (2PC) protocol for distributed transaction coordination.
/// </summary>
public sealed class CrossShardTransaction : ICrossShardTransaction
{
    private readonly IShardRegistry _shardRegistry;
    private readonly Func<string, CancellationToken, Task<DbContext>> _contextFactory;
    private readonly ILogger<CrossShardTransaction> _logger;
    private readonly ConcurrentDictionary<string, ShardTransactionParticipant> _participants = new();
    private readonly CancellationTokenSource _timeoutCts;
    private readonly object _stateLock = new();
    private TransactionState _state = TransactionState.Active;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrossShardTransaction"/> class.
    /// </summary>
    /// <param name="transactionId">The unique transaction identifier.</param>
    /// <param name="options">The transaction options.</param>
    /// <param name="shardRegistry">The shard registry.</param>
    /// <param name="contextFactory">Factory for creating DbContext instances for shards.</param>
    /// <param name="logger">The logger.</param>
    internal CrossShardTransaction(
        string transactionId,
        CrossShardTransactionOptions options,
        IShardRegistry shardRegistry,
        Func<string, CancellationToken, Task<DbContext>> contextFactory,
        ILogger<CrossShardTransaction> logger)
    {
        TransactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
        IsolationLevel = options?.IsolationLevel ?? CrossShardIsolationLevel.ReadCommitted;
        Timeout = options?.Timeout ?? CrossShardTransactionOptions.DefaultTimeout;
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        CreatedAt = DateTime.UtcNow;

        // Set up timeout handling
        _timeoutCts = new CancellationTokenSource(Timeout);
        _timeoutCts.Token.Register(() => OnTimeout());
    }

    /// <inheritdoc />
    public string TransactionId { get; }

    /// <inheritdoc />
    public TransactionState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
        private set
        {
            lock (_stateLock)
            {
                _state = value;
            }
        }
    }

    /// <inheritdoc />
    public CrossShardIsolationLevel IsolationLevel { get; }

    /// <inheritdoc />
    public TimeSpan Timeout { get; }

    /// <inheritdoc />
    public DateTime CreatedAt { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<string> EnlistedShards => _participants.Keys.ToList().AsReadOnly();

    /// <inheritdoc />
    public async Task EnlistAsync(string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shardId);

        EnsureStateIs(TransactionState.Active, "enlist a shard");

        if (_participants.ContainsKey(shardId))
        {
            return; // Already enlisted
        }

        var shard = _shardRegistry.GetShard(shardId)
            ?? throw new ShardNotFoundException($"Shard '{shardId}' not found in registry.");

        await EnlistInternalAsync(shardId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task EnlistAsync(IShardMetadata shard, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shard);

        await EnlistAsync(shard.ShardId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureStateIs(TransactionState.Active, "commit");

        if (_participants.IsEmpty)
        {
            State = TransactionState.Committed;
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _timeoutCts.Token);

        try
        {
            // Phase 1: Prepare
            await PrepareAllParticipantsAsync(linkedCts.Token).ConfigureAwait(false);

            // Phase 2: Commit
            await CommitAllParticipantsAsync(linkedCts.Token).ConfigureAwait(false);

            State = TransactionState.Committed;
            TransactionLogMessages.TransactionCommitted(_logger, TransactionId, _participants.Count);
        }
        catch (OperationCanceledException) when (_timeoutCts.IsCancellationRequested)
        {
            State = TransactionState.Failed;
            throw new TransactionTimeoutException(TransactionId, Timeout);
        }
        catch (Exception ex)
        {
            TransactionLogMessages.TransactionCommitFailed(_logger, ex, TransactionId);

            // Attempt rollback on failure
            await RollbackAllParticipantsAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        var currentState = State;
        if (currentState is TransactionState.Committed or TransactionState.RolledBack)
        {
            return;
        }

        State = TransactionState.RollingBack;

        await RollbackAllParticipantsAsync(cancellationToken).ConfigureAwait(false);

        State = TransactionState.RolledBack;
        TransactionLogMessages.TransactionRolledBack(_logger, TransactionId);
    }

    /// <inheritdoc />
    public ITransactionParticipant? GetParticipant(string shardId)
    {
        ArgumentNullException.ThrowIfNull(shardId);

        return _participants.TryGetValue(shardId, out var participant) ? participant : null;
    }

    /// <summary>
    /// Gets the participant for a shard, creating it if necessary.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The shard transaction participant.</returns>
    public async Task<ShardTransactionParticipant> GetOrCreateParticipantAsync(
        string shardId,
        CancellationToken cancellationToken = default)
    {
        if (_participants.TryGetValue(shardId, out var existing))
        {
            return existing;
        }

        await EnlistAsync(shardId, cancellationToken).ConfigureAwait(false);

        return _participants[shardId];
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Attempt rollback if not committed
        if (State is TransactionState.Active or TransactionState.Preparing or TransactionState.Prepared)
        {
            try
            {
                await RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TransactionLogMessages.RollbackError(_logger, ex, TransactionId);
            }
        }

        // Dispose all participants
        foreach (var participant in _participants.Values)
        {
            await participant.DisposeAsync().ConfigureAwait(false);
        }

        _participants.Clear();
        _timeoutCts.Dispose();
    }

    private async Task EnlistInternalAsync(string shardId, CancellationToken cancellationToken)
    {
        var context = await _contextFactory(shardId, cancellationToken).ConfigureAwait(false);

        // Start a database transaction
        // Note: EF Core's BeginTransactionAsync doesn't support isolation level parameter directly.
        // The isolation level should be set at the connection level or via raw SQL if needed.
        var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var participant = new ShardTransactionParticipant(shardId, context, dbTransaction);

        if (!_participants.TryAdd(shardId, participant))
        {
            // Another thread added the participant, clean up
            await participant.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            TransactionLogMessages.EnlistedShard(_logger, shardId, TransactionId);
        }
    }

    private async Task PrepareAllParticipantsAsync(CancellationToken cancellationToken)
    {
        State = TransactionState.Preparing;

        var prepareTasks = _participants.Values
            .Select(async participant =>
            {
                try
                {
                    var vote = await participant.PrepareAsync(cancellationToken).ConfigureAwait(false);
                    return (participant.ShardId, Vote: vote, Error: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (participant.ShardId, Vote: ParticipantVote.Abort, Error: ex);
                }
            });

        var results = await Task.WhenAll(prepareTasks).ConfigureAwait(false);

        // Check for any abort votes
        var abortedResults = results.Where(r => r.Vote == ParticipantVote.Abort).ToList();
        if (abortedResults.Count > 0)
        {
            var failedShards = abortedResults.Select(r => r.ShardId).ToList();
            var firstError = abortedResults.FirstOrDefault(r => r.Error is not null).Error;

            throw new TransactionPrepareException(
                $"Prepare phase failed. {abortedResults.Count} shard(s) voted to abort: {string.Join(", ", failedShards)}",
                TransactionId,
                failedShards.First(),
                firstError);
        }

        State = TransactionState.Prepared;
        TransactionLogMessages.PreparePhaseCompleted(_logger, TransactionId, _participants.Count);
    }

    private async Task CommitAllParticipantsAsync(CancellationToken cancellationToken)
    {
        State = TransactionState.Committing;

        var committedShards = new List<string>();
        var failedShards = new List<string>();
        Exception? firstError = null;

        // Commit each participant - this is the critical section
        foreach (var participant in _participants.Values.Where(p => p.Vote == ParticipantVote.Prepared))
        {
            try
            {
                await participant.CommitAsync(cancellationToken).ConfigureAwait(false);
                committedShards.Add(participant.ShardId);
            }
            catch (Exception ex)
            {
                failedShards.Add(participant.ShardId);
                firstError ??= ex;

                TransactionLogMessages.ShardCommitFailed(_logger, ex, participant.ShardId, TransactionId);
            }
        }

        if (failedShards.Count > 0)
        {
            State = TransactionState.Failed;

            // This is a critical situation - some shards committed, some didn't
            // The transaction is now in-doubt
            throw new TransactionCommitException(
                $"Commit phase failed. {committedShards.Count} shard(s) committed, {failedShards.Count} failed. " +
                $"Transaction is in-doubt and may require manual recovery.",
                TransactionId,
                committedShards,
                failedShards,
                firstError);
        }
    }

    private async Task RollbackAllParticipantsAsync(CancellationToken cancellationToken)
    {
        var rollbackTasks = _participants.Values.Select(async participant =>
        {
            try
            {
                await participant.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return (participant.ShardId, Success: true);
            }
            catch (Exception ex)
            {
                TransactionLogMessages.ShardRollbackFailed(_logger, ex, participant.ShardId, TransactionId);
                return (participant.ShardId, Success: false);
            }
        });

        await Task.WhenAll(rollbackTasks).ConfigureAwait(false);
    }

    private void OnTimeout()
    {
        var currentState = State;
        if (currentState is TransactionState.Committed or TransactionState.RolledBack)
        {
            return;
        }

        TransactionLogMessages.TransactionTimedOut(_logger, TransactionId, currentState.ToString());

        State = TransactionState.Failed;
    }

    private void EnsureStateIs(TransactionState expectedState, string operation)
    {
        var currentState = State;
        if (currentState != expectedState)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} when transaction is in state '{currentState}'. Expected '{expectedState}'.");
        }
    }

    private static IsolationLevel MapIsolationLevel(CrossShardIsolationLevel level) => level switch
    {
        CrossShardIsolationLevel.ReadCommitted => System.Data.IsolationLevel.ReadCommitted,
        CrossShardIsolationLevel.RepeatableRead => System.Data.IsolationLevel.RepeatableRead,
        CrossShardIsolationLevel.Serializable => System.Data.IsolationLevel.Serializable,
        CrossShardIsolationLevel.Snapshot => System.Data.IsolationLevel.Snapshot,
        _ => System.Data.IsolationLevel.ReadCommitted
    };
}
