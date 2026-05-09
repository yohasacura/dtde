using Dtde.Abstractions.Exceptions;
using Dtde.Abstractions.Metadata;
using Dtde.Abstractions.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dtde.Core.Transactions;

/// <summary>
/// Coordinates cross-shard transactions using the two-phase commit (2PC) protocol.
/// </summary>
/// <remarks>
/// <para>
/// The coordinator manages the lifecycle of cross-shard transactions, including:
/// </para>
/// <list type="bullet">
/// <item>Transaction creation and configuration</item>
/// <item>Shard enlistment and participant management</item>
/// <item>Two-phase commit protocol execution</item>
/// <item>Transaction recovery for in-doubt transactions</item>
/// </list>
/// <para>
/// For best performance, prefer using the ExecuteInTransactionAsync methods
/// which provide automatic transaction management with proper error handling.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Using automatic transaction management (recommended)
/// await coordinator.ExecuteInTransactionAsync(async transaction =>
/// {
///     var participant1 = await transaction.GetOrCreateParticipantAsync("shard-2024");
///     participant1.Context.Update(entity1);
///
///     var participant2 = await transaction.GetOrCreateParticipantAsync("shard-2025");
///     participant2.Context.Add(entity2);
/// });
///
/// // Manual transaction control
/// await using var transaction = await coordinator.BeginTransactionAsync();
/// try
/// {
///     await transaction.EnlistAsync("shard-2024");
///     // ... perform operations ...
///     await transaction.CommitAsync();
/// }
/// catch
/// {
///     await transaction.RollbackAsync();
///     throw;
/// }
/// </code>
/// </example>
public sealed class CrossShardTransactionCoordinator : ICrossShardTransactionCoordinator
{
    private readonly IShardRegistry _shardRegistry;
    private readonly ShardParticipantFactory _participantFactory;
    private readonly ITransactionLog? _transactionLog;
    private readonly ILogger<CrossShardTransactionCoordinator> _logger;
    private readonly ILogger<CrossShardTransaction> _transactionLogger;
    private readonly AsyncLocal<ICrossShardTransaction?> _currentTransaction = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CrossShardTransactionCoordinator"/> class.
    /// </summary>
    /// <param name="shardRegistry">The shard registry.</param>
    /// <param name="participantFactory">Factory that materialises a per-shard context and starts its local transaction with the requested isolation level.</param>
    /// <param name="logger">The logger for the coordinator.</param>
    /// <param name="transactionLogger">The logger for transactions.</param>
    public CrossShardTransactionCoordinator(
        IShardRegistry shardRegistry,
        ShardParticipantFactory participantFactory,
        ILogger<CrossShardTransactionCoordinator> logger,
        ILogger<CrossShardTransaction> transactionLogger)
        : this(shardRegistry, participantFactory, transactionLog: null, logger, transactionLogger)
    {
    }

    /// <summary>
    /// Initializes a new instance with an <see cref="ITransactionLog"/>.
    /// Lifecycle events are persisted so <see cref="RecoverAsync"/> can drive
    /// in-doubt transactions to a terminal state after a coordinator crash.
    /// </summary>
    /// <param name="shardRegistry">The shard registry.</param>
    /// <param name="participantFactory">Factory that materialises a per-shard context and starts its local transaction with the requested isolation level.</param>
    /// <param name="transactionLog">Optional durable log for crash recovery.</param>
    /// <param name="logger">The logger for the coordinator.</param>
    /// <param name="transactionLogger">The logger for transactions.</param>
    public CrossShardTransactionCoordinator(
        IShardRegistry shardRegistry,
        ShardParticipantFactory participantFactory,
        ITransactionLog? transactionLog,
        ILogger<CrossShardTransactionCoordinator> logger,
        ILogger<CrossShardTransaction> transactionLogger)
    {
        _shardRegistry = shardRegistry ?? throw new ArgumentNullException(nameof(shardRegistry));
        _participantFactory = participantFactory ?? throw new ArgumentNullException(nameof(participantFactory));
        _transactionLog = transactionLog;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _transactionLogger = transactionLogger ?? throw new ArgumentNullException(nameof(transactionLogger));
    }

