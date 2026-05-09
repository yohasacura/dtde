using System.Collections.Concurrent;

using Dtde.Abstractions.Transactions;

namespace Dtde.Core.Transactions;

/// <summary>
/// Default <see cref="ITransactionLog"/> implementation. Records lifecycle
/// events in memory only — survives nothing, but is correct for the
/// in-process happy path. Use <see cref="FileBasedTransactionLog"/> or a
/// custom implementation for crash recovery in production.
/// </summary>
public sealed class InMemoryTransactionLog : ITransactionLog
{
    private readonly ConcurrentDictionary<string, LogState> _entries = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task RecordTransactionStartedAsync(
        string transactionId,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionId);

        _entries[transactionId] = new LogState
        {
            StartedAt = DateTime.UtcNow,
            State = TransactionLogState.Started,
        };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordParticipantEnlistedAsync(
        string transactionId,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionId);
        ArgumentNullException.ThrowIfNull(participantId);

        if (_entries.TryGetValue(transactionId, out var state))
        {
            lock (state)
            {
                state.Enlisted.Add(participantId);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordParticipantPreparedAsync(
        string transactionId,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionId);
        ArgumentNullException.ThrowIfNull(participantId);

        if (_entries.TryGetValue(transactionId, out var state))
        {
            lock (state)
            {
                state.Prepared.Add(participantId);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordTransactionCommittedAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionId);

        if (_entries.TryGetValue(transactionId, out var state))
        {
            lock (state)
            {
                state.State = TransactionLogState.Committed;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordTransactionRolledBackAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionId);

        if (_entries.TryGetValue(transactionId, out var state))
        {
            lock (state)
            {
                state.State = TransactionLogState.RolledBack;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TransactionLogEntry>> GetInDoubtTransactionsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TransactionLogEntry> entries = _entries
            .Where(kv => kv.Value.State == TransactionLogState.Started)
            .Select(kv =>
            {
                lock (kv.Value)
                {
                    return new TransactionLogEntry(
                        kv.Key,
                        kv.Value.StartedAt,
                        kv.Value.Enlisted.ToArray(),
                        kv.Value.Prepared.ToArray(),
                        kv.Value.State);
                }
            })
            .OrderBy(e => e.StartedAt)
            .ToArray();

        return Task.FromResult(entries);
    }

    private sealed class LogState
    {
        public DateTime StartedAt { get; set; }
        public List<string> Enlisted { get; } = new();
        public List<string> Prepared { get; } = new();
        public TransactionLogState State { get; set; }
    }
}
