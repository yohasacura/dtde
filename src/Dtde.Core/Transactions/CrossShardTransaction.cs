using System.Collections.Concurrent;
using System.Data;

using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Dtde.Core.Transactions;

/// <summary>
/// Materialises a per-shard <see cref="DbContext"/> and starts a local
/// database transaction on it with the requested isolation level. Implemented
/// in <c>Dtde.EntityFramework</c> so the relational
/// <c>BeginTransactionAsync(IsolationLevel, CancellationToken)</c> overload
/// can be used without leaking a relational reference into <c>Dtde.Core</c>.
/// </summary>
/// <param name="shardId">The shard's id (default-group local id, or fully-qualified <c>group::id</c>).</param>
/// <param name="isolationLevel">The requested isolation level.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>The freshly-created per-shard context and its open local transaction.</returns>
public delegate Task<(DbContext Context, IDbContextTransaction Transaction)> ShardParticipantFactory(
    string shardId,
    IsolationLevel isolationLevel,
    CancellationToken cancellationToken);

/// <summary>
/// Represents a cross-shard transaction that coordinates operations across multiple shards.
/// Implements the two-phase commit (2PC) protocol for distributed transaction coordination.
/// </summary>
/// <remarks>
/// <para>
/// Participants are keyed by their <see cref="ShardIdentityExtensions.ToQualifiedId">fully-qualified id</see>
/// — <c>"groupName::localId"</c> for named-group shards, or just the local id for default-group shards.
/// This guarantees that two shards with the same local id in different groups (for example, shard <c>"0"</c> in
/// <c>hash8</c> versus shard <c>"0"</c> in <c>hash3</c>) enrol as distinct participants.
/// </para>
/// <para>
/// When the transaction commits with exactly one enlisted participant, the prepare phase is skipped — a
/// single-shard transaction is just an EF Core local transaction, and 2PC adds no atomicity over what the
/// underlying provider already gives.
/// </para>
/// </remarks>
public sealed class CrossShardTransaction : ICrossShardTransaction
{
    private readonly IShardRegistry _shardRegistry;
    private readonly ShardParticipantFactory _participantFactory;
    private readonly ITransactionLog? _transactionLog;
    private readonly Action? _onDisposed;
    private readonly ILogger<CrossShardTransaction> _logger;
    private readonly ConcurrentDictionary<string, ShardTransactionParticipant> _participants = new();
    private readonly CancellationTokenSource _timeoutCts;
    private readonly object _stateLock = new();
    private readonly IsolationLevel _participantIsolationLevel;
    private TransactionState _state = TransactionState.Active;
    private int _disposed; // 0 = not disposed; 1 = disposed (volatile via Interlocked).

