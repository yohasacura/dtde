using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Dtde.Abstractions.Transactions;

namespace Dtde.Core.Transactions;

/// <summary>
/// JSON-lines append-only <see cref="ITransactionLog"/>. Each lifecycle
/// event is one line; recovery rebuilds state by replaying the file.
/// Survives process restarts; suitable for integration tests and
/// single-node deployments. For multi-coordinator production deployments,
/// implement <see cref="ITransactionLog"/> against a shared durable store.
/// </summary>
/// <remarks>
/// <para>
/// Writes are appended sequentially under a per-instance lock. The file is
/// flushed and closed on every write so that a crash mid-transaction
/// leaves a recoverable state. This is correct but not blazing fast — for
/// hot transaction paths use <see cref="InMemoryTransactionLog"/> and
/// route long-running / business-critical transactions through this.
/// </para>
/// <para>
/// The file format is intentionally append-only and JSON-lines so that
/// human inspection (and external tooling) is straightforward. There is no
/// rotation; once the log is full of completed transactions the operator
/// can safely truncate it (DTDE only inspects in-doubt entries during
/// recovery — completed entries are noise).
/// </para>
/// </remarks>
public sealed class FileBasedTransactionLog : ITransactionLog, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Initializes a new instance backed by the given file path. The file
    /// is created if it does not exist.
    /// </summary>
    /// <param name="filePath">The log file's path.</param>
    public FileBasedTransactionLog(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(filePath))
        {
            using var _ = File.Create(filePath);
        }
    }

    /// <summary>
    /// Gets the path of the backing log file.
    /// </summary>
    public string FilePath => _filePath;

    /// <inheritdoc />
    public Task RecordTransactionStartedAsync(
        string transactionId,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionId);

        return AppendAsync(new LogRecord
        {
            TransactionId = transactionId,
            EventType = LogEventType.Started,
            Timestamp = DateTime.UtcNow,
            IsolationLevel = options?.IsolationLevel.ToString(),
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordParticipantEnlistedAsync(
        string transactionId,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionId);
        ArgumentNullException.ThrowIfNull(participantId);

        return AppendAsync(new LogRecord
        {
            TransactionId = transactionId,
            EventType = LogEventType.Enlisted,
            Timestamp = DateTime.UtcNow,
            ParticipantId = participantId,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordParticipantPreparedAsync(
        string transactionId,
        string participantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionId);
        ArgumentNullException.ThrowIfNull(participantId);

        return AppendAsync(new LogRecord
        {
            TransactionId = transactionId,
            EventType = LogEventType.Prepared,
            Timestamp = DateTime.UtcNow,
            ParticipantId = participantId,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordTransactionCommittedAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionId);

        return AppendAsync(new LogRecord
        {
            TransactionId = transactionId,
            EventType = LogEventType.Committed,
            Timestamp = DateTime.UtcNow,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordTransactionRolledBackAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactionId);

        return AppendAsync(new LogRecord
        {
            TransactionId = transactionId,
            EventType = LogEventType.RolledBack,
            Timestamp = DateTime.UtcNow,
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransactionLogEntry>> GetInDoubtTransactionsAsync(
        CancellationToken cancellationToken = default)
    {
        var states = new Dictionary<string, FileLogState>(StringComparer.Ordinal);

        // Replay the log file under the write lock so a recovery scan that
        // races with new writes still sees a coherent snapshot.
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(
                _filePath,
                FileMode.OpenOrCreate,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                LogRecord? record;
                try
                {
                    record = JsonSerializer.Deserialize<LogRecord>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    // Malformed line — skip it. A real impl might log a
                    // warning, but a corrupted log shouldn't break recovery.
                    continue;
                }

                if (record is null)
                {
                    continue;
                }

                if (!states.TryGetValue(record.TransactionId, out var state))
                {
                    state = new FileLogState();
                    states[record.TransactionId] = state;
                }

                ApplyEvent(state, record);
            }
        }
        finally
        {
            _writeLock.Release();
        }

        IReadOnlyList<TransactionLogEntry> result = states
            .Where(kv => kv.Value.State == TransactionLogState.Started)
            .Select(kv => new TransactionLogEntry(
                kv.Key,
                kv.Value.StartedAt,
                kv.Value.Enlisted.ToArray(),
                kv.Value.Prepared.ToArray(),
                kv.Value.State))
            .OrderBy(e => e.StartedAt)
            .ToArray();

        return result;
    }

    private async Task AppendAsync(LogRecord record, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(
                _filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);
            await using var writer = new StreamWriter(stream)
            {
                AutoFlush = false,
            };

            var json = JsonSerializer.Serialize(record, JsonOptions);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void ApplyEvent(FileLogState state, LogRecord record)
    {
        switch (record.EventType)
        {
            case LogEventType.Started:
                state.StartedAt = record.Timestamp;
                state.State = TransactionLogState.Started;
                break;

            case LogEventType.Enlisted:
                if (!string.IsNullOrEmpty(record.ParticipantId))
                {
                    state.Enlisted.Add(record.ParticipantId);
                }
                break;

            case LogEventType.Prepared:
                if (!string.IsNullOrEmpty(record.ParticipantId))
                {
                    state.Prepared.Add(record.ParticipantId);
                }
                break;

            case LogEventType.Committed:
                state.State = TransactionLogState.Committed;
                break;

            case LogEventType.RolledBack:
                state.State = TransactionLogState.RolledBack;
                break;
        }
    }

    private sealed class FileLogState
    {
        public DateTime StartedAt { get; set; }
        public List<string> Enlisted { get; } = new();
        public List<string> Prepared { get; } = new();
        public TransactionLogState State { get; set; } = TransactionLogState.Started;
    }

    private enum LogEventType
    {
        Started,
        Enlisted,
        Prepared,
        Committed,
        RolledBack,
    }

    private sealed class LogRecord
    {
        public string TransactionId { get; set; } = string.Empty;
        public LogEventType EventType { get; set; }
        public DateTime Timestamp { get; set; }
        public string? ParticipantId { get; set; }
        public string? IsolationLevel { get; set; }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeLock.Dispose();
    }
}