    /// <summary>
    /// Gets the transaction log this coordinator is using, if any.
    /// </summary>
    public ITransactionLog? TransactionLog => _transactionLog;

    /// <inheritdoc />
    public ICrossShardTransaction? CurrentTransaction => _currentTransaction.Value;

    /// <inheritdoc />
    public Task<ICrossShardTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        return BeginTransactionAsync(CrossShardTransactionOptions.Default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ICrossShardTransaction> BeginTransactionAsync(
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (_currentTransaction.Value is not null)
        {
            throw new InvalidOperationException(
                "A cross-shard transaction is already active in the current context. " +
                "Nested transactions are not supported.");
        }

        var transactionId = GenerateTransactionId(options.TransactionName);

        TransactionLogMessages.BeginningTransactionWithOptions(_logger, transactionId, options.IsolationLevel);

        var transaction = new CrossShardTransaction(
            transactionId,
            options,
            _shardRegistry,
            _participantFactory,
            _transactionLog,
            _transactionLogger);

        // Set the ambient transaction in the caller's synchronous frame —
        // AsyncLocal mutations made *after* an await don't flow back to the
        // caller, so the assignment must happen before any async work below.
        _currentTransaction.Value = transaction;

        // Without a log, we're done synchronously.
        return _transactionLog is null
            ? Task.FromResult<ICrossShardTransaction>(transaction)
            : RecordStartAndReturnAsync(transaction, transactionId, options, cancellationToken);
    }

    private async Task<ICrossShardTransaction> RecordStartAndReturnAsync(
        CrossShardTransaction transaction,
        string transactionId,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            await _transactionLog!
                .RecordTransactionStartedAsync(transactionId, options, cancellationToken)
                .ConfigureAwait(false);
            return transaction;
        }
        catch
        {
            // Log write failed; tear the transaction down so the caller's
            // happy path doesn't proceed with an unrecoverable transaction.
            // The caller observes the original exception; the transaction
            // we created and registered is disposed here.
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public Task ExecuteInTransactionAsync(
        Func<ICrossShardTransaction, Task> action,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInTransactionAsync(action, CrossShardTransactionOptions.Default, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ExecuteInTransactionAsync(
        Func<ICrossShardTransaction, Task> action,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);

        var transaction = await BeginTransactionAsync(options, cancellationToken)
            .ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                await ExecuteWithRetryAsync(
                    async () =>
                    {
                        await action(transaction).ConfigureAwait(false);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    },
                    options,
                    cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Must catch all exceptions to ensure proper rollback
            catch (Exception ex)
#pragma warning restore CA1031
            {
                TransactionLogMessages.TransactionFailed(_logger, ex, transaction.TransactionId);

                try
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Must catch all exceptions during rollback to prevent masking original exception
                catch (Exception rollbackEx)
#pragma warning restore CA1031
                {
                    TransactionLogMessages.TransactionRollbackError(_logger, rollbackEx, transaction.TransactionId);
                }

                throw;
            }
            finally
            {
                _currentTransaction.Value = null;
            }
        }
    }

    /// <inheritdoc />
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<ICrossShardTransaction, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInTransactionAsync(action, CrossShardTransactionOptions.Default, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<ICrossShardTransaction, Task<TResult>> action,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(options);

        var transaction = await BeginTransactionAsync(options, cancellationToken)
            .ConfigureAwait(false);

        await using (transaction.ConfigureAwait(false))
        {
            try
            {
                var result = default(TResult);

                await ExecuteWithRetryAsync(
                    async () =>
                    {
                        result = await action(transaction).ConfigureAwait(false);
                        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    },
                    options,
                    cancellationToken).ConfigureAwait(false);

                return result!;
            }
#pragma warning disable CA1031 // Must catch all exceptions to ensure proper rollback
            catch (Exception ex)
#pragma warning restore CA1031
            {
                TransactionLogMessages.TransactionFailed(_logger, ex, transaction.TransactionId);

                try
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Must catch all exceptions during rollback to prevent masking original exception
                catch (Exception rollbackEx)
#pragma warning restore CA1031
                {
                    TransactionLogMessages.TransactionRollbackError(_logger, rollbackEx, transaction.TransactionId);
                }

                throw;
            }
            finally
            {
                _currentTransaction.Value = null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<int> RecoverAsync(CancellationToken cancellationToken = default)
    {
        // Without a durable log there's nothing to recover from.
        if (_transactionLog is null)
        {
            TransactionLogMessages.NoInDoubtTransactions(_logger);
            return 0;
        }

        var inDoubt = await _transactionLog
            .GetInDoubtTransactionsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (inDoubt.Count == 0)
        {
            TransactionLogMessages.NoInDoubtTransactions(_logger);
            return 0;
        }

        TransactionLogMessages.RecoveringInDoubtTransactions(_logger, inDoubt.Count);

        var resolved = 0;
        foreach (var entry in inDoubt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The classic 2PC recovery rule: if every enlisted participant
            // was logged as prepared, the global decision was "commit" — even
            // if the coordinator died before the commit phase finished. The
            // resolution is recorded so the log no longer flags this
            // transaction as in-doubt.
            //
            // Otherwise (some participants never prepared), the transaction
            // must be rolled back. The participants' local transactions are
            // already gone with the crashed process, so this is just the
            // logical decision; the actual cleanup happens at the storage
            // layer (most relational providers automatically abort orphaned
            // transactions when the connection drops).
            if (entry.AllParticipantsPrepared)
            {
                await _transactionLog
                    .RecordTransactionCommittedAsync(entry.TransactionId, cancellationToken)
                    .ConfigureAwait(false);
                TransactionLogMessages.RecoveredCommittedTransaction(
                    _logger,
                    entry.TransactionId,
                    entry.EnlistedParticipants.Count);
            }
            else
            {
                await _transactionLog
                    .RecordTransactionRolledBackAsync(entry.TransactionId, cancellationToken)
                    .ConfigureAwait(false);
                TransactionLogMessages.RecoveredRolledBackTransaction(
                    _logger,
                    entry.TransactionId,
                    entry.EnlistedParticipants.Count,
                    entry.PreparedParticipants.Count);
            }

            resolved++;
        }

        return resolved;
    }

    private async Task ExecuteWithRetryAsync(
        Func<Task> action,
        CrossShardTransactionOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.EnableRetry)
        {
            await action().ConfigureAwait(false);
            return;
        }

        var attempts = 0;
        var delay = options.RetryDelay;

        while (true)
        {
            attempts++;

            try
            {
                await action().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsTransientError(ex) && attempts < options.MaxRetryAttempts)
            {
                TransactionLogMessages.RetryingTransaction(_logger, ex, attempts, options.MaxRetryAttempts, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                if (options.UseExponentialBackoff)
                {
                    delay = TimeSpan.FromMilliseconds(Math.Min(
                        delay.TotalMilliseconds * 2,
                        options.MaxRetryDelay.TotalMilliseconds));
                }
            }
        }
    }

    private static bool IsTransientError(Exception ex)
    {
        // Check for transient database errors that can be retried
        return ex is TimeoutException
            || ex is OperationCanceledException
            || (ex.InnerException is not null && IsTransientError(ex.InnerException))
            || ex.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateTransactionId(string? transactionName)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var uniqueId = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..8];

        return string.IsNullOrEmpty(transactionName)
            ? $"XS-{timestamp}-{uniqueId}"
            : $"XS-{transactionName}-{timestamp}-{uniqueId}";
    }
}