    /// <summary>
    /// Whether this transaction has been disposed. The coordinator checks
    /// this when surfacing <c>CurrentTransaction</c> so a stale ambient
    /// transaction in a caller's <see cref="System.Threading.AsyncLocal{T}"/>
    /// — set during a previous, now-disposed scope — doesn't poison
    /// subsequent <c>BeginTransactionAsync</c> calls.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrossShardTransaction"/> class.
    /// </summary>
    /// <param name="transactionId">The unique transaction identifier.</param>
    /// <param name="options">The transaction options.</param>
    /// <param name="shardRegistry">The shard registry.</param>
    /// <param name="participantFactory">Factory that materialises a per-shard context and starts its local transaction.</param>
    /// <param name="logger">The logger.</param>
    internal CrossShardTransaction(
        string transactionId,
        CrossShardTransactionOptions options,
        IShardRegistry shardRegistry,
        ShardParticipantFactory participantFactory,
        ILogger<CrossShardTransaction> logger)
        : this(transactionId, options, shardRegistry, participantFactory, transactionLog: null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance with an <see cref="ITransactionLog"/>.
    /// Lifecycle events are recorded so the coordinator's
    /// <c>RecoverAsync</c> can drive in-doubt transactions to a terminal
    /// state after a coordinator crash.
    /// </summary>
    /// <param name="transactionId">The unique transaction identifier.</param>
    /// <param name="options">The transaction options.</param>
    /// <param name="shardRegistry">The shard registry.</param>
    /// <param name="participantFactory">Factory that materialises a per-shard context and starts its local transaction.</param>
    /// <param name="transactionLog">Optional log for crash recovery.</param>
    /// <param name="logger">The logger.</param>
    internal CrossShardTransaction(
        string transactionId,
        CrossShardTransactionOptions options,
        IShardRegistry shardRegistry,
        ShardParticipantFactory participantFactory,
        ITransactionLog? transactionLog,
        ILogger<CrossShardTransaction> logger)
        : this(transactionId, options, shardRegistry, participantFactory, transactionLog, onDisposed: null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance with a dispose callback. The coordinator
    /// uses this overload to clear its <c>CurrentTransaction</c> when this
    /// transaction is disposed.
    /// </summary>
    internal CrossShardTransaction(
        string transactionId,
        CrossShardTransactionOptions options,
        IShardRegistry shardRegistry,
        ShardParticipantFactory participantFactory,
        ITransactionLog? transactionLog,
        Action? onDisposed,
        ILogger<CrossShardTransaction> logger)
    {
        TransactionId = transactionId ?? throw new ArgumentNullException(nameof(transactionId));
        IsolationLevel = options?.IsolationLevel ?? CrossShardIsolationLevel.ReadCommitted;
        Timeout = options?.Timeout ?? CrossShardTransactionOptions.DefaultTimeout;
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
        _participantFactory = participantFactory ?? throw new ArgumentNullException(nameof(participantFactory));
        _transactionLog = transactionLog;
        _onDisposed = onDisposed;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _participantIsolationLevel = MapIsolationLevel(IsolationLevel);
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

        // Validate shard exists (throws ShardNotFoundException if not found).
        // The shard registry accepts both default-group local ids ("EU") and
        // fully-qualified ids ("hash8::0") — see ShardRegistry.
        _ = _shardRegistry.GetShard(shardId)
            ?? throw new ShardNotFoundException($"Shard '{shardId}' not found in registry.");

        await EnlistInternalAsync(shardId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task EnlistAsync(IShardMetadata shard, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shard);

        // Use the fully-qualified id (group::id) so two shards with the same
        // local id in different groups don't alias to one participant.
        return EnlistAsync(shard.ToQualifiedId(), cancellationToken);
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
            // Single-shard fast path: a transaction with exactly one
            // participant doesn't gain anything from the prepare phase — the
            // underlying EF Core local transaction is already atomic. Skip the
            // 2PC overhead and commit directly.
            if (_participants.Count == 1)
            {
                await CommitSingleShardAsync(linkedCts.Token).ConfigureAwait(false);
            }
            else
            {
                // Phase 1: Prepare
                await PrepareAllParticipantsAsync(linkedCts.Token).ConfigureAwait(false);

                // Phase 2: Commit
                await CommitAllParticipantsAsync(linkedCts.Token).ConfigureAwait(false);
            }

            State = TransactionState.Committed;
            TransactionLogMessages.TransactionCommitted(_logger, TransactionId, _participants.Count);

            if (_transactionLog is not null)
            {
                await _transactionLog.RecordTransactionCommittedAsync(
                    TransactionId,
                    CancellationToken.None).ConfigureAwait(false);
            }
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

        if (_transactionLog is not null)
        {
            await _transactionLog.RecordTransactionRolledBackAsync(
                TransactionId,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ITransactionParticipant? GetParticipant(string shardId)
    {
        ArgumentNullException.ThrowIfNull(shardId);

        return _participants.TryGetValue(shardId, out var participant) ? participant : null;
    }

    /// <summary>
    /// Gets the participant for a shard, creating it if necessary. Accepts
    /// either a default-group local id or a fully-qualified <c>group::id</c>.
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

    /// <summary>
    /// Gets the participant for a shard, creating it if necessary. Uses the
    /// shard's <see cref="ShardIdentityExtensions.ToQualifiedId">qualified id</see>
    /// so shards in different groups never alias.
    /// </summary>
    /// <param name="shard">The shard metadata.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The shard transaction participant.</returns>
    public Task<ShardTransactionParticipant> GetOrCreateParticipantAsync(
        IShardMetadata shard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shard);
        return GetOrCreateParticipantAsync(shard.ToQualifiedId(), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Attempt rollback if not committed
        if (State is TransactionState.Active or TransactionState.Preparing or TransactionState.Prepared)
        {
            try
            {
                await RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Must catch all exceptions during dispose to ensure cleanup completes
            catch (Exception ex)
#pragma warning restore CA1031
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

        // Run the coordinator's "I'm gone" callback so the next
        // BeginTransactionAsync in this scope sees no ambient transaction.
        // The callback runs synchronously here (no awaits between disposal
        // and the callback), so the AsyncLocal mutation flows back to the
        // caller's frame correctly.
        _onDisposed?.Invoke();
    }

    private async Task EnlistInternalAsync(string shardId, CancellationToken cancellationToken)
    {
        // The participant factory (provided by the EntityFramework layer)
        // creates the context and begins the local transaction at the
        // requested isolation level — relational providers honour it,
        // in-memory ignores it gracefully.
        var (context, dbTransaction) = await _participantFactory(
            shardId,
            _participantIsolationLevel,
            cancellationToken).ConfigureAwait(false);

        var participant = new ShardTransactionParticipant(shardId, context, dbTransaction);

        if (!_participants.TryAdd(shardId, participant))
        {
            // Another thread added the participant, clean up
            await participant.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            TransactionLogMessages.EnlistedShard(_logger, shardId, TransactionId);

            if (_transactionLog is not null)
            {
                await _transactionLog.RecordParticipantEnlistedAsync(
                    TransactionId,
                    shardId,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task CommitSingleShardAsync(CancellationToken cancellationToken)
    {
        var participant = _participants.Values.Single();

        // Flush any pending operations and saved changes inside the local
        // transaction. PrepareAsync does both for us — by skipping the multi-
        // shard 2PC dance we still get the SaveChangesAsync inside the
        // participant's transaction, then commit the single transaction.
        await participant.PrepareAsync(cancellationToken).ConfigureAwait(false);
        await participant.CommitAsync(cancellationToken).ConfigureAwait(false);
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
#pragma warning disable CA1031 // Catch all exceptions during 2PC prepare phase to ensure proper abort handling
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    return (participant.ShardId, Vote: ParticipantVote.Abort, Error: (Exception?)ex);
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

        // Record the prepared participants in the durable log BEFORE
        // proceeding to the commit phase. This is the critical 2PC
        // invariant: once every participant is logged as prepared, even a
        // crashed coordinator can decide to commit on recovery.
        if (_transactionLog is not null)
        {
            foreach (var result in results.Where(r => r.Vote == ParticipantVote.Prepared))
            {
                await _transactionLog.RecordParticipantPreparedAsync(
                    TransactionId,
                    result.ShardId,
                    cancellationToken).ConfigureAwait(false);
            }
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

        // Commit every prepared OR read-only participant. Read-only
        // participants still hold an open local transaction (work may have
        // been written via SaveChangesAsync earlier in the scope, e.g. via
        // bulk operations) and need to commit to persist that work and
        // release locks.
        foreach (var participant in _participants.Values
            .Where(p => p.Vote is ParticipantVote.Prepared or ParticipantVote.ReadOnly))
        {
            try
            {
                await participant.CommitAsync(cancellationToken).ConfigureAwait(false);
                committedShards.Add(participant.ShardId);
            }
#pragma warning disable CA1031 // Must catch all exceptions during 2PC commit to track partial failures
            catch (Exception ex)
#pragma warning restore CA1031
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
#pragma warning disable CA1031 // Must catch all exceptions during rollback to ensure all shards are attempted
            catch (Exception ex)
#pragma warning restore CA1031
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

        TransactionLogMessages.TransactionTimedOut(_logger, TransactionId, currentState);

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
